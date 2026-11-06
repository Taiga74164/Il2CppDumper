#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Il2CppDumper.Diff
{
    public static class FieldOffsetSeverity
    {
        public const string BreaksExternalReaders = "breaks-external-readers";
        public const string LeafConsumersMaySurvive = "leaf-consumers-may-survive";
    }

    public static class FieldOffsetDriftType
    {
        public const string Shifted = "shifted";
        public const string Removed = "removed";
        public const string Added = "added";
    }

    public class FieldOffsetDriftReport
    {
        [JsonPropertyName("schemaVersion")]
        public int schemaVersion { get; set; } = 1;

        [JsonPropertyName("structs")]
        public List<FieldOffsetStructDrift> structs { get; set; } = new();

        [JsonIgnore]
        public bool HasDrift => structs.Count > 0;
    }

    public class FieldOffsetStructDrift
    {
        [JsonPropertyName("struct")]
        public string @struct { get; set; } = string.Empty;

        [JsonPropertyName("size")]
        public FieldOffsetSize size { get; set; } = new();

        [JsonPropertyName("fields")]
        public List<FieldOffsetFieldDrift> fields { get; set; } = new();

        [JsonPropertyName("verdict")]
        public FieldOffsetVerdict verdict { get; set; } = new();
    }

    public class FieldOffsetSize
    {
        [JsonPropertyName("left")]
        public string? left { get; set; }

        [JsonPropertyName("right")]
        public string? right { get; set; }
    }

    public class FieldOffsetFieldDrift
    {
        [JsonPropertyName("name")]
        public string name { get; set; } = string.Empty;

        [JsonPropertyName("left")]
        public string? left { get; set; }

        [JsonPropertyName("right")]
        public string? right { get; set; }

        [JsonPropertyName("drift")]
        public string drift { get; set; } = string.Empty;

        [JsonPropertyName("severity")]
        public string severity { get; set; } = string.Empty;
    }

    public class FieldOffsetVerdict
    {
        [JsonPropertyName("shifted")]
        public int shifted { get; set; }

        [JsonPropertyName("removed")]
        public int removed { get; set; }

        [JsonPropertyName("added")]
        public int added { get; set; }
    }
}
