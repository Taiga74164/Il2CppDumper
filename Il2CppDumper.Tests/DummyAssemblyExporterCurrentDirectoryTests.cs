using System;
using System.IO;
using System.Text;
using Xunit;

namespace Il2CppDumper.Tests
{
    public class DummyAssemblyExporterCurrentDirectoryTests
    {
        private static string FindRepoRoot()
        {
            var current = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                if (File.Exists(Path.Combine(current, "README.md")) &&
                    File.Exists(Path.Combine(current, "README.zh-CN.md")))
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

        private static Metadata CreateEmptyMetadata()
        {
            var bytes = new byte[4096];
            BitConverter.GetBytes(0xFAB11BAFu).CopyTo(bytes, 0);
            BitConverter.GetBytes(29).CopyTo(bytes, 4);
            var m = new Metadata(new MemoryStream(bytes));
            m.header.stringOffset = 1024;
            m.header.stringSize = 2048;
            return m;
        }

        private static uint AddString(Metadata m, string value, ref uint pos)
        {
            var result = pos;
            var bytes = Encoding.UTF8.GetBytes(value + "\0");
            m.Position = m.header.stringOffset + pos;
            m.Writer.Write(bytes);
            pos += (uint)bytes.Length;
            return result;
        }

        private static Il2CppExecutor CreateMinimalExecutor()
        {
            var m = CreateEmptyMetadata();
            uint pos = 0;
            AddString(m, "", ref pos);
            var imageName = AddString(m, "Repro.dll", ref pos);
            var typeName = AddString(m, "Container", ref pos);

            m.imageDefs = new[]
            {
                new Il2CppImageDefinition { nameIndex = imageName, assemblyIndex = -1, typeCount = 1 }
            };
            m.typeDefs = new[]
            {
                new Il2CppTypeDefinition
                {
                    nameIndex = typeName,
                    declaringTypeIndex = -1,
                    parentIndex = -1,
                    genericContainerIndex = -1,
                    method_count = 0,
                    property_count = 0,
                    flags = 1
                }
            };
            m.methodDefs = Array.Empty<Il2CppMethodDefinition>();
            m.parameterDefs = Array.Empty<Il2CppParameterDefinition>();
            m.propertyDefs = Array.Empty<Il2CppPropertyDefinition>();

            var il = new WebAssemblyMemory(new MemoryStream(new byte[64]), 0);
            il.Version = 16;
            il.methodPointers = Array.Empty<ulong>();
            il.types = new[]
            {
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_VOID }
            };

            return new Il2CppExecutor(m, il);
        }

        [Fact]
        public void Acceptance_NoSetCurrentDirectoryCallRemainsInDummyAssemblyExporter()
        {
            var repoRoot = FindRepoRoot();
            var exporterSourcePath = Path.Combine(repoRoot, "Il2CppDumper", "Outputs", "DummyAssemblyExporter.cs");
            Assert.True(File.Exists(exporterSourcePath), $"Source file missing: {exporterSourcePath}");

            var sourceCode = File.ReadAllText(exporterSourcePath);
            Assert.DoesNotContain("SetCurrentDirectory", sourceCode);
        }

        [Fact]
        public void Acceptance_ExportTwiceInSameProcess_OutputsLandCorrectly_AndCurrentDirectoryUnchanged()
        {
            var initialCwd = Environment.CurrentDirectory;
            var tempBase = Path.Combine(Path.GetTempPath(), "il2cpp-issue-26-double-export-" + Guid.NewGuid());
            Directory.CreateDirectory(tempBase);

            var dir1 = Path.Combine(tempBase, "out1");
            var dir2 = Path.Combine(tempBase, "out2");

            try
            {
                var executor = CreateMinimalExecutor();

                // First export
                DummyAssemblyExporter.Export(executor, dir1, false);
                Assert.Equal(initialCwd, Environment.CurrentDirectory);

                var expectedDll1 = Path.Combine(dir1, "DummyDll", "Repro.dll");
                Assert.True(File.Exists(expectedDll1), $"Expected output DLL in dir1: {expectedDll1}");
                Assert.True(new FileInfo(expectedDll1).Length > 0);

                // Second export in same process
                DummyAssemblyExporter.Export(executor, dir2, false);
                Assert.Equal(initialCwd, Environment.CurrentDirectory);

                var expectedDll2 = Path.Combine(dir2, "DummyDll", "Repro.dll");
                Assert.True(File.Exists(expectedDll2), $"Expected output DLL in dir2: {expectedDll2}");
                Assert.True(new FileInfo(expectedDll2).Length > 0);
            }
            finally
            {
                Environment.CurrentDirectory = initialCwd;
                if (Directory.Exists(tempBase))
                {
                    try { Directory.Delete(tempBase, true); } catch { /* best effort */ }
                }
            }
        }

        [Fact]
        public void Acceptance_ExportWithRelativeOutputPaths_DoesNotNestOutputOrCorruptBaseDirectory()
        {
            var originalCwd = Environment.CurrentDirectory;
            var tempBase = Path.Combine(Path.GetTempPath(), "il2cpp-issue-26-relative-export-" + Guid.NewGuid());
            Directory.CreateDirectory(tempBase);

            try
            {
                Environment.CurrentDirectory = tempBase;
                var executor = CreateMinimalExecutor();

                const string relDir1 = "rel_out1";
                const string relDir2 = "rel_out2";

                // First relative export
                DummyAssemblyExporter.Export(executor, relDir1, false);
                Assert.Equal(tempBase, Environment.CurrentDirectory);

                var expectedDll1 = Path.Combine(tempBase, relDir1, "DummyDll", "Repro.dll");
                Assert.True(File.Exists(expectedDll1), $"Expected DLL at {expectedDll1}");

                // Second relative export must resolve against initial tempBase, NOT against rel_out1\DummyDll
                DummyAssemblyExporter.Export(executor, relDir2, false);
                Assert.Equal(tempBase, Environment.CurrentDirectory);

                var expectedDll2 = Path.Combine(tempBase, relDir2, "DummyDll", "Repro.dll");
                Assert.True(File.Exists(expectedDll2), $"Expected DLL at {expectedDll2}");

                // Prior buggy implementation nested rel_out2 inside rel_out1\DummyDll
                var nestedBogusPath = Path.Combine(tempBase, relDir1, "DummyDll", relDir2);
                Assert.False(Directory.Exists(nestedBogusPath), $"relDir2 should not be nested in relDir1: {nestedBogusPath}");
            }
            finally
            {
                Environment.CurrentDirectory = originalCwd;
                if (Directory.Exists(tempBase))
                {
                    try { Directory.Delete(tempBase, true); } catch { /* best effort */ }
                }
            }
        }
    }
}
