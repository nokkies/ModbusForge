using System;
using System.Threading;
using System.Threading.Channels;
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
    /// </summary>
    public sealed class HeadlessPollingService : BackgroundService
    {
        private readonly IModbusService _modbusService;
        private readonly ILogger<HeadlessPollingService> _logger;
        private readonly string _host;
        private readonly int _port;
        private readonly byte _unitId;
        private readonly int _startAddress;
        private readonly int _count;
        private readonly int _intervalMs;
        private readonly PlcArea _area;

        public HeadlessPollingService(
            IModbusService modbusService,
            IConfiguration configuration,
            ILogger<HeadlessPollingService> logger)
        {
            _modbusService = modbusService ?? throw new ArgumentNullException(nameof(modbusService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _host = configuration["Connection:Host"] ?? "127.0.0.1";
            _port = int.TryParse(configuration["Connection:Port"], out var p) ? p : 502;
            _unitId = byte.TryParse(configuration["Connection:UnitId"], out var u) ? u : (byte)1;
            _startAddress = int.TryParse(configuration["Polling:StartAddress"], out var s) ? s : 0;
            _count = int.TryParse(configuration["Polling:Count"], out var c) ? c : 10;
            _intervalMs = int.TryParse(configuration["Polling:IntervalMs"], out var i) ? i : 1000;
            _area = Enum.TryParse<PlcArea>(configuration["Polling:Area"], true, out var a) ? a : PlcArea.HoldingRegister;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Connecting to {Host}:{Port} (unit {UnitId})...", _host, _port, _unitId);

            var connected = await _modbusService.ConnectAsync(_host, _port, _unitId.ToString(), stoppingToken);
            if (!connected)
            {
                _logger.LogError("Failed to connect to {Host}:{Port}", _host, _port);
                return;
            }

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

            await _modbusService.DisconnectAsync();
        }

        private async Task PollOnceAsync(CancellationToken token)
        {
            if (_area == PlcArea.HoldingRegister || _area == PlcArea.InputRegister)
            {
                var values = _area == PlcArea.HoldingRegister
                    ? await _modbusService.ReadHoldingRegistersAsync(_unitId, _startAddress, _count)
                    : await _modbusService.ReadInputRegistersAsync(_unitId, _startAddress, _count);

                if (values is null)
                {
                    _logger.LogWarning("No response for {Area} [{StartAddress}..{EndAddress}]", _area, _startAddress, _startAddress + _count - 1);
                    return;
                }

                _logger.LogInformation("{Area} [{StartAddress}..{EndAddress}]: {Values}",
                    _area, _startAddress, _startAddress + values.Length - 1, string.Join(", ", values));
            }
            else
            {
                var states = _area == PlcArea.Coil
                    ? await _modbusService.ReadCoilsAsync(_unitId, _startAddress, _count)
                    : await _modbusService.ReadDiscreteInputsAsync(_unitId, _startAddress, _count);

                if (states is null)
                {
                    _logger.LogWarning("No response for {Area} [{StartAddress}..{EndAddress}]", _area, _startAddress, _startAddress + _count - 1);
                    return;
                }

                _logger.LogInformation("{Area} [{StartAddress}..{EndAddress}]: {Values}",
                    _area, _startAddress, _startAddress + states.Length - 1, string.Join(", ", states));
            }
        }
    }
}
