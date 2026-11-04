using System;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace Il2CppDumper.Tests
{
    public class StringLiteralExtractionTests
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

        [Fact]
        public void GetStringLiteralFromIndex_Version35_ResolvesFirstMiddleAndLastIndex()
        {
            // "alpha" (5 bytes, pos 0) + "bravo" (5 bytes, pos 5) + "charlie" (7 bytes, pos 10) = 17 bytes
            var dataBytes = Encoding.UTF8.GetBytes("alphabravocharlie");
            using var m = CreateV35Metadata(dataBytes, 0, 5, 10);

            Assert.Equal("alpha", m.GetStringLiteralFromIndex(0));
            Assert.Equal("bravo", m.GetStringLiteralFromIndex(1));
            // Prior bug: index + 1 >= Length caused index 2 (last index) to return string.Empty
            Assert.Equal("charlie", m.GetStringLiteralFromIndex(2));
        }

        [Fact]
        public void GetStringLiteralFromIndex_Version35_SingleStringLiteral_ResolvesLastIndex()
        {
            var dataBytes = Encoding.UTF8.GetBytes("single_literal");
            using var m = CreateV35Metadata(dataBytes, 0);

            // With single literal, index 0 is both first and last index
            Assert.Equal("single_literal", m.GetStringLiteralFromIndex(0));
        }

        [Fact]
        public void GetStringLiteralFromIndex_Version35_IndexWithDataExtendingPastSection_ReturnsEmpty()
        {
            // Section only has 10 bytes: "0123456789"
            var dataBytes = Encoding.UTF8.GetBytes("0123456789");
            // Literal 0 starts at 0, but next literal 1 declares dataIndex 25 (past sectionSize 10)
            using var m = CreateV35Metadata(dataBytes, 0, 25);

            // Literal 0 extent (0..25) extends past section size (10) -> returns string.Empty
            Assert.Equal(string.Empty, m.GetStringLiteralFromIndex(0));

            // Literal 1 dataIndex (25) is >= section size (10) -> returns string.Empty
            Assert.Equal(string.Empty, m.GetStringLiteralFromIndex(1));
        }

        [Fact]
        public void GetStringLiteralFromIndex_Version35_OutOfRangeIndices_ReturnsEmpty()
        {
            var dataBytes = Encoding.UTF8.GetBytes("sample_data");
            using var m = CreateV35Metadata(dataBytes, 0, 6);

            // Valid indices
            Assert.Equal("sample", m.GetStringLiteralFromIndex(0));
            Assert.Equal("_data", m.GetStringLiteralFromIndex(1));

            // Out-of-range indices
            Assert.Equal(string.Empty, m.GetStringLiteralFromIndex(2));
            Assert.Equal(string.Empty, m.GetStringLiteralFromIndex(999));
            Assert.Equal(string.Empty, m.GetStringLiteralFromIndex(uint.MaxValue));
        }

        [Fact]
        public void GetStringLiteralFromIndex_Version38Plus_ResolvesFirstAndLastIndex()
        {
            var dataBytes = Encoding.UTF8.GetBytes("first_v38_last_v38");
            // "first_v38_" (10 bytes), "last_v38" (8 bytes) -> total 18 bytes
            using var m = CreateV38PlusMetadata(38, dataBytes, 0, 10);

            Assert.Equal("first_v38_", m.GetStringLiteralFromIndex(0));
            Assert.Equal("last_v38", m.GetStringLiteralFromIndex(1));
        }

        [Fact]
        public void GetStringLiteralFromIndex_Version39_ResolvesFirstAndLastIndex()
        {
            var dataBytes = Encoding.UTF8.GetBytes("unity_v39_literal_tail");
            // "unity_v39_" (10 bytes), "literal_tail" (12 bytes) -> total 22 bytes
            using var m = CreateV38PlusMetadata(39, dataBytes, 0, 10);

            Assert.Equal("unity_v39_", m.GetStringLiteralFromIndex(0));
            Assert.Equal("literal_tail", m.GetStringLiteralFromIndex(1));
        }

        [Fact]
        public void GetStringLiteralFromIndex_Version38Plus_DataExtendingPastSectionAndOutOfRange_ReturnsEmpty()
        {
            var dataBytes = Encoding.UTF8.GetBytes("short");
            using var m = CreateV38PlusMetadata(38, dataBytes, 0, 20);

            // Extends past section
            Assert.Equal(string.Empty, m.GetStringLiteralFromIndex(0));
            Assert.Equal(string.Empty, m.GetStringLiteralFromIndex(1));

            // Out of range
            Assert.Equal(string.Empty, m.GetStringLiteralFromIndex(2));
            Assert.Equal(string.Empty, m.GetStringLiteralFromIndex(100));
        }

        [Fact]
        public void GetStringLiteralFromIndex_Version31_ResolvesViaLengthFieldAndRejectsPastSection()
        {
            var dataBytes = Encoding.UTF8.GetBytes("legacy_literal_data");
            // Literal 0: length 6, dataIndex 0 -> "legacy"
            // Literal 1: length 7, dataIndex 7 -> "_litera"
            // Literal 2: length 50, dataIndex 14 -> extends past section size 19
            using var m = CreateV31Metadata(dataBytes, (6, 0), (7, 7), (50, 14));

            Assert.Equal("legacy", m.GetStringLiteralFromIndex(0));
            Assert.Equal("literal", m.GetStringLiteralFromIndex(1));
            Assert.Equal(string.Empty, m.GetStringLiteralFromIndex(2));
            Assert.Equal(string.Empty, m.GetStringLiteralFromIndex(3));
        }

        [Fact]
        public void GetStringLiteralFromIndex_Version34_ResolvesNullTerminatedAndRejectsUnterminatedPastSection()
        {
            // Null terminated: "hello\0world\0unterminated"
            var ms = new MemoryStream();
            ms.Write(Encoding.UTF8.GetBytes("hello\0world\0unterminated"));
            var dataBytes = ms.ToArray();

            // Literal 0 at 0 ("hello"), literal 1 at 6 ("world"), literal 2 at 12 ("unterminated" with no trailing \0 within section)
            using var m = CreateV34Metadata(dataBytes, 0, 6, 12);

            Assert.Equal("hello", m.GetStringLiteralFromIndex(0));
            Assert.Equal("world", m.GetStringLiteralFromIndex(1));
            // Literal 2 does not have a null terminator within section bounds, so returns empty
            Assert.Equal(string.Empty, m.GetStringLiteralFromIndex(2));
            Assert.Equal(string.Empty, m.GetStringLiteralFromIndex(5));
        }
    }
}
