using System;
using System.Diagnostics;
using System.IO;
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

        private static (int exitCode, string stdout, string stderr) RunCli(string appDir, params string[] args)
        {
            var dllPath = Path.Combine(appDir, "Il2CppDumper.dll");
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"\"{dllPath}\" {string.Join(" ", Array.ConvertAll(args, a => $"\"{a}\""))}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = appDir
            };

            using var process = Process.Start(psi)!;
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
    }
}
