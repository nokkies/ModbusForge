using System.ComponentModel.DataAnnotations;

namespace ModbusForge.Services.Api.Dtos;

/// <summary>Request body for POST /api/custom-tags.</summary>
public sealed class CreateCustomEntryRequest
{
    [Required]
    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    [Range(0, 65535)]
    public int Address { get; set; }

    /// <summary>uint | int | real</summary>
    [RegularExpression("^(uint|int|real)$")]
    public string Type { get; set; } = "uint";

    [MaxLength(64)]
    public string Value { get; set; } = "0";

    public bool Continuous { get; set; } = false;

    [Range(100, int.MaxValue)]
    public int PeriodMs { get; set; } = 1000;

    public bool Monitor { get; set; } = false;

    [Range(100, int.MaxValue)]
    public int ReadPeriodMs { get; set; } = 1000;

    /// <summary>HoldingRegister | Coil | InputRegister | DiscreteInput</summary>
    [RegularExpression("^(HoldingRegister|Coil|InputRegister|DiscreteInput)$")]
    public string Area { get; set; } = "HoldingRegister";

    public bool Trend { get; set; } = false;
}
