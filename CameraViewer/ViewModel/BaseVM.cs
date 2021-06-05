using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CameraViewer.ViewModel
{
    public abstract class BaseVM : INotifyPropertyChanged
    {
        private bool _disablePropertyChangedEvent = false;
        public bool DisablePropertyChangedEvent
        {
            set
            {
                if (_disablePropertyChangedEvent == value)
                    return;

                _disablePropertyChangedEvent = value;
                RaisePropertyChanged();
            }

            get => _disablePropertyChangedEvent;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void RaisePropertyChanged([CallerMemberName] string propertyName = "")
        {
            if (!DisablePropertyChangedEvent)
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool Set<T>(ref T backingField, T value, [CallerMemberName] string propertyName = "")
        {
            if (EqualityComparer<T>.Default.Equals(backingField, value))
            {
                return false;
            }

            backingField = value;
            this.RaisePropertyChanged(propertyName);
            return true;
        }

        protected bool Set<T>(ref T backingField, T value, bool raisePropertyChanged, [CallerMemberName] string propertyName = "")
        {
            if (EqualityComparer<T>.Default.Equals(backingField, value))
            {
                return false;
            }

            backingField = value;
            if (raisePropertyChanged)
                this.RaisePropertyChanged(propertyName);

            return true;
        }
    }
}