using System.ComponentModel.DataAnnotations;
using ModbusForge.Models;

namespace ModbusForge.Services.Api.Dtos;

/// <summary>Request body for POST /api/simulation/nodes.</summary>
public sealed class CreateSimulationNodeRequest
{
    /// <summary>
    /// Optional stable identifier; a new GUID is generated if omitted.
    /// </summary>
    [MaxLength(64)]
    public string? Id { get; set; }

    [Required]
    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    public PlcElementType ElementType { get; set; } = PlcElementType.Input;

    [Range(0, 10000)]
    public double X { get; set; } = 100;

    [Range(0, 10000)]
    public double Y { get; set; } = 100;

    [Range(50, 2000)]
    public double Width { get; set; } = 240;

    [Range(50, 2000)]
    public double Height { get; set; } = 140;

    public bool IsEnabled { get; set; } = true;

    [MaxLength(32)]
    public string? Waveform { get; set; } = "Ramp";

    [Range(10, int.MaxValue)]
    public int PeriodMs { get; set; } = 1000;

    [Range(0, double.MaxValue)]
    public double Amplitude { get; set; } = 100;

    public double Offset { get; set; } = 0;
}
