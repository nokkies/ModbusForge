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
    public class HeadlessPollingService : BackgroundService
    {
        /// <summary>
        /// The connection is treated as lost after this many consecutive polls
        /// without a response. The transport's own ConnectionLost event usually
        /// fires first for dead sockets; this catches the case where the socket
        /// is alive but the device is not answering.
        /// </summary>
        private const int MaxConsecutiveNullReads = 3;

        // Modbus protocol limits for a single request; larger counts are
        // chunked by the service, so these only guard against nonsense input.
        private const int MinPollCount = 1;
        private const int MaxRegisterPollCount = 125;
        private const int MaxBitPollCount = 2000;
        private const int MaxAddress = 65535;
        private const int MinIntervalMs = 50;

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
        private int _connectionLost;

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

            _mqttSettings = HeadlessProfileFactory.CreateMqttSettings(configuration);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var error = ValidatePollingParameters();
            if (error is not null)
            {
                // A misconfiguration never heals itself - fail fast and stop.
                _logger.LogError("Invalid polling configuration: {Error}", error);
                _lifetime.StopApplication();
                return;
            }

            LogConnecting();

            // Unattended hosts start before (or with) the device they talk to:
            // retry with backoff instead of exiting on the first failure.
            var connected = await HeadlessConnection.EnsureConnectedAsync(
                _modbusService, _profile, _logger, stoppingToken);
            if (!connected)
                return; // cancelled

            // The service instance is long-lived (the socket reconnects
            // underneath it), so the handler stays subscribed for the whole
            // lifetime of this service.
            _modbusService.ConnectionLost += OnConnectionLost;

            await ConnectMqttAsync(stoppingToken);

            _logger.LogInformation("Connected. Polling {Area} every {IntervalMs}ms.", _area, _intervalMs);

            var consecutiveNullReads = 0;

            while (!stoppingToken.IsCancellationRequested)
            {
                if (Volatile.Read(ref _connectionLost) != 0)
                {
                    Volatile.Write(ref _connectionLost, 0);
                    consecutiveNullReads = 0;
                    await ReconnectAsync(stoppingToken, "Connection lost");
                    if (stoppingToken.IsCancellationRequested)
                        break;
                }

                bool gotResponse;
                try
                {
                    gotResponse = await PollOnceAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Poll failed");
                    gotResponse = false;
                }

                if (gotResponse)
                {
                    consecutiveNullReads = 0;
                }
                else if (++consecutiveNullReads >= MaxConsecutiveNullReads)
                {
                    consecutiveNullReads = 0;
                    await ReconnectAsync(stoppingToken, $"{MaxConsecutiveNullReads} consecutive polls without a response");
                    if (stoppingToken.IsCancellationRequested)
                        break;
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

            await (_mqttService?.DisconnectAsync() ?? Task.CompletedTask);
            await _modbusService.DisconnectAsync();
            _lifetime.StopApplication();
        }

        private string? ValidatePollingParameters()
        {
            if (_startAddress < 0 || _startAddress > MaxAddress)
                return $"start address must be 0..{MaxAddress}, got {_startAddress}";

            var maxCount = _area is PlcArea.Coil or PlcArea.DiscreteInput ? MaxBitPollCount : MaxRegisterPollCount;
            if (_count < MinPollCount || _count > maxCount)
                return $"poll count must be {MinPollCount}..{maxCount} for {_area}, got {_count}";

            if (_startAddress + _count > MaxAddress + 1)
                return $"start address + count must not exceed {MaxAddress + 1}, got {_startAddress + _count}";

            if (_intervalMs < MinIntervalMs)
                return $"poll interval must be at least {MinIntervalMs}ms, got {_intervalMs}";

            return null;
        }

        private void LogConnecting()
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
        }

        private async Task ConnectMqttAsync(CancellationToken token)
        {
            if (_mqttService is null) return;

            _mqttService.ApplySettings(_mqttSettings);
            try
            {
                await _mqttService.ConnectAsync(token);
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                // The gateway keeps retrying its broker in the background;
                // a down broker must not take the Modbus polling down with it.
                _logger.LogWarning(ex, "MQTT gateway failed to start; polling continues without publishing");
            }
        }

        private async Task ReconnectAsync(CancellationToken token, string reason)
        {
            _logger.LogWarning("{Reason} - reconnecting...", reason);

            try
            {
                await _modbusService.DisconnectAsync();
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                _logger.LogDebug(ex, "Error while disconnecting before reconnect");
            }

            var connected = await HeadlessConnection.EnsureConnectedAsync(
                _modbusService, _profile, _logger, token);
            if (!connected)
                return; // cancelled

            _logger.LogInformation("Reconnected.");
        }

        private void OnConnectionLost(object? sender, EventArgs e)
        {
            Volatile.Write(ref _connectionLost, 1);
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
