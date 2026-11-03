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
