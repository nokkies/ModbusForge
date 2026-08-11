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
                try
                {
                    await PollOnceAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Poll failed");
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

        private async Task PollOnceAsync(CancellationToken token)
        {
            if (_area == PlcArea.HoldingRegister || _area == PlcArea.InputRegister)
            {
                var values = _area == PlcArea.HoldingRegister
                    ? await _modbusService.ReadHoldingRegistersAsync(_profile.UnitId, _startAddress, _count)
                    : await _modbusService.ReadInputRegistersAsync(_profile.UnitId, _startAddress, _count);

                if (values is null)
                {
                    _logger.LogWarning("No response for {Area} [{StartAddress}..{EndAddress}]", _area, _startAddress, _startAddress + _count - 1);
                    return;
                }

                _logger.LogInformation("{Area} [{StartAddress}..{EndAddress}]: {Values}",
                    _area, _startAddress, _startAddress + values.Length - 1, string.Join(", ", values));

                await PublishAsync(values, null, token);
            }
            else
            {
                var states = _area == PlcArea.Coil
                    ? await _modbusService.ReadCoilsAsync(_profile.UnitId, _startAddress, _count)
                    : await _modbusService.ReadDiscreteInputsAsync(_profile.UnitId, _startAddress, _count);

                if (states is null)
                {
                    _logger.LogWarning("No response for {Area} [{StartAddress}..{EndAddress}]", _area, _startAddress, _startAddress + _count - 1);
                    return;
                }

                _logger.LogInformation("{Area} [{StartAddress}..{EndAddress}]: {Values}",
                    _area, _startAddress, _startAddress + states.Length - 1, string.Join(", ", states));

                await PublishAsync(null, states, token);
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
