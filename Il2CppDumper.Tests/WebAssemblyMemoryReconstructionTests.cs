using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Il2CppDumper.Tests
{
    public class WebAssemblyMemoryReconstructionTests
    {
        [Fact]
        public void CreateMemory_Issue4ExactReproductionFixture_ReconstructsMemoryWithByteAtOffset1024()
        {
            // Valid WASM v1: memory min 1 page, active data at 1024, payload 'A' (23 bytes).
            byte[] bytes = {
                0, 0x61, 0x73, 0x6D, 1, 0, 0, 0,
                5, 3, 1, 0, 1,
                11, 8, 1, 0, 0x41, 0x80, 8, 0x0B, 1, 0x41
            };

            using var inputStream = new MemoryStream(bytes);
            var wasm = new WebAssembly(inputStream);
            var mem = wasm.CreateMemory();

            Assert.NotNull(mem);
            Assert.Equal(1025ul, mem.Length);
            Assert.Equal(0ul, mem.Position);

            // Verify zero-filled gap from 0 to 1023
            var gap = mem.ReadBytes(1024);
            Assert.True(gap.All(b => b == 0), "Gaps in linear memory before initialized segments must be zero-filled.");

            // Verify payload 'A' at offset 1024
            var payloadByte = mem.ReadByte();
            Assert.Equal(0x41, payloadByte);
        }

        [Fact]
        public void CreateMemory_SparseSegments_ReconstructsAllRangesWithZeroFilledGaps()
        {
            var segment1 = (offset: 100u, data: new byte[] { 0x11, 0x22, 0x33, 0x44 });
            var segment2 = (offset: 2000u, data: new byte[] { 0xAA, 0xBB });
            var wasmBytes = BuildWasmModule(segment1, segment2);

            Assert.True(wasmBytes.Length < 2000, "WASM binary file size must be substantially smaller than memory extent.");

            using var inputStream = new MemoryStream(wasmBytes);
            var wasm = new WebAssembly(inputStream);
            var mem = wasm.CreateMemory();

            Assert.Equal(2002ul, mem.Length);
            Assert.Equal(0ul, mem.Position);

            // Leading gap
            var leadingGap = mem.ReadBytes(100);
            Assert.True(leadingGap.All(b => b == 0));

            // First segment
            var actualSeg1 = mem.ReadBytes(4);
            Assert.Equal(segment1.data, actualSeg1);

            // Inter-segment gap
            var intermediateGap = mem.ReadBytes(2000 - 104);
            Assert.True(intermediateGap.All(b => b == 0));

            // Second segment
            var actualSeg2 = mem.ReadBytes(2);
            Assert.Equal(segment2.data, actualSeg2);
        }

        [Fact]
        public void CreateMemory_UnorderedSegments_BssStartAndMemoryExtentTakeMaxEndAddress()
        {
            // Segment 1 has a higher address (1500..1509), Segment 2 has a lower address (500..519)
            var segmentHigh = (offset: 1500u, data: new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });
            var segmentLow = (offset: 500u, data: new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });
            var wasmBytes = BuildWasmModule(segmentHigh, segmentLow);

            using var inputStream = new MemoryStream(wasmBytes);
            var wasm = new WebAssembly(inputStream);
            var mem = wasm.CreateMemory();

            Assert.Equal(1510ul, mem.Length);

            mem.Position = 500;
            Assert.Equal(segmentLow.data, mem.ReadBytes(4));

            mem.Position = 1500;
            Assert.Equal(segmentHigh.data, mem.ReadBytes(10));
        }

        [Fact]
        public void CreateMemory_OverlappingSegments_LaterSegmentOverwritesEarlier()
        {
            var segment1 = (offset: 100u, data: new byte[] { 1, 1, 1, 1, 1 });
            var segment2 = (offset: 102u, data: new byte[] { 2, 2, 2 });
            var wasmBytes = BuildWasmModule(segment1, segment2);

            using var inputStream = new MemoryStream(wasmBytes);
            var wasm = new WebAssembly(inputStream);
            var mem = wasm.CreateMemory();

            Assert.Equal(105ul, mem.Length);

            mem.Position = 100;
            var bytes = mem.ReadBytes(5);
            Assert.Equal(new byte[] { 1, 1, 2, 2, 2 }, bytes);
        }

        [Fact]
        public void CreateMemory_NoDataSections_ReturnsEmptyMemory()
        {
            var wasmBytes = BuildWasmModuleWithoutDataSection();

            using var inputStream = new MemoryStream(wasmBytes);
            var wasm = new WebAssembly(inputStream);
            var mem = wasm.CreateMemory();

            Assert.NotNull(mem);
            Assert.Equal(0ul, mem.Length);
        }

        [Fact]
        public void CreateMemory_DataSectionWithZeroCount_ReturnsEmptyMemory()
        {
            var wasmBytes = BuildWasmModule();

            using var inputStream = new MemoryStream(wasmBytes);
            var wasm = new WebAssembly(inputStream);
            var mem = wasm.CreateMemory();

            Assert.NotNull(mem);
            Assert.Equal(0ul, mem.Length);
        }

        [Fact]
        public void CreateMemory_SegmentWithEmptyData_ReconstructsMemoryUpToOffset()
        {
            var emptyDataSegment = (offset: 512u, data: Array.Empty<byte>());
            var wasmBytes = BuildWasmModule(emptyDataSegment);

            using var inputStream = new MemoryStream(wasmBytes);
            var wasm = new WebAssembly(inputStream);
            var mem = wasm.CreateMemory();

            Assert.Equal(512ul, mem.Length);
            var bytes = mem.ReadBytes(512);
            Assert.True(bytes.All(b => b == 0));
        }

        [Fact]
        public void CreateMemory_CheckedSizeAndRange_SegmentOffsetAndLengthOverflowThrowsOverflowException()
        {
            // Segment offset near uint.MaxValue with data length causing 32-bit overflow
            var wasmBytes = BuildWasmModuleRawSegments(writer =>
            {
                WriteULeb128(writer, 1); // 1 segment
                WriteULeb128(writer, 0); // index
                writer.Write((byte)0x41); // i32.const
                WriteULeb128(writer, uint.MaxValue - 2); // offset: 0xFFFFFFFD
                writer.Write((byte)0x0B); // end
                WriteULeb128(writer, 10); // length: 10 (0xFFFFFFFD + 10 overflows uint)
                writer.Write(new byte[10]);
            });

            using var inputStream = new MemoryStream(wasmBytes);
            var wasm = new WebAssembly(inputStream);

            Assert.Throws<OverflowException>(() => wasm.CreateMemory());
        }

        [Fact]
        public void CreateMemory_CheckedSizeAndRange_ExtentExceedingIntMaxValueThrowsOverflowException()
        {
            // Segment offset at int.MaxValue + 1 (0x80000000u) with data length 1
            var wasmBytes = BuildWasmModuleRawSegments(writer =>
            {
                WriteULeb128(writer, 1); // 1 segment
                WriteULeb128(writer, 0); // index
                writer.Write((byte)0x41); // i32.const
                WriteULeb128(writer, 0x80000000u); // offset: 2147483648
                writer.Write((byte)0x0B); // end
                WriteULeb128(writer, 1); // length: 1
                writer.Write(new byte[] { 0x42 });
            });

            using var inputStream = new MemoryStream(wasmBytes);
            var wasm = new WebAssembly(inputStream);

            Assert.Throws<OverflowException>(() => wasm.CreateMemory());
        }

        [Fact]
        public void CreateMemory_SectionHelperDataAndBssBoundariesMatchReconstructedExtent()
        {
            var segment = (offset: 1024u, data: new byte[] { 0xAA, 0xBB });
            var wasmBytes = BuildWasmModule(segment);

            using var inputStream = new MemoryStream(wasmBytes);
            var wasm = new WebAssembly(inputStream);
            var mem = wasm.CreateMemory();

            var sectionHelper = mem.GetSectionHelper(0, 0, 0);
            Assert.NotNull(sectionHelper);
            Assert.NotEmpty(sectionHelper.Data);
            Assert.NotEmpty(sectionHelper.Bss);

            Assert.Equal(1024ul, sectionHelper.Data[0].offset);
            Assert.Equal(mem.Length, sectionHelper.Data[0].offsetEnd);
            Assert.Equal(mem.Length, sectionHelper.Bss[0].offset);
        }

        [Fact]
        public void SectionHelper_ExtentBelow1024_ProducesEmptyDataAndSearchCompletesWithoutException()
        {
            // Reproduction fixture from Bot Review: file length 1125, but reconstructed extent 17 bytes (segment at offset 16)
            using var input = new MemoryStream();
            using (var writer = new BinaryWriter(input, System.Text.Encoding.UTF8, true))
            {
                writer.Write(new byte[] { 0, 0x61, 0x73, 0x6D, 1, 0, 0, 0 });
                writer.Write(new byte[] { 5, 3, 1, 0, 1 }); // memory: 1 page
                writer.Write(new byte[] { 0, 0xCC, 8 }); // custom section: 1100-byte payload
                writer.Write(new byte[1100]); // arbitrary custom data
                writer.Write(new byte[] { 11, 7, 1, 0, 0x41, 16, 0x0B, 1, 0x41 }); // data at offset 16, length 1
            }

            using var wasm = new WebAssembly(new MemoryStream(input.ToArray()));
            using var memory = wasm.CreateMemory();
            memory.Version = 27;

            Assert.Equal(17ul, memory.Length);

            var helper = memory.GetSectionHelper(1, 1, 1);
            Assert.NotNull(helper);
            Assert.Empty(helper.Data);

            var codeReg = helper.FindCodeRegistration();
            Assert.Equal(0ul, codeReg);

            var metaReg = helper.FindMetadataRegistration();
            Assert.Equal(0ul, metaReg);

            var searchResult = memory.PlusSearch(1, 1, 1);
            Assert.False(searchResult);
        }

        [Fact]
        public void SectionHelper_EmptyMemory_ProducesEmptyDataAndSearchCompletesWithoutException()
        {
            var wasmBytes = BuildWasmModuleWithoutDataSection();
            using var wasm = new WebAssembly(new MemoryStream(wasmBytes));
            using var memory = wasm.CreateMemory();
            memory.Version = 27;

            Assert.Equal(0ul, memory.Length);

            var helper = memory.GetSectionHelper(1, 1, 1);
            Assert.NotNull(helper);
            Assert.Empty(helper.Data);

            Assert.Equal(0ul, helper.FindCodeRegistration());
            Assert.Equal(0ul, helper.FindMetadataRegistration());
            Assert.False(memory.PlusSearch(1, 1, 1));
        }

        [Fact]
        public void SectionHelper_BoundaryExactly1024_ProducesEmptyDataAndSearchCompletesWithoutException()
        {
            // Segment ending exactly at offset 1024 (offset 1020, length 4)
            var segment = (offset: 1020u, data: new byte[] { 1, 2, 3, 4 });
            var wasmBytes = BuildWasmModule(segment);

            using var wasm = new WebAssembly(new MemoryStream(wasmBytes));
            using var memory = wasm.CreateMemory();
            memory.Version = 27;

            Assert.Equal(1024ul, memory.Length);

            var helper = memory.GetSectionHelper(1, 1, 1);
            Assert.NotNull(helper);
            Assert.Empty(helper.Data);

            Assert.Equal(0ul, helper.FindCodeRegistration());
            Assert.Equal(0ul, helper.FindMetadataRegistration());
            Assert.False(memory.PlusSearch(1, 1, 1));
        }

        [Fact]
        public void SectionHelper_BoundaryJustAbove1024_ProducesValidDataRangeAndSearchCompletesWithoutException()
        {
            // Boundary at 1025 (offset 1024, length 1)
            var segment = (offset: 1024u, data: new byte[] { 0x41 });
            var wasmBytes = BuildWasmModule(segment);

            using var wasm = new WebAssembly(new MemoryStream(wasmBytes));
            using var memory = wasm.CreateMemory();
            memory.Version = 27;

            Assert.Equal(1025ul, memory.Length);

            var helper = memory.GetSectionHelper(1, 1, 1);
            Assert.NotNull(helper);
            Assert.Single(helper.Data);
            Assert.Equal(1024ul, helper.Data[0].offset);
            Assert.Equal(1025ul, helper.Data[0].offsetEnd);

            Assert.Equal(0ul, helper.FindCodeRegistration());
            Assert.Equal(0ul, helper.FindMetadataRegistration());
            Assert.False(memory.PlusSearch(1, 1, 1));
        }

        [Fact]
        public void SectionHelper_MetadataV24_IncompletePointerAtEndOfData_FindRegistrationsAndPlusSearchCompleteWithoutException()
        {
            // Case from Bot Review: Metadata v24 with extent 1025 (only 1 byte available at offset 1024)
            // Tests FindCodeRegistrationOld() boundary when reading first word
            var segment = (offset: 1024u, data: new byte[] { 0x41 });
            var wasmBytes = BuildWasmModuleWithCustomPayload(1102, segment);

            using var wasm = new WebAssembly(new MemoryStream(wasmBytes));
            using var memory = wasm.CreateMemory();
            memory.Version = 24.0;

            Assert.Equal(1025ul, memory.Length);

            var helper = memory.GetSectionHelper(1, 1, 1);
            Assert.NotNull(helper);
            Assert.Single(helper.Data);

            Assert.Equal(0ul, helper.FindCodeRegistration());
            Assert.Equal(0ul, helper.FindMetadataRegistration());
            Assert.False(memory.PlusSearch(1, 1, 1));
        }

        [Fact]
        public void SectionHelper_MetadataV27_IncompleteCandidateLayoutAtEndOfData_FindRegistrationsAndPlusSearchCompleteWithoutException()
        {
            // Case from Bot Review: Metadata v27 with extent 1029 (5 bytes: first int32 equals typeDefinitionsCount = 1)
            // Tests FindMetadataRegistrationV21() boundary when advancing and reading subsequent candidate fields
            var segment = (offset: 1024u, data: new byte[] { 1, 0, 0, 0, 0 });
            var wasmBytes = BuildWasmModuleWithCustomPayload(1102, segment);

            using var wasm = new WebAssembly(new MemoryStream(wasmBytes));
            using var memory = wasm.CreateMemory();
            memory.Version = 27.0;

            Assert.Equal(1029ul, memory.Length);

            var helper = memory.GetSectionHelper(1, 1, 1);
            Assert.NotNull(helper);
            Assert.Single(helper.Data);

            Assert.Equal(0ul, helper.FindCodeRegistration());
            Assert.Equal(0ul, helper.FindMetadataRegistration());
            Assert.False(memory.PlusSearch(1, 1, 1));
        }

        [Fact]
        public void SectionHelper_FindCodeRegistration_FinalPointerAtExactExtent_FindsRegistration()
        {
            // Case from Bot Review: Metadata v27, imageCount = 1, memory initialized [1024, 1204)
            // Final reference pointer at 1200 occupies the exact final 4 bytes of extent (1204)
            var data = new byte[180]; // initialized range [1024, 1204)
            System.Text.Encoding.ASCII.GetBytes("mscorlib.dll\0").CopyTo(data, 0);
            BitConverter.GetBytes(1024u).CopyTo(data, 1056 - 1024);
            BitConverter.GetBytes(1056u).CopyTo(data, 1088 - 1024);
            BitConverter.GetBytes(1u).CopyTo(data, 1196 - 1024);
            BitConverter.GetBytes(1088u).CopyTo(data, 1200 - 1024);

            var segment = (offset: 1024u, data: data);
            var wasmBytes = BuildWasmModuleWithCustomPayload(1102, segment);

            using var wasm = new WebAssembly(new MemoryStream(wasmBytes));
            using var memory = wasm.CreateMemory();
            memory.Version = 27;

            Assert.Equal(1204ul, memory.Length);

            var helper = memory.GetSectionHelper(1, 1, 1);
            Assert.NotNull(helper);

            var codeReg = helper.FindCodeRegistration();
            Assert.Equal(1148ul, codeReg);
        }

        [Fact]
        public void SectionHelper_FindCodeRegistration_FinalPointerWithPartialTail_FindsRegistrationWithoutIncompleteRead()
        {
            // Extent 1206 (182 bytes in segment): final reference at 1200..1203 followed by 2 extra tail bytes (1204..1205)
            // Ensures partial tail bytes are preserved without attempting an incomplete 4-byte read
            var data = new byte[182]; // initialized range [1024, 1206)
            System.Text.Encoding.ASCII.GetBytes("mscorlib.dll\0").CopyTo(data, 0);
            BitConverter.GetBytes(1024u).CopyTo(data, 1056 - 1024);
            BitConverter.GetBytes(1056u).CopyTo(data, 1088 - 1024);
            BitConverter.GetBytes(1u).CopyTo(data, 1196 - 1024);
            BitConverter.GetBytes(1088u).CopyTo(data, 1200 - 1024);
            data[180] = 0xDE;
            data[181] = 0xAD;

            var segment = (offset: 1024u, data: data);
            var wasmBytes = BuildWasmModuleWithCustomPayload(1102, segment);

            using var wasm = new WebAssembly(new MemoryStream(wasmBytes));
            using var memory = wasm.CreateMemory();
            memory.Version = 27;

            Assert.Equal(1206ul, memory.Length);

            var helper = memory.GetSectionHelper(1, 1, 1);
            Assert.NotNull(helper);

            var codeReg = helper.FindCodeRegistration();
            Assert.Equal(1148ul, codeReg);
        }

        private static byte[] BuildWasmModuleWithCustomPayload(int customSectionSize, params (uint offset, byte[] data)[] segments)
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            writer.Write(new byte[] { 0, 0x61, 0x73, 0x6D, 1, 0, 0, 0 });
            writer.Write(new byte[] { 5, 3, 1, 0, 1 }); // memory: 1 page

            if (customSectionSize > 0)
            {
                writer.Write((byte)0); // Custom section ID 0
                WriteULeb128(writer, (uint)customSectionSize);
                writer.Write(new byte[customSectionSize]);
            }

            using var payloadMs = new MemoryStream();
            using var payloadWriter = new BinaryWriter(payloadMs);
            WriteULeb128(payloadWriter, (uint)(segments?.Length ?? 0));
            if (segments != null)
            {
                for (int i = 0; i < segments.Length; i++)
                {
                    var (offset, data) = segments[i];
                    WriteULeb128(payloadWriter, 0); // memory index
                    payloadWriter.Write((byte)0x41); // i32.const
                    WriteULeb128(payloadWriter, offset);
                    payloadWriter.Write((byte)0x0B); // end
                    WriteULeb128(payloadWriter, (uint)(data?.Length ?? 0));
                    if (data != null && data.Length > 0)
                    {
                        payloadWriter.Write(data);
                    }
                }
            }
            var payload = payloadMs.ToArray();
            writer.Write((byte)11); // Section ID 11 (Data section)
            WriteULeb128(writer, (uint)payload.Length);
            writer.Write(payload);

            return ms.ToArray();
        }

        private static byte[] BuildWasmModule(params (uint offset, byte[] data)[] segments)
        {
            return BuildWasmModuleRawSegments(writer =>
            {
                WriteULeb128(writer, (uint)(segments?.Length ?? 0));
                if (segments != null)
                {
                    for (int i = 0; i < segments.Length; i++)
                    {
                        var (offset, data) = segments[i];
                        WriteULeb128(writer, 0); // memory index
                        writer.Write((byte)0x41); // i32.const
                        WriteULeb128(writer, offset);
                        writer.Write((byte)0x0B); // end
                        WriteULeb128(writer, (uint)(data?.Length ?? 0));
                        if (data != null && data.Length > 0)
                        {
                            writer.Write(data);
                        }
                    }
                }
            });
        }

        private static byte[] BuildWasmModuleWithoutDataSection()
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            writer.Write(0x6D736100u); // \0asm
            writer.Write(1); // version 1
            return ms.ToArray();
        }

        private static byte[] BuildWasmModuleRawSegments(Action<BinaryWriter> writeSegments)
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            writer.Write(0x6D736100u); // \0asm
            writer.Write(1); // version 1

            using var payloadMs = new MemoryStream();
            using var payloadWriter = new BinaryWriter(payloadMs);
            writeSegments(payloadWriter);
            var payload = payloadMs.ToArray();

            writer.Write((byte)11); // Section ID 11 (Data section)
            WriteULeb128(writer, (uint)payload.Length);
            writer.Write(payload);

            return ms.ToArray();
        }

        private static void WriteULeb128(BinaryWriter writer, uint value)
        {
            do
            {
                byte b = (byte)(value & 0x7F);
                value >>= 7;
                if (value != 0)
                    b |= 0x80;
                writer.Write(b);
            } while (value != 0);
        }
    }
}
