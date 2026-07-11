namespace ModbusForge.Services.Api.Dtos;

/// <summary>Snapshot of the application connection state returned by GET /api/app/status.</summary>
public sealed record ApiStatus(bool IsConnected, string Mode);
