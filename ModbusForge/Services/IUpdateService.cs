using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ModbusForge.Services
{
    /// <summary>
    /// Result of an update check against the GitHub releases API.
    /// </summary>
    public class UpdateCheckResult
    {
        public bool IsUpdateAvailable { get; set; }
        public Version? LatestVersion { get; set; }
        public string? ReleaseUrl { get; set; }
        public string? ReleaseNotes { get; set; }
        public string? AssetDownloadUrl { get; set; }
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Checks for newer application releases and can download/install updates.
    /// </summary>
    public interface IUpdateService
    {
        Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Downloads the installer for the given release to the destination path.
        /// </summary>
        Task<bool> DownloadInstallerAsync(string downloadUrl, string destinationPath, IProgress<double>? progress = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Launches the downloaded installer and shuts down the current application.
        /// </summary>
        void LaunchInstaller(string installerPath, bool silent = true);

        /// <summary>
        /// Opens the given release URL in the user's default browser.
        /// </summary>
        void OpenReleasePage(string? url);
    }
}
