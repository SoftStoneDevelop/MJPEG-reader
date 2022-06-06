using System;
using System.IO;
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

                            if (_cancellationTokenSource.Token.IsCancellationRequested)
                                break;

                            if (_writableBitmap == null)
                            {
                                var image = new BitmapImage();
                                image.BeginInit();
                                using var ms = new MemoryStream(imageData);
                                image.StreamSource = ms;
                                image.EndInit();

                                _writableBitmap = new WriteableBitmap(
                                    image.PixelWidth,
                                    image.PixelHeight,
                                    image.DpiX,
                                    image.DpiY,
                                    image.Format,
                                    null
                                    );

                                if (_cameraImage == null)
                                {
                                    _dispatcher.Invoke(DispatcherPriority.Normal, (ThreadStart)(() =>
                                    {
                                        CameraImage = new Image
                                        {
                                            Source = _writableBitmap,
                                            HorizontalAlignment = HorizontalAlignment.Center,
                                            VerticalAlignment = VerticalAlignment.Center,
                                        };
                                    }));
                                }
                            }

                            var rect = new Int32Rect(0, 0, _writableBitmap.PixelWidth, _writableBitmap.PixelHeight);
                            var stride = (rect.Width * _writableBitmap.Format.BitsPerPixel + 7) / 8;

                            _writableBitmap.WritePixels(
                                rect,
                                imageData,
                                stride,
                                0);

                            //TODO save to selected folder
                            //image.Save(@$"E:\work\mjpeg task\mjpeg\image{DateTime.UtcNow}.jpg");
                        }
                    }
                    catch
                    {
                        //ignore
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