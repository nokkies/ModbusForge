using System;
using System.Collections.Generic;
using System.Linq;
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
    WriteMultipleRegisters,
    MaskWriteRegister,
    ReadWriteMultipleRegisters,
    ReadDeviceIdentification,
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

    // Advanced function code parameters
    [ObservableProperty]
    private ushort _andMask;

    [ObservableProperty]
    private ushort _orMask;

    [ObservableProperty]
    private int _writeStartAddress = 1;

    [ObservableProperty]
    private string _writeValuesText = string.Empty;

    [ObservableProperty]
    private byte _objectId = 0;

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
                ScriptCommandType.WriteMultipleRegisters => $"Write [{WriteValuesText}] to Registers {WriteStartAddress}",
                ScriptCommandType.MaskWriteRegister => $"Mask Write Reg {Address} (AND {AndMask}, OR {OrMask})",
                ScriptCommandType.ReadWriteMultipleRegisters => $"Read {Count} from {Address}, Write [{WriteValuesText}] to {WriteStartAddress}",
                ScriptCommandType.ReadDeviceIdentification => $"Read Device Id Object {ObjectId}",
                ScriptCommandType.Delay => $"Delay {DelayMs}ms",
                ScriptCommandType.Log => $"Log: {Message}",
                ScriptCommandType.Loop => $"Loop {LoopCount} times",
                _ => "Unknown"
            };
        }
    }

    public IEnumerable<ushort>? ParseWriteValues()
    {
        if (string.IsNullOrWhiteSpace(WriteValuesText))
            return null;

        var values = new List<ushort>();
        foreach (var token in WriteValuesText.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (ushort.TryParse(token.Trim(), out var v))
                values.Add(v);
        }

        return values;
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
            IsEnabled = IsEnabled,
            AndMask = AndMask,
            OrMask = OrMask,
            WriteStartAddress = WriteStartAddress,
            WriteValuesText = WriteValuesText,
            ObjectId = ObjectId
        };
    }
}
