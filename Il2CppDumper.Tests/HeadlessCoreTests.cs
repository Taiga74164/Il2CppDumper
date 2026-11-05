using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using Il2CppDumper.Core;
using Xunit;

namespace Il2CppDumper.Tests
{
    public class HeadlessCoreTests
    {
        private static byte[] CreateMinimalMetadata(int version = 29)
        {
            var bytes = new byte[1024];
            BitConverter.GetBytes(0xFAB11BAFu).CopyTo(bytes, 0); // magic
            BitConverter.GetBytes(version).CopyTo(bytes, 4);           // version
            BitConverter.GetBytes(256).CopyTo(bytes, 160);        // typeDefinitions offset
            BitConverter.GetBytes(88).CopyTo(bytes, 164);         // typeDefinitions size
            return bytes;
        }

        private static byte[] WriteULeb128(int value)
        {
            var list = new List<byte>();
            while (value > 127)
            {
                list.Add((byte)((value & 127) | 128));
                value >>= 7;
            }
            list.Add((byte)value);
            return list.ToArray();
        }

        private static byte[] CreateSyntheticWasm()
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            // WebAssembly magic and version 1
            w.Write(new byte[] { 0x00, 0x61, 0x73, 0x6d, 0x01, 0x00, 0x00, 0x00 });
            // Section 5: Memory section (1 page)
            w.Write(new byte[] { 0x05, 0x03, 0x01, 0x00, 0x01 });
            // Section 11: Data section
            var payload = new List<byte>();
            payload.AddRange(new byte[] { 0x01, 0x00, 0x41, 0x00, 0x0b }); // count=1, idx=0, i32.const, offset=0, end
            var data = new byte[4096];
            payload.AddRange(WriteULeb128(data.Length));
            payload.AddRange(data);
            w.Write((byte)11);
            w.Write(WriteULeb128(payload.Count));
            w.Write(payload.ToArray());
            return ms.ToArray();
        }

        private static (string wasmPath, string metadataPath) CreateSyntheticFixtures(string fixturesDir)
        {
            var metaPath = Path.Combine(fixturesDir, "metadata.dat");
            File.WriteAllBytes(metaPath, CreateMinimalMetadata());

            var wasmPath = Path.Combine(fixturesDir, "empty.wasm");
            File.WriteAllBytes(wasmPath, CreateSyntheticWasm());

            var scriptPath = Path.Combine(fixturesDir, "script.json");
            File.WriteAllText(scriptPath, JsonSerializer.Serialize(new
            {
                codeRegistration = 32UL,
                metadataRegistration = 256UL
            }));

            return (wasmPath, metaPath);
        }

        private static string GetBuildOutputDir()
        {
            var loc = typeof(Program).Assembly.Location;
            return Path.GetDirectoryName(loc)!;
        }

        private static string SetupIsolatedApp(string rootDir)
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
            File.WriteAllText(configPath, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));

            return appDir;
        }

        private static (int exitCode, string stdout, string stderr) RunCliWithInput(string appDir, string? stdin, params string[] args)
        {
            var dllPath = Path.Combine(appDir, "Il2CppDumper.dll");
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                WorkingDirectory = appDir
            };
            psi.ArgumentList.Add(dllPath);
            foreach (var arg in args)
            {
                psi.ArgumentList.Add(arg);
            }

            using var process = Process.Start(psi)!;
            if (stdin != null)
            {
                process.StandardInput.Write(stdin);
                process.StandardInput.Flush();
            }
            process.StandardInput.Close();
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(30000);
            return (process.ExitCode, stdout, stderr);
        }

        private static (int exitCode, string stdout, string stderr) RunCli(string appDir, params string[] args)
        {
            return RunCliWithInput(appDir, null, args);
        }

        private static byte[] CreateSyntheticFatMacho()
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            // Magic 0xCAFEBABE (big endian)
            w.Write(new byte[] { 0xCA, 0xFE, 0xBA, 0xBE });
            // Count = 2 (big endian)
            w.Write(new byte[] { 0x00, 0x00, 0x00, 0x02 });

            // Arch 0: 64-bit at offset 64, size 64
            w.Write(new byte[8]); // cputype, cpusubtype
            w.Write(new byte[] { 0x00, 0x00, 0x00, 0x40 }); // offset 64
            w.Write(new byte[] { 0x00, 0x00, 0x00, 0x40 }); // size 64
            w.Write(new byte[4]); // align

            // Arch 1: 32-bit at offset 128, size 64
            w.Write(new byte[8]);
            w.Write(new byte[] { 0x00, 0x00, 0x00, 0x80 }); // offset 128
            w.Write(new byte[] { 0x00, 0x00, 0x00, 0x40 }); // size 64
            w.Write(new byte[4]); // align

            // Pad to offset 64
            while (ms.Position < 64) w.Write((byte)0);
            // Arch 0 payload: 0xFEEDFACF (little endian uint32)
            w.Write(0xFEEDFACFu);
            while (ms.Position < 128) w.Write((byte)0);
            // Arch 1 payload: 0xFEEDFACE (little endian uint32)
            w.Write(0xFEEDFACEu);
            while (ms.Position < 192) w.Write((byte)0);

            return ms.ToArray();
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

        [Fact]
        public void Library_ArgumentValidation_ThrowsOnlyOnArgumentErrors()
        {
            Assert.Throws<ArgumentNullException>(() => Dumper.Dump(null!));
            Assert.Throws<ArgumentException>(() => Dumper.Dump(new DumpRequest { ExecutablePath = "", MetadataPath = "meta.dat" }));
            Assert.Throws<ArgumentException>(() => Dumper.Dump(new DumpRequest { ExecutablePath = "   ", MetadataPath = "meta.dat" }));
            Assert.Throws<ArgumentException>(() => Dumper.Dump(new DumpRequest { ExecutablePath = "bin.exe", MetadataPath = "" }));
            Assert.Throws<ArgumentException>(() => Dumper.Dump(new DumpRequest { ExecutablePath = "bin.exe", MetadataPath = "   " }));
        }

        [Fact]
        public void Library_EmptyStdoutCapture_DuringSilentLibraryRun()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "il2cpp-core-silent-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var fixturesDir = Path.Combine(tempDir, "fixtures");
                Directory.CreateDirectory(fixturesDir);
                (var wasmPath, var metaPath) = CreateSyntheticFixtures(fixturesDir);
                var outDir = Path.Combine(tempDir, "out");

                var originalOut = Console.Out;
                using var capturedStdout = new StringWriter();
                try
                {
                    Console.SetOut(capturedStdout);

                    var request = new DumpRequest
                    {
                        ExecutablePath = wasmPath,
                        MetadataPath = metaPath,
                        OutputDirectory = outDir,
                        Config = new Config { RequireAnyKey = true },
                        Log = null // silent
                    };

                    var result = Dumper.Dump(request);
                    Assert.True(result.Success);
                    Assert.Null(result.Error);
                }
                finally
                {
                    Console.SetOut(originalOut);
                }

                // Acceptance: Il2CppDumper.Core contains no Console I/O; test asserts empty stdout capture during a library run.
                Assert.Empty(capturedStdout.ToString());
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
        public void Library_InjectableLogSink_CapturesLogsWithoutConsolePollution()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "il2cpp-core-sink-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var fixturesDir = Path.Combine(tempDir, "fixtures");
                Directory.CreateDirectory(fixturesDir);
                (var wasmPath, var metaPath) = CreateSyntheticFixtures(fixturesDir);
                var outDir = Path.Combine(tempDir, "out");

                var originalOut = Console.Out;
                using var capturedConsole = new StringWriter();
                using var customSink = new StringWriter();
                try
                {
                    Console.SetOut(capturedConsole);

                    var request = new DumpRequest
                    {
                        ExecutablePath = wasmPath,
                        MetadataPath = metaPath,
                        OutputDirectory = outDir,
                        Config = new Config { RequireAnyKey = false },
                        Log = customSink
                    };

                    var result = Dumper.Dump(request);
                    Assert.True(result.Success);
                }
                finally
                {
                    Console.SetOut(originalOut);
                }

                Assert.Empty(capturedConsole.ToString());
                var customLog = customSink.ToString();
                Assert.Contains("Initializing metadata...", customLog);
                Assert.Contains("Metadata Version: 29", customLog);
                Assert.Contains("Dumping...", customLog);
                Assert.Contains("Done!", customLog);
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
        public void Library_InitFailure_UnsupportedBinary_MatchesCliFailureText()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "il2cpp-core-badbin-" + Guid.NewGuid());
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

                var cliOutDir = Path.Combine(tempDir, "cli-out");
                var libOutDir = Path.Combine(tempDir, "lib-out");

                var (cliExitCode, cliStdout, _) = RunCli(appDir, badBinPath, metadataPath, cliOutDir);

                var request = new DumpRequest
                {
                    ExecutablePath = badBinPath,
                    MetadataPath = metadataPath,
                    OutputDirectory = libOutDir,
                    Log = null
                };

                var result = Dumper.Dump(request);

                Assert.Equal(1, cliExitCode);
                Assert.False(result.Success);
                Assert.NotNull(result.Error);
                Assert.Contains("ERROR: il2cpp file not supported.", result.Error);
                Assert.Contains("System.NotSupportedException: ERROR: il2cpp file not supported.", result.Error);
                Assert.Contains("System.NotSupportedException: ERROR: il2cpp file not supported.", cliStdout);
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
        public void Library_NullConfig_UsesEffectiveDocumentedDefaults()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "il2cpp-core-nullconfig-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var fixturesDir = Path.Combine(tempDir, "fixtures");
                Directory.CreateDirectory(fixturesDir);
                (var wasmPath, var metaPath) = CreateSyntheticFixtures(fixturesDir);
                var outDir = Path.Combine(tempDir, "out");

                var request = new DumpRequest
                {
                    ExecutablePath = wasmPath,
                    MetadataPath = metaPath,
                    OutputDirectory = outDir,
                    Config = null, // Acceptance: null -> effective documented defaults
                    Log = null
                };

                var result = Dumper.Dump(request);
                Assert.True(result.Success);
                Assert.True(File.Exists(Path.Combine(outDir, "dump.cs")));
                Assert.True(File.Exists(Path.Combine(outDir, "dump-metadata.json")));

                var metaJson = File.ReadAllText(Path.Combine(outDir, "dump-metadata.json"));
                using var doc = JsonDocument.Parse(metaJson);
                var configElem = doc.RootElement.GetProperty("config");

                // #25 alignment: DumpProperty and DumpAttribute default to true
                Assert.True(configElem.GetProperty("DumpProperty").GetBoolean());
                Assert.True(configElem.GetProperty("DumpAttribute").GetBoolean());
                Assert.True(configElem.GetProperty("GenerateDummyDll").GetBoolean());
                Assert.True(configElem.GetProperty("GenerateStruct").GetBoolean());
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
        public void Library_RequireAnyKeyTrue_DoesNotBlockLibraryExecution()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "il2cpp-core-no-hang-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var fixturesDir = Path.Combine(tempDir, "fixtures");
                Directory.CreateDirectory(fixturesDir);
                (var wasmPath, var metaPath) = CreateSyntheticFixtures(fixturesDir);
                var outDir = Path.Combine(tempDir, "out");

                var request = new DumpRequest
                {
                    ExecutablePath = wasmPath,
                    MetadataPath = metaPath,
                    OutputDirectory = outDir,
                    Config = new Config { RequireAnyKey = true }, // Must have zero blocking effect on library
                    Log = null
                };

                var stopwatch = Stopwatch.StartNew();
                var result = Dumper.Dump(request);
                stopwatch.Stop();

                Assert.True(result.Success);
                Assert.True(stopwatch.ElapsedMilliseconds < 10000);
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
        public void Library_NullOutputDirectory_CreatesAndUsesCallerTempDirectory()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "il2cpp-core-tempdir-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var fixturesDir = Path.Combine(tempDir, "fixtures");
                Directory.CreateDirectory(fixturesDir);
                (var wasmPath, var metaPath) = CreateSyntheticFixtures(fixturesDir);

                var request = new DumpRequest
                {
                    ExecutablePath = wasmPath,
                    MetadataPath = metaPath,
                    OutputDirectory = null, // Acceptance: null -> caller-provided temp dir
                    Log = null
                };

                var result = Dumper.Dump(request);
                Assert.True(result.Success);
                Assert.NotNull(result.Outputs);
                Assert.True(result.Outputs.ContainsKey("dump.cs"));
                Assert.True(File.Exists(result.Outputs["dump.cs"]));

                var createdDir = Path.GetDirectoryName(result.Outputs["dump.cs"])!;
                try
                {
                    if (Directory.Exists(createdDir))
                    {
                        Directory.Delete(createdDir, true);
                    }
                }
                catch { }
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
        public void ByteEqualityGoldenTest_InProcessRun_ProducesOutputsByteEqualToCliRun()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "il2cpp-core-golden-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var appDir = SetupIsolatedApp(tempDir);
                var fixturesDir = Path.Combine(tempDir, "fixtures");
                Directory.CreateDirectory(fixturesDir);
                (var wasmPath, var metaPath) = CreateSyntheticFixtures(fixturesDir);

                var cliOutDir = Path.Combine(tempDir, "cli-out");
                var libOutDir = Path.Combine(tempDir, "lib-out");

                // 1. Run CLI
                var (exitCode, stdout, _) = RunCli(appDir, wasmPath, metaPath, cliOutDir);
                Assert.Equal(0, exitCode);

                // 2. Run Library In-Process with matching config
                var config = new Config
                {
                    RequireAnyKey = false,
                    GenerateStruct = true,
                    GenerateDummyDll = true
                };

                var request = new DumpRequest
                {
                    ExecutablePath = wasmPath,
                    MetadataPath = metaPath,
                    OutputDirectory = libOutDir,
                    Config = config,
                    Log = null
                };

                var result = Dumper.Dump(request);
                Assert.True(result.Success);

                // 3. Assert Byte Equality across all deterministic artifacts
                var deterministicArtifacts = new[]
                {
                    "dump.cs",
                    "script.json",
                    "stringliteral.json",
                    "stringliteral-indexed.json",
                    "il2cpp.h"
                };

                foreach (var artifact in deterministicArtifacts)
                {
                    var cliFile = Path.Combine(cliOutDir, artifact);
                    var libFile = Path.Combine(libOutDir, artifact);

                    Assert.True(File.Exists(cliFile), $"CLI artifact missing: {artifact}");
                    Assert.True(File.Exists(libFile), $"Library artifact missing: {artifact}");

                    var cliBytes = File.ReadAllBytes(cliFile);
                    var libBytes = File.ReadAllBytes(libFile);

                    Assert.Equal(cliBytes, libBytes);
                }

                // Also assert Outputs dictionary contains all produced files
                Assert.True(result.Outputs.ContainsKey("dump.cs"));
                Assert.True(result.Outputs.ContainsKey("script.json"));
                Assert.True(result.Outputs.ContainsKey("stringliteral.json"));
                Assert.True(result.Outputs.ContainsKey("stringliteral-indexed.json"));
                Assert.True(result.Outputs.ContainsKey("il2cpp.h"));
                Assert.True(result.Outputs.ContainsKey("dump-metadata.json"));
                Assert.True(result.Outputs.ContainsKey("DummyDll"));
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
        public void Cli_ManualRegistration_PromptsAndSucceedsWithExitCode0()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "il2cpp-cli-manual-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var appDir = SetupIsolatedApp(tempDir);
                var fixturesDir = Path.Combine(tempDir, "fixtures");
                Directory.CreateDirectory(fixturesDir);

                var metaPath = Path.Combine(fixturesDir, "metadata.dat");
                File.WriteAllBytes(metaPath, CreateMinimalMetadata());

                var wasmPath = Path.Combine(fixturesDir, "empty.wasm");
                File.WriteAllBytes(wasmPath, CreateSyntheticWasm());
                // Notice: no script.json created! Auto search will fail and fallback to manual prompt.

                var cliOutDir = Path.Combine(tempDir, "cli-out");

                var (exitCode, stdout, _) = RunCliWithInput(appDir, "20\n100\n", wasmPath, metaPath, cliOutDir);

                Assert.Equal(0, exitCode);
                Assert.Contains("ERROR: Can't use auto mode to process file, try manual mode.", stdout);
                Assert.Contains("Input CodeRegistration: ", stdout);
                Assert.Contains("Input MetadataRegistration: ", stdout);
                Assert.True(File.Exists(Path.Combine(cliOutDir, "dump.cs")));
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
        public void Library_ManualRegistration_ProgrammaticFallback_SucceedsSilently()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "il2cpp-lib-manual-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var fixturesDir = Path.Combine(tempDir, "fixtures");
                Directory.CreateDirectory(fixturesDir);

                var metaPath = Path.Combine(fixturesDir, "metadata.dat");
                File.WriteAllBytes(metaPath, CreateMinimalMetadata());

                var wasmPath = Path.Combine(fixturesDir, "empty.wasm");
                File.WriteAllBytes(wasmPath, CreateSyntheticWasm());

                var libOutDir = Path.Combine(tempDir, "lib-out");

                var request = new DumpRequest
                {
                    ExecutablePath = wasmPath,
                    MetadataPath = metaPath,
                    OutputDirectory = libOutDir,
                    CodeRegistration = 0x20,
                    MetadataRegistration = 0x100,
                    Log = null
                };

                var result = Dumper.Dump(request);

                Assert.True(result.Success);
                Assert.Null(result.Error);
                Assert.True(File.Exists(Path.Combine(libOutDir, "dump.cs")));
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
        public void Cli_MachoFat_Eof_PreservesDumpMetadataSidecarAndExits1()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "il2cpp-cli-macho-eof-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var appDir = SetupIsolatedApp(tempDir);
                var fixturesDir = Path.Combine(tempDir, "fixtures");
                Directory.CreateDirectory(fixturesDir);

                var metaPath = Path.Combine(fixturesDir, "metadata.dat");
                File.WriteAllBytes(metaPath, CreateMinimalMetadata(version: 29));

                var fatPath = Path.Combine(fixturesDir, "binary.fat");
                File.WriteAllBytes(fatPath, CreateSyntheticFatMacho());

                var cliOutDir = Path.Combine(tempDir, "cli-out");

                var (exitCode, stdout, _) = RunCliWithInput(appDir, "", fatPath, metaPath, cliOutDir);

                Assert.Equal(1, exitCode);
                Assert.Contains("Initializing metadata...", stdout);
                Assert.Contains("Metadata Version: 29", stdout);
                Assert.Contains("Initializing il2cpp file...", stdout);
                Assert.Contains("Select Platform: 1.64bit 2.32bit", stdout);
                Assert.Contains("ERROR: Unexpected end of input while selecting platform.", stdout);

                var sidecarPath = Path.Combine(cliOutDir, "dump-metadata.json");
                Assert.True(File.Exists(sidecarPath), "dump-metadata.json sidecar must be preserved on FAT Mach-O EOF abort.");

                var json = File.ReadAllText(sidecarPath);
                using var doc = JsonDocument.Parse(json);
                Assert.Equal(29, doc.RootElement.GetProperty("metadataVersion").GetDouble());
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
        public void Library_WriteInitialFailure_ReturnsSuccessFalseAndError()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "il2cpp-lib-writeinitial-fail-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var fixturesDir = Path.Combine(tempDir, "fixtures");
                Directory.CreateDirectory(fixturesDir);
                (var wasmPath, var metaPath) = CreateSyntheticFixtures(fixturesDir);

                var libOutDir = Path.Combine(tempDir, "lib-out");
                Directory.CreateDirectory(libOutDir);

                // Create a directory with the name dump-metadata.json so FileStream write fails
                Directory.CreateDirectory(Path.Combine(libOutDir, "dump-metadata.json"));

                var request = new DumpRequest
                {
                    ExecutablePath = wasmPath,
                    MetadataPath = metaPath,
                    OutputDirectory = libOutDir,
                    Log = null
                };

                var result = Dumper.Dump(request);

                Assert.False(result.Success);
                Assert.NotNull(result.Error);
                Assert.Contains("ERROR: Failed to write dump metadata:", result.Error);
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
        public void Library_DumpMetadata_ToolIdentity_MatchesDumperAssemblyNotHost()
        {
            var tool = DumpMetadataWriter.GetToolInfo();
            Assert.Equal("Il2CppDumper", tool.Name);

            var dumperAssembly = typeof(DumpMetadata).Assembly;
            var expectedVersion = dumperAssembly.GetName().Version?.ToString() ?? "0.0.0.0";
            Assert.Equal(expectedVersion, tool.Version);
        }

        [Fact]
        public void Program_PromptDumpAddress_ParsesHexAndFallback()
        {
            using (var reader = new StringReader("10000000\n"))
            using (var writer = new StringWriter())
            {
                var addr = Program.PromptDumpAddress(reader, writer);
                Assert.Equal(0x10000000UL, addr);
                Assert.Contains("Input dump address or 0 to not set it: ", writer.ToString());
            }

            using (var reader = new StringReader("0\n"))
            using (var writer = new StringWriter())
            {
                var addr = Program.PromptDumpAddress(reader, writer);
                Assert.Equal(0UL, addr);
            }

            using (var reader = new StringReader("invalid\n"))
            using (var writer = new StringWriter())
            {
                var addr = Program.PromptDumpAddress(reader, writer);
                Assert.Equal(0UL, addr);
            }
        }

        [Fact]
        public void Program_PromptManualRegistration_ParsesHexValues()
        {
            using var reader = new StringReader("20\n100\n");
            using var writer = new StringWriter();

            var result = Program.PromptManualRegistration(reader, writer);
            Assert.NotNull(result);
            Assert.Equal(0x20UL, result.Value.CodeRegistration);
            Assert.Equal(0x100UL, result.Value.MetadataRegistration);

            var outStr = writer.ToString();
            Assert.Contains("Input CodeRegistration: ", outStr);
            Assert.Contains("Input MetadataRegistration: ", outStr);
        }

        [Fact]
        public void Library_ExecuteDump_StructGeneratorFailure_ReturnsSpecificError()
        {
            var env = CreateSyntheticEnvironment(returnType: 0, invalidImageName: true);
            using var m = env.metadata;
            using var il = env.il2Cpp;
            var tempDir = Path.Combine(Path.GetTempPath(), "il2cpp-struct-err-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var config = new Config
                {
                    GenerateStruct = true,
                    GenerateDummyDll = false
                };
                var success = Dumper.ExecuteDump(m, il, config, tempDir + Path.DirectorySeparatorChar, out var error);
                Assert.False(success);
                Assert.NotNull(error);
                Assert.Contains("ERROR: Some errors in generating struct", error);
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
        public void Library_ExecuteDump_DummyDllFailure_ReturnsSpecificError()
        {
            var env = CreateSyntheticEnvironment(returnType: 0);
            using var m = env.metadata;
            using var il = env.il2Cpp;
            m.typeDefs[0].field_count = 1;
            m.fieldDefs = null!;
            var tempDir = Path.Combine(Path.GetTempPath(), "il2cpp-dummydll-err-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var config = new Config
                {
                    GenerateStruct = false,
                    GenerateDummyDll = true
                };
                var success = Dumper.ExecuteDump(m, il, config, tempDir + Path.DirectorySeparatorChar, out var error);
                Assert.False(success);
                Assert.NotNull(error);
                Assert.Contains("ERROR: Some errors in generating dummy dll", error);
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
