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
            Assert.Contains("`stringliteral-indexed.json`", content);
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
            Assert.Contains("`stringliteral-indexed.json`", content);
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
        public void Readme_English_DocumentsLibraryUsage_WithDumpRequestContractAndRequireAnyKeyNote()
        {
            var repoRoot = FindRepoRoot();
            var readmePath = Path.Combine(repoRoot, "README.md");
            var content = File.ReadAllText(readmePath);

            Assert.Contains("### Library usage", content);
            Assert.Contains("using Il2CppDumper;", content);
            Assert.Contains("`Il2CppDumper.Core`", content);
            Assert.Contains("DumpRequest", content);
            Assert.Contains("DumpResult", content);
            Assert.Contains("Dumper.Dump", content);
            Assert.Contains("`RequireAnyKey`", content);
        }

        [Fact]
        public void Readme_Chinese_DocumentsLibraryUsage_WithDumpRequestContractAndRequireAnyKeyNote()
        {
            var repoRoot = FindRepoRoot();
            var readmePath = Path.Combine(repoRoot, "README.zh-CN.md");
            var content = File.ReadAllText(readmePath);

            Assert.Contains("### 类库调用（Library usage）", content);
            Assert.Contains("using Il2CppDumper;", content);
            Assert.Contains("`Il2CppDumper.Core`", content);
            Assert.Contains("DumpRequest", content);
            Assert.Contains("DumpResult", content);
            Assert.Contains("Dumper.Dump", content);
            Assert.Contains("`RequireAnyKey`", content);
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
                "GenerateJsonDump",
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
                "GenerateJsonDump",
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
        public void Config_DefaultConstructor_HasGenerateJsonDumpFalseByDefault()
        {
            var config = new Config();

            Assert.False(config.GenerateJsonDump);
        }

        [Fact]
        public void Config_DeserializingEmptyJson_ProducesFalseForGenerateJsonDump()
        {
            var config = JsonSerializer.Deserialize<Config>("{}");

            Assert.NotNull(config);
            Assert.False(config.GenerateJsonDump);
        }

        [Fact]
        public void Config_DeserializingGenerateJsonDumpTrue_EnablesGenerationGate()
        {
            const string json = "{\"GenerateJsonDump\": true}";
            var config = JsonSerializer.Deserialize<Config>(json);

            Assert.NotNull(config);
            Assert.True(config.GenerateJsonDump);
        }

        [Fact]
        public void Config_DeserializingGenerateJsonDumpFalse_DisablesGenerationGate()
        {
            const string json = "{\"GenerateJsonDump\": false}";
            var config = JsonSerializer.Deserialize<Config>(json);

            Assert.NotNull(config);
            Assert.False(config.GenerateJsonDump);
        }

        [Fact]
        public void Config_ShippedConfigJson_AlignsWithCodeDefaultsForGenerateJsonDump()
        {
            var repoRoot = FindRepoRoot();
            var configJsonPath = Path.Combine(repoRoot, "Il2CppDumper", "config.json");
            Assert.True(File.Exists(configJsonPath), $"Expected config.json at {configJsonPath}");

            var json = File.ReadAllText(configJsonPath);
            var shippedConfig = JsonSerializer.Deserialize<Config>(json);
            var codeDefaultConfig = new Config();

            Assert.NotNull(shippedConfig);
            Assert.False(shippedConfig.GenerateJsonDump);
            Assert.Equal(codeDefaultConfig.GenerateJsonDump, shippedConfig.GenerateJsonDump);
        }

        [Fact]
        public void Readme_English_DocumentsDumpJson_WithPortugueseSection()
        {
            var repoRoot = FindRepoRoot();
            var readmePath = Path.Combine(repoRoot, "README.md");
            var content = File.ReadAllText(readmePath);

            Assert.Contains("#### dump.json", content);
            Assert.Contains("`GenerateJsonDump`", content);
            Assert.Contains("`types`", content);
            Assert.Contains("`fields`", content);
            Assert.Contains("`properties`", content);
            Assert.Contains("`methods`", content);
            Assert.Contains("`enums`", content);
            Assert.Contains("`nested`", content);
            Assert.Contains("`attributes`", content);
            Assert.Contains("[Serializable]", content);
            Assert.Contains("**Português:**", content);
        }

        [Fact]
        public void Readme_Chinese_DocumentsDumpJson_WithPortugueseSection()
        {
            var repoRoot = FindRepoRoot();
            var readmePath = Path.Combine(repoRoot, "README.zh-CN.md");
            var content = File.ReadAllText(readmePath);

            Assert.Contains("#### dump.json", content);
            Assert.Contains("`GenerateJsonDump`", content);
            Assert.Contains("`types`", content);
            Assert.Contains("`fields`", content);
            Assert.Contains("`properties`", content);
            Assert.Contains("`methods`", content);
            Assert.Contains("`enums`", content);
            Assert.Contains("`nested`", content);
            Assert.Contains("`attributes`", content);
            Assert.Contains("[Serializable]", content);
            Assert.Contains("**Português:**", content);
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

        [Fact]
        public void Readme_English_DocumentsStringLiteralForensicUsage()
        {
            var repoRoot = FindRepoRoot();
            var readmePath = Path.Combine(repoRoot, "README.md");
            var content = File.ReadAllText(readmePath);

            Assert.Contains("#### stringliteral-indexed.json", content);
            Assert.Contains("**Forensic usage:**", content);
            Assert.Contains("`GetStringLiteralFromIndex(n)`", content);
            Assert.Contains("`dataIndex` and `length`", content);
            Assert.Contains("**Uso forense:**", content);
        }

        [Fact]
        public void Readme_Chinese_DocumentsStringLiteralForensicUsage()
        {
            var repoRoot = FindRepoRoot();
            var readmePath = Path.Combine(repoRoot, "README.zh-CN.md");
            var content = File.ReadAllText(readmePath);

            Assert.Contains("#### stringliteral-indexed.json", content);
            Assert.Contains("**取证用途（Forensic usage）：**", content);
            Assert.Contains("`GetStringLiteralFromIndex(n)`", content);
            Assert.Contains("`dataIndex` 与 `length`", content);
            Assert.Contains("**Uso forense:**", content);
        }
        [Fact]
        public void Readme_English_DocumentsDiffingSection()
        {
            var repoRoot = FindRepoRoot();
            var readmePath = Path.Combine(repoRoot, "README.md");
            var content = File.ReadAllText(readmePath);

            Assert.Contains("### Diffing (Il2CppDumper.Diff)", content);
            Assert.Contains("Il2CppDumper.Diff <left> <right> <output>", content);
            Assert.Contains("#### Inputs", content);
            Assert.Contains("`dump.json`", content);
            Assert.Contains("`dump.cs` text is never an input", content);
            Assert.Contains("#### Exit codes", content);
            Assert.Contains("`0`: Identical", content);
            Assert.Contains("`1`: Differences found", content);
            Assert.Contains("`2`: Usage / input error", content);
            Assert.Contains("#### Schema reference (`dump-diff.json`)", content);
            Assert.Contains("\"addressChanged\"", content);
            Assert.Contains("\"addedByIndex\"", content);
            Assert.Contains("\"valueChangedByIndex\"", content);
            Assert.Contains("\"summary\"", content);
            Assert.Contains("**Português:**", content);

            // B4: Schema confrontation test - ensure the documented schema deserializes cleanly into DumpDiffResult
            var jsonStart = content.IndexOf("```json", StringComparison.Ordinal);
            Assert.True(jsonStart >= 0);
            var codeStart = content.IndexOf('{', jsonStart);
            var codeEnd = content.IndexOf("```", codeStart, StringComparison.Ordinal);
            var jsonSnippet = content.Substring(codeStart, codeEnd - codeStart).Trim();

            var parsed = System.Text.Json.JsonSerializer.Deserialize<Il2CppDumper.Diff.DumpDiffResult>(jsonSnippet);
            Assert.NotNull(parsed);
            Assert.Equal("f1", parsed.types.changed[0].fields.added[0]);
            Assert.Equal("f2", parsed.types.changed[0].fields.removed[0]);
            Assert.Equal("f3", parsed.types.changed[0].fields.changed[0].field);
            Assert.Equal("int", parsed.types.changed[0].fields.changed[0].left.type);
            Assert.Equal("0x10", parsed.types.changed[0].fields.changed[0].left.offset);
            Assert.Equal("m1", parsed.types.changed[0].methods.added[0]);
            Assert.Equal("m2", parsed.types.changed[0].methods.removed[0]);
            Assert.Equal("m3", parsed.types.changed[0].methods.changed[0].method);
            Assert.Equal("void", parsed.types.changed[0].methods.changed[0].left.returnType);
            Assert.NotNull(parsed.methods.addressChanged);
            Assert.Equal("Namespace.TypeName", parsed.methods.addressChanged[0].type);
            Assert.Equal("MethodName", parsed.methods.addressChanged[0].method);
        }

        [Fact]
        public void Readme_Chinese_DocumentsDiffingSection()
        {
            var repoRoot = FindRepoRoot();
            var readmePath = Path.Combine(repoRoot, "README.zh-CN.md");
            var content = File.ReadAllText(readmePath);

            Assert.Contains("### 差异对比 (Diffing / Il2CppDumper.Diff)", content);
            Assert.Contains("Il2CppDumper.Diff <left> <right> <output>", content);
            Assert.Contains("#### 输入 (Inputs)", content);
            Assert.Contains("`dump.json`", content);
            Assert.Contains("`dump.cs` 文本绝不作为输入", content);
            Assert.Contains("#### 退出状态码 (Exit codes)", content);
            Assert.Contains("`0`: 完全一致", content);
            Assert.Contains("`1`: 发现差异", content);
            Assert.Contains("`2`: 参数使用或输入错误", content);
            Assert.Contains("#### 数据模式参考 (Schema reference - `dump-diff.json`)", content);
            Assert.Contains("\"addressChanged\"", content);
            Assert.Contains("\"addedByIndex\"", content);
            Assert.Contains("\"valueChangedByIndex\"", content);
            Assert.Contains("\"summary\"", content);
            Assert.Contains("**Português:**", content);

            // B4: Schema confrontation test - ensure Chinese README schema matches emitted model
            var jsonStart = content.IndexOf("```json", StringComparison.Ordinal);
            Assert.True(jsonStart >= 0);
            var codeStart = content.IndexOf('{', jsonStart);
            var codeEnd = content.IndexOf("```", codeStart, StringComparison.Ordinal);
            var jsonSnippet = content.Substring(codeStart, codeEnd - codeStart).Trim();

            var parsed = System.Text.Json.JsonSerializer.Deserialize<Il2CppDumper.Diff.DumpDiffResult>(jsonSnippet);
            Assert.NotNull(parsed);
            Assert.Equal("f1", parsed.types.changed[0].fields.added[0]);
            Assert.Equal("f2", parsed.types.changed[0].fields.removed[0]);
            Assert.Equal("f3", parsed.types.changed[0].fields.changed[0].field);
            Assert.Equal("m1", parsed.types.changed[0].methods.added[0]);
            Assert.Equal("m2", parsed.types.changed[0].methods.removed[0]);
            Assert.Equal("m3", parsed.types.changed[0].methods.changed[0].method);
            Assert.NotNull(parsed.methods.addressChanged);
            Assert.Equal("Namespace.TypeName", parsed.methods.addressChanged[0].type);
        }
    }
}
