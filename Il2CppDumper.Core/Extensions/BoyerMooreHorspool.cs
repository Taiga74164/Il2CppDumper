using System;
using System.Collections.Generic;

namespace Il2CppDumper
{
    static class BoyerMooreHorspool
    {
        public static IEnumerable<int> Search(this byte[] source, byte[] pattern)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (pattern == null)
            {
                throw new ArgumentNullException(nameof(pattern));
            }

            int valueLength = source.Length;
            int patternLength = pattern.Length;

            if (valueLength == 0 || patternLength == 0 || patternLength > valueLength)
            {
                yield break;
            }

            var badCharacters = new int[256];

            for (var i = 0; i < 256; i++)
            {
                badCharacters[i] = patternLength;
            }

            var lastPatternByte = patternLength - 1;

            for (int i = 0; i < lastPatternByte; i++)
            {
                badCharacters[pattern[i]] = lastPatternByte - i;
            }

            int index = 0;

            while (index <= valueLength - patternLength)
            {
                for (var i = lastPatternByte; source[index + i] == pattern[i]; i--)
                {
                    if (i == 0)
                    {
                        yield return index;
                        break;
                    }
                }

                index += badCharacters[source[index + lastPatternByte]];
            }
        }

        public static IEnumerable<int> Search(this byte[] source, string stringPattern)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (stringPattern == null)
            {
                throw new ArgumentNullException(nameof(stringPattern));
            }

            var patternTokens = stringPattern.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            int valueLength = source.Length;
            int patternLength = patternTokens.Length;

            if (valueLength == 0 || patternLength == 0 || patternLength > valueLength)
            {
                yield break;
            }

            var pattern = new int[patternLength];
            for (int i = 0; i < patternLength; i++)
            {
                if (patternTokens[i] == "?" || patternTokens[i] == "??")
                {
                    pattern[i] = -1;
                }
                else
                {
                    pattern[i] = Convert.ToInt32(patternTokens[i], 16);
                }
            }

            var badCharacters = new int[256];
            var lastPatternByte = patternLength - 1;

            int defaultShift = patternLength;
            for (int i = 0; i < lastPatternByte; i++)
            {
                if (pattern[i] == -1)
                {
                    defaultShift = lastPatternByte - i;
                }
            }

            for (var i = 0; i < 256; i++)
            {
                badCharacters[i] = defaultShift;
            }

            for (int i = 0; i < lastPatternByte; i++)
            {
                if (pattern[i] != -1)
                {
                    var shift = lastPatternByte - i;
                    if (shift < badCharacters[pattern[i]])
                    {
                        badCharacters[pattern[i]] = shift;
                    }
                }
            }

            int index = 0;

            while (index <= valueLength - patternLength)
            {
                for (var i = lastPatternByte; CheckEqual(source, pattern, index, i); i--)
                {
                    if (i == 0)
                    {
                        yield return index;
                        break;
                    }
                }

                index += badCharacters[source[index + lastPatternByte]];
            }
        }

        private static bool CheckEqual(byte[] source, int[] pattern, int index, int i)
        {
            return pattern[i] == -1 || source[index + i] == pattern[i];
        }
    }
}
