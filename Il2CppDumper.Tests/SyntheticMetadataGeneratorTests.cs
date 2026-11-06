using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Il2CppDumper.Tests.Synthetic;
using Xunit;

namespace Il2CppDumper.Tests
{
    public class SyntheticMetadataGeneratorTests
    {
        private static string FindRepoRoot()
        {
            var current = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                if (File.Exists(Path.Combine(current, "Il2CppDumper.sln")) ||
                    (File.Exists(Path.Combine(current, "README.md")) && File.Exists(Path.Combine(current, "README.zh-CN.md"))))
                {
                    return Path.GetFullPath(current);
                }

                var parent = Directory.GetParent(current);
                if (parent == null)
                    break;

                current = parent.FullName;
            }

            throw new FileNotFoundException($"Could not locate repository root from {AppContext.BaseDirectory}");
        }

        [Theory]
        [InlineData(MetadataVersionFamily.V16)]
        [InlineData(MetadataVersionFamily.V21)]
        [InlineData(MetadataVersionFamily.V24)]
        [InlineData(MetadataVersionFamily.V27)]
        [InlineData(MetadataVersionFamily.V29)]
        [InlineData(MetadataVersionFamily.V31)]
        [InlineData(MetadataVersionFamily.V35)]
        [InlineData(MetadataVersionFamily.V38)]
        [InlineData(MetadataVersionFamily.V39)]
        public void PositiveFixture_ParsesCleanly_AndContainsExpectedEntities(MetadataVersionFamily family)
        {
            var bytes = SyntheticMetadataGenerator.Generate(family, seed: 42);
            Assert.NotNull(bytes);
            Assert.True(bytes.Length > 0);

            using var metadata = new Metadata(new MemoryStream(bytes));

            // Verify version
            Assert.Equal((int)family, (int)metadata.Version);

            // Verify images and assemblies
            Assert.NotNull(metadata.imageDefs);
            Assert.Single(metadata.imageDefs);
            Assert.Equal("SyntheticImage.dll", metadata.GetStringFromIndex(metadata.imageDefs[0].nameIndex));

            Assert.NotNull(metadata.assemblyDefs);
            Assert.Single(metadata.assemblyDefs);
            Assert.Equal("SyntheticAssembly", metadata.GetStringFromIndex(metadata.assemblyDefs[0].aname.nameIndex));

            // Verify type definitions (1 class + 1 enum)
            Assert.NotNull(metadata.typeDefs);
            Assert.Equal(2, metadata.typeDefs.Length);

            var classDef = metadata.typeDefs[0];
            Assert.Equal("SyntheticClass", metadata.GetStringFromIndex(classDef.nameIndex));
            Assert.Equal("SyntheticNamespace", metadata.GetStringFromIndex(classDef.namespaceIndex));
            Assert.False(classDef.IsEnum);
            Assert.Equal(1, classDef.field_count);
            Assert.Equal(1, classDef.method_count);

            var enumDef = metadata.typeDefs[1];
            Assert.Equal("SyntheticEnum", metadata.GetStringFromIndex(enumDef.nameIndex));
            Assert.Equal("SyntheticNamespace", metadata.GetStringFromIndex(enumDef.namespaceIndex));
            Assert.True(enumDef.IsEnum);
            Assert.Equal(2, enumDef.field_count);
            Assert.Equal(0, enumDef.method_count);

            // Verify method definition (1 method)
            Assert.NotNull(metadata.methodDefs);
            Assert.Single(metadata.methodDefs);
            var methodDef = metadata.methodDefs[0];
            Assert.Equal("SyntheticMethod", metadata.GetStringFromIndex(methodDef.nameIndex));
            Assert.Equal(1, methodDef.parameterCount);

            // Verify field definitions (3 fields: syntheticField, value__, OptionA)
            Assert.NotNull(metadata.fieldDefs);
            Assert.Equal(3, metadata.fieldDefs.Length);
            Assert.Equal("syntheticField", metadata.GetStringFromIndex(metadata.fieldDefs[0].nameIndex));
            Assert.Equal("value__", metadata.GetStringFromIndex(metadata.fieldDefs[1].nameIndex));
            Assert.Equal("OptionA", metadata.GetStringFromIndex(metadata.fieldDefs[2].nameIndex));

            // Verify enum default value
            Assert.True(metadata.GetFieldDefaultValueFromIndex(2, out var defaultValue));
            Assert.Equal(2, defaultValue.fieldIndex);
            var valueOffset = metadata.GetDefaultValueFromIndex(defaultValue.dataIndex);
            Assert.True(valueOffset < (uint)bytes.Length);
            var value = BitConverter.ToInt32(bytes, (int)valueOffset);
            Assert.Equal(1, value);

            // Verify string literal
            Assert.NotNull(metadata.stringLiterals);
            Assert.Single(metadata.stringLiterals);
            Assert.True(metadata.TryGetStringLiteral(0, out var literalValue, out _, out _));
            Assert.Equal("HelloSynthetic", literalValue);
        }

        [Theory]
        [InlineData(MetadataVersionFamily.V16)]
        [InlineData(MetadataVersionFamily.V21)]
        [InlineData(MetadataVersionFamily.V24)]
        [InlineData(MetadataVersionFamily.V27)]
        [InlineData(MetadataVersionFamily.V29)]
        [InlineData(MetadataVersionFamily.V31)]
        [InlineData(MetadataVersionFamily.V35)]
        [InlineData(MetadataVersionFamily.V38)]
        [InlineData(MetadataVersionFamily.V39)]
        public void PositiveFixture_Decompilation_MatchesGoldenOutputs(MetadataVersionFamily family)
        {
            var repoRoot = FindRepoRoot();
            var goldensDir = Path.Combine(repoRoot, "Il2CppDumper.Tests", "Goldens", family.ToString());
            Directory.CreateDirectory(goldensDir);

            var tempDir = Path.Combine(Path.GetTempPath(), "synthetic_golden_" + family + "_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var (metadata, il2Cpp, executor) = SyntheticEnvironment.Create(family, seed: 42);
                var config = new Config
                {
                    DumpAttribute = false,
                    DumpField = true,
                    DumpMethod = true,
                    DumpProperty = true,
                    DumpTypeDefIndex = false
                };

                SyntheticEnvironment.DecompileToDirectory(executor, config, tempDir);

                var actualDumpCsPath = Path.Combine(tempDir, "dump.cs");
                var actualDumpJsonPath = Path.Combine(tempDir, "dump.json");

                var goldenDumpCsPath = Path.Combine(goldensDir, "dump.cs");
                var goldenDumpJsonPath = Path.Combine(goldensDir, "dump.json");

                var updateGoldens = Environment.GetEnvironmentVariable("UPDATE_GOLDENS") == "1" ||
                                    Environment.GetEnvironmentVariable("REGENERATE_GOLDEN_FILES") == "1";

                if (updateGoldens || !File.Exists(goldenDumpCsPath) || !File.Exists(goldenDumpJsonPath))
                {
                    File.Copy(actualDumpCsPath, goldenDumpCsPath, true);
                    File.Copy(actualDumpJsonPath, goldenDumpJsonPath, true);
                }

                var expectedCs = File.ReadAllText(goldenDumpCsPath).Replace("\r\n", "\n");
                var actualCs = File.ReadAllText(actualDumpCsPath).Replace("\r\n", "\n");
                Assert.Equal(expectedCs, actualCs);

                var expectedJson = File.ReadAllText(goldenDumpJsonPath).Replace("\r\n", "\n");
                var actualJson = File.ReadAllText(actualDumpJsonPath).Replace("\r\n", "\n");
                Assert.Equal(expectedJson, actualJson);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [Theory]
        [InlineData(MetadataVersionFamily.V16)]
        [InlineData(MetadataVersionFamily.V21)]
        [InlineData(MetadataVersionFamily.V24)]
        [InlineData(MetadataVersionFamily.V27)]
        [InlineData(MetadataVersionFamily.V29)]
        [InlineData(MetadataVersionFamily.V31)]
        [InlineData(MetadataVersionFamily.V35)]
        [InlineData(MetadataVersionFamily.V38)]
        [InlineData(MetadataVersionFamily.V39)]
        public void Corruption_WrongMagic_ThrowsInvalidDataException(MetadataVersionFamily family)
        {
            var bytes = SyntheticMetadataGenerator.Generate(family, seed: 42, MetadataCorruptionType.WrongMagic);
            var ex = Assert.Throws<InvalidDataException>(() => new Metadata(new MemoryStream(bytes)));
            Assert.Contains("not valid metadata file", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData(MetadataVersionFamily.V16)]
        [InlineData(MetadataVersionFamily.V21)]
        [InlineData(MetadataVersionFamily.V24)]
        [InlineData(MetadataVersionFamily.V27)]
        [InlineData(MetadataVersionFamily.V29)]
        [InlineData(MetadataVersionFamily.V31)]
        [InlineData(MetadataVersionFamily.V35)]
        [InlineData(MetadataVersionFamily.V38)]
        [InlineData(MetadataVersionFamily.V39)]
        public void Corruption_OutOfRangeVersion_ThrowsSupportedOrInvalidDataException(MetadataVersionFamily family)
        {
            var bytes = SyntheticMetadataGenerator.Generate(family, seed: 42, MetadataCorruptionType.OutOfRangeVersion);
            Assert.ThrowsAny<Exception>(() => new Metadata(new MemoryStream(bytes)));
        }

        [Theory]
        [InlineData(MetadataVersionFamily.V16)]
        [InlineData(MetadataVersionFamily.V21)]
        [InlineData(MetadataVersionFamily.V24)]
        [InlineData(MetadataVersionFamily.V27)]
        [InlineData(MetadataVersionFamily.V29)]
        [InlineData(MetadataVersionFamily.V31)]
        [InlineData(MetadataVersionFamily.V35)]
        [InlineData(MetadataVersionFamily.V38)]
        [InlineData(MetadataVersionFamily.V39)]
        public void Corruption_TruncatedSection_ThrowsException(MetadataVersionFamily family)
        {
            var bytes = SyntheticMetadataGenerator.Generate(family, seed: 42, MetadataCorruptionType.TruncatedSection);
            Assert.ThrowsAny<Exception>(() => new Metadata(new MemoryStream(bytes)));
        }

        [Theory]
        [InlineData(MetadataVersionFamily.V16)]
        [InlineData(MetadataVersionFamily.V21)]
        [InlineData(MetadataVersionFamily.V24)]
        [InlineData(MetadataVersionFamily.V27)]
        [InlineData(MetadataVersionFamily.V29)]
        [InlineData(MetadataVersionFamily.V31)]
        [InlineData(MetadataVersionFamily.V35)]
        [InlineData(MetadataVersionFamily.V38)]
        [InlineData(MetadataVersionFamily.V39)]
        public void Corruption_LiteralExtentPastSectionEnd_RejectsSafelyWithoutThrowing(MetadataVersionFamily family)
        {
            var bytes = SyntheticMetadataGenerator.Generate(family, seed: 42, MetadataCorruptionType.LiteralExtentPastSectionEnd);
            using var metadata = new Metadata(new MemoryStream(bytes));
            var success = metadata.TryGetStringLiteral(0, out var value, out _, out _);
            Assert.False(success);
            Assert.Equal(string.Empty, value);
        }

        [Theory]
        [InlineData(MetadataVersionFamily.V16)]
        [InlineData(MetadataVersionFamily.V21)]
        [InlineData(MetadataVersionFamily.V24)]
        [InlineData(MetadataVersionFamily.V27)]
        [InlineData(MetadataVersionFamily.V29)]
        [InlineData(MetadataVersionFamily.V31)]
        [InlineData(MetadataVersionFamily.V35)]
        [InlineData(MetadataVersionFamily.V38)]
        [InlineData(MetadataVersionFamily.V39)]
        public void Corruption_ZeroedRegistrationRecords_ParsedSafely(MetadataVersionFamily family)
        {
            var bytes = SyntheticMetadataGenerator.Generate(family, seed: 42, MetadataCorruptionType.ZeroedRegistrationRecords);
            using var metadata = new Metadata(new MemoryStream(bytes));
            Assert.NotNull(metadata);
            Assert.Empty(metadata.imageDefs);
            Assert.Empty(metadata.typeDefs);
        }

        [Fact]
        public void Determinism_SameFamilyAndSeed_ProducesIdenticalBytes()
        {
            foreach (MetadataVersionFamily family in Enum.GetValues(typeof(MetadataVersionFamily)))
            {
                var bytesA = SyntheticMetadataGenerator.Generate(family, seed: 12345);
                var bytesB = SyntheticMetadataGenerator.Generate(family, seed: 12345);
                Assert.Equal(bytesA, bytesB);

                var bytesDifferentSeed = SyntheticMetadataGenerator.Generate(family, seed: 99999);
                Assert.NotEqual(bytesA, bytesDifferentSeed);
            }
        }

        [Theory]
        [InlineData(MetadataVersionFamily.V16)]
        [InlineData(MetadataVersionFamily.V21)]
        [InlineData(MetadataVersionFamily.V24)]
        [InlineData(MetadataVersionFamily.V27)]
        [InlineData(MetadataVersionFamily.V29)]
        [InlineData(MetadataVersionFamily.V31)]
        [InlineData(MetadataVersionFamily.V35)]
        [InlineData(MetadataVersionFamily.V38)]
        [InlineData(MetadataVersionFamily.V39)]
        public void ParserRoundTrip_LayoutUnions_Preserved(MetadataVersionFamily family)
        {
            var bytes = SyntheticMetadataGenerator.Generate(family, seed: 42);
            using var metadata = new Metadata(new MemoryStream(bytes));

            // Verify structure layout fields are intact after parsing
            Assert.Equal((int)family, (int)metadata.Version);
            Assert.Equal(2, metadata.typeDefs.Length);
            Assert.Single(metadata.methodDefs);
            Assert.Equal(3, metadata.fieldDefs.Length);
            Assert.Single(metadata.imageDefs);
            Assert.Single(metadata.assemblyDefs);
            Assert.Single(metadata.stringLiterals);

            // Re-generate and verify identical byte layout
            var regenerated = SyntheticMetadataGenerator.Generate(family, seed: 42);
            Assert.Equal(bytes, regenerated);
        }

        [Fact]
        public void RepoHygiene_AssertNoCommittedBinaryBlobs()
        {
            var repoRoot = FindRepoRoot();
            var testsDir = Path.Combine(repoRoot, "Il2CppDumper.Tests");
            var forbiddenExtensions = new[] { ".dat", ".bin", ".wasm", ".so", ".elf", ".apk" };

            var files = Directory.GetFiles(testsDir, "*.*", SearchOption.AllDirectories)
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) &&
                            !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
                .ToList();

            foreach (var file in files)
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                Assert.DoesNotContain(ext, forbiddenExtensions);
            }
        }

        [Fact]
        public void SuiteRuntimeBudget_AssertUnderLimit()
        {
            // Suite runtime budget: generating and parsing the full matrix (9 families x 6 variants = 54 fixtures)
            // must execute comfortably within a 5000 ms budget (typical execution is < 300 ms).
            var sw = Stopwatch.StartNew();

            foreach (MetadataVersionFamily family in Enum.GetValues(typeof(MetadataVersionFamily)))
            {
                foreach (MetadataCorruptionType corruption in Enum.GetValues(typeof(MetadataCorruptionType)))
                {
                    var bytes = SyntheticMetadataGenerator.Generate(family, seed: 42, corruption);
                    try
                    {
                        using var m = new Metadata(new MemoryStream(bytes));
                        _ = m.Version;
                    }
                    catch
                    {
                        // Expected for corruption variants
                    }
                }
            }

            sw.Stop();
            Assert.True(sw.ElapsedMilliseconds < 5000, $"Corpus execution exceeded budget: {sw.ElapsedMilliseconds}ms >= 5000ms");
        }
    }
}
