using System;

namespace ClientMJPEG
{
    public static class Extensions
    {
        /// <summary>
        /// Find first equal bytes in source
        /// </summary>
        /// <returns>-1 if not find in source</returns>
        public static int FindBytesIndex(
            this Memory<byte> source,
            in byte[] pattern,
            in int patternSize
        )
        {
            var index = -1;

            for (int i = 0; i < source.Length; i++)
            {
                if (source.Length - i < patternSize)
                    return index;

                for (int j = 0; j < patternSize; j++)
                {
                    if (source.Span[i + j] == pattern[j])
                    {
                        if (j == patternSize - 1)
                        {
                            index = i;
                            return index;
                        }
                        else
                        {
                            continue;
                        }
                    }
                    else
                    {
                        break;
                    }
                }
            }

            return index;
        }
    }
}