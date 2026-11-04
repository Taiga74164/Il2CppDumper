using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Il2CppDumper.Tests
{
    public class CliExitCodeTests
    {
        private static string GetBuildOutputDir()
        {
            var loc = typeof(Program).Assembly.Location;
            return Path.GetDirectoryName(loc)!;
        }

        private static string SetupIsolatedApp(string rootDir, Action<Config>? configure = null)
        {
            var sourceDir = GetBuildOutputDir();
            var appDir = Path.Combine(rootDir, "app");
            Directory.CreateDirectory(appDir);

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var fileName = Path.GetFileName(file);
                File.Copy(file, Path.Combine(appDir, fileName), true);
            }

            var configPath = Path.Combine(appDir, "config.json");
            var config = JsonSerializer.Deserialize<Config>(File.ReadAllText(configPath))!;
            config.RequireAnyKey = false;
            configure?.Invoke(config);
            File.WriteAllText(configPath, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));

            return appDir;
        }

        private static byte[] CreateMinimalMetadata()
        {
            var bytes = new byte[1024];
            BitConverter.GetBytes(0xFAB11BAFu).CopyTo(bytes, 0); // magic
            BitConverter.GetBytes(29).CopyTo(bytes, 4);           // version
            BitConverter.GetBytes(256).CopyTo(bytes, 160);        // typeDefinitions offset
            BitConverter.GetBytes(88).CopyTo(bytes, 164);         // typeDefinitions size
            return bytes;
        }

        private static (Metadata metadata, WebAssemblyMemory il2Cpp) CreateSyntheticEnvironment(int returnType = 0, bool invalidImageName = false)
        {
            var bytes = new byte[4096];
            BitConverter.GetBytes(0xFAB11BAFu).CopyTo(bytes, 0);
            BitConverter.GetBytes(29).CopyTo(bytes, 4);
            var m = new Metadata(new MemoryStream(bytes));
            m.header.stringOffset = 512;
            m.header.stringSize = 2048;
            uint pos = 0;
            uint Text(string s)
            {
                var result = pos;
                var b = Encoding.UTF8.GetBytes(s + "\0");
                m.Position = m.header.stringOffset + pos;
                m.Writer.Write(b);
                pos += (uint)b.Length;
                return result;
            }
            Text("");
            var image = Text("Repro.dll");
            var type = Text("Container");
            var method = Text("M");
            m.imageDefs = new[] { new Il2CppImageDefinition { nameIndex = invalidImageName ? 99999u : image, assemblyIndex = -1, typeCount = 1 } };
            m.typeDefs = new[] { new Il2CppTypeDefinition { nameIndex = type, declaringTypeIndex = -1, parentIndex = -1, genericContainerIndex = -1, method_count = 1, flags = 1 } };
            m.methodDefs = new[] { new Il2CppMethodDefinition { nameIndex = method, returnType = returnType, genericContainerIndex = -1, methodIndex = -1, flags = 6, slot = ushort.MaxValue } };
            var il = new WebAssemblyMemory(new MemoryStream(new byte[64]), 0);
            il.Version = 16;
            il.methodPointers = Array.Empty<ulong>();
            il.types = new[] { new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_VOID } };
            return (m, il);
        }

        private static bool InvokeProgramDump(Metadata metadata, Il2Cpp il2Cpp, string outputDir, Action<Config>? configure = null)
        {
            var programType = typeof(Program);
            var configField = programType.GetField("config", BindingFlags.NonPublic | BindingFlags.Static)!;
            var originalConfig = configField.GetValue(null);
            try
            {
                var cfg = new Config
                {
                    GenerateStruct = false,
                    GenerateDummyDll = false,
                    RequireAnyKey = false,
                    DumpAttribute = false,
                    DumpField = false,
                    DumpProperty = false,
                    DumpMethod = true,
                    DumpMethodOffset = false
                };
                configure?.Invoke(cfg);
                configField.SetValue(null, cfg);

                var dumpMethod = programType.GetMethod("Dump", BindingFlags.NonPublic | BindingFlags.Static)!;
                return (bool)dumpMethod.Invoke(null, new object[] { metadata, il2Cpp, outputDir })!;
            }
            finally
            {
                configField.SetValue(null, originalConfig);
            }
        }

        private static (int exitCode, string stdout, string stderr) RunCli(string appDir, params string[] args)
        {
            var dllPath = Path.Combine(appDir, "Il2CppDumper.dll");
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"\"{dllPath}\" {string.Join(" ", Array.ConvertAll(args, a => $"\"{a}\""))}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                WorkingDirectory = appDir
            };

            using var process = Process.Start(psi)!;
            process.StandardInput.Close();
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(30000);
            return (process.ExitCode, stdout, stderr);
        }

        [Fact]
        public void Issue8_ExactReproduction_UnsupportedBinaryFixture_ExitsNonZero()
        {
            // Exact reproduction from Issue #8:
            // "bad.bin" with "BAD!" header and synthetic metadata.dat
            var tempDir = Path.Combine(Path.GetTempPath(), "il2cpp-issue-8-repro-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var appDir = SetupIsolatedApp(tempDir);
                var fixturesDir = Path.Combine(tempDir, "fixtures");
                Directory.CreateDirectory(fixturesDir);

                var metadataPath = Path.Combine(fixturesDir, "metadata.dat");
                File.WriteAllBytes(metadataPath, CreateMinimalMetadata());

                var badBinPath = Path.Combine(fixturesDir, "bad.bin");
                File.WriteAllBytes(badBinPath, Encoding.ASCII.GetBytes("BAD!"));

                var (exitCode, stdout, _) = RunCli(appDir, badBinPath, metadataPath);

                Assert.Contains("System.NotSupportedException: ERROR: il2cpp file not supported.", stdout);
                Assert.NotEqual(0, exitCode);
                Assert.Equal(1, exitCode);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }

        [Theory]
        [InlineData("-h")]
        [InlineData("--help")]
        [InlineData("/?")]
        [InlineData("/h")]
        public void Cli_HelpRequests_RetainSuccessBehaviorWithExitCodeZero(string helpFlag)
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "il2cpp-issue-8-help-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var appDir = SetupIsolatedApp(tempDir);
                var (exitCode, stdout, _) = RunCli(appDir, helpFlag);

                Assert.Equal(0, exitCode);
                Assert.Contains("usage:", stdout, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }

        [Fact]
        public void Cli_ExcessArguments_ExitsNonZero()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "il2cpp-issue-8-excess-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var appDir = SetupIsolatedApp(tempDir);
                var (exitCode, stdout, _) = RunCli(appDir, "arg1", "arg2", "arg3", "arg4");

                Assert.Equal(1, exitCode);
                Assert.Contains("usage:", stdout, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }

        [Fact]
        public void Cli_MissingMetadataFile_ExitsNonZero()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "il2cpp-issue-8-missingmeta-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var appDir = SetupIsolatedApp(tempDir);
                var binPath1 = Path.Combine(tempDir, "file1.bin");
                var binPath2 = Path.Combine(tempDir, "file2.bin");
                File.WriteAllBytes(binPath1, new byte[64]); // not metadata (magic != 0xFAB11BAF)
                File.WriteAllBytes(binPath2, new byte[64]); // not metadata (magic != 0xFAB11BAF)

                // Pass two files where neither is metadata
                var (exitCode, stdout, _) = RunCli(appDir, binPath1, binPath2);

                Assert.Equal(1, exitCode);
                Assert.Contains("ERROR: Metadata file not found or encrypted.", stdout);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }

        [Fact]
        public void Cli_InitFailure_UnreadableMetadata_ExitsNonZero()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "il2cpp-issue-8-initfail-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var appDir = SetupIsolatedApp(tempDir);
                var fixturesDir = Path.Combine(tempDir, "fixtures");
                Directory.CreateDirectory(fixturesDir);

                // Metadata with magic but only 4 bytes (truncated, reading version throws EndOfStreamException)
                var metaPath = Path.Combine(fixturesDir, "corrupt-metadata.dat");
                File.WriteAllBytes(metaPath, BitConverter.GetBytes(0xFAB11BAFu));

                var binPath = Path.Combine(fixturesDir, "libil2cpp.so");
                File.WriteAllBytes(binPath, new byte[] { 0x7f, (byte)'E', (byte)'L', (byte)'F' });

                var (exitCode, stdout, stderr) = RunCli(appDir, binPath, metaPath);

                Assert.Equal(1, exitCode);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }

        [Fact]
        public void Cli_InitFailure_AutoSearchFails_ExitsNonZero()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "il2cpp-issue-8-searchfail-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var appDir = SetupIsolatedApp(tempDir);
                var fixturesDir = Path.Combine(tempDir, "fixtures");
                Directory.CreateDirectory(fixturesDir);

                var metaPath = Path.Combine(fixturesDir, "metadata.dat");
                File.WriteAllBytes(metaPath, CreateMinimalMetadata());

                // Minimal 8-byte WebAssembly binary (\0asm + version 1) with no code/metadata registration
                var wasmPath = Path.Combine(fixturesDir, "program.wasm");
                var wasmBytes = new byte[] { 0x00, 0x61, 0x73, 0x6d, 0x01, 0x00, 0x00, 0x00 };
                File.WriteAllBytes(wasmPath, wasmBytes);

                var (exitCode, stdout, _) = RunCli(appDir, wasmPath, metaPath, tempDir + Path.DirectorySeparatorChar);

                Assert.Equal(1, exitCode);
                Assert.Contains("ERROR: An error occurred while processing.", stdout);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }

        [Fact]
        public void Decompiler_InternalDumpingError_ReturnsFalse_AndPreservesPartialOutput()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "il2cpp-decompiler-err-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var env = CreateSyntheticEnvironment(returnType: 999);
                using var m = env.metadata;
                using var il = env.il2Cpp;
                var executor = new Il2CppExecutor(m, il);
                var decompiler = new Il2CppDecompiler(executor);
                var config = new Config
                {
                    DumpAttribute = false,
                    DumpField = false,
                    DumpProperty = false,
                    DumpMethod = true,
                    DumpMethodOffset = false
                };

                var success = decompiler.Decompile(config, tempDir + Path.DirectorySeparatorChar);

                Assert.False(success);
                var dumpPath = Path.Combine(tempDir, "dump.cs");
                Assert.True(File.Exists(dumpPath));
                var dumpContent = File.ReadAllText(dumpPath);
                Assert.Contains("IndexOutOfRangeException", dumpContent);
                Assert.Contains("// Image 0: Repro.dll", dumpContent);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }

        [Fact]
        public void Decompiler_ValidDump_ReturnsTrue_AndEmitsNoErrors()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "il2cpp-decompiler-ok-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var env = CreateSyntheticEnvironment(returnType: 0);
                using var m = env.metadata;
                using var il = env.il2Cpp;
                var executor = new Il2CppExecutor(m, il);
                var decompiler = new Il2CppDecompiler(executor);
                var config = new Config
                {
                    DumpAttribute = false,
                    DumpField = false,
                    DumpProperty = false,
                    DumpMethod = true,
                    DumpMethodOffset = false
                };

                var success = decompiler.Decompile(config, tempDir + Path.DirectorySeparatorChar);

                Assert.True(success);
                var dumpPath = Path.Combine(tempDir, "dump.cs");
                Assert.True(File.Exists(dumpPath));
                var dumpContent = File.ReadAllText(dumpPath);
                Assert.DoesNotContain("ERROR: Some errors in dumping", dumpContent);
                Assert.Contains("public class Container", dumpContent);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }

        [Fact]
        public void ProgramDump_InternalDecompilerError_ReturnsFalse_EvenWhenGeneratorsDisabled()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "il2cpp-dump-err-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var env = CreateSyntheticEnvironment(returnType: 999);
                using var m = env.metadata;
                using var il = env.il2Cpp;
                var success = InvokeProgramDump(m, il, tempDir + Path.DirectorySeparatorChar, cfg =>
                {
                    cfg.GenerateStruct = false;
                    cfg.GenerateDummyDll = false;
                });

                Assert.False(success);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }

        [Fact]
        public void ProgramDump_ValidControl_ReturnsTrue()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "il2cpp-dump-ok-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var env = CreateSyntheticEnvironment(returnType: 0);
                using var m = env.metadata;
                using var il = env.il2Cpp;
                var success = InvokeProgramDump(m, il, tempDir + Path.DirectorySeparatorChar, cfg =>
                {
                    cfg.GenerateStruct = false;
                    cfg.GenerateDummyDll = false;
                });

                Assert.True(success);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }

        [Fact]
        public void ProgramDump_StructGeneratorFailure_ReturnsFalse()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "il2cpp-dump-struct-fail-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var env = CreateSyntheticEnvironment(returnType: 0, invalidImageName: true);
                using var m = env.metadata;
                using var il = env.il2Cpp;
                var success = InvokeProgramDump(m, il, tempDir + Path.DirectorySeparatorChar, cfg =>
                {
                    cfg.GenerateStruct = true;
                    cfg.GenerateDummyDll = false;
                });

                Assert.False(success);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }

        [Fact]
        public void ProgramDump_DummyDllFailure_ReturnsFalse()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "il2cpp-dump-dummydll-fail-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var env = CreateSyntheticEnvironment(returnType: 0);
                using var m = env.metadata;
                using var il = env.il2Cpp;
                // Decompiler with DumpField = false ignores fieldDefs,
                // while DummyAssemblyGenerator unconditionally accesses fieldDefs when field_count > 0.
                m.typeDefs[0].field_count = 1;
                m.fieldDefs = null!;

                var success = InvokeProgramDump(m, il, tempDir + Path.DirectorySeparatorChar, cfg =>
                {
                    cfg.GenerateStruct = false;
                    cfg.GenerateDummyDll = true;
                });

                Assert.False(success);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }
    }
}
