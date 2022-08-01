using System;
using System.Net;
using System.Windows;
using CameraViewer.View;
using CameraViewer.ViewModel;

namespace CameraViewer.Factories
{
    public interface IWindowCreatorService
    {
        public Window CreateCameraWindow(IPAddress address, int port, string path);
    }

    public class WindowCreatorService : IWindowCreatorService
    {
        private readonly IImageCreatorFactory _imageCreatorFactory;

        public WindowCreatorService(
            IImageCreatorFactory imageCreatorFactory
            )
        {
            _imageCreatorFactory = imageCreatorFactory ?? throw new ArgumentNullException(nameof(imageCreatorFactory));
        }

        public Window CreateCameraWindow(IPAddress address, int port, string path)
        {
            var window = new CameraWindow();
            var vm = new CameraWindowVM(_imageCreatorFactory.GetCreator(address, port, path));
            window.DataContext = vm;

            return window;
        }
    }
}