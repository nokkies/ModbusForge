using System.Threading;
using System.Threading.Tasks;
using ModbusForge.Models;

namespace ModbusForge.Services;

/// <summary>
/// Checks GitHub for a newer ModbusForge release.
/// </summary>
public interface IUpdateCheckService
{
    /// <summary>
    /// Compares the running <paramref name="currentVersion"/> against the latest
    /// GitHub release and returns whether an update is available.
    /// </summary>
    Task<UpdateCheckResult> CheckForUpdateAsync(string currentVersion, CancellationToken cancellationToken = default);
}
