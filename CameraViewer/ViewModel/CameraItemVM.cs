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

                        if (_writableBitmap == null)
                        {
                            using var ms = new MemoryStream(imageData);
                            var image = new BitmapImage();
                            image.BeginInit();
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
                                Application.Current.Dispatcher.Invoke(DispatcherPriority.Normal, (ThreadStart)(() =>
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
                _routine?.Wait();
                _routine?.Dispose();
                _imageCreator?.Dispose();
                _cancellationTokenSource?.Dispose();
            }
            catch { }
        }
    }
}