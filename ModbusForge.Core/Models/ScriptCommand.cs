using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ModbusForge.Models;

public enum ScriptCommandType
{
    ReadHoldingRegisters,
    ReadInputRegisters,
    ReadCoils,
    ReadDiscreteInputs,
    WriteSingleRegister,
    WriteSingleCoil,
    Delay,
    Log,
    Loop
}

public partial class ScriptCommand : ObservableObject
{
    [ObservableProperty]
    private ScriptCommandType _commandType = ScriptCommandType.ReadHoldingRegisters;

    [ObservableProperty]
    private int _address = 1;

    [ObservableProperty]
    private int _count = 1;

    [ObservableProperty]
    private ushort _value;

    [ObservableProperty]
    private bool _boolValue;

    [ObservableProperty]
    private int _delayMs = 1000;

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private int _loopCount = 1;

    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    private string _lastResult = string.Empty;

    [ObservableProperty]
    private bool _lastSuccess;

    public string DisplayText
    {
        get
        {
            return CommandType switch
            {
                ScriptCommandType.ReadHoldingRegisters => $"Read {Count} Holding Register(s) from {Address}",
                ScriptCommandType.ReadInputRegisters => $"Read {Count} Input Register(s) from {Address}",
                ScriptCommandType.ReadCoils => $"Read {Count} Coil(s) from {Address}",
                ScriptCommandType.ReadDiscreteInputs => $"Read {Count} Discrete Input(s) from {Address}",
                ScriptCommandType.WriteSingleRegister => $"Write {Value} to Register {Address}",
                ScriptCommandType.WriteSingleCoil => $"Write {(BoolValue ? "ON" : "OFF")} to Coil {Address}",
                ScriptCommandType.Delay => $"Delay {DelayMs}ms",
                ScriptCommandType.Log => $"Log: {Message}",
                ScriptCommandType.Loop => $"Loop {LoopCount} times",
                _ => "Unknown"
            };
        }
    }

    public ScriptCommand Clone()
    {
        return new ScriptCommand
        {
            CommandType = CommandType,
            Address = Address,
            Count = Count,
            Value = Value,
            BoolValue = BoolValue,
            DelayMs = DelayMs,
            Message = Message,
            LoopCount = LoopCount,
            IsEnabled = IsEnabled
        };
    }
}
