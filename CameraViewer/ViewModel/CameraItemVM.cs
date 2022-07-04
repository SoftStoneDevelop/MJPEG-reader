using ClientMJPEG;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using DispatcherPriority = System.Windows.Threading.DispatcherPriority;

namespace CameraViewer.ViewModel
{
    public class CameraItemVM : BaseVM, IDisposable
    {
        private readonly ImageCreator _imageCreator;
        private Task _routine;
        private CancellationTokenSource _cancellationTokenSource;

        public CameraItemVM(
            ImageCreator imageCreator
            )
        {
            _imageCreator = imageCreator;
            _imageCreator.Start();

            _cancellationTokenSource = new CancellationTokenSource();
            _routine = Task.Factory.StartNew(async () =>
            {
                try
                {
                    while (await _imageCreator.ImageByteReader.WaitToReadAsync(_cancellationTokenSource.Token))
                    {
                        var imageData = await _imageCreator.ImageByteReader.ReadAsync(_cancellationTokenSource.Token);
                        var pool = ArrayPool<byte>.Shared;
                        var data = pool.Rent(imageData.Memory.Length);
                        BitmapFrame source;

                        var result = decodeJPEG(imageData.Memory.Span, new Span<byte>(data));
                        try
                        {
                            if (_cancellationTokenSource.Token.IsCancellationRequested)
                                break;

                            imageData.Memory.CopyTo(data);//TODO fix double copying, memory stream don't support span
                            var ms = new MemoryStream(data);
                            JpegBitmapDecoder decoder = new JpegBitmapDecoder(ms, BitmapCreateOptions.None, BitmapCacheOption.Default);
                            source = decoder.Frames[0];
                        }
                        finally
                        {
                            imageData.Dispose();
                            pool.Return(data);
                        }

                        if (_writableBitmap == null)
                        {
                            _rect = new Int32Rect(0, 0, source.PixelWidth, source.PixelHeight);
                            _stride = (source.PixelWidth * source.Format.BitsPerPixel + 7) / 8;
                            _pixels = new byte[_rect.Height * _stride];

                            Application.Current.Dispatcher.Invoke(DispatcherPriority.Normal, (ThreadStart)(() =>
                            {
                                _writableBitmap = new WriteableBitmap(
                                    source.PixelWidth,
                                    source.PixelHeight,
                                    source.DpiX,
                                    source.DpiY,
                                    source.Format,
                                    null
                                    );

                                CameraImage = new Image
                                {
                                    Source = _writableBitmap,
                                    HorizontalAlignment = HorizontalAlignment.Center,
                                    VerticalAlignment = VerticalAlignment.Center,
                                    MaxWidth = source.Width,
                                    MaxHeight = source.Height,
                                };
                            }));
                        }

                        source.CopyPixels(_rect, _pixels, _stride, 0);

                        Application.Current.Dispatcher.Invoke(DispatcherPriority.Normal, (ThreadStart)(() =>
                        {
                            _writableBitmap.WritePixels(
                            _rect,
                            _pixels,
                            _stride,
                            0);
                        }));
                    }
                }
                catch (OperationCanceledException)
                {
                    //ignore
                }
                catch (AggregateException agg) when (agg.InnerExceptions.Any(ex => ex is not OperationCanceledException))
                {
                    throw;
                }
            }, _cancellationTokenSource.Token);
        }

        public bool decodeJPEG(Span<byte> jpeg, Span<byte> pixels)
        {
            var currentIndex = 0;
            if (jpeg.Length < 2 || jpeg[currentIndex++] != 0xFF || jpeg[currentIndex++] != 0xD8)
            {
                return false;
            }

            if(!TryGetAPP0(jpeg.Slice(currentIndex), out var header))
            {
                return false;
            }

            var n = header.Xthumbnail * header.Ythumbnail;
            if(n > 0)
            {
                header.ThumbnailStartIndex = 16;
            }
            else
            {
                header.ThumbnailStartIndex = -1;
            }

            currentIndex += header.Length + 2;

            if (header.VersionMinor > 1)
            {
                throw new Exception("Version higher than 1.1 is not supported");
            }

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

            if(qtList.Count == 0)
            {
                return false;
            }

            return true;
        }

        public bool TryGetAPP0(Span<byte> data, out APP0 header)
        {
            header = new APP0();
            var currentIndex = 0;

            // Total APP0 field byte count,
            // including the byte count value(2 bytes),
            // but excluding the APP0 marker itself
            if (data[currentIndex++] != 0xFF || data[currentIndex++] != 0xE0)
            {
                return false;
            }

            header.Length = BitConverter.ToInt16(data.Slice(currentIndex));
            currentIndex += 2;

            // = X'4A', X'46', X'49', X'46', X'00'
            // This zero terminated string (“JFIF”) uniquely
            // identifies this APP0 marker.This string shall
            // have zero parity (bit 7=0).
            if (data[currentIndex++] != 0x4A || 
                data[currentIndex++] != 0x46 || 
                data[currentIndex++] != 0x49 || 
                data[currentIndex++] != 0x46 ||
                data[currentIndex++] != 0x00
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
            header.Xdensity = BitConverter.ToInt16(data.Slice(currentIndex++));
            header.Ydensity = BitConverter.ToInt16(data.Slice(currentIndex++));
            header.Xthumbnail = data[currentIndex++];
            header.Ythumbnail = data[currentIndex++];

            return true;
        }

        public struct APP0
        {
            public short Length;
            public byte VersionMajor;
            public byte VersionMinor;

            public UnitsType UnitsType;

            /// <summary>
            /// Horizontal pixel density
            /// </summary>
            public short Xdensity;

            /// <summary>
            /// Vertical pixel density
            /// </summary>
            public short Ydensity;

            /// <summary>
            /// Thumbnail horizontal pixel count
            /// </summary>
            public byte Xthumbnail;

            /// <summary>
            /// Thumbnail vertical pixel count
            /// </summary>
            public byte Ythumbnail;

            public int ThumbnailStartIndex;
        }

        public struct APP0Ext
        {
            public short Length;
            public ThumbnailFormat ThumbnailFormat;
            public int ThumbnailStartIndex;
        }

        public enum UnitsType : byte
        {
            NoUnits = 0,
            DotsPerInch = 1,
            DotsPerCm = 2
        }

        public enum ThumbnailFormat : byte
        {
            JPEG = 0,
            OneBytePerPixel = 1,
            ThreeBytePerPixel = 2
        }


        public Image CameraImage
        {
            get => _cameraImage;
            set => Set(ref _cameraImage, value);
        }
        private Image _cameraImage;
        private WriteableBitmap _writableBitmap;
        private Int32Rect _rect;
        private int _stride;
        private byte[] _pixels;

        public void Dispose()
        {
            try
            {
                _cancellationTokenSource?.Cancel();
                _routine?.Wait();
                _routine?.Dispose();
                _imageCreator?.Dispose();
                _cancellationTokenSource?.Dispose();
            }
            catch { }
        }
    }
}