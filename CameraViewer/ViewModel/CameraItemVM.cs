using ClientMJPEG;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
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
                        var ms = new MemoryStream(imageData);


                        if (_cancellationTokenSource.Token.IsCancellationRequested)
                            break;

                        var tempImage = CameraImage;
                        Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, (ThreadStart)(() =>
                        {
                            var image = new BitmapImage();
                            image.BeginInit();
                            image.StreamSource = ms;
                            image.EndInit();
                            
                            CameraImage = image;
                        }));

                        Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Background, (ThreadStart)(() =>
                        {
                            tempImage?.StreamSource?.Dispose();
                        }));

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

        public BitmapImage CameraImage
        {
            get => _cameraImage;
            set => Set(ref _cameraImage, value);
        }
        private BitmapImage _cameraImage;

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