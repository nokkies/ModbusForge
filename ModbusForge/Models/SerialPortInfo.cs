namespace ModbusForge.Avalonia.Models;

/// <summary>
/// Represents a serial port entry for the Connection Manager dropdown.
/// </summary>
public sealed class SerialPortInfo
{
    public string PortName { get; }

    public string Description { get; }

    public string DisplayName { get; }

    public bool IsCustom { get; }

    public SerialPortInfo(string portName, string? description = null, bool isCustom = false)
    {
        PortName = portName;
        Description = description ?? string.Empty;
        IsCustom = isCustom;

        if (IsCustom)
        {
            DisplayName = portName;
        }
        else if (!string.IsNullOrWhiteSpace(Description))
        {
            DisplayName = $"{PortName} ({Description})";
        }
        else
        {
            DisplayName = PortName;
        }
    }

    public override string ToString() => DisplayName;
}
