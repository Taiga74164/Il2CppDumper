using System;
using System.IO;
using System.Linq;
using System.Text;
using Mono.Cecil;
using Xunit;

namespace Il2CppDumper.Tests
{
    public class WriteOnlyIndexerTypeTests
    {
        private static Metadata CreateEmptyMetadata()
        {
            var bytes = new byte[4096];
            BitConverter.GetBytes(0xFAB11BAFu).CopyTo(bytes, 0);
            BitConverter.GetBytes(29).CopyTo(bytes, 4);
            var m = new Metadata(new MemoryStream(bytes));
            m.header.stringOffset = 1024;
            m.header.stringSize = 2048;
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

        [Fact]
        public void Issue6_ExactReproduction_WriteOnlyIndexer_InfersValueTypeAndPreservesIndexParameter()
        {
            // Exact reproduction from Issue #6:
            // Type "Container" with write-only indexer "Item" with setter (int key, string value).
            using var m = CreateEmptyMetadata();
            uint pos = 0;
            AddString(m, "", ref pos);
            var imageName = AddString(m, "Repro.dll", ref pos);
            var typeName = AddString(m, "Container", ref pos);
            var setterName = AddString(m, "set_Item", ref pos);
            var itemName = AddString(m, "Item", ref pos);
            var keyName = AddString(m, "key", ref pos);
            var valueName = AddString(m, "value", ref pos);

            m.imageDefs = new[]
            {
                new Il2CppImageDefinition { nameIndex = imageName, assemblyIndex = -1, typeCount = 1 }
            };
            m.typeDefs = new[]
            {
                new Il2CppTypeDefinition
                {
                    nameIndex = typeName,
                    declaringTypeIndex = -1,
                    parentIndex = -1,
                    genericContainerIndex = -1,
                    method_count = 1,
                    property_count = 1,
                    flags = 1
                }
            };
            m.methodDefs = new[]
            {
                new Il2CppMethodDefinition
                {
                    nameIndex = setterName,
                    returnType = 0,
                    parameterCount = 2,
                    parameterStart = 0,
                    genericContainerIndex = -1,
                    methodIndex = -1,
                    flags = 0x886,
                    slot = ushort.MaxValue
                }
            };
            m.parameterDefs = new[]
            {
                new Il2CppParameterDefinition { nameIndex = keyName, typeIndex = 1 },
                new Il2CppParameterDefinition { nameIndex = valueName, typeIndex = 2 }
            };
            m.propertyDefs = new[]
            {
                new Il2CppPropertyDefinition { nameIndex = itemName, get = -1, set = 0 }
            };

            using var il = new WebAssemblyMemory(new MemoryStream(new byte[64]), 0);
            il.Version = 16;
            il.methodPointers = Array.Empty<ulong>();
            il.types = new[]
            {
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_VOID },
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_I4 },
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_STRING }
            };

            var executor = new Il2CppExecutor(m, il);

            // 1. Verify DummyDll generator
            var generator = new DummyAssemblyGenerator(executor, false);
            var property = generator.Assemblies[1].MainModule.Types[0].Properties[0];

            Assert.Equal("System.String", property.PropertyType.FullName);
            Assert.Single(property.Parameters);
            Assert.Equal("System.Int32", property.Parameters[0].ParameterType.FullName);
            Assert.Equal("System.String Container::Item(System.Int32)", property.FullName);

            // 2. Verify Il2CppDecompiler text output
            var tempDir = Path.Combine(Path.GetTempPath(), "il2cpp-issue-6-test-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                new Il2CppDecompiler(executor).Decompile(new Config { DumpAttribute = false, DumpProperty = true }, tempDir + Path.DirectorySeparatorChar);
                var dumpLines = File.ReadAllLines(Path.Combine(tempDir, "dump.cs"));
                var itemLine = dumpLines.First(x => x.Contains(" Item {"));

                Assert.Contains("string Item { set; }", itemLine);
                Assert.DoesNotContain("int Item { set; }", itemLine);
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
        public void WriteOnlyIndexer_MultiParameter_InfersValueTypeAndPreservesAllIndexParameters()
        {
            // Indexer with 3 index parameters and 1 value parameter:
            // set_Item(int row, string col, bool flag, double value)
            using var m = CreateEmptyMetadata();
            uint pos = 0;
            AddString(m, "", ref pos);
            var imageName = AddString(m, "MultiIndex.dll", ref pos);
            var typeName = AddString(m, "MultiGrid", ref pos);
            var setterName = AddString(m, "set_Item", ref pos);
            var itemName = AddString(m, "Item", ref pos);
            var p0 = AddString(m, "row", ref pos);
            var p1 = AddString(m, "col", ref pos);
            var p2 = AddString(m, "flag", ref pos);
            var p3 = AddString(m, "value", ref pos);

            m.imageDefs = new[]
            {
                new Il2CppImageDefinition { nameIndex = imageName, assemblyIndex = -1, typeCount = 1 }
            };
            m.typeDefs = new[]
            {
                new Il2CppTypeDefinition
                {
                    nameIndex = typeName,
                    declaringTypeIndex = -1,
                    parentIndex = -1,
                    genericContainerIndex = -1,
                    method_count = 1,
                    property_count = 1,
                    flags = 1
                }
            };
            m.methodDefs = new[]
            {
                new Il2CppMethodDefinition
                {
                    nameIndex = setterName,
                    returnType = 0,
                    parameterCount = 4,
                    parameterStart = 0,
                    genericContainerIndex = -1,
                    methodIndex = -1,
                    flags = 0x886,
                    slot = ushort.MaxValue
                }
            };
            m.parameterDefs = new[]
            {
                new Il2CppParameterDefinition { nameIndex = p0, typeIndex = 1 }, // int
                new Il2CppParameterDefinition { nameIndex = p1, typeIndex = 2 }, // string
                new Il2CppParameterDefinition { nameIndex = p2, typeIndex = 3 }, // bool
                new Il2CppParameterDefinition { nameIndex = p3, typeIndex = 4 }  // double (value)
            };
            m.propertyDefs = new[]
            {
                new Il2CppPropertyDefinition { nameIndex = itemName, get = -1, set = 0 }
            };

            using var il = new WebAssemblyMemory(new MemoryStream(new byte[64]), 0);
            il.Version = 16;
            il.methodPointers = Array.Empty<ulong>();
            il.types = new[]
            {
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_VOID },
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_I4 },
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_STRING },
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_BOOLEAN },
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_R8 }
            };

            var executor = new Il2CppExecutor(m, il);

            var generator = new DummyAssemblyGenerator(executor, false);
            var property = generator.Assemblies[1].MainModule.Types[0].Properties[0];

            Assert.Equal("System.Double", property.PropertyType.FullName);
            Assert.Equal(3, property.Parameters.Count);
            Assert.Equal("System.Int32", property.Parameters[0].ParameterType.FullName);
            Assert.Equal("System.String", property.Parameters[1].ParameterType.FullName);
            Assert.Equal("System.Boolean", property.Parameters[2].ParameterType.FullName);
            Assert.Equal("System.Double MultiGrid::Item(System.Int32,System.String,System.Boolean)", property.FullName);

            var tempDir = Path.Combine(Path.GetTempPath(), "il2cpp-issue-6-multi-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                new Il2CppDecompiler(executor).Decompile(new Config { DumpAttribute = false, DumpProperty = true }, tempDir + Path.DirectorySeparatorChar);
                var dumpLines = File.ReadAllLines(Path.Combine(tempDir, "dump.cs"));
                var itemLine = dumpLines.First(x => x.Contains(" Item {"));

                Assert.Contains("double Item { set; }", itemLine);
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
        public void OrdinaryWriteOnlyProperty_SingleParameter_RetainsExpectedType()
        {
            // Ordinary write-only property: set_Title(string value)
            using var m = CreateEmptyMetadata();
            uint pos = 0;
            AddString(m, "", ref pos);
            var imageName = AddString(m, "Ordinary.dll", ref pos);
            var typeName = AddString(m, "Book", ref pos);
            var setterName = AddString(m, "set_Title", ref pos);
            var propName = AddString(m, "Title", ref pos);
            var valName = AddString(m, "value", ref pos);

            m.imageDefs = new[]
            {
                new Il2CppImageDefinition { nameIndex = imageName, assemblyIndex = -1, typeCount = 1 }
            };
            m.typeDefs = new[]
            {
                new Il2CppTypeDefinition
                {
                    nameIndex = typeName,
                    declaringTypeIndex = -1,
                    parentIndex = -1,
                    genericContainerIndex = -1,
                    method_count = 1,
                    property_count = 1,
                    flags = 1
                }
            };
            m.methodDefs = new[]
            {
                new Il2CppMethodDefinition
                {
                    nameIndex = setterName,
                    returnType = 0,
                    parameterCount = 1,
                    parameterStart = 0,
                    genericContainerIndex = -1,
                    methodIndex = -1,
                    flags = 0x886,
                    slot = ushort.MaxValue
                }
            };
            m.parameterDefs = new[]
            {
                new Il2CppParameterDefinition { nameIndex = valName, typeIndex = 1 } // string
            };
            m.propertyDefs = new[]
            {
                new Il2CppPropertyDefinition { nameIndex = propName, get = -1, set = 0 }
            };

            using var il = new WebAssemblyMemory(new MemoryStream(new byte[64]), 0);
            il.Version = 16;
            il.methodPointers = Array.Empty<ulong>();
            il.types = new[]
            {
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_VOID },
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_STRING }
            };

            var executor = new Il2CppExecutor(m, il);

            var generator = new DummyAssemblyGenerator(executor, false);
            var property = generator.Assemblies[1].MainModule.Types[0].Properties[0];

            Assert.Equal("System.String", property.PropertyType.FullName);
            Assert.Empty(property.Parameters);
            Assert.Equal("System.String Book::Title()", property.FullName);

            var tempDir = Path.Combine(Path.GetTempPath(), "il2cpp-issue-6-ordinary-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                new Il2CppDecompiler(executor).Decompile(new Config { DumpAttribute = false, DumpProperty = true }, tempDir + Path.DirectorySeparatorChar);
                var dumpLines = File.ReadAllLines(Path.Combine(tempDir, "dump.cs"));
                var propLine = dumpLines.First(x => x.Contains(" Title {"));

                Assert.Contains("string Title { set; }", propLine);
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
        public void ReadWriteProperty_And_ReadWriteIndexer_RetainGetterReturnType()
        {
            // Type with both a read-write property and a read-write indexer.
            // Getter return type should govern the property type.
            using var m = CreateEmptyMetadata();
            uint pos = 0;
            AddString(m, "", ref pos);
            var imageName = AddString(m, "ReadWrite.dll", ref pos);
            var typeName = AddString(m, "RWContainer", ref pos);
            var getPropName = AddString(m, "get_Count", ref pos);
            var setPropName = AddString(m, "set_Count", ref pos);
            var propName = AddString(m, "Count", ref pos);
            var getIndexName = AddString(m, "get_Item", ref pos);
            var setIndexName = AddString(m, "set_Item", ref pos);
            var indexName = AddString(m, "Item", ref pos);
            var keyParamName = AddString(m, "key", ref pos);
            var valParamName = AddString(m, "value", ref pos);

            m.imageDefs = new[]
            {
                new Il2CppImageDefinition { nameIndex = imageName, assemblyIndex = -1, typeCount = 1 }
            };
            m.typeDefs = new[]
            {
                new Il2CppTypeDefinition
                {
                    nameIndex = typeName,
                    declaringTypeIndex = -1,
                    parentIndex = -1,
                    genericContainerIndex = -1,
                    method_count = 4,
                    property_count = 2,
                    flags = 1
                }
            };
            m.methodDefs = new[]
            {
                // method 0: int get_Count()
                new Il2CppMethodDefinition { nameIndex = getPropName, returnType = 1, parameterCount = 0, parameterStart = 0, genericContainerIndex = -1, methodIndex = -1, flags = 0x886, slot = ushort.MaxValue },
                // method 1: void set_Count(int value)
                new Il2CppMethodDefinition { nameIndex = setPropName, returnType = 0, parameterCount = 1, parameterStart = 0, genericContainerIndex = -1, methodIndex = -1, flags = 0x886, slot = ushort.MaxValue },
                // method 2: string get_Item(int key)
                new Il2CppMethodDefinition { nameIndex = getIndexName, returnType = 2, parameterCount = 1, parameterStart = 1, genericContainerIndex = -1, methodIndex = -1, flags = 0x886, slot = ushort.MaxValue },
                // method 3: void set_Item(int key, string value)
                new Il2CppMethodDefinition { nameIndex = setIndexName, returnType = 0, parameterCount = 2, parameterStart = 2, genericContainerIndex = -1, methodIndex = -1, flags = 0x886, slot = ushort.MaxValue },
            };
            m.parameterDefs = new[]
            {
                new Il2CppParameterDefinition { nameIndex = valParamName, typeIndex = 1 }, // for set_Count (int)
                new Il2CppParameterDefinition { nameIndex = keyParamName, typeIndex = 1 }, // for get_Item (int)
                new Il2CppParameterDefinition { nameIndex = keyParamName, typeIndex = 1 }, // for set_Item key (int)
                new Il2CppParameterDefinition { nameIndex = valParamName, typeIndex = 2 }  // for set_Item value (string)
            };
            m.propertyDefs = new[]
            {
                new Il2CppPropertyDefinition { nameIndex = propName, get = 0, set = 1 },
                new Il2CppPropertyDefinition { nameIndex = indexName, get = 2, set = 3 }
            };

            using var il = new WebAssemblyMemory(new MemoryStream(new byte[64]), 0);
            il.Version = 16;
            il.methodPointers = Array.Empty<ulong>();
            il.types = new[]
            {
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_VOID },
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_I4 },
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_STRING }
            };

            var executor = new Il2CppExecutor(m, il);

            var generator = new DummyAssemblyGenerator(executor, false);
            var properties = generator.Assemblies[1].MainModule.Types[0].Properties;

            var countProp = properties[0];
            Assert.Equal("System.Int32", countProp.PropertyType.FullName);
            Assert.NotNull(countProp.GetMethod);
            Assert.NotNull(countProp.SetMethod);

            var itemProp = properties[1];
            Assert.Equal("System.String", itemProp.PropertyType.FullName);
            Assert.Single(itemProp.Parameters);
            Assert.Equal("System.Int32", itemProp.Parameters[0].ParameterType.FullName);
            Assert.NotNull(itemProp.GetMethod);
            Assert.NotNull(itemProp.SetMethod);

            var tempDir = Path.Combine(Path.GetTempPath(), "il2cpp-issue-6-rw-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                new Il2CppDecompiler(executor).Decompile(new Config { DumpAttribute = false, DumpProperty = true }, tempDir + Path.DirectorySeparatorChar);
                var dumpLines = File.ReadAllLines(Path.Combine(tempDir, "dump.cs"));
                var countLine = dumpLines.First(x => x.Contains(" Count {"));
                var itemLine = dumpLines.First(x => x.Contains(" Item {"));

                Assert.Contains("int Count { get; set; }", countLine);
                Assert.Contains("string Item { get; set; }", itemLine);
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
        public void WriteOnlyProperty_ZeroParameterSetter_DoesNotThrowAndHandlesGracefully()
        {
            // Corrupt metadata edge-case: setter with 0 parameters
            using var m = CreateEmptyMetadata();
            uint pos = 0;
            AddString(m, "", ref pos);
            var imageName = AddString(m, "ZeroParam.dll", ref pos);
            var typeName = AddString(m, "Corrupt", ref pos);
            var setterName = AddString(m, "set_Bad", ref pos);
            var propName = AddString(m, "Bad", ref pos);

            m.imageDefs = new[]
            {
                new Il2CppImageDefinition { nameIndex = imageName, assemblyIndex = -1, typeCount = 1 }
            };
            m.typeDefs = new[]
            {
                new Il2CppTypeDefinition
                {
                    nameIndex = typeName,
                    declaringTypeIndex = -1,
                    parentIndex = -1,
                    genericContainerIndex = -1,
                    method_count = 1,
                    property_count = 1,
                    flags = 1
                }
            };
            m.methodDefs = new[]
            {
                new Il2CppMethodDefinition
                {
                    nameIndex = setterName,
                    returnType = 0,
                    parameterCount = 0, // zero parameters
                    parameterStart = 0,
                    genericContainerIndex = -1,
                    methodIndex = -1,
                    flags = 0x886,
                    slot = ushort.MaxValue
                }
            };
            m.parameterDefs = Array.Empty<Il2CppParameterDefinition>();
            m.propertyDefs = new[]
            {
                new Il2CppPropertyDefinition { nameIndex = propName, get = -1, set = 0 }
            };

            using var il = new WebAssemblyMemory(new MemoryStream(new byte[64]), 0);
            il.Version = 16;
            il.methodPointers = Array.Empty<ulong>();
            il.types = new[]
            {
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_VOID }
            };

            var executor = new Il2CppExecutor(m, il);

            // DummyAssemblyGenerator should not throw ArgumentOutOfRangeException
            var generator = new DummyAssemblyGenerator(executor, false);
            var property = generator.Assemblies[1].MainModule.Types[0].Properties[0];
            Assert.NotNull(property);

            // Il2CppDecompiler should not throw ArgumentOutOfRangeException
            var tempDir = Path.Combine(Path.GetTempPath(), "il2cpp-issue-6-zeroparam-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                new Il2CppDecompiler(executor).Decompile(new Config { DumpAttribute = false, DumpProperty = true }, tempDir + Path.DirectorySeparatorChar);
                var dumpLines = File.ReadAllLines(Path.Combine(tempDir, "dump.cs"));
                var badLine = dumpLines.First(x => x.Contains(" Bad {"));

                Assert.Contains("void Bad { set; }", badLine);
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
}
