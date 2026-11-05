#nullable enable
using System;
using System.Collections.Generic;

namespace Il2CppDumper.Core
{
    public sealed class DumpResult
    {
        public bool Success;
        public string? Error;           // mirrors the #8 failure text
        public IReadOnlyList<string> Warnings;
        public IReadOnlyDictionary<string, string> Outputs; // artifact → path

        public DumpResult()
        {
            Warnings = Array.Empty<string>();
            Outputs = new Dictionary<string, string>();
        }

        public DumpResult(bool success, string? error, IReadOnlyList<string>? warnings = null, IReadOnlyDictionary<string, string>? outputs = null)
        {
            Success = success;
            Error = error;
            Warnings = warnings ?? Array.Empty<string>();
            Outputs = outputs ?? new Dictionary<string, string>();
        }
    }
}
