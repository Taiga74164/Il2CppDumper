using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Il2CppDumper.Tests
{
    public class StringLiteralForensicTests
    {
        private static Metadata CreateV35Metadata(byte[] literalDataBytes, params int[] dataIndices)
        {
            var streamBytes = new byte[8192];
            BitConverter.GetBytes(0xFAB11BAFu).CopyTo(streamBytes, 0); // magic
            BitConverter.GetBytes(35).CopyTo(streamBytes, 4);           // version 35
            var m = new Metadata(new MemoryStream(streamBytes))
            {
                Version = 35
            };
            m.header.stringLiteralDataOffset = 1024;
            m.header.stringLiteralDataSize = literalDataBytes.Length;
            Buffer.BlockCopy(literalDataBytes, 0, streamBytes, (int)m.header.stringLiteralDataOffset, literalDataBytes.Length);

            m.stringLiterals = dataIndices.Select(idx => new Il2CppStringLiteral { dataIndex = idx }).ToArray();
            m.typeDefs = Array.Empty<Il2CppTypeDefinition>();
            m.methodDefs = Array.Empty<Il2CppMethodDefinition>();
            m.imageDefs = Array.Empty<Il2CppImageDefinition>();
            return m;
        }

        private static Metadata CreateV38PlusMetadata(int version, byte[] literalDataBytes, params int[] dataIndices)
        {
            var streamBytes = new byte[8192];
            BitConverter.GetBytes(0xFAB11BAFu).CopyTo(streamBytes, 0); // magic
            BitConverter.GetBytes(version).CopyTo(streamBytes, 4);       // version 38, 39, etc.
            var m = new Metadata(new MemoryStream(streamBytes))
            {
                Version = version
            };
            m.header.stringLiteralData = new Il2CppSectionMetadata
            {
                offset = 1024,
                sectionSize = literalDataBytes.Length,
                count = literalDataBytes.Length
            };
            Buffer.BlockCopy(literalDataBytes, 0, streamBytes, m.header.stringLiteralData.offset, literalDataBytes.Length);

            m.stringLiterals = dataIndices.Select(idx => new Il2CppStringLiteral { dataIndex = idx }).ToArray();
            m.typeDefs = Array.Empty<Il2CppTypeDefinition>();
            m.methodDefs = Array.Empty<Il2CppMethodDefinition>();
            m.imageDefs = Array.Empty<Il2CppImageDefinition>();
            return m;
        }

        private static Metadata CreateV31Metadata(byte[] literalDataBytes, params (uint length, int dataIndex)[] literals)
        {
            var streamBytes = new byte[8192];
            BitConverter.GetBytes(0xFAB11BAFu).CopyTo(streamBytes, 0); // magic
            BitConverter.GetBytes(31).CopyTo(streamBytes, 4);           // version 31
            var m = new Metadata(new MemoryStream(streamBytes))
            {
                Version = 31
            };
            m.header.stringLiteralDataOffset = 1024;
            m.header.stringLiteralDataSize = literalDataBytes.Length;
            Buffer.BlockCopy(literalDataBytes, 0, streamBytes, (int)m.header.stringLiteralDataOffset, literalDataBytes.Length);

            m.stringLiterals = literals.Select(l => new Il2CppStringLiteral { length = l.length, dataIndex = l.dataIndex }).ToArray();
            m.typeDefs = Array.Empty<Il2CppTypeDefinition>();
            m.methodDefs = Array.Empty<Il2CppMethodDefinition>();
            m.imageDefs = Array.Empty<Il2CppImageDefinition>();
            return m;
        }

        private static Metadata CreateV34Metadata(byte[] literalDataBytes, params int[] dataIndices)
        {
            var streamBytes = new byte[8192];
            BitConverter.GetBytes(0xFAB11BAFu).CopyTo(streamBytes, 0); // magic
            BitConverter.GetBytes(34).CopyTo(streamBytes, 4);           // version 34
            var m = new Metadata(new MemoryStream(streamBytes))
            {
                Version = 34
            };
            m.header.stringLiteralDataOffset = 1024;
            m.header.stringLiteralDataSize = literalDataBytes.Length;
            Buffer.BlockCopy(literalDataBytes, 0, streamBytes, (int)m.header.stringLiteralDataOffset, literalDataBytes.Length);

            m.stringLiterals = dataIndices.Select(idx => new Il2CppStringLiteral { dataIndex = idx }).ToArray();
            m.typeDefs = Array.Empty<Il2CppTypeDefinition>();
            m.methodDefs = Array.Empty<Il2CppMethodDefinition>();
            m.imageDefs = Array.Empty<Il2CppImageDefinition>();
            return m;
        }

        private sealed class TestIl2CppV35 : Il2Cpp
        {
            private readonly SectionHelper _sectionHelper;

            public TestIl2CppV35(Stream stream, SectionHelper sectionHelper) : base(stream)
            {
                Version = 35;
                                Is32Bit = true;
                _sectionHelper = sectionHelper;
                types = Array.Empty<Il2CppType>();
                methodSpecs = Array.Empty<Il2CppMethodSpec>();
                methodPointers = Array.Empty<ulong>();
                codeGenModuleMethodPointers = new Dictionary<string, ulong[]>();
                genericMethodPointers = Array.Empty<ulong>();
                invokerPointers = Array.Empty<ulong>();
                reversePInvokeWrappers = Array.Empty<ulong>();
                unresolvedVirtualCallPointers = Array.Empty<ulong>();
            }

            public override ulong MapVATR(ulong addr) => addr;
            public override ulong MapRTVA(ulong addr)
            {
                if (addr == 1024) return 0x400;
                if (addr == 1028) return 0x404;
                return addr;
            }
            public override ulong GetRVA(ulong addr) => addr;
            public override bool Search() => false;
            public override bool PlusSearch(int methodCount, int typeDefinitionsCount, int imageCount) => false;
            public override bool SymbolSearch() => false;
            public override SectionHelper GetSectionHelper(int methodCount, int typeDefinitionsCount, int imageCount) => _sectionHelper;
            public override bool CheckDump() => false;
        }

        private class LegacyStringLiteralEntry
        {
            public string value { get; set; } = string.Empty;
            public string address { get; set; } = string.Empty;
        }

        [Fact]
        public void DumpStringLiterals_Version35_SequentialIndicesAndMatchingExtents()
        {
            // Synthetic v35+ fixture with 3 literals:
            // "alpha" (5 bytes, pos 0) + "bravo" (5 bytes, pos 5) + "charlie" (7 bytes, pos 10) = 17 bytes
            var dataBytes = Encoding.UTF8.GetBytes("alphabravocharlie");
            using var m = CreateV35Metadata(dataBytes, 0, 5, 10);

            var records = m.DumpStringLiterals();

            Assert.Equal(3, records.Count);

            // Assert indices are strictly sequential (0, 1, 2)
            for (int i = 0; i < records.Count; i++)
            {
                Assert.Equal((uint)i, records[i].index);
            }

            // Assert values
            Assert.Equal("alpha", records[0].value);
            Assert.Equal("bravo", records[1].value);
            Assert.Equal("charlie", records[2].value); // Previously dropped last entry in v35+

            // Assert position extents
            Assert.Equal(0, records[0].dataIndex);
            Assert.Equal(5, records[0].length);

            Assert.Equal(5, records[1].dataIndex);
            Assert.Equal(5, records[1].length);

            Assert.Equal(10, records[2].dataIndex);
            Assert.Equal(7, records[2].length);

            // Assert contiguous extents across all items matching total section size
            for (int i = 0; i < records.Count; i++)
            {
                var extentEnd = records[i].dataIndex + records[i].length;
                var nextStart = i + 1 < records.Count ? records[i + 1].dataIndex : dataBytes.Length;
                Assert.Equal(nextStart, extentEnd);
            }
        }

        [Fact]
        public void DumpStringLiterals_Version38Plus_SectionMetadata_MatchesExtentsAndIncludesLast()
        {
            var dataBytes = Encoding.UTF8.GetBytes("first_v38_mid_v38_last_v38");
            using var m = CreateV38PlusMetadata(38, dataBytes, 0, 10, 18);

            var records = m.DumpStringLiterals();

            Assert.Equal(3, records.Count);
            Assert.Equal(0u, records[0].index);
            Assert.Equal("first_v38_", records[0].value);
            Assert.Equal(0, records[0].dataIndex);
            Assert.Equal(10, records[0].length);

            Assert.Equal(1u, records[1].index);
            Assert.Equal("mid_v38_", records[1].value);
            Assert.Equal(10, records[1].dataIndex);
            Assert.Equal(8, records[1].length);

            Assert.Equal(2u, records[2].index);
            Assert.Equal("last_v38", records[2].value);
            Assert.Equal(18, records[2].dataIndex);
            Assert.Equal(8, records[2].length);

            // Contiguous extent check
            Assert.Equal(10, records[0].dataIndex + records[0].length);
            Assert.Equal(18, records[1].dataIndex + records[1].length);
            Assert.Equal(dataBytes.Length, records[2].dataIndex + records[2].length);
        }

        [Fact]
        public void DumpStringLiterals_Version39_SectionMetadata_MatchesExtentsAndIncludesLast()
        {
            var dataBytes = Encoding.UTF8.GetBytes("unity_v39_forensic_literal_tail");
            using var m = CreateV38PlusMetadata(39, dataBytes, 0, 10, 19);

            var records = m.DumpStringLiterals();

            Assert.Equal(3, records.Count);
            Assert.Equal(0u, records[0].index);
            Assert.Equal("unity_v39_", records[0].value);
            Assert.Equal(0, records[0].dataIndex);
            Assert.Equal(10, records[0].length);

            Assert.Equal(1u, records[1].index);
            Assert.Equal("forensic_", records[1].value);
            Assert.Equal(10, records[1].dataIndex);
            Assert.Equal(9, records[1].length);

            Assert.Equal(2u, records[2].index);
            Assert.Equal("literal_tail", records[2].value);
            Assert.Equal(19, records[2].dataIndex);
            Assert.Equal(12, records[2].length);

            Assert.Equal(dataBytes.Length, records[2].dataIndex + records[2].length);
        }

        [Fact]
        public void DumpStringLiterals_Version31_ResolvesViaLengthFieldAndMatchesExtents()
        {
            var dataBytes = Encoding.UTF8.GetBytes("legacy_literal_section");
            using var m = CreateV31Metadata(dataBytes, (6u, 0), (7u, 7), (7u, 15));

            var records = m.DumpStringLiterals();

            Assert.Equal(3, records.Count);
            Assert.Equal(0u, records[0].index);
            Assert.Equal("legacy", records[0].value);
            Assert.Equal(0, records[0].dataIndex);
            Assert.Equal(6, records[0].length);

            Assert.Equal(1u, records[1].index);
            Assert.Equal("literal", records[1].value);
            Assert.Equal(7, records[1].dataIndex);
            Assert.Equal(7, records[1].length);

            Assert.Equal(2u, records[2].index);
            Assert.Equal("section", records[2].value);
            Assert.Equal(15, records[2].dataIndex);
            Assert.Equal(7, records[2].length);
        }

        [Fact]
        public void DumpStringLiterals_Version34_NullTerminated_MatchesExtents()
        {
            var dataBytes = Encoding.UTF8.GetBytes("first\0second\0third\0");
            using var m = CreateV34Metadata(dataBytes, 0, 6, 13);

            var records = m.DumpStringLiterals();

            Assert.Equal(3, records.Count);
            Assert.Equal(0u, records[0].index);
            Assert.Equal("first", records[0].value);
            Assert.Equal(0, records[0].dataIndex);
            Assert.Equal(5, records[0].length);

            Assert.Equal(1u, records[1].index);
            Assert.Equal("second", records[1].value);
            Assert.Equal(6, records[1].dataIndex);
            Assert.Equal(6, records[1].length);

            Assert.Equal(2u, records[2].index);
            Assert.Equal("third", records[2].value);
            Assert.Equal(13, records[2].dataIndex);
            Assert.Equal(5, records[2].length);
        }

        [Fact]
        public void DumpStringLiterals_JsonSerialization_MatchesExpectedShape()
        {
            var dataBytes = Encoding.UTF8.GetBytes("alphabravocharlie");
            using var m = CreateV35Metadata(dataBytes, 0, 5, 10);

            var records = m.DumpStringLiterals();

            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true
            };
            var json = JsonSerializer.Serialize(records, jsonOptions);

            // Assert exact forensic fields exist in JSON string without address
            Assert.Contains("\"index\": 0", json);
            Assert.Contains("\"value\": \"alpha\"", json);
            Assert.Contains("\"dataIndex\": 0", json);
            Assert.Contains("\"length\": 5", json);
            Assert.DoesNotContain("\"address\"", json);

            // Forensic consumers expecting shape { index, value, dataIndex, length }
            var forensicConsumers = JsonSerializer.Deserialize<List<StringLiteralRecord>>(json);
            Assert.NotNull(forensicConsumers);
            Assert.Equal(3, forensicConsumers.Count);
            Assert.Equal(0u, forensicConsumers[0].index);
            Assert.Equal("alpha", forensicConsumers[0].value);
            Assert.Equal(0, forensicConsumers[0].dataIndex);
            Assert.Equal(5, forensicConsumers[0].length);

            Assert.Equal(2u, forensicConsumers[2].index);
            Assert.Equal("charlie", forensicConsumers[2].value);
            Assert.Equal(10, forensicConsumers[2].dataIndex);
            Assert.Equal(7, forensicConsumers[2].length);
        }

        [Fact]
        public void DumpStringLiterals_CorruptedOrOutOfBounds_GracefulFallback()
        {
            var dataBytes = Encoding.UTF8.GetBytes("valid");
            // Index 0: dataIndex 0, but next index has dataIndex 999 (extends past sectionSize 5) -> safely empty
            // Index 1: dataIndex 999 >= sectionSize -> safely empty
            // Index 2: dataIndex -1 < 0 -> safely empty
            using var m = CreateV35Metadata(dataBytes, 0, 999, -1);

            var records = m.DumpStringLiterals();

            Assert.Equal(3, records.Count);
            Assert.Equal(0u, records[0].index);
            Assert.Equal(string.Empty, records[0].value);
            Assert.Equal(0, records[0].dataIndex);
            Assert.Equal(0, records[0].length);

            Assert.Equal(1u, records[1].index);
            Assert.Equal(string.Empty, records[1].value);
            Assert.Equal(999, records[1].dataIndex);
            Assert.Equal(0, records[1].length);

            Assert.Equal(2u, records[2].index);
            Assert.Equal(string.Empty, records[2].value);
            Assert.Equal(-1, records[2].dataIndex);
            Assert.Equal(0, records[2].length);
        }

        [Fact]
        public void DumpStringLiterals_ValidItemFollowedByCorruptedItem_HandlesGracefully()
        {
            // "valid" (5 bytes, pos 0) + "next" (4 bytes, pos 5) = 9 bytes total
            var dataBytes = Encoding.UTF8.GetBytes("validnext");
            // Index 0: 0, Index 1: 5 (valid length 5-0=5 and 9-5=4)
            // Index 2: 999 (corrupted out of bounds)
            using var m = CreateV35Metadata(dataBytes, 0, 5, 999);

            var records = m.DumpStringLiterals();

            Assert.Equal(3, records.Count);
            Assert.Equal(0u, records[0].index);
            Assert.Equal("valid", records[0].value);
            Assert.Equal(0, records[0].dataIndex);
            Assert.Equal(5, records[0].length);

            // Index 1: next index is 999 -> length 994 -> exceeds sectionSize 9 -> safely empty
            Assert.Equal(1u, records[1].index);
            Assert.Equal(string.Empty, records[1].value);
            Assert.Equal(5, records[1].dataIndex);
            Assert.Equal(0, records[1].length);

            // Index 2: dataIndex 999 >= sectionSize 9 -> safely empty
            Assert.Equal(2u, records[2].index);
            Assert.Equal(string.Empty, records[2].value);
            Assert.Equal(999, records[2].dataIndex);
            Assert.Equal(0, records[2].length);
        }

        [Fact]
        public void WriteScript_DuplicateReferencesAndNonAscii_PreservesLegacyStringLiteralAndScriptJsonEscaping_AndEmitsIndexedForensicTable()
        {
            // Exact reproduction fixture from B1 & B2 review:
            // Metadata v35 with 2 string literals:
            //   Index 0: "á<alpha>" (9 bytes UTF-8)
            //   Index 1: "bravo" (5 bytes UTF-8)
            // Total data bytes = 14
            var literalBytes = Encoding.UTF8.GetBytes("á<alpha>bravo");
            using var m = CreateV35Metadata(literalBytes, 0, 9);

            // Synthetic Il2Cpp stream containing 2 valid metadata usage tokens for index 0
            // at offsets 1024 and 1028:
            var ilBytes = new byte[4096];
            // encodedToken: usage kIl2CppMetadataUsageStringLiteral (5), decodedIndex 0
            // metadataValue == ((5 << 29) | (0 << 1)) + 1 == 0xA0000001u
            var token = ((uint)Il2CppMetadataUsage.kIl2CppMetadataUsageStringLiteral << 29) | (0u << 1) + 1;
            BitConverter.GetBytes(token).CopyTo(ilBytes, 1024);
            BitConverter.GetBytes(token).CopyTo(ilBytes, 1028);

            var sectionHelper = new SectionHelper(new WebAssemblyMemory(new MemoryStream(new byte[16]), 0), 0, 0, 0, 0);
            sectionHelper.SetSection(SearchSectionType.Data, new SearchSection { offset = 1024, offsetEnd = 2048 });

            using var il = new TestIl2CppV35(new MemoryStream(ilBytes), sectionHelper);
            var executor = new Il2CppExecutor(m, il);
            var generator = new StructGenerator(executor);

            var tempDir = Path.Combine(Path.GetTempPath(), "il2cpp-struct-b1-b2-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                generator.WriteScript(tempDir + Path.DirectorySeparatorChar);

                // --- B1 Verification on stringliteral.json (Legacy Output) ---
                var legacyPath = Path.Combine(tempDir, "stringliteral.json");
                Assert.True(File.Exists(legacyPath));
                var legacyJson = File.ReadAllText(legacyPath);
                var legacyEntries = JsonSerializer.Deserialize<List<LegacyStringLiteralEntry>>(legacyJson);
                Assert.NotNull(legacyEntries);

                // Both references to index 0 ("á<alpha>") at 0x400 and 0x404 MUST be preserved
                Assert.Equal(2, legacyEntries.Count);
                Assert.Equal("á<alpha>", legacyEntries[0].value);
                Assert.Equal("0x400", legacyEntries[0].address);
                Assert.Equal("á<alpha>", legacyEntries[1].value);
                Assert.Equal("0x404", legacyEntries[1].address);

                // Unreferenced literal "bravo" MUST NOT be in legacy stringliteral.json (exact parity with base)
                Assert.DoesNotContain(legacyEntries, e => e.value == "bravo");

                // --- B1 Verification on stringliteral-indexed.json (Forensic Output) ---
                var indexedPath = Path.Combine(tempDir, "stringliteral-indexed.json");
                Assert.True(File.Exists(indexedPath));
                var indexedJson = File.ReadAllText(indexedPath);
                var indexedEntries = JsonSerializer.Deserialize<List<StringLiteralRecord>>(indexedJson);
                Assert.NotNull(indexedEntries);

                // Must contain EVERY literal from metadata (both index 0 and index 1)
                Assert.Equal(2, indexedEntries.Count);
                Assert.Equal(0u, indexedEntries[0].index);
                Assert.Equal("á<alpha>", indexedEntries[0].value);
                Assert.Equal(0, indexedEntries[0].dataIndex);
                Assert.Equal(9, indexedEntries[0].length);

                Assert.Equal(1u, indexedEntries[1].index);
                Assert.Equal("bravo", indexedEntries[1].value);
                Assert.Equal(9, indexedEntries[1].dataIndex);
                Assert.Equal(5, indexedEntries[1].length);

                // Extents must match
                Assert.Equal(0 + 9, indexedEntries[0].dataIndex + indexedEntries[0].length);
                Assert.Equal(9 + 5, indexedEntries[1].dataIndex + indexedEntries[1].length);

                // --- B2 Verification on script.json (Standard JSON Escaping) ---
                var scriptPath = Path.Combine(tempDir, "script.json");
                Assert.True(File.Exists(scriptPath));
                var scriptJson = File.ReadAllText(scriptPath);

                // script.json MUST retain standard .NET JSON escaping for non-ASCII and '< >'
                // exactly like the base code without UnsafeRelaxedJsonEscaping
                Assert.Contains("\\u00E1\\u003Calpha\\u003E", scriptJson);
                Assert.DoesNotContain("á<alpha>", scriptJson);
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
