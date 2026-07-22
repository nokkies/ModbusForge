using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Modbus.Data;
using ModbusForge.Simulation.Core;

namespace ModbusForge.Simulation.Engine
{
    /// <summary>
    /// Per-node execution context implementation used by the engine.
    /// </summary>
    internal sealed class ExecutionContext : IExecutionContext
    {
        private readonly SimulationNode _node;
        private readonly IReadOnlyDictionary<string, ISimulationValue> _inputs;
        private readonly Dictionary<string, ISimulationValue> _outputs = new(StringComparer.Ordinal);

        public DateTimeOffset CurrentTime { get; }
        public TimeSpan Elapsed { get; }
        public int CycleCount { get; }
        public bool IsFirstScan { get; }
        public DataStore? DataStore { get; }
        public ILogger Logger { get; }
        public IStateBag State => _node.State;

        public ExecutionContext(
            SimulationNode node,
            IReadOnlyDictionary<string, ISimulationValue> inputs,
            DateTimeOffset currentTime,
            TimeSpan elapsed,
            int cycleCount,
            DataStore? dataStore,
            ILogger logger)
        {
            _node = node ?? throw new ArgumentNullException(nameof(node));
            _inputs = inputs ?? new Dictionary<string, ISimulationValue>(StringComparer.Ordinal);
            CurrentTime = currentTime;
            Elapsed = elapsed;
            CycleCount = cycleCount;
            IsFirstScan = cycleCount == 0;
            DataStore = dataStore;
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public ISimulationValue? ReadInput(string portName)
        {
            if (_inputs.TryGetValue(portName, out var value))
                return value;

            return null;
        }

        public T ReadParameter<T>(string parameterName, T defaultValue)
        {
            if (_node.Parameters.TryGetValue(parameterName, out var raw) && raw is T value)
                return value;

            if (raw is not null && typeof(T).IsAssignableFrom(raw.GetType()))
                return (T)raw;

            if (raw is IConvertible convertible)
            {
                try
                {
                    return (T)Convert.ChangeType(convertible, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
                }
                catch
                {
                    return defaultValue;
                }
            }

            return defaultValue;
        }

        public T? ReadParameter<T>(string parameterName)
        {
            if (_node.Parameters.TryGetValue(parameterName, out var raw) && raw is T value)
                return value;

            if (raw is not null && typeof(T).IsAssignableFrom(raw.GetType()))
                return (T)raw;

            if (raw is IConvertible convertible)
            {
                try
                {
                    return (T)Convert.ChangeType(convertible, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
                }
                catch
                {
                    return default;
                }
            }

            return default;
        }

        public void WriteOutput(string portName, ISimulationValue value)
        {
            _outputs[portName] = value ?? throw new ArgumentNullException(nameof(value));
        }

        public IReadOnlyDictionary<string, ISimulationValue> GetOutputs()
        {
            return _outputs;
        }
    }
}
