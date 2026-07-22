using System;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using ModbusForge.Services;

namespace ModbusForge.ViewModels
{
    public class PreferencesViewModel : ViewModelBase
    {
        private readonly ISettingsService _settingsService;
        private readonly IDialogService _dialogService;

        private bool _autoReconnect;
        private int _autoReconnectIntervalMs;
        private bool _showConnectionDiagnosticsOnError;
        private bool _enableConsoleLogging;
        private int _maxConsoleMessages;
        private int _maxConcurrentTrendRequests;
        private bool _confirmOnExit;
        private bool _enableApi;
        private int _apiPort;
        private bool _enableApiDocumentation;
        private bool _enableApiAuthentication;
        private string _apiKey = string.Empty;

        public PreferencesViewModel(ISettingsService settingsService, IDialogService? dialogService = null)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _dialogService = dialogService ?? new NullDialogService();

            LoadFromService();

            SaveCommand = new RelayCommand(Save);
            CancelCommand = new RelayCommand(Cancel);
        }

        public bool AutoReconnect
        {
            get => _autoReconnect;
            set => SetProperty(ref _autoReconnect, value);
        }

        public int AutoReconnectIntervalMs
        {
            get => _autoReconnectIntervalMs;
            set => SetProperty(ref _autoReconnectIntervalMs, value);
        }

        public bool ShowConnectionDiagnosticsOnError
        {
            get => _showConnectionDiagnosticsOnError;
            set => SetProperty(ref _showConnectionDiagnosticsOnError, value);
        }

        public bool EnableConsoleLogging
        {
            get => _enableConsoleLogging;
            set => SetProperty(ref _enableConsoleLogging, value);
        }

        public int MaxConsoleMessages
        {
            get => _maxConsoleMessages;
            set => SetProperty(ref _maxConsoleMessages, value);
        }

        public int MaxConcurrentTrendRequests
        {
            get => _maxConcurrentTrendRequests;
            set => SetProperty(ref _maxConcurrentTrendRequests, value);
        }

        public bool ConfirmOnExit
        {
            get => _confirmOnExit;
            set => SetProperty(ref _confirmOnExit, value);
        }

        public bool EnableApi
        {
            get => _enableApi;
            set => SetProperty(ref _enableApi, value);
        }

        public int ApiPort
        {
            get => _apiPort;
            set => SetProperty(ref _apiPort, value);
        }

        public bool EnableApiDocumentation
        {
            get => _enableApiDocumentation;
            set => SetProperty(ref _enableApiDocumentation, value);
        }

        public bool EnableApiAuthentication
        {
            get => _enableApiAuthentication;
            set => SetProperty(ref _enableApiAuthentication, value);
        }

        public string ApiKey
        {
            get => _apiKey;
            set => SetProperty(ref _apiKey, value);
        }

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        public event EventHandler<bool>? RequestClose;

        private void LoadFromService()
        {
            _autoReconnect = _settingsService.AutoReconnect;
            _autoReconnectIntervalMs = Math.Max(100, _settingsService.AutoReconnectIntervalMs);
            _showConnectionDiagnosticsOnError = _settingsService.ShowConnectionDiagnosticsOnError;
            _enableConsoleLogging = _settingsService.EnableConsoleLogging;
            _maxConsoleMessages = Math.Max(1, _settingsService.MaxConsoleMessages);
            _maxConcurrentTrendRequests = Math.Max(1, _settingsService.MaxConcurrentTrendRequests);
            _confirmOnExit = _settingsService.ConfirmOnExit;
            _enableApi = _settingsService.EnableApi;
            _apiPort = Math.Max(1, Math.Min(_settingsService.ApiPort, 65535));
            _enableApiDocumentation = _settingsService.EnableApiDocumentation;
            _enableApiAuthentication = _settingsService.EnableApiAuthentication;
            _apiKey = _settingsService.ApiKey;
        }

        private void Save()
        {
            _settingsService.AutoReconnect = AutoReconnect;
            _settingsService.AutoReconnectIntervalMs = Math.Max(100, AutoReconnectIntervalMs);
            _settingsService.ShowConnectionDiagnosticsOnError = ShowConnectionDiagnosticsOnError;
            _settingsService.EnableConsoleLogging = EnableConsoleLogging;
            _settingsService.MaxConsoleMessages = Math.Max(1, MaxConsoleMessages);
            _settingsService.MaxConcurrentTrendRequests = Math.Max(1, MaxConcurrentTrendRequests);
            _settingsService.ConfirmOnExit = ConfirmOnExit;
            _settingsService.EnableApi = EnableApi;
            _settingsService.ApiPort = Math.Max(1, Math.Min(ApiPort, 65535));
            _settingsService.EnableApiDocumentation = EnableApiDocumentation;
            _settingsService.EnableApiAuthentication = EnableApiAuthentication;
            _settingsService.ApiKey = ApiKey?.Trim() ?? string.Empty;

            if (_settingsService.Save())
            {
                RequestClose?.Invoke(this, true);
            }
            else
            {
                _dialogService.Show(
                    "Failed to save settings. Please check your permissions or disk space.",
                    "Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        private void Cancel()
        {
            RequestClose?.Invoke(this, false);
        }

        public override void Dispose()
        {
            // Nothing to dispose
            base.Dispose();
        }
    }
}
