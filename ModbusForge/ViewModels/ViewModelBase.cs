using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace ModbusForge.ViewModels
{
    public abstract class ViewModelBase : ObservableObject, IDisposable
    {
        private bool _disposed = false;

        // Base class for all ViewModels
        // Implements INotifyPropertyChanged through ObservableObject

        public virtual void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed resources here
                }
                _disposed = true;
            }
        }

        ~ViewModelBase()
        {
            Dispose(false);
        }
    }
}
