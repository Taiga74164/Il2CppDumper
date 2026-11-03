using System;
using System.IO;
using System.Reflection;
using System.Text;
using Il2CppDumper;
using Xunit;
using static Il2CppDumper.Il2CppConstants;

namespace Il2CppDumper.Tests
{
    public class EnumUnderlyingTypeTests
    {
        private static Metadata CreateEmptyMetadata(double version = 35)
        {
            var bytes = new byte[2048];
            BitConverter.GetBytes(0xFAB11BAFu).CopyTo(bytes, 0);
            BitConverter.GetBytes(29).CopyTo(bytes, 4);
            var m = new Metadata(new MemoryStream(bytes));
            m.header.stringOffset = 512;
            m.header.stringSize = 1024;
            if (version >= 38)
            {
                m.header.strings = new Il2CppSectionMetadata { offset = 512, sectionSize = 1024 };
            }
            m.Version = version;
            return m;
        }

        private static void WriteString(Metadata m, uint offset, string text)
        {
            var utf8 = Encoding.UTF8.GetBytes(text + "\0");
            var target = 512 + (int)offset;
            m.Position = (ulong)target;
            m.Writer.Write(utf8);
            m.Position = 0;
        }

        private static WebAssemblyMemory CreateMemory(double version = 35)
        {
            var memory = new WebAssemblyMemory(new MemoryStream(new byte[256]), 0)
            {
                Version = version
            };
            return memory;
        }

        private static string InvokeParseType(StructGenerator generator, Il2CppType type, Il2CppGenericContext? context = null)
        {
            var method = typeof(StructGenerator).GetMethod("ParseType", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            return (string)method.Invoke(generator, new object?[] { type, context })!;
        }

        private static bool InvokeIsCustomType(StructGenerator generator, Il2CppType type, Il2CppGenericContext? context = null)
        {
            var method = typeof(StructGenerator).GetMethod("IsCustomType", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            return (bool)method.Invoke(generator, new object?[] { type, context })!;
        }

        [Fact]
        public void Reproduction_FromIssue2_ResolvesU1_ConsumesOneByte_EmitsUint8()
        {
            using var m = CreateEmptyMetadata(version: 35);
            m.typeDefs = new[]
            {
                new Il2CppTypeDefinition
                {
                    bitfield = 3,
                    fieldStart = 0,
                    field_count = 1
                }
            };
            m.fieldDefs = new[]
            {
                new Il2CppFieldDefinition { typeIndex = 2 }
            };

            using var il = CreateMemory(version: 35);
            il.types = new[]
            {
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_I4 },
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE, data = new Il2CppType.Union { dummy = 0 } },
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_U1 }
            };

            var executor = new Il2CppExecutor(m, il);

            // Payload: IL2CPP_TYPE_ENUM, compressed typeIndex 1 (encoded as 2), byte value 0x80, trailing bytes 0x22, 0xBB, 0xCC
            using var reader = new BinaryReader(new MemoryStream(new byte[] { (byte)Il2CppTypeEnum.IL2CPP_TYPE_ENUM, 2, 0x80, 0x22, 0xBB, 0xCC }));
            var decoded = executor.ReadEncodedTypeEnum(reader, out var enumType);
            Assert.Equal(Il2CppTypeEnum.IL2CPP_TYPE_U1, decoded);
            Assert.Same(il.types[1], enumType);

            var readSuccess = executor.GetConstantValueFromBlob(decoded, reader, out var blobValue);
            Assert.True(readSuccess);
            Assert.Equal((byte)0x80, blobValue.Value);
            Assert.Equal(3L, reader.BaseStream.Position);

            var generator = new StructGenerator(executor);
            var headerType = InvokeParseType(generator, il.types[1]);
            Assert.Equal("uint8_t", headerType);
        }

        [Theory]
        [InlineData(35.0)]
        [InlineData(38.0)]
        [InlineData(39.0)]
        [InlineData(108.0)]
        [InlineData(110.0)]
        public void GetEnumUnderlyingType_PostV31Versions_ResolvesFromFieldDefs(double version)
        {
            using var m = CreateEmptyMetadata(version);
            m.typeDefs = new[]
            {
                new Il2CppTypeDefinition
                {
                    bitfield = 3, // IsEnum == true
                    fieldStart = 0,
                    field_count = 1,
                    elementTypeIndex = 0 // left at default 0
                }
            };
            m.fieldDefs = new[]
            {
                new Il2CppFieldDefinition { typeIndex = 2 }
            };

            using var il = CreateMemory(version);
            il.types = new[]
            {
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_I4 }, // index 0 (should NOT be picked)
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE, data = new Il2CppType.Union { dummy = 0 } },
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_U2 }  // index 2 (actual enum underlying type)
            };

            var executor = new Il2CppExecutor(m, il);
            var resolvedType = executor.GetEnumUnderlyingType(m.typeDefs[0]);

            Assert.NotNull(resolvedType);
            Assert.Same(il.types[2], resolvedType);
            Assert.Equal(Il2CppTypeEnum.IL2CPP_TYPE_U2, resolvedType.type);

            var generator = new StructGenerator(executor);
            var headerType = InvokeParseType(generator, il.types[1]);
            Assert.Equal("uint16_t", headerType);
        }

        [Theory]
        [InlineData(24.0)]
        [InlineData(27.0)]
        [InlineData(29.0)]
        [InlineData(31.0)]
        public void GetEnumUnderlyingType_PreV35Versions_ResolvesFromElementTypeIndex(double version)
        {
            using var m = CreateEmptyMetadata(version);
            m.typeDefs = new[]
            {
                new Il2CppTypeDefinition
                {
                    bitfield = 3, // IsEnum == true
                    fieldStart = 0,
                    field_count = 1,
                    elementTypeIndex = 2 // explicitly points to underlying type index 2
                }
            };
            m.fieldDefs = new[]
            {
                new Il2CppFieldDefinition { typeIndex = 0 } // index 0 in fieldDefs
            };

            using var il = CreateMemory(version);
            il.types = new[]
            {
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_I4 },
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE, data = new Il2CppType.Union { dummy = 0 } },
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_I1 } // index 2 (stored in elementTypeIndex)
            };

            var executor = new Il2CppExecutor(m, il);
            var resolvedType = executor.GetEnumUnderlyingType(m.typeDefs[0]);

            Assert.NotNull(resolvedType);
            Assert.Same(il.types[2], resolvedType);
            Assert.Equal(Il2CppTypeEnum.IL2CPP_TYPE_I1, resolvedType.type);

            var generator = new StructGenerator(executor);
            var headerType = InvokeParseType(generator, il.types[1]);
            Assert.Equal("int8_t", headerType);
        }

        [Fact]
        public void GetEnumUnderlyingType_PreV35_InvalidElementTypeIndex_FallsBackToFieldDefs()
        {
            using var m = CreateEmptyMetadata(29.0);
            m.typeDefs = new[]
            {
                new Il2CppTypeDefinition
                {
                    bitfield = 3,
                    fieldStart = 0,
                    field_count = 1,
                    elementTypeIndex = -1 // invalid index
                }
            };
            m.fieldDefs = new[]
            {
                new Il2CppFieldDefinition { typeIndex = 2 }
            };

            using var il = CreateMemory(29.0);
            il.types = new[]
            {
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_I4 },
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE, data = new Il2CppType.Union { dummy = 0 } },
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_U4 }
            };

            var executor = new Il2CppExecutor(m, il);
            var resolvedType = executor.GetEnumUnderlyingType(m.typeDefs[0]);

            Assert.NotNull(resolvedType);
            Assert.Same(il.types[2], resolvedType);
            Assert.Equal(Il2CppTypeEnum.IL2CPP_TYPE_U4, resolvedType.type);
        }

        [Theory]
        [InlineData(Il2CppTypeEnum.IL2CPP_TYPE_I1, "int8_t", 1)]
        [InlineData(Il2CppTypeEnum.IL2CPP_TYPE_U1, "uint8_t", 1)]
        [InlineData(Il2CppTypeEnum.IL2CPP_TYPE_I2, "int16_t", 2)]
        [InlineData(Il2CppTypeEnum.IL2CPP_TYPE_U2, "uint16_t", 2)]
        [InlineData(Il2CppTypeEnum.IL2CPP_TYPE_I4, "int32_t", 4)]
        [InlineData(Il2CppTypeEnum.IL2CPP_TYPE_U4, "uint32_t", 4)]
        [InlineData(Il2CppTypeEnum.IL2CPP_TYPE_I8, "int64_t", 8)]
        [InlineData(Il2CppTypeEnum.IL2CPP_TYPE_U8, "uint64_t", 8)]
        public void ReadEncodedTypeEnum_VariousUnderlyingScalarTypes_ConsumesExpectedBytesAndEmitsCorrectCHeader(
            Il2CppTypeEnum expectedTypeEnum, string expectedCHeader, int expectedBlobBytes)
        {
            using var m = CreateEmptyMetadata(version: 38);
            WriteString(m, 10, "value__");
            m.typeDefs = new[]
            {
                new Il2CppTypeDefinition
                {
                    bitfield = 3,
                    fieldStart = 0,
                    field_count = 1,
                    elementTypeIndex = 0
                }
            };
            m.fieldDefs = new[]
            {
                new Il2CppFieldDefinition
                {
                    nameIndex = 10,
                    typeIndex = 2
                }
            };

            using var il = CreateMemory(version: 38);
            il.types = new[]
            {
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_I4 }, // index 0 (wrong)
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE, data = new Il2CppType.Union { dummy = 0 } },
                new Il2CppType { type = expectedTypeEnum } // index 2
            };

            var executor = new Il2CppExecutor(m, il);

            // Construct payload with IL2CPP_TYPE_ENUM, typeIndex = 1 (encoded as 2 in compressed int)
            var blob = new byte[32];
            blob[0] = (byte)Il2CppTypeEnum.IL2CPP_TYPE_ENUM;
            blob[1] = 2; // decodes to 1
            for (int b = 2; b < blob.Length; b++)
            {
                blob[b] = 0x42;
            }

            using var reader = new BinaryReader(new MemoryStream(blob));
            var decoded = executor.ReadEncodedTypeEnum(reader, out var enumType);
            Assert.Equal(expectedTypeEnum, decoded);
            Assert.Same(il.types[1], enumType);

            var startPos = reader.BaseStream.Position; // 2
            var blobRead = executor.GetConstantValueFromBlob(decoded, reader, out _);
            Assert.True(blobRead);
            var consumed = reader.BaseStream.Position - startPos;

            // Note: For 32-bit types (I4/U4) on il2cpp v29+, ReadCompressedInt32/UInt32 is used.
            // 0x42 has the high bit 0, so compressed format consumes 1 byte.
            // For 1-byte, 2-byte, 8-byte types, standard byte width is consumed.
            if (expectedTypeEnum is Il2CppTypeEnum.IL2CPP_TYPE_I4 or Il2CppTypeEnum.IL2CPP_TYPE_U4)
            {
                Assert.Equal(1L, consumed); // 0x42 single-byte compressed integer
            }
            else
            {
                Assert.Equal(expectedBlobBytes, consumed);
            }

            var generator = new StructGenerator(executor);
            var cHeader = InvokeParseType(generator, il.types[1]);
            Assert.Equal(expectedCHeader, cHeader);
        }

        [Fact]
        public void EnumWithMultipleFields_PrefersNonStaticNamedValueFieldOverStaticLiterals()
        {
            using var m = CreateEmptyMetadata(version: 35);
            WriteString(m, 10, "OptionA");
            WriteString(m, 20, "value__");
            WriteString(m, 30, "OptionB");

            m.typeDefs = new[]
            {
                new Il2CppTypeDefinition
                {
                    bitfield = 3,
                    fieldStart = 0,
                    field_count = 3,
                    elementTypeIndex = 0
                }
            };
            m.fieldDefs = new[]
            {
                // Field 0: Static literal field
                new Il2CppFieldDefinition { nameIndex = 10, typeIndex = 1 },
                // Field 1: Instance field value__
                new Il2CppFieldDefinition { nameIndex = 20, typeIndex = 3 },
                // Field 2: Static literal field
                new Il2CppFieldDefinition { nameIndex = 30, typeIndex = 1 }
            };

            using var il = CreateMemory(version: 35);
            il.types = new[]
            {
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_I4 }, // index 0
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE, attrs = FIELD_ATTRIBUTE_STATIC | FIELD_ATTRIBUTE_LITERAL, data = new Il2CppType.Union { dummy = 0 } }, // index 1
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE, data = new Il2CppType.Union { dummy = 0 } }, // index 2 (the enum itself)
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_U8, attrs = 0 } // index 3 (underlying type)
            };

            var executor = new Il2CppExecutor(m, il);
            var resolvedType = executor.GetEnumUnderlyingType(m.typeDefs[0]);

            Assert.NotNull(resolvedType);
            Assert.Same(il.types[3], resolvedType);
            Assert.Equal(Il2CppTypeEnum.IL2CPP_TYPE_U8, resolvedType.type);

            var generator = new StructGenerator(executor);
            var header = InvokeParseType(generator, il.types[2]);
            Assert.Equal("uint64_t", header);
        }

        [Fact]
        public void EnumWithMultipleFields_WithoutValueName_PrefersNonStaticOverStatic()
        {
            using var m = CreateEmptyMetadata(version: 39);
            m.typeDefs = new[]
            {
                new Il2CppTypeDefinition
                {
                    bitfield = 3,
                    fieldStart = 0,
                    field_count = 2,
                    elementTypeIndex = 0
                }
            };
            m.fieldDefs = new[]
            {
                // Field 0: Static literal
                new Il2CppFieldDefinition { nameIndex = 0, typeIndex = 1 },
                // Field 1: Non-static
                new Il2CppFieldDefinition { nameIndex = 0, typeIndex = 2 }
            };

            using var il = CreateMemory(version: 39);
            il.types = new[]
            {
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_I4 }, // index 0
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_I4, attrs = FIELD_ATTRIBUTE_STATIC | FIELD_ATTRIBUTE_LITERAL }, // index 1
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_I2, attrs = 0 } // index 2 (underlying)
            };

            var executor = new Il2CppExecutor(m, il);
            var resolvedType = executor.GetEnumUnderlyingType(m.typeDefs[0]);

            Assert.NotNull(resolvedType);
            Assert.Same(il.types[2], resolvedType);
            Assert.Equal(Il2CppTypeEnum.IL2CPP_TYPE_I2, resolvedType.type);
        }

        [Fact]
        public void StructGenerator_IsCustomType_ReturnsFalseForEnumWithPrimitiveUnderlyingType()
        {
            using var m = CreateEmptyMetadata(version: 35);
            m.typeDefs = new[]
            {
                new Il2CppTypeDefinition
                {
                    bitfield = 3,
                    fieldStart = 0,
                    field_count = 1
                }
            };
            m.fieldDefs = new[]
            {
                new Il2CppFieldDefinition { typeIndex = 2 }
            };

            using var il = CreateMemory(version: 35);
            il.types = new[]
            {
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_I4 },
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE, data = new Il2CppType.Union { dummy = 0 } },
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_U1 }
            };

            var executor = new Il2CppExecutor(m, il);
            var generator = new StructGenerator(executor);

            var isCustom = InvokeIsCustomType(generator, il.types[1]);
            Assert.False(isCustom);
        }

        [Fact]
        public void EdgeCases_NullTypeDef_ReturnsNull()
        {
            using var m = CreateEmptyMetadata(version: 35);
            using var il = CreateMemory(version: 35);
            il.types = Array.Empty<Il2CppType>();

            var executor = new Il2CppExecutor(m, il);
            var result = executor.GetEnumUnderlyingType(null!);
            Assert.Null(result);
        }

        [Fact]
        public void EdgeCases_NoFields_ReturnsFallbackIfElementTypeIndexValid()
        {
            using var m = CreateEmptyMetadata(version: 35);
            m.typeDefs = new[]
            {
                new Il2CppTypeDefinition
                {
                    bitfield = 3,
                    fieldStart = 0,
                    field_count = 0,
                    elementTypeIndex = 1
                }
            };
            m.fieldDefs = Array.Empty<Il2CppFieldDefinition>();

            using var il = CreateMemory(version: 35);
            il.types = new[]
            {
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_I4 },
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_U1 }
            };

            var executor = new Il2CppExecutor(m, il);
            var result = executor.GetEnumUnderlyingType(m.typeDefs[0]);
            Assert.NotNull(result);
            Assert.Same(il.types[1], result);
        }

        [Fact]
        public void EdgeCases_InvalidFieldTypeIndex_DoesNotThrow()
        {
            using var m = CreateEmptyMetadata(version: 35);
            m.typeDefs = new[]
            {
                new Il2CppTypeDefinition
                {
                    bitfield = 3,
                    fieldStart = 0,
                    field_count = 1,
                    elementTypeIndex = -1
                }
            };
            m.fieldDefs = new[]
            {
                new Il2CppFieldDefinition { typeIndex = 999 } // out of range
            };

            using var il = CreateMemory(version: 35);
            il.types = new[]
            {
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_I4 }
            };

            var executor = new Il2CppExecutor(m, il);
            var result = executor.GetEnumUnderlyingType(m.typeDefs[0]);
            Assert.Null(result);
        }
    }
}
