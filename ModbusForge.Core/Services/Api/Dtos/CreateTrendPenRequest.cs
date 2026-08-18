using System.ComponentModel.DataAnnotations;

namespace ModbusForge.Services.Api.Dtos;

/// <summary>Request body for POST /api/trends/pens.</summary>
public sealed class CreateTrendPenRequest
{
    /// <summary>Pen name / series key. Optional - defaults to "HR Trend {address}" style.</summary>
    [MaxLength(128)]
    public string? Name { get; set; }

    [Range(0, 65535)]
    public int Address { get; set; }

    /// <summary>HoldingRegister | Coil | InputRegister | DiscreteInput</summary>
    [RegularExpression("^(HoldingRegister|Coil|InputRegister|DiscreteInput)$")]
    public string Area { get; set; } = "HoldingRegister";

    /// <summary>int | uint | real | string</summary>
    [RegularExpression("^(int|uint|real|string)$")]
    public string Type { get; set; } = "int";

    [Range(100, int.MaxValue)]
    public int ReadPeriodMs { get; set; } = 1000;
}
