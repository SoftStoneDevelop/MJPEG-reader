using ClientMJPEG;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CameraViewer
{
    public class JFIFDecoder
    {
        private static class JFIFMarkers
        {
            public const byte FF = 0xFF;

            public const byte SOI = 0xD8;
            public const byte EOI = 0xD9;

            public const byte APP0 = 0xE0;
            public const byte APP1 = 0xE1;
            public const byte APP2 = 0xE2;
            public const byte APP3 = 0xE3;
            public const byte APP4 = 0xE4;
            public const byte APP5 = 0xE5;
            public const byte APP6 = 0xE6;
            public const byte APP7 = 0xE7;
            public const byte APP8 = 0xE8;
            public const byte APP9 = 0xE9;

            public const byte SOF0 = 0xC0;
            public const byte SOF1 = 0xC1;
            public const byte SOF2 = 0xC2;
            public const byte SOF3 = 0xC3;

            public const byte J = 0x4A;
            public const byte F = 0x46;
            public const byte I = 0x49;
            public const byte Empty = 0x00;

        }

        public bool DecodeJPEG(
            Span<byte> jpeg,
            Span<byte> pixels
            )
        {
            var currentIndex = 0;

            if (jpeg.Length < 2 || 
                jpeg[currentIndex++] != JFIFMarkers.FF || 
                jpeg[currentIndex++] != JFIFMarkers.SOI
                )
            {
                return false;
            }

            if (!ParseAPP0(jpeg, currentIndex, out var header))
            {
                return false;
            }

            //skipp splitter
            currentIndex += header.Length + 2;

            //skipp specific APPn
            //Span<byte> slice = jpeg.Slice(currentIndex);
            //if (jpeg[currentIndex] == JFIFMarkers.FF && jpeg[currentIndex + 1] > JFIFMarkers.APP0 && jpeg[currentIndex + 1] < JFIFMarkers.APP9)
            //{
            //    var length = BitConverter.ToUInt16(jpeg.Slice(currentIndex + 2));
            //    currentIndex += length + 1;
            //}

            var c = BitConverter.ToString(jpeg.Slice(currentIndex, 1).ToArray(), 0);

            //Quantization Tables
            var qtList = new List<int>(1);
            while (
                currentIndex < jpeg.Length &&
                jpeg[currentIndex] == 0xFF &&
                jpeg[currentIndex + 1] == 0xDB
                )
            {
                currentIndex += 2;
                //TODO fill table
                qtList.Add(0);
            }

            if (qtList.Count == 0)
            {
                return false;
            }

            return true;
        }

        private bool ParseAPP0(
            Span<byte> data,
            int currentIndex,
            out APP0 header
            )
        {
            header = new APP0();

            // Total APP0 field byte count,
            // including the byte count value(2 bytes),
            // but excluding the APP0 marker itself
            if (data[currentIndex++] != JFIFMarkers.FF || data[currentIndex++] != JFIFMarkers.APP0)
            {
                return false;
            }

            header.Length = BitConverter.ToUInt16(data.Slice(currentIndex));
            currentIndex += 2;

            // = X'4A', X'46', X'49', X'46', X'00'
            // This zero terminated string (“JFIF”) uniquely
            // identifies this APP0 marker.This string shall
            // have zero parity (bit 7=0).
            if (data[currentIndex++] != JFIFMarkers.J ||
                data[currentIndex++] != JFIFMarkers.F ||
                data[currentIndex++] != JFIFMarkers.I ||
                data[currentIndex++] != JFIFMarkers.F ||
                data[currentIndex++] != JFIFMarkers.Empty
                )
            {
                return false;
            }

            // = X'0102'
            // The most significant byte is used for major
            // revisions, the least significant byte for minor
            // revisions.Version 1.02 is the current released
            // revision.
            header.VersionMajor = data[currentIndex++];
            header.VersionMinor = data[currentIndex++];

            // Units for the X and Y densities.
            // units = 0: no units, X and Y specify the pixel
            // aspect ratio
            // units = 1: X and Y are dots per inch
            // units = 2: X and Y are dots per cm
            header.UnitsType = (UnitsType)data[currentIndex++];
            header.Xdensity = BitConverter.ToUInt16(data.Slice(currentIndex++));
            header.Ydensity = BitConverter.ToUInt16(data.Slice(currentIndex++));
            header.Xthumbnail = data[currentIndex++];
            header.Ythumbnail = data[currentIndex++];

            currentIndex += 3 * header.Xthumbnail * header.Ythumbnail;

            return true;
        }

        private struct APP0
        {
            public ushort Length;
            public byte VersionMajor;
            public byte VersionMinor;

            public UnitsType UnitsType;

            /// <summary>
            /// Horizontal pixel density
            /// </summary>
            public ushort Xdensity;

            /// <summary>
            /// Vertical pixel density
            /// </summary>
            public ushort Ydensity;

            /// <summary>
            /// Thumbnail horizontal pixel count
            /// </summary>
            public byte Xthumbnail;

            /// <summary>
            /// Thumbnail vertical pixel count
            /// </summary>
            public byte Ythumbnail;
        }

        private struct APP0Ext
        {
            public short Length;
            public ThumbnailFormat ThumbnailFormat;
            public int ThumbnailStartIndex;
        }

        private enum UnitsType : byte
        {
            NoUnits = 0,
            DotsPerInch = 1,
            DotsPerCm = 2
        }

        private enum ThumbnailFormat : byte
        {
            JPEG = 0,
            OneBytePerPixel = 1,
            ThreeBytePerPixel = 2
        }
    }
}
