using ClientMJPEG;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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

                        if (_cancellationTokenSource.Token.IsCancellationRequested)
                            break;

                        var ms = new MemoryStream(imageData);
                        JpegBitmapDecoder decoder = new JpegBitmapDecoder(ms, BitmapCreateOptions.None, BitmapCacheOption.Default);
                        var source = decoder.Frames[0];

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
                                };
                            }));
                        }

                        source.CopyPixels(_rect, _pixels, _stride, 0);
                        _imageCreator.ReturnArray(imageData);

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
                catch
                {
                    //ignore
                }
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
                _routine?.Wait();
                _routine?.Dispose();
                _imageCreator?.Dispose();
                _cancellationTokenSource?.Dispose();
            }
            catch { }
        }
    }
}