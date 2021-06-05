using System.Windows;
using CameraViewer.Factories;
using CameraViewer.ViewModel;
using Unity;

namespace CameraViewer
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private readonly IUnityContainer _container;

        public App()
        {
            _container = new UnityContainer();
            ConfigureContainer(_container);
        }

        private void ConfigureContainer(IUnityContainer container)
        {
            container.RegisterType<IImageCreatorFactory, ImageCreatorFactory>();
            container.RegisterType<IWindowCreatorService, WindowCreatorService>();

            container.RegisterType<MainWindowVM>();
            container.RegisterType<MainWindow>();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            var mainWindow = _container.Resolve<MainWindow>();
            mainWindow.Show();
        }
    }
}