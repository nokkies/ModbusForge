using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModbusForge.Models;

namespace ModbusForge.Services;

public class ScriptRunner : IScriptRunner
{
    private readonly ILogger<ScriptRunner> _logger;
    private CancellationTokenSource? _cts;
    private int _running; // 0 = idle, 1 = running (Interlocked)

    public bool IsRunning => Volatile.Read(ref _running) == 1;

    public event EventHandler<ScriptExecutionEventArgs>? CommandExecuted;
    public event EventHandler<string>? LogMessage;
    public event EventHandler? ScriptStarted;
    public event EventHandler<bool>? ScriptCompleted;
    public event EventHandler? ScriptCancelled;

    public ScriptRunner(ILogger<ScriptRunner> logger)
    {
        _logger = logger;
    }

    public async Task RunScriptAsync(Script script, IModbusService modbusService, byte unitId, CancellationToken cancellationToken = default)
    {
        // The runner is a singleton and can be reached from the UI and the REST
        // API at the same time: claim it atomically instead of check-then-act.
        if (Interlocked.Exchange(ref _running, 1) == 1)
        {
            Log("Script is already running; request ignored");
            return;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _cts.Token;

        // A file-loaded script could carry RepeatCount 0; 0 would silently run
        // nothing, so clamp to a single pass.
        var repeats = Math.Max(1, script.RepeatCount);

        ScriptStarted?.Invoke(this, EventArgs.Empty);
        Log($"Starting script: {script.Name}");

        bool allSuccess = true;
        bool cancelled = false;

        try
        {
            for (int repeat = 0; repeat < repeats; repeat++)
            {
                if (token.IsCancellationRequested) break;

                if (repeats > 1)
                {
                    Log($"--- Repeat {repeat + 1} of {repeats} ---");
                }

                int i = 0;
                while (i < script.Commands.Count)
                {
                    if (token.IsCancellationRequested) break;

                    var cmd = script.Commands[i];
                    if (!cmd.IsEnabled)
                    {
                        Log($"Skipping disabled command: {cmd.DisplayText}");
                        i++;
                        continue;
                    }

                    if (cmd.CommandType == ScriptCommandType.Loop)
                    {
                        // A Loop command repeats the rest of the script (everything
                        // after this command) N times and then consumes it, so the
                        // main pass never runs the looped region a second time.
                        var (loopSuccess, loopResult) = await ExecuteLoopAsync(
                            cmd, i, script, modbusService, unitId, token, repeat + 1, repeats);

                        cmd.LastSuccess = loopSuccess;
                        cmd.LastResult = loopResult;

                        CommandExecuted?.Invoke(this, new ScriptExecutionEventArgs(
                            cmd, i, script.Commands.Count, loopSuccess, loopResult, repeat + 1, repeats));

                        if (!loopSuccess)
                        {
                            allSuccess = false;
                            if (script.StopOnError)
                            {
                                Log($"Script stopped due to error: {loopResult}");
                                break;
                            }
                        }

                        i = script.Commands.Count;
                        continue;
                    }

                    var (success, result) = await ExecuteCommandAsync(cmd, modbusService, unitId, token);

                    cmd.LastSuccess = success;
                    cmd.LastResult = result;

                    CommandExecuted?.Invoke(this, new ScriptExecutionEventArgs(
                        cmd, i, script.Commands.Count, success, result, repeat + 1, repeats));

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

                    i++;
                }

                if (!allSuccess && script.StopOnError) break;
            }
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
        {
            Log($"Script error: {ex.Message}");
            _logger.LogError(ex, "Script execution error");
            allSuccess = false;
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
            _cts?.Dispose();
            _cts = null;

            if (cancelled)
            {
                // A user stop is not a failure - the UI must say "stopped", not
                // "FAILED", or the user cannot tell the two apart.
                Log("Script stopped by user");
                ScriptCancelled?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                Log($"Script completed: {(allSuccess ? "SUCCESS" : "FAILED")}");
                ScriptCompleted?.Invoke(this, allSuccess);
            }
        }
    }

    /// <summary>
    /// Executes a Loop command: the rest of the script (all commands after the
    /// Loop) is run <c>LoopCount</c> times in total. The region is then consumed
    /// by the caller, so those commands are not run again on the main pass.
    /// </summary>
    private async Task<(bool success, string result)> ExecuteLoopAsync(
        ScriptCommand loopCmd, int loopIndex, Script script, IModbusService modbusService,
        byte unitId, CancellationToken token, int currentRepeat, int totalRepeats)
    {
        // Clamp like the script-level RepeatCount: a 0 in a saved file must not
        // silently run zero iterations.
        var loopCount = Math.Max(1, loopCmd.LoopCount);

        int regionStart = loopIndex + 1;
        int regionEnd = script.Commands.Count;
        var regionCount = regionEnd - regionStart;

        // A Loop inside the looped region would recurse without bound; fail
        // clearly instead of spinning.
        for (int k = regionStart; k < regionEnd; k++)
        {
            if (script.Commands[k].CommandType == ScriptCommandType.Loop)
            {
                Log("Nested loops are not supported");
                return (false, "Nested loops are not supported");
            }
        }

        Log($"Loop {loopCount}x: {regionCount} command(s)");

        bool allOk = true;
        for (int iteration = 1; iteration <= loopCount; iteration++)
        {
            if (loopCount > 1)
            {
                Log($"  Loop iteration {iteration} of {loopCount}");
            }

            for (int k = regionStart; k < regionEnd; k++)
            {
                var c = script.Commands[k];
                if (!c.IsEnabled)
                {
                    Log($"Skipping disabled command: {c.DisplayText}");
                    continue;
                }

                var (ok, res) = await ExecuteCommandAsync(c, modbusService, unitId, token);

                c.LastSuccess = ok;
                c.LastResult = res;

                CommandExecuted?.Invoke(this, new ScriptExecutionEventArgs(
                    c, k, script.Commands.Count, ok, res, currentRepeat, totalRepeats));

                if (!ok)
                {
                    allOk = false;
                    if (script.StopOnError)
                    {
                        // The caller reports the stop; just return the failure.
                        return (false, res);
                    }
                }

                // Apply the inter-command delay at iteration boundaries as well
                // so the spacing between executed commands stays uniform.
                bool moreInIteration = k < regionEnd - 1;
                bool moreIterations = iteration < loopCount;
                if (script.DelayBetweenCommandsMs > 0 && (moreInIteration || moreIterations))
                {
                    await Task.Delay(script.DelayBetweenCommandsMs, token);
                }
            }
        }

        var result = allOk
            ? $"Looped {loopCount}x: {regionCount} command(s)"
            : $"Looped {loopCount}x: {regionCount} command(s); some commands failed";
        return (allOk, result);
    }

    private async Task<(bool success, string result)> ExecuteCommandAsync(
        ScriptCommand cmd, IModbusService modbusService, byte unitId, CancellationToken token)
    {
        try
        {
            switch (cmd.CommandType)
            {
                case ScriptCommandType.ReadHoldingRegisters:
                    // A null result means the device sent no response - that is a
                    // FAILED read (and must trip StopOnError), not a successful one.
                    var holdingRegs = await modbusService.ReadHoldingRegistersAsync(unitId, cmd.Address, cmd.Count);
                    var holdingResult = holdingRegs != null ? string.Join(", ", holdingRegs) : "no response";
                    Log($"Read Holding Registers [{cmd.Address}..{cmd.Address + cmd.Count - 1}]: {holdingResult}");
                    return (holdingRegs != null, holdingResult);

                case ScriptCommandType.ReadInputRegisters:
                    var inputRegs = await modbusService.ReadInputRegistersAsync(unitId, cmd.Address, cmd.Count);
                    var inputResult = inputRegs != null ? string.Join(", ", inputRegs) : "no response";
                    Log($"Read Input Registers [{cmd.Address}..{cmd.Address + cmd.Count - 1}]: {inputResult}");
                    return (inputRegs != null, inputResult);

                case ScriptCommandType.ReadCoils:
                    var coils = await modbusService.ReadCoilsAsync(unitId, cmd.Address, cmd.Count);
                    var coilResult = coils != null ? string.Join(", ", Array.ConvertAll(coils, b => b ? "ON" : "OFF")) : "no response";
                    Log($"Read Coils [{cmd.Address}..{cmd.Address + cmd.Count - 1}]: {coilResult}");
                    return (coils != null, coilResult);

                case ScriptCommandType.ReadDiscreteInputs:
                    var discreteInputs = await modbusService.ReadDiscreteInputsAsync(unitId, cmd.Address, cmd.Count);
                    var discreteResult = discreteInputs != null ? string.Join(", ", Array.ConvertAll(discreteInputs, b => b ? "ON" : "OFF")) : "no response";
                    Log($"Read Discrete Inputs [{cmd.Address}..{cmd.Address + cmd.Count - 1}]: {discreteResult}");
                    return (discreteInputs != null, discreteResult);

                case ScriptCommandType.WriteSingleRegister:
                    await modbusService.WriteSingleRegisterAsync(unitId, cmd.Address, cmd.Value);
                    Log($"Write Register [{cmd.Address}] = {cmd.Value}");
                    return (true, $"Written: {cmd.Value}");

                case ScriptCommandType.WriteSingleCoil:
                    await modbusService.WriteSingleCoilAsync(unitId, cmd.Address, cmd.BoolValue);
                    Log($"Write Coil [{cmd.Address}] = {(cmd.BoolValue ? "ON" : "OFF")}");
                    return (true, $"Written: {(cmd.BoolValue ? "ON" : "OFF")}");

                case ScriptCommandType.WriteMultipleRegisters:
                    var valuesToWrite = cmd.ParseWriteValues()?.ToArray();
                    if (valuesToWrite == null || valuesToWrite.Length == 0)
                        return (false, "No write values provided");

                    await modbusService.WriteRegistersAsync(unitId, cmd.WriteStartAddress, valuesToWrite);
                    Log($"Write Registers [{cmd.WriteStartAddress}..{cmd.WriteStartAddress + valuesToWrite.Length - 1}] = {string.Join(", ", valuesToWrite)}");
                    return (true, $"Written {valuesToWrite.Length} registers");

                case ScriptCommandType.MaskWriteRegister:
                    var masked = await modbusService.MaskWriteRegisterAsync(unitId, cmd.Address, cmd.AndMask, cmd.OrMask);
                    var maskResult = masked.HasValue ? masked.Value.ToString(CultureInfo.InvariantCulture) : "null";
                    Log($"Mask Write Register [{cmd.Address}] AND={cmd.AndMask} OR={cmd.OrMask}: {maskResult}");
                    return (masked.HasValue, maskResult);

                case ScriptCommandType.ReadWriteMultipleRegisters:
                    var writeValues = cmd.ParseWriteValues()?.ToArray();
                    if (writeValues == null || writeValues.Length == 0)
                        return (false, "No write values provided");

                    var readWriteResult = await modbusService.ReadWriteMultipleRegistersAsync(
                        unitId, cmd.Address, cmd.Count, cmd.WriteStartAddress, writeValues);
                    var readWriteText = readWriteResult != null ? string.Join(", ", readWriteResult) : "null";
                    Log($"Read/Write Multiple: read {readWriteText}, wrote {writeValues.Length} regs to {cmd.WriteStartAddress}");
                    return (readWriteResult != null, readWriteText);

                case ScriptCommandType.ReadDeviceIdentification:
                    var deviceId = await modbusService.ReadDeviceIdentificationAsync(unitId, cmd.ObjectId, DeviceIdCategory.Basic);
                    var deviceIdText = deviceId != null ? FormatDeviceIdentification(deviceId) : "null";
                    Log($"Read Device Identification Object {cmd.ObjectId}: {deviceIdText}");
                    return (deviceId != null, deviceIdText);

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
        catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
        {
            Log($"Command failed: {ex.Message}");
            return (false, ex.Message);
        }
    }

    private static string FormatDeviceIdentification(DeviceIdentification deviceId)
    {
        return $"Vendor={deviceId.VendorName ?? ""}, Product={deviceId.ProductCode ?? ""}, Version={deviceId.MajorMinorRevision ?? ""}";
    }

    public void Stop()
    {
        if (IsRunning)
        {
            Log("Stopping script...");
            _cts?.Cancel();
        }
    }

    private void Log(string message)
    {
        var timestamped = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        LogMessage?.Invoke(this, timestamped);
        _logger.LogDebug(message);
    }
}
