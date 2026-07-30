using System;
using System.Threading;
using System.Threading.Tasks;

namespace ModbusForge.Services
{
    /// <summary>
    /// Checks whether a newer version of the application is available.
    /// </summary>
    public interface IUpdateService
    {
        Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken cancellationToken = default);

        void OpenReleasePage(string? url);

        Task<bool> DownloadInstallerAsync(string downloadUrl, string destinationPath, IProgress<double>? progress = null, CancellationToken cancellationToken = default);

        void LaunchInstaller(string installerPath, bool silent = true);
    }

    public sealed class UpdateCheckResult
    {
        public bool IsUpdateAvailable { get; set; }
        public string CurrentVersion { get; set; } = string.Empty;
        public string LatestVersion { get; set; } = string.Empty;
        public string ReleaseUrl { get; set; } = string.Empty;
        public string? ReleaseNotes { get; set; }
        public string? AssetDownloadUrl { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
