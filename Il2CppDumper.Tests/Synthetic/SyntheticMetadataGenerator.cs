using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Il2CppDumper;

namespace Il2CppDumper.Tests.Synthetic
{
    public static class SyntheticMetadataGenerator
    {
        private const uint SanityMagic = 0xFAB11BAFu;

        public static byte[] Generate(
            MetadataVersionFamily family,
            int seed = 42,
            MetadataCorruptionType corruption = MetadataCorruptionType.None)
        {
            var version = (int)family;
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);

            // Deterministic variations based on seed
            var rng = new Random(seed);
            var seedSuffix = seed == 42 ? string.Empty : $"_{seed}";

            // Build string table
            var stringBuilder = new StringTableBuilder();
            var stringOffsetMap = new Dictionary<string, uint>();

            uint GetOrAddString(string val)
            {
                if (!stringOffsetMap.TryGetValue(val, out var offset))
                {
                    offset = stringBuilder.Add(val);
                    stringOffsetMap[val] = offset;
                }
                return offset;
            }

            GetOrAddString(string.Empty);
            var asmName = GetOrAddString("SyntheticAssembly" + seedSuffix);
            var imgName = GetOrAddString("SyntheticImage.dll");
            var nsName = GetOrAddString("SyntheticNamespace");
            var clsName = GetOrAddString("SyntheticClass");
            var methodName = GetOrAddString("SyntheticMethod");
            var fieldName = GetOrAddString("syntheticField");
            var enumName = GetOrAddString("SyntheticEnum");
            var valueFieldName = GetOrAddString("value__");
            var optionAName = GetOrAddString("OptionA");
            var paramName = GetOrAddString("param1");

            var stringBytes = stringBuilder.ToArray();

            // String literal payload
            var literalText = "HelloSynthetic" + seedSuffix;
            var literalDataBytes = Encoding.UTF8.GetBytes(literalText);

            // Default value data (int32 = 1)
            var defaultValueData = BitConverter.GetBytes(1);

            // Construct payload sections
            byte[] stringLiteralsBytes;
            byte[] stringLiteralDataBytes;
            byte[] fieldDefaultValuesBytes;
            byte[] parameterDefaultValuesBytes;
            byte[] parametersBytes;
            byte[] fieldsBytes;
            byte[] methodsBytes;
            byte[] typeDefsBytes;
            byte[] imagesBytes;
            byte[] assembliesBytes;

            // Corruption handling for string literal extent
            var literalDataIndex = 0;
            var literalLength = literalDataBytes.Length;
            if (corruption == MetadataCorruptionType.LiteralExtentPastSectionEnd)
            {
                literalDataIndex = literalDataBytes.Length + 50;
                literalLength = 1000;
            }

            if (version < 38)
            {
                // String literals
                using (var sMs = new MemoryStream())
                using (var sWriter = new BinaryWriter(sMs, Encoding.UTF8))
                {
                    if (version <= 31)
                    {
                        var lit = new Il2CppStringLiteral
                        {
                            length = (uint)literalLength,
                            dataIndex = literalDataIndex
                        };
                        WriteClass(sWriter, lit, version);
                    }
                    else
                    {
                        var lit = new Il2CppStringLiteral
                        {
                            dataIndex = literalDataIndex
                        };
                        WriteClass(sWriter, lit, version);
                    }
                    stringLiteralsBytes = sMs.ToArray();
                }

                stringLiteralDataBytes = literalDataBytes;

                // Field default value (OptionA = 1)
                using (var fdvMs = new MemoryStream())
                using (var fdvWriter = new BinaryWriter(fdvMs, Encoding.UTF8))
                {
                    var fdv = new Il2CppFieldDefaultValue
                    {
                        fieldIndex = 2, // OptionA is field index 2
                        typeIndex = 2,  // int
                        dataIndex = 0
                    };
                    WriteClass(fdvWriter, fdv, version);
                    fieldDefaultValuesBytes = fdvMs.ToArray();
                }

                parameterDefaultValuesBytes = Array.Empty<byte>();

                // Parameters (1 parameter)
                using (var pMs = new MemoryStream())
                using (var pWriter = new BinaryWriter(pMs, Encoding.UTF8))
                {
                    var p = new Il2CppParameterDefinition
                    {
                        nameIndex = paramName,
                        token = 0x08000001,
                        customAttributeIndex = -1,
                        typeIndex = 2
                    };
                    WriteClass(pWriter, p, version);
                    parametersBytes = pMs.ToArray();
                }

                // Fields (3 fields: 0=syntheticField, 1=value__, 2=OptionA)
                using (var fMs = new MemoryStream())
                using (var fWriter = new BinaryWriter(fMs, Encoding.UTF8))
                {
                    var f0 = new Il2CppFieldDefinition
                    {
                        nameIndex = fieldName,
                        typeIndex = 2,
                        customAttributeIndex = -1,
                        token = 0x04000001
                    };
                    var f1 = new Il2CppFieldDefinition
                    {
                        nameIndex = valueFieldName,
                        typeIndex = 2,
                        customAttributeIndex = -1,
                        token = 0x04000002
                    };
                    var f2 = new Il2CppFieldDefinition
                    {
                        nameIndex = optionAName,
                        typeIndex = 3, // const literal int
                        customAttributeIndex = -1,
                        token = 0x04000003
                    };
                    WriteClass(fWriter, f0, version);
                    WriteClass(fWriter, f1, version);
                    WriteClass(fWriter, f2, version);
                    fieldsBytes = fMs.ToArray();
                }

                // Methods (1 method)
                using (var mMs = new MemoryStream())
                using (var mWriter = new BinaryWriter(mMs, Encoding.UTF8))
                {
                    var m = new Il2CppMethodDefinition
                    {
                        nameIndex = methodName,
                        declaringType = 0,
                        returnType = 4, // void
                        returnParameterToken = 0,
                        parameterStart = 0,
                        customAttributeIndex = -1,
                        genericContainerIndex = -1,
                        methodIndex = 0,
                        invokerIndex = 0,
                        delegateWrapperIndex = -1,
                        rgctxStartIndex = -1,
                        rgctxCount = 0,
                        token = 0x06000001,
                        flags = 0x0006, // Public
                        iflags = 0,
                        slot = 0,
                        parameterCount = 1
                    };
                    WriteClass(mWriter, m, version);
                    methodsBytes = mMs.ToArray();
                }

                // Type definitions (Type 0 = SyntheticClass, Type 1 = SyntheticEnum)
                using (var tMs = new MemoryStream())
                using (var tWriter = new BinaryWriter(tMs, Encoding.UTF8))
                {
                    var t0 = new Il2CppTypeDefinition
                    {
                        nameIndex = clsName,
                        namespaceIndex = nsName,
                        customAttributeIndex = -1,
                        byvalTypeIndex = 0,
                        byrefTypeIndex = -1,
                        declaringTypeIndex = -1,
                        parentIndex = -1,
                        elementTypeIndex = -1,
                        rgctxStartIndex = -1,
                        rgctxCount = 0,
                        genericContainerIndex = -1,
                        delegateWrapperFromManagedToNativeIndex = -1,
                        marshalingFunctionsIndex = -1,
                        ccwFunctionIndex = -1,
                        guidIndex = -1,
                        flags = 0x00000001, // Public | Class
                        fieldStart = 0,
                        methodStart = 0,
                        eventStart = -1,
                        propertyStart = -1,
                        nestedTypesStart = -1,
                        interfacesStart = -1,
                        vtableStart = -1,
                        interfaceOffsetsStart = -1,
                        method_count = 1,
                        property_count = 0,
                        field_count = 1,
                        event_count = 0,
                        nested_type_count = 0,
                        vtable_count = 0,
                        interfaces_count = 0,
                        interface_offsets_count = 0,
                        bitfield = 0,
                        token = 0x02000001
                    };
                    var t1 = new Il2CppTypeDefinition
                    {
                        nameIndex = enumName,
                        namespaceIndex = nsName,
                        customAttributeIndex = -1,
                        byvalTypeIndex = 1,
                        byrefTypeIndex = -1,
                        declaringTypeIndex = -1,
                        parentIndex = -1,
                        elementTypeIndex = -1,
                        rgctxStartIndex = -1,
                        rgctxCount = 0,
                        genericContainerIndex = -1,
                        delegateWrapperFromManagedToNativeIndex = -1,
                        marshalingFunctionsIndex = -1,
                        ccwFunctionIndex = -1,
                        guidIndex = -1,
                        flags = 0x00000101, // Public | Sealed
                        fieldStart = 1,
                        methodStart = 1,
                        eventStart = -1,
                        propertyStart = -1,
                        nestedTypesStart = -1,
                        interfacesStart = -1,
                        vtableStart = -1,
                        interfaceOffsetsStart = -1,
                        method_count = 0,
                        property_count = 0,
                        field_count = 2,
                        event_count = 0,
                        nested_type_count = 0,
                        vtable_count = 0,
                        interfaces_count = 0,
                        interface_offsets_count = 0,
                        bitfield = 0x3, // IsValueType | IsEnum
                        token = 0x02000002
                    };
                    WriteClass(tWriter, t0, version);
                    WriteClass(tWriter, t1, version);
                    typeDefsBytes = tMs.ToArray();
                }

                // Images (1 image)
                using (var iMs = new MemoryStream())
                using (var iWriter = new BinaryWriter(iMs, Encoding.UTF8))
                {
                    var img = new Il2CppImageDefinition
                    {
                        nameIndex = imgName,
                        assemblyIndex = 0,
                        typeStart = 0,
                        typeCount = 2,
                        exportedTypeStart = -1,
                        exportedTypeCount = 0,
                        entryPointIndex = -1,
                        token = 1, // critical for v24 detection
                        customAttributeStart = 0,
                        customAttributeCount = 0
                    };
                    WriteClass(iWriter, img, version);
                    imagesBytes = iMs.ToArray();
                }

                // Assemblies (1 assembly)
                using (var aMs = new MemoryStream())
                using (var aWriter = new BinaryWriter(aMs, Encoding.UTF8))
                {
                    var asm = new Il2CppAssemblyDefinition
                    {
                        imageIndex = 0,
                        token = 0x20000001,
                        customAttributeIndex = -1,
                        unusedCustomAttributeIndex = -1,
                        referencedAssemblyStart = -1,
                        referencedAssemblyCount = 0,
                        aname = new Il2CppAssemblyNameDefinition
                        {
                            nameIndex = asmName,
                            cultureIndex = 0,
                            hashValueIndex = -1,
                            publicKeyIndex = 0,
                            hash_alg = 0,
                            hash_len = 0,
                            flags = 0,
                            major = 1,
                            minor = 0,
                            build = 0,
                            revision = 0,
                            public_key_token = new byte[8]
                        },
                        moduleToken = 0x00000001
                    };
                    WriteClass(aWriter, asm, version);
                    assembliesBytes = aMs.ToArray();
                }
            }
            else
            {
                // Version >= 38
                // For small counts (< 255):
                // typeDefinitionIndexSize = 1
                // genericContainerIndexSize = 1
                // parameterIndexSize = (version >= 39 ? 1 : 4)
                // fieldIndexSize = 4, methodIndexSize = 4, typeIndexSize = 4
                var typeDefIdxSize = 1;
                var genericContainerIdxSize = 1;
                var paramIdxSize = version >= 39 ? 1 : 4;
                var typeIdxSize = 4;
                var fieldIdxSize = 4;
                var methodIdxSize = 4;
                var defaultValueDataIdxSize = 4;

                // String literals (dataIndex only in v38+)
                using (var sMs = new MemoryStream())
                using (var sWriter = new BinaryWriter(sMs, Encoding.UTF8))
                {
                    sWriter.Write(literalDataIndex);
                    stringLiteralsBytes = sMs.ToArray();
                }

                stringLiteralDataBytes = literalDataBytes;

                // Field default values
                using (var fdvMs = new MemoryStream())
                using (var fdvWriter = new BinaryWriter(fdvMs, Encoding.UTF8))
                {
                    WriteMetadataIndex(fdvWriter, 2, fieldIdxSize);
                    WriteMetadataIndex(fdvWriter, 2, typeIdxSize);
                    WriteMetadataIndex(fdvWriter, 0, defaultValueDataIdxSize);
                    fieldDefaultValuesBytes = fdvMs.ToArray();
                }

                parameterDefaultValuesBytes = Array.Empty<byte>();

                // Parameters
                using (var pMs = new MemoryStream())
                using (var pWriter = new BinaryWriter(pMs, Encoding.UTF8))
                {
                    pWriter.Write(paramName);
                    pWriter.Write(0x08000001u);
                    WriteMetadataIndex(pWriter, 2, typeIdxSize);
                    parametersBytes = pMs.ToArray();
                }

                // Fields (3 fields)
                using (var fMs = new MemoryStream())
                using (var fWriter = new BinaryWriter(fMs, Encoding.UTF8))
                {
                    // f0
                    fWriter.Write(fieldName);
                    WriteMetadataIndex(fWriter, 2, typeIdxSize);
                    fWriter.Write(0x04000001u);

                    // f1
                    fWriter.Write(valueFieldName);
                    WriteMetadataIndex(fWriter, 2, typeIdxSize);
                    fWriter.Write(0x04000002u);

                    // f2
                    fWriter.Write(optionAName);
                    WriteMetadataIndex(fWriter, 3, typeIdxSize);
                    fWriter.Write(0x04000003u);

                    fieldsBytes = fMs.ToArray();
                }

                // Methods (1 method)
                using (var mMs = new MemoryStream())
                using (var mWriter = new BinaryWriter(mMs, Encoding.UTF8))
                {
                    mWriter.Write(methodName);
                    WriteMetadataIndex(mWriter, 0, typeDefIdxSize);
                    WriteMetadataIndex(mWriter, 4, typeIdxSize); // void return
                    mWriter.Write(0); // returnParameterToken
                    WriteMetadataIndex(mWriter, 0, paramIdxSize);
                    WriteMetadataIndex(mWriter, -1, genericContainerIdxSize);
                    mWriter.Write(0x06000001u); // token
                    mWriter.Write((ushort)0x0006); // flags = Public
                    mWriter.Write((ushort)0); // iflags
                    mWriter.Write((ushort)0); // slot
                    mWriter.Write((ushort)1); // parameterCount
                    methodsBytes = mMs.ToArray();
                }

                // Type definitions
                using (var tMs = new MemoryStream())
                using (var tWriter = new BinaryWriter(tMs, Encoding.UTF8))
                {
                    // t0: SyntheticClass
                    tWriter.Write(clsName);
                    tWriter.Write(nsName);
                    WriteMetadataIndex(tWriter, 0, typeIdxSize); // byvalTypeIndex
                    WriteMetadataIndex(tWriter, -1, typeIdxSize); // declaringTypeIndex
                    WriteMetadataIndex(tWriter, -1, typeIdxSize); // parentIndex
                    WriteMetadataIndex(tWriter, -1, genericContainerIdxSize);
                    tWriter.Write(0x00000001u); // flags
                    WriteMetadataIndex(tWriter, 0, fieldIdxSize); // fieldStart
                    WriteMetadataIndex(tWriter, 0, methodIdxSize); // methodStart
                    WriteMetadataIndex(tWriter, -1, 4); // eventStart
                    WriteMetadataIndex(tWriter, -1, 4); // propertyStart
                    WriteMetadataIndex(tWriter, -1, 4); // nestedTypesStart
                    WriteMetadataIndex(tWriter, -1, 4); // interfacesStart
                    tWriter.Write(-1); // vtableStart
                    WriteMetadataIndex(tWriter, -1, 4); // interfaceOffsetsStart
                    tWriter.Write((ushort)1); // method_count
                    tWriter.Write((ushort)0); // property_count
                    tWriter.Write((ushort)1); // field_count
                    tWriter.Write((ushort)0); // event_count
                    tWriter.Write((ushort)0); // nested_type_count
                    tWriter.Write((ushort)0); // vtable_count
                    tWriter.Write((ushort)0); // interfaces_count
                    tWriter.Write((ushort)0); // interface_offsets_count
                    tWriter.Write(0u); // bitfield
                    tWriter.Write(0x02000001u); // token

                    // t1: SyntheticEnum
                    tWriter.Write(enumName);
                    tWriter.Write(nsName);
                    WriteMetadataIndex(tWriter, 1, typeIdxSize); // byvalTypeIndex
                    WriteMetadataIndex(tWriter, -1, typeIdxSize);
                    WriteMetadataIndex(tWriter, -1, typeIdxSize);
                    WriteMetadataIndex(tWriter, -1, genericContainerIdxSize);
                    tWriter.Write(0x00000101u); // flags
                    WriteMetadataIndex(tWriter, 1, fieldIdxSize); // fieldStart
                    WriteMetadataIndex(tWriter, 1, methodIdxSize); // methodStart
                    WriteMetadataIndex(tWriter, -1, 4);
                    WriteMetadataIndex(tWriter, -1, 4);
                    WriteMetadataIndex(tWriter, -1, 4);
                    WriteMetadataIndex(tWriter, -1, 4);
                    tWriter.Write(-1);
                    WriteMetadataIndex(tWriter, -1, 4);
                    tWriter.Write((ushort)0); // method_count
                    tWriter.Write((ushort)0); // property_count
                    tWriter.Write((ushort)2); // field_count
                    tWriter.Write((ushort)0); // event_count
                    tWriter.Write((ushort)0); // nested_type_count
                    tWriter.Write((ushort)0); // vtable_count
                    tWriter.Write((ushort)0); // interfaces_count
                    tWriter.Write((ushort)0); // interface_offsets_count
                    tWriter.Write(0x3u); // bitfield: IsValueType | IsEnum
                    tWriter.Write(0x02000002u); // token

                    typeDefsBytes = tMs.ToArray();
                }

                // Images
                using (var iMs = new MemoryStream())
                using (var iWriter = new BinaryWriter(iMs, Encoding.UTF8))
                {
                    iWriter.Write(imgName);
                    iWriter.Write(0); // assemblyIndex
                    WriteMetadataIndex(iWriter, 0, typeDefIdxSize); // typeStart
                    iWriter.Write(2u); // typeCount
                    WriteMetadataIndex(iWriter, -1, typeDefIdxSize); // exportedTypeStart
                    iWriter.Write(0u); // exportedTypeCount
                    WriteMetadataIndex(iWriter, -1, methodIdxSize); // entryPointIndex
                    iWriter.Write(1u); // token
                    iWriter.Write(0); // customAttributeStart
                    iWriter.Write(0u); // customAttributeCount
                    imagesBytes = iMs.ToArray();
                }

                // Assemblies
                using (var aMs = new MemoryStream())
                using (var aWriter = new BinaryWriter(aMs, Encoding.UTF8))
                {
                    var asm = new Il2CppAssemblyDefinition
                    {
                        imageIndex = 0,
                        token = 0x20000001,
                        unusedCustomAttributeIndex = -1,
                        referencedAssemblyStart = -1,
                        referencedAssemblyCount = 0,
                        aname = new Il2CppAssemblyNameDefinition
                        {
                            nameIndex = asmName,
                            cultureIndex = 0,
                            publicKeyIndex = 0,
                            hash_alg = 0,
                            hash_len = 0,
                            flags = 0,
                            major = 1,
                            minor = 0,
                            build = 0,
                            revision = 0,
                            public_key_token = new byte[8]
                        },
                        moduleToken = 0x00000001
                    };
                    WriteClass(aWriter, asm, version);
                    assembliesBytes = aMs.ToArray();
                }
            }

            // Calculate header size
            var headerSize = CalculateHeaderSize(version);
            var currentOffset = (uint)headerSize;

            uint stringLiteralsOffset = currentOffset;
            int stringLiteralsSize = stringLiteralsBytes.Length;
            currentOffset += (uint)stringLiteralsSize;

            uint stringLiteralDataOffset = currentOffset;
            int stringLiteralDataSize = stringLiteralDataBytes.Length;
            currentOffset += (uint)stringLiteralDataSize;

            uint stringsOffset = currentOffset;
            int stringsSize = stringBytes.Length;
            currentOffset += (uint)stringsSize;

            uint fieldDefaultValuesOffset = currentOffset;
            int fieldDefaultValuesSize = fieldDefaultValuesBytes.Length;
            currentOffset += (uint)fieldDefaultValuesSize;

            uint defaultValueDataOffset = currentOffset;
            int defaultValueDataSize = defaultValueData.Length;
            currentOffset += (uint)defaultValueDataSize;

            uint parametersOffset = currentOffset;
            int parametersSize = parametersBytes.Length;
            currentOffset += (uint)parametersSize;

            uint fieldsOffset = currentOffset;
            int fieldsSize = fieldsBytes.Length;
            currentOffset += (uint)fieldsSize;

            uint methodsOffset = currentOffset;
            int methodsSize = methodsBytes.Length;
            currentOffset += (uint)methodsSize;

            uint typeDefsOffset = currentOffset;
            int typeDefsSize = typeDefsBytes.Length;
            currentOffset += (uint)typeDefsSize;

            uint imagesOffset = currentOffset;
            int imagesSize = imagesBytes.Length;
            currentOffset += (uint)imagesSize;

            uint assembliesOffset = currentOffset;
            int assembliesSize = assembliesBytes.Length;
            currentOffset += (uint)assembliesSize;

            // Zeroed registration records corruption mode
            var imagesCount = 1;
            var typeDefsCount = 2;
            if (corruption == MetadataCorruptionType.ZeroedRegistrationRecords)
            {
                imagesSize = 0;
                typeDefsSize = 0;
                imagesCount = 0;
                typeDefsCount = 0;
            }

            // Write header
            var magic = corruption == MetadataCorruptionType.WrongMagic ? 0xDEADBEEFu : SanityMagic;
            var verVal = corruption == MetadataCorruptionType.OutOfRangeVersion ? 999 : version;

            if (version < 38)
            {
                var header = new Il2CppGlobalMetadataHeader
                {
                    sanity = magic,
                    version = verVal,
                    stringLiteralOffset = stringLiteralsOffset,
                    stringLiteralSize = stringLiteralsSize,
                    stringLiteralDataOffset = stringLiteralDataOffset,
                    stringLiteralDataSize = stringLiteralDataSize,
                    stringOffset = stringsOffset,
                    stringSize = stringsSize,
                    eventsOffset = currentOffset,
                    eventsSize = 0,
                    propertiesOffset = currentOffset,
                    propertiesSize = 0,
                    methodsOffset = methodsOffset,
                    methodsSize = methodsSize,
                    parameterDefaultValuesOffset = currentOffset,
                    parameterDefaultValuesSize = 0,
                    fieldDefaultValuesOffset = fieldDefaultValuesOffset,
                    fieldDefaultValuesSize = fieldDefaultValuesSize,
                    fieldAndParameterDefaultValueDataOffset = defaultValueDataOffset,
                    fieldAndParameterDefaultValueDataSize = defaultValueDataSize,
                    fieldMarshaledSizesOffset = (int)currentOffset,
                    fieldMarshaledSizesSize = 0,
                    parametersOffset = parametersOffset,
                    parametersSize = parametersSize,
                    fieldsOffset = fieldsOffset,
                    fieldsSize = fieldsSize,
                    genericParametersOffset = currentOffset,
                    genericParametersSize = 0,
                    genericParameterConstraintsOffset = currentOffset,
                    genericParameterConstraintsSize = 0,
                    genericContainersOffset = currentOffset,
                    genericContainersSize = 0,
                    nestedTypesOffset = currentOffset,
                    nestedTypesSize = 0,
                    interfacesOffset = currentOffset,
                    interfacesSize = 0,
                    vtableMethodsOffset = currentOffset,
                    vtableMethodsSize = 0,
                    interfaceOffsetsOffset = (int)currentOffset,
                    interfaceOffsetsSize = 0,
                    typeDefinitionsOffset = typeDefsOffset,
                    typeDefinitionsSize = typeDefsSize,
                    rgctxEntriesOffset = currentOffset,
                    rgctxEntriesCount = 0,
                    imagesOffset = imagesOffset,
                    imagesSize = imagesSize,
                    assembliesOffset = assembliesOffset,
                    assembliesSize = assembliesSize,
                    metadataUsageListsOffset = currentOffset,
                    metadataUsageListsCount = 0,
                    metadataUsagePairsOffset = currentOffset,
                    metadataUsagePairsCount = 0,
                    fieldRefsOffset = currentOffset,
                    fieldRefsSize = 0,
                    referencedAssembliesOffset = (int)currentOffset,
                    referencedAssembliesSize = 0,
                    attributesInfoOffset = currentOffset,
                    attributesInfoCount = 0,
                    attributeTypesOffset = currentOffset,
                    attributeTypesCount = 0,
                    attributeDataOffset = currentOffset,
                    attributeDataSize = 0,
                    attributeDataRangeOffset = currentOffset,
                    attributeDataRangeSize = 0,
                    unresolvedVirtualCallParameterTypesOffset = (int)currentOffset,
                    unresolvedVirtualCallParameterTypesSize = 0,
                    unresolvedVirtualCallParameterRangesOffset = (int)currentOffset,
                    unresolvedVirtualCallParameterRangesSize = 0,
                    windowsRuntimeTypeNamesOffset = (int)currentOffset,
                    windowsRuntimeTypeNamesSize = 0,
                    windowsRuntimeStringsOffset = (int)currentOffset,
                    windowsRuntimeStringsSize = 0,
                    exportedTypeDefinitionsOffset = (int)currentOffset,
                    exportedTypeDefinitionsSize = 0
                };

                WriteClass(writer, header, version);
            }
            else
            {
                // Version >= 38 section-based header
                writer.Write(magic);
                writer.Write(verVal);

                Il2CppSectionMetadata Sec(uint off, int size, int count) => new()
                {
                    offset = (int)off,
                    sectionSize = size,
                    count = count
                };

                var sections = new List<Il2CppSectionMetadata>
                {
                    Sec(stringLiteralsOffset, stringLiteralsSize, 1),
                    Sec(stringLiteralDataOffset, stringLiteralDataSize, stringLiteralDataSize),
                    Sec(stringsOffset, stringsSize, stringsSize),
                    Sec(currentOffset, 0, 0), // events
                    Sec(currentOffset, 0, 0), // properties
                    Sec(methodsOffset, methodsSize, 1), // methods
                    Sec(currentOffset, 0, 0), // parameterDefaultValues
                    Sec(fieldDefaultValuesOffset, fieldDefaultValuesSize, 1), // fieldDefaultValues
                    Sec(defaultValueDataOffset, defaultValueDataSize, defaultValueDataSize), // fieldAndParameterDefaultValueData
                    Sec(currentOffset, 0, 0), // fieldMarshaledSizes
                    Sec(parametersOffset, parametersSize, 1), // parameters
                    Sec(fieldsOffset, fieldsSize, 3), // fields
                    Sec(currentOffset, 0, 0), // genericParameters
                    Sec(currentOffset, 0, 0), // genericParameterConstraints
                    Sec(currentOffset, 0, 0), // genericContainers
                    Sec(currentOffset, 0, 0), // nestedTypes
                    Sec(currentOffset, 0, 0), // interfaces
                    Sec(currentOffset, 0, 0), // vtableMethods
                    Sec(currentOffset, 0, 0), // interfaceOffsets
                    Sec(typeDefsOffset, typeDefsSize, typeDefsCount), // typeDefinitions
                    Sec(imagesOffset, imagesSize, imagesCount), // images
                    Sec(assembliesOffset, assembliesSize, 1), // assemblies
                    Sec(currentOffset, 0, 0), // fieldRefs
                    Sec(currentOffset, 0, 0), // referencedAssemblies
                    Sec(currentOffset, 0, 0), // attributeData
                    Sec(currentOffset, 0, 0), // attributeDataRanges
                    Sec(currentOffset, 0, 0), // unresolvedIndirectCallParameterTypes
                    Sec(currentOffset, 0, 0), // unresolvedIndirectCallParameterRanges
                    Sec(currentOffset, 0, 0), // windowsRuntimeTypeNames
                    Sec(currentOffset, 0, 0), // windowsRuntimeStrings
                    Sec(currentOffset, 0, 0), // exportedTypeDefinitions
                };

                foreach (var s in sections)
                {
                    writer.Write(s.offset);
                    writer.Write(s.sectionSize);
                    writer.Write(s.count);
                }
            }

            // Write all section payloads sequentially
            writer.Write(stringLiteralsBytes);
            writer.Write(stringLiteralDataBytes);
            writer.Write(stringBytes);
            writer.Write(fieldDefaultValuesBytes);
            writer.Write(defaultValueData);
            writer.Write(parametersBytes);
            writer.Write(fieldsBytes);
            writer.Write(methodsBytes);
            writer.Write(typeDefsBytes);
            writer.Write(imagesBytes);
            writer.Write(assembliesBytes);

            writer.Flush();
            var result = ms.ToArray();

            // Truncation corruption
            if (corruption == MetadataCorruptionType.TruncatedSection)
            {
                var truncatedLength = Math.Min(result.Length / 2, 80);
                var truncated = new byte[truncatedLength];
                Buffer.BlockCopy(result, 0, truncated, 0, truncatedLength);
                return truncated;
            }

            return result;
        }

        private static int CalculateHeaderSize(int version)
        {
            if (version >= 38)
            {
                return 4 + 4 + 31 * 12; // 380 bytes
            }
            return version switch
            {
                16 => 192,
                21 => 240,
                24 => 272,
                _ => 256
            };
        }

        private static void WriteMetadataIndex(BinaryWriter writer, int value, int size)
        {
            if (size == 4)
            {
                writer.Write(value);
            }
            else if (size == 2)
            {
                writer.Write(value == -1 ? ushort.MaxValue : (ushort)value);
            }
            else
            {
                writer.Write(value == -1 ? byte.MaxValue : (byte)value);
            }
        }

        public static void WriteClass<T>(BinaryWriter writer, T obj, double version)
        {
            WriteClassInternal(writer, obj, typeof(T), version);
        }

        private static void WriteClassInternal(BinaryWriter writer, object? obj, Type type, double version)
        {
            if (obj == null) return;
            foreach (var f in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                var attrs = f.GetCustomAttributes<VersionAttribute>().ToArray();
                if (attrs.Length > 0)
                {
                    var match = false;
                    foreach (var a in attrs)
                    {
                        if (version >= a.Min && version <= a.Max)
                        {
                            match = true;
                            break;
                        }
                    }
                    if (!match) continue;
                }

                var ftype = f.FieldType;
                var val = f.GetValue(obj);

                if (ftype == typeof(uint))
                    writer.Write((uint)(val ?? 0u));
                else if (ftype == typeof(int))
                    writer.Write((int)(val ?? 0));
                else if (ftype == typeof(ushort))
                    writer.Write((ushort)(val ?? (ushort)0));
                else if (ftype == typeof(short))
                    writer.Write((short)(val ?? (short)0));
                else if (ftype == typeof(byte))
                    writer.Write((byte)(val ?? (byte)0));
                else if (ftype == typeof(byte[]))
                {
                    var arr = (byte[])val!;
                    var lenAttr = f.GetCustomAttribute<ArrayLengthAttribute>();
                    var len = lenAttr != null ? lenAttr.Length : (arr?.Length ?? 0);
                    var buf = new byte[len];
                    if (arr != null)
                        Buffer.BlockCopy(arr, 0, buf, 0, Math.Min(arr.Length, len));
                    writer.Write(buf);
                }
                else
                {
                    WriteClassInternal(writer, val, ftype, version);
                }
            }
        }

        private sealed class StringTableBuilder
        {
            private readonly MemoryStream _stream = new();
            private readonly BinaryWriter _writer;

            public StringTableBuilder()
            {
                _writer = new BinaryWriter(_stream, Encoding.UTF8);
            }

            public uint Add(string str)
            {
                var pos = (uint)_stream.Position;
                var bytes = Encoding.UTF8.GetBytes(str);
                _writer.Write(bytes);
                _writer.Write((byte)0);
                return pos;
            }

            public byte[] ToArray()
            {
                _writer.Flush();
                return _stream.ToArray();
            }
        }
    }
}
