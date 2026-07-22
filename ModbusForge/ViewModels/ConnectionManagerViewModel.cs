using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using ModbusForge.Models;
using ModbusForge.Services;

namespace ModbusForge.ViewModels
{
    public class ConnectionManagerViewModel : ViewModelBase
    {
        private readonly IConnectionManager _connectionManager;
        private readonly IDialogService _dialogService;

        private ConnectionProfile? _selectedProfile;
        private bool _isConnecting;

        public ConnectionManagerViewModel(IConnectionManager connectionManager, IDialogService? dialogService = null)
        {
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            _dialogService = dialogService ?? new NullDialogService();

            AddCommand = new RelayCommand(AddProfile);
            RemoveCommand = new RelayCommand(RemoveProfile);
            CloneCommand = new RelayCommand(CloneProfile);
            SetActiveCommand = new RelayCommand(SetActiveProfile);
            ConnectCommand = new AsyncRelayCommand(ConnectAsync);
            DisconnectCommand = new AsyncRelayCommand(DisconnectAsync);
            SaveAndCloseCommand = new RelayCommand(SaveAndClose);
            CancelCommand = new RelayCommand(Cancel);

            if (_connectionManager.Profiles.Count > 0)
            {
                SelectedProfile = _connectionManager.ActiveProfile ?? _connectionManager.Profiles[0];
            }

            _connectionManager.Profiles.CollectionChanged += Profiles_CollectionChanged;
        }

        public ObservableCollection<ConnectionProfile> Profiles => _connectionManager.Profiles;

        public ConnectionProfile? SelectedProfile
        {
            get => _selectedProfile;
            set
            {
                if (_selectedProfile != value)
                {
                    if (_selectedProfile != null)
                    {
                        _selectedProfile.PropertyChanged -= SelectedProfile_PropertyChanged;
                    }

                    _selectedProfile = value;

                    if (_selectedProfile != null)
                    {
                        _selectedProfile.PropertyChanged += SelectedProfile_PropertyChanged;
                    }

                    OnPropertyChanged(nameof(SelectedProfile));
                    OnPropertyChanged(nameof(HasSelection));
                    OnPropertyChanged(nameof(CanConnect));
                    OnPropertyChanged(nameof(CanDisconnect));
                    OnPropertyChanged(nameof(CanSetActive));
                    OnPropertyChanged(nameof(CanRemove));
                    OnPropertyChanged(nameof(CanClone));
                }
            }
        }

        public bool HasSelection => SelectedProfile != null;

        public bool IsConnecting
        {
            get => _isConnecting;
            private set
            {
                if (SetProperty(ref _isConnecting, value))
                {
                    OnPropertyChanged(nameof(CanConnect));
                    OnPropertyChanged(nameof(CanDisconnect));
                }
            }
        }

        public bool CanConnect => HasSelection && !IsConnecting && SelectedProfile is { IsConnected: false };
        public bool CanDisconnect => HasSelection && !IsConnecting && SelectedProfile is { IsConnected: true };
        public bool CanSetActive => HasSelection;
        public bool CanRemove => HasSelection && Profiles.Count > 1;
        public bool CanClone => HasSelection;

        public ICommand AddCommand { get; }
        public ICommand RemoveCommand { get; }
        public ICommand CloneCommand { get; }
        public ICommand SetActiveCommand { get; }
        public IAsyncRelayCommand ConnectCommand { get; }
        public IAsyncRelayCommand DisconnectCommand { get; }
        public ICommand SaveAndCloseCommand { get; }
        public ICommand CancelCommand { get; }

        public event EventHandler<bool>? RequestClose;

        private void SelectedProfile_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ConnectionProfile.IsConnected))
            {
                OnPropertyChanged(nameof(CanConnect));
                OnPropertyChanged(nameof(CanDisconnect));
            }
        }

        private void Profiles_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(CanRemove));
        }

        private void AddProfile()
        {
            var newProfile = new ConnectionProfile($"Connection {Profiles.Count + 1}", "127.0.0.1", 502, 1);
            _connectionManager.AddProfile(newProfile);
            SelectedProfile = newProfile;
        }

        private void RemoveProfile()
        {
            if (SelectedProfile == null) return;

            if (Profiles.Count <= 1)
            {
                _dialogService.Show("Cannot remove the last connection profile.", "Remove Profile",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            var result = _dialogService.Show($"Remove connection '{SelectedProfile.Name}'?", "Confirm Remove",
                System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                var toRemove = SelectedProfile;
                var index = Profiles.IndexOf(toRemove);
                var nextIndex = index == 0 && Profiles.Count > 1 ? 1 : 0;
                SelectedProfile = Profiles[nextIndex] != toRemove ? Profiles[nextIndex] : null;
                _connectionManager.RemoveProfile(toRemove);
                if (SelectedProfile == null && Profiles.Count > 0)
                {
                    SelectedProfile = Profiles[0];
                }
            }
        }

        private void CloneProfile()
        {
            if (SelectedProfile == null) return;

            var cloned = SelectedProfile.Clone();
            _connectionManager.AddProfile(cloned);
            SelectedProfile = cloned;
        }

        private void SetActiveProfile()
        {
            if (SelectedProfile == null) return;

            _connectionManager.SetActiveProfile(SelectedProfile);
            _dialogService.Show($"'{SelectedProfile.Name}' is now the active connection.", "Active Connection",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }

        private async Task ConnectAsync()
        {
            if (SelectedProfile == null || IsConnecting) return;

            IsConnecting = true;
            try
            {
                var success = await _connectionManager.ConnectProfileAsync(SelectedProfile);
                if (!success)
                {
                    _dialogService.Show($"Failed to connect to {SelectedProfile.IpAddress}:{SelectedProfile.Port}",
                        "Connection Failed", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _dialogService.Show($"Connection failed: {ex.Message}", "Connection Error",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
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
            try
            {
                await _connectionManager.DisconnectProfileAsync(SelectedProfile);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _dialogService.Show($"Disconnect failed: {ex.Message}", "Disconnect Error",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
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

        public override void Dispose()
        {
            _connectionManager.Profiles.CollectionChanged -= Profiles_CollectionChanged;

            if (_selectedProfile != null)
            {
                _selectedProfile.PropertyChanged -= SelectedProfile_PropertyChanged;
            }

            base.Dispose();
        }
    }
}
