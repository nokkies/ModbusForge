using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO.Ports;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModbusForge.Models;
using ModbusForge.Services;

namespace ModbusForge.Avalonia.ViewModels
{
    /// <summary>
    /// Avalonia port of the connection manager. Manages Modbus connection profiles and their TCP/serial settings.
    /// </summary>
    public sealed partial class ConnectionManagerViewModel : ObservableObject, IDisposable
    {
        public static IReadOnlyList<TransportType> TransportOptions { get; } =
            new[] { TransportType.Tcp, TransportType.Rtu, TransportType.Ascii };

        public static IReadOnlyList<Parity> ParityOptions { get; } =
            new[] { Parity.None, Parity.Even, Parity.Odd, Parity.Mark, Parity.Space };

        public static IReadOnlyList<StopBits> StopBitsOptions { get; } =
            new[] { StopBits.None, StopBits.One, StopBits.OnePointFive, StopBits.Two };

        public bool IsSerial => SelectedProfile?.Transport is TransportType.Rtu or TransportType.Ascii;

        private readonly IConnectionManager _connectionManager;
        private readonly IDispatcher _dispatcher;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RemoveCommand))]
        [NotifyCanExecuteChangedFor(nameof(CloneCommand))]
        [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
        [NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]
        private ConnectionProfile? _selectedProfile;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
        [NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]
        private bool _isConnecting;

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        public ObservableCollection<ConnectionProfile> Profiles => _connectionManager.Profiles;

        public ObservableCollection<string> AvailableSerialPorts { get; } = new();

        public bool HasSelection => SelectedProfile != null;

        public bool CanRemove => HasSelection && Profiles.Count > 1;

        public bool CanClone => HasSelection;

        public ConnectionManagerViewModel(IConnectionManager connectionManager, IDispatcher dispatcher)
        {
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

            AddCommand = new RelayCommand(AddProfile);
            RemoveCommand = new RelayCommand(RemoveProfile, () => CanRemove);
            CloneCommand = new RelayCommand(CloneProfile, () => CanClone);
            ConnectCommand = new AsyncRelayCommand(ConnectAsync, () => CanConnect());
            DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, () => CanDisconnect());
            SaveCommand = new RelayCommand(SaveAndClose);
            CancelCommand = new RelayCommand(Cancel);

            if (_connectionManager.Profiles.Count > 0)
            {
                SelectedProfile = _connectionManager.ActiveProfile ?? _connectionManager.Profiles[0];
            }

            _connectionManager.Profiles.CollectionChanged += Profiles_CollectionChanged;
            _connectionManager.ActiveProfileChanged += ConnectionManager_ActiveProfileChanged;

            RefreshSerialPorts();
        }

        public IRelayCommand AddCommand { get; }
        public IRelayCommand RemoveCommand { get; }
        public IRelayCommand CloneCommand { get; }
        public IAsyncRelayCommand ConnectCommand { get; }
        public IAsyncRelayCommand DisconnectCommand { get; }
        public IRelayCommand SaveCommand { get; }
        public IRelayCommand CancelCommand { get; }

        public event EventHandler<bool>? RequestClose;

        partial void OnSelectedProfileChanged(ConnectionProfile? value)
        {
            if (SelectedProfile != null)
            {
                SelectedProfile.PropertyChanged += SelectedProfile_PropertyChanged;
            }

            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(IsSerial));
            OnPropertyChanged(nameof(CanRemove));
            OnPropertyChanged(nameof(CanClone));
            OnPropertyChanged(nameof(CanConnect));
            OnPropertyChanged(nameof(CanDisconnect));
            RefreshSerialPorts();
        }

        partial void OnSelectedProfileChanging(ConnectionProfile? value)
        {
            if (SelectedProfile != null)
            {
                SelectedProfile.PropertyChanged -= SelectedProfile_PropertyChanged;
            }
        }

        private bool CanConnect() => HasSelection && !IsConnecting && SelectedProfile is { IsConnected: false };

        private bool CanDisconnect() => HasSelection && !IsConnecting && SelectedProfile is { IsConnected: true };

        private void SelectedProfile_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ConnectionProfile.IsConnected))
            {
                OnPropertyChanged(nameof(CanConnect));
                OnPropertyChanged(nameof(CanDisconnect));
            }
            else if (e.PropertyName == nameof(ConnectionProfile.Transport))
            {
                OnPropertyChanged(nameof(IsSerial));
            }
        }

        private void Profiles_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(CanRemove));
        }

        private void ConnectionManager_ActiveProfileChanged(object? sender, ConnectionProfile? e)
        {
            _dispatcher.Invoke(() => SelectedProfile = e);
        }

        private void RefreshSerialPorts()
        {
            AvailableSerialPorts.Clear();
            foreach (var port in SerialPort.GetPortNames())
            {
                AvailableSerialPorts.Add(port);
            }
        }

        private void AddProfile()
        {
            var newProfile = new ConnectionProfile($"Connection {Profiles.Count + 1}", "127.0.0.1", 502, 1);
            _connectionManager.AddProfile(newProfile);
            _connectionManager.SetActiveProfile(newProfile);
            SelectedProfile = newProfile;
        }

        private void RemoveProfile()
        {
            if (SelectedProfile == null || Profiles.Count <= 1)
            {
                return;
            }

            var toRemove = SelectedProfile;
            var index = Profiles.IndexOf(toRemove);
            var nextIndex = index == 0 && Profiles.Count > 1 ? 1 : 0;
            var next = Profiles[nextIndex] != toRemove ? Profiles[nextIndex] : null;
            _connectionManager.RemoveProfile(toRemove);
            SelectedProfile = next ?? Profiles.FirstOrDefault();
        }

        private void CloneProfile()
        {
            if (SelectedProfile == null) return;

            var cloned = SelectedProfile.Clone();
            _connectionManager.AddProfile(cloned);
            SelectedProfile = cloned;
        }

        private async Task ConnectAsync()
        {
            if (SelectedProfile == null || IsConnecting) return;

            IsConnecting = true;
            StatusMessage = $"Connecting to {SelectedProfile.DisplayName}...";

            try
            {
                var success = await _connectionManager.ConnectProfileAsync(SelectedProfile);
                StatusMessage = success ? "Connected." : "Connection failed.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Connection error: {ex.Message}";
            }
            finally
            {
                IsConnecting = false;
            }
        }

        private async Task DisconnectAsync()
        {
            if (SelectedProfile == null || IsConnecting) return;

            IsConnecting = true;
            StatusMessage = $"Disconnecting from {SelectedProfile.DisplayName}...";

            try
            {
                await _connectionManager.DisconnectProfileAsync(SelectedProfile);
                StatusMessage = "Disconnected.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Disconnect error: {ex.Message}";
            }
            finally
            {
                IsConnecting = false;
            }
        }

        private void SaveAndClose()
        {
            _connectionManager.SaveProfiles();
            RequestClose?.Invoke(this, true);
        }

        private void Cancel()
        {
            RequestClose?.Invoke(this, false);
        }

        public void Dispose()
        {
            _connectionManager.Profiles.CollectionChanged -= Profiles_CollectionChanged;
            _connectionManager.ActiveProfileChanged -= ConnectionManager_ActiveProfileChanged;

            if (SelectedProfile != null)
            {
                SelectedProfile.PropertyChanged -= SelectedProfile_PropertyChanged;
            }
        }
    }
}
