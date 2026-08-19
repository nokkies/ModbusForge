using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModbusForge.Helpers;
using ModbusForge.Models;
using ModbusForge.Services;

namespace ModbusForge.Headless
{
    /// <summary>
    /// Headless background service that loads custom watch entries from a JSON file
    /// and continuously reads/writes them according to their Monitor and Continuous flags.
    /// </summary>
    public sealed class HeadlessCustomService : BackgroundService
    {
        private readonly IModbusService _modbusService;
        private readonly MqttGatewayService? _mqttService;
        private readonly IHostApplicationLifetime _lifetime;
        private readonly ILogger<HeadlessCustomService> _logger;
        private readonly ConnectionProfile _profile;
        private readonly int _tickMs;
        private readonly string _customFile;
        private readonly MqttSettings _mqttSettings;

        public HeadlessCustomService(
            IModbusService modbusService,
            MqttGatewayService? mqttService,
            IHostApplicationLifetime lifetime,
            IConfiguration configuration,
            ILogger<HeadlessCustomService> logger)
        {
            _modbusService = modbusService ?? throw new ArgumentNullException(nameof(modbusService));
            _mqttService = mqttService;
            _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _profile = HeadlessProfileFactory.CreateConnectionProfile(configuration);
            _tickMs = configuration.GetValue<int?>("Custom:TickMs") ?? 100;
            _customFile = configuration["Custom:Path"] ?? string.Empty;
            _mqttSettings = HeadlessProfileFactory.CreateMqttSettings(configuration);
        }

        /// <summary>
        /// The connection is treated as lost after this many consecutive ticks
        /// in which every attempted read went unanswered.
        /// </summary>
        private const int MaxFailedReadTicks = 3;

        private const int MinTickMs = 10;

        private int _connectionLost;
        private int _failedReadTicks;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (_tickMs < MinTickMs)
            {
                // A misconfiguration never heals itself - fail fast and stop.
                _logger.LogError("Invalid custom watch configuration: tick must be at least {MinTickMs}ms, got {TickMs}", MinTickMs, _tickMs);
                _lifetime.StopApplication();
                return;
            }

            var entries = await LoadCustomEntriesAsync(_customFile, stoppingToken);
            if (entries is null || entries.Count == 0)
            {
                // Nothing to run: stopping the host surfaces the missing/broken
                // file instead of idling forever.
                _logger.LogError("No custom entries found in {File}", _customFile);
                _lifetime.StopApplication();
                return;
            }

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

            if (_mqttService is not null)
            {
                _mqttService.ApplySettings(_mqttSettings);
                try
                {
                    await _mqttService.ConnectAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
                {
                    // The gateway keeps retrying its broker in the background;
                    // a down broker must not take the custom watch down with it.
                    _logger.LogWarning(ex, "MQTT gateway failed to start; watch continues without publishing");
                }
            }

            _logger.LogInformation("Connected. Running custom watch on {Count} entries.", entries.Count);

            while (!stoppingToken.IsCancellationRequested)
            {
                if (Volatile.Read(ref _connectionLost) != 0)
                {
                    Volatile.Write(ref _connectionLost, 0);
                    Volatile.Write(ref _failedReadTicks, 0);
                    await ReconnectAsync(stoppingToken, "Connection lost");
                    if (stoppingToken.IsCancellationRequested)
                        break;
                }

                var now = DateTime.UtcNow;
                var readsThisTick = 0;
                var readFailuresThisTick = 0;

                foreach (var entry in entries)
                {
                    if (stoppingToken.IsCancellationRequested) break;

                    try
                    {
                        if (entry.Monitor && (now - entry.LastReadUtc).TotalMilliseconds >= entry.ReadPeriodMs)
                        {
                            readsThisTick++;
                            var (value, readOk) = await ReadCustomValueAsync(entry, stoppingToken);
                            if (!readOk)
                                readFailuresThisTick++;

                            entry.Value = value;
                            entry.LastReadUtc = now;
                            _logger.LogInformation("[{Name}] {Area}:{Address} = {Value}", entry.Name, entry.Area, entry.Address, value);
                            await PublishAsync(entry, value, stoppingToken);
                        }

                        if (entry.Continuous && (now - entry.LastWriteUtc).TotalMilliseconds >= entry.PeriodMs)
                        {
                            var ok = await WriteCustomValueAsync(entry, stoppingToken);
                            entry.LastWriteUtc = now;
                            _logger.LogInformation("[{Name}] wrote {Value} to {Area}:{Address} ({Ok})",
                                entry.Name, entry.WriteValue, entry.Area, entry.Address, ok);
                            await PublishAsync(entry, entry.WriteValue, stoppingToken);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Custom watch error for {Name}", entry.Name);
                    }
                }

                // A device that answers no read at all (alive socket, dead
                // device) never raises ConnectionLost - the failed-tick count
                // is the fallback. Ticks with no reads at all (write-only
                // watches, or periods not yet due) do not count.
                if (readsThisTick > 0)
                {
                    Volatile.Write(ref _failedReadTicks,
                        readFailuresThisTick == readsThisTick ? Volatile.Read(ref _failedReadTicks) + 1 : 0);
                }

                if (Volatile.Read(ref _failedReadTicks) >= MaxFailedReadTicks)
                {
                    Volatile.Write(ref _failedReadTicks, 0);
                    await ReconnectAsync(stoppingToken, $"{MaxFailedReadTicks} consecutive ticks without a single answered read");
                    if (stoppingToken.IsCancellationRequested)
                        break;
                }

                try
                {
                    await Task.Delay(_tickMs, stoppingToken);
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

        private static async Task<List<CustomEntry>?> LoadCustomEntriesAsync(string path, CancellationToken token)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            await using var stream = File.OpenRead(path);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: token);

            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                return JsonSerializer.Deserialize<List<CustomEntry>>(doc.RootElement.GetRawText());
            }

            if (doc.RootElement.TryGetProperty("CustomEntries", out var customEntries))
            {
                return JsonSerializer.Deserialize<List<CustomEntry>>(customEntries.GetRawText());
            }

            return null;
        }

        /// <summary>
        /// Reads an entry. <c>Ok</c> is false when the device did not answer -
        /// the caller uses that to detect a silent death of the connection.
        /// </summary>
        private async Task<(string Value, bool Ok)> ReadCustomValueAsync(CustomEntry entry, CancellationToken token)
        {
            var area = (entry.Area ?? "HoldingRegister").ToLowerInvariant();
            var type = (entry.Type ?? "int").ToLowerInvariant();

            switch (area)
            {
                case "holdingregister":
                case "inputregister":
                    int count = type == "real" ? 2 : 1;
                    var values = area == "holdingregister"
                        ? await _modbusService.ReadHoldingRegistersAsync(_profile.UnitId, entry.Address, count)
                        : await _modbusService.ReadInputRegistersAsync(_profile.UnitId, entry.Address, count);

                    if (values == null || values.Length == 0) return ("No response", false);

                    return (type switch
                    {
                        "int" => unchecked((short)values[0]).ToString(CultureInfo.InvariantCulture),
                        "real" when values.Length >= 2 => DataTypeConverter.ToSingle(values[0], values[1], false, false).ToString(CultureInfo.InvariantCulture),
                        "string" => DataTypeConverter.ToString(values[0]),
                        _ => values[0].ToString(CultureInfo.InvariantCulture)
                    }, true);

                case "coil":
                case "discreteinput":
                    var coilValues = area == "coil"
                        ? await _modbusService.ReadCoilsAsync(_profile.UnitId, entry.Address, 1)
                        : await _modbusService.ReadDiscreteInputsAsync(_profile.UnitId, entry.Address, 1);
                    if (coilValues == null || coilValues.Length == 0) return ("No response", false);
                    return (coilValues[0] ? "1" : "0", true);

                default:
                    return ($"Unknown area: {entry.Area}", false);
            }
        }

        private async Task<bool> WriteCustomValueAsync(CustomEntry entry, CancellationToken token)
        {
            var area = (entry.Area ?? "HoldingRegister").ToLowerInvariant();
            var type = (entry.Type ?? "int").ToLowerInvariant();

            switch (area)
            {
                case "holdingregister":
                    switch (type)
                    {
                        case "real":
                            if (float.TryParse(entry.WriteValue, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ||
                                float.TryParse(entry.WriteValue, NumberStyles.Float, CultureInfo.CurrentCulture, out f))
                            {
                                var words = DataTypeConverter.ToUInt16(f, false, false);
                                await _modbusService.WriteRegistersAsync(_profile.UnitId, entry.Address, words);
                                return true;
                            }
                            return false;

                        case "string":
                            var stringWords = DataTypeConverter.ToUInt16(entry.WriteValue ?? string.Empty);
                            await _modbusService.WriteRegistersAsync(_profile.UnitId, entry.Address, stringWords);
                            return true;

                        case "int":
                            if (int.TryParse(entry.WriteValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int iv))
                            {
                                await _modbusService.WriteSingleRegisterAsync(_profile.UnitId, entry.Address, unchecked((ushort)iv));
                                return true;
                            }
                            return false;

                        default:
                            if (uint.TryParse(entry.WriteValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint uv))
                            {
                                if (uv > 0xFFFF) uv = 0xFFFF;
                                await _modbusService.WriteSingleRegisterAsync(_profile.UnitId, entry.Address, (ushort)uv);
                                return true;
                            }
                            return false;
                    }

                case "coil":
                    if (TryParseBool(entry.WriteValue, out bool b))
                    {
                        await _modbusService.WriteSingleCoilAsync(_profile.UnitId, entry.Address, b);
                        return true;
                    }
                    return false;

                default:
                    return false;
            }
        }

        private async Task PublishAsync(CustomEntry entry, object? value, CancellationToken token)
        {
            if (_mqttService is null) return;

            var plcArea = Enum.TryParse<PlcArea>(entry.Area, true, out var a) ? a : PlcArea.HoldingRegister;

            var update = new MqttTagUpdate
            {
                UnitId = _profile.UnitId,
                TagName = entry.Name,
                Area = plcArea,
                Address = entry.Address,
                Value = value,
                Timestamp = DateTime.UtcNow,
            };

            try
            {
                await _mqttService.PublishAsync(new[] { update }, token);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to publish MQTT update for {Name}", entry.Name);
            }
        }

        private static bool TryParseBool(string? text, out bool result)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                result = false;
                return false;
            }

            var trimmed = text.Trim();

            if (bool.TryParse(trimmed, out result))
                return true;

            if (int.TryParse(trimmed, out var value))
            {
                result = value != 0;
                return true;
            }

            if (trimmed.Equals("on", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                result = true;
                return true;
            }

            if (trimmed.Equals("off", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("no", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                result = false;
                return true;
            }

            result = false;
            return false;
        }
    }
}
