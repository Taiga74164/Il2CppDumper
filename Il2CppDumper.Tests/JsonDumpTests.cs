using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Sdk;
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

            // 5. Null parameter default & Parameter modifiers: DefaultParamHolder
            var defaultHolderName = AddString(m, "DefaultParamHolder", ref strPos);
            var executeName = AddString(m, "Execute", ref strPos);
            var targetParamName = AddString(m, "target", ref strPos);
            var labelParamName = AddString(m, "label", ref strPos);

            var modifyParamsName = AddString(m, "ModifyParams", ref strPos);
            var refValName = AddString(m, "refVal", ref strPos);
            var outValName = AddString(m, "outVal", ref strPos);
            var inValName = AddString(m, "inVal", ref strPos);
            var inAttrValName = AddString(m, "inAttrVal", ref strPos);
            var outAttrValName = AddString(m, "outAttrVal", ref strPos);

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
                // Type 1: Outer (decorated with Serializable)
                new Il2CppTypeDefinition
                {
                    nameIndex = outerName,
                    namespaceIndex = nsRepro,
                    declaringTypeIndex = -1,
                    parentIndex = -1,
                    genericContainerIndex = -1,
                    nestedTypesStart = 0,
                    nested_type_count = 1,
                    flags = TYPE_ATTRIBUTE_PUBLIC | TYPE_ATTRIBUTE_SERIALIZABLE
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
                    method_count = 2,
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
                },
                // Method 4: DefaultParamHolder.ModifyParams(ref int refVal, out string outVal, in int inVal, [In] int inAttrVal, [Out] int outAttrVal)
                new Il2CppMethodDefinition
                {
                    nameIndex = modifyParamsName,
                    returnType = 0,
                    parameterStart = 6,
                    parameterCount = 5,
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
                new Il2CppParameterDefinition { nameIndex = labelParamName, typeIndex = 3 },
                // ModifyParams params (ref, out, in, [In], [Out])
                new Il2CppParameterDefinition { nameIndex = refValName, typeIndex = 9 },
                new Il2CppParameterDefinition { nameIndex = outValName, typeIndex = 10 },
                new Il2CppParameterDefinition { nameIndex = inValName, typeIndex = 11 },
                new Il2CppParameterDefinition { nameIndex = inAttrValName, typeIndex = 12 },
                new Il2CppParameterDefinition { nameIndex = outAttrValName, typeIndex = 13 }
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
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE, data = new Il2CppType.Union { dummy = 4 }, attrs = FIELD_ATTRIBUTE_PUBLIC | FIELD_ATTRIBUTE_STATIC | FIELD_ATTRIBUTE_LITERAL }, // 8: Mode literal
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_I4, byref = 1 },                                                      // 9: ref int
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_STRING, byref = 1, attrs = PARAM_ATTRIBUTE_OUT },                    // 10: out string
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_I4, byref = 1, attrs = PARAM_ATTRIBUTE_IN },                         // 11: in int
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_I4, byref = 0, attrs = PARAM_ATTRIBUTE_IN },                         // 12: [In] int
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_I4, byref = 0, attrs = PARAM_ATTRIBUTE_OUT }                         // 13: [Out] int
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

        private static void AssertParity(string[] csLines, JsonDumpModel model, bool checkAttributes = false)
        {
            var allTypes = FlattenTypes(model.types).ToDictionary(t => t.fullName, t => t);

            string currentNamespace = "";
            JsonDumpType? currentType = null;
            string? expectedEnumUnderlyingType = null;
            var inType = false;

            for (var lineIndex = 0; lineIndex < csLines.Length; lineIndex++)
            {
                var line = csLines[lineIndex].Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("/*") || line.StartsWith("*"))
                    continue;

                if (line.StartsWith("// Namespace:"))
                {
                    currentNamespace = line.Substring("// Namespace:".Length).Trim();
                    continue;
                }

                if (line.StartsWith("//"))
                    continue;

                // Check for type definition start
                var typeDeclMatch = Regex.Match(line, @"^(?:public|protected|private|internal)\s+(?:static\s+|abstract\s+|sealed\s+)*(?:class|struct|enum|interface)\s+([^\s:{]+)");
                if (typeDeclMatch.Success)
                {
                    var typeName = typeDeclMatch.Groups[1].Value;
                    var fullName = string.IsNullOrEmpty(currentNamespace) ? typeName : $"{currentNamespace}.{typeName}";
                    Assert.True(allTypes.ContainsKey(fullName), $"Type '{fullName}' from dump.cs must exist in dump.json");
                    currentType = allTypes[fullName];
                    expectedEnumUnderlyingType = null;
                    inType = true;

                    if (checkAttributes)
                    {
                        var prevIdx = lineIndex - 1;
                        while (prevIdx >= 0 && csLines[prevIdx].Trim().StartsWith("["))
                        {
                            var attrText = csLines[prevIdx].Trim();
                            Assert.True(currentType.attributes.Contains(attrText), $"Type '{fullName}' must contain attribute '{attrText}' in dump.json");
                            prevIdx--;
                        }
                    }
                    continue;
                }

                if (line == "{")
                    continue;

                if (line == "}")
                {
                    currentType = null;
                    expectedEnumUnderlyingType = null;
                    inType = false;
                    continue;
                }

                if (!inType || currentType == null)
                    continue;

                // Inside type block:
                // 1. Enum constant
                var constMatch = Regex.Match(line, @"^public\s+const\s+(\S+)\s+(\w+)\s*=\s*(.+);$");
                if (constMatch.Success)
                {
                    var enumTypeName = constMatch.Groups[1].Value;
                    var constName = constMatch.Groups[2].Value;
                    var constVal = constMatch.Groups[3].Value.Trim();

                    var jsonEnum = currentType.enums.FirstOrDefault();
                    Assert.True(jsonEnum != null, $"Enum constant '{constName}' declared in '{currentType.fullName}', but no enum found in dump.json type.");
                    
                    // Explicitly compare enum identity:
                    Assert.Equal(enumTypeName, jsonEnum.name);

                    // Explicitly compare underlyingType from value__ field in dump.cs:
                    var expectedUnderlying = expectedEnumUnderlyingType 
                        ?? currentType.fields.FirstOrDefault(f => f.name == "value__")?.type;
                    if (!string.IsNullOrEmpty(expectedUnderlying))
                    {
                        Assert.Equal(expectedUnderlying, jsonEnum.underlyingType);
                    }

                    Assert.True(jsonEnum.constants.ContainsKey(constName),
                        $"Enum constant '{constName}' not found in JsonDumpEnum for '{currentType.fullName}'");
                    Assert.Equal(constVal, jsonEnum.constants[constName].ToString());
                    continue;
                }

                // 2. Property
                var propMatch = Regex.Match(line, @"^(?:public|protected|private|internal)\s+(?:static\s+)?(\S+)\s+(\w+)\s*\{\s*(?:get;\s*)?(?:set;\s*)?\}$");
                if (propMatch.Success)
                {
                    var propType = propMatch.Groups[1].Value;
                    var propName = propMatch.Groups[2].Value;
                    var foundProp = currentType.properties.FirstOrDefault(p => p.name == propName);
                    Assert.True(foundProp != null, $"Property '{propName}' from dump.cs must be present in declaring JsonDumpType '{currentType.fullName}'");
                    Assert.Equal(propType, foundProp.type);

                    if (line.Contains("get;"))
                        Assert.NotNull(foundProp.getter);
                    else
                        Assert.Null(foundProp.getter);

                    if (line.Contains("set;"))
                        Assert.NotNull(foundProp.setter);
                    else
                        Assert.Null(foundProp.setter);

                    continue;
                }

                // 3. Method
                var methodMatch = Regex.Match(line, @"^(?:public|protected|private|internal)\s+(?:static\s+)?(?:abstract\s+|override\s+|virtual\s+)?(\S+)\s+([A-Za-z0-9_<>]+)\((.*)\)\s*(?:\{\s*\}|;)$");
                if (methodMatch.Success)
                {
                    var retType = methodMatch.Groups[1].Value;
                    var methName = methodMatch.Groups[2].Value;
                    var rawParams = methodMatch.Groups[3].Value.Trim();

                    var foundMethod = currentType.methods.FirstOrDefault(m => m.name == methName && m.returnType == retType);
                    Assert.True(foundMethod != null, $"Method '{retType} {methName}' from dump.cs must be present in declaring JsonDumpType '{currentType.fullName}'");

                    if (string.IsNullOrEmpty(rawParams))
                    {
                        Assert.Empty(foundMethod.parameters);
                    }
                    else
                    {
                        var expectedParams = rawParams.Split(new[] { ", " }, StringSplitOptions.None);
                        Assert.Equal(expectedParams.Length, foundMethod.parameters.Count);
                        for (var p = 0; p < expectedParams.Length; p++)
                        {
                            var expectedParamStr = expectedParams[p].Trim();
                            string? expectedDefault = null;
                            if (expectedParamStr.Contains('='))
                            {
                                var parts = expectedParamStr.Split('=');
                                expectedParamStr = parts[0].Trim();
                                expectedDefault = parts[1].Trim();
                            }
                            var lastSpace = expectedParamStr.LastIndexOf(' ');
                            var expectedParamType = expectedParamStr.Substring(0, lastSpace).Trim();
                            var expectedParamName = expectedParamStr.Substring(lastSpace + 1).Trim();

                            var actualParam = foundMethod.parameters[p];
                            Assert.Equal(expectedParamName, actualParam.name);
                            Assert.Equal(expectedParamType, actualParam.type);
                            if (expectedDefault != null)
                            {
                                Assert.Equal(expectedDefault, actualParam.defaultValue);
                            }
                        }
                    }
                    continue;
                }

                // 4. Field
                var fieldMatch = Regex.Match(line, @"^(?:public|protected|private|internal)\s+(?:static\s+)?(?:readonly\s+)?(?:const\s+)?(\S+)\s+(\w+)\s*(?:=\s*[^;]+)?\s*;");
                if (fieldMatch.Success)
                {
                    var fldType = fieldMatch.Groups[1].Value;
                    var fldName = fieldMatch.Groups[2].Value;
                    if (fldName == "value__")
                    {
                        expectedEnumUnderlyingType = fldType;
                    }
                    var foundField = currentType.fields.FirstOrDefault(f => f.name == fldName);
                    Assert.True(foundField != null, $"Field '{fldName}' from dump.cs must be present in declaring JsonDumpType '{currentType.fullName}'");
                    Assert.Equal(fldType, foundField.type);
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

                    // 5. Null parameter default from PR#18 and Parameter modifiers (ref, out, in, [In], [Out])
                    var defaultHolder = allTypes.FirstOrDefault(t => t.fullName == "Repro.DefaultParamHolder");
                    Assert.NotNull(defaultHolder);
                    var execMethod = defaultHolder.methods.FirstOrDefault(m => m.name == "Execute");
                    Assert.NotNull(execMethod);
                    Assert.Equal(2, execMethod.parameters.Count);
                    var labelParam = execMethod.parameters[1];
                    Assert.Equal("label", labelParam.name);
                    Assert.Equal("null", labelParam.defaultValue);

                    var modMethod = defaultHolder.methods.FirstOrDefault(m => m.name == "ModifyParams");
                    Assert.NotNull(modMethod);
                    Assert.Equal(5, modMethod.parameters.Count);
                    Assert.Equal("ref int", modMethod.parameters[0].type);
                    Assert.Equal("out string", modMethod.parameters[1].type);
                    Assert.Equal("in int", modMethod.parameters[2].type);
                    Assert.Equal("[In] int", modMethod.parameters[3].type);
                    Assert.Equal("[Out] int", modMethod.parameters[4].type);
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
                    DumpAttribute = true
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

                    // Strictly validate each member in its declaring type and verify enum constants
                    AssertParity(csLines, model, checkAttributes: true);
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
        public void ParityValidator_Detects_MemberMovedToWrongClass()
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
                    DumpAttribute = true
                };

                var tempDir = Path.Combine(Path.GetTempPath(), "dumpjson-tamper-method-" + Guid.NewGuid()) + Path.DirectorySeparatorChar;
                Directory.CreateDirectory(tempDir);

                try
                {
                    var decompiler = new Il2CppDecompiler(executor);
                    decompiler.Decompile(config, tempDir);

                    var csLines = File.ReadAllLines(Path.Combine(tempDir, "dump.cs"));
                    var jsonText = File.ReadAllText(Path.Combine(tempDir, "dump.json"));
                    var model = JsonSerializer.Deserialize<JsonDumpModel>(jsonText, JsonDumpGenerator.JsonOptions)!;

                    var allTypes = FlattenTypes(model.types).ToList();
                    var outer = allTypes.First(t => t.fullName == "Repro.Outer");
                    var inner = allTypes.First(t => t.fullName == "Repro.Outer.Inner");

                    // Move InnerMethod from Inner to Outer
                    var innerMethod = inner.methods.First(m => m.name == "InnerMethod");
                    inner.methods.Remove(innerMethod);
                    outer.methods.Add(innerMethod);

                    // Must throw because InnerMethod is expected in declaring type Repro.Outer.Inner
                    var ex = Assert.Throws<TrueException>(() => AssertParity(csLines, model, checkAttributes: false));
                    Assert.Contains("InnerMethod", ex.UserMessage);
                    Assert.Contains("Repro.Outer.Inner", ex.UserMessage);
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
        public void ParityValidator_Detects_FieldMovedToWrongClass()
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
                    DumpAttribute = true
                };

                var tempDir = Path.Combine(Path.GetTempPath(), "dumpjson-tamper-field-" + Guid.NewGuid()) + Path.DirectorySeparatorChar;
                Directory.CreateDirectory(tempDir);

                try
                {
                    var decompiler = new Il2CppDecompiler(executor);
                    decompiler.Decompile(config, tempDir);

                    var csLines = File.ReadAllLines(Path.Combine(tempDir, "dump.cs"));
                    var jsonText = File.ReadAllText(Path.Combine(tempDir, "dump.json"));
                    var model = JsonSerializer.Deserialize<JsonDumpModel>(jsonText, JsonDumpGenerator.JsonOptions)!;

                    var allTypes = FlattenTypes(model.types).ToList();
                    var outer = allTypes.First(t => t.fullName == "Repro.Outer");
                    var inner = allTypes.First(t => t.fullName == "Repro.Outer.Inner");

                    // Move innerField from Inner to Outer
                    var innerField = inner.fields.First(f => f.name == "innerField");
                    inner.fields.Remove(innerField);
                    outer.fields.Add(innerField);

                    var ex = Assert.Throws<TrueException>(() => AssertParity(csLines, model, checkAttributes: false));
                    Assert.Contains("innerField", ex.UserMessage);
                    Assert.Contains("Repro.Outer.Inner", ex.UserMessage);
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
        public void ParityValidator_Detects_EnumConstantValueTampered()
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
                    DumpAttribute = true
                };

                var tempDir = Path.Combine(Path.GetTempPath(), "dumpjson-tamper-enumval-" + Guid.NewGuid()) + Path.DirectorySeparatorChar;
                Directory.CreateDirectory(tempDir);

                try
                {
                    var decompiler = new Il2CppDecompiler(executor);
                    decompiler.Decompile(config, tempDir);

                    var csLines = File.ReadAllLines(Path.Combine(tempDir, "dump.cs"));
                    var jsonText = File.ReadAllText(Path.Combine(tempDir, "dump.json"));
                    var model = JsonSerializer.Deserialize<JsonDumpModel>(jsonText, JsonDumpGenerator.JsonOptions)!;

                    var allTypes = FlattenTypes(model.types).ToList();
                    var mode = allTypes.First(t => t.fullName == "Repro.Mode");

                    // Tamper Ready value from 7 to 999
                    mode.enums[0].constants["Ready"] = 999;

                    Assert.Throws<EqualException>(() => AssertParity(csLines, model, checkAttributes: false));
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
        public void ParityValidator_Detects_EnumConstantNameMismatch()
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
                    DumpAttribute = true
                };

                var tempDir = Path.Combine(Path.GetTempPath(), "dumpjson-tamper-enumname-" + Guid.NewGuid()) + Path.DirectorySeparatorChar;
                Directory.CreateDirectory(tempDir);

                try
                {
                    var decompiler = new Il2CppDecompiler(executor);
                    decompiler.Decompile(config, tempDir);

                    var csLines = File.ReadAllLines(Path.Combine(tempDir, "dump.cs"));
                    var jsonText = File.ReadAllText(Path.Combine(tempDir, "dump.json"));
                    var model = JsonSerializer.Deserialize<JsonDumpModel>(jsonText, JsonDumpGenerator.JsonOptions)!;

                    var allTypes = FlattenTypes(model.types).ToList();
                    var mode = allTypes.First(t => t.fullName == "Repro.Mode");

                    // Rename Ready to NotReady
                    var val = mode.enums[0].constants["Ready"];
                    mode.enums[0].constants.Remove("Ready");
                    mode.enums[0].constants["NotReady"] = val;

                    var ex = Assert.Throws<TrueException>(() => AssertParity(csLines, model, checkAttributes: false));
                    Assert.Contains("Ready", ex.UserMessage);
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
        public void ParityValidator_Detects_EnumNameMismatch()
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
                    DumpAttribute = true
                };

                var tempDir = Path.Combine(Path.GetTempPath(), "dumpjson-tamper-enumnameonly-" + Guid.NewGuid()) + Path.DirectorySeparatorChar;
                Directory.CreateDirectory(tempDir);

                try
                {
                    var decompiler = new Il2CppDecompiler(executor);
                    decompiler.Decompile(config, tempDir);

                    var csLines = File.ReadAllLines(Path.Combine(tempDir, "dump.cs"));
                    var jsonText = File.ReadAllText(Path.Combine(tempDir, "dump.json"));
                    var model = JsonSerializer.Deserialize<JsonDumpModel>(jsonText, JsonDumpGenerator.JsonOptions)!;

                    var allTypes = FlattenTypes(model.types).ToList();
                    var mode = allTypes.First(t => t.fullName == "Repro.Mode");

                    // Adulterar apenas o name do enum
                    mode.enums[0].name = "TamperedModeName";

                    Assert.Throws<EqualException>(() => AssertParity(csLines, model, checkAttributes: false));
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
        public void ParityValidator_Detects_EnumUnderlyingTypeMismatch()
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
                    DumpAttribute = true
                };

                var tempDir = Path.Combine(Path.GetTempPath(), "dumpjson-tamper-enumtypeonly-" + Guid.NewGuid()) + Path.DirectorySeparatorChar;
                Directory.CreateDirectory(tempDir);

                try
                {
                    var decompiler = new Il2CppDecompiler(executor);
                    decompiler.Decompile(config, tempDir);

                    var csLines = File.ReadAllLines(Path.Combine(tempDir, "dump.cs"));
                    var jsonText = File.ReadAllText(Path.Combine(tempDir, "dump.json"));
                    var model = JsonSerializer.Deserialize<JsonDumpModel>(jsonText, JsonDumpGenerator.JsonOptions)!;

                    var allTypes = FlattenTypes(model.types).ToList();
                    var mode = allTypes.First(t => t.fullName == "Repro.Mode");

                    // Adulterar apenas o underlyingType do enum
                    mode.enums[0].underlyingType = "byte";

                    Assert.Throws<EqualException>(() => AssertParity(csLines, model, checkAttributes: false));
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
        public void ParityValidator_Detects_EnumNameAndUnderlyingTypeMismatch()
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
                    DumpAttribute = true
                };

                var tempDir = Path.Combine(Path.GetTempPath(), "dumpjson-tamper-enumboth-" + Guid.NewGuid()) + Path.DirectorySeparatorChar;
                Directory.CreateDirectory(tempDir);

                try
                {
                    var decompiler = new Il2CppDecompiler(executor);
                    decompiler.Decompile(config, tempDir);

                    var csLines = File.ReadAllLines(Path.Combine(tempDir, "dump.cs"));
                    var jsonText = File.ReadAllText(Path.Combine(tempDir, "dump.json"));
                    var model = JsonSerializer.Deserialize<JsonDumpModel>(jsonText, JsonDumpGenerator.JsonOptions)!;

                    var allTypes = FlattenTypes(model.types).ToList();
                    var mode = allTypes.First(t => t.fullName == "Repro.Mode");

                    // Adulterar ambos (name e underlyingType)
                    mode.enums[0].name = "TamperedModeName";
                    mode.enums[0].underlyingType = "byte";

                    Assert.Throws<EqualException>(() => AssertParity(csLines, model, checkAttributes: false));
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
        public void ParityValidator_Detects_ParameterModifierMismatch()
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
                    DumpAttribute = true
                };

                var tempDir = Path.Combine(Path.GetTempPath(), "dumpjson-tamper-parammod-" + Guid.NewGuid()) + Path.DirectorySeparatorChar;
                Directory.CreateDirectory(tempDir);

                try
                {
                    var decompiler = new Il2CppDecompiler(executor);
                    decompiler.Decompile(config, tempDir);

                    var csLines = File.ReadAllLines(Path.Combine(tempDir, "dump.cs"));
                    var jsonText = File.ReadAllText(Path.Combine(tempDir, "dump.json"));
                    var model = JsonSerializer.Deserialize<JsonDumpModel>(jsonText, JsonDumpGenerator.JsonOptions)!;

                    var allTypes = FlattenTypes(model.types).ToList();
                    var defaultHolder = allTypes.First(t => t.fullName == "Repro.DefaultParamHolder");
                    var modMethod = defaultHolder.methods.First(m => m.name == "ModifyParams");

                    // Strip "ref " modifier from first parameter
                    modMethod.parameters[0].type = "int";

                    Assert.Throws<EqualException>(() => AssertParity(csLines, model, checkAttributes: false));
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
        public void ParityValidator_Detects_MissingTypeAttribute()
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
                    DumpAttribute = true
                };

                var tempDir = Path.Combine(Path.GetTempPath(), "dumpjson-tamper-typeattr-" + Guid.NewGuid()) + Path.DirectorySeparatorChar;
                Directory.CreateDirectory(tempDir);

                try
                {
                    var decompiler = new Il2CppDecompiler(executor);
                    decompiler.Decompile(config, tempDir);

                    var csLines = File.ReadAllLines(Path.Combine(tempDir, "dump.cs"));
                    var jsonText = File.ReadAllText(Path.Combine(tempDir, "dump.json"));
                    var model = JsonSerializer.Deserialize<JsonDumpModel>(jsonText, JsonDumpGenerator.JsonOptions)!;

                    var allTypes = FlattenTypes(model.types).ToList();
                    var outer = allTypes.First(t => t.fullName == "Repro.Outer");

                    // Remove [Serializable] attribute from Outer
                    outer.attributes.Clear();

                    var ex = Assert.Throws<TrueException>(() => AssertParity(csLines, model, checkAttributes: true));
                    Assert.Contains("Repro.Outer", ex.UserMessage);
                    Assert.Contains("[Serializable]", ex.UserMessage);
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
        public void Config_DumpAttributeFalse_ProducesEmptyAttributes_AndOmitsSerializable()
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

                var tempDir = Path.Combine(Path.GetTempPath(), "dumpjson-attr-disabled-" + Guid.NewGuid()) + Path.DirectorySeparatorChar;
                Directory.CreateDirectory(tempDir);

                try
                {
                    var decompiler = new Il2CppDecompiler(executor);
                    decompiler.Decompile(config, tempDir);

                    var csText = File.ReadAllText(Path.Combine(tempDir, "dump.cs"));
                    var jsonText = File.ReadAllText(Path.Combine(tempDir, "dump.json"));
                    var model = JsonSerializer.Deserialize<JsonDumpModel>(jsonText, JsonDumpGenerator.JsonOptions)!;

                    Assert.DoesNotContain("[Serializable]", csText);

                    var allTypes = FlattenTypes(model.types).ToList();
                    foreach (var type in allTypes)
                    {
                        Assert.Empty(type.attributes);
                        foreach (var fld in type.fields)
                            Assert.Empty(fld.attributes);
                        foreach (var prop in type.properties)
                            Assert.Empty(prop.attributes);
                        foreach (var meth in type.methods)
                            Assert.Empty(meth.attributes);
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
        public void CustomAttributes_TypeAndPropertyAttributes_PopulatedInJsonDumpMatchingDumpCs()
        {
            // Verify custom attributes on Type and Property when DumpAttribute is true
            var bytes = new byte[8192];
            BitConverter.GetBytes(0xFAB11BAFu).CopyTo(bytes, 0);
            BitConverter.GetBytes(24).CopyTo(bytes, 4); // version 24
            var m = new Metadata(new MemoryStream(bytes))
            {
                Version = 24
            };
            m.header.stringOffset = 512;
            m.header.stringSize = 1024;

            uint strPos = 0;
            uint WriteStr(string s)
            {
                var ret = strPos;
                var b = Encoding.UTF8.GetBytes(s + "\0");
                Buffer.BlockCopy(b, 0, bytes, (int)(m.header.stringOffset + strPos), b.Length);
                strPos += (uint)b.Length;
                return ret;
            }

            var typeDef = new Il2CppTypeDefinition
            {
                nameIndex = WriteStr("AttributedClass"),
                namespaceIndex = WriteStr("TestNamespace"),
                byvalTypeIndex = 0,
                propertyStart = 0,
                property_count = 1,
                declaringTypeIndex = -1,
                parentIndex = -1,
                genericContainerIndex = -1,
                customAttributeIndex = 0,
                token = 0x02000001,
                flags = TYPE_ATTRIBUTE_PUBLIC | TYPE_ATTRIBUTE_SERIALIZABLE
            };

            var attrTypeDef = new Il2CppTypeDefinition
            {
                nameIndex = WriteStr("CustomFlagAttribute"),
                namespaceIndex = WriteStr("TestNamespace"),
                byvalTypeIndex = 1,
                declaringTypeIndex = -1,
                parentIndex = -1,
                genericContainerIndex = -1
            };

            var propDef = new Il2CppPropertyDefinition
            {
                nameIndex = WriteStr("AttributedProperty"),
                customAttributeIndex = 1,
                token = 0x17000001,
                get = -1,
                set = -1
            };

            m.typeDefs = new[] { typeDef, attrTypeDef };
            m.propertyDefs = new[] { propDef };
            m.fieldDefs = Array.Empty<Il2CppFieldDefinition>();
            m.methodDefs = Array.Empty<Il2CppMethodDefinition>();
            m.parameterDefs = Array.Empty<Il2CppParameterDefinition>();

            var imageDef = new Il2CppImageDefinition
            {
                nameIndex = WriteStr("TestAssembly.dll"),
                typeStart = 0,
                typeCount = 2
            };
            m.imageDefs = new[] { imageDef };

            m.attributeTypeRanges = new[]
            {
                new Il2CppCustomAttributeTypeRange { token = 0x02000001, start = 0, count = 1 },
                new Il2CppCustomAttributeTypeRange { token = 0x17000001, start = 1, count = 1 }
            };
            m.attributeTypes = new[] { 1, 1 };

            var dic = new Dictionary<Il2CppImageDefinition, Dictionary<uint, int>>
            {
                {
                    imageDef,
                    new Dictionary<uint, int>
                    {
                        { 0x02000001, 0 },
                        { 0x17000001, 1 }
                    }
                }
            };
            typeof(Metadata).GetField("attributeTypeRangesDic", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(m, dic);

            var il = new WebAssemblyMemory(new MemoryStream(new byte[256]), 0)
            {
                Version = 24,
                methodPointers = Array.Empty<ulong>(),
                types = new[]
                {
                    new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_CLASS, data = new Il2CppType.Union { dummy = 0 } },
                    new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_CLASS, data = new Il2CppType.Union { dummy = 1 } }
                }
            };

            using (m)
            using (il)
            {
                var executor = new Il2CppExecutor(m, il);
                executor.customAttributeGenerators = new ulong[] { 0x1000, 0x2000 };

                var config = new Config
                {
                    GenerateJsonDump = true,
                    DumpProperty = true,
                    DumpAttribute = true
                };

                var tempDir = Path.Combine(Path.GetTempPath(), "dumpjson-custattr-" + Guid.NewGuid()) + Path.DirectorySeparatorChar;
                Directory.CreateDirectory(tempDir);

                try
                {
                    var decompiler = new Il2CppDecompiler(executor);
                    decompiler.Decompile(config, tempDir);

                    var csText = File.ReadAllText(Path.Combine(tempDir, "dump.cs"));
                    var jsonText = File.ReadAllText(Path.Combine(tempDir, "dump.json"));
                    var model = JsonSerializer.Deserialize<JsonDumpModel>(jsonText, JsonDumpGenerator.JsonOptions)!;

                    var targetType = model.types.First(t => t.fullName == "TestNamespace.AttributedClass");

                    // Type attributes: custom attribute + [Serializable]
                    Assert.Contains(targetType.attributes, a => a.Contains("CustomFlagAttribute"));
                    Assert.Contains("[Serializable]", targetType.attributes);
                    Assert.Contains("CustomFlagAttribute", csText);
                    Assert.Contains("[Serializable]", csText);

                    // Property attributes: custom attribute
                    var targetProp = targetType.properties.First(p => p.name == "AttributedProperty");
                    Assert.Contains(targetProp.attributes, a => a.Contains("CustomFlagAttribute"));
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
