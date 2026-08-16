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
        private readonly IDispatcher? _dispatcher;

        [ObservableProperty]
        private MqttSettings _settings;

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        public bool IsConnected => _mqttGateway.IsConnected;

        public bool IsRunning => _mqttGateway.IsRunning;

        /// <summary>
        /// True while the gateway is active but the broker is unreachable - the
        /// reconnect loop keeps trying, and the UI should say so (e.g. after an
        /// automatic startup resume while the broker is still down).
        /// </summary>
        public bool IsRetrying => _mqttGateway.IsRunning && !_mqttGateway.IsConnected;

        public ICommand ApplyAndConnectCommand { get; }
        public ICommand DisconnectCommand { get; }

        public MqttViewModel(
            ISettingsService settingsService,
            MqttGatewayService mqttGateway,
            IDispatcher? dispatcher = null)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _mqttGateway = mqttGateway ?? throw new ArgumentNullException(nameof(mqttGateway));
            _dispatcher = dispatcher;

            _settings = _settingsService.MqttSettings;

            ApplyAndConnectCommand = new AsyncRelayCommand(ApplyAndConnectAsync);
            DisconnectCommand = new AsyncRelayCommand(DisconnectAsync);

            _mqttGateway.ConnectionStateChanged += OnConnectionStateChanged;
            RaiseConnectionState();
        }

        private async Task ApplyAndConnectAsync()
        {
            _mqttGateway.ApplySettings(Settings);
            _settingsService.MqttSettings = Settings;
            _settingsService.Save();

            try
            {
                // A running gateway keeps the broker, client id and publish period it
                // was started with, so "Apply" must restart it for the new settings
                // to take effect (and unchecking Enabled stops the gateway).
                if (_mqttGateway.IsRunning)
                {
                    await _mqttGateway.DisconnectAsync();
                }

                await _mqttGateway.ConnectAsync(CancellationToken.None);
                StatusMessage = _mqttGateway.IsConnected
                    ? $"Connected to MQTT broker {Settings.BrokerHost}:{Settings.BrokerPort}."
                    : _mqttGateway.IsRunning
                        ? $"Not connected - retrying every few seconds ({Settings.BrokerHost}:{Settings.BrokerPort})."
                        : "MQTT gateway is disabled.";
                RaiseConnectionState();
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
            RaiseConnectionState();
        }

        /// <summary>
        /// The gateway raises its state changes from worker threads; marshal them to
        /// the UI thread so the "Connected" indicator updates while the broker drops
        /// or the reconnect loop recovers.
        /// </summary>
        private void OnConnectionStateChanged(object? sender, EventArgs e)
        {
            if (_dispatcher is null || _dispatcher.CheckAccess)
            {
                RaiseConnectionState();
                return;
            }

            _dispatcher.Post(RaiseConnectionState);
        }

        private void RaiseConnectionState()
        {
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(IsRetrying));
        }
    }
}
