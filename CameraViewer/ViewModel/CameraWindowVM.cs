using System;
using System.Buffers;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ClientMJPEG;

namespace CameraViewer.ViewModel
{
    public class CameraWindowVM : BaseVM, IDisposable
    {
        private Dispatcher _dispatcher;
        private readonly ImageCreator _imageCreator;
        private Task _routine;
        private CancellationTokenSource _cancellationTokenSource;

        private Task _setDispatcherTask;

        public CameraWindowVM(
            ImageCreator imageCreator
            )
        {
            _imageCreator = imageCreator;
            _imageCreator.Start();

            _cancellationTokenSource = new CancellationTokenSource();
            var currentThread = Thread.CurrentThread;
            _setDispatcherTask = Task.Factory.StartNew(() =>
            {
                _dispatcher = Dispatcher.FromThread(currentThread);
                while (_dispatcher == null && !_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    _dispatcher = Dispatcher.FromThread(currentThread);
                    Thread.Yield();
                }

                _routine = Task.Factory.StartNew(async _ =>
                {
                    try
                    {
                        while (await _imageCreator.ImageByteReader.WaitToReadAsync(_cancellationTokenSource.Token))
                        {
                            var imageData = await _imageCreator.ImageByteReader.ReadAsync(_cancellationTokenSource.Token);
                            var pool = ArrayPool<byte>.Shared;
                            var data = pool.Rent(imageData.Memory.Length);
                            BitmapFrame source;
                            try
                            {
                                if (_cancellationTokenSource.Token.IsCancellationRequested)
                                    break;

                                imageData.Memory.CopyTo(data);
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

                                _dispatcher.Invoke(DispatcherPriority.Normal, (ThreadStart)(() =>
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
                                    };
                                }));
                            }

                            source.CopyPixels(_rect, _pixels, _stride, 0);

                            _dispatcher.Invoke(DispatcherPriority.Normal, (ThreadStart)(() =>
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
                }, TaskCreationOptions.LongRunning, _cancellationTokenSource.Token);

            }, _cancellationTokenSource.Token);
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
                _setDispatcherTask?.Wait();
                _setDispatcherTask?.Dispose();

                _routine?.Wait();
                _routine?.Dispose();

                _imageCreator?.Dispose();
                _cancellationTokenSource?.Dispose();
            }
            catch { }
        }
    }
}