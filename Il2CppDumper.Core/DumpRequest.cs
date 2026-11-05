#nullable enable
using System;
using System.IO;

namespace Il2CppDumper.Core
{
    public sealed class DumpRequest
    {
        public string ExecutablePath;   // required
        public string MetadataPath;     // required
        public string? OutputDirectory; // null → caller-provided temp dir
        public Config? Config;          // null → defaults as documented
        public TextWriter? Log;         // null → silent
        public int? MachoFatIndex;      // null → auto (prefer 64-bit)

        public DumpRequest()
        {
            ExecutablePath = string.Empty;
            MetadataPath = string.Empty;
        }

        public DumpRequest(string executablePath, string metadataPath, string? outputDirectory = null, Config? config = null, TextWriter? log = null)
        {
            ExecutablePath = executablePath;
            MetadataPath = metadataPath;
            OutputDirectory = outputDirectory;
            Config = config;
            Log = log;
        }
    }
}
