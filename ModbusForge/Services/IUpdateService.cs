using System;
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
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Checks for newer application releases and opens release pages.
    /// </summary>
    public interface IUpdateService
    {
        Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Opens the given release URL in the user's default browser.
        /// </summary>
        void OpenReleasePage(string? url);
    }
}
