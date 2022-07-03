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
            Memory<byte> pattern
        )
        {
            var index = -1;

            for (int i = 0; i < source.Length; i++)
            {
                if (source.Length - i < pattern.Length)
                    return index;

                for (int j = 0; j < pattern.Length; j++)
                {
                    if (source.Span[i + j] == pattern.Span[j])
                    {
                        if (j == pattern.Length - 1)
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