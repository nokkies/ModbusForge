using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModbusForge.Models;
using ModbusForge.Services.Api.Dtos;
using ModbusForge.ViewModels;

namespace ModbusForge.Services.Api;

/// <summary>
/// WPF-aware implementation of <see cref="IApiApplicationService"/>.
/// All access to WPF-owned objects is marshalled through <see cref="IDispatcher"/>.
/// Does NOT depend on <see cref="MainViewModel"/> directly; the constructor receives
/// narrow interfaces so the service can be tested without a full ViewModel.
/// </summary>
public sealed class WpfApiApplicationService : IApiApplicationService
{
    private const int ConnectionStateTimeoutMs = 30_000;

    private readonly IAppStateAccessor _appState;
    private readonly IModbusService _modbusService;
    private readonly IScriptRuleService _scriptRuleService;
    private readonly IConsoleLoggerService _consoleLoggerService;
    private readonly ITrendLogger _trendLogger;
    private readonly IDispatcher _dispatcher;
    private readonly ILogger<WpfApiApplicationService> _logger;

    // Serialise connect/disconnect so that concurrent API calls queue rather than race.
    private readonly SemaphoreSlim _connectLock = new(1, 1);

    public WpfApiApplicationService(
        IAppStateAccessor appState,
        IModbusService modbusService,
        IScriptRuleService scriptRuleService,
        IConsoleLoggerService consoleLoggerService,
        ITrendLogger trendLogger,
        IDispatcher dispatcher,
        ILogger<WpfApiApplicationService> logger)
    {
        _appState = appState ?? throw new ArgumentNullException(nameof(appState));
        _modbusService = modbusService ?? throw new ArgumentNullException(nameof(modbusService));
        _scriptRuleService = scriptRuleService ?? throw new ArgumentNullException(nameof(scriptRuleService));
        _consoleLoggerService = consoleLoggerService ?? throw new ArgumentNullException(nameof(consoleLoggerService));
        _trendLogger = trendLogger ?? throw new ArgumentNullException(nameof(trendLogger));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Status
    // ──────────────────────────────────────────────────────────────────────────

    public ApiStatus GetStatus()
    {
        // Marshal the read through the dispatcher for consistency with the
        // write-side calls, so WPF-owned state is never read from the API thread.
        return _dispatcher
            .InvokeAsync(() => new ApiStatus(_appState.IsConnected, _appState.Mode))
            .GetAwaiter()
            .GetResult();
    }

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

            if (_appState.IsConnected)
                return OperationResult.Ok();

            // Event-driven wait instead of polling.
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            PropertyChangedEventHandler? handler = null;
            handler = (_, e) =>
            {
                if (e.PropertyName == nameof(IAppStateAccessor.IsConnected) && _appState.IsConnected)
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

            if (!_appState.IsConnected)
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
        => _modbusService.ReadHoldingRegistersAsync(unitId, address, count);

    public Task<bool[]?> ReadCoilsAsync(byte unitId, ushort address, ushort count, CancellationToken token)
        => _modbusService.ReadCoilsAsync(unitId, address, count);

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
