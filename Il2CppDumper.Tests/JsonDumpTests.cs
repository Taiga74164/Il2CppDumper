using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;
using static Il2CppDumper.Il2CppConstants;

namespace Il2CppDumper.Tests
{
    public class JsonDumpTests
    {
        private static Metadata CreateEmptyMetadata()
        {
            var bytes = new byte[16384];
            BitConverter.GetBytes(0xFAB11BAFu).CopyTo(bytes, 0); // magic
            BitConverter.GetBytes(29).CopyTo(bytes, 4);           // version
            var m = new Metadata(new MemoryStream(bytes))
            {
                Version = 29
            };
            m.header.stringOffset = 1024;
            m.header.stringSize = 4096;
            m.header.fieldAndParameterDefaultValueDataOffset = 8192;
            m.header.fieldAndParameterDefaultValueDataSize = 1024;
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

        private static Dictionary<int, Il2CppFieldDefaultValue> GetFieldDefaultValuesDic(Metadata m)
        {
            var field = typeof(Metadata).GetField("fieldDefaultValuesDic", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            return (Dictionary<int, Il2CppFieldDefaultValue>)field.GetValue(m)!;
        }

        private static Dictionary<int, Il2CppParameterDefaultValue> GetParameterDefaultValuesDic(Metadata m)
        {
            var field = typeof(Metadata).GetField("parameterDefaultValuesDic", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            return (Dictionary<int, Il2CppParameterDefaultValue>)field.GetValue(m)!;
        }

        private static (Metadata metadata, WebAssemblyMemory il2Cpp) CreateSyntheticEnvironment()
        {
            var m = CreateEmptyMetadata();
            uint strPos = 0;
            AddString(m, "", ref strPos);
            var imgName = AddString(m, "Repro.dll", ref strPos);
            var nsRepro = AddString(m, "Repro", ref strPos);

            // 1. Generic type: Container<T> with method Process<U>(T item, U tag)
            var containerName = AddString(m, "Container`1", ref strPos);
            var processName = AddString(m, "Process", ref strPos);
            var paramTName = AddString(m, "T", ref strPos);
            var paramUName = AddString(m, "U", ref strPos);
            var paramItemName = AddString(m, "item", ref strPos);
            var paramTagName = AddString(m, "tag", ref strPos);

            // 2. Declaring type: Outer, Nested type: Inner
            var outerName = AddString(m, "Outer", ref strPos);
            var innerName = AddString(m, "Inner", ref strPos);
            var innerFieldName = AddString(m, "innerField", ref strPos);
            var innerMethodName = AddString(m, "InnerMethod", ref strPos);

            // 3. Write-only indexer type: IndexerHolder with Item { set; }
            var indexerHolderName = AddString(m, "IndexerHolder", ref strPos);
            var itemName = AddString(m, "Item", ref strPos);
            var setItemName = AddString(m, "set_Item", ref strPos);
            var keyName = AddString(m, "key", ref strPos);
            var valName = AddString(m, "value", ref strPos);

            // 4. Enum with constants: Mode with value__, Ready = 7, Stopped = 0
            var modeName = AddString(m, "Mode", ref strPos);
            var valueFieldName = AddString(m, "value__", ref strPos);
            var readyName = AddString(m, "Ready", ref strPos);
            var stoppedName = AddString(m, "Stopped", ref strPos);

            // 5. Null parameter default: DefaultParamHolder with Execute(object target, string label = null)
            var defaultHolderName = AddString(m, "DefaultParamHolder", ref strPos);
            var executeName = AddString(m, "Execute", ref strPos);
            var targetParamName = AddString(m, "target", ref strPos);
            var labelParamName = AddString(m, "label", ref strPos);

            m.imageDefs = new[]
            {
                new Il2CppImageDefinition
                {
                    nameIndex = imgName,
                    assemblyIndex = -1,
                    typeStart = 0,
                    typeCount = 6
                }
            };

            m.typeDefs = new[]
            {
                // Type 0: Container<T>
                new Il2CppTypeDefinition
                {
                    nameIndex = containerName,
                    namespaceIndex = nsRepro,
                    declaringTypeIndex = -1,
                    parentIndex = -1,
                    genericContainerIndex = 0,
                    methodStart = 0,
                    method_count = 1,
                    flags = TYPE_ATTRIBUTE_PUBLIC
                },
                // Type 1: Outer
                new Il2CppTypeDefinition
                {
                    nameIndex = outerName,
                    namespaceIndex = nsRepro,
                    declaringTypeIndex = -1,
                    parentIndex = -1,
                    genericContainerIndex = -1,
                    nestedTypesStart = 0,
                    nested_type_count = 1,
                    flags = TYPE_ATTRIBUTE_PUBLIC
                },
                // Type 2: Inner (nested in Outer)
                new Il2CppTypeDefinition
                {
                    nameIndex = innerName,
                    namespaceIndex = nsRepro,
                    declaringTypeIndex = 6,
                    parentIndex = -1,
                    genericContainerIndex = -1,
                    fieldStart = 0,
                    field_count = 1,
                    methodStart = 1,
                    method_count = 1,
                    flags = TYPE_ATTRIBUTE_NESTED_PUBLIC
                },
                // Type 3: IndexerHolder
                new Il2CppTypeDefinition
                {
                    nameIndex = indexerHolderName,
                    namespaceIndex = nsRepro,
                    declaringTypeIndex = -1,
                    parentIndex = -1,
                    genericContainerIndex = -1,
                    methodStart = 2,
                    method_count = 1,
                    propertyStart = 0,
                    property_count = 1,
                    flags = TYPE_ATTRIBUTE_PUBLIC
                },
                // Type 4: Mode (enum)
                new Il2CppTypeDefinition
                {
                    nameIndex = modeName,
                    namespaceIndex = nsRepro,
                    declaringTypeIndex = -1,
                    parentIndex = -1,
                    genericContainerIndex = -1,
                    fieldStart = 1,
                    field_count = 3,
                    bitfield = 3, // IsEnum = true
                    flags = TYPE_ATTRIBUTE_PUBLIC | TYPE_ATTRIBUTE_SEALED
                },
                // Type 5: DefaultParamHolder
                new Il2CppTypeDefinition
                {
                    nameIndex = defaultHolderName,
                    namespaceIndex = nsRepro,
                    declaringTypeIndex = -1,
                    parentIndex = -1,
                    genericContainerIndex = -1,
                    methodStart = 3,
                    method_count = 1,
                    flags = TYPE_ATTRIBUTE_PUBLIC
                }
            };

            m.nestedTypeIndices = new[] { 2 };

            m.genericContainers = new[]
            {
                new Il2CppGenericContainer { genericParameterStart = 0, type_argc = 1 },
                new Il2CppGenericContainer { genericParameterStart = 1, type_argc = 1 }
            };

            m.genericParameters = new[]
            {
                new Il2CppGenericParameter { nameIndex = paramTName },
                new Il2CppGenericParameter { nameIndex = paramUName }
            };

            m.fieldDefs = new[]
            {
                // Field 0: Inner.innerField
                new Il2CppFieldDefinition { nameIndex = innerFieldName, typeIndex = 1, token = 0x04000001 },
                // Field 1: Mode.value__
                new Il2CppFieldDefinition { nameIndex = valueFieldName, typeIndex = 1, token = 0x04000002 },
                // Field 2: Mode.Ready
                new Il2CppFieldDefinition { nameIndex = readyName, typeIndex = 8, token = 0x04000003 },
                // Field 3: Mode.Stopped
                new Il2CppFieldDefinition { nameIndex = stoppedName, typeIndex = 8, token = 0x04000004 }
            };

            m.methodDefs = new[]
            {
                // Method 0: Container<T>.Process<U>(T item, U tag)
                new Il2CppMethodDefinition
                {
                    nameIndex = processName,
                    returnType = 0,
                    parameterStart = 0,
                    parameterCount = 2,
                    genericContainerIndex = 1,
                    methodIndex = -1,
                    flags = METHOD_ATTRIBUTE_PUBLIC,
                    slot = ushort.MaxValue
                },
                // Method 1: Inner.InnerMethod()
                new Il2CppMethodDefinition
                {
                    nameIndex = innerMethodName,
                    returnType = 0,
                    parameterStart = 0,
                    parameterCount = 0,
                    genericContainerIndex = -1,
                    methodIndex = -1,
                    flags = METHOD_ATTRIBUTE_PUBLIC,
                    slot = ushort.MaxValue
                },
                // Method 2: IndexerHolder.set_Item(int key, string value)
                new Il2CppMethodDefinition
                {
                    nameIndex = setItemName,
                    returnType = 0,
                    parameterStart = 2,
                    parameterCount = 2,
                    genericContainerIndex = -1,
                    methodIndex = -1,
                    flags = METHOD_ATTRIBUTE_PUBLIC,
                    slot = ushort.MaxValue
                },
                // Method 3: DefaultParamHolder.Execute(object target, string label = null)
                new Il2CppMethodDefinition
                {
                    nameIndex = executeName,
                    returnType = 0,
                    parameterStart = 4,
                    parameterCount = 2,
                    genericContainerIndex = -1,
                    methodIndex = -1,
                    flags = METHOD_ATTRIBUTE_PUBLIC,
                    slot = ushort.MaxValue
                }
            };

            m.parameterDefs = new[]
            {
                // Process params
                new Il2CppParameterDefinition { nameIndex = paramItemName, typeIndex = 4 },
                new Il2CppParameterDefinition { nameIndex = paramTagName, typeIndex = 5 },
                // set_Item params
                new Il2CppParameterDefinition { nameIndex = keyName, typeIndex = 1 },
                new Il2CppParameterDefinition { nameIndex = valName, typeIndex = 2 },
                // Execute params
                new Il2CppParameterDefinition { nameIndex = targetParamName, typeIndex = 3 },
                new Il2CppParameterDefinition { nameIndex = labelParamName, typeIndex = 3 }
            };

            m.propertyDefs = new[]
            {
                new Il2CppPropertyDefinition { nameIndex = itemName, get = -1, set = 0, token = 0x17000001 }
            };

            // Setup default value blobs in metadata stream
            var dataOffset = m.header.fieldAndParameterDefaultValueDataOffset;
            m.Position = dataOffset;
            m.Writer.Write(7); // Ready = 7
            m.Writer.Write(0); // Stopped = 0

            var fieldDefaults = GetFieldDefaultValuesDic(m);
            fieldDefaults[2] = new Il2CppFieldDefaultValue { fieldIndex = 2, typeIndex = 1, dataIndex = 0 };
            fieldDefaults[3] = new Il2CppFieldDefaultValue { fieldIndex = 3, typeIndex = 1, dataIndex = 4 };

            var paramDefaults = GetParameterDefaultValuesDic(m);
            paramDefaults[5] = new Il2CppParameterDefaultValue { parameterIndex = 5, typeIndex = 3, dataIndex = 0 };

            var il = new WebAssemblyMemory(new MemoryStream(new byte[1024]), 0)
            {
                Version = 16, // Version < 27 treats klassIndex directly as typeDef index
                methodPointers = Array.Empty<ulong>()
            };

            il.types = new[]
            {
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_VOID },                                                               // 0: void
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_I4 },                                                                 // 1: int
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_STRING },                                                             // 2: string
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_OBJECT },                                                             // 3: object
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_VAR, data = new Il2CppType.Union { dummy = 0 } },     // 4: T
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_MVAR, data = new Il2CppType.Union { dummy = 1 } },    // 5: U
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_CLASS, data = new Il2CppType.Union { dummy = 1 } },   // 6: Outer
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE, data = new Il2CppType.Union { dummy = 4 } }, // 7: Mode
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE, data = new Il2CppType.Union { dummy = 4 }, attrs = FIELD_ATTRIBUTE_PUBLIC | FIELD_ATTRIBUTE_STATIC | FIELD_ATTRIBUTE_LITERAL } // 8: Mode literal
            };

            // Set field attributes:
            // Field 0: Inner.innerField (instance int)
            il.types[1].attrs = FIELD_ATTRIBUTE_PUBLIC;
            // Field 1: Mode.value__ (instance int)
            // Field 2: Mode.Ready (literal static Mode)
            // Field 3: Mode.Stopped (literal static Mode)

            return (m, il);
        }

        private static IEnumerable<JsonDumpType> FlattenTypes(IEnumerable<JsonDumpType> types)
        {
            foreach (var type in types)
            {
                yield return type;
                if (type.nested != null && type.nested.Count > 0)
                {
                    foreach (var child in FlattenTypes(type.nested))
                    {
                        yield return child;
                    }
                }
            }
        }

        [Fact]
        public void SyntheticFixture_GeneratesValidJson_AndRoundTripsSystemTextJsonDeserialize()
        {
            var (m, il) = CreateSyntheticEnvironment();
            using (m)
            using (il)
            {
                var executor = new Il2CppExecutor(m, il);
                var config = new Config
                {
                    GenerateJsonDump = true,
                    DumpProperty = true,
                    DumpField = true,
                    DumpMethod = true,
                    DumpAttribute = false
                };

                var tempDir = Path.Combine(Path.GetTempPath(), "dumpjson-roundtrip-" + Guid.NewGuid()) + Path.DirectorySeparatorChar;
                Directory.CreateDirectory(tempDir);

                try
                {
                    var decompiler = new Il2CppDecompiler(executor);
                    var success = decompiler.Decompile(config, tempDir);
                    Assert.True(success);

                    var jsonPath = Path.Combine(tempDir, "dump.json");
                    Assert.True(File.Exists(jsonPath));

                    var jsonText = File.ReadAllText(jsonPath);
                    Assert.NotEmpty(jsonText);

                    // Round-trip deserialization via System.Text.Json
                    var model = JsonSerializer.Deserialize<JsonDumpModel>(jsonText, JsonDumpGenerator.JsonOptions);
                    Assert.NotNull(model);
                    Assert.NotEmpty(model.types);

                    var allTypes = FlattenTypes(model.types).ToList();

                    // 1. Generics check
                    var containerType = allTypes.FirstOrDefault(t => t.fullName == "Repro.Container<T>");
                    Assert.NotNull(containerType);
                    var processMethod = containerType.methods.FirstOrDefault(m => m.name == "Process<U>");
                    Assert.NotNull(processMethod);
                    Assert.Equal(2, processMethod.parameters.Count);
                    Assert.Equal("T", processMethod.parameters[0].type);
                    Assert.Equal("U", processMethod.parameters[1].type);

                    // 2. Nested types check
                    var outerType = model.types.FirstOrDefault(t => t.fullName == "Repro.Outer");
                    Assert.NotNull(outerType);
                    Assert.Single(outerType.nested);
                    var innerType = outerType.nested[0];
                    Assert.Equal("Repro.Outer.Inner", innerType.fullName);
                    var innerField = innerType.fields.FirstOrDefault(f => f.name == "innerField");
                    Assert.NotNull(innerField);
                    Assert.Equal("int", innerField.type);
                    var innerMethod = innerType.methods.FirstOrDefault(m => m.name == "InnerMethod");
                    Assert.NotNull(innerMethod);
                    Assert.Equal("void", innerMethod.returnType);

                    // 3. Write-only indexer from PR#17
                    var indexerType = allTypes.FirstOrDefault(t => t.fullName == "Repro.IndexerHolder");
                    Assert.NotNull(indexerType);
                    var itemProperty = indexerType.properties.FirstOrDefault(p => p.name == "Item");
                    Assert.NotNull(itemProperty);
                    Assert.Equal("string", itemProperty.type);
                    Assert.Null(itemProperty.getter);
                    Assert.Equal("set_Item", itemProperty.setter);

                    // 4. Enum with constants
                    var modeType = allTypes.FirstOrDefault(t => t.fullName == "Repro.Mode");
                    Assert.NotNull(modeType);
                    Assert.NotEmpty(modeType.enums);
                    var modeEnum = modeType.enums.First(e => e.name == "Mode");
                    Assert.Equal("int", modeEnum.underlyingType);
                    Assert.Equal(7L, Convert.ToInt64(modeEnum.GetConstantValue("Ready")));
                    Assert.Equal(0L, Convert.ToInt64(modeEnum.GetConstantValue("Stopped")));

                    // 5. Null parameter default from PR#18
                    var defaultHolder = allTypes.FirstOrDefault(t => t.fullName == "Repro.DefaultParamHolder");
                    Assert.NotNull(defaultHolder);
                    var execMethod = defaultHolder.methods.FirstOrDefault(m => m.name == "Execute");
                    Assert.NotNull(execMethod);
                    Assert.Equal(2, execMethod.parameters.Count);
                    var labelParam = execMethod.parameters[1];
                    Assert.Equal("label", labelParam.name);
                    Assert.Equal("null", labelParam.defaultValue);
                }
                finally
                {
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, true);
                    }
                }
            }
        }

        [Fact]
        public void SyntheticFixture_Parity_EveryDumpCsMemberPresentInDumpJson()
        {
            var (m, il) = CreateSyntheticEnvironment();
            using (m)
            using (il)
            {
                var executor = new Il2CppExecutor(m, il);
                var config = new Config
                {
                    GenerateJsonDump = true,
                    DumpProperty = true,
                    DumpField = true,
                    DumpMethod = true,
                    DumpAttribute = false
                };

                var tempDir = Path.Combine(Path.GetTempPath(), "dumpjson-parity-" + Guid.NewGuid()) + Path.DirectorySeparatorChar;
                Directory.CreateDirectory(tempDir);

                try
                {
                    var decompiler = new Il2CppDecompiler(executor);
                    decompiler.Decompile(config, tempDir);

                    var csLines = File.ReadAllLines(Path.Combine(tempDir, "dump.cs"));
                    var jsonText = File.ReadAllText(Path.Combine(tempDir, "dump.json"));
                    var model = JsonSerializer.Deserialize<JsonDumpModel>(jsonText, JsonDumpGenerator.JsonOptions)!;
                    var allTypes = FlattenTypes(model.types).ToList();

                    // Parse members from dump.cs text:
                    // 1. Fields: "\t<access> [modifiers] <type> <name>;" or with " = ...;"
                    // 2. Properties: "\t<access> [modifiers] <type> <name> { ... }"
                    // 3. Methods: "\t<access> [modifiers] <type> <name>(...) { }"
                    // 4. Enum constants: "\tpublic const <enum> <name> = <val>;"

                    var allJsonFields = allTypes.SelectMany(t => t.fields).ToList();
                    var allJsonProperties = allTypes.SelectMany(t => t.properties).ToList();
                    var allJsonMethods = allTypes.SelectMany(t => t.methods).ToList();
                    var allJsonEnumConstants = allTypes.SelectMany(t => t.enums).SelectMany(e => e.constants.Keys).ToHashSet();

                    foreach (var rawLine in csLines)
                    {
                        var line = rawLine.Trim();
                        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//") || line.StartsWith("/*") || line.StartsWith("{") || line.StartsWith("}"))
                            continue;

                        // Enum constant
                        var constMatch = Regex.Match(line, @"^public\s+const\s+\S+\s+(\w+)\s*=\s*(.+);$");
                        if (constMatch.Success)
                        {
                            var constName = constMatch.Groups[1].Value;
                            Assert.True(allJsonEnumConstants.Contains(constName), $"Enum constant '{constName}' from dump.cs must be present in dump.json constants");
                            continue;
                        }

                        // Property: e.g. "public string Item { set; }"
                        var propMatch = Regex.Match(line, @"^(?:public|protected|private|internal)\s+(?:static\s+)?(\S+)\s+(\w+)\s*\{\s*(?:get;\s*)?(?:set;\s*)?\}$");
                        if (propMatch.Success)
                        {
                            var propType = propMatch.Groups[1].Value;
                            var propName = propMatch.Groups[2].Value;
                            var foundProp = allJsonProperties.FirstOrDefault(p => p.name == propName && p.type == propType);
                            Assert.True(foundProp != null, $"Property '{propType} {propName}' from dump.cs must be present in dump.json properties (name + type equality)");
                            continue;
                        }

                        // Method: e.g. "public void Process<U>(...) { }" or "public void Execute(...) { }"
                        var methodMatch = Regex.Match(line, @"^(?:public|protected|private|internal)\s+(?:static\s+)?(?:abstract\s+|override\s+|virtual\s+)?(\S+)\s+([A-Za-z0-9_<>]+)\(.*\)\s*(?:\{\s*\}|;)$");
                        if (methodMatch.Success)
                        {
                            var retType = methodMatch.Groups[1].Value;
                            var methName = methodMatch.Groups[2].Value;
                            var foundMethod = allJsonMethods.FirstOrDefault(m => m.name == methName && m.returnType == retType);
                            Assert.True(foundMethod != null, $"Method '{retType} {methName}' from dump.cs must be present in dump.json methods (name + type equality)");
                            continue;
                        }

                        // Field: e.g. "public int innerField;" or "public int value__;"
                        var fieldMatch = Regex.Match(line, @"^(?:public|protected|private|internal)\s+(?:static\s+)?(?:readonly\s+)?(\S+)\s+(\w+)\s*(?:=\s*[^;]+)?\s*;");
                        if (fieldMatch.Success)
                        {
                            var fldType = fieldMatch.Groups[1].Value;
                            var fldName = fieldMatch.Groups[2].Value;
                            var foundField = allJsonFields.FirstOrDefault(f => f.name == fldName && f.type == fldType);
                            Assert.True(foundField != null, $"Field '{fldType} {fldName}' from dump.cs must be present in dump.json fields (name + type equality)");
                        }
                    }
                }
                finally
                {
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, true);
                    }
                }
            }
        }

        [Fact]
        public void Config_GenerateJsonDumpFalse_DoesNotWriteDumpJson()
        {
            var (m, il) = CreateSyntheticEnvironment();
            using (m)
            using (il)
            {
                var executor = new Il2CppExecutor(m, il);
                var config = new Config
                {
                    GenerateJsonDump = false
                };

                var tempDir = Path.Combine(Path.GetTempPath(), "dumpjson-disabled-" + Guid.NewGuid()) + Path.DirectorySeparatorChar;
                Directory.CreateDirectory(tempDir);

                try
                {
                    var decompiler = new Il2CppDecompiler(executor);
                    var success = decompiler.Decompile(config, tempDir);
                    Assert.True(success);

                    Assert.True(File.Exists(Path.Combine(tempDir, "dump.cs")));
                    Assert.False(File.Exists(Path.Combine(tempDir, "dump.json")));
                }
                finally
                {
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, true);
                    }
                }
            }
        }

        [Fact]
        public void ProgramDump_OutputPhase_HonorsGenerateJsonDump()
        {
            var (m, il) = CreateSyntheticEnvironment();
            using (m)
            using (il)
            {
                var tempDir = Path.Combine(Path.GetTempPath(), "dumpjson-programdump-" + Guid.NewGuid()) + Path.DirectorySeparatorChar;
                Directory.CreateDirectory(tempDir);

                var programType = typeof(Program);
                var configField = programType.GetField("config", BindingFlags.NonPublic | BindingFlags.Static)!;
                var originalConfig = configField.GetValue(null);

                try
                {
                    var cfg = new Config { GenerateJsonDump = true, GenerateStruct = false, GenerateDummyDll = false };
                    configField.SetValue(null, cfg);

                    var dumpMethod = programType.GetMethod("Dump", BindingFlags.NonPublic | BindingFlags.Static)!;
                    var result = (bool)dumpMethod.Invoke(null, new object[] { m, il, tempDir })!;

                    Assert.True(result);
                    Assert.True(File.Exists(Path.Combine(tempDir, "dump.cs")));
                    Assert.True(File.Exists(Path.Combine(tempDir, "dump.json")));
                }
                finally
                {
                    configField.SetValue(null, originalConfig);
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, true);
                    }
                }
            }
        }
    }
}
