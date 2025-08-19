using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModbusForge.Services;
using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;

namespace ModbusForge.ViewModels
{
    public partial class MainViewModel : ViewModelBase, IDisposable
    {
        private readonly IModbusService _modbusService;
        private bool _disposed = false;

        public MainViewModel() : this(App.ServiceProvider.GetRequiredService<IModbusService>())
        {
        }

        public MainViewModel(IModbusService modbusService)
        {
            _modbusService = modbusService;
            ConnectCommand = new RelayCommand(async () => await ConnectAsync());
            DisconnectCommand = new RelayCommand(Disconnect, CanDisconnect);
        }

        [ObservableProperty]
        private string _title = "ModbusForge";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]
        private bool _isConnected = false;

        [ObservableProperty]
        private string _serverAddress = "127.0.0.1";

        [ObservableProperty]
        private int _port = 502;

        [ObservableProperty]
        private string _statusMessage = "Disconnected";

        public ICommand ConnectCommand { get; }
        public ICommand DisconnectCommand { get; }

        private async Task ConnectAsync()
        {
            try
            {
                StatusMessage = "Connecting...";
                var result = await Task.Run(() => _modbusService.ConnectAsync(ServerAddress, Port).GetAwaiter().GetResult());
                
                if (result)
                {
                    IsConnected = true;
                    StatusMessage = "Connected";
                    CommandManager.InvalidateRequerySuggested();
                }
                else
                {
                    StatusMessage = "Connection failed";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                MessageBox.Show($"Failed to connect: {ex.Message}", "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Disconnect()
        {
            try
            {
                Task.Run(async () => await _modbusService.DisconnectAsync()).Wait();
                IsConnected = false;
                StatusMessage = "Disconnected";
                CommandManager.InvalidateRequerySuggested();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error disconnecting: {ex.Message}";
                MessageBox.Show($"Failed to disconnect: {ex.Message}", "Disconnection Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool CanDisconnect() => IsConnected;

        public void Dispose()
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
                    _modbusService?.Dispose();
                }
                _disposed = true;
            }
        }

        ~MainViewModel()
        {
            Dispose(false);
        }
    }
}
