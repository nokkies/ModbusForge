using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModbusForge.Models;

namespace ModbusForge.Services;

public class ScriptRunner : IScriptRunner
{
    private readonly ILogger<ScriptRunner> _logger;
    private CancellationTokenSource? _cts;
    private bool _isRunning;

    public bool IsRunning => _isRunning;

    public event EventHandler<ScriptExecutionEventArgs>? CommandExecuted;
    public event EventHandler<string>? LogMessage;
    public event EventHandler? ScriptStarted;
    public event EventHandler<bool>? ScriptCompleted;

    public ScriptRunner(ILogger<ScriptRunner> logger)
    {
        _logger = logger;
    }

    public async Task RunScriptAsync(Script script, IModbusService modbusService, byte unitId, CancellationToken cancellationToken = default)
    {
        if (_isRunning)
        {
            Log("Script is already running");
            return;
        }

        _isRunning = true;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _cts.Token;

        ScriptStarted?.Invoke(this, EventArgs.Empty);
        Log($"Starting script: {script.Name}");

        bool allSuccess = true;

        try
        {
            for (int repeat = 0; repeat < script.RepeatCount; repeat++)
            {
                if (token.IsCancellationRequested) break;

                if (script.RepeatCount > 1)
                {
                    Log($"--- Repeat {repeat + 1} of {script.RepeatCount} ---");
                }

                for (int i = 0; i < script.Commands.Count; i++)
                {
                    if (token.IsCancellationRequested) break;

                    var cmd = script.Commands[i];
                    if (!cmd.IsEnabled)
                    {
                        Log($"Skipping disabled command: {cmd.DisplayText}");
                        continue;
                    }

                    var (success, result) = await ExecuteCommandAsync(cmd, modbusService, unitId, token);
                    
                    cmd.LastSuccess = success;
                    cmd.LastResult = result;

                    CommandExecuted?.Invoke(this, new ScriptExecutionEventArgs(
                        cmd, i, script.Commands.Count, success, result, repeat + 1, script.RepeatCount));

                    if (!success)
                    {
                        allSuccess = false;
                        if (script.StopOnError)
                        {
                            Log($"Script stopped due to error: {result}");
                            break;
                        }
                    }

                    if (script.DelayBetweenCommandsMs > 0 && i < script.Commands.Count - 1)
                    {
                        await Task.Delay(script.DelayBetweenCommandsMs, token);
                    }
                }

                if (!allSuccess && script.StopOnError) break;
            }
        }
        catch (OperationCanceledException)
        {
            Log("Script cancelled");
            allSuccess = false;
        }
        catch (Exception ex)
        {
            Log($"Script error: {ex.Message}");
            _logger.LogError(ex, "Script execution error");
            allSuccess = false;
        }
        finally
        {
            _isRunning = false;
            _cts?.Dispose();
            _cts = null;
            Log($"Script completed: {(allSuccess ? "SUCCESS" : "FAILED")}");
            ScriptCompleted?.Invoke(this, allSuccess);
        }
    }

    private async Task<(bool success, string result)> ExecuteCommandAsync(
        ScriptCommand cmd, IModbusService modbusService, byte unitId, CancellationToken token)
    {
        try
        {
            switch (cmd.CommandType)
            {
                case ScriptCommandType.ReadHoldingRegisters:
                    var holdingRegs = await modbusService.ReadHoldingRegistersAsync(unitId, cmd.Address, cmd.Count);
                    var holdingResult = holdingRegs != null ? string.Join(", ", holdingRegs) : "null";
                    Log($"Read Holding Registers [{cmd.Address}..{cmd.Address + cmd.Count - 1}]: {holdingResult}");
                    return (true, holdingResult);

                case ScriptCommandType.ReadInputRegisters:
                    var inputRegs = await modbusService.ReadInputRegistersAsync(unitId, cmd.Address, cmd.Count);
                    var inputResult = inputRegs != null ? string.Join(", ", inputRegs) : "null";
                    Log($"Read Input Registers [{cmd.Address}..{cmd.Address + cmd.Count - 1}]: {inputResult}");
                    return (true, inputResult);

                case ScriptCommandType.ReadCoils:
                    var coils = await modbusService.ReadCoilsAsync(unitId, cmd.Address, cmd.Count);
                    var coilResult = coils != null ? string.Join(", ", Array.ConvertAll(coils, b => b ? "ON" : "OFF")) : "null";
                    Log($"Read Coils [{cmd.Address}..{cmd.Address + cmd.Count - 1}]: {coilResult}");
                    return (true, coilResult);

                case ScriptCommandType.ReadDiscreteInputs:
                    var discreteInputs = await modbusService.ReadDiscreteInputsAsync(unitId, cmd.Address, cmd.Count);
                    var discreteResult = discreteInputs != null ? string.Join(", ", Array.ConvertAll(discreteInputs, b => b ? "ON" : "OFF")) : "null";
                    Log($"Read Discrete Inputs [{cmd.Address}..{cmd.Address + cmd.Count - 1}]: {discreteResult}");
                    return (true, discreteResult);

                case ScriptCommandType.WriteSingleRegister:
                    await modbusService.WriteSingleRegisterAsync(unitId, cmd.Address, cmd.Value);
                    Log($"Write Register [{cmd.Address}] = {cmd.Value}");
                    return (true, $"Written: {cmd.Value}");

                case ScriptCommandType.WriteSingleCoil:
                    await modbusService.WriteSingleCoilAsync(unitId, cmd.Address, cmd.BoolValue);
                    Log($"Write Coil [{cmd.Address}] = {(cmd.BoolValue ? "ON" : "OFF")}");
                    return (true, $"Written: {(cmd.BoolValue ? "ON" : "OFF")}");

                case ScriptCommandType.Delay:
                    Log($"Delay {cmd.DelayMs}ms");
                    await Task.Delay(cmd.DelayMs, token);
                    return (true, $"Delayed {cmd.DelayMs}ms");

                case ScriptCommandType.Log:
                    Log(cmd.Message);
                    return (true, cmd.Message);

                default:
                    return (false, "Unknown command type");
            }
        }
        catch (Exception ex)
        {
            Log($"Command failed: {ex.Message}");
            return (false, ex.Message);
        }
    }

    public void Stop()
    {
        if (_isRunning && _cts != null)
        {
            Log("Stopping script...");
            _cts.Cancel();
        }
    }

    private void Log(string message)
    {
        var timestamped = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        LogMessage?.Invoke(this, timestamped);
        _logger.LogDebug(message);
    }
}
