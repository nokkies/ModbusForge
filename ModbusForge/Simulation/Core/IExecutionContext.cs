using System;
using Microsoft.Extensions.Logging;
using Modbus.Data;

namespace ModbusForge.Simulation.Core
{
    /// <summary>
    /// Context passed to a function block during execution.
    /// </summary>
    public interface IExecutionContext
    {
        /// <summary>
        /// Current simulation time. Use this for deterministic timing instead of DateTime.Now.
        /// </summary>
        DateTimeOffset CurrentTime { get; }

        /// <summary>
        /// Elapsed time since the previous execution cycle.
        /// </summary>
        TimeSpan Elapsed { get; }

        /// <summary>
        /// Number of completed execution cycles.
        /// </summary>
        int CycleCount { get; }

        /// <summary>
        /// True on the first execution cycle.
        /// </summary>
        bool IsFirstScan { get; }

        /// <summary>
        /// The Modbus data store, if available in the current runtime.
        /// </summary>
        DataStore? DataStore { get; }

        /// <summary>
        /// Logger scoped to the current block execution.
        /// </summary>
        ILogger Logger { get; }

        /// <summary>
        /// Reads an input port value. Returns a default value if the port is not connected or unbound.
        /// </summary>
        ISimulationValue? ReadInput(string portName);

        /// <summary>
        /// Reads a parameter value, falling back to the supplied default if missing.
        /// </summary>
        T ReadParameter<T>(string parameterName, T defaultValue);

        /// <summary>
        /// Reads a parameter value if it exists.
        /// </summary>
        T? ReadParameter<T>(string parameterName);

        /// <summary>
        /// Gets the state bag for this block instance. Values persist across execution cycles.
        /// </summary>
        IStateBag State { get; }

        /// <summary>
        /// Writes an output port value.
        /// </summary>
        void WriteOutput(string portName, ISimulationValue value);
    }
}
