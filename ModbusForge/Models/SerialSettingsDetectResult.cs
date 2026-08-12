using System;
using System.IO.Ports;

namespace ModbusForge.Avalonia.Models;

/// <summary>
/// Result of an auto-detect scan for serial Modbus settings.
/// </summary>
public sealed class SerialSettingsDetectResult
{
    public bool Found { get; init; }

    public int BaudRate { get; init; }

    public Parity Parity { get; init; }

    public int DataBits { get; init; }

    public StopBits StopBits { get; init; }

    public string Log { get; init; } = string.Empty;

    public string Summary { get; init; } = string.Empty;

    public override string ToString()
    {
        return Found
            ? $"{BaudRate}/{DataBits}/{ParityChar(Parity)}/{StopBitsChar(StopBits)}"
            : "not found";
    }

    private static char ParityChar(Parity parity) => parity switch
    {
        Parity.Even => 'E',
        Parity.Odd => 'O',
        Parity.Mark => 'M',
        Parity.Space => 'S',
        _ => 'N',
    };

    private static string StopBitsChar(StopBits stopBits) => stopBits switch
    {
        StopBits.One => "1",
        StopBits.OnePointFive => "1.5",
        StopBits.Two => "2",
        _ => "0",
    };
}
