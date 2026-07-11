using System.Threading;
using System.Threading.Tasks;
using ModbusForge.Services.Api.Dtos;

namespace ModbusForge.Services.Api;

/// <summary>
/// Focused facade that the local REST API uses to interact with the WPF application.
/// Implementations handle all dispatcher transitions; callers need not be aware of WPF threading.
/// </summary>
public interface IApiApplicationService
{
    /// <summary>Gets a snapshot of the current application status.</summary>
    ApiStatus GetStatus();

    /// <summary>Initiates a connection and waits up to <paramref name="token"/> / the built-in timeout.</summary>
    Task<OperationResult> ConnectAsync(CancellationToken token);

    /// <summary>Initiates a disconnection and waits for completion.</summary>
    Task<OperationResult> DisconnectAsync(CancellationToken token);

    // --- Modbus reads ---

    /// <summary>Reads holding registers. Returns null if the Modbus device did not respond.</summary>
    Task<ushort[]?> ReadHoldingRegistersAsync(byte unitId, ushort address, ushort count, CancellationToken token);

    /// <summary>Reads coils. Returns null if the Modbus device did not respond.</summary>
    Task<bool[]?> ReadCoilsAsync(byte unitId, ushort address, ushort count, CancellationToken token);

    // --- Custom tags ---

    /// <summary>Returns a snapshot of all custom tag entries.</summary>
    Task<System.Collections.Generic.IReadOnlyList<Models.CustomEntry>> GetCustomTagsAsync(CancellationToken token);

    /// <summary>
    /// Adds or replaces a custom tag entry.
    /// The address must not collide with an existing entry unless the caller is doing an explicit replace.
    /// Returns the canonical entry after insertion.
    /// </summary>
    Task<Models.CustomEntry> AddCustomTagAsync(Models.CustomEntry entry, CancellationToken token);

    /// <summary>Removes the custom tag at <paramref name="address"/>. Returns false if not found.</summary>
    Task<bool> RemoveCustomTagAsync(int address, CancellationToken token);

    // --- Simulation nodes ---

    /// <summary>Returns a snapshot of all simulation nodes in the current config.</summary>
    Task<System.Collections.Generic.IReadOnlyList<Models.VisualNode>> GetSimulationNodesAsync(CancellationToken token);

    /// <summary>Adds or replaces a simulation node by Id.</summary>
    Task<Models.VisualNode> UpsertSimulationNodeAsync(Models.VisualNode node, CancellationToken token);

    /// <summary>Removes the simulation node with the given id. Returns false if not found.</summary>
    Task<bool> RemoveSimulationNodeAsync(string id, CancellationToken token);

    // --- Scripts ---

    /// <summary>Returns a snapshot of all script rules.</summary>
    Task<System.Collections.Generic.IReadOnlyList<Models.ScriptRule>> GetScriptRulesAsync(CancellationToken token);

    /// <summary>Adds or replaces a script rule by name.</summary>
    Task<Models.ScriptRule> UpsertScriptRuleAsync(Models.ScriptRule rule, CancellationToken token);

    /// <summary>Removes the script rule with the given name. Returns false if not found.</summary>
    Task<bool> RemoveScriptRuleAsync(string name, CancellationToken token);

    // --- Logs / Trends ---

    /// <summary>Returns a snapshot of recent console log messages.</summary>
    Task<System.Collections.Generic.IReadOnlyList<string>> GetLogsAsync(CancellationToken token);

    /// <summary>Adds a trend series key with an optional display name.</summary>
    Task AddTrendAsync(string key, string displayName, CancellationToken token);
}
