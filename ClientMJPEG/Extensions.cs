﻿using System;

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
            int size,
            Memory<byte> pattern
        )
        {
            var index = -1;
            for (int i = 0; i < size; i++)
            {
                if (size - i < pattern.Length)
                    return index;

                var temp = source.Slice(i, pattern.Length);
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (temp.Span[j] == pattern.Span[j])
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