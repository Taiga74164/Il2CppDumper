using System;
using System.IO;
using Xunit;

namespace Il2CppDumper.Tests
{
    public class MachoFatArchSelectionTests
    {
        private static Fat[] CreateStandardFatArray()
        {
            return new[]
            {
                new Fat { magic = 0xFEEDFACF, offset = 64, size = 64 }, // 64bit
                new Fat { magic = 0xFEEDFACE, offset = 128, size = 64 } // 32bit
            };
        }

        [Fact]
        public void TrySelectMachoFatIndex_ValidFirstChoice_SelectsZeroIndex()
        {
            var fats = CreateStandardFatArray();
            using var reader = new StringReader("1\n");
            using var writer = new StringWriter();

            var result = Program.TrySelectMachoFatIndex(reader, writer, fats, out var selectedIndex);

            Assert.True(result);
            Assert.Equal(0, selectedIndex);
            var output = writer.ToString();
            Assert.Contains("Select Platform: 1.64bit 2.32bit", output);
            Assert.DoesNotContain("Invalid selection", output);
            Assert.DoesNotContain("ERROR", output);
        }

        [Fact]
        public void TrySelectMachoFatIndex_ValidSecondChoice_SelectsOneIndex()
        {
            var fats = CreateStandardFatArray();
            using var reader = new StringReader("2\n");
            using var writer = new StringWriter();

            var result = Program.TrySelectMachoFatIndex(reader, writer, fats, out var selectedIndex);

            Assert.True(result);
            Assert.Equal(1, selectedIndex);
            var output = writer.ToString();
            Assert.Contains("Select Platform: 1.64bit 2.32bit", output);
            Assert.DoesNotContain("Invalid selection", output);
            Assert.DoesNotContain("ERROR", output);
        }

        [Fact]
        public void TrySelectMachoFatIndex_WhitespaceAroundValidDigit_SelectsCorrectIndex()
        {
            var fats = CreateStandardFatArray();
            using var reader = new StringReader("   1   \n");
            using var writer = new StringWriter();

            var result = Program.TrySelectMachoFatIndex(reader, writer, fats, out var selectedIndex);

            Assert.True(result);
            Assert.Equal(0, selectedIndex);
        }

        [Fact]
        public void TrySelectMachoFatIndex_NonNumericInput_RePromptsAndSucceedsOnSubsequentValidNumericInput()
        {
            // Exact scenario from Issue #27:
            // Non-numeric input (e.g., 'a', 'xyz', punctuation) re-prompts instead of crashing with FormatException.
            var fats = CreateStandardFatArray();
            using var reader = new StringReader("a\nxyz\n!\n2\n");
            using var writer = new StringWriter();

            var result = Program.TrySelectMachoFatIndex(reader, writer, fats, out var selectedIndex);

            Assert.True(result);
            Assert.Equal(1, selectedIndex);

            var output = writer.ToString();
            // Verify re-prompt messages are emitted
            Assert.Contains("Invalid selection. Please enter a number between 1 and 2.", output);
            // Verify the prompt was displayed multiple times
            var promptCount = (output.Length - output.Replace("Select Platform:", "").Length) / "Select Platform:".Length;
            Assert.Equal(4, promptCount);
        }

        [Fact]
        public void TrySelectMachoFatIndex_OutOfRangeInput_RePromptsAndSucceedsOnSubsequentValidInput()
        {
            var fats = CreateStandardFatArray();
            using var reader = new StringReader("0\n3\n99\n-1\n1\n");
            using var writer = new StringWriter();

            var result = Program.TrySelectMachoFatIndex(reader, writer, fats, out var selectedIndex);

            Assert.True(result);
            Assert.Equal(0, selectedIndex);

            var output = writer.ToString();
            Assert.Contains("Invalid selection. Please enter a number between 1 and 2.", output);
        }

        [Fact]
        public void TrySelectMachoFatIndex_ImmediateEof_TerminatesGracefullyWithFalse()
        {
            // Exact scenario from Issue #27:
            // EOF handling: terminates gracefully and signals non-zero exit code.
            var fats = CreateStandardFatArray();
            using var reader = new StringReader(string.Empty);
            using var writer = new StringWriter();

            var result = Program.TrySelectMachoFatIndex(reader, writer, fats, out var selectedIndex);

            Assert.False(result);
            Assert.Equal(-1, selectedIndex);

            var output = writer.ToString();
            Assert.Contains("ERROR: Unexpected end of input while selecting platform.", output);
        }

        [Fact]
        public void TrySelectMachoFatIndex_NonNumericInputFollowedByEof_TerminatesGracefullyWithFalse()
        {
            var fats = CreateStandardFatArray();
            using var reader = new StringReader("bad_input\n");
            using var writer = new StringWriter();

            var result = Program.TrySelectMachoFatIndex(reader, writer, fats, out var selectedIndex);

            Assert.False(result);
            Assert.Equal(-1, selectedIndex);

            var output = writer.ToString();
            Assert.Contains("Invalid selection. Please enter a number between 1 and 2.", output);
            Assert.Contains("ERROR: Unexpected end of input while selecting platform.", output);
        }

        [Fact]
        public void TrySelectMachoFatIndex_EmptyOrNullFats_ReturnsFalseWithError()
        {
            using var reader = new StringReader("1\n");
            using var writer = new StringWriter();

            var nullResult = Program.TrySelectMachoFatIndex(reader, writer, null, out var nullIndex);
            Assert.False(nullResult);
            Assert.Equal(-1, nullIndex);

            var emptyResult = Program.TrySelectMachoFatIndex(reader, writer, Array.Empty<Fat>(), out var emptyIndex);
            Assert.False(emptyResult);
            Assert.Equal(-1, emptyIndex);
        }
    }
}
