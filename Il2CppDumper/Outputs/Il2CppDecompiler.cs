using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using static Il2CppDumper.Il2CppConstants;

namespace Il2CppDumper
{
    public class DecompilerLogger
    {
        private readonly Action<string> logWarningAction;

        public DecompilerLogger(Action<string> logWarningAction = null)
        {
            this.logWarningAction = logWarningAction ?? (msg => Console.WriteLine(msg));
        }

        public virtual void LogWarning(string message)
        {
            logWarningAction(message);
        }
    }

    public class Il2CppDecompiler
    {
        private readonly Il2CppExecutor executor;
        private readonly Metadata metadata;
        private readonly Il2Cpp il2Cpp;
        private readonly Dictionary<Il2CppMethodDefinition, string> methodModifiers;
        public DecompilerLogger log { get; set; } = new();
        public int CustomAttributeWrittenCount { get; private set; }
        public int CustomAttributeFailedCount { get; private set; }

        public Il2CppDecompiler(Il2CppExecutor il2CppExecutor)
        {
            executor = il2CppExecutor;
            metadata = il2CppExecutor.metadata;
            il2Cpp = il2CppExecutor.il2Cpp;
            methodModifiers = new();
        }

        public bool Decompile(Config config, string outputDir)
        {
            var success = true;
            CustomAttributeWrittenCount = 0;
            CustomAttributeFailedCount = 0;
            var writer = new StreamWriter(new FileStream(outputDir + "dump.cs", FileMode.Create), new UTF8Encoding(false));
            //dump image
            for (var imageIndex = 0; imageIndex < metadata.imageDefs.Length; imageIndex++)
            {
                var imageDef = metadata.imageDefs[imageIndex];
                writer.Write($"// Image {imageIndex}: {metadata.GetStringFromIndex(imageDef.nameIndex)} - {imageDef.typeStart}\n");
            }
            //dump type
            foreach (var imageDef in metadata.imageDefs)
            {
                try
                {
                    var imageName = metadata.GetStringFromIndex(imageDef.nameIndex);
                    var typeEnd = imageDef.typeStart + imageDef.typeCount;
                    for (int typeDefIndex = imageDef.typeStart; typeDefIndex < typeEnd; typeDefIndex++)
                    {
                        var typeDef = metadata.typeDefs[typeDefIndex];
                        var extends = new List<string>();
                        if (typeDef.parentIndex >= 0)
                        {
                            var parent = il2Cpp.types[typeDef.parentIndex];
                            var parentName = executor.GetTypeName(parent, false, false);
                            if (!typeDef.IsValueType && !typeDef.IsEnum && parentName != "object")
                            {
                                extends.Add(parentName);
                            }
                        }
                        if (typeDef.interfaces_count > 0)
                        {
                            for (int i = 0; i < typeDef.interfaces_count; i++)
                            {
                                var @interface = il2Cpp.types[metadata.interfaceIndices[typeDef.interfacesStart + i]];
                                extends.Add(executor.GetTypeName(@interface, false, false));
                            }
                        }
                        writer.Write($"\n// Namespace: {metadata.GetStringFromIndex(typeDef.namespaceIndex)}\n");
                        if (config.DumpAttribute)
                        {
                            writer.Write(GetCustomAttribute(imageDef, typeDef.customAttributeIndex, typeDef.token, owner: metadata.GetStringFromIndex(typeDef.nameIndex)));
                        }
                        if (config.DumpAttribute && (typeDef.flags & TYPE_ATTRIBUTE_SERIALIZABLE) != 0)
                            writer.Write("[Serializable]\n");
                        var visibility = typeDef.flags & TYPE_ATTRIBUTE_VISIBILITY_MASK;
                        switch (visibility)
                        {
                            case TYPE_ATTRIBUTE_PUBLIC:
                            case TYPE_ATTRIBUTE_NESTED_PUBLIC:
                                writer.Write("public ");
                                break;
                            case TYPE_ATTRIBUTE_NOT_PUBLIC:
                            case TYPE_ATTRIBUTE_NESTED_FAM_AND_ASSEM:
                            case TYPE_ATTRIBUTE_NESTED_ASSEMBLY:
                                writer.Write("internal ");
                                break;
                            case TYPE_ATTRIBUTE_NESTED_PRIVATE:
                                writer.Write("private ");
                                break;
                            case TYPE_ATTRIBUTE_NESTED_FAMILY:
                                writer.Write("protected ");
                                break;
                            case TYPE_ATTRIBUTE_NESTED_FAM_OR_ASSEM:
                                writer.Write("protected internal ");
                                break;
                        }
                        if ((typeDef.flags & TYPE_ATTRIBUTE_ABSTRACT) != 0 && (typeDef.flags & TYPE_ATTRIBUTE_SEALED) != 0)
                            writer.Write("static ");
                        else if ((typeDef.flags & TYPE_ATTRIBUTE_INTERFACE) == 0 && (typeDef.flags & TYPE_ATTRIBUTE_ABSTRACT) != 0)
                            writer.Write("abstract ");
                        else if (!typeDef.IsValueType && !typeDef.IsEnum && (typeDef.flags & TYPE_ATTRIBUTE_SEALED) != 0)
                            writer.Write("sealed ");
                        if ((typeDef.flags & TYPE_ATTRIBUTE_INTERFACE) != 0)
                            writer.Write("interface ");
                        else if (typeDef.IsEnum)
                            writer.Write("enum ");
                        else if (typeDef.IsValueType)
                            writer.Write("struct ");
                        else
                            writer.Write("class ");
                        var typeName = executor.GetTypeDefName(typeDef, false, true);
                        writer.Write($"{typeName}");
                        if (extends.Count > 0)
                            writer.Write($" : {string.Join(", ", extends)}");
                        if (config.DumpTypeDefIndex)
                            writer.Write($" // TypeDefIndex: {typeDefIndex}\n{{");
                        else
                            writer.Write("\n{");
                        //dump field
                        if (config.DumpField && typeDef.field_count > 0)
                        {
                            writer.Write("\n\t// Fields\n");
                            var fieldEnd = typeDef.fieldStart + typeDef.field_count;
                            for (var i = typeDef.fieldStart; i < fieldEnd; ++i)
                            {
                                var fieldDef = metadata.fieldDefs[i];
                                var fieldType = il2Cpp.types[fieldDef.typeIndex];
                                var isStatic = false;
                                var isConst = false;
                                if (config.DumpAttribute)
                                {
                                    writer.Write(GetCustomAttribute(imageDef, fieldDef.customAttributeIndex, fieldDef.token, "\t", $"{metadata.GetStringFromIndex(typeDef.nameIndex)}.{metadata.GetStringFromIndex(fieldDef.nameIndex)}"));
                                }
                                writer.Write("\t");
                                var access = fieldType.attrs & FIELD_ATTRIBUTE_FIELD_ACCESS_MASK;
                                switch (access)
                                {
                                    case FIELD_ATTRIBUTE_PRIVATE:
                                        writer.Write("private ");
                                        break;
                                    case FIELD_ATTRIBUTE_PUBLIC:
                                        writer.Write("public ");
                                        break;
                                    case FIELD_ATTRIBUTE_FAMILY:
                                        writer.Write("protected ");
                                        break;
                                    case FIELD_ATTRIBUTE_ASSEMBLY:
                                    case FIELD_ATTRIBUTE_FAM_AND_ASSEM:
                                        writer.Write("internal ");
                                        break;
                                    case FIELD_ATTRIBUTE_FAM_OR_ASSEM:
                                        writer.Write("protected internal ");
                                        break;
                                }
                                if ((fieldType.attrs & FIELD_ATTRIBUTE_LITERAL) != 0)
                                {
                                    isConst = true;
                                    writer.Write("const ");
                                }
                                else
                                {
                                    if ((fieldType.attrs & FIELD_ATTRIBUTE_STATIC) != 0)
                                    {
                                        isStatic = true;
                                        writer.Write("static ");
                                    }
                                    if ((fieldType.attrs & FIELD_ATTRIBUTE_INIT_ONLY) != 0)
                                    {
                                        writer.Write("readonly ");
                                    }
                                }
                                writer.Write($"{executor.GetTypeName(fieldType, false, false)} {metadata.GetStringFromIndex(fieldDef.nameIndex)}");
                                if (metadata.GetFieldDefaultValueFromIndex(i, out var fieldDefaultValue) && fieldDefaultValue.dataIndex != -1)
                                {
                                    if (executor.TryGetDefaultValue(fieldDefaultValue.typeIndex, fieldDefaultValue.dataIndex, out var value))
                                    {
                                        writer.Write($" = ");
                                        if (value is string str)
                                        {
                                            writer.Write($"\"{str.ToEscapedString()}\"");
                                        }
                                        else if (value is char c)
                                        {
                                            var v = (int)c;
                                            writer.Write($"'\\x{v:x}'");
                                        }
                                        else if (value != null)
                                        {
                                            writer.Write($"{value}");
                                        }
                                        else
                                        {
                                            writer.Write("null");
                                        }
                                    }
                                    else
                                    {
                                        writer.Write($" /*Metadata offset 0x{value:X}*/");
                                    }
                                }
                                if (config.DumpFieldOffset && !isConst)
                                    writer.Write("; // 0x{0:X}\n", il2Cpp.GetFieldOffsetFromIndex(typeDefIndex, i - typeDef.fieldStart, i, typeDef.IsValueType, isStatic));
                                else
                                    writer.Write(";\n");
                            }
                        }
                        //dump property
                        if (config.DumpProperty && typeDef.property_count > 0)
                        {
                            writer.Write("\n\t// Properties\n");
                            var propertyEnd = typeDef.propertyStart + typeDef.property_count;
                            for (var i = typeDef.propertyStart; i < propertyEnd; ++i)
                            {
                                var propertyDef = metadata.propertyDefs[i];
                                if (config.DumpAttribute)
                                {
                                    writer.Write(GetCustomAttribute(imageDef, propertyDef.customAttributeIndex, propertyDef.token, "\t", $"{metadata.GetStringFromIndex(typeDef.nameIndex)}.{metadata.GetStringFromIndex(propertyDef.nameIndex)}"));
                                }
                                writer.Write("\t");
                                if (propertyDef.get >= 0)
                                {
                                    var methodDef = metadata.methodDefs[metadata.GetMethodIndexFromTypeDefinition(typeDefIndex, propertyDef.get)];
                                    writer.Write(GetModifiers(methodDef));
                                    var propertyType = il2Cpp.types[methodDef.returnType];
                                    writer.Write($"{executor.GetTypeName(propertyType, false, false)} {metadata.GetStringFromIndex(propertyDef.nameIndex)} {{ ");
                                }
                                else if (propertyDef.set >= 0)
                                {
                                    var methodDef = metadata.methodDefs[metadata.GetMethodIndexFromTypeDefinition(typeDefIndex, propertyDef.set)];
                                    writer.Write(GetModifiers(methodDef));
                                    if (methodDef.parameterCount > 0)
                                    {
                                        var parameterDef = metadata.parameterDefs[methodDef.parameterStart + methodDef.parameterCount - 1];
                                        var propertyType = il2Cpp.types[parameterDef.typeIndex];
                                        writer.Write($"{executor.GetTypeName(propertyType, false, false)} {metadata.GetStringFromIndex(propertyDef.nameIndex)} {{ ");
                                    }
                                    else
                                    {
                                        writer.Write($"void {metadata.GetStringFromIndex(propertyDef.nameIndex)} {{ ");
                                    }
                                }
                                if (propertyDef.get >= 0)
                                    writer.Write("get; ");
                                if (propertyDef.set >= 0)
                                    writer.Write("set; ");
                                writer.Write("}");
                                writer.Write("\n");
                            }
                        }
                        //dump method
                        if (config.DumpMethod && typeDef.method_count > 0)
                        {
                            writer.Write("\n\t// Methods\n");
                            for (var methodIndexInType = 0; methodIndexInType < typeDef.method_count; methodIndexInType++)
                            {
                                var i = metadata.GetMethodIndexFromTypeDefinition(typeDefIndex, methodIndexInType);
                                writer.Write("\n");
                                var methodDef = metadata.methodDefs[i];
                                var isAbstract = (methodDef.flags & METHOD_ATTRIBUTE_ABSTRACT) != 0;
                                if (config.DumpAttribute)
                                {
                                    writer.Write(GetCustomAttribute(imageDef, methodDef.customAttributeIndex, methodDef.token, "\t", $"{metadata.GetStringFromIndex(typeDef.nameIndex)}.{metadata.GetStringFromIndex(methodDef.nameIndex)}"));
                                }
                                if (config.DumpMethodOffset)
                                {
                                    var methodPointer = il2Cpp.GetMethodPointer(imageName, methodDef);
                                    if (!isAbstract && methodPointer > 0)
                                    {
                                        var fixedMethodPointer = il2Cpp.GetRVA(methodPointer);
                                        writer.Write("\t// RVA: 0x{0:X} Offset: 0x{1:X} VA: 0x{2:X}", fixedMethodPointer, il2Cpp.MapVATR(methodPointer), methodPointer);
                                    }
                                    else
                                    {
                                        writer.Write("\t// RVA: -1 Offset: -1");
                                    }
                                    if (methodDef.slot != ushort.MaxValue)
                                    {
                                        writer.Write(" Slot: {0}", methodDef.slot);
                                    }
                                    writer.Write("\n");
                                }
                                writer.Write("\t");
                                writer.Write(GetModifiers(methodDef));
                                var methodReturnType = il2Cpp.types[methodDef.returnType];
                                var methodName = metadata.GetStringFromIndex(methodDef.nameIndex);
                                if (methodDef.genericContainerIndex >= 0)
                                {
                                    var genericContainer = metadata.genericContainers[methodDef.genericContainerIndex];
                                    methodName += executor.GetGenericContainerParams(genericContainer);
                                }
                                if (methodReturnType.byref == 1)
                                {
                                    writer.Write("ref ");
                                }
                                writer.Write($"{executor.GetTypeName(methodReturnType, false, false)} {methodName}(");
                                var parameterStrs = new List<string>();
                                for (var j = 0; j < methodDef.parameterCount; ++j)
                                {
                                    var parameterStr = "";
                                    var parameterDef = metadata.parameterDefs[methodDef.parameterStart + j];
                                    var parameterName = metadata.GetStringFromIndex(parameterDef.nameIndex);
                                    var parameterType = il2Cpp.types[parameterDef.typeIndex];
                                    var parameterTypeName = executor.GetTypeName(parameterType, false, false);
                                    if (parameterType.byref == 1)
                                    {
                                        if ((parameterType.attrs & PARAM_ATTRIBUTE_OUT) != 0 && (parameterType.attrs & PARAM_ATTRIBUTE_IN) == 0)
                                        {
                                            parameterStr += "out ";
                                        }
                                        else if ((parameterType.attrs & PARAM_ATTRIBUTE_OUT) == 0 && (parameterType.attrs & PARAM_ATTRIBUTE_IN) != 0)
                                        {
                                            parameterStr += "in ";
                                        }
                                        else
                                        {
                                            parameterStr += "ref ";
                                        }
                                    }
                                    else
                                    {
                                        if ((parameterType.attrs & PARAM_ATTRIBUTE_IN) != 0)
                                        {
                                            parameterStr += "[In] ";
                                        }
                                        if ((parameterType.attrs & PARAM_ATTRIBUTE_OUT) != 0)
                                        {
                                            parameterStr += "[Out] ";
                                        }
                                    }
                                    parameterStr += $"{parameterTypeName} {parameterName}";
                                    if (metadata.GetParameterDefaultValueFromIndex(methodDef.parameterStart + j, out var parameterDefault) && parameterDefault.dataIndex != -1)
                                    {
                                        if (executor.TryGetDefaultValue(parameterDefault.typeIndex, parameterDefault.dataIndex, out var value))
                                        {
                                            parameterStr += " = ";
                                            if (value is string str)
                                            {
                                                parameterStr += $"\"{str.ToEscapedString()}\"";
                                            }
                                            else if (value is char c)
                                            {
                                                var v = (int)c;
                                                parameterStr += $"'\\x{v:x}'";
                                            }
                                            else if (value != null)
                                            {
                                                parameterStr += $"{value}";
                                            }
                                            else
                                            {
                                                parameterStr += "null";
                                            }
                                        }
                                        else
                                        {
                                            parameterStr += $" /*Metadata offset 0x{value:X}*/";
                                        }
                                    }
                                    parameterStrs.Add(parameterStr);
                                }
                                writer.Write(string.Join(", ", parameterStrs));
                                if (isAbstract)
                                {
                                    writer.Write(");\n");
                                }
                                else
                                {
                                    writer.Write(") { }\n");
                                }

                                if (il2Cpp.methodDefinitionMethodSpecs.TryGetValue(i, out var methodSpecs))
                                {
                                    writer.Write("\t/* GenericInstMethod :\n");
                                    var groups = methodSpecs.GroupBy(x => il2Cpp.methodSpecGenericMethodPointers[x]);
                                    foreach (var group in groups)
                                    {
                                        writer.Write("\t|\n");
                                        var genericMethodPointer = group.Key;
                                        if (genericMethodPointer > 0)
                                        {
                                            var fixedPointer = il2Cpp.GetRVA(genericMethodPointer);
                                            writer.Write($"\t|-RVA: 0x{fixedPointer:X} Offset: 0x{il2Cpp.MapVATR(genericMethodPointer):X} VA: 0x{genericMethodPointer:X}\n");
                                        }
                                        else
                                        {
                                            writer.Write("\t|-RVA: -1 Offset: -1\n");
                                        }
                                        foreach (var methodSpec in group)
                                        {
                                            (var methodSpecTypeName, var methodSpecMethodName) = executor.GetMethodSpecName(methodSpec);
                                            writer.Write($"\t|-{methodSpecTypeName}.{methodSpecMethodName}\n");
                                        }
                                    }
                                    writer.Write("\t*/\n");
                                }
                            }
                        }
                        writer.Write("}\n");
                    }
                }
                catch (Exception e)
                {
                    success = false;
                    Console.WriteLine("ERROR: Some errors in dumping");
                    writer.Write("/*");
                    writer.Write(e);
                    writer.Write("*/\n}\n");
                }
            }
            if (config.DumpAttribute && CustomAttributeFailedCount > 0)
            {
                log.LogWarning($"WARNING: Custom attribute decoding completed with {CustomAttributeFailedCount} failure(s) ({CustomAttributeWrittenCount} written, {CustomAttributeFailedCount} skipped).");
            }
            writer.Close();
            return success;
        }

        public string GetCustomAttribute(Il2CppImageDefinition imageDef, int customAttributeIndex, uint token, string padding = "", string owner = null)
        {
            if (il2Cpp.Version < 21)
                return string.Empty;
            var attributeIndex = metadata.GetCustomAttributeIndex(imageDef, customAttributeIndex, token);
            if (attributeIndex >= 0)
            {
                owner ??= ResolveOwnerFromToken(token);
                if (il2Cpp.Version < 29)
                {
                    var methodPointer = executor.customAttributeGenerators[attributeIndex];
                    var fixedMethodPointer = il2Cpp.GetRVA(methodPointer);
                    var attributeTypeRange = metadata.attributeTypeRanges[attributeIndex];
                    var sb = new StringBuilder();
                    for (var i = 0; i < attributeTypeRange.count; i++)
                    {
                        var typeIndex = metadata.attributeTypes[attributeTypeRange.start + i];
                        sb.AppendFormat("{0}[{1}] // RVA: 0x{2:X} Offset: 0x{3:X} VA: 0x{4:X}\n",
                            padding,
                            executor.GetTypeName(il2Cpp.types[typeIndex], false, false),
                            fixedMethodPointer,
                            il2Cpp.MapVATR(methodPointer),
                            methodPointer);
                    }
                    return sb.ToString();
                }
                else
                {
                    try
                    {
                        var startRange = metadata.attributeDataRanges[attributeIndex];
                        var endRange = metadata.attributeDataRanges[attributeIndex + 1];
                        metadata.Position = (ulong)(metadata.Version >= 38
                            ? metadata.header.attributeData.offset + startRange.startOffset
                            : metadata.header.attributeDataOffset + startRange.startOffset);
                        var buff = metadata.ReadBytes((int)(endRange.startOffset - startRange.startOffset));
                        var reader = new CustomAttributeDataReader(executor, buff);
                        if (reader.Count == 0)
                        {
                            return string.Empty;
                        }
                        var sb = new StringBuilder();
                        for (var i = 0; i < reader.Count; i++)
                        {
                            var attributeTypeName = reader.GetAttributeTypeName(i);
                            try
                            {
                                var attributeData = reader.GetStringCustomAttributeData();
                                sb.Append(padding);
                                sb.Append(attributeData);
                                sb.Append('\n');
                                CustomAttributeWrittenCount++;
                            }
                            catch (Exception ex)
                            {
                                CustomAttributeFailedCount++;
                                reader.AdvanceToNextAttribute(i + 1);
                                log.LogWarning($"WARNING: Failed to decode custom attribute '{attributeTypeName}' on '{owner}' (token 0x{token:X8}): {ex.Message}");
                            }


                        }
                        return sb.ToString();
                    }
                    catch (Exception ex)
                    {
                        CustomAttributeFailedCount++;
                        log.LogWarning($"WARNING: Failed to read custom attribute data on '{owner}' (token 0x{token:X8}): {ex.Message}");
                        return string.Empty;
                    }
                }
            }
            else
            {
                return string.Empty;
            }
        }

        private string ResolveOwnerFromToken(uint token)
        {
            var table = token & 0xFF000000;
            var index = (int)(token & 0x00FFFFFF) - 1;
            try
            {
                switch (table)
                {
                    case 0x02000000: // TypeDef
                        if (index >= 0 && index < metadata.typeDefs.Length)
                            return metadata.GetStringFromIndex(metadata.typeDefs[index].nameIndex);
                        break;
                    case 0x04000000: // FieldDef
                        if (index >= 0 && index < metadata.fieldDefs.Length)
                            return metadata.GetStringFromIndex(metadata.fieldDefs[index].nameIndex);
                        break;
                    case 0x06000000: // MethodDef
                        if (index >= 0 && index < metadata.methodDefs.Length)
                            return metadata.GetStringFromIndex(metadata.methodDefs[index].nameIndex);
                        break;
                    case 0x17000000: // PropertyDef
                        if (index >= 0 && index < metadata.propertyDefs.Length)
                            return metadata.GetStringFromIndex(metadata.propertyDefs[index].nameIndex);
                        break;
                }
            }
            catch
            {
                // ignore
            }
            return $"token 0x{token:X8}";
        }

        public string GetModifiers(Il2CppMethodDefinition methodDef)
        {
            if (methodModifiers.TryGetValue(methodDef, out string str))
                return str;
            var access = methodDef.flags & METHOD_ATTRIBUTE_MEMBER_ACCESS_MASK;
            switch (access)
            {
                case METHOD_ATTRIBUTE_PRIVATE:
                    str += "private ";
                    break;
                case METHOD_ATTRIBUTE_PUBLIC:
                    str += "public ";
                    break;
                case METHOD_ATTRIBUTE_FAMILY:
                    str += "protected ";
                    break;
                case METHOD_ATTRIBUTE_ASSEM:
                case METHOD_ATTRIBUTE_FAM_AND_ASSEM:
                    str += "internal ";
                    break;
                case METHOD_ATTRIBUTE_FAM_OR_ASSEM:
                    str += "protected internal ";
                    break;
            }
            if ((methodDef.flags & METHOD_ATTRIBUTE_STATIC) != 0)
                str += "static ";
            if ((methodDef.flags & METHOD_ATTRIBUTE_ABSTRACT) != 0)
            {
                str += "abstract ";
                if ((methodDef.flags & METHOD_ATTRIBUTE_VTABLE_LAYOUT_MASK) == METHOD_ATTRIBUTE_REUSE_SLOT)
                    str += "override ";
            }
            else if ((methodDef.flags & METHOD_ATTRIBUTE_FINAL) != 0)
            {
                if ((methodDef.flags & METHOD_ATTRIBUTE_VTABLE_LAYOUT_MASK) == METHOD_ATTRIBUTE_REUSE_SLOT)
                    str += "sealed override ";
            }
            else if ((methodDef.flags & METHOD_ATTRIBUTE_VIRTUAL) != 0)
            {
                if ((methodDef.flags & METHOD_ATTRIBUTE_VTABLE_LAYOUT_MASK) == METHOD_ATTRIBUTE_NEW_SLOT)
                    str += "virtual ";
                else
                    str += "override ";
            }
            if ((methodDef.flags & METHOD_ATTRIBUTE_PINVOKE_IMPL) != 0)
                str += "extern ";
            methodModifiers.Add(methodDef, str);
            return str;
        }
    }
}
