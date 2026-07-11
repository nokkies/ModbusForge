using System.ComponentModel.DataAnnotations;

namespace ModbusForge.Services.Api.Dtos;

/// <summary>Request body for POST /api/scripts.</summary>
public sealed class CreateScriptRuleRequest
{
    [Required]
    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    /// <summary>RegisterValue | CoilsState | TimeBased</summary>
    [RegularExpression("^(RegisterValue|CoilsState|TimeBased)$")]
    public string ConditionType { get; set; } = "RegisterValue";

    [Range(0, 65535)]
    public int TriggerAddress { get; set; } = 1;

    /// <summary>HoldingRegister | Coil | InputRegister | DiscreteInput</summary>
    [RegularExpression("^(HoldingRegister|Coil|InputRegister|DiscreteInput)$")]
    public string TriggerArea { get; set; } = "HoldingRegister";

    /// <summary>Equals | NotEquals | GreaterThan | LessThan | GreaterThanOrEqual | LessThanOrEqual</summary>
    [RegularExpression("^(Equals|NotEquals|GreaterThan|LessThan|GreaterThanOrEqual|LessThanOrEqual)$")]
    public string TriggerOperator { get; set; } = "Equals";

    [MaxLength(64)]
    public string TriggerValue { get; set; } = "0";

    /// <summary>SetRegister | SetCoil | Delay | LogMessage</summary>
    [RegularExpression("^(SetRegister|SetCoil|Delay|LogMessage)$")]
    public string ActionType { get; set; } = "SetRegister";

    [Range(0, 65535)]
    public int ActionAddress { get; set; } = 1;

    [RegularExpression("^(HoldingRegister|Coil|InputRegister|DiscreteInput)$")]
    public string ActionArea { get; set; } = "HoldingRegister";

    [MaxLength(64)]
    public string ActionValue { get; set; } = "1";

    [Range(0, 3600000)]
    public int DelayMs { get; set; } = 1000;

    [MaxLength(256)]
    public string LogMessage { get; set; } = "Rule triggered";

    public bool OneTime { get; set; } = false;
}
