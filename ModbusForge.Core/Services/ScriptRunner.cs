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
        catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
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
                // A null read means the device did not respond - report it as a failure,
                // not as a successful "null" read.
                case ScriptCommandType.ReadHoldingRegisters:
                    var holdingRegs = await modbusService.ReadHoldingRegistersAsync(unitId, cmd.Address, cmd.Count);
                    if (holdingRegs is null)
                        return (false, "No response from device");
                    var holdingResult = string.Join(", ", holdingRegs);
                    Log($"Read Holding Registers [{cmd.Address}..{cmd.Address + cmd.Count - 1}]: {holdingResult}");
                    return (true, holdingResult);

                case ScriptCommandType.ReadInputRegisters:
                    var inputRegs = await modbusService.ReadInputRegistersAsync(unitId, cmd.Address, cmd.Count);
                    if (inputRegs is null)
                        return (false, "No response from device");
                    var inputResult = string.Join(", ", inputRegs);
                    Log($"Read Input Registers [{cmd.Address}..{cmd.Address + cmd.Count - 1}]: {inputResult}");
                    return (true, inputResult);

                case ScriptCommandType.ReadCoils:
                    var coils = await modbusService.ReadCoilsAsync(unitId, cmd.Address, cmd.Count);
                    if (coils is null)
                        return (false, "No response from device");
                    var coilResult = string.Join(", ", Array.ConvertAll(coils, b => b ? "ON" : "OFF"));
                    Log($"Read Coils [{cmd.Address}..{cmd.Address + cmd.Count - 1}]: {coilResult}");
                    return (true, coilResult);

                case ScriptCommandType.ReadDiscreteInputs:
                    var discreteInputs = await modbusService.ReadDiscreteInputsAsync(unitId, cmd.Address, cmd.Count);
                    if (discreteInputs is null)
                        return (false, "No response from device");
                    var discreteResult = string.Join(", ", Array.ConvertAll(discreteInputs, b => b ? "ON" : "OFF"));
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
