using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Il2CppDumper.Tests
{
    public class BoyerMooreHorspoolTests
    {
        [Fact]
        public void Search_IssueReproFixtures_ReturnExpectedIndexOne()
        {
            // Fixture 1 from Issue #3
            var fixture1Source = new byte[] { 0, 0x11, 0xAA };
            var fixture1Pattern = "? AA";
            var result1 = fixture1Source.Search(fixture1Pattern).ToList();
            Assert.Equal(new[] { 1 }, result1);

            // Fixture 2 (ARM registration signature) from Issue #3
            var fixture2Source = new byte[] { 0x55, 0x44, 0x10, 0x44, 0xE7, 0x44, 0x00, 0x44, 0xE0, 0x44, 0x20, 0x44, 0xE0 };
            var fixture2Pattern = "? 0x10 ? 0xE7 ? 0x00 ? 0xE0 ? 0x20 ? 0xE0";
            var result2 = fixture2Source.Search(fixture2Pattern).ToList();
            Assert.Equal(new[] { 1 }, result2);
        }

        [Fact]
        public void Search_InvokedViaReflection_MatchesExpected()
        {
            var type = typeof(Metadata).Assembly.GetType("Il2CppDumper.BoyerMooreHorspool");
            Assert.NotNull(type);
            var method = type.GetMethod("Search", new[] { typeof(byte[]), typeof(string) });
            Assert.NotNull(method);

            var found = ((IEnumerable<int>)method.Invoke(null, new object[] { new byte[] { 0, 0x11, 0xAA }, "? AA" })!).ToList();
            Assert.Equal(new[] { 1 }, found);

            var arm = new byte[] { 0x55, 0x44, 0x10, 0x44, 0xE7, 0x44, 0, 0x44, 0xE0, 0x44, 0x20, 0x44, 0xE0 };
            var matches = ((IEnumerable<int>)method.Invoke(null, new object[] { arm, "? 0x10 ? 0xE7 ? 0x00 ? 0xE0 ? 0x20 ? 0xE0" })!).ToList();
            Assert.Equal(new[] { 1 }, matches);
        }

        [Fact]
        public void Search_LeadingWildcards_FindsAllOccurrences()
        {
            var source = new byte[] { 0x00, 0x11, 0xAA, 0xBB, 0x22, 0xAA, 0xBB, 0x33 };
            var pattern = "? AA BB";

            var result = source.Search(pattern).ToList();
            var expected = ExhaustiveSearch(source, pattern);

            Assert.Equal(new[] { 1, 4 }, result);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void Search_MultipleLeadingWildcards_FindsAllOccurrences()
        {
            var source = new byte[] { 0x01, 0x02, 0xAA, 0xBB, 0x03, 0x04, 0xAA, 0xBB };
            var pattern = "? ? AA BB";

            var result = source.Search(pattern).ToList();
            var expected = ExhaustiveSearch(source, pattern);

            Assert.Equal(new[] { 0, 4 }, result);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void Search_InteriorWildcards_FindsAllOccurrences()
        {
            var source = new byte[] { 0xAA, 0x10, 0xBB, 0xCC, 0xAA, 0x20, 0xBB, 0xCC };
            var pattern = "AA ? BB";

            var result = source.Search(pattern).ToList();
            var expected = ExhaustiveSearch(source, pattern);

            Assert.Equal(new[] { 0, 4 }, result);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void Search_MultipleInteriorWildcards_FindsAllOccurrences()
        {
            var source = new byte[] { 0xAA, 0x01, 0x02, 0xBB, 0xAA, 0x03, 0x04, 0xBB };
            var pattern = "AA ? ? BB";

            var result = source.Search(pattern).ToList();
            var expected = ExhaustiveSearch(source, pattern);

            Assert.Equal(new[] { 0, 4 }, result);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void Search_TrailingWildcards_FindsAllOccurrences()
        {
            var source = new byte[] { 0xAA, 0xBB, 0x11, 0x00, 0xAA, 0xBB, 0x22 };
            var pattern = "AA BB ?";

            var result = source.Search(pattern).ToList();
            var expected = ExhaustiveSearch(source, pattern);

            Assert.Equal(new[] { 0, 4 }, result);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void Search_MultipleTrailingWildcards_FindsAllOccurrences()
        {
            var source = new byte[] { 0xAA, 0xBB, 0x11, 0x22, 0xAA, 0xBB, 0x33, 0x44 };
            var pattern = "AA BB ? ?";

            var result = source.Search(pattern).ToList();
            var expected = ExhaustiveSearch(source, pattern);

            Assert.Equal(new[] { 0, 4 }, result);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void Search_OverlappingMatches_FindsAllOverlappingOffsets()
        {
            // Pattern with interior wildcard overlapping
            var source1 = new byte[] { 0xAA, 0x11, 0xAA, 0x22, 0xAA };
            var pattern1 = "AA ? AA";
            var result1 = source1.Search(pattern1).ToList();
            Assert.Equal(new[] { 0, 2 }, result1);
            Assert.Equal(ExhaustiveSearch(source1, pattern1), result1);

            // Pattern with trailing wildcard overlapping
            var source2 = new byte[] { 0xAA, 0xAA, 0xAA };
            var pattern2 = "AA ?";
            var result2 = source2.Search(pattern2).ToList();
            Assert.Equal(new[] { 0, 1 }, result2);
            Assert.Equal(ExhaustiveSearch(source2, pattern2), result2);

            // Literal overlapping
            var source3 = new byte[] { 0xAA, 0xAA, 0xAA, 0xAA };
            var pattern3 = "AA AA";
            var result3 = source3.Search(pattern3).ToList();
            Assert.Equal(new[] { 0, 1, 2 }, result3);
            Assert.Equal(ExhaustiveSearch(source3, pattern3), result3);
        }

        [Fact]
        public void Search_LiteralOnlyPattern_MatchesExpected()
        {
            var source = new byte[] { 0x10, 0x20, 0x30, 0x40, 0x10, 0x20, 0x30 };
            var pattern = "0x10 0x20 0x30";

            var result = source.Search(pattern).ToList();
            var expected = ExhaustiveSearch(source, pattern);

            Assert.Equal(new[] { 0, 4 }, result);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void Search_AllWildcards_MatchesEveryPossiblePosition()
        {
            var source = new byte[] { 0x10, 0x20, 0x30, 0x40, 0x50 };
            var pattern = "? ? ?";

            var result = source.Search(pattern).ToList();
            var expected = ExhaustiveSearch(source, pattern);

            Assert.Equal(new[] { 0, 1, 2 }, result);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void Search_SingleWildcardAndSingleLiteral_MatchesExpected()
        {
            var source = new byte[] { 0x10, 0x20, 0x10 };

            var resultWildcard = source.Search("?").ToList();
            Assert.Equal(new[] { 0, 1, 2 }, resultWildcard);

            var resultLiteral = source.Search("0x10").ToList();
            Assert.Equal(new[] { 0, 2 }, resultLiteral);
        }

        [Fact]
        public void Search_DoubleQuestionMarkWildcards_HandledCorrectly()
        {
            var source = new byte[] { 0x55, 0x11, 0xAA, 0x22, 0xAA };
            var pattern = "?? AA";

            var result = source.Search(pattern).ToList();
            var expected = ExhaustiveSearch(source, pattern);

            Assert.Equal(new[] { 1, 3 }, result);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void Search_HexFormatVariations_ToleratesPrefixAndCase()
        {
            var source = new byte[] { 0x0A, 0xBC, 0xDE, 0xF0 };
            var pattern = "0xa bc 0xDe F0";

            var result = source.Search(pattern).ToList();
            Assert.Equal(new[] { 0 }, result);
        }

        [Fact]
        public void Search_MultipleSpacesAndWhitespace_HandledCorrectly()
        {
            var source = new byte[] { 0x00, 0xAA, 0xBB };
            var pattern = "  ?   AA   0xBB  ";

            var result = source.Search(pattern).ToList();
            Assert.Equal(new[] { 0 }, result);
        }

        [Fact]
        public void Search_EdgeCases_NullAndEmptyInputs()
        {
            Assert.Throws<ArgumentNullException>(() => ((byte[])null!).Search("AA").ToList());
            Assert.Throws<ArgumentNullException>(() => new byte[] { 1 }.Search((string)null!).ToList());

            Assert.Empty(Array.Empty<byte>().Search("AA").ToList());
            Assert.Empty(new byte[] { 1 }.Search("").ToList());
            Assert.Empty(new byte[] { 1 }.Search("   ").ToList());
            Assert.Empty(new byte[] { 1 }.Search("01 02 03").ToList()); // Pattern longer than source
        }

        [Theory]
        [InlineData(1337)]
        [InlineData(4242)]
        [InlineData(9999)]
        public void Search_ComparedAgainstExhaustiveSearch_RandomizedBuffers(int seed)
        {
            var rng = new Random(seed);

            for (int iter = 0; iter < 50; iter++)
            {
                int bufferLength = rng.Next(10, 500);
                var buffer = new byte[bufferLength];
                rng.NextBytes(buffer);

                // Test various pattern lengths and wildcard densities
                int patternLength = rng.Next(1, 15);
                double wildcardProbability = rng.NextDouble(); // 0.0 to 1.0

                var tokens = new List<string>();
                for (int p = 0; p < patternLength; p++)
                {
                    if (rng.NextDouble() < wildcardProbability)
                    {
                        tokens.Add(rng.Next(2) == 0 ? "?" : "??");
                    }
                    else
                    {
                        byte b = (byte)rng.Next(0, 256);
                        tokens.Add(rng.Next(2) == 0 ? $"0x{b:X2}" : $"{b:X2}");
                    }
                }

                string patternString = string.Join(" ", tokens);

                var actual = buffer.Search(patternString).ToList();
                var expected = ExhaustiveSearch(buffer, patternString);

                Assert.Equal(expected, actual);
            }
        }

        [Theory]
        [InlineData(0xAA, "AA ? AA", 20)]
        [InlineData(0x00, "? 00 ?", 30)]
        [InlineData(0xFF, "FF FF", 15)]
        [InlineData(0x77, "? ?", 10)]
        public void Search_ComparedAgainstExhaustiveSearch_RepetitiveBuffers(byte fillByte, string pattern, int length)
        {
            var buffer = new byte[length];
            Array.Fill(buffer, fillByte);

            var actual = buffer.Search(pattern).ToList();
            var expected = ExhaustiveSearch(buffer, pattern);

            Assert.Equal(expected, actual);
        }

        private static List<int> ExhaustiveSearch(byte[] source, string patternString)
        {
            var tokens = patternString.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (source == null || tokens.Length == 0 || tokens.Length > source.Length)
            {
                return new List<int>();
            }

            var pattern = new int[tokens.Length];
            for (int i = 0; i < tokens.Length; i++)
            {
                if (tokens[i] == "?" || tokens[i] == "??")
                {
                    pattern[i] = -1;
                }
                else
                {
                    pattern[i] = Convert.ToInt32(tokens[i], 16);
                }
            }

            var matches = new List<int>();
            for (int index = 0; index <= source.Length - pattern.Length; index++)
            {
                bool matched = true;
                for (int i = 0; i < pattern.Length; i++)
                {
                    if (pattern[i] != -1 && source[index + i] != pattern[i])
                    {
                        matched = false;
                        break;
                    }
                }

                if (matched)
                {
                    matches.Add(index);
                }
            }

            return matches;
        }
    }
}
