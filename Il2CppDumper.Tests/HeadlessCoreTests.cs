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
        private static byte[] CreateMinimalMetadata()
        {
            var bytes = new byte[1024];
            BitConverter.GetBytes(0xFAB11BAFu).CopyTo(bytes, 0); // magic
            BitConverter.GetBytes(29).CopyTo(bytes, 4);           // version
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

        private static (int exitCode, string stdout, string stderr) RunCli(string appDir, params string[] args)
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
            process.StandardInput.Close();
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(30000);
            return (process.ExitCode, stdout, stderr);
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
                var fixturesDir = Path.Combine(tempDir, "fixtures");
                Directory.CreateDirectory(fixturesDir);

                var metadataPath = Path.Combine(fixturesDir, "metadata.dat");
                File.WriteAllBytes(metadataPath, CreateMinimalMetadata());

                var badBinPath = Path.Combine(fixturesDir, "bad.bin");
                File.WriteAllBytes(badBinPath, Encoding.ASCII.GetBytes("BAD!"));

                var outDir = Path.Combine(tempDir, "out");

                var request = new DumpRequest
                {
                    ExecutablePath = badBinPath,
                    MetadataPath = metadataPath,
                    OutputDirectory = outDir,
                    Log = null
                };

                var result = Dumper.Dump(request);

                Assert.False(result.Success);
                Assert.NotNull(result.Error);
                // Acceptance: Init failure (bad.bin fixture) -> Success=false with non-empty Error; test asserts the string is the same one the CLI would print.
                Assert.Contains("System.NotSupportedException: ERROR: il2cpp file not supported.", result.Error);
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
    }
}
