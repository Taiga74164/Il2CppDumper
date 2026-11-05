using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
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
            return m;
        }

        private class LegacyStringLiteralConsumerModel
        {
            public string value { get; set; } = string.Empty;
            public string address { get; set; } = string.Empty;
        }

        private class ForensicStringLiteralConsumerModel
        {
            public uint index { get; set; }
            public string value { get; set; } = string.Empty;
            public int dataIndex { get; set; }
            public int length { get; set; }
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

            // Default address is "0x0" when unmapped
            Assert.All(records, r => Assert.Equal("0x0", r.address));
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
        public void DumpStringLiterals_AddressMapping_PopulatesRvaWhenPresent()
        {
            var dataBytes = Encoding.UTF8.GetBytes("alphabravocharlie");
            using var m = CreateV35Metadata(dataBytes, 0, 5, 10);

            var addressMap = new Dictionary<uint, ulong>
            {
                [0] = 0x2000,
                [2] = 0x3500
            };

            var records = m.DumpStringLiterals(addressMap, addr => addr - 0x1000);

            Assert.Equal("0x1000", records[0].address);
            Assert.Equal("0x0", records[1].address); // Unmapped literal has "0x0"
            Assert.Equal("0x2500", records[2].address);
        }

        [Fact]
        public void DumpStringLiterals_JsonSerialization_AdditiveBackwardCompatibility()
        {
            var dataBytes = Encoding.UTF8.GetBytes("alphabravocharlie");
            using var m = CreateV35Metadata(dataBytes, 0, 5, 10);

            var addressMap = new Dictionary<uint, ulong>
            {
                [0] = 0x00401000,
                [1] = 0x00402000,
                [2] = 0x00403000
            };

            var records = m.DumpStringLiterals(addressMap, addr => addr);

            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            var json = JsonSerializer.Serialize(records, jsonOptions);

            // Assert additive fields exist in JSON string
            Assert.Contains("\"index\": 0", json);
            Assert.Contains("\"value\": \"alpha\"", json);
            Assert.Contains("\"address\": \"0x401000\"", json);
            Assert.Contains("\"dataIndex\": 0", json);
            Assert.Contains("\"length\": 5", json);

            // Existing consumers expecting legacy shape { value, address } continue working seamlessly
            var legacyConsumers = JsonSerializer.Deserialize<List<LegacyStringLiteralConsumerModel>>(json);
            Assert.NotNull(legacyConsumers);
            Assert.Equal(3, legacyConsumers.Count);
            Assert.Equal("alpha", legacyConsumers[0].value);
            Assert.Equal("0x401000", legacyConsumers[0].address);
            Assert.Equal("bravo", legacyConsumers[1].value);
            Assert.Equal("0x402000", legacyConsumers[1].address);
            Assert.Equal("charlie", legacyConsumers[2].value);
            Assert.Equal("0x403000", legacyConsumers[2].address);

            // Forensic consumers expecting new shape { index, value, dataIndex, length }
            var forensicConsumers = JsonSerializer.Deserialize<List<ForensicStringLiteralConsumerModel>>(json);
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
    }
}
