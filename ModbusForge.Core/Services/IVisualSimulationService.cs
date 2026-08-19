using System;
using ModbusForge.Core.Simulation.Core;
using ModbusForge.Models;

namespace ModbusForge.Services
{
    /// <summary>
    /// Common interface for the visual node simulation service used by the Avalonia desktop UI.
    /// </summary>
    public interface IVisualSimulationService : IDisposable
    {
        /// <summary>
        /// True when the simulation timer is active.
        /// </summary>
        bool IsRunning { get; }

        /// <summary>
        /// The function block catalog. Exposed so the node editor can query
        /// the available output ports for a given element type.
        /// </summary>
        FunctionBlockCatalog Catalog { get; }

        /// <summary>
        /// Starts the simulation using the supplied editor configuration.
        /// </summary>
        void Start(VisualNodeEditorConfig config);

        /// <summary>
        /// Stops the simulation and resets live values.
        /// </summary>
        void Stop();

        /// <summary>
        /// Executes one simulation cycle and updates the configured nodes.
        /// </summary>
        void UpdateNodeValues();

        /// <summary>
        /// Current simulation scan period in milliseconds.
        /// </summary>
        int ScanIntervalMs { get; }

        /// <summary>
        /// Sets the simulation scan period (clamped to the supported range), effective immediately.
        /// </summary>
        void SetScanIntervalMs(int ms);

        /// <summary>
        /// "device" when bound addresses read/write the connected server's data store,
        /// "local" when they use the private offline store.
        /// </summary>
        string StoreMode { get; }

        /// <summary>
        /// Raised after a graph rebuild when the set of cycle-locked (non-evaluated) node ids changes.
        /// </summary>
        event Action<IReadOnlyList<string>> CyclesChanged;

        /// <summary>
        /// Returns the cached boolean value of the specified node.
        /// </summary>
        bool GetNodeValue(string nodeId);

        /// <summary>
        /// Writes a user-edited value to the data store at the node's input or output address.
        /// </summary>
        void WriteNodeValue(string nodeId, double value);
    }
}
