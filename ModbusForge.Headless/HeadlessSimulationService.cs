using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModbusForge.Data;
using ModbusForge.Models;
using ModbusForge.Services;

namespace ModbusForge.Headless
{
    /// <summary>
    /// Runs a saved visual simulation graph (.mfsim/.json) headlessly: the shared
    /// engine ticks on its normal scan period against the private offline data
    /// store, and when the host stops (Ctrl+C, or the configured step count) the
    /// final node values and every non-default register/bit are dumped to the log
    /// for inspection or CI assertions.
    /// </summary>
    public class HeadlessSimulationService : BackgroundService
    {
        /// <summary>Grace added to the step timer so the final tick finishes before shutdown.</summary>
        private const int StepTimerGraceMs = 500;

        /// <summary>Safety cap on dumped points per area; a misbehaving graph must not flood the log.</summary>
        private const int MaxDumpedPoints = 1000;

        private readonly ILogger<HeadlessSimulationService> _logger;
        private readonly IHostApplicationLifetime _lifetime;
        private readonly ILoggerFactory _loggerFactory;
        private readonly string _path;
        private readonly int _intervalMsOverride;
        private readonly int _steps;

        private AvaloniaVisualSimulationService? _service;
        private VisualNodeEditorConfig? _config;

        /// <summary>
        /// The running simulation engine, or null before start / after stop.
        /// Internal for tests (pre-seeding the store, asserting scan period).
        /// </summary>
        internal AvaloniaVisualSimulationService? Engine => _service;

        public HeadlessSimulationService(
            IConfiguration configuration,
            IHostApplicationLifetime lifetime,
            ILoggerFactory loggerFactory,
            ILogger<HeadlessSimulationService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
            _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));

            _path = configuration["Simulation:Path"] ?? string.Empty;
            _intervalMsOverride = configuration.GetValue<int?>("Simulation:IntervalMs") ?? -1;
            _steps = configuration.GetValue<int?>("Simulation:Steps") ?? 0;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var error = LoadConfiguration();
            if (error is not null)
            {
                // A missing or corrupt file never heals itself - fail fast and stop,
                // mirroring the polling service's misconfiguration behaviour.
                _logger.LogError("Invalid simulation configuration: {Error}", error);
                _lifetime.StopApplication();
                return;
            }

            // A distinct logger category keeps engine diagnostics (cycle detection,
            // dropped connections) attributed to the simulation service in the log.
            _service = new AvaloniaVisualSimulationService(
                _loggerFactory.CreateLogger<AvaloniaVisualSimulationService>());

            var config = _config
                ?? throw new InvalidOperationException("Simulation configuration was not loaded.");

            if (_intervalMsOverride > 0)
            {
                // Start() applies config.ScanIntervalMs, so the CLI override is
                // folded into the config before the service starts.
                config.ScanIntervalMs = _intervalMsOverride;
            }

            _service.Start(config);
            _logger.LogInformation(
                "Simulation loaded from {Path}: {Nodes} nodes, {Connections} connections (store: {StoreMode}).",
                _path, config.Nodes.Count, config.Connections.Count, _service.StoreMode);

            if (_steps > 0)
            {
                var delay = _steps * _service.ScanIntervalMs + StepTimerGraceMs;
                _logger.LogInformation(
                    "Simulation will stop automatically after {Steps} ticks (~{DelayMs} ms).",
                    _steps, delay);
                try
                {
                    await Task.Delay(delay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                _lifetime.StopApplication();
                return;
            }

            try
            {
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown path.
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_service is not null)
            {
                // Capture the final state BEFORE stopping: Stop() resets node
                // values and error text to their stopped defaults. Dispose
                // stops the engine (it calls Stop internally) and releases
                // the timer - do not call Stop() separately.
                DumpFinalState();
                _service.Dispose();
                _service = null;
            }

            // Let the base cancel and await the long-running ExecuteAsync.
            await base.StopAsync(cancellationToken);
        }

        /// <summary>
        /// Loads the simulation file in the same tolerant shape the editor's loader
        /// accepts: the VisualNodeEditorConfig at the document root, or wrapped in
        /// a "Config" property.
        /// </summary>
        private string? LoadConfiguration()
        {
            if (string.IsNullOrWhiteSpace(_path))
                return "Simulation:Path is not set";

            if (!File.Exists(_path))
                return $"file not found: {_path}";

            try
            {
                var json = File.ReadAllText(_path);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var root = JsonNode.Parse(json) as JsonObject
                    ?? throw new InvalidDataException("The simulation file does not contain a JSON object.");
                var configNode = root["Config"] ?? root;
                _config = configNode.Deserialize<VisualNodeEditorConfig>(options)
                    ?? throw new InvalidDataException("The simulation file has no configuration.");
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                return $"failed to load {_path}: {ex.Message}";
            }

            return null;
        }

        private void DumpFinalState()
        {
            var config = _config;
            var store = _service?.CurrentDataStore;
            if (config is null || store is null) return;

            _logger.LogInformation("=== Simulation final state ===");

            VisualNode[] nodes;
            try
            {
                nodes = config.Nodes.ToArray();
            }
            catch (InvalidOperationException)
            {
                nodes = Array.Empty<VisualNode>();
            }

            foreach (var node in nodes)
            {
                var parts = new List<string> { node.CurrentValue ? "true" : "false" };

                // The node's real (double) value, shown only when it carries more
                // information than the bool (which already covers 0 and 1).
                var live = node.CurrentValueDouble;
                if (live != 0 && live != 1)
                    parts.Add(live.ToString("G5", CultureInfo.InvariantCulture));

                if (!string.IsNullOrEmpty(node.SecondaryOutputText))
                    parts.Add(node.SecondaryOutputText);
                if (!string.IsNullOrEmpty(node.ErrorText))
                    parts.Add($"error: {node.ErrorText}");

                _logger.LogInformation("  {Name} ({Id}): {Values}", node.Name, node.Id, string.Join(", ", parts));
            }

            LogNonDefaultPoints("HR", store.HoldingRegisters, v => v != 0, v => v.ToString(CultureInfo.InvariantCulture));
            LogNonDefaultPoints("IR", store.InputRegisters, v => v != 0, v => v.ToString(CultureInfo.InvariantCulture));
            LogNonDefaultPoints("COIL", store.CoilDiscretes, v => v, v => v ? "on" : "off");
            LogNonDefaultPoints("DI", store.InputDiscretes, v => v, v => v ? "on" : "off");
        }

        private void LogNonDefaultPoints<T>(
            string prefix,
            ModbusDataCollection<T> collection,
            Func<T, bool> isSet,
            Func<T, string> format)
            where T : struct
        {
            var points = new List<string>();
            var truncated = false;

            for (var address = 1; address < collection.Count; address++)
            {
                var value = collection[address];
                if (!isSet(value)) continue;

                points.Add($"{prefix}[{address}] = {format(value)}");
                if (points.Count >= MaxDumpedPoints)
                {
                    truncated = true;
                    break;
                }
            }

            if (points.Count == 0)
            {
                _logger.LogInformation("  {Prefix}*: all default", prefix);
                return;
            }

            if (truncated)
                points.Add($"... (truncated at {MaxDumpedPoints} points)");

            _logger.LogInformation("  {Prefix}* non-default:\n{Points}", prefix, string.Join('\n', points));
        }
    }
}
