using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Il2CppDumper.Tests
{
    public class DumpMetadataTests
    {
        private const string KnownMinimalMetadataSha256 = "267164a53bf5d03b2b6c2b4c88f4bd380b4344281ede2cb219dd2fac09e0ca93";
        private const string KnownSyntheticWasmSha256 = "318afafcacba227c0f7fa94991668216fc3d3ed68e898660569f061fbd2e474c";

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

        private static byte[] CreateMinimalMetadata(int version = 29)
        {
            var bytes = new byte[1024];
            BitConverter.GetBytes(0xFAB11BAFu).CopyTo(bytes, 0); // magic
            BitConverter.GetBytes(version).CopyTo(bytes, 4);      // version
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
        public void Cli_SuccessfulRun_WritesDumpMetadataSidecar_WithExpectedKeysAndKnownFixtureHashes()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "il2cpp-issue-29-ok-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var appDir = SetupIsolatedApp(tempDir);
                var fixturesDir = Path.Combine(tempDir, "fixtures");
                Directory.CreateDirectory(fixturesDir);

                var (wasmPath, metaPath) = CreateSyntheticFixtures(fixturesDir);
                var outputDir = Path.Combine(tempDir, "out");

                var (exitCode, _, _) = RunCli(appDir, wasmPath, metaPath, outputDir);
                Assert.Equal(0, exitCode);

                var sidecarPath = Path.Combine(outputDir, "dump-metadata.json");
                Assert.True(File.Exists(sidecarPath));

                var rawJson = File.ReadAllText(sidecarPath);
                using var doc = JsonDocument.Parse(rawJson);
                var root = doc.RootElement;

                Assert.True(root.TryGetProperty("tool", out var toolEl));
                Assert.True(toolEl.TryGetProperty("name", out var toolNameEl));
                Assert.True(toolEl.TryGetProperty("version", out var toolVersionEl));
                Assert.True(toolEl.TryGetProperty("commit", out var toolCommitEl));
                Assert.Equal("Il2CppDumper", toolNameEl.GetString());
                Assert.Equal("1.0.0.0", toolVersionEl.GetString());
                Assert.NotNull(toolCommitEl.GetString());

                Assert.True(root.TryGetProperty("inputs", out var inputsEl));
                Assert.True(inputsEl.TryGetProperty("il2cpp", out var il2cppEl));
                Assert.True(inputsEl.TryGetProperty("metadata", out var metadataEl));

                Assert.Equal(Path.GetFullPath(wasmPath), il2cppEl.GetProperty("path").GetString());
                Assert.Equal(4119L, il2cppEl.GetProperty("length").GetInt64());
                Assert.Equal(KnownSyntheticWasmSha256, il2cppEl.GetProperty("sha256").GetString());

                Assert.Equal(Path.GetFullPath(metaPath), metadataEl.GetProperty("path").GetString());
                Assert.Equal(1024L, metadataEl.GetProperty("length").GetInt64());
                Assert.Equal(KnownMinimalMetadataSha256, metadataEl.GetProperty("sha256").GetString());

                Assert.True(root.TryGetProperty("metadataVersion", out var metaVerEl));
                Assert.Equal(29, metaVerEl.GetInt32());

                Assert.True(root.TryGetProperty("config", out var configEl));
                Assert.True(configEl.TryGetProperty("DumpMethod", out _));
                Assert.True(configEl.TryGetProperty("DumpField", out _));
                Assert.True(configEl.TryGetProperty("DumpProperty", out _));
                Assert.True(configEl.TryGetProperty("DumpAttribute", out _));
                Assert.True(configEl.TryGetProperty("GenerateDummyDll", out _));
                Assert.True(configEl.TryGetProperty("GenerateStruct", out _));

                Assert.True(root.TryGetProperty("generatedAtUtc", out var generatedAtEl));
                Assert.True(DateTimeOffset.TryParse(generatedAtEl.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _));
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
        public void Cli_FailedRun_UnsupportedBinary_StillWritesDumpMetadataSidecar()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "il2cpp-issue-29-badbin-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var appDir = SetupIsolatedApp(tempDir);
                var fixturesDir = Path.Combine(tempDir, "fixtures");
                Directory.CreateDirectory(fixturesDir);

                var metaPath = Path.Combine(fixturesDir, "metadata.dat");
                File.WriteAllBytes(metaPath, CreateMinimalMetadata());

                var badBinPath = Path.Combine(fixturesDir, "bad.bin");
                var badBinBytes = Encoding.ASCII.GetBytes("BAD!");
                File.WriteAllBytes(badBinPath, badBinBytes);
                var expectedBadBinSha256 = Convert.ToHexString(SHA256.HashData(badBinBytes)).ToLowerInvariant();

                var outputDir = Path.Combine(tempDir, "out");
                var (exitCode, _, _) = RunCli(appDir, badBinPath, metaPath, outputDir);
                Assert.Equal(1, exitCode);

                var sidecarPath = Path.Combine(outputDir, "dump-metadata.json");
                Assert.True(File.Exists(sidecarPath));

                var parsed = JsonSerializer.Deserialize<DumpMetadata>(File.ReadAllText(sidecarPath))!;
                Assert.NotNull(parsed);
                Assert.Equal(29, parsed.MetadataVersion);
                Assert.Equal(expectedBadBinSha256, parsed.Inputs.Il2Cpp.Sha256);
                Assert.Equal(4L, parsed.Inputs.Il2Cpp.Length);
                Assert.Equal(KnownMinimalMetadataSha256, parsed.Inputs.Metadata.Sha256);
                Assert.Equal(1024L, parsed.Inputs.Metadata.Length);
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
        public void Cli_FailedRun_CorruptTruncatedMetadata_StillWritesDumpMetadataSidecarAsEarlyAsStreamsOpen()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "il2cpp-issue-29-corruptmeta-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var appDir = SetupIsolatedApp(tempDir);
                var fixturesDir = Path.Combine(tempDir, "fixtures");
                Directory.CreateDirectory(fixturesDir);

                var corruptMetaBytes = BitConverter.GetBytes(0xFAB11BAFu); // 4 bytes only; Metadata constructor throws EndOfStreamException
                var metaPath = Path.Combine(fixturesDir, "corrupt-metadata.dat");
                File.WriteAllBytes(metaPath, corruptMetaBytes);
                var expectedCorruptMetaSha256 = Convert.ToHexString(SHA256.HashData(corruptMetaBytes)).ToLowerInvariant();

                var binBytes = new byte[] { 0x7f, (byte)'E', (byte)'L', (byte)'F' };
                var binPath = Path.Combine(fixturesDir, "libil2cpp.so");
                File.WriteAllBytes(binPath, binBytes);
                var expectedBinSha256 = Convert.ToHexString(SHA256.HashData(binBytes)).ToLowerInvariant();

                var outputDir = Path.Combine(tempDir, "out");
                var (exitCode, _, _) = RunCli(appDir, binPath, metaPath, outputDir);
                Assert.Equal(1, exitCode);

                var sidecarPath = Path.Combine(outputDir, "dump-metadata.json");
                Assert.True(File.Exists(sidecarPath));

                var parsed = JsonSerializer.Deserialize<DumpMetadata>(File.ReadAllText(sidecarPath))!;
                Assert.NotNull(parsed);
                Assert.Equal(0, parsed.MetadataVersion);
                Assert.Equal(expectedBinSha256, parsed.Inputs.Il2Cpp.Sha256);
                Assert.Equal(4L, parsed.Inputs.Il2Cpp.Length);
                Assert.Equal(expectedCorruptMetaSha256, parsed.Inputs.Metadata.Sha256);
                Assert.Equal(4L, parsed.Inputs.Metadata.Length);
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
        public void Cli_FailedRun_AutoSearchFails_StillWritesDumpMetadataSidecarWithResolvedMetadataVersion()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "il2cpp-issue-29-searchfail-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var appDir = SetupIsolatedApp(tempDir);
                var fixturesDir = Path.Combine(tempDir, "fixtures");
                Directory.CreateDirectory(fixturesDir);

                var metaPath = Path.Combine(fixturesDir, "metadata.dat");
                File.WriteAllBytes(metaPath, CreateMinimalMetadata(version: 35));

                var wasmPath = Path.Combine(fixturesDir, "program.wasm");
                var wasmBytes = new byte[] { 0x00, 0x61, 0x73, 0x6d, 0x01, 0x00, 0x00, 0x00 };
                File.WriteAllBytes(wasmPath, wasmBytes);

                var outputDir = Path.Combine(tempDir, "out");
                var (exitCode, _, _) = RunCli(appDir, wasmPath, metaPath, outputDir);
                Assert.Equal(1, exitCode);

                var sidecarPath = Path.Combine(outputDir, "dump-metadata.json");
                Assert.True(File.Exists(sidecarPath));

                var parsed = JsonSerializer.Deserialize<DumpMetadata>(File.ReadAllText(sidecarPath))!;
                Assert.NotNull(parsed);
                Assert.Equal(35, parsed.MetadataVersion);
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
        public void Cli_EffectiveConfig_IsCapturedInDumpMetadataSidecar()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "il2cpp-issue-29-cfg-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var appDir = SetupIsolatedApp(tempDir, cfg =>
                {
                    cfg.DumpProperty = false;
                    cfg.GenerateStruct = false;
                    cfg.GenerateDummyDll = false;
                    cfg.ForceIl2CppVersion = true;
                    cfg.ForceVersion = 27.1;
                });
                var fixturesDir = Path.Combine(tempDir, "fixtures");
                Directory.CreateDirectory(fixturesDir);

                var (wasmPath, metaPath) = CreateSyntheticFixtures(fixturesDir);
                var outputDir = Path.Combine(tempDir, "out");

                var (exitCode, _, _) = RunCli(appDir, wasmPath, metaPath, outputDir);
                Assert.Equal(0, exitCode);

                var sidecarPath = Path.Combine(outputDir, "dump-metadata.json");
                var parsed = JsonSerializer.Deserialize<DumpMetadata>(File.ReadAllText(sidecarPath))!;

                Assert.False(parsed.Config.DumpProperty);
                Assert.False(parsed.Config.GenerateStruct);
                Assert.False(parsed.Config.GenerateDummyDll);
                Assert.True(parsed.Config.ForceIl2CppVersion);
                Assert.Equal(27.1, parsed.Config.ForceVersion);
                Assert.False(parsed.Config.RequireAnyKey);
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
        public void DumpMetadataWriter_UpdateMetadataVersion_PersistsRefinedSubVersion()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "il2cpp-issue-29-subver-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var metaPath = Path.Combine(tempDir, "metadata.dat");
                File.WriteAllBytes(metaPath, CreateMinimalMetadata(version: 24));
                var binPath = Path.Combine(tempDir, "empty.wasm");
                File.WriteAllBytes(binPath, CreateSyntheticWasm());

                var outDir = Path.Combine(tempDir, "out");
                var initial = DumpMetadataWriter.WriteInitial(outDir, binPath, metaPath, new Config());
                Assert.Equal(24, initial.MetadataVersion);

                DumpMetadataWriter.UpdateMetadataVersion(outDir, initial, 24.2);

                var reloaded = JsonSerializer.Deserialize<DumpMetadata>(File.ReadAllText(Path.Combine(outDir, "dump-metadata.json")))!;
                Assert.Equal(24.2, reloaded.MetadataVersion);
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
        public void ComputeStreamSha256_StreamsIncrementallyWithoutSeekingOrLoadingAllBytes()
        {
            var payload = CreateSyntheticWasm();
            using var nonSeekable = new NonSeekableChunkedStream(payload, maxChunkBytes: 257);

            var hash = DumpMetadataWriter.ComputeStreamSha256(nonSeekable);

            Assert.Equal(KnownSyntheticWasmSha256, hash);
            Assert.True(nonSeekable.ReadCallCount > 10);
        }

        private sealed class NonSeekableChunkedStream : Stream
        {
            private readonly byte[] data;
            private readonly int maxChunkBytes;
            private int offset;

            public int ReadCallCount { get; private set; }

            public NonSeekableChunkedStream(byte[] data, int maxChunkBytes)
            {
                this.data = data;
                this.maxChunkBytes = maxChunkBytes;
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override void Flush() { }

            public override int Read(byte[] buffer, int offset, int count)
            {
                ReadCallCount++;
                var remaining = data.Length - this.offset;
                if (remaining <= 0)
                {
                    return 0;
                }
                var toCopy = Math.Min(Math.Min(count, maxChunkBytes), remaining);
                Array.Copy(data, this.offset, buffer, offset, toCopy);
                this.offset += toCopy;
                return toCopy;
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
