using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Il2CppDumper
{
    public class DumpMetadata
    {
        [JsonPropertyName("tool")]
        public DumpMetadataTool Tool { get; set; }

        [JsonPropertyName("inputs")]
        public DumpMetadataInputs Inputs { get; set; }

        [JsonPropertyName("metadataVersion")]
        public double MetadataVersion { get; set; }

        [JsonPropertyName("config")]
        public Config Config { get; set; }

        [JsonPropertyName("generatedAtUtc")]
        public string GeneratedAtUtc { get; set; }
    }

    public class DumpMetadataTool
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("version")]
        public string Version { get; set; }

        [JsonPropertyName("commit")]
        public string Commit { get; set; }
    }

    public class DumpMetadataInputs
    {
        [JsonPropertyName("il2cpp")]
        public DumpMetadataInputFile Il2Cpp { get; set; }

        [JsonPropertyName("metadata")]
        public DumpMetadataInputFile Metadata { get; set; }
    }

    public class DumpMetadataInputFile
    {
        [JsonPropertyName("path")]
        public string Path { get; set; }

        [JsonPropertyName("length")]
        public long Length { get; set; }

        [JsonPropertyName("sha256")]
        public string Sha256 { get; set; }
    }

    public static class DumpMetadataWriter
    {
        public const string FileName = "dump-metadata.json";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        public static string ComputeStreamSha256(Stream stream)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            int bytesRead;
            while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                hasher.AppendData(buffer, 0, bytesRead);
            }
            var hashBytes = hasher.GetHashAndReset();
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        public static DumpMetadataInputFile CreateInputFileInfo(string filePath)
        {
            var resolvedPath = Path.GetFullPath(filePath);
            using var stream = new FileStream(resolvedPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920, useAsync: false);
            var length = stream.Length;
            var sha256 = ComputeStreamSha256(stream);
            return new DumpMetadataInputFile
            {
                Path = resolvedPath,
                Length = length,
                Sha256 = sha256
            };
        }

        public static (DumpMetadataInputFile inputInfo, double initialMetadataVersion) CreateMetadataInputFileInfo(string metadataPath)
        {
            var resolvedPath = Path.GetFullPath(metadataPath);
            using var stream = new FileStream(resolvedPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920, useAsync: false);
            var length = stream.Length;
            double initialVersion = 0;
            if (length >= 8)
            {
                Span<byte> header = stackalloc byte[8];
                var read = stream.Read(header);
                if (read == 8)
                {
                    var sanity = BitConverter.ToUInt32(header.Slice(0, 4));
                    var version = BitConverter.ToInt32(header.Slice(4, 4));
                    if (sanity == 0xFAB11BAFu && version >= 16 && version <= 200)
                    {
                        initialVersion = version;
                    }
                }
                stream.Position = 0;
            }
            var sha256 = ComputeStreamSha256(stream);
            return (new DumpMetadataInputFile
            {
                Path = resolvedPath,
                Length = length,
                Sha256 = sha256
            }, initialVersion);
        }

        public static DumpMetadataTool GetToolInfo()
        {
            var assembly = typeof(DumpMetadata).Assembly;
            var version = assembly.GetName().Version?.ToString() ?? "0.0.0.0";
            var commit = string.Empty;
            var infoVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrEmpty(infoVersion))
            {
                var plusIndex = infoVersion.IndexOf('+');
                if (plusIndex >= 0 && plusIndex + 1 < infoVersion.Length)
                {
                    commit = infoVersion.Substring(plusIndex + 1);
                }
            }
            return new DumpMetadataTool
            {
                Name = "Il2CppDumper",
                Version = version,
                Commit = commit
            };
        }

        public static DumpMetadata Create(string il2cppPath, string metadataPath, Config config, double? resolvedMetadataVersion = null)
        {
            var il2cppInfo = CreateInputFileInfo(il2cppPath);
            var (metadataInfo, initialMetadataVersion) = CreateMetadataInputFileInfo(metadataPath);
            return new DumpMetadata
            {
                Tool = GetToolInfo(),
                Inputs = new DumpMetadataInputs
                {
                    Il2Cpp = il2cppInfo,
                    Metadata = metadataInfo
                },
                MetadataVersion = resolvedMetadataVersion ?? initialMetadataVersion,
                Config = config ?? new Config(),
                GeneratedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
            };
        }

        public static DumpMetadata WriteInitial(string outputDir, string il2cppPath, string metadataPath, Config config)
        {
            var dumpMetadata = Create(il2cppPath, metadataPath, config);
            Write(outputDir, dumpMetadata);
            return dumpMetadata;
        }

        public static void UpdateMetadataVersion(string outputDir, DumpMetadata dumpMetadata, double resolvedMetadataVersion)
        {
            if (dumpMetadata == null)
            {
                return;
            }
            if (dumpMetadata.MetadataVersion != resolvedMetadataVersion)
            {
                dumpMetadata.MetadataVersion = resolvedMetadataVersion;
                Write(outputDir, dumpMetadata);
            }
        }

        public static void Write(string outputDir, DumpMetadata dumpMetadata)
        {
            Directory.CreateDirectory(outputDir);
            var outputPath = Path.Combine(outputDir, FileName);
            var json = JsonSerializer.Serialize(dumpMetadata, JsonOptions);
            File.WriteAllText(outputPath, json, new UTF8Encoding(false));
        }
    }
}
