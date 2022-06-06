using System;
using System.Threading.Tasks;
using System.Windows;
using CameraViewer.Factories;
using CameraViewer.ViewModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CameraViewer
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private readonly IHost _container;

        public App()
        {
            _container = ConfigureContainer();
            _container.RunAsync();
        }

        private IHost ConfigureContainer()
        {
            var host = Host.CreateDefaultBuilder();
            var container = host.ConfigureServices(services =>
            {
                services.AddSingleton<IImageCreatorFactory, ImageCreatorFactory>();
                services.AddSingleton<IWindowCreatorService, WindowCreatorService>();
                services.AddSingleton<MainWindowVM>();
                services.AddSingleton<MainWindow>();
            }
            )
            .Build();

            return container;
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            var mainWindow = _container.Services.GetService<MainWindow>();
            mainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _container?.Dispose();
            base.OnExit(e);
        }
    }
}