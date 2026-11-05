#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using static Il2CppDumper.Il2CppConstants;

namespace Il2CppDumper
{
    public class JsonDumpModel
    {
        public List<JsonDumpType> types { get; set; } = new();
    }

    public class JsonDumpType
    {
        public string fullName { get; set; } = string.Empty;
        public List<string> bases { get; set; } = new();
        public List<string> attributes { get; set; } = new();
        public List<JsonDumpField> fields { get; set; } = new();
        public List<JsonDumpProperty> properties { get; set; } = new();
        public List<JsonDumpMethod> methods { get; set; } = new();
        public List<JsonDumpEnum> enums { get; set; } = new();
        public List<JsonDumpType> nested { get; set; } = new();
    }

    public class JsonDumpField
    {
        public string name { get; set; } = string.Empty;
        public string type { get; set; } = string.Empty;
        public string? offset { get; set; }
        public bool isStatic { get; set; }
        public List<string> attributes { get; set; } = new();
    }

    public class JsonDumpProperty
    {
        public string name { get; set; } = string.Empty;
        public string type { get; set; } = string.Empty;
        public List<string> attributes { get; set; } = new();
        public string? getter { get; set; }
        public string? setter { get; set; }
    }

    public class JsonDumpMethod
    {
        public string name { get; set; } = string.Empty;
        public string returnType { get; set; } = string.Empty;
        public List<JsonDumpParameter> parameters { get; set; } = new();
        public bool isStatic { get; set; }
        public List<string> attributes { get; set; } = new();
    }

    public class JsonDumpParameter
    {
        public string name { get; set; } = string.Empty;
        public string type { get; set; } = string.Empty;
        public string? defaultValue { get; set; }
    }

    public class JsonDumpEnum
    {
        public string name { get; set; } = string.Empty;
        public string underlyingType { get; set; } = string.Empty;
        public Dictionary<string, object> constants { get; set; } = new();

        public object? GetConstantValue(string constantName)
        {
            if (!constants.TryGetValue(constantName, out var val))
            {
                return null;
            }

            if (val is JsonElement elem)
            {
                if (elem.ValueKind == JsonValueKind.Number)
                {
                    if (elem.TryGetInt64(out var l))
                        return l;
                    if (elem.TryGetUInt64(out var ul))
                        return ul;
                    if (elem.TryGetDouble(out var d))
                        return d;
                }
                if (elem.ValueKind == JsonValueKind.String)
                    return elem.GetString();
                if (elem.ValueKind == JsonValueKind.True)
                    return true;
                if (elem.ValueKind == JsonValueKind.False)
                    return false;
                if (elem.ValueKind == JsonValueKind.Null)
                    return null;
            }

            return val;
        }
    }

    public static class JsonDumpGenerator
    {
        public const string FileName = "dump.json";

        public static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        public static void Write(string outputDir, Il2CppExecutor executor, Config config)
        {
            var decompiler = new Il2CppDecompiler(executor);
            Write(outputDir, decompiler, executor, config);
        }

        public static void Write(string outputDir, Il2CppDecompiler decompiler, Il2CppExecutor executor, Config config)
        {
            var model = Create(decompiler, executor, config);
            var json = JsonSerializer.Serialize(model, JsonOptions);
            var targetPath = Path.Combine(outputDir, FileName);
            File.WriteAllText(targetPath, json, new UTF8Encoding(false));
        }

        public static JsonDumpModel Create(Il2CppExecutor executor, Config config)
        {
            var decompiler = new Il2CppDecompiler(executor);
            return Create(decompiler, executor, config);
        }

        public static JsonDumpModel Create(Il2CppDecompiler decompiler, Il2CppExecutor executor, Config config)
        {
            var metadata = executor.metadata;
            var il2Cpp = executor.il2Cpp;
            var model = new JsonDumpModel();

            var typeModelMap = new Dictionary<Il2CppTypeDefinition, JsonDumpType>();
            var nestedChildren = new HashSet<Il2CppTypeDefinition>();

            // 1. Create all type models
            foreach (var imageDef in metadata.imageDefs)
            {
                var typeEnd = imageDef.typeStart + imageDef.typeCount;
                for (var typeDefIndex = imageDef.typeStart; typeDefIndex < typeEnd && typeDefIndex < metadata.typeDefs.Length; typeDefIndex++)
                {
                    var typeDef = metadata.typeDefs[typeDefIndex];
                    var typeModel = CreateTypeModel(decompiler, executor, metadata, il2Cpp, config, imageDef, typeDef, typeDefIndex);
                    typeModelMap[typeDef] = typeModel;
                }
            }

            // 2. Link nested types and parent enums via metadata.nestedTypeIndices
            foreach (var imageDef in metadata.imageDefs)
            {
                var typeEnd = imageDef.typeStart + imageDef.typeCount;
                for (var typeDefIndex = imageDef.typeStart; typeDefIndex < typeEnd && typeDefIndex < metadata.typeDefs.Length; typeDefIndex++)
                {
                    var typeDef = metadata.typeDefs[typeDefIndex];
                    if (!typeModelMap.TryGetValue(typeDef, out var parentModel))
                        continue;

                    if (metadata.nestedTypeIndices != null && typeDef.nested_type_count > 0)
                    {
                        for (int n = 0; n < typeDef.nested_type_count; n++)
                        {
                            var nestedTypeIndex = typeDef.nestedTypesStart + n;
                            if (nestedTypeIndex >= 0 && nestedTypeIndex < metadata.nestedTypeIndices.Length)
                            {
                                var nestedIndex = metadata.nestedTypeIndices[nestedTypeIndex];
                                if (nestedIndex >= 0 && nestedIndex < metadata.typeDefs.Length)
                                {
                                    var childTypeDef = metadata.typeDefs[nestedIndex];
                                    if (typeModelMap.TryGetValue(childTypeDef, out var childModel))
                                    {
                                        if (!parentModel.nested.Contains(childModel))
                                        {
                                            parentModel.nested.Add(childModel);
                                            nestedChildren.Add(childTypeDef);
                                            if (childTypeDef.IsEnum)
                                            {
                                                var enumModel = CreateEnumModel(executor, metadata, il2Cpp, childTypeDef);
                                                if (!parentModel.enums.Any(e => e.name == enumModel.name))
                                                {
                                                    parentModel.enums.Add(enumModel);
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // 3. Child-to-parent linking via declaringTypeIndex (fallback/direct link)
            foreach (var (typeDef, typeModel) in typeModelMap)
            {
                if (typeDef.declaringTypeIndex != -1 && !nestedChildren.Contains(typeDef) && typeDef.declaringTypeIndex < il2Cpp.types.Length)
                {
                    var parentIl2CppType = il2Cpp.types[typeDef.declaringTypeIndex];
                    var parentTypeDef = executor.GetTypeDefinitionFromIl2CppType(parentIl2CppType);
                    if (parentTypeDef != null && typeModelMap.TryGetValue(parentTypeDef, out var parentModel))
                    {
                        if (!parentModel.nested.Contains(typeModel))
                        {
                            parentModel.nested.Add(typeModel);
                            nestedChildren.Add(typeDef);
                            if (typeDef.IsEnum)
                            {
                                var enumModel = CreateEnumModel(executor, metadata, il2Cpp, typeDef);
                                if (!parentModel.enums.Any(e => e.name == enumModel.name))
                                {
                                    parentModel.enums.Add(enumModel);
                                }
                            }
                        }
                    }
                }
            }

            // 4. Populate root types (all non-nested types)
            foreach (var (typeDef, typeModel) in typeModelMap)
            {
                if (!nestedChildren.Contains(typeDef))
                {
                    model.types.Add(typeModel);
                }
            }

            return model;
        }

        private static JsonDumpType CreateTypeModel(
            Il2CppDecompiler decompiler,
            Il2CppExecutor executor,
            Metadata metadata,
            Il2Cpp il2Cpp,
            Config config,
            Il2CppImageDefinition imageDef,
            Il2CppTypeDefinition typeDef,
            int typeDefIndex)
        {
            var fullName = executor.GetTypeDefName(typeDef, true, true);
            var extends = new List<string>();
            if (typeDef.parentIndex >= 0 && typeDef.parentIndex < il2Cpp.types.Length)
            {
                var parent = il2Cpp.types[typeDef.parentIndex];
                var parentName = executor.GetTypeName(parent, false, false);
                if (!typeDef.IsValueType && !typeDef.IsEnum && parentName != "object")
                {
                    extends.Add(parentName);
                }
            }
            if (typeDef.interfaces_count > 0 && metadata.interfaceIndices != null)
            {
                for (int i = 0; i < typeDef.interfaces_count; i++)
                {
                    var interfaceIndex = typeDef.interfacesStart + i;
                    if (interfaceIndex >= 0 && interfaceIndex < metadata.interfaceIndices.Length)
                    {
                        var typeIdx = metadata.interfaceIndices[interfaceIndex];
                        if (typeIdx >= 0 && typeIdx < il2Cpp.types.Length)
                        {
                            var @interface = il2Cpp.types[typeIdx];
                            extends.Add(executor.GetTypeName(@interface, false, false));
                        }
                    }
                }
            }

            var typeAttributes = new List<string>();
            if (config.DumpAttribute)
            {
                var typeOwner = metadata.GetStringFromIndex(typeDef.nameIndex);
                typeAttributes = decompiler.GetCustomAttributeStrings(imageDef, typeDef.customAttributeIndex, typeDef.token, typeOwner, false);
                if ((typeDef.flags & TYPE_ATTRIBUTE_SERIALIZABLE) != 0)
                {
                    typeAttributes.Add("[Serializable]");
                }
            }

            var typeModel = new JsonDumpType
            {
                fullName = fullName,
                bases = extends,
                attributes = typeAttributes
            };

            if (typeDef.IsEnum)
            {
                typeModel.enums.Add(CreateEnumModel(executor, metadata, il2Cpp, typeDef));
            }

            // Fields
            if (config.DumpField && typeDef.field_count > 0 && metadata.fieldDefs != null)
            {
                var fieldEnd = typeDef.fieldStart + typeDef.field_count;
                for (var i = typeDef.fieldStart; i < fieldEnd && i < metadata.fieldDefs.Length; ++i)
                {
                    var fieldDef = metadata.fieldDefs[i];
                    if (fieldDef.typeIndex < 0 || fieldDef.typeIndex >= il2Cpp.types.Length)
                        continue;

                    var fieldType = il2Cpp.types[fieldDef.typeIndex];
                    var isStatic = false;
                    var isConst = false;
                    if ((fieldType.attrs & FIELD_ATTRIBUTE_LITERAL) != 0)
                    {
                        isConst = true;
                    }
                    else
                    {
                        if ((fieldType.attrs & FIELD_ATTRIBUTE_STATIC) != 0)
                        {
                            isStatic = true;
                        }
                    }

                    var fieldName = metadata.GetStringFromIndex(fieldDef.nameIndex);
                    var fieldTypeName = executor.GetTypeName(fieldType, false, false);

                    string? offset = null;
                    if (config.DumpFieldOffset && !isConst)
                    {
                        var offsetVal = il2Cpp.GetFieldOffsetFromIndex(typeDefIndex, i - typeDef.fieldStart, i, typeDef.IsValueType, isStatic);
                        offset = $"0x{offsetVal:X}";
                    }

                    var attributes = new List<string>();
                    if (config.DumpAttribute)
                    {
                        var fieldOwner = $"{metadata.GetStringFromIndex(typeDef.nameIndex)}.{fieldName}";
                        attributes = decompiler.GetCustomAttributeStrings(imageDef, fieldDef.customAttributeIndex, fieldDef.token, fieldOwner, false);
                    }

                    typeModel.fields.Add(new JsonDumpField
                    {
                        name = fieldName,
                        type = fieldTypeName,
                        offset = offset,
                        isStatic = isStatic,
                        attributes = attributes
                    });
                }
            }

            // Properties
            if (config.DumpProperty && typeDef.property_count > 0 && metadata.propertyDefs != null)
            {
                var propertyEnd = typeDef.propertyStart + typeDef.property_count;
                for (var i = typeDef.propertyStart; i < propertyEnd && i < metadata.propertyDefs.Length; ++i)
                {
                    var propertyDef = metadata.propertyDefs[i];
                    var propName = metadata.GetStringFromIndex(propertyDef.nameIndex);
                    string propType = "void";
                    string? getter = null;
                    string? setter = null;

                    if (propertyDef.get >= 0)
                    {
                        var methodIndex = metadata.GetMethodIndexFromTypeDefinition(typeDefIndex, propertyDef.get);
                        if (methodIndex >= 0 && methodIndex < metadata.methodDefs.Length)
                        {
                            var methodDef = metadata.methodDefs[methodIndex];
                            getter = metadata.GetStringFromIndex(methodDef.nameIndex);
                            if (methodDef.returnType >= 0 && methodDef.returnType < il2Cpp.types.Length)
                            {
                                var propertyType = il2Cpp.types[methodDef.returnType];
                                propType = executor.GetTypeName(propertyType, false, false);
                            }
                        }
                    }
                    else if (propertyDef.set >= 0)
                    {
                        var methodIndex = metadata.GetMethodIndexFromTypeDefinition(typeDefIndex, propertyDef.set);
                        if (methodIndex >= 0 && methodIndex < metadata.methodDefs.Length)
                        {
                            var methodDef = metadata.methodDefs[methodIndex];
                            setter = metadata.GetStringFromIndex(methodDef.nameIndex);
                            if (methodDef.parameterCount > 0 && metadata.parameterDefs != null)
                            {
                                var paramIndex = methodDef.parameterStart + methodDef.parameterCount - 1;
                                if (paramIndex >= 0 && paramIndex < metadata.parameterDefs.Length)
                                {
                                    var parameterDef = metadata.parameterDefs[paramIndex];
                                    if (parameterDef.typeIndex >= 0 && parameterDef.typeIndex < il2Cpp.types.Length)
                                    {
                                        var propertyType = il2Cpp.types[parameterDef.typeIndex];
                                        propType = executor.GetTypeName(propertyType, false, false);
                                    }
                                }
                            }
                        }
                    }

                    if (propertyDef.get >= 0 && propertyDef.set >= 0)
                    {
                        var methodIndex = metadata.GetMethodIndexFromTypeDefinition(typeDefIndex, propertyDef.set);
                        if (methodIndex >= 0 && methodIndex < metadata.methodDefs.Length)
                        {
                            var methodDef = metadata.methodDefs[methodIndex];
                            setter = metadata.GetStringFromIndex(methodDef.nameIndex);
                        }
                    }

                    var propAttributes = new List<string>();
                    if (config.DumpAttribute)
                    {
                        var propOwner = $"{metadata.GetStringFromIndex(typeDef.nameIndex)}.{propName}";
                        propAttributes = decompiler.GetCustomAttributeStrings(imageDef, propertyDef.customAttributeIndex, propertyDef.token, propOwner, false);
                    }

                    typeModel.properties.Add(new JsonDumpProperty
                    {
                        name = propName,
                        type = propType,
                        attributes = propAttributes,
                        getter = getter,
                        setter = setter
                    });
                }
            }

            // Methods
            if (config.DumpMethod && typeDef.method_count > 0 && metadata.methodDefs != null)
            {
                for (var methodIndexInType = 0; methodIndexInType < typeDef.method_count; methodIndexInType++)
                {
                    var i = metadata.GetMethodIndexFromTypeDefinition(typeDefIndex, methodIndexInType);
                    if (i < 0 || i >= metadata.methodDefs.Length)
                        continue;

                    var methodDef = metadata.methodDefs[i];
                    var isStatic = (methodDef.flags & METHOD_ATTRIBUTE_STATIC) != 0;
                    string returnTypeName = "void";
                    if (methodDef.returnType >= 0 && methodDef.returnType < il2Cpp.types.Length)
                    {
                        var methodReturnType = il2Cpp.types[methodDef.returnType];
                        returnTypeName = executor.GetTypeName(methodReturnType, false, false);
                        if (methodReturnType.byref == 1)
                        {
                            returnTypeName = "ref " + returnTypeName;
                        }
                    }

                    var methodName = metadata.GetStringFromIndex(methodDef.nameIndex);
                    if (methodDef.genericContainerIndex >= 0 && metadata.genericContainers != null && methodDef.genericContainerIndex < metadata.genericContainers.Length)
                    {
                        var genericContainer = metadata.genericContainers[methodDef.genericContainerIndex];
                        methodName += executor.GetGenericContainerParams(genericContainer);
                    }

                    var parameters = new List<JsonDumpParameter>();
                    if (metadata.parameterDefs != null)
                    {
                        for (var j = 0; j < methodDef.parameterCount; ++j)
                        {
                            var paramIndex = methodDef.parameterStart + j;
                            if (paramIndex < 0 || paramIndex >= metadata.parameterDefs.Length)
                                continue;

                            var parameterDef = metadata.parameterDefs[paramIndex];
                            var paramName = metadata.GetStringFromIndex(parameterDef.nameIndex);
                            string paramTypeName = "";
                            if (parameterDef.typeIndex >= 0 && parameterDef.typeIndex < il2Cpp.types.Length)
                            {
                                var parameterType = il2Cpp.types[parameterDef.typeIndex];
                                var typeName = executor.GetTypeName(parameterType, false, false);
                                var prefix = "";
                                if (parameterType.byref == 1)
                                {
                                    if ((parameterType.attrs & PARAM_ATTRIBUTE_OUT) != 0 && (parameterType.attrs & PARAM_ATTRIBUTE_IN) == 0)
                                    {
                                        prefix = "out ";
                                    }
                                    else if ((parameterType.attrs & PARAM_ATTRIBUTE_OUT) == 0 && (parameterType.attrs & PARAM_ATTRIBUTE_IN) != 0)
                                    {
                                        prefix = "in ";
                                    }
                                    else
                                    {
                                        prefix = "ref ";
                                    }
                                }
                                else
                                {
                                    if ((parameterType.attrs & PARAM_ATTRIBUTE_IN) != 0)
                                    {
                                        prefix += "[In] ";
                                    }
                                    if ((parameterType.attrs & PARAM_ATTRIBUTE_OUT) != 0)
                                    {
                                        prefix += "[Out] ";
                                    }
                                }
                                paramTypeName = prefix + typeName;
                            }

                            string? defaultValue = null;
                            if (metadata.GetParameterDefaultValueFromIndex(paramIndex, out var parameterDefault) && parameterDefault.dataIndex != -1)
                            {
                                if (executor.TryGetDefaultValue(parameterDefault.typeIndex, parameterDefault.dataIndex, out var val))
                                {
                                    if (val is string str)
                                        defaultValue = $"\"{str.ToEscapedString()}\"";
                                    else if (val is char c)
                                        defaultValue = $"'\\x{(int)c:x}'";
                                    else if (val != null)
                                        defaultValue = $"{val}";
                                    else
                                        defaultValue = "null";
                                }
                                else
                                {
                                    defaultValue = $"/*Metadata offset 0x{val:X}*/";
                                }
                            }

                            parameters.Add(new JsonDumpParameter
                            {
                                name = paramName,
                                type = paramTypeName,
                                defaultValue = defaultValue
                            });
                        }
                    }

                    var attributes = new List<string>();
                    if (config.DumpAttribute)
                    {
                        var methodOwner = $"{metadata.GetStringFromIndex(typeDef.nameIndex)}.{metadata.GetStringFromIndex(methodDef.nameIndex)}";
                        attributes = decompiler.GetCustomAttributeStrings(imageDef, methodDef.customAttributeIndex, methodDef.token, methodOwner, false);
                    }

                    typeModel.methods.Add(new JsonDumpMethod
                    {
                        name = methodName,
                        returnType = returnTypeName,
                        parameters = parameters,
                        isStatic = isStatic,
                        attributes = attributes
                    });
                }
            }

            return typeModel;
        }

        public static JsonDumpEnum CreateEnumModel(Il2CppExecutor executor, Metadata metadata, Il2Cpp il2Cpp, Il2CppTypeDefinition typeDef)
        {
            var enumName = metadata.GetStringFromIndex(typeDef.nameIndex);
            var underlyingType = executor.GetEnumUnderlyingType(typeDef);
            var underlyingTypeName = underlyingType != null ? executor.GetTypeName(underlyingType, false, false) : "int";

            var constants = new Dictionary<string, object>();
            if (typeDef.field_count > 0 && metadata.fieldDefs != null)
            {
                var fieldEnd = typeDef.fieldStart + typeDef.field_count;
                for (var i = typeDef.fieldStart; i < fieldEnd && i < metadata.fieldDefs.Length; ++i)
                {
                    var fieldDef = metadata.fieldDefs[i];
                    if (fieldDef.typeIndex < 0 || fieldDef.typeIndex >= il2Cpp.types.Length)
                        continue;

                    var fieldType = il2Cpp.types[fieldDef.typeIndex];
                    if ((fieldType.attrs & FIELD_ATTRIBUTE_LITERAL) != 0)
                    {
                        var constName = metadata.GetStringFromIndex(fieldDef.nameIndex);
                        if (metadata.GetFieldDefaultValueFromIndex(i, out var fieldDefault) && fieldDefault.dataIndex != -1)
                        {
                            if (executor.TryGetDefaultValue(fieldDefault.typeIndex, fieldDefault.dataIndex, out var val))
                            {
                                constants[constName] = val;
                            }
                        }
                    }
                }
            }

            return new JsonDumpEnum
            {
                name = enumName,
                underlyingType = underlyingTypeName,
                constants = constants
            };
        }
    }
}
