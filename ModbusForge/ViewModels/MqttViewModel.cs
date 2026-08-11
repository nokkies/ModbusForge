using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModbusForge.Models;
using ModbusForge.Services;

namespace ModbusForge.Avalonia.ViewModels
{
    public partial class MqttViewModel : ObservableObject
    {
        private readonly ISettingsService _settingsService;
        private readonly MqttGatewayService _mqttGateway;

        [ObservableProperty]
        private MqttSettings _settings;

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        public bool IsConnected => _mqttGateway.IsConnected;

        public ICommand ApplyAndConnectCommand { get; }
        public ICommand DisconnectCommand { get; }

        public MqttViewModel(ISettingsService settingsService, MqttGatewayService mqttGateway)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _mqttGateway = mqttGateway ?? throw new ArgumentNullException(nameof(mqttGateway));

            _settings = _settingsService.MqttSettings;

            ApplyAndConnectCommand = new AsyncRelayCommand(ApplyAndConnectAsync);
            DisconnectCommand = new AsyncRelayCommand(DisconnectAsync);
        }

        private async Task ApplyAndConnectAsync()
        {
            _mqttGateway.ApplySettings(Settings);
            _settingsService.MqttSettings = Settings;
            _settingsService.Save();

            try
            {
                await _mqttGateway.ConnectAsync(CancellationToken.None);
                StatusMessage = _mqttGateway.IsConnected
                    ? $"Connected to MQTT broker {Settings.BrokerHost}:{Settings.BrokerPort}."
                    : "MQTT broker connection failed.";
                OnPropertyChanged(nameof(IsConnected));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                StatusMessage = $"MQTT connect error: {ex.Message}";
            }
        }

        private async Task DisconnectAsync()
        {
            await _mqttGateway.DisconnectAsync();
            StatusMessage = "MQTT disconnected.";
            OnPropertyChanged(nameof(IsConnected));
        }
    }
}
