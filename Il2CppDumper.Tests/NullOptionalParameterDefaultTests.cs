using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Xunit;

namespace Il2CppDumper.Tests
{
    public class NullOptionalParameterDefaultTests
    {
        private static Metadata CreateEmptyMetadata()
        {
            var bytes = new byte[8192];
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

        private static Dictionary<int, Il2CppParameterDefaultValue> GetParameterDefaultValuesDic(Metadata m)
        {
            var field = typeof(Metadata).GetField("parameterDefaultValuesDic", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            return (Dictionary<int, Il2CppParameterDefaultValue>)field.GetValue(m)!;
        }

        [Fact]
        public void Issue7_ExactReproduction_EmitsParameterWithNullDefaultAfterName()
        {
            // Exact reproduction from Issue #7:
            // Method M(object arg = null)
            using var m = CreateEmptyMetadata();
            uint pos = 0;
            AddString(m, "", ref pos);
            var image = AddString(m, "Repro.dll", ref pos);
            var type = AddString(m, "Container", ref pos);
            var method = AddString(m, "M", ref pos);
            var parameter = AddString(m, "arg", ref pos);

            m.imageDefs = new[]
            {
                new Il2CppImageDefinition { nameIndex = image, typeCount = 1 }
            };
            m.typeDefs = new[]
            {
                new Il2CppTypeDefinition
                {
                    nameIndex = type,
                    declaringTypeIndex = -1,
                    parentIndex = -1,
                    genericContainerIndex = -1,
                    method_count = 1,
                    flags = 1
                }
            };
            m.methodDefs = new[]
            {
                new Il2CppMethodDefinition
                {
                    nameIndex = method,
                    returnType = 0,
                    parameterCount = 1,
                    parameterStart = 0,
                    genericContainerIndex = -1,
                    methodIndex = -1,
                    flags = 6,
                    slot = ushort.MaxValue
                }
            };
            m.parameterDefs = new[]
            {
                new Il2CppParameterDefinition { nameIndex = parameter, typeIndex = 1 }
            };

            var defaults = GetParameterDefaultValuesDic(m);
            defaults[0] = new Il2CppParameterDefaultValue
            {
                parameterIndex = 0,
                typeIndex = 1,
                dataIndex = 0
            };

            using var il = new WebAssemblyMemory(new MemoryStream(new byte[64]), 0);
            il.Version = 16;
            il.methodPointers = Array.Empty<ulong>();
            il.types = new[]
            {
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_VOID },
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_OBJECT }
            };

            var tempDir = Path.Combine(Path.GetTempPath(), "il2cpp-issue-7-repro-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                new Il2CppDecompiler(new Il2CppExecutor(m, il)).Decompile(
                    new Config { DumpAttribute = false, DumpProperty = false, DumpMethod = true },
                    tempDir + Path.DirectorySeparatorChar);

                var dumpLines = File.ReadAllLines(Path.Combine(tempDir, "dump.cs"));
                var methodLine = dumpLines.First(x => x.Contains(" M("));

                // Acceptance criteria: The reproduction emits M(object arg = null)
                Assert.Contains("public void M(object arg = null) { }", methodLine);
                Assert.DoesNotContain("nullobject", methodLine);
                Assert.DoesNotContain("arg = )", methodLine);
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
        public void Decompile_MultipleParametersWithNullAndNonNullDefaults_FormattedInCorrectPositions()
        {
            // Method Multi(int a = 42, object b = null, string c = "hello", object d = null)
            using var m = CreateEmptyMetadata();
            uint pos = 0;
            AddString(m, "", ref pos);
            var image = AddString(m, "MultiParam.dll", ref pos);
            var type = AddString(m, "Service", ref pos);
            var method = AddString(m, "Multi", ref pos);
            var p0 = AddString(m, "a", ref pos);
            var p1 = AddString(m, "b", ref pos);
            var p2 = AddString(m, "c", ref pos);
            var p3 = AddString(m, "d", ref pos);

            // Set up default value blob for 'a' (int 42) and 'c' (string "hello") in metadata.
            // string header stringOffset was 1024..3072. We can put blob data at offset 4000.
            m.header.fieldAndParameterDefaultValueDataOffset = 4000;
            m.header.fieldAndParameterDefaultValueDataSize = 1000;
            m.Position = 4000;
            m.Writer.Write(42); // dataIndex 0 for int: 42
            var strPos = (int)(m.Position - 4000);
            var strBytes = Encoding.UTF8.GetBytes("hello");
            m.Writer.Write(strBytes.Length);
            m.Writer.Write(strBytes);

            m.imageDefs = new[]
            {
                new Il2CppImageDefinition { nameIndex = image, typeCount = 1 }
            };
            m.typeDefs = new[]
            {
                new Il2CppTypeDefinition
                {
                    nameIndex = type,
                    declaringTypeIndex = -1,
                    parentIndex = -1,
                    genericContainerIndex = -1,
                    method_count = 1,
                    flags = 1
                }
            };
            m.methodDefs = new[]
            {
                new Il2CppMethodDefinition
                {
                    nameIndex = method,
                    returnType = 0,
                    parameterCount = 4,
                    parameterStart = 0,
                    genericContainerIndex = -1,
                    methodIndex = -1,
                    flags = 6,
                    slot = ushort.MaxValue
                }
            };
            m.parameterDefs = new[]
            {
                new Il2CppParameterDefinition { nameIndex = p0, typeIndex = 1 }, // int
                new Il2CppParameterDefinition { nameIndex = p1, typeIndex = 2 }, // object
                new Il2CppParameterDefinition { nameIndex = p2, typeIndex = 3 }, // string
                new Il2CppParameterDefinition { nameIndex = p3, typeIndex = 2 }  // object
            };

            var defaults = GetParameterDefaultValuesDic(m);
            defaults[0] = new Il2CppParameterDefaultValue { parameterIndex = 0, typeIndex = 1, dataIndex = 0 }; // a = 42
            defaults[1] = new Il2CppParameterDefaultValue { parameterIndex = 1, typeIndex = 2, dataIndex = 0 }; // b = null (object)
            defaults[2] = new Il2CppParameterDefaultValue { parameterIndex = 2, typeIndex = 3, dataIndex = strPos }; // c = "hello"
            defaults[3] = new Il2CppParameterDefaultValue { parameterIndex = 3, typeIndex = 2, dataIndex = 0 }; // d = null (object)

            using var il = new WebAssemblyMemory(new MemoryStream(new byte[64]), 0);
            il.Version = 16;
            il.methodPointers = Array.Empty<ulong>();
            il.types = new[]
            {
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_VOID },
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_I4 },
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_OBJECT },
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_STRING }
            };

            var tempDir = Path.Combine(Path.GetTempPath(), "il2cpp-issue-7-multi-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                new Il2CppDecompiler(new Il2CppExecutor(m, il)).Decompile(
                    new Config { DumpAttribute = false, DumpProperty = false, DumpMethod = true },
                    tempDir + Path.DirectorySeparatorChar);

                var dumpLines = File.ReadAllLines(Path.Combine(tempDir, "dump.cs"));
                var methodLine = dumpLines.First(x => x.Contains(" Multi("));

                Assert.Equal("public void Multi(int a = 42, object b = null, string c = \"hello\", object d = null) { }", methodLine.Trim());
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
        public void Decompile_AbstractMethodWithNullDefault_EmitsSemicolonAndCorrectParameters()
        {
            // Abstract method: public abstract void Run(object state = null);
            using var m = CreateEmptyMetadata();
            uint pos = 0;
            AddString(m, "", ref pos);
            var image = AddString(m, "Abstract.dll", ref pos);
            var type = AddString(m, "BaseTask", ref pos);
            var method = AddString(m, "Run", ref pos);
            var paramName = AddString(m, "state", ref pos);

            m.imageDefs = new[]
            {
                new Il2CppImageDefinition { nameIndex = image, typeCount = 1 }
            };
            m.typeDefs = new[]
            {
                new Il2CppTypeDefinition
                {
                    nameIndex = type,
                    declaringTypeIndex = -1,
                    parentIndex = -1,
                    genericContainerIndex = -1,
                    method_count = 1,
                    flags = 1
                }
            };
            m.methodDefs = new[]
            {
                // METHOD_ATTRIBUTE_ABSTRACT = 0x0400 | METHOD_ATTRIBUTE_PUBLIC = 0x0006
                new Il2CppMethodDefinition
                {
                    nameIndex = method,
                    returnType = 0,
                    parameterCount = 1,
                    parameterStart = 0,
                    genericContainerIndex = -1,
                    methodIndex = -1,
                    flags = 0x0406,
                    slot = ushort.MaxValue
                }
            };
            m.parameterDefs = new[]
            {
                new Il2CppParameterDefinition { nameIndex = paramName, typeIndex = 1 }
            };

            var defaults = GetParameterDefaultValuesDic(m);
            defaults[0] = new Il2CppParameterDefaultValue { parameterIndex = 0, typeIndex = 1, dataIndex = 0 };

            using var il = new WebAssemblyMemory(new MemoryStream(new byte[64]), 0);
            il.Version = 16;
            il.methodPointers = Array.Empty<ulong>();
            il.types = new[]
            {
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_VOID },
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_OBJECT }
            };

            var tempDir = Path.Combine(Path.GetTempPath(), "il2cpp-issue-7-abstract-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                new Il2CppDecompiler(new Il2CppExecutor(m, il)).Decompile(
                    new Config { DumpAttribute = false, DumpProperty = false, DumpMethod = true },
                    tempDir + Path.DirectorySeparatorChar);

                var dumpLines = File.ReadAllLines(Path.Combine(tempDir, "dump.cs"));
                var methodLine = dumpLines.First(x => x.Contains(" Run("));

                Assert.Contains("void Run(object state = null);", methodLine);
                Assert.EndsWith(");", methodLine.Trim());
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
