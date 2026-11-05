using System.Runtime.InteropServices;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Xunit;

namespace Il2CppDumper.Tests
{
    public class CliOutputDirectoryTests
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
        public void Issue9_ExactReproduction_NonExistentOutputDirectory_IsCreatedAndUsed()
        {
            // Exact reproduction from Issue #9:
            // User specifies a new output directory that does not exist yet.
            // Expected: The directory is created and used, and no dump files are written to the fallback app directory.
            var tempDir = Path.Combine(Path.GetTempPath(), "il2cpp-issue-9-repro-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var appDir = SetupIsolatedApp(tempDir);
                var fixturesDir = Path.Combine(tempDir, "fixtures");
                Directory.CreateDirectory(fixturesDir);

                (var wasmPath, var metaPath) = CreateSyntheticFixtures(fixturesDir);

                var destination = Path.Combine(fixturesDir, "new-output-directory");
                Assert.False(Directory.Exists(destination));

                var (exitCode, stdout, _) = RunCli(appDir, wasmPath, metaPath, destination);

                Assert.Equal(0, exitCode);
                Assert.True(Directory.Exists(destination));
                Assert.True(File.Exists(Path.Combine(destination, "dump.cs")));
                Assert.True(File.Exists(Path.Combine(destination, "script.json")));
                Assert.False(File.Exists(Path.Combine(appDir, "dump.cs")));
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
        public void Cli_PreExistingOutputDirectory_ControlContinuesToWork()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "il2cpp-issue-9-existing-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var appDir = SetupIsolatedApp(tempDir);
                var fixturesDir = Path.Combine(tempDir, "fixtures");
                Directory.CreateDirectory(fixturesDir);

                (var wasmPath, var metaPath) = CreateSyntheticFixtures(fixturesDir);

                var destination = Path.Combine(fixturesDir, "existing-output-directory");
                Directory.CreateDirectory(destination);
                Assert.True(Directory.Exists(destination));

                var (exitCode, stdout, _) = RunCli(appDir, wasmPath, metaPath, destination);

                Assert.Equal(0, exitCode);
                Assert.True(File.Exists(Path.Combine(destination, "dump.cs")));
                Assert.False(File.Exists(Path.Combine(appDir, "dump.cs")));
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
        public void Cli_TwoArguments_UsesApplicationBaseDirectoryAsFallback()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "il2cpp-issue-9-twoargs-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var appDir = SetupIsolatedApp(tempDir);
                var fixturesDir = Path.Combine(tempDir, "fixtures");
                Directory.CreateDirectory(fixturesDir);

                (var wasmPath, var metaPath) = CreateSyntheticFixtures(fixturesDir);

                var (exitCode, stdout, _) = RunCli(appDir, wasmPath, metaPath);

                Assert.Equal(0, exitCode);
                Assert.True(File.Exists(Path.Combine(appDir, "dump.cs")));
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
        public void Cli_NonExistentOutputDirectory_ArgumentsInDifferentOrder_IsCreatedAndUsed()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "il2cpp-issue-9-order-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var appDir = SetupIsolatedApp(tempDir);
                var fixturesDir = Path.Combine(tempDir, "fixtures");
                Directory.CreateDirectory(fixturesDir);

                (var wasmPath, var metaPath) = CreateSyntheticFixtures(fixturesDir);

                var destination = Path.Combine(fixturesDir, "reordered-output-directory");
                Assert.False(Directory.Exists(destination));

                // Passing output directory as first argument: destination, wasm, metadata
                var (exitCode, stdout, _) = RunCli(appDir, destination, wasmPath, metaPath);

                Assert.Equal(0, exitCode);
                Assert.True(Directory.Exists(destination));
                Assert.True(File.Exists(Path.Combine(destination, "dump.cs")));
                Assert.False(File.Exists(Path.Combine(appDir, "dump.cs")));
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
        public void Cli_NonExistentOutputDirectory_WithTrailingSeparator_IsCreatedAndNormalized()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "il2cpp-issue-9-trailing-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var appDir = SetupIsolatedApp(tempDir);
                var fixturesDir = Path.Combine(tempDir, "fixtures");
                Directory.CreateDirectory(fixturesDir);

                (var wasmPath, var metaPath) = CreateSyntheticFixtures(fixturesDir);

                var destination = Path.Combine(fixturesDir, "trailing-dir");
                var destinationWithSlash = destination + Path.DirectorySeparatorChar;

                var (exitCode, stdout, _) = RunCli(appDir, wasmPath, metaPath, destinationWithSlash);

                Assert.Equal(0, exitCode);
                Assert.True(Directory.Exists(destination));
                Assert.True(File.Exists(Path.Combine(destination, "dump.cs")));
                Assert.False(File.Exists(Path.Combine(appDir, "dump.cs")));
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
        public void Cli_InvalidOutputDirectoryPath_FailsWithClearErrorAndExitCodeOne()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "il2cpp-issue-9-invalidpath-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var appDir = SetupIsolatedApp(tempDir);
                var fixturesDir = Path.Combine(tempDir, "fixtures");
                Directory.CreateDirectory(fixturesDir);

                (var wasmPath, var metaPath) = CreateSyntheticFixtures(fixturesDir);

                // Portable invalid directory path across Windows and Unix:
                // Attempting to create a child directory under an existing regular file throws IOException on all platforms (ENOTDIR on POSIX).
                var blockingFile = Path.Combine(fixturesDir, "blocking_file.txt");
                File.WriteAllText(blockingFile, "blocking content");
                var invalidDestination = Path.Combine(blockingFile, "child_dir");

                var (exitCode, stdout, _) = RunCli(appDir, wasmPath, metaPath, invalidDestination);

                Assert.Equal(1, exitCode);
                Assert.Contains("ERROR: Failed to create output directory", stdout);
                Assert.False(File.Exists(Path.Combine(appDir, "dump.cs")));
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
        public void Cli_InvalidOutputDirectoryCharacters_OnWindows_FailsWithClearErrorAndExitCodeOne()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return;
            }

            var tempDir = Path.Combine(Path.GetTempPath(), "il2cpp-issue-9-wininvalid-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var appDir = SetupIsolatedApp(tempDir);
                var fixturesDir = Path.Combine(tempDir, "fixtures");
                Directory.CreateDirectory(fixturesDir);

                (var wasmPath, var metaPath) = CreateSyntheticFixtures(fixturesDir);

                var invalidDestination = Path.Combine(fixturesDir, "invalid|path?dir");

                var (exitCode, stdout, _) = RunCli(appDir, wasmPath, metaPath, invalidDestination);

                Assert.Equal(1, exitCode);
                Assert.Contains("ERROR: Failed to create output directory", stdout);
                Assert.False(File.Exists(Path.Combine(appDir, "dump.cs")));
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
        public void Cli_OutputDirectoryIsExistingFile_FailsWithClearErrorAndExitCodeOne()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "il2cpp-issue-9-existingfile-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var appDir = SetupIsolatedApp(tempDir);
                var fixturesDir = Path.Combine(tempDir, "fixtures");
                Directory.CreateDirectory(fixturesDir);

                (var wasmPath, var metaPath) = CreateSyntheticFixtures(fixturesDir);

                var existingFileAsDir = Path.Combine(fixturesDir, "already_a_file.txt");
                File.WriteAllText(existingFileAsDir, "I am a file, not a directory");

                var (exitCode, stdout, _) = RunCli(appDir, wasmPath, metaPath, existingFileAsDir);

                Assert.Equal(1, exitCode);
                Assert.Contains("ERROR: Failed to create output directory", stdout);
                Assert.False(File.Exists(Path.Combine(appDir, "dump.cs")));
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
        public void Cli_MissingMetadataFile_DoesNotCreateOutputDirectory()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "il2cpp-issue-9-missingmeta-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var appDir = SetupIsolatedApp(tempDir);
                var fixturesDir = Path.Combine(tempDir, "fixtures");
                Directory.CreateDirectory(fixturesDir);

                var binPath1 = Path.Combine(fixturesDir, "file1.bin");
                var missingMetaPath = Path.Combine(fixturesDir, "missing-metadata.dat");
                File.WriteAllBytes(binPath1, new byte[64]);

                var destination = Path.Combine(fixturesDir, "should-not-exist");

                var (exitCode, stdout, _) = RunCli(appDir, binPath1, missingMetaPath, destination);

                Assert.Equal(1, exitCode);
                Assert.Contains("ERROR: Metadata file not found or encrypted.", stdout);
                Assert.False(Directory.Exists(destination));
                Assert.False(File.Exists(Path.Combine(appDir, "dump-metadata.json")));
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
        public void Cli_EmptyStringOutputDirectory_ReturnsExitCode1_AndEmitsControlledErrorMessage()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "il2cpp-issue-26-emptyout-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var appDir = SetupIsolatedApp(tempDir);
                var fixturesDir = Path.Combine(tempDir, "fixtures");
                Directory.CreateDirectory(fixturesDir);

                (var wasmPath, var metaPath) = CreateSyntheticFixtures(fixturesDir);

                var (exitCode, stdout, stderr) = RunCli(appDir, wasmPath, metaPath, "");

                Assert.Equal(1, exitCode);
                Assert.Contains("ERROR: Failed to create output directory:", stdout);
                Assert.DoesNotContain("Unhandled exception", stdout, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Unhandled exception", stderr, StringComparison.OrdinalIgnoreCase);
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
        public void Cli_WhitespaceOutputDirectory_PreservesPlatformSpecificContract_AndNeverCrashes()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "il2cpp-issue-26-spaceout-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var appDir = SetupIsolatedApp(tempDir);
                var fixturesDir = Path.Combine(tempDir, "fixtures");
                Directory.CreateDirectory(fixturesDir);

                (var wasmPath, var metaPath) = CreateSyntheticFixtures(fixturesDir);

                var (exitCode, stdout, stderr) = RunCli(appDir, wasmPath, metaPath, "   ");

                Assert.DoesNotContain("Unhandled exception", stdout, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Unhandled exception", stderr, StringComparison.OrdinalIgnoreCase);

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Assert.Equal(1, exitCode);
                    Assert.Contains("ERROR: Failed to create output directory:", stdout);
                }
                else
                {
                    Assert.Equal(0, exitCode);
                    var outputDir = Path.Combine(appDir, "   ");
                    Assert.True(Directory.Exists(outputDir), $"Expected output directory at {outputDir}");
                    Assert.True(File.Exists(Path.Combine(outputDir, "dump.cs")), $"Expected dump.cs in {outputDir}");
                }
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
