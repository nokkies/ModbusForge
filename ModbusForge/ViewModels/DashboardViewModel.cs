using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModbusForge.Models;
using ModbusForge.Services;

namespace ModbusForge.ViewModels
{
    public partial class DashboardViewModel : ObservableObject, IDisposable
    {
        private readonly IConnectionManager _connectionManager;
        private readonly IModbusService _modbusService;
        private readonly IShellWindowService _shellWindowService;
        private readonly IDispatcher _dispatcher;

        private ConnectionProfile? _selectedProfile;
        private bool _isBusy;
        private string _statusMessage = "No connection";

        public DashboardViewModel(
            IConnectionManager connectionManager,
            IModbusService modbusService,
            IShellWindowService shellWindowService,
            IDispatcher dispatcher)
        {
            ArgumentNullException.ThrowIfNull(connectionManager);
            ArgumentNullException.ThrowIfNull(modbusService);
            ArgumentNullException.ThrowIfNull(shellWindowService);
            ArgumentNullException.ThrowIfNull(dispatcher);

            _connectionManager = connectionManager;
            _modbusService = modbusService;
            _shellWindowService = shellWindowService;
            _dispatcher = dispatcher;

            ConnectCommand = new AsyncRelayCommand(ConnectAsync, () => CanConnect);
            DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, () => CanDisconnect);
            ReadRegistersCommand = new AsyncRelayCommand(ReadRegistersAsync, () => CanRead);
            OpenScriptEditorCommand = new RelayCommand(OpenScriptEditor, () => CanOpenScriptEditor);
            OpenConnectionManagerCommand = new RelayCommand(OpenConnectionManager);

            _connectionManager.ActiveProfileChanged += OnActiveProfileChanged;
            _connectionManager.ProfileConnected += OnProfileConnected;
            _connectionManager.ProfileDisconnected += OnProfileDisconnected;
            _connectionManager.Profiles.CollectionChanged += OnProfilesCollectionChanged;

            SelectedProfile = _connectionManager.ActiveProfile;
            RefreshStatus();
        }

        public ObservableCollection<ConnectionProfile> RecentProfiles => _connectionManager.Profiles;

        public ConnectionProfile? SelectedProfile
        {
            get => _selectedProfile;
            set
            {
                if (SetProperty(ref _selectedProfile, value))
                {
                    OnPropertyChanged(nameof(CanConnect));
                    ConnectCommand.NotifyCanExecuteChanged();
                    OnPropertyChanged(nameof(ActiveProfileName));
                }
            }
        }

        public bool IsConnected => GetActiveService()?.IsConnected ?? false;

        private IModbusService? GetActiveService() => _connectionManager.ActiveService ?? _modbusService;

        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        public string ActiveProfileName => _connectionManager.ActiveProfile?.Name ?? SelectedProfile?.Name ?? "None";

        public bool CanConnect => !IsConnected && (SelectedProfile ?? _connectionManager.ActiveProfile) != null;

        public bool CanDisconnect => IsConnected;

        public bool CanRead => IsConnected;

        public bool CanOpenScriptEditor => _connectionManager.ActiveProfile != null || SelectedProfile != null;

        public bool HasRecentProfiles => _connectionManager.Profiles.Count > 0;

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    OnPropertyChanged(nameof(CanConnect));
                    ConnectCommand.NotifyCanExecuteChanged();
                }
            }
        }

        public IAsyncRelayCommand ConnectCommand { get; }
        public IAsyncRelayCommand DisconnectCommand { get; }
        public IAsyncRelayCommand ReadRegistersCommand { get; }
        public IRelayCommand OpenScriptEditorCommand { get; }
        public IRelayCommand OpenConnectionManagerCommand { get; }

        private async Task ConnectAsync()
        {
            var profile = SelectedProfile ?? _connectionManager.ActiveProfile;
            if (profile == null || IsConnected) return;

            _connectionManager.SetActiveProfile(profile);

            IsBusy = true;
            try
            {
                var success = await _connectionManager.ConnectProfileAsync(profile);
                StatusMessage = success ? $"Connected to {profile.Name}" : $"Failed to connect to {profile.Name}";
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                StatusMessage = $"Connection error: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task DisconnectAsync()
        {
            if (!IsConnected) return;

            IsBusy = true;
            try
            {
                var profile = _connectionManager.ActiveProfile ?? SelectedProfile;
                if (profile != null)
                {
                    await _connectionManager.DisconnectProfileAsync(profile);
                }
                else
                {
                    await _connectionManager.DisconnectAllAsync();
                }
                StatusMessage = "Disconnected";
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                StatusMessage = $"Disconnect error: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ReadRegistersAsync()
        {
            var service = GetActiveService();
            if (service?.IsConnected != true) return;

            var profile = _connectionManager.ActiveProfile ?? SelectedProfile;
            var unitId = profile?.UnitId ?? 1;

            IsBusy = true;
            try
            {
                var result = await service.ReadHoldingRegistersAsync(unitId, 1, 10);
                StatusMessage = result != null ? $"Read {result.Length} registers" : "Read failed";
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                StatusMessage = $"Read error: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void OpenScriptEditor()
        {
            var profile = _connectionManager.ActiveProfile ?? SelectedProfile;
            var unitId = profile?.UnitId ?? 1;
            _shellWindowService.ShowScriptEditor(Application.Current.MainWindow, unitId);
        }

        private void OpenConnectionManager()
        {
            _shellWindowService.ShowConnectionManager(Application.Current.MainWindow);
        }

        private void OnActiveProfileChanged(object? sender, ConnectionProfile? e)
        {
            _dispatcher.Invoke(() =>
            {
                SelectedProfile = e;
                RefreshStatus();
            });
        }

        private void OnProfileConnected(object? sender, ConnectionProfile e)
        {
            _dispatcher.Invoke(RefreshStatus);
        }

        private void OnProfileDisconnected(object? sender, ConnectionProfile e)
        {
            _dispatcher.Invoke(RefreshStatus);
        }

        private void OnProfilesCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            _dispatcher.Invoke(() =>
            {
                OnPropertyChanged(nameof(HasRecentProfiles));
                OnPropertyChanged(nameof(ActiveProfileName));
                OnPropertyChanged(nameof(CanConnect));
                ConnectCommand.NotifyCanExecuteChanged();
            });
        }

        private void OnSelectedProfilePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ConnectionProfile.IsConnected) ||
                e.PropertyName == nameof(ConnectionProfile.Name))
            {
                _dispatcher.Invoke(RefreshStatus);
            }
        }

        private void RefreshStatus()
        {
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(CanConnect));
            OnPropertyChanged(nameof(CanDisconnect));
            OnPropertyChanged(nameof(CanRead));
            OnPropertyChanged(nameof(CanOpenScriptEditor));
            OnPropertyChanged(nameof(ActiveProfileName));
            OnPropertyChanged(nameof(HasRecentProfiles));

            ConnectCommand.NotifyCanExecuteChanged();
            DisconnectCommand.NotifyCanExecuteChanged();
            ReadRegistersCommand.NotifyCanExecuteChanged();
            OpenScriptEditorCommand.NotifyCanExecuteChanged();

            if (IsConnected)
            {
                var profile = _connectionManager.ActiveProfile;
                StatusMessage = profile != null ? $"Connected to {profile.Name}" : "Connected";
            }
            else
            {
                StatusMessage = _connectionManager.Profiles.Count > 0
                    ? "Select a profile and connect"
                    : "No connection profiles. Open Connection Manager to add one.";
            }

            UpdateActiveProfileSubscription();
        }

        private ConnectionProfile? _subscribedProfile;
        private void UpdateActiveProfileSubscription()
        {
            var profile = _connectionManager.ActiveProfile;
            if (_subscribedProfile == profile) return;

            if (_subscribedProfile != null)
            {
                _subscribedProfile.PropertyChanged -= OnSelectedProfilePropertyChanged;
            }

            _subscribedProfile = profile;
            if (_subscribedProfile != null)
            {
                _subscribedProfile.PropertyChanged += OnSelectedProfilePropertyChanged;
            }
        }

        public void Dispose()
        {
            _connectionManager.ActiveProfileChanged -= OnActiveProfileChanged;
            _connectionManager.ProfileConnected -= OnProfileConnected;
            _connectionManager.ProfileDisconnected -= OnProfileDisconnected;
            _connectionManager.Profiles.CollectionChanged -= OnProfilesCollectionChanged;

            if (_subscribedProfile != null)
            {
                _subscribedProfile.PropertyChanged -= OnSelectedProfilePropertyChanged;
            }
        }
    }
}
