namespace ModbusForge.Models;

/// <summary>
/// Result of checking whether a newer ModbusForge release is available on GitHub.
/// </summary>
public record UpdateCheckResult(
    bool IsUpdateAvailable,
    string CurrentVersion,
    string LatestVersion,
    string ReleaseUrl,
    string? ErrorMessage = null);
