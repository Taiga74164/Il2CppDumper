namespace Il2CppDumper.Tests.Synthetic
{
    /// <summary>
    /// Corruption variants covering parser fault injection and boundary cases specified in Issue #44.
    /// </summary>
    public enum MetadataCorruptionType
    {
        None = 0,
        TruncatedSection = 1,
        WrongMagic = 2,
        OutOfRangeVersion = 3,
        LiteralExtentPastSectionEnd = 4,
        ZeroedRegistrationRecords = 5
    }
}
