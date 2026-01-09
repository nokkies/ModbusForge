using System;
using System.Threading;
using System.Threading.Tasks;
using ModbusForge.Models;

namespace ModbusForge.Services;

public class ScriptExecutionEventArgs : EventArgs
{
    public ScriptCommand Command { get; }
    public int CommandIndex { get; }
    public int TotalCommands { get; }
    public bool Success { get; }
    public string Result { get; }
    public int CurrentRepeat { get; }
    public int TotalRepeats { get; }

    public ScriptExecutionEventArgs(ScriptCommand command, int index, int total, bool success, string result, int currentRepeat, int totalRepeats)
    {
        Command = command;
        CommandIndex = index;
        TotalCommands = total;
        Success = success;
        Result = result;
        CurrentRepeat = currentRepeat;
        TotalRepeats = totalRepeats;
    }
}

public interface IScriptRunner
{
    bool IsRunning { get; }
    
    event EventHandler<ScriptExecutionEventArgs>? CommandExecuted;
    event EventHandler<string>? LogMessage;
    event EventHandler? ScriptStarted;
    event EventHandler<bool>? ScriptCompleted;
    
    Task RunScriptAsync(Script script, IModbusService modbusService, byte unitId, CancellationToken cancellationToken = default);
    void Stop();
}
