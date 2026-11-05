#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Il2CppDumper.Diff
{
    public class DumpDiffResult
    {
        public DumpDiffEndpoint left { get; set; } = new();
        public DumpDiffEndpoint right { get; set; } = new();
        public DumpDiffTypes types { get; set; } = new();
        public DumpDiffMethods methods { get; set; } = new();
        public DumpDiffLiterals literals { get; set; } = new();
        public DumpDiffSummary summary { get; set; } = new();

        [JsonIgnore]
        public bool HasDifferences =>
            types.added.Count > 0 ||
            types.removed.Count > 0 ||
            types.changed.Count > 0 ||
            (methods.addressChanged != null && methods.addressChanged.Count > 0) ||
            literals.addedByIndex.Count > 0 ||
            literals.valueChangedByIndex.Count > 0;
    }

    public class DumpDiffEndpoint
    {
        public string source { get; set; } = string.Empty;
        public string sha256 { get; set; } = string.Empty;
    }

    public class DumpDiffTypes
    {
        public List<string> added { get; set; } = new();
        public List<string> removed { get; set; } = new();
        public List<DumpDiffTypeChange> changed { get; set; } = new();
    }

    public class DumpDiffTypeChange
    {
        public string type { get; set; } = string.Empty;
        public DumpDiffFieldChanges fields { get; set; } = new();
        public DumpDiffMethodChanges methods { get; set; } = new();
    }

    public class DumpDiffFieldChanges
    {
        public List<string> added { get; set; } = new();
        public List<string> removed { get; set; } = new();
        public List<DumpDiffFieldChange> changed { get; set; } = new();
    }

    public class DumpDiffFieldChange
    {
        public string field { get; set; } = string.Empty;
        public DumpDiffFieldDetails left { get; set; } = new();
        public DumpDiffFieldDetails right { get; set; } = new();
    }

    public class DumpDiffFieldDetails
    {
        public string type { get; set; } = string.Empty;
        public string? offset { get; set; }
    }

    public class DumpDiffMethodChanges
    {
        public List<string> added { get; set; } = new();
        public List<string> removed { get; set; } = new();
        public List<DumpDiffMethodChange> changed { get; set; } = new();
    }

    public class DumpDiffMethodChange
    {
        public string method { get; set; } = string.Empty;
        public DumpDiffMethodDetails left { get; set; } = new();
        public DumpDiffMethodDetails right { get; set; } = new();
    }

    public class DumpDiffMethodDetails
    {
        public string returnType { get; set; } = string.Empty;
        public string parameters { get; set; } = string.Empty;
    }

    public class DumpDiffMethods
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<DumpDiffAddressChange>? addressChanged { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? note { get; set; }
    }

    public class DumpDiffAddressChange
    {
        public string type { get; set; } = string.Empty;
        public string method { get; set; } = string.Empty;
        public string left { get; set; } = string.Empty;
        public string right { get; set; } = string.Empty;
    }

    public class DumpDiffLiterals
    {
        public List<DumpDiffLiteralAdded> addedByIndex { get; set; } = new();
        public List<DumpDiffLiteralChanged> valueChangedByIndex { get; set; } = new();

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? note { get; set; }
    }

    public class DumpDiffLiteralAdded
    {
        public uint index { get; set; }
        public string value { get; set; } = string.Empty;
    }

    public class DumpDiffLiteralChanged
    {
        public uint index { get; set; }
        public string left { get; set; } = string.Empty;
        public string right { get; set; } = string.Empty;
    }

    public class DumpDiffSummary
    {
        public int addedTypes { get; set; }
        public int removedTypes { get; set; }
        public int addressChanges { get; set; }
        public int valueChanges { get; set; }
    }
}
