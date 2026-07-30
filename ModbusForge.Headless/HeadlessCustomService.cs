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
        private readonly ILogger<HeadlessCustomService> _logger;
        private readonly string _host;
        private readonly int _port;
        private readonly byte _unitId;
        private readonly int _tickMs;
        private readonly string _customFile;

        public HeadlessCustomService(
            IModbusService modbusService,
            IConfiguration configuration,
            ILogger<HeadlessCustomService> logger)
        {
            _modbusService = modbusService ?? throw new ArgumentNullException(nameof(modbusService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _host = configuration["Connection:Host"] ?? "127.0.0.1";
            _port = int.TryParse(configuration["Connection:Port"], out var p) ? p : 502;
            _unitId = byte.TryParse(configuration["Connection:UnitId"], out var u) ? u : (byte)1;
            _tickMs = int.TryParse(configuration["Custom:TickMs"], out var t) ? t : 100;
            _customFile = configuration["Custom:Path"] ?? string.Empty;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var entries = await LoadCustomEntriesAsync(_customFile, stoppingToken);
            if (entries is null || entries.Count == 0)
            {
                _logger.LogError("No custom entries found in {File}", _customFile);
                return;
            }

            _logger.LogInformation("Connecting to {Host}:{Port} (unit {UnitId})...", _host, _port, _unitId);

            var connected = await _modbusService.ConnectAsync(_host, _port, _unitId.ToString(), stoppingToken);
            if (!connected)
            {
                _logger.LogError("Failed to connect to {Host}:{Port}", _host, _port);
                return;
            }

            _logger.LogInformation("Connected. Running custom watch on {Count} entries.", entries.Count);

            while (!stoppingToken.IsCancellationRequested)
            {
                var now = DateTime.UtcNow;

                foreach (var entry in entries)
                {
                    if (stoppingToken.IsCancellationRequested) break;

                    try
                    {
                        if (entry.Monitor && (now - entry.LastReadUtc).TotalMilliseconds >= entry.ReadPeriodMs)
                        {
                            var value = await ReadCustomValueAsync(entry, stoppingToken);
                            entry.Value = value;
                            entry.LastReadUtc = now;
                            _logger.LogInformation("[{Name}] {Area}:{Address} = {Value}", entry.Name, entry.Area, entry.Address, value);
                        }

                        if (entry.Continuous && (now - entry.LastWriteUtc).TotalMilliseconds >= entry.PeriodMs)
                        {
                            var ok = await WriteCustomValueAsync(entry, stoppingToken);
                            entry.LastWriteUtc = now;
                            _logger.LogInformation("[{Name}] wrote {Value} to {Area}:{Address} ({Ok})",
                                entry.Name, entry.WriteValue, entry.Area, entry.Address, ok);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Custom watch error for {Name}", entry.Name);
                    }
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

            await _modbusService.DisconnectAsync();
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

        private async Task<string> ReadCustomValueAsync(CustomEntry entry, CancellationToken token)
        {
            var area = (entry.Area ?? "HoldingRegister").ToLowerInvariant();
            var type = (entry.Type ?? "uint").ToLowerInvariant();

            switch (area)
            {
                case "holdingregister":
                case "inputregister":
                    int count = type == "real" ? 2 : 1;
                    var values = area == "holdingregister"
                        ? await _modbusService.ReadHoldingRegistersAsync(_unitId, entry.Address, count)
                        : await _modbusService.ReadInputRegistersAsync(_unitId, entry.Address, count);

                    if (values == null || values.Length == 0) return "No response";

                    return type switch
                    {
                        "int" => unchecked((short)values[0]).ToString(CultureInfo.InvariantCulture),
                        "real" when values.Length >= 2 => DataTypeConverter.ToSingle(values[0], values[1], false, false).ToString(CultureInfo.InvariantCulture),
                        "string" => DataTypeConverter.ToString(values[0]),
                        _ => values[0].ToString(CultureInfo.InvariantCulture)
                    };

                case "coil":
                case "discreteinput":
                    var coilValues = area == "coil"
                        ? await _modbusService.ReadCoilsAsync(_unitId, entry.Address, 1)
                        : await _modbusService.ReadDiscreteInputsAsync(_unitId, entry.Address, 1);
                    if (coilValues == null || coilValues.Length == 0) return "No response";
                    return coilValues[0] ? "1" : "0";

                default:
                    return $"Unknown area: {entry.Area}";
            }
        }

        private async Task<bool> WriteCustomValueAsync(CustomEntry entry, CancellationToken token)
        {
            var area = (entry.Area ?? "HoldingRegister").ToLowerInvariant();
            var type = (entry.Type ?? "uint").ToLowerInvariant();

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
                                await _modbusService.WriteRegistersAsync(_unitId, entry.Address, words);
                                return true;
                            }
                            return false;

                        case "string":
                            var stringWords = DataTypeConverter.ToUInt16(entry.WriteValue ?? string.Empty);
                            await _modbusService.WriteRegistersAsync(_unitId, entry.Address, stringWords);
                            return true;

                        case "int":
                            if (int.TryParse(entry.WriteValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int iv))
                            {
                                await _modbusService.WriteSingleRegisterAsync(_unitId, entry.Address, unchecked((ushort)iv));
                                return true;
                            }
                            return false;

                        default:
                            if (uint.TryParse(entry.WriteValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint uv))
                            {
                                if (uv > 0xFFFF) uv = 0xFFFF;
                                await _modbusService.WriteSingleRegisterAsync(_unitId, entry.Address, (ushort)uv);
                                return true;
                            }
                            return false;
                    }

                case "coil":
                    if (TryParseBool(entry.WriteValue, out bool b))
                    {
                        await _modbusService.WriteSingleCoilAsync(_unitId, entry.Address, b);
                        return true;
                    }
                    return false;

                default:
                    return false;
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
