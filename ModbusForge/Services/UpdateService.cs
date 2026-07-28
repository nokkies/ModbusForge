using System;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ModbusForge.Services
{
    public class UpdateService : IUpdateService
    {
        private const string GitHubReleasesApi = "https://api.github.com/repos/nokkies/ModbusForge/releases/latest";
        private readonly ILogger<UpdateService> _logger;

        public UpdateService(ILogger<UpdateService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken cancellationToken = default)
        {
            var currentVersion = GetCurrentVersion();

            try
            {
                using var client = CreateHttpClient();
                var response = await client.GetAsync(GitHubReleasesApi, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var latestVersion = ParseLatestVersion(json);
                var releaseUrl = ParseReleaseUrl(json);
                var releaseNotes = ParseReleaseNotes(json);

                if (latestVersion == null)
                {
                    _logger.LogWarning("Could not parse latest release version from GitHub");
                    return new UpdateCheckResult { ErrorMessage = "Could not determine the latest release version." };
                }

                var isUpdateAvailable = latestVersion > currentVersion;

                _logger.LogInformation(
                    "Update check: current {CurrentVersion}, latest {LatestVersion}, update available: {IsUpdateAvailable}",
                    currentVersion,
                    latestVersion,
                    isUpdateAvailable);

                return new UpdateCheckResult
                {
                    IsUpdateAvailable = isUpdateAvailable,
                    LatestVersion = latestVersion,
                    ReleaseUrl = releaseUrl,
                    ReleaseNotes = releaseNotes
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check for application updates");
                return new UpdateCheckResult { ErrorMessage = $"Unable to check for updates: {ex.Message}" };
            }
        }

        public void OpenReleasePage(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;

            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to open release page {Url}", url);
            }
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "ModbusForge-UpdateCheck");
            client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
            client.Timeout = TimeSpan.FromSeconds(15);
            return client;
        }

        private static Version GetCurrentVersion()
        {
            var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            return assembly.GetName().Version ?? new Version(1, 0, 0, 0);
        }

        private static Version? ParseLatestVersion(string json)
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.TryGetProperty("tag_name", out var tagProperty))
                {
                    var tag = tagProperty.GetString() ?? string.Empty;
                    return ParseVersionFromTag(tag);
                }
            }
            catch (Exception)
            {
                // Fall through and return null
            }

            return null;
        }

        private static Version? ParseVersionFromTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return null;

            // Tags are typically 'v5.8.12' or '5.8.12.0'
            var versionString = tag.Trim().ToLowerInvariant();
            if (versionString.StartsWith("v", StringComparison.Ordinal))
            {
                versionString = versionString.Substring(1);
            }

            // Strip pre-release/build metadata so Version can parse it
            var separatorIndex = versionString.IndexOfAny(new[] { '-', '+' });
            if (separatorIndex >= 0)
            {
                versionString = versionString.Substring(0, separatorIndex);
            }

            if (Version.TryParse(versionString, out var version))
            {
                return version;
            }

            return null;
        }

        private static string? ParseReleaseUrl(string json)
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.TryGetProperty("html_url", out var urlProperty))
                {
                    return urlProperty.GetString();
                }
            }
            catch (Exception)
            {
                // Fall through
            }

            return null;
        }

        private static string? ParseReleaseNotes(string json)
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.TryGetProperty("body", out var bodyProperty))
                {
                    return bodyProperty.GetString();
                }
            }
            catch (Exception)
            {
                // Fall through
            }

            return null;
        }
    }
}
