namespace ModbusForge.Services.Api.Dtos;

/// <summary>Generic outcome of a connect / disconnect operation.</summary>
public sealed record OperationResult(bool Success, string? Error = null)
{
    public static OperationResult Ok() => new(true);
    public static OperationResult Fail(string error) => new(false, error);
}
