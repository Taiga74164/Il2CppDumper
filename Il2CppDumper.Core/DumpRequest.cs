#nullable enable
using System;
using System.IO;

namespace Il2CppDumper.Core
{
    public sealed class DumpRequest
    {
        public string ExecutablePath { get; set; } = string.Empty;   // required
        public string MetadataPath { get; set; } = string.Empty;     // required
        public string? OutputDirectory { get; set; }                 // null → caller-provided temp dir
        public Config? Config { get; set; }                          // null → defaults as documented
        public TextWriter? Log { get; set; }                         // null → silent
        public int? MachoFatIndex { get; set; }                      // null → auto (prefer 64-bit)
        public Func<Fat[], int?>? MachoFatSelector { get; set; }     // optional interactive or custom selector
        public ulong? DumpAddress { get; set; }                      // optional ELF dump address
        public Func<ulong?>? DumpAddressPrompt { get; set; }         // optional ELF dump address prompt
        public ulong? CodeRegistration { get; set; }                 // optional manual registration fallback
        public ulong? MetadataRegistration { get; set; }             // optional manual registration fallback
        public Func<(ulong CodeRegistration, ulong MetadataRegistration)?>? ManualRegistrationPrompt { get; set; }

        public DumpRequest()
        {
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
