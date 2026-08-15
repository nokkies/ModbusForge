using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
                var latestVersionRaw = ParseLatestVersion(json);
                var releaseUrl = ParseReleaseUrl(json);
                var releaseNotes = ParseReleaseNotes(json);
                var assetDownloadUrl = ParseAssetDownloadUrl(json, latestVersionRaw);

                if (latestVersionRaw == null)
                {
                    _logger.LogWarning("Could not parse latest release version from GitHub");
                    return new UpdateCheckResult { CurrentVersion = currentVersion.ToString(), ErrorMessage = "Could not determine the latest release version." };
                }

                var currentVersionNormalized = NormalizeVersion(currentVersion);
                var latestVersionNormalized = NormalizeVersion(latestVersionRaw);
                var isUpdateAvailable = latestVersionNormalized > currentVersionNormalized;

                _logger.LogInformation(
                    "Update check: current {CurrentVersion}, latest {LatestVersion}, update available: {IsUpdateAvailable}",
                    currentVersion,
                    latestVersionRaw,
                    isUpdateAvailable);

                return new UpdateCheckResult
                {
                    IsUpdateAvailable = isUpdateAvailable,
                    CurrentVersion = currentVersion.ToString(),
                    LatestVersion = latestVersionRaw?.ToString() ?? string.Empty,
                    ReleaseUrl = releaseUrl ?? string.Empty,
                    ReleaseNotes = releaseNotes,
                    AssetDownloadUrl = assetDownloadUrl
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check for application updates");
                return new UpdateCheckResult { CurrentVersion = currentVersion.ToString(), ErrorMessage = $"Unable to check for updates: {ex.Message}" };
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

        public async Task<bool> DownloadInstallerAsync(string downloadUrl, string destinationPath, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(downloadUrl) || string.IsNullOrWhiteSpace(destinationPath))
            {
                _logger.LogWarning("DownloadInstallerAsync called with missing URL or destination path");
                return false;
            }

            try
            {
                var directory = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using var client = CreateHttpClient();
                using var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);

                var buffer = new byte[8192];
                long readBytes = 0;
                int bytesRead;
                while ((bytesRead = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
                    readBytes += bytesRead;

                    if (totalBytes > 0 && progress != null)
                    {
                        progress.Report(readBytes / (double)totalBytes);
                    }
                }

                _logger.LogInformation("Downloaded installer to {DestinationPath}", destinationPath);
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to download installer from {DownloadUrl}", downloadUrl);
                return false;
            }
        }

        public void LaunchInstaller(string installerPath, bool silent = true)
        {
            if (string.IsNullOrWhiteSpace(installerPath) || !File.Exists(installerPath))
            {
                _logger.LogWarning("LaunchInstaller called with missing or non-existent installer {InstallerPath}", installerPath);
                return;
            }

            try
            {
                var arguments = silent ? "/SILENT /NORESTART" : string.Empty;
                _logger.LogInformation("Launching installer {InstallerPath} with arguments {Arguments}", installerPath, arguments);
                Process.Start(new ProcessStartInfo(installerPath, arguments) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to launch installer {InstallerPath}", installerPath);
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
            return NormalizeVersion(assembly.GetName().Version);
        }

        private static Version NormalizeVersion(Version? version)
        {
            if (version == null)
                return new Version(1, 0, 0, 0);

            var build = version.Build >= 0 ? version.Build : 0;
            var revision = version.Revision >= 0 ? version.Revision : 0;
            return new Version(version.Major, version.Minor, build, revision);
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

        internal static string? ParseAssetDownloadUrl(string json, Version? version)
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.TryGetProperty("assets", out var assetsProperty) &&
                    assetsProperty.ValueKind == JsonValueKind.Array)
                {
                    string? firstInstallerUrl = null;

                    foreach (var asset in assetsProperty.EnumerateArray())
                    {
                        if (asset.TryGetProperty("name", out var nameProperty) &&
                            asset.TryGetProperty("browser_download_url", out var urlProperty))
                        {
                            var name = nameProperty.GetString() ?? string.Empty;
                            var url = urlProperty.GetString();

                            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                                continue;

                            if (firstInstallerUrl is null)
                                firstInstallerUrl = url;

                            // Prefer an asset whose filename contains the release version to avoid
                            // stale installers (e.g. old ModbusForge-2.0.2-setup.exe attached to v6.0.8).
                            if (version is not null &&
                                name.Contains(version.ToString(), StringComparison.OrdinalIgnoreCase))
                            {
                                return url;
                            }
                        }
                    }

                    return firstInstallerUrl;
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
