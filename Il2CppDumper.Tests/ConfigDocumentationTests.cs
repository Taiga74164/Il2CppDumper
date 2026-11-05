using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Xunit;

namespace Il2CppDumper.Tests
{
    public class ConfigDocumentationTests
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

        [Fact]
        public void Readme_English_DocumentsGenerateStruct_AndDoesNotDocumentGenerateScript()
        {
            var repoRoot = FindRepoRoot();
            var readmePath = Path.Combine(repoRoot, "README.md");
            var content = File.ReadAllText(readmePath);

            Assert.DoesNotContain("GenerateScript", content);
            Assert.Contains("`GenerateStruct`", content);
            Assert.Contains("`script.json`", content);
            Assert.Contains("`stringliteral.json`", content);
            Assert.Contains("`il2cpp.h`", content);
        }

        [Fact]
        public void Readme_Chinese_DocumentsGenerateStruct_AndDoesNotDocumentGenerateScript()
        {
            var repoRoot = FindRepoRoot();
            var readmePath = Path.Combine(repoRoot, "README.zh-CN.md");
            var content = File.ReadAllText(readmePath);

            Assert.DoesNotContain("GenerateScript", content);
            Assert.Contains("`GenerateStruct`", content);
            Assert.Contains("`script.json`", content);
            Assert.Contains("`stringliteral.json`", content);
            Assert.Contains("`il2cpp.h`", content);
        }

        [Fact]
        public void Readme_English_DocumentsDumpPropertyAndDumpAttributeDefaults()
        {
            var repoRoot = FindRepoRoot();
            var readmePath = Path.Combine(repoRoot, "README.md");
            var content = File.ReadAllText(readmePath);

            Assert.Contains("`DumpProperty` and `DumpAttribute` default to `true`", content);
        }

        [Fact]
        public void Readme_Chinese_DocumentsDumpPropertyAndDumpAttributeDefaults()
        {
            var repoRoot = FindRepoRoot();
            var readmePath = Path.Combine(repoRoot, "README.zh-CN.md");
            var content = File.ReadAllText(readmePath);

            Assert.Contains("`DumpProperty` \u548c `DumpAttribute` \u9ed8\u8ba4\u503c\u4e3a `true`", content);
        }

        [Fact]
        public void Config_DeserializingGenerateStructFalse_DisablesGenerationGate()
        {
            // Verifies the exact scenario from Issue #11:
            // Documenting GenerateStruct ensures setting it to false in JSON successfully disables the gate.
            const string json = "{\"GenerateStruct\": false}";
            var config = JsonSerializer.Deserialize<Config>(json);

            Assert.NotNull(config);
            Assert.False(config.GenerateStruct);
        }

        [Fact]
        public void Config_DeserializingGenerateStructTrue_EnablesGenerationGate()
        {
            const string json = "{\"GenerateStruct\": true}";
            var config = JsonSerializer.Deserialize<Config>(json);

            Assert.NotNull(config);
            Assert.True(config.GenerateStruct);
        }

        [Fact]
        public void Config_DefaultConstructor_HasDumpPropertyAndDumpAttributeEnabledByDefault()
        {
            var config = new Config();

            Assert.True(config.DumpProperty);
            Assert.True(config.DumpAttribute);
        }

        [Fact]
        public void Config_DeserializingEmptyJson_ProducesTrueForDumpPropertyAndDumpAttribute()
        {
            var config = JsonSerializer.Deserialize<Config>("{}");

            Assert.NotNull(config);
            Assert.True(config.DumpProperty);
            Assert.True(config.DumpAttribute);
        }

        [Fact]
        public void Config_ShippedConfigJson_AlignsWithCodeDefaultsForPropertiesAndAttributes()
        {
            var repoRoot = FindRepoRoot();
            var configJsonPath = Path.Combine(repoRoot, "Il2CppDumper", "config.json");
            Assert.True(File.Exists(configJsonPath), $"Expected config.json at {configJsonPath}");

            var json = File.ReadAllText(configJsonPath);
            var shippedConfig = JsonSerializer.Deserialize<Config>(json);
            var codeDefaultConfig = new Config();

            Assert.NotNull(shippedConfig);
            Assert.True(shippedConfig.DumpProperty);
            Assert.True(shippedConfig.DumpAttribute);
            Assert.Equal(codeDefaultConfig.DumpProperty, shippedConfig.DumpProperty);
            Assert.Equal(codeDefaultConfig.DumpAttribute, shippedConfig.DumpAttribute);
        }

        [Fact]
        public void Config_ExplicitFalseJson_AllowsDisablingDumpPropertyAndDumpAttribute()
        {
            const string json = "{\"DumpProperty\": false, \"DumpAttribute\": false}";
            var config = JsonSerializer.Deserialize<Config>(json);

            Assert.NotNull(config);
            Assert.False(config.DumpProperty);
            Assert.False(config.DumpAttribute);
        }

        [Fact]
        public void Config_AllOptionsDocumentedInEnglishReadme_MatchConfigProperties()
        {
            var expectedOptions = new[]
            {
                "DumpMethod",
                "DumpField",
                "DumpProperty",
                "DumpAttribute",
                "DumpFieldOffset",
                "DumpMethodOffset",
                "DumpTypeDefIndex",
                "GenerateDummyDll",
                "GenerateStruct",
                "DummyDllAddToken",
                "RequireAnyKey",
                "ForceIl2CppVersion",
                "ForceVersion",
                "ForceDump",
                "NoRedirectedPointer"
            };

            var repoRoot = FindRepoRoot();
            var content = File.ReadAllText(Path.Combine(repoRoot, "README.md"));

            foreach (var option in expectedOptions)
            {
                Assert.Contains($"`{option}`", content);
                var prop = typeof(Config).GetProperty(option, BindingFlags.Public | BindingFlags.Instance);
                Assert.True(prop != null, $"Property {option} documented in README.md must exist on Config class.");
            }
        }

        [Fact]
        public void Config_AllOptionsDocumentedInChineseReadme_MatchConfigProperties()
        {
            var expectedOptions = new[]
            {
                "DumpMethod",
                "DumpField",
                "DumpProperty",
                "DumpAttribute",
                "DumpFieldOffset",
                "DumpMethodOffset",
                "DumpTypeDefIndex",
                "GenerateDummyDll",
                "GenerateStruct",
                "DummyDllAddToken",
                "RequireAnyKey",
                "ForceIl2CppVersion",
                "ForceVersion",
                "ForceDump",
                "NoRedirectedPointer"
            };

            var repoRoot = FindRepoRoot();
            var content = File.ReadAllText(Path.Combine(repoRoot, "README.zh-CN.md"));

            foreach (var option in expectedOptions)
            {
                Assert.Contains($"`{option}`", content);
                var prop = typeof(Config).GetProperty(option, BindingFlags.Public | BindingFlags.Instance);
                Assert.True(prop != null, $"Property {option} documented in README.zh-CN.md must exist on Config class.");
            }
        }

        [Fact]
        public void Readme_English_DocumentsDumpMetadataJson_WithPortugueseSection()
        {
            var repoRoot = FindRepoRoot();
            var readmePath = Path.Combine(repoRoot, "README.md");
            var content = File.ReadAllText(readmePath);

            Assert.Contains("#### dump-metadata.json", content);
            Assert.Contains("`sha256`", content);
            Assert.Contains("`metadataVersion`", content);
            Assert.Contains("`generatedAtUtc`", content);
            Assert.Contains("**Português:**", content);
            Assert.Contains("proveniência", content);
        }

        [Fact]
        public void Readme_Chinese_DocumentsDumpMetadataJson_WithPortugueseSection()
        {
            var repoRoot = FindRepoRoot();
            var readmePath = Path.Combine(repoRoot, "README.zh-CN.md");
            var content = File.ReadAllText(readmePath);

            Assert.Contains("#### dump-metadata.json", content);
            Assert.Contains("`sha256`", content);
            Assert.Contains("`metadataVersion`", content);
            Assert.Contains("`generatedAtUtc`", content);
            Assert.Contains("**Português:**", content);
            Assert.Contains("proveniência", content);
        }
    }
}
