using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Il2CppDumper;
using System.Reflection;
using Xunit;

namespace Il2CppDumper.Tests
{
    public class CustomAttributeDecodingTests
    {
        private static (Metadata metadata, WebAssemblyMemory il2Cpp, Il2CppImageDefinition imageDef) CreateEnvironment(byte[] blob)
        {
            var bytes = new byte[8192];
            BitConverter.GetBytes(0xFAB11BAFu).CopyTo(bytes, 0); // magic
            BitConverter.GetBytes(29).CopyTo(bytes, 4);           // version 29
            var m = new Metadata(new MemoryStream(bytes))
            {
                Version = 29
            };
            m.header.stringOffset = 512;
            m.header.stringSize = 1024;
            m.header.attributeDataOffset = 2048;

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
                nameIndex = WriteStr("TargetClass"),
                namespaceIndex = WriteStr("TestNamespace"),
                byvalTypeIndex = 0,
                fieldStart = 0,
                field_count = 1,
                declaringTypeIndex = -1,
                parentIndex = -1,
                genericContainerIndex = -1
            };

            var validAttrTypeDef = new Il2CppTypeDefinition
            {
                nameIndex = WriteStr("ValidAttribute"),
                namespaceIndex = WriteStr("TestNamespace"),
                byvalTypeIndex = 1,
                declaringTypeIndex = -1,
                parentIndex = -1,
                genericContainerIndex = -1
            };

            var malformedAttrTypeDef = new Il2CppTypeDefinition
            {
                nameIndex = WriteStr("MalformedAttribute"),
                namespaceIndex = WriteStr("TestNamespace"),
                byvalTypeIndex = 2,
                declaringTypeIndex = -1,
                parentIndex = -1,
                genericContainerIndex = -1
            };

            var fieldDef = new Il2CppFieldDefinition
            {
                nameIndex = WriteStr("targetField"),
                typeIndex = 0,
                token = 0x04000001
            };

            m.typeDefs = new[] { typeDef, validAttrTypeDef, malformedAttrTypeDef };
            m.fieldDefs = new[] { fieldDef };
            m.methodDefs = Array.Empty<Il2CppMethodDefinition>();
            m.propertyDefs = Array.Empty<Il2CppPropertyDefinition>();
            m.parameterDefs = Array.Empty<Il2CppParameterDefinition>();

            var validMethodDef = new Il2CppMethodDefinition
            {
                nameIndex = WriteStr(".ctor"),
                declaringType = 1,
                returnType = 0,
                parameterStart = 0,
                parameterCount = 0
            };
            var malformedMethodDef = new Il2CppMethodDefinition
            {
                nameIndex = WriteStr(".ctor"),
                declaringType = 2,
                returnType = 0,
                parameterStart = 0,
                parameterCount = 0
            };

            m.attributeTypeRanges = new[]
            {
                new Il2CppCustomAttributeTypeRange
                {
                    token = 0x04000001,
                    start = 0,
                    count = 2
                }
            };

            m.attributeTypes = new[] { 0, 1 };

            var dummyWriterMethodDef = new Il2CppMethodDefinition[2];
            dummyWriterMethodDef[0] = validMethodDef;
            dummyWriterMethodDef[1] = malformedMethodDef;
            m.methodDefs = dummyWriterMethodDef;

            m.Position = m.header.attributeDataOffset;
            m.Writer.Write(blob);

            m.attributeDataRanges = new[]
            {
                new Il2CppCustomAttributeDataRange { startOffset = 0 },
                new Il2CppCustomAttributeDataRange { startOffset = (uint)blob.Length }
            };

            var imageDef = new Il2CppImageDefinition
            {
                nameIndex = WriteStr("TestAssembly.dll"),
                typeStart = 0,
                typeCount = 1
            };
            m.imageDefs = new[] { imageDef };

            var dic = new Dictionary<Il2CppImageDefinition, Dictionary<uint, int>>
            {
                {
                    imageDef,
                    new Dictionary<uint, int>
                    {
                        { 0x04000001, 0 }
                    }
                }
            };
            typeof(Metadata).GetField("attributeTypeRangesDic", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(m, dic);

            var il = new WebAssemblyMemory(new MemoryStream(new byte[256]), 0)
            {
                Version = 29,
                types = new[]
                {
                    new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_I4 },
                    new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_CLASS },
                    new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_CLASS }
                }
            };

            return (m, il, imageDef);
        }

        private static byte[] CreateSyntheticBlobWithValidAndMalformed()
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            // Count = 2
            bw.Write((byte)2);
            // ctorIndex 0 (ValidAttribute)
            bw.Write((int)0);
            // ctorIndex 1 (MalformedAttribute)
            bw.Write((int)1);

            // Entry 0 (Valid): 0 arguments, 0 fields, 0 properties
            bw.Write((byte)0);
            bw.Write((byte)0);
            bw.Write((byte)0);

            // Entry 1 (Malformed): 1 argument with invalid type byte (0xFF)
            bw.Write((byte)1);
            bw.Write((byte)0);
            bw.Write((byte)0);
            bw.Write((byte)0xFF);

            return ms.ToArray();
        }

        private static byte[] CreateSyntheticValidBlob()
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            // Count = 1
            bw.Write((byte)1);
            // ctorIndex 0
            bw.Write((int)0);
            // Entry 0 (Valid): 0 arguments, 0 fields, 0 properties
            bw.Write((byte)0);
            bw.Write((byte)0);
            bw.Write((byte)0);
            return ms.ToArray();
        }

        [Fact]
        public void GetCustomAttribute_WithValidAndMalformedEntries_WritesValidAndSkipsMalformedWithWarningCaptured()
        {
            var blob = CreateSyntheticBlobWithValidAndMalformed();
            var (metadata, il2Cpp, imageDef) = CreateEnvironment(blob);
            var executor = new Il2CppExecutor(metadata, il2Cpp);
            var decompiler = new Il2CppDecompiler(executor);

            var capturedWarnings = new List<string>();
            decompiler.log = new DecompilerLogger(msg => capturedWarnings.Add(msg));

            var output = decompiler.GetCustomAttribute(imageDef, 0, 0x04000001, padding: "", owner: "TargetClass.targetField");

            // 1. Valid attribute must be written
            Assert.Contains("[Valid]", output);

            // 2. Malformed attribute must be skipped
            Assert.DoesNotContain("[Malformed]", output);

            // 3. Warning must be captured with attribute type name and owner
            Assert.Single(capturedWarnings);
            Assert.Contains("MalformedAttribute", capturedWarnings[0]);
            Assert.Contains("TargetClass.targetField", capturedWarnings[0]);

            // 4. Counts must accurately track written vs failed
            Assert.Equal(1, decompiler.CustomAttributeWrittenCount);
            Assert.Equal(1, decompiler.CustomAttributeFailedCount);
        }

        [Fact]
        public void Decompile_WithFailedAttributes_EmitsRunSummaryWithCounts()
        {
            var blob = CreateSyntheticBlobWithValidAndMalformed();
            var (metadata, il2Cpp, _) = CreateEnvironment(blob);
            var executor = new Il2CppExecutor(metadata, il2Cpp);
            var decompiler = new Il2CppDecompiler(executor);

            var capturedWarnings = new List<string>();
            decompiler.log = new DecompilerLogger(msg => capturedWarnings.Add(msg));

            var tempDir = Path.Combine(Path.GetTempPath(), "il2cpp_test_" + Guid.NewGuid().ToString("N")) + Path.DirectorySeparatorChar;
            Directory.CreateDirectory(tempDir);
            try
            {
                var config = new Config
                {
                    DumpAttribute = true,
                    DumpField = true,
                    DumpFieldOffset = false,
                    DumpTypeDefIndex = false,
                    DumpMethod = false,
                    DumpProperty = false,
                    GenerateDummyDll = false,
                    GenerateStruct = false
                };

                var success = decompiler.Decompile(config, tempDir);
                Assert.True(success);

                // Summary must be emitted because CustomAttributeFailedCount > 0
                Assert.Contains(capturedWarnings, w => w.Contains("Custom attribute decoding completed") &&
                                                       w.Contains("1 failure(s)") &&
                                                       w.Contains("1 written") &&
                                                       w.Contains("1 skipped"));
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [Fact]
        public void Decompile_SuccessPath_DoesNotEmitSummaryOrWarnings()
        {
            var blob = CreateSyntheticValidBlob();
            var (metadata, il2Cpp, _) = CreateEnvironment(blob);
            var executor = new Il2CppExecutor(metadata, il2Cpp);
            var decompiler = new Il2CppDecompiler(executor);

            var capturedWarnings = new List<string>();
            decompiler.log = new DecompilerLogger(msg => capturedWarnings.Add(msg));

            var tempDir = Path.Combine(Path.GetTempPath(), "il2cpp_test_" + Guid.NewGuid().ToString("N")) + Path.DirectorySeparatorChar;
            Directory.CreateDirectory(tempDir);
            try
            {
                var config = new Config
                {
                    DumpAttribute = true,
                    DumpField = true,
                    DumpFieldOffset = false,
                    DumpTypeDefIndex = false,
                    DumpMethod = false,
                    DumpProperty = false,
                    GenerateDummyDll = false,
                    GenerateStruct = false
                };

                var success = decompiler.Decompile(config, tempDir);
                Assert.True(success);

                // Success path: no warnings or failure summary emitted
                Assert.Empty(capturedWarnings);
                Assert.Equal(1, decompiler.CustomAttributeWrittenCount);
                Assert.Equal(0, decompiler.CustomAttributeFailedCount);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [Fact]
        public void GetCustomAttribute_WithoutExplicitOwner_ResolvesOwnerFromToken()
        {
            var blob = CreateSyntheticBlobWithValidAndMalformed();
            var (metadata, il2Cpp, imageDef) = CreateEnvironment(blob);
            var executor = new Il2CppExecutor(metadata, il2Cpp);
            var decompiler = new Il2CppDecompiler(executor);

            var capturedWarnings = new List<string>();
            decompiler.log = new DecompilerLogger(msg => capturedWarnings.Add(msg));

            // Owner omitted: should resolve from token 0x04000001 (fieldDef -> "targetField")
            var output = decompiler.GetCustomAttribute(imageDef, 0, 0x04000001, padding: "");

            Assert.Contains("[Valid]", output);
            Assert.Single(capturedWarnings);
            Assert.Contains("targetField", capturedWarnings[0]);
        }
    }
}
