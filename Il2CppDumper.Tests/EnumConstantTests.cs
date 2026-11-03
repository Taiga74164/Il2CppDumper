using System;
using System.IO;
using Mono.Cecil;
using Xunit;

namespace Il2CppDumper.Tests
{
    public class EnumConstantTests
    {
        [Fact]
        public void Reproduction_AcceptsEnumConstant_AndRetainsValueThroughExport()
        {
            // Reproduces the exact scenario reported in Issue #1
            using var assembly = AssemblyDefinition.CreateAssembly(
                new AssemblyNameDefinition("EnumRepro", new Version(1, 0)),
                "EnumRepro.dll",
                ModuleKind.Dll);

            var module = assembly.MainModule;
            var enumType = new TypeDefinition(
                "Test",
                "Mode",
                TypeAttributes.Public | TypeAttributes.Sealed,
                module.ImportReference(typeof(Enum)));
            module.Types.Add(enumType);

            enumType.Fields.Add(new FieldDefinition(
                "value__",
                FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName,
                module.TypeSystem.Int32));

            var readyField = new FieldDefinition(
                "Ready",
                FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal | FieldAttributes.HasDefault,
                enumType);
            enumType.Fields.Add(readyField);

            var setResult = DummyAssemblyGenerator.TrySetConstant(readyField, enumType, 7);
            Assert.True(setResult, "TrySetConstant should succeed for enum type with valid integer value.");
            Assert.True(readyField.HasConstant);
            Assert.Equal(7, readyField.Constant);

            DummyAssemblyExporter.SanitizeConstants(assembly);

            Assert.True(readyField.HasConstant, "SanitizeConstants should retain valid enum constant.");
            Assert.Equal(7, readyField.Constant);

            using var stream = new MemoryStream();
            assembly.Write(stream);
            Assert.True(stream.Length > 0, "Assembly should serialize successfully.");

            stream.Position = 0;
            using var roundtripAssembly = AssemblyDefinition.ReadAssembly(stream);
            var roundtripField = roundtripAssembly.MainModule.GetType("Test.Mode").Fields[1];

            Assert.True(roundtripField.HasConstant);
            Assert.Equal(7, roundtripField.Constant);
            Assert.IsType<int>(roundtripField.Constant);
        }

        [Theory]
        [InlineData("System.Byte", (byte)0)]
        [InlineData("System.Byte", (byte)7)]
        [InlineData("System.Byte", (byte)255)]
        [InlineData("System.SByte", (sbyte)-128)]
        [InlineData("System.SByte", (sbyte)-1)]
        [InlineData("System.SByte", (sbyte)0)]
        [InlineData("System.SByte", (sbyte)127)]
        [InlineData("System.Int16", (short)-32768)]
        [InlineData("System.Int16", (short)-1)]
        [InlineData("System.Int16", (short)0)]
        [InlineData("System.Int16", (short)32767)]
        [InlineData("System.UInt16", (ushort)0)]
        [InlineData("System.UInt16", (ushort)7)]
        [InlineData("System.UInt16", (ushort)65535)]
        [InlineData("System.Int32", int.MinValue)]
        [InlineData("System.Int32", -1)]
        [InlineData("System.Int32", 0)]
        [InlineData("System.Int32", 7)]
        [InlineData("System.Int32", int.MaxValue)]
        [InlineData("System.UInt32", 0u)]
        [InlineData("System.UInt32", 7u)]
        [InlineData("System.UInt32", uint.MaxValue)]
        [InlineData("System.Int64", long.MinValue)]
        [InlineData("System.Int64", -1L)]
        [InlineData("System.Int64", 0L)]
        [InlineData("System.Int64", 7L)]
        [InlineData("System.Int64", long.MaxValue)]
        [InlineData("System.UInt64", 0UL)]
        [InlineData("System.UInt64", 7UL)]
        [InlineData("System.UInt64", ulong.MaxValue)]
        public void EnumFields_WithVariousUnderlyingTypes_AreNormalizedAndPreserved(string underlyingTypeName, object inputValue)
        {
            using var assembly = AssemblyDefinition.CreateAssembly(
                new AssemblyNameDefinition("EnumFieldTest_" + underlyingTypeName.Replace(".", "_"), new Version(1, 0)),
                "EnumFieldTest.dll",
                ModuleKind.Dll);

            var module = assembly.MainModule;
            var underlyingType = ResolveTypeReference(module, underlyingTypeName);

            var enumType = new TypeDefinition(
                "Test",
                "TypedEnum_" + underlyingTypeName.Replace(".", "_"),
                TypeAttributes.Public | TypeAttributes.Sealed,
                module.ImportReference(typeof(Enum)));
            module.Types.Add(enumType);

            enumType.Fields.Add(new FieldDefinition(
                "value__",
                FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName,
                underlyingType));

            var enumMember = new FieldDefinition(
                "Member",
                FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal | FieldAttributes.HasDefault,
                enumType);
            enumType.Fields.Add(enumMember);

            var setResult = DummyAssemblyGenerator.TrySetConstant(enumMember, enumType, inputValue);
            Assert.True(setResult, $"TrySetConstant should accept {inputValue} for enum backed by {underlyingTypeName}.");
            Assert.True(enumMember.HasConstant);

            DummyAssemblyExporter.SanitizeConstants(assembly);
            Assert.True(enumMember.HasConstant, "SanitizeConstants should retain constant.");

            using var stream = new MemoryStream();
            assembly.Write(stream);

            stream.Position = 0;
            using var readAssembly = AssemblyDefinition.ReadAssembly(stream);
            var readMember = readAssembly.MainModule.GetType(enumType.FullName).Fields[1];

            Assert.True(readMember.HasConstant);
            Assert.Equal(inputValue, readMember.Constant);
            Assert.Equal(inputValue.GetType(), readMember.Constant.GetType());
        }

        [Theory]
        [InlineData("System.Byte", (byte)15)]
        [InlineData("System.SByte", (sbyte)-10)]
        [InlineData("System.Int16", (short)-1000)]
        [InlineData("System.UInt16", (ushort)50000)]
        [InlineData("System.Int32", 123456)]
        [InlineData("System.UInt32", 3000000000u)]
        [InlineData("System.Int64", -900000000000L)]
        [InlineData("System.UInt64", 90000000000000UL)]
        public void EnumParameters_WithVariousUnderlyingTypes_AreNormalizedAndPreserved(string underlyingTypeName, object inputValue)
        {
            using var assembly = AssemblyDefinition.CreateAssembly(
                new AssemblyNameDefinition("EnumParamTest_" + underlyingTypeName.Replace(".", "_"), new Version(1, 0)),
                "EnumParamTest.dll",
                ModuleKind.Dll);

            var module = assembly.MainModule;
            var underlyingType = ResolveTypeReference(module, underlyingTypeName);

            var enumType = new TypeDefinition(
                "Test",
                "ParamEnum_" + underlyingTypeName.Replace(".", "_"),
                TypeAttributes.Public | TypeAttributes.Sealed,
                module.ImportReference(typeof(Enum)));
            module.Types.Add(enumType);

            enumType.Fields.Add(new FieldDefinition(
                "value__",
                FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName,
                underlyingType));

            var holderClass = new TypeDefinition("Test", "Holder", TypeAttributes.Public, module.TypeSystem.Object);
            module.Types.Add(holderClass);

            var method = new MethodDefinition("Execute", MethodAttributes.Public, module.TypeSystem.Void);
            holderClass.Methods.Add(method);

            var parameter = new ParameterDefinition("optParam", ParameterAttributes.Optional | ParameterAttributes.HasDefault, enumType);
            method.Parameters.Add(parameter);

            var setResult = DummyAssemblyGenerator.TrySetConstant(parameter, enumType, inputValue);
            Assert.True(setResult, $"TrySetConstant should accept {inputValue} for enum parameter backed by {underlyingTypeName}.");
            Assert.True(parameter.HasConstant);

            DummyAssemblyExporter.SanitizeConstants(assembly);
            Assert.True(parameter.HasConstant, "SanitizeConstants should retain enum parameter constant.");

            using var stream = new MemoryStream();
            assembly.Write(stream);

            stream.Position = 0;
            using var readAssembly = AssemblyDefinition.ReadAssembly(stream);
            var readParam = readAssembly.MainModule.GetType("Test.Holder").Methods[0].Parameters[0];

            Assert.True(readParam.HasConstant);
            Assert.Equal(inputValue, readParam.Constant);
            Assert.Equal(inputValue.GetType(), readParam.Constant.GetType());
        }

        [Theory]
        [InlineData("System.UInt16", (short)7, typeof(ushort), (ushort)7)]
        [InlineData("System.UInt16", 7u, typeof(ushort), (ushort)7)]
        [InlineData("System.Byte", 7, typeof(byte), (byte)7)]
        [InlineData("System.SByte", (short)-5, typeof(sbyte), (sbyte)-5)]
        [InlineData("System.Int16", (int)500, typeof(short), (short)500)]
        [InlineData("System.Int32", (short)100, typeof(int), 100)]
        [InlineData("System.Int32", 100L, typeof(int), 100)]
        [InlineData("System.UInt32", 100, typeof(uint), 100u)]
        [InlineData("System.Int64", 100, typeof(long), 100L)]
        [InlineData("System.UInt64", 100u, typeof(ulong), 100UL)]
        public void EnumFields_CrossRepresentation_NormalizedToDeclaredUnderlyingType(
            string underlyingTypeName, object input, Type expectedType, object expectedValue)
        {
            using var assembly = AssemblyDefinition.CreateAssembly(
                new AssemblyNameDefinition("CrossFieldTest_" + underlyingTypeName.Replace(".", "_"), new Version(1, 0)),
                "CrossFieldTest.dll",
                ModuleKind.Dll);

            var module = assembly.MainModule;
            var underlyingType = ResolveTypeReference(module, underlyingTypeName);

            var enumType = new TypeDefinition(
                "Test",
                "CrossEnum_" + underlyingTypeName.Replace(".", "_"),
                TypeAttributes.Public | TypeAttributes.Sealed,
                module.ImportReference(typeof(Enum)));
            module.Types.Add(enumType);

            enumType.Fields.Add(new FieldDefinition(
                "value__",
                FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName,
                underlyingType));

            var enumMember = new FieldDefinition(
                "Member",
                FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal | FieldAttributes.HasDefault,
                enumType);
            enumType.Fields.Add(enumMember);

            var accepted = DummyAssemblyGenerator.TrySetConstant(enumMember, enumType, input);
            Assert.True(accepted);
            Assert.Equal(expectedType, enumMember.Constant.GetType());
            Assert.Equal(expectedValue, enumMember.Constant);

            DummyAssemblyExporter.SanitizeConstants(assembly);
            Assert.True(enumMember.HasConstant);
            Assert.Equal(expectedType, enumMember.Constant.GetType());

            using var stream = new MemoryStream();
            assembly.Write(stream);
            stream.Position = 0;

            using var read = AssemblyDefinition.ReadAssembly(stream);
            var actual = read.MainModule.GetType(enumType.FullName).Fields[1].Constant;
            Assert.Equal(expectedType, actual.GetType());
            Assert.Equal(expectedValue, actual);
        }

        [Theory]
        [InlineData("System.UInt16", (short)7, typeof(ushort), (ushort)7)]
        [InlineData("System.UInt16", 7u, typeof(ushort), (ushort)7)]
        [InlineData("System.Byte", 7, typeof(byte), (byte)7)]
        public void EnumParameters_CrossRepresentation_NormalizedToDeclaredUnderlyingType(
            string underlyingTypeName, object input, Type expectedType, object expectedValue)
        {
            using var assembly = AssemblyDefinition.CreateAssembly(
                new AssemblyNameDefinition("CrossParamTest_" + underlyingTypeName.Replace(".", "_"), new Version(1, 0)),
                "CrossParamTest.dll",
                ModuleKind.Dll);

            var module = assembly.MainModule;
            var underlyingType = ResolveTypeReference(module, underlyingTypeName);

            var enumType = new TypeDefinition("Test", "CrossParamEnum", TypeAttributes.Public | TypeAttributes.Sealed, module.ImportReference(typeof(Enum)));
            module.Types.Add(enumType);
            enumType.Fields.Add(new FieldDefinition("value__", FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName, underlyingType));

            var cls = new TypeDefinition("Test", "H", TypeAttributes.Public, module.TypeSystem.Object);
            module.Types.Add(cls);
            var method = new MethodDefinition("M", MethodAttributes.Public, module.TypeSystem.Void);
            cls.Methods.Add(method);
            var param = new ParameterDefinition("p", ParameterAttributes.Optional | ParameterAttributes.HasDefault, enumType);
            method.Parameters.Add(param);

            var accepted = DummyAssemblyGenerator.TrySetConstant(param, enumType, input);
            Assert.True(accepted);
            Assert.Equal(expectedType, param.Constant.GetType());
            Assert.Equal(expectedValue, param.Constant);

            DummyAssemblyExporter.SanitizeConstants(assembly);
            Assert.True(param.HasConstant);
            Assert.Equal(expectedType, param.Constant.GetType());

            using var stream = new MemoryStream();
            assembly.Write(stream);
            stream.Position = 0;

            using var read = AssemblyDefinition.ReadAssembly(stream);
            var actual = read.MainModule.GetType("Test.H").Methods[0].Parameters[0].Constant;
            Assert.Equal(expectedType, actual.GetType());
            Assert.Equal(expectedValue, actual);
        }

        [Theory]
        [InlineData("System.UInt16", (short)-1)]
        [InlineData("System.UInt16", 65536u)]
        [InlineData("System.Byte", -1)]
        [InlineData("System.Byte", 256)]
        [InlineData("System.SByte", -129)]
        [InlineData("System.SByte", 128)]
        [InlineData("System.Int16", -32769)]
        [InlineData("System.Int16", 32768)]
        [InlineData("System.UInt32", -1)]
        [InlineData("System.Int32", 2147483648L)]
        [InlineData("System.UInt64", -1L)]
        public void EnumConstants_OutOfRangeValues_AreRejected(string underlyingTypeName, object outOfRangeInput)
        {
            using var assembly = AssemblyDefinition.CreateAssembly(
                new AssemblyNameDefinition("OutOfRangeTest", new Version(1, 0)),
                "OutOfRangeTest.dll",
                ModuleKind.Dll);

            var module = assembly.MainModule;
            var underlyingType = ResolveTypeReference(module, underlyingTypeName);

            var enumType = new TypeDefinition("Test", "OutOfRangeEnum", TypeAttributes.Public | TypeAttributes.Sealed, module.ImportReference(typeof(Enum)));
            module.Types.Add(enumType);
            enumType.Fields.Add(new FieldDefinition("value__", FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName, underlyingType));

            var field = new FieldDefinition("F", FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal | FieldAttributes.HasDefault, enumType);
            enumType.Fields.Add(field);

            Assert.False(DummyAssemblyGenerator.TrySetConstant(field, enumType, outOfRangeInput),
                $"TrySetConstant should reject out-of-range value {outOfRangeInput} for {underlyingTypeName}.");

            // If an out-of-range value was pre-assigned, sanitizer must clear it
            field.Constant = outOfRangeInput;
            DummyAssemblyExporter.SanitizeConstants(assembly);
            Assert.False(field.HasConstant);
            Assert.Null(field.Constant);
            Assert.False(field.Attributes.HasFlag(FieldAttributes.HasDefault));
        }

        [Theory]
        [InlineData("7")]
        [InlineData("0")]
        [InlineData(7.5d)]
        [InlineData(3.14f)]
        [InlineData(true)]
        [InlineData(false)]
        public void EnumConstants_NonIntegralPayloads_AreRejected(object nonIntegralInput)
        {
            using var assembly = AssemblyDefinition.CreateAssembly(
                new AssemblyNameDefinition("NonIntegralPayloadTest", new Version(1, 0)),
                "NonIntegralPayloadTest.dll",
                ModuleKind.Dll);

            var module = assembly.MainModule;
            var enumType = new TypeDefinition("Test", "PayloadEnum", TypeAttributes.Public | TypeAttributes.Sealed, module.ImportReference(typeof(Enum)));
            module.Types.Add(enumType);
            enumType.Fields.Add(new FieldDefinition("value__", FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName, module.TypeSystem.Int32));

            var field = new FieldDefinition("F", FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal | FieldAttributes.HasDefault, enumType);
            enumType.Fields.Add(field);

            Assert.False(DummyAssemblyGenerator.TrySetConstant(field, enumType, nonIntegralInput));

            field.Constant = nonIntegralInput;
            DummyAssemblyExporter.SanitizeConstants(assembly);
            Assert.False(field.HasConstant);
            Assert.Null(field.Constant);
            Assert.False(field.Attributes.HasFlag(FieldAttributes.HasDefault));
        }

        [Fact]
        public void EnumConstants_DecimalAndObjectPayloads_AreRejected()
        {
            using var assembly = AssemblyDefinition.CreateAssembly(
                new AssemblyNameDefinition("DecimalObjectTest", new Version(1, 0)),
                "DecimalObjectTest.dll",
                ModuleKind.Dll);

            var module = assembly.MainModule;
            var enumType = new TypeDefinition("Test", "DecimalEnum", TypeAttributes.Public | TypeAttributes.Sealed, module.ImportReference(typeof(Enum)));
            module.Types.Add(enumType);
            enumType.Fields.Add(new FieldDefinition("value__", FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName, module.TypeSystem.Int32));

            var field = new FieldDefinition("F", FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal | FieldAttributes.HasDefault, enumType);
            enumType.Fields.Add(field);

            Assert.False(DummyAssemblyGenerator.TrySetConstant(field, enumType, 9.2m));
            Assert.False(DummyAssemblyGenerator.TrySetConstant(field, enumType, new object()));
        }

        [Theory]
        [InlineData("array", false)]
        [InlineData("pointer", false)]
        [InlineData("byref", false)]
        [InlineData("array", true)]
        [InlineData("pointer", true)]
        [InlineData("byref", true)]
        [InlineData("optional_modifier_pointer", true)]
        [InlineData("required_modifier_array", true)]
        public void TypeWrappers_ArraysPointersByRefs_RejectScalarConstants(string wrapper, bool isEnum)
        {
            using var assembly = AssemblyDefinition.CreateAssembly(
                new AssemblyNameDefinition("WrapperTest_" + wrapper + "_" + (isEnum ? "enum" : "primitive"), new Version(1, 0)),
                "WrapperTest.dll",
                ModuleKind.Dll);

            var m = assembly.MainModule;
            TypeReference baseType;
            if (isEnum)
            {
                var enumTypeDef = new TypeDefinition("Test", "E_" + wrapper, TypeAttributes.Public | TypeAttributes.Sealed, m.ImportReference(typeof(Enum)));
                m.Types.Add(enumTypeDef);
                enumTypeDef.Fields.Add(new FieldDefinition("value__", FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName, m.TypeSystem.Int32));
                baseType = enumTypeDef;
            }
            else
            {
                baseType = m.TypeSystem.Int32;
            }

            var modType = m.ImportReference(new TypeReference("System.Runtime.CompilerServices", "IsConst", m, m.TypeSystem.CoreLibrary));

            TypeReference target = wrapper switch
            {
                "array" => new ArrayType(baseType),
                "pointer" => new PointerType(baseType),
                "byref" => new ByReferenceType(baseType),
                "optional_modifier_pointer" => new OptionalModifierType(modType, new PointerType(baseType)),
                "required_modifier_array" => new RequiredModifierType(modType, new ArrayType(baseType)),
                _ => throw new ArgumentException("Unknown wrapper: " + wrapper)
            };

            var holder = new TypeDefinition("Test", "Holder_" + wrapper + "_" + (isEnum ? "enum" : "prim"), TypeAttributes.Public, m.TypeSystem.Object);
            m.Types.Add(holder);

            var field = new FieldDefinition("InvalidField", FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal | FieldAttributes.HasDefault, target);
            holder.Fields.Add(field);

            var method = new MethodDefinition("TestMethod", MethodAttributes.Public, m.TypeSystem.Void);
            holder.Methods.Add(method);
            var parameter = new ParameterDefinition("InvalidParam", ParameterAttributes.Optional | ParameterAttributes.HasDefault, target);
            method.Parameters.Add(parameter);

            Assert.False(DummyAssemblyGenerator.CanWriteConstant(target), $"CanWriteConstant must be false for {target.FullName}.");
            Assert.False(DummyAssemblyGenerator.TrySetConstant(field, target, 7), $"TrySetConstant (field) must reject scalar constant for {target.FullName}.");
            Assert.False(DummyAssemblyGenerator.TrySetConstant(parameter, target, 7), $"TrySetConstant (parameter) must reject scalar constant for {target.FullName}.");

            // Even if an invalid constant was pre-assigned on field and parameter, sanitizer must clear them and assembly serialization must succeed
            field.Constant = 7;
            field.Attributes |= FieldAttributes.HasDefault;
            parameter.Constant = 7;
            parameter.Attributes |= ParameterAttributes.HasDefault;

            DummyAssemblyExporter.SanitizeConstants(assembly);

            Assert.False(field.HasConstant);
            Assert.Null(field.Constant);
            Assert.False(field.Attributes.HasFlag(FieldAttributes.HasDefault));

            Assert.False(parameter.HasConstant);
            Assert.Null(parameter.Constant);
            Assert.False(parameter.Attributes.HasFlag(ParameterAttributes.HasDefault));

            using var stream = new MemoryStream();
            assembly.Write(stream);
            Assert.True(stream.Length > 0);

            stream.Position = 0;
            using var read = AssemblyDefinition.ReadAssembly(stream);
            var readHolder = read.MainModule.GetType(holder.FullName);
            var readField = readHolder.Fields[0];
            Assert.False(readField.HasConstant);
            Assert.Null(readField.Constant);

            var readParam = readHolder.Methods[0].Parameters[0];
            Assert.False(readParam.HasConstant);
            Assert.Null(readParam.Constant);
        }

        [Fact]
        public void ImportedEnumReferences_AndOrdinaryEnumDefinitions_RemainSupported()
        {
            using var defAssembly = AssemblyDefinition.CreateAssembly(
                new AssemblyNameDefinition("DefAssembly", new Version(1, 0)),
                "DefAssembly.dll",
                ModuleKind.Dll);

            var defModule = defAssembly.MainModule;
            var enumDef = new TypeDefinition("Exported", "SharedEnum", TypeAttributes.Public | TypeAttributes.Sealed, defModule.ImportReference(typeof(Enum)));
            defModule.Types.Add(enumDef);
            enumDef.Fields.Add(new FieldDefinition("value__", FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName, defModule.TypeSystem.Int32));

            // 1. Ordinary enum definition in same assembly
            var localField = new FieldDefinition("LocalF", FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal | FieldAttributes.HasDefault, enumDef);
            enumDef.Fields.Add(localField);
            Assert.True(DummyAssemblyGenerator.CanWriteConstant(enumDef));
            Assert.True(DummyAssemblyGenerator.TrySetConstant(localField, enumDef, 42));
            Assert.Equal(42, localField.Constant);

            // 2. Modifier around enum definition remains supported
            var modType = defModule.ImportReference(new TypeReference("System.Runtime.CompilerServices", "IsConst", defModule, defModule.TypeSystem.CoreLibrary));
            var optMod = new OptionalModifierType(modType, enumDef);
            Assert.True(DummyAssemblyGenerator.CanWriteConstant(optMod));

            // 3. Imported enum reference in consumer assembly
            using var consumerAssembly = AssemblyDefinition.CreateAssembly(
                new AssemblyNameDefinition("ConsumerAssembly", new Version(1, 0)),
                "ConsumerAssembly.dll",
                ModuleKind.Dll);
            var consumerModule = consumerAssembly.MainModule;
            var importedRef = consumerModule.ImportReference(enumDef);

            var consumerHolder = new TypeDefinition("Consumer", "Holder", TypeAttributes.Public, consumerModule.TypeSystem.Object);
            consumerModule.Types.Add(consumerHolder);
            var consumerField = new FieldDefinition("ConsumerF", FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal | FieldAttributes.HasDefault, importedRef);
            consumerHolder.Fields.Add(consumerField);

            bool canWrite = DummyAssemblyGenerator.CanWriteConstant(importedRef);
            DummyAssemblyGenerator.TrySetConstant(consumerField, importedRef, 42);
            DummyAssemblyExporter.SanitizeConstants(consumerAssembly);

            using var stream = new MemoryStream();
            consumerAssembly.Write(stream);
            Assert.True(stream.Length > 0);
        }

        [Fact]
        public void InvalidEnumUnderlyingTypes_AreRejected()
        {
            using var assembly = AssemblyDefinition.CreateAssembly(
                new AssemblyNameDefinition("InvalidUnderlyingTest", new Version(1, 0)),
                "InvalidUnderlyingTest.dll",
                ModuleKind.Dll);

            var m = assembly.MainModule;

            // Enum with string value__
            var stringEnum = new TypeDefinition("Test", "StringEnum", TypeAttributes.Public | TypeAttributes.Sealed, m.ImportReference(typeof(Enum)));
            m.Types.Add(stringEnum);
            stringEnum.Fields.Add(new FieldDefinition("value__", FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName, m.TypeSystem.String));
            var stringField = new FieldDefinition("F", FieldAttributes.Public, stringEnum);
            stringEnum.Fields.Add(stringField);

            Assert.False(DummyAssemblyGenerator.CanWriteConstant(stringEnum));
            Assert.False(DummyAssemblyGenerator.TrySetConstant(stringField, stringEnum, 7));

            // Enum with float value__
            var floatEnum = new TypeDefinition("Test", "FloatEnum", TypeAttributes.Public | TypeAttributes.Sealed, m.ImportReference(typeof(Enum)));
            m.Types.Add(floatEnum);
            floatEnum.Fields.Add(new FieldDefinition("value__", FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName, m.TypeSystem.Single));
            var floatField = new FieldDefinition("F", FieldAttributes.Public, floatEnum);
            floatEnum.Fields.Add(floatField);

            Assert.False(DummyAssemblyGenerator.CanWriteConstant(floatEnum));
            Assert.False(DummyAssemblyGenerator.TrySetConstant(floatField, floatEnum, 7));
        }

        [Fact]
        public void RealClrEnumInstances_AreNormalizedThroughUnderlyingPath()
        {
            using var assembly = AssemblyDefinition.CreateAssembly(
                new AssemblyNameDefinition("ClrEnumTest", new Version(1, 0)),
                "ClrEnumTest.dll",
                ModuleKind.Dll);

            var m = assembly.MainModule;
            var enumType = new TypeDefinition("Test", "TargetEnum", TypeAttributes.Public | TypeAttributes.Sealed, m.ImportReference(typeof(Enum)));
            m.Types.Add(enumType);
            enumType.Fields.Add(new FieldDefinition("value__", FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName, m.TypeSystem.Int32));

            var field = new FieldDefinition("F", FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal | FieldAttributes.HasDefault, enumType);
            enumType.Fields.Add(field);

            // AttributeTargets.Class has int value 4
            var accepted = DummyAssemblyGenerator.TrySetConstant(field, enumType, AttributeTargets.Class);
            Assert.True(accepted);
            Assert.Equal(4, field.Constant);
            Assert.IsType<int>(field.Constant);

            DummyAssemblyExporter.SanitizeConstants(assembly);
            Assert.True(field.HasConstant);
            Assert.Equal(4, field.Constant);

            using var stream = new MemoryStream();
            assembly.Write(stream);
            stream.Position = 0;

            using var read = AssemblyDefinition.ReadAssembly(stream);
            var readVal = read.MainModule.GetType("Test.TargetEnum").Fields[1].Constant;
            Assert.Equal(4, readVal);
            Assert.IsType<int>(readVal);
        }

        [Fact]
        public void Validation_RejectsGenuinelyUnsupportedConstantValues()
        {
            using var assembly = AssemblyDefinition.CreateAssembly(
                new AssemblyNameDefinition("InvalidConstantTest", new Version(1, 0)),
                "InvalidConstantTest.dll",
                ModuleKind.Dll);

            var module = assembly.MainModule;
            var enumType = new TypeDefinition(
                "Test",
                "ValidationEnum",
                TypeAttributes.Public | TypeAttributes.Sealed,
                module.ImportReference(typeof(Enum)));
            module.Types.Add(enumType);

            enumType.Fields.Add(new FieldDefinition(
                "value__",
                FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName,
                module.TypeSystem.Int32));

            var field = new FieldDefinition(
                "InvalidField",
                FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal | FieldAttributes.HasDefault,
                enumType);
            enumType.Fields.Add(field);

            // Null constant on enum must be rejected
            Assert.False(DummyAssemblyGenerator.TrySetConstant(field, enumType, null));

            // String constant on enum must be rejected
            Assert.False(DummyAssemblyGenerator.TrySetConstant(field, enumType, "not_an_int"));

            // Arbitrary object constant on enum must be rejected
            Assert.False(DummyAssemblyGenerator.TrySetConstant(field, enumType, new object()));

            // Struct (non-enum value type) constant must be rejected
            var structType = new TypeDefinition("Test", "MyStruct", TypeAttributes.Public | TypeAttributes.SequentialLayout | TypeAttributes.Sealed, module.ImportReference(typeof(ValueType)));
            module.Types.Add(structType);
            var structField = new FieldDefinition("StructField", FieldAttributes.Public, structType);
            structType.Fields.Add(structField);
            Assert.False(DummyAssemblyGenerator.TrySetConstant(structField, structType, 123));

            // Pre-existing invalid constant must be sanitized by SanitizeConstants
            field.Constant = "invalid_payload";
            Assert.True(field.HasConstant);

            DummyAssemblyExporter.SanitizeConstants(assembly);

            Assert.False(field.HasConstant, "Sanitizer should remove unsupported constant from enum field.");
            Assert.Null(field.Constant);
            Assert.False(field.Attributes.HasFlag(FieldAttributes.HasDefault));
        }

        [Fact]
        public void PrimitiveConstants_ArePreservedWithoutRegression()
        {
            using var assembly = AssemblyDefinition.CreateAssembly(
                new AssemblyNameDefinition("PrimitiveTest", new Version(1, 0)),
                "PrimitiveTest.dll",
                ModuleKind.Dll);

            var module = assembly.MainModule;
            var type = new TypeDefinition("Test", "Primitives", TypeAttributes.Public, module.TypeSystem.Object);
            module.Types.Add(type);

            var intField = new FieldDefinition("IntConst", FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal | FieldAttributes.HasDefault, module.TypeSystem.Int32);
            type.Fields.Add(intField);
            Assert.True(DummyAssemblyGenerator.TrySetConstant(intField, module.TypeSystem.Int32, 42));

            var strField = new FieldDefinition("StrConst", FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal | FieldAttributes.HasDefault, module.TypeSystem.String);
            type.Fields.Add(strField);
            Assert.True(DummyAssemblyGenerator.TrySetConstant(strField, module.TypeSystem.String, "hello"));

            var nullStrField = new FieldDefinition("NullStrConst", FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal | FieldAttributes.HasDefault, module.TypeSystem.String);
            type.Fields.Add(nullStrField);
            Assert.True(DummyAssemblyGenerator.TrySetConstant(nullStrField, module.TypeSystem.String, null));

            var boolField = new FieldDefinition("BoolConst", FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal | FieldAttributes.HasDefault, module.TypeSystem.Boolean);
            type.Fields.Add(boolField);
            Assert.True(DummyAssemblyGenerator.TrySetConstant(boolField, module.TypeSystem.Boolean, true));

            var floatField = new FieldDefinition("FloatConst", FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal | FieldAttributes.HasDefault, module.TypeSystem.Single);
            type.Fields.Add(floatField);
            Assert.True(DummyAssemblyGenerator.TrySetConstant(floatField, module.TypeSystem.Single, 3.14f));

            var doubleField = new FieldDefinition("DoubleConst", FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal | FieldAttributes.HasDefault, module.TypeSystem.Double);
            type.Fields.Add(doubleField);
            Assert.True(DummyAssemblyGenerator.TrySetConstant(doubleField, module.TypeSystem.Double, 2.71828));

            DummyAssemblyExporter.SanitizeConstants(assembly);

            Assert.Equal(42, intField.Constant);
            Assert.Equal("hello", strField.Constant);
            Assert.Null(nullStrField.Constant);
            Assert.Equal(true, boolField.Constant);
            Assert.Equal(3.14f, floatField.Constant);
            Assert.Equal(2.71828, doubleField.Constant);

            using var stream = new MemoryStream();
            assembly.Write(stream);
            Assert.True(stream.Length > 0);
        }

        private static TypeReference ResolveTypeReference(ModuleDefinition module, string fullName)
        {
            return fullName switch
            {
                "System.Byte" => module.TypeSystem.Byte,
                "System.SByte" => module.TypeSystem.SByte,
                "System.Int16" => module.TypeSystem.Int16,
                "System.UInt16" => module.TypeSystem.UInt16,
                "System.Int32" => module.TypeSystem.Int32,
                "System.UInt32" => module.TypeSystem.UInt32,
                "System.Int64" => module.TypeSystem.Int64,
                "System.UInt64" => module.TypeSystem.UInt64,
                _ => throw new ArgumentException("Unsupported type: " + fullName)
            };
        }
    }
}
