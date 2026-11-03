using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Il2CppDumper.Tests
{
    public class SectionHelperRegistrationReferenceSearchTests
    {
        private sealed class DummyIl2Cpp64Bit : Il2Cpp
        {
            public DummyIl2Cpp64Bit(Stream stream) : base(stream)
            {
                Is32Bit = false;
            }

            public override ulong MapVATR(ulong addr) => addr;
            public override ulong MapRTVA(ulong addr) => addr;
            public override bool Search() => false;
            public override bool PlusSearch(int methodCount, int typeDefinitionsCount, int imageCount) => false;
            public override bool SymbolSearch() => false;
            public override SectionHelper GetSectionHelper(int methodCount, int typeDefinitionsCount, int imageCount) => null!;
            public override bool CheckDump() => false;
        }

        private sealed class DummyIl2Cpp32Bit : Il2Cpp
        {
            public DummyIl2Cpp32Bit(Stream stream) : base(stream)
            {
                Is32Bit = true;
            }

            public override ulong MapVATR(ulong addr) => addr;
            public override ulong MapRTVA(ulong addr) => addr;
            public override bool Search() => false;
            public override bool PlusSearch(int methodCount, int typeDefinitionsCount, int imageCount) => false;
            public override bool SymbolSearch() => false;
            public override SectionHelper GetSectionHelper(int methodCount, int typeDefinitionsCount, int imageCount) => null!;
            public override bool CheckDump() => false;
        }

        [Fact]
        public void FindReference_Issue5ExactReproduction_ReturnsVirtualAddress4096()
        {
            // Exact reproduction from Issue #5:
            // 32-bit WebAssemblyMemory containing 0x1234u in a 4-byte section [0, 4) at address 0x1000.
            using var il = new WebAssemblyMemory(new MemoryStream(BitConverter.GetBytes(0x1234u)), 0);
            var helper = new SectionHelper(il, 0, 0, 0, 0);
            helper.SetSection(SearchSectionType.Data, new SearchSection
            {
                offset = 0,
                offsetEnd = 4,
                address = 0x1000,
                addressEnd = 0x1004
            });

            var method = typeof(SectionHelper).GetMethod("FindReference", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);

            var found = ((IEnumerable<ulong>)method.Invoke(helper, new object[] { 0x1234ul })!).ToList();
            Assert.Single(found);
            Assert.Equal(4096ul, found[0]);
        }

        [Fact]
        public void FindReference_DirectCallMatchesReflectionCall()
        {
            using var il = new WebAssemblyMemory(new MemoryStream(BitConverter.GetBytes(0x1234u)), 0);
            var helper = new SectionHelper(il, 0, 0, 0, 0);
            helper.SetSection(SearchSectionType.Data, new SearchSection
            {
                offset = 0,
                offsetEnd = 4,
                address = 0x1000,
                addressEnd = 0x1004
            });

            var direct = helper.FindReference(0x1234ul).ToList();
            Assert.Single(direct);
            Assert.Equal(4096ul, direct[0]);
        }

        [Fact]
        public void FindReference_32Bit_FinalSlotInMultiSlotSection_FindsReference()
        {
            // Section with 3 32-bit pointers: [0x1111, 0x2222, 0x3333]
            var buffer = new byte[12];
            BitConverter.GetBytes(0x1111u).CopyTo(buffer, 0);
            BitConverter.GetBytes(0x2222u).CopyTo(buffer, 4);
            BitConverter.GetBytes(0x3333u).CopyTo(buffer, 8);

            using var il = new DummyIl2Cpp32Bit(new MemoryStream(buffer));
            var helper = new SectionHelper(il, 0, 0, 0, 0);
            helper.SetSection(SearchSectionType.Data, new SearchSection
            {
                offset = 0,
                offsetEnd = 12,
                address = 0x5000,
                addressEnd = 0x500C
            });

            // Target is the final slot at offset 8 (0x5008)
            var found = helper.FindReference(0x3333ul).ToList();
            Assert.Single(found);
            Assert.Equal(0x5008ul, found[0]);
        }

        [Fact]
        public void FindReference_64Bit_SinglePointerSection_FindsReference()
        {
            // 64-bit single pointer in 8-byte section
            var buffer = BitConverter.GetBytes(0xDEADBEEFCAFEBABEu);

            using var il = new DummyIl2Cpp64Bit(new MemoryStream(buffer));
            var helper = new SectionHelper(il, 0, 0, 0, 0);
            helper.SetSection(SearchSectionType.Data, new SearchSection
            {
                offset = 0,
                offsetEnd = 8,
                address = 0x80000000ul,
                addressEnd = 0x80000008ul
            });

            var found = helper.FindReference(0xDEADBEEFCAFEBABEu).ToList();
            Assert.Single(found);
            Assert.Equal(0x80000000ul, found[0]);
        }

        [Fact]
        public void FindReference_64Bit_FinalSlotInMultiSlotSection_FindsReference()
        {
            // 64-bit section with 3 slots: [0x1000, 0x2000, 0x3000]
            var buffer = new byte[24];
            BitConverter.GetBytes(0x1000ul).CopyTo(buffer, 0);
            BitConverter.GetBytes(0x2000ul).CopyTo(buffer, 8);
            BitConverter.GetBytes(0x3000ul).CopyTo(buffer, 16);

            using var il = new DummyIl2Cpp64Bit(new MemoryStream(buffer));
            var helper = new SectionHelper(il, 0, 0, 0, 0);
            helper.SetSection(SearchSectionType.Data, new SearchSection
            {
                offset = 0,
                offsetEnd = 24,
                address = 0x10000ul,
                addressEnd = 0x10018ul
            });

            var found = helper.FindReference(0x3000ul).ToList();
            Assert.Single(found);
            Assert.Equal(0x10010ul, found[0]);
        }

        [Fact]
        public void FindReference_SectionWithNonZeroOffset_CalculatesAddressCorrectly()
        {
            // Section offset starts at 32, offsetEnd = 40 (single 64-bit pointer)
            var buffer = new byte[40];
            BitConverter.GetBytes(0xABCD1234ul).CopyTo(buffer, 32);

            using var il = new DummyIl2Cpp64Bit(new MemoryStream(buffer));
            var helper = new SectionHelper(il, 0, 0, 0, 0);
            helper.SetSection(SearchSectionType.Data, new SearchSection
            {
                offset = 32,
                offsetEnd = 40,
                address = 0x20000ul,
                addressEnd = 0x20008ul
            });

            var found = helper.FindReference(0xABCD1234ul).ToList();
            Assert.Single(found);
            Assert.Equal(0x20000ul, found[0]);
        }

        [Fact]
        public void FindReference_StreamShorterThanSection_ClampsToStreamLengthWithoutThrowing()
        {
            // Section claims offsetEnd 16, but stream has only 8 bytes (two 32-bit slots)
            var buffer = new byte[8];
            BitConverter.GetBytes(0xAAAu).CopyTo(buffer, 0);
            BitConverter.GetBytes(0xBBBu).CopyTo(buffer, 4);

            using var il = new DummyIl2Cpp32Bit(new MemoryStream(buffer));
            var helper = new SectionHelper(il, 0, 0, 0, 0);
            helper.SetSection(SearchSectionType.Data, new SearchSection
            {
                offset = 0,
                offsetEnd = 16,
                address = 0x1000,
                addressEnd = 0x1010
            });

            var found = helper.FindReference(0xBBBul).ToList();
            Assert.Single(found);
            Assert.Equal(0x1004ul, found[0]);
        }

        [Fact]
        public void FindReference_UnalignedTrailingBytes_IgnoresPartialSlotWithoutThrowing()
        {
            // 32-bit: 10 bytes total (slots at 0..3 and 4..7, trailing 2 bytes at 8..9)
            var buffer = new byte[10];
            BitConverter.GetBytes(0x1111u).CopyTo(buffer, 0);
            BitConverter.GetBytes(0x2222u).CopyTo(buffer, 4);
            buffer[8] = 0xAA;
            buffer[9] = 0xBB;

            using var il = new DummyIl2Cpp32Bit(new MemoryStream(buffer));
            var helper = new SectionHelper(il, 0, 0, 0, 0);
            helper.SetSection(SearchSectionType.Data, new SearchSection
            {
                offset = 0,
                offsetEnd = 10,
                address = 0x4000,
                addressEnd = 0x400A
            });

            var found = helper.FindReference(0x2222ul).ToList();
            Assert.Single(found);
            Assert.Equal(0x4004ul, found[0]);
        }

        [Fact]
        public void FindReference_EmptySection_ReturnsEmptyWithoutUnderflowOrException()
        {
            using var il = new DummyIl2Cpp32Bit(new MemoryStream(Array.Empty<byte>()));
            var helper = new SectionHelper(il, 0, 0, 0, 0);
            helper.SetSection(SearchSectionType.Data, new SearchSection
            {
                offset = 0,
                offsetEnd = 0,
                address = 0x1000,
                addressEnd = 0x1000
            });

            var found = helper.FindReference(0x1234ul).ToList();
            Assert.Empty(found);
        }

        [Fact]
        public void FindReference_ShortSectionSmallerThanPointer_ReturnsEmptyWithoutUnderflowOrException()
        {
            // 3 bytes in 32-bit mode (pointer requires 4 bytes)
            var buffer = new byte[] { 0x01, 0x02, 0x03 };
            using var il = new DummyIl2Cpp32Bit(new MemoryStream(buffer));
            var helper = new SectionHelper(il, 0, 0, 0, 0);
            helper.SetSection(SearchSectionType.Data, new SearchSection
            {
                offset = 0,
                offsetEnd = 3,
                address = 0x1000,
                addressEnd = 0x1003
            });

            var found = helper.FindReference(0x1234ul).ToList();
            Assert.Empty(found);
        }

        [Fact]
        public void FindReference_InvertedSection_ReturnsEmptyWithoutUnderflowOrException()
        {
            var buffer = new byte[16];
            using var il = new DummyIl2Cpp32Bit(new MemoryStream(buffer));
            var helper = new SectionHelper(il, 0, 0, 0, 0);
            helper.SetSection(SearchSectionType.Data, new SearchSection
            {
                offset = 12,
                offsetEnd = 4, // inverted
                address = 0x2000,
                addressEnd = 0x2004
            });

            var found = helper.FindReference(0x1234ul).ToList();
            Assert.Empty(found);
        }

        [Fact]
        public void FindReference_SectionBeyondStreamLength_ReturnsEmptyWithoutUnderflowOrException()
        {
            var buffer = new byte[8];
            using var il = new DummyIl2Cpp32Bit(new MemoryStream(buffer));
            var helper = new SectionHelper(il, 0, 0, 0, 0);
            helper.SetSection(SearchSectionType.Data, new SearchSection
            {
                offset = 16,
                offsetEnd = 32,
                address = 0x3000,
                addressEnd = 0x3010
            });

            var found = helper.FindReference(0x1234ul).ToList();
            Assert.Empty(found);
        }

        [Fact]
        public void FindReference_ExtremeOffsetValues_ReturnsEmptyWithoutOverflowOrException()
        {
            var buffer = new byte[16];
            using var il = new DummyIl2Cpp32Bit(new MemoryStream(buffer));
            var helper = new SectionHelper(il, 0, 0, 0, 0);
            helper.SetSection(SearchSectionType.Data, new SearchSection
            {
                offset = ulong.MaxValue - 2,
                offsetEnd = 16,
                address = 0x1000,
                addressEnd = 0x1010
            });

            var found = helper.FindReference(0x1234ul).ToList();
            Assert.Empty(found);
        }

        [Fact]
        public void FindReference_NullDataList_ReturnsEmptyWithoutException()
        {
            using var il = new DummyIl2Cpp32Bit(new MemoryStream(new byte[16]));
            var helper = new SectionHelper(il, 0, 0, 0, 0);
            // Notice: SetSection(Data) is not called, so data remains null.

            var found = helper.FindReference(0x1234ul).ToList();
            Assert.Empty(found);
        }

        [Fact]
        public void FindReference_DataListWithNullEntry_SafelySkipsNullEntry()
        {
            var buffer = BitConverter.GetBytes(0x5678u);
            using var il = new DummyIl2Cpp32Bit(new MemoryStream(buffer));
            var helper = new SectionHelper(il, 0, 0, 0, 0);
            helper.SetSection(SearchSectionType.Data, (SearchSection)null!, new SearchSection
            {
                offset = 0,
                offsetEnd = 4,
                address = 0x6000,
                addressEnd = 0x6004
            });

            var found = helper.FindReference(0x5678ul).ToList();
            Assert.Single(found);
            Assert.Equal(0x6000ul, found[0]);
        }
    }
}
