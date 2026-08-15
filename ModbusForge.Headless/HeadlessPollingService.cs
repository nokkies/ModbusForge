using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModbusForge.Models;
using ModbusForge.Services;

namespace ModbusForge.Headless
{
    /// <summary>
    /// Background service that connects to a Modbus device and continuously polls a configured area.
    /// Values can be logged to stdout/file and/or forwarded to an MQTT broker.
    /// </summary>
    public sealed class HeadlessPollingService : BackgroundService
    {
        /// <summary>Consecutive failed reads before a reconnect is attempted.</summary>
        private const int MaxConsecutiveFailuresBeforeReconnect = 3;

        /// <summary>Default delay before a reconnect attempt, giving transient network issues time to clear.</summary>
        private const int DefaultReconnectBackoffMs = 5000;

        private readonly IModbusService _modbusService;
        private readonly MqttGatewayService? _mqttService;
        private readonly IHostApplicationLifetime _lifetime;
        private readonly ILogger<HeadlessPollingService> _logger;
        private readonly ConnectionProfile _profile;
        private readonly int _startAddress;
        private readonly int _count;
        private readonly int _intervalMs;
        private readonly PlcArea _area;
        private readonly MqttSettings _mqttSettings;
        private readonly int _reconnectBackoffMs;
        private int _consecutiveFailures;

        public HeadlessPollingService(
            IModbusService modbusService,
            MqttGatewayService? mqttService,
            IHostApplicationLifetime lifetime,
            IConfiguration configuration,
            ILogger<HeadlessPollingService> logger)
        {
            _modbusService = modbusService ?? throw new ArgumentNullException(nameof(modbusService));
            _mqttService = mqttService;
            _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _profile = HeadlessProfileFactory.CreateConnectionProfile(configuration);

            _startAddress = configuration.GetValue<int?>("Polling:StartAddress") ?? 0;
            _count = configuration.GetValue<int?>("Polling:Count") ?? 10;
            _intervalMs = configuration.GetValue<int?>("Polling:IntervalMs") ?? 1000;
            _area = Enum.TryParse<PlcArea>(configuration["Polling:Area"], true, out var a) ? a : PlcArea.HoldingRegister;
            _reconnectBackoffMs = configuration.GetValue<int?>("Polling:ReconnectBackoffMs") ?? DefaultReconnectBackoffMs;

            _mqttSettings = HeadlessProfileFactory.CreateMqttSettings(configuration);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (_profile.Transport == TransportType.Tcp)
            {
                _logger.LogInformation("Connecting to {Host}:{Port} (unit {UnitId}) via {Transport}...",
                    _profile.IpAddress, _profile.Port, _profile.UnitId, _profile.Transport);
            }
            else
            {
                _logger.LogInformation("Connecting to {ComPort} @ {BaudRate} (unit {UnitId}) via {Transport}...",
                    _profile.ComPort, _profile.BaudRate, _profile.UnitId, _profile.Transport);
            }

            var connected = await _modbusService.ConnectAsync(_profile, stoppingToken);
            if (!connected)
            {
                _logger.LogError("Failed to connect to the Modbus device.");
                _lifetime.StopApplication();
                return;
            }

            _mqttService?.ApplySettings(_mqttSettings);
            await (_mqttService?.ConnectAsync(stoppingToken) ?? Task.CompletedTask);

            _logger.LogInformation("Connected. Polling {Area} every {IntervalMs}ms.", _area, _intervalMs);

            while (!stoppingToken.IsCancellationRequested)
            {
                bool pollSucceeded;
                try
                {
                    pollSucceeded = await PollOnceAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Poll failed");
                    pollSucceeded = false;
                }

                if (pollSucceeded)
                {
                    _consecutiveFailures = 0;
                }
                else if (!_modbusService.IsConnected || _consecutiveFailures >= MaxConsecutiveFailuresBeforeReconnect)
                {
                    _consecutiveFailures++;
                    await TryReconnectAsync(stoppingToken);
                    _consecutiveFailures = 0;
                }
                else
                {
                    _consecutiveFailures++;
                }

                try
                {
                    await Task.Delay(_intervalMs, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            await StopServicesAsync();
            _lifetime.StopApplication();
        }

        /// <summary>Test seam: runs the service loop directly (visible to ModbusForge.Headless.Tests).</summary>
        internal Task ExecuteForTest(CancellationToken token) => ExecuteAsync(token);

        private async Task TryReconnectAsync(CancellationToken token)
        {
            _logger.LogWarning(
                "Connection lost or {Failures} consecutive read failure(s) - attempting reconnect in {BackoffMs} ms",
                _consecutiveFailures, _reconnectBackoffMs);

            try
            {
                await _modbusService.DisconnectAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error while disconnecting before reconnect");
            }

            await Task.Delay(_reconnectBackoffMs, token);

            var connected = await _modbusService.ConnectAsync(_profile, token);
            if (connected)
            {
                _logger.LogInformation("Reconnected to the Modbus device.");
            }
            else
            {
                _logger.LogError("Reconnect failed - will keep retrying on subsequent poll cycles.");
            }
        }

        private async Task StopServicesAsync()
        {
            if (_mqttService is not null)
            {
                try
                {
                    await _mqttService.DisconnectAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error while disconnecting MQTT");
                }
            }

            try
            {
                await _modbusService.DisconnectAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error while disconnecting Modbus");
            }
        }

        private async Task<bool> PollOnceAsync(CancellationToken token)
        {
            if (_area == PlcArea.HoldingRegister || _area == PlcArea.InputRegister)
            {
                var values = _area == PlcArea.HoldingRegister
                    ? await _modbusService.ReadHoldingRegistersAsync(_profile.UnitId, _startAddress, _count)
                    : await _modbusService.ReadInputRegistersAsync(_profile.UnitId, _startAddress, _count);

                if (values is null)
                {
                    _logger.LogWarning("No response for {Area} [{StartAddress}..{EndAddress}]", _area, _startAddress, _startAddress + _count - 1);
                    return false;
                }

                _logger.LogInformation("{Area} [{StartAddress}..{EndAddress}]: {Values}",
                    _area, _startAddress, _startAddress + values.Length - 1, string.Join(", ", values));

                await PublishAsync(values, null, token);
                return true;
            }
            else
            {
                var states = _area == PlcArea.Coil
                    ? await _modbusService.ReadCoilsAsync(_profile.UnitId, _startAddress, _count)
                    : await _modbusService.ReadDiscreteInputsAsync(_profile.UnitId, _startAddress, _count);

                if (states is null)
                {
                    _logger.LogWarning("No response for {Area} [{StartAddress}..{EndAddress}]", _area, _startAddress, _startAddress + _count - 1);
                    return false;
                }

                _logger.LogInformation("{Area} [{StartAddress}..{EndAddress}]: {Values}",
                    _area, _startAddress, _startAddress + states.Length - 1, string.Join(", ", states));

                await PublishAsync(null, states, token);
                return true;
            }
        }

        private async Task PublishAsync(ushort[]? values, bool[]? states, CancellationToken token)
        {
            if (_mqttService is null) return;

            var updates = new List<MqttTagUpdate>();

            if (values is not null)
            {
                for (int i = 0; i < values.Length; i++)
                {
                    updates.Add(new MqttTagUpdate
                    {
                        UnitId = _profile.UnitId,
                        TagName = $"{_area}_{_startAddress + i}",
                        Area = _area,
                        Address = _startAddress + i,
                        Value = values[i],
                        Timestamp = DateTime.UtcNow,
                    });
                }
            }
            else if (states is not null)
            {
                for (int i = 0; i < states.Length; i++)
                {
                    updates.Add(new MqttTagUpdate
                    {
                        UnitId = _profile.UnitId,
                        TagName = $"{_area}_{_startAddress + i}",
                        Area = _area,
                        Address = _startAddress + i,
                        Value = states[i],
                        Timestamp = DateTime.UtcNow,
                    });
                }
            }

            if (updates.Count > 0)
            {
                try
                {
                    await _mqttService.PublishAsync(updates, token);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to publish MQTT updates");
                }
            }
        }
    }
}
