using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModbusForge.Models;
using ModbusForge.Services.Api.Dtos;

namespace ModbusForge.Services.Api;

/// <summary>
/// Shared, UI-agnostic implementation of <see cref="IApiApplicationService"/>.
/// All access to UI-owned objects is marshalled through <see cref="IDispatcher"/>.
/// Does NOT depend on any UI framework directly; the constructor receives
/// narrow interfaces so the service can be used across WPF, Avalonia and tests.
/// </summary>
public sealed class ApiApplicationService : IApiApplicationService
{
    private const int ConnectionStateTimeoutMs = 30_000;

    private readonly IAppStateAccessor _appState;
    private readonly IConnectionManager _connectionManager;
    private readonly IScriptRuleService _scriptRuleService;
    private readonly IConsoleLoggerService _consoleLoggerService;
    private readonly ITrendLogger _trendLogger;
    private readonly IDispatcher _dispatcher;
    private readonly ILogger<ApiApplicationService> _logger;

    // Serialise connect/disconnect so that concurrent API calls queue rather than race.
    private readonly SemaphoreSlim _connectLock = new(1, 1);

    public ApiApplicationService(
        IAppStateAccessor appState,
        IConnectionManager connectionManager,
        IScriptRuleService scriptRuleService,
        IConsoleLoggerService consoleLoggerService,
        ITrendLogger trendLogger,
        IDispatcher dispatcher,
        ILogger<ApiApplicationService> logger)
    {
        _appState = appState ?? throw new ArgumentNullException(nameof(appState));
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _scriptRuleService = scriptRuleService ?? throw new ArgumentNullException(nameof(scriptRuleService));
        _consoleLoggerService = consoleLoggerService ?? throw new ArgumentNullException(nameof(consoleLoggerService));
        _trendLogger = trendLogger ?? throw new ArgumentNullException(nameof(trendLogger));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// The service backing the currently active connection profile. Resolved per
    /// call so API operations always follow the UI's connection (including
    /// profile switches and reconnections).
    /// </summary>
    private IModbusService? ActiveModbusService => _connectionManager.ActiveService;

    // ──────────────────────────────────────────────────────────────────────────
    // Status
    // ──────────────────────────────────────────────────────────────────────────

    public Task<ApiStatus> GetStatusAsync(CancellationToken token)
        => _dispatcher.InvokeAsync(
            () => new ApiStatus(_appState.IsConnected, _appState.Mode));

    // ──────────────────────────────────────────────────────────────────────────
    // Connect / Disconnect
    // ──────────────────────────────────────────────────────────────────────────

    public async Task<OperationResult> ConnectAsync(CancellationToken token)
    {
        // Serialise concurrent calls to prevent double-connect races.
        await _connectLock.WaitAsync(token);
        try
        {
            bool initiated = false;
            await _dispatcher.InvokeAsync(() =>
            {
                if (!_appState.IsConnected && _appState.ConnectCommand.CanExecute(null))
                {
                    _appState.ConnectCommand.Execute(null);
                    initiated = true;
                }
            });

            if (!initiated)
                return OperationResult.Fail("Already connected or cannot connect.");

            // Event-driven wait instead of polling. The wait resolves on the
            // OUTCOME of the attempt — success OR a recorded connection error —
            // so a failed connect is reported with its real reason instead of
            // hanging for the full timeout.
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            string? failureMessage = null;
            PropertyChangedEventHandler? handler = null;
            handler = (_, e) =>
            {
                if (e.PropertyName == nameof(IAppStateAccessor.IsConnected) && _appState.IsConnected)
                    tcs.TrySetResult(true);
                else if (e.PropertyName == nameof(IAppStateAccessor.HasConnectionError) && _appState.HasConnectionError)
                {
                    failureMessage = _appState.StatusMessage;
                    tcs.TrySetResult(false);
                }
            };
            _appState.PropertyChanged += handler;

            // Re-check the state on the dispatcher: a fast failure can fire
            // between Execute and the subscription. null means still pending.
            var immediate = await _dispatcher.InvokeAsync<bool?>(() =>
            {
                if (_appState.IsConnected) return true;
                if (_appState.HasConnectionError)
                {
                    failureMessage = _appState.StatusMessage;
                    return false;
                }
                return null;
            });

            if (immediate is not null)
            {
                // The attempt already finished (fast success or fast failure);
                // the subscription is no longer needed.
                _appState.PropertyChanged -= handler;
                return immediate.Value
                    ? OperationResult.Ok()
                    : OperationResult.Fail(failureMessage ?? "Connection failed.");
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeoutCts.CancelAfter(ConnectionStateTimeoutMs);
            try
            {
                bool connected = await tcs.Task.WaitAsync(timeoutCts.Token);
                return connected
                    ? OperationResult.Ok()
                    : OperationResult.Fail(failureMessage ?? "Connection failed.");
            }
            catch (OperationCanceledException)
            {
                return OperationResult.Fail(
                    token.IsCancellationRequested
                        ? "Request was cancelled."
                        : "Connection attempt timed out.");
            }
            finally
            {
                // Always unsubscribe to prevent memory leaks.
                _appState.PropertyChanged -= handler;
            }
        }
        finally
        {
            _connectLock.Release();
        }
    }

    public async Task<OperationResult> DisconnectAsync(CancellationToken token)
    {
        await _connectLock.WaitAsync(token);
        try
        {
            bool initiated = false;
            await _dispatcher.InvokeAsync(() =>
            {
                if (_appState.IsConnected && _appState.DisconnectCommand.CanExecute(null))
                {
                    _appState.DisconnectCommand.Execute(null);
                    initiated = true;
                }
            });

            if (!initiated)
                return OperationResult.Fail("Already disconnected or cannot disconnect.");

            // Read the outcome state on the dispatcher (never from the API
            // thread): a fast disconnect may have completed before we could
            // subscribe to the notification.
            if (!await _dispatcher.InvokeAsync(() => _appState.IsConnected))
                return OperationResult.Ok();

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            PropertyChangedEventHandler? handler = null;
            handler = (_, e) =>
            {
                if (e.PropertyName == nameof(IAppStateAccessor.IsConnected) && !_appState.IsConnected)
                    tcs.TrySetResult(true);
            };
            _appState.PropertyChanged += handler;

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeoutCts.CancelAfter(ConnectionStateTimeoutMs);
            try
            {
                await tcs.Task.WaitAsync(timeoutCts.Token);
                return OperationResult.Ok();
            }
            catch (OperationCanceledException)
            {
                return OperationResult.Fail(
                    token.IsCancellationRequested
                        ? "Request was cancelled."
                        : "Disconnect timed out.");
            }
            finally
            {
                _appState.PropertyChanged -= handler;
            }
        }
        finally
        {
            _connectLock.Release();
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Modbus reads (concurrency handled by the Modbus service's own lock)
    // ──────────────────────────────────────────────────────────────────────────

    public Task<ushort[]?> ReadHoldingRegistersAsync(byte unitId, ushort address, ushort count, CancellationToken token)
        => ActiveModbusService?.ReadHoldingRegistersAsync(unitId, address, count) ?? Task.FromResult<ushort[]?>(null);

    public Task<bool[]?> ReadCoilsAsync(byte unitId, ushort address, ushort count, CancellationToken token)
        => ActiveModbusService?.ReadCoilsAsync(unitId, address, count) ?? Task.FromResult<bool[]?>(null);

    // ──────────────────────────────────────────────────────────────────────────
    // Custom tags
    // ──────────────────────────────────────────────────────────────────────────

    public Task<IReadOnlyList<CustomEntry>> GetCustomTagsAsync(CancellationToken token)
        => _dispatcher.InvokeAsync<IReadOnlyList<CustomEntry>>(
            () => _appState.CustomEntries.ToList());

    public async Task<CustomEntry> AddCustomTagAsync(CustomEntry entry, CancellationToken token)
    {
        await _dispatcher.InvokeAsync(() => _appState.CustomEntries.Add(entry));
        return entry;
    }

    public Task<bool> RemoveCustomTagAsync(int address, CancellationToken token)
        => _dispatcher.InvokeAsync(() =>
        {
            var entry = _appState.CustomEntries.FirstOrDefault(e => e.Address == address);
            if (entry is null) return false;
            _appState.CustomEntries.Remove(entry);
            return true;
        });

    // ──────────────────────────────────────────────────────────────────────────
    // Simulation nodes
    // ──────────────────────────────────────────────────────────────────────────

    public Task<IReadOnlyList<VisualNode>> GetSimulationNodesAsync(CancellationToken token)
        => _dispatcher.InvokeAsync<IReadOnlyList<VisualNode>>(
            () => _appState.SimulationNodes.ToList());

    public async Task<VisualNode> UpsertSimulationNodeAsync(VisualNode node, CancellationToken token)
    {
        await _dispatcher.InvokeAsync(() =>
        {
            var existing = _appState.SimulationNodes.FirstOrDefault(n => n.Id == node.Id);
            if (existing != null)
                _appState.SimulationNodes.Remove(existing);
            _appState.SimulationNodes.Add(node);
        });
        return node;
    }

    public Task<bool> RemoveSimulationNodeAsync(string id, CancellationToken token)
        => _dispatcher.InvokeAsync(() =>
        {
            var existing = _appState.SimulationNodes.FirstOrDefault(n => n.Id == id);
            if (existing is null) return false;
            _appState.SimulationNodes.Remove(existing);
            return true;
        });

    // ──────────────────────────────────────────────────────────────────────────
    // Script rules
    // ──────────────────────────────────────────────────────────────────────────

    public Task<IReadOnlyList<ScriptRule>> GetScriptRulesAsync(CancellationToken token)
        => _dispatcher.InvokeAsync<IReadOnlyList<ScriptRule>>(
            () => _scriptRuleService.Rules.ToList());

    public async Task<ScriptRule> UpsertScriptRuleAsync(ScriptRule rule, CancellationToken token)
    {
        await _dispatcher.InvokeAsync(() =>
        {
            var existing = _scriptRuleService.Rules.FirstOrDefault(r => r.Name == rule.Name);
            if (existing != null)
                _scriptRuleService.RemoveRule(existing);
            _scriptRuleService.AddRule(rule);
        });
        return rule;
    }

    public Task<bool> RemoveScriptRuleAsync(string name, CancellationToken token)
        => _dispatcher.InvokeAsync(() =>
        {
            var existing = _scriptRuleService.Rules.FirstOrDefault(r => r.Name == name);
            if (existing is null) return false;
            _scriptRuleService.RemoveRule(existing);
            return true;
        });

    // ──────────────────────────────────────────────────────────────────────────
    // Logs / Trends
    // ──────────────────────────────────────────────────────────────────────────

    public Task<IReadOnlyList<string>> GetLogsAsync(CancellationToken token)
        => _dispatcher.InvokeAsync<IReadOnlyList<string>>(
            () => _consoleLoggerService.LogMessages.ToList());

    public Task AddTrendAsync(string key, string displayName, CancellationToken token)
        => _dispatcher.InvokeAsync(() =>
            _trendLogger.Add(key, string.IsNullOrEmpty(displayName) ? key : displayName));
}
