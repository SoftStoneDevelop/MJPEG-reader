using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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
                            var ms = new MemoryStream(imageData);


                            if (_cancellationTokenSource.Token.IsCancellationRequested)
                                break;

                            var tempImage = CameraImage;
                            _dispatcher.BeginInvoke(DispatcherPriority.Normal, (ThreadStart)(() =>
                            {
                                var image = new BitmapImage();
                                image.BeginInit();
                                image.StreamSource = ms;
                                image.EndInit();

                                CameraImage = image;
                            }));

                            _dispatcher.BeginInvoke(DispatcherPriority.Background, (ThreadStart)(() =>
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
                }, TaskCreationOptions.LongRunning, _cancellationTokenSource.Token);

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