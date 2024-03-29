﻿using System;
using System.Collections.Generic;
using CameraViewer.Common;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using CameraViewer.Factories;
using System.Runtime;

namespace CameraViewer.ViewModel
{
    public class MainWindowVM : BaseVM, IDataErrorInfo
    {
        private readonly IImageCreatorFactory _imageCreatorFactory;
        private readonly IWindowCreatorService _windowCreatorService;

        public MainWindowVM(
            IImageCreatorFactory imageCreatorFactory,
            IWindowCreatorService windowCreatorService
            )
        {
            _imageCreatorFactory = imageCreatorFactory ?? throw new ArgumentNullException(nameof(imageCreatorFactory));
            _windowCreatorService = windowCreatorService ?? throw new ArgumentNullException(nameof(windowCreatorService));

            NewCameraHost = "31.160.161.51";
            NewCameraPort = "8081";
            NewCameraPath = "/mjpg/video.mjpg";
            Cameras = new ObservableCollection<CameraItemVM>();

            AddCameraCommand = new DelegateCommand(
                    () =>
                    {
                        Cameras.Add(new CameraItemVM(_imageCreatorFactory.GetCreator(_cameraHost, _cameraPort, _newCameraPath)));
                    },
                    () => CanAdd
                )
                ;

            OpenCameraWindowCommand = new DelegateCommand(
                    () =>
                    {
                        var ipAdress = _cameraHost;
                        var port = _cameraPort;
                        var path = _newCameraPath;
                        var thread = new Thread(() =>
                        {
                            try
                            {
                                var window = _windowCreatorService.CreateCameraWindow(ipAdress, port, path);

                                window.Closing
                                    += (_, _) =>
                                    {
                                        ((IDisposable)window.DataContext)?.Dispose();
                                    };

                                window.Closed
                                    += (_, _) =>
                                    {
                                        window.Dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal);
                                        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                                        GC.Collect();
                                    };

                                window.Show();

                                Dispatcher.Run();
                            }
                            catch { /* ignore*/ }
                        });

                        thread.SetApartmentState(ApartmentState.STA);
                        thread.Start();
                    },
                    () => CanAdd
                )
                ;

            CloseCameraCommand = new DelegateCommand<CameraItemVM>(
                    async item =>
                    {
                        if (item != null)
                        {
                            Cameras.Remove(item);
                            await Task.Factory.StartNew(() =>
                            {
                                item.Dispose();
                            }).ConfigureAwait(false);
                            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                            GC.Collect();
                        }
                    }
                )
                ;
        }

        public DelegateCommand<CameraItemVM> CloseCameraCommand
        {
            get => _closeCameraCommand;
            set => Set(ref _closeCameraCommand, value);
        }
        private DelegateCommand<CameraItemVM> _closeCameraCommand;

        public DelegateCommand AddCameraCommand
        {
            get => _addCameraCommand;
            set => Set(ref _addCameraCommand, value);
        }
        private DelegateCommand _addCameraCommand;

        public DelegateCommand OpenCameraWindowCommand
        {
            get => _openCameraWindowCommand;
            set => Set(ref _openCameraWindowCommand, value);
        }
        private DelegateCommand _openCameraWindowCommand;

        public ObservableCollection<CameraItemVM> Cameras
        {
            get => _cameras;
            set => Set(ref _cameras, value);
        }
        private ObservableCollection<CameraItemVM> _cameras;

        public bool CanAdd
        {
            get => _canAdd;
            set
            {
                if (Set(ref _canAdd, value))
                {
                    AddCameraCommand?.RaiseCanExecuteChanged();
                    OpenCameraWindowCommand?.RaiseCanExecuteChanged();
                }
            }
        }
        private bool _canAdd;

        public string NewCameraHost
        {
            get => _newCameraHost;
            set => Set(ref _newCameraHost, value);
        }
        private string _newCameraHost;
        private IPAddress _cameraHost;

        public string NewCameraPort
        {
            get => _newCameraPort;
            set => Set(ref _newCameraPort, value);
        }
        private string _newCameraPort;
        private int _cameraPort;

        public string NewCameraPath
        {
            get => _newCameraPath;
            set => Set(ref _newCameraPath, value);
        }
        private string _newCameraPath;

        #region IDataErrorInfo

        public string Error { get; }

        private Dictionary<string, string> _errors = new(2);

        public string this[string columnName]
        {
            get
            {
                switch (columnName)
                {
                    case nameof(NewCameraHost):
                    {
                        if (IPAddress.TryParse(NewCameraHost, out var ipAddress))
                        {
                            _errors.Remove(columnName);
                            _cameraHost = ipAddress;
                            CanAdd = !_errors.Any();

                            return "";
                        }
                        else
                        {
                            _errors[columnName] = "Incorrect Host";
                            CanAdd = !_errors.Any();
                            return "Incorrect Host";
                        }
                    }

                    case nameof(NewCameraPort):
                    {
                        if (int.TryParse(NewCameraPort, out var cameraPort))
                        {
                            _errors.Remove(columnName);
                            _cameraPort = cameraPort;
                            CanAdd = !_errors.Any();

                            return "";
                        }
                        else
                        {
                            _errors[columnName] = "Incorrect Port";
                            CanAdd = !_errors.Any();
                            return "Incorrect Port";

                        }
                    }

                    default:
                    {
                        return "";
                    }
                }
            }
        }

        #endregion
    }
}