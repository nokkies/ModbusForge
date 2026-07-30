using System;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModbusForge.Models;
using ModbusForge.Services;

namespace ModbusForge.Avalonia.ViewModels
{
    public partial class PreferencesViewModel : ObservableObject
    {
        private readonly ISettingsService _settingsService;
        private readonly IMessageBoxService? _messageBoxService;

        [ObservableProperty]
        private bool _autoReconnect;

        [ObservableProperty]
        private int _autoReconnectIntervalMs;

        [ObservableProperty]
        private bool _showConnectionDiagnosticsOnError;

        [ObservableProperty]
        private bool _enableConsoleLogging;

        [ObservableProperty]
        private int _maxConsoleMessages;

        [ObservableProperty]
        private int _maxConcurrentTrendRequests;

        [ObservableProperty]
        private bool _confirmOnExit;

        [ObservableProperty]
        private bool _enableApi;

        [ObservableProperty]
        private int _apiPort;

        [ObservableProperty]
        private bool _enableApiDocumentation;

        [ObservableProperty]
        private bool _enableApiAuthentication;

        [ObservableProperty]
        private string _apiKey = string.Empty;

        [ObservableProperty]
        private bool _checkForUpdatesOnStartup;

        public MqttSettings MqttSettings { get; } = new MqttSettings();

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand RegenerateApiKeyCommand { get; }

        public event EventHandler<bool>? RequestClose;

        public PreferencesViewModel(ISettingsService settingsService, IMessageBoxService? messageBoxService = null)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _messageBoxService = messageBoxService;

            LoadFromService();

            SaveCommand = new AsyncRelayCommand(SaveAsync);
            CancelCommand = new RelayCommand(Cancel);
            RegenerateApiKeyCommand = new RelayCommand(RegenerateApiKey);
        }

        private void LoadFromService()
        {
            AutoReconnect = _settingsService.AutoReconnect;
            AutoReconnectIntervalMs = Math.Max(100, _settingsService.AutoReconnectIntervalMs);
            ShowConnectionDiagnosticsOnError = _settingsService.ShowConnectionDiagnosticsOnError;
            EnableConsoleLogging = _settingsService.EnableConsoleLogging;
            MaxConsoleMessages = Math.Max(1, _settingsService.MaxConsoleMessages);
            MaxConcurrentTrendRequests = Math.Max(1, _settingsService.MaxConcurrentTrendRequests);
            ConfirmOnExit = _settingsService.ConfirmOnExit;
            EnableApi = _settingsService.EnableApi;
            ApiPort = Math.Max(1, Math.Min(_settingsService.ApiPort, 65535));
            EnableApiDocumentation = _settingsService.EnableApiDocumentation;
            EnableApiAuthentication = _settingsService.EnableApiAuthentication;
            ApiKey = _settingsService.ApiKey;
            CheckForUpdatesOnStartup = _settingsService.CheckForUpdatesOnStartup;

            var mqtt = _settingsService.MqttSettings;
            MqttSettings.Enabled = mqtt.Enabled;
            MqttSettings.BrokerHost = mqtt.BrokerHost;
            MqttSettings.BrokerPort = mqtt.BrokerPort;
            MqttSettings.ClientId = mqtt.ClientId;
            MqttSettings.Username = mqtt.Username;
            MqttSettings.Password = mqtt.Password;
            MqttSettings.TopicTemplate = mqtt.TopicTemplate;
            MqttSettings.QualityOfService = mqtt.QualityOfService;
            MqttSettings.RetainMessages = mqtt.RetainMessages;
            MqttSettings.PublishPeriodMs = Math.Max(0, mqtt.PublishPeriodMs);
        }

        private async Task SaveAsync()
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
            _settingsService.CheckForUpdatesOnStartup = CheckForUpdatesOnStartup;
            _settingsService.MqttSettings = MqttSettings;

            if (_settingsService.Save())
            {
                RequestClose?.Invoke(this, true);
            }
            else
            {
                if (_messageBoxService != null)
                {
                    await _messageBoxService.ShowAsync("Failed to save settings. Please check your permissions or disk space.", "Error", DialogButton.Ok, DialogIcon.Error);
                }
            }
        }

        private void Cancel()
        {
            RequestClose?.Invoke(this, false);
        }

        private void RegenerateApiKey()
        {
            ApiKey = Guid.NewGuid().ToString("N");
        }
    }
}
