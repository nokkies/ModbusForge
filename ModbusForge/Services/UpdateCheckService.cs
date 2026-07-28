using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModbusForge.Models;

namespace ModbusForge.Services;

/// <summary>
/// Queries the GitHub Releases API for the latest ModbusForge release and
/// compares it with the currently running version.
/// </summary>
public class UpdateCheckService : IUpdateCheckService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<UpdateCheckService> _logger;

    private const string Owner = "nokkies";
    private const string Repository = "ModbusForge";
    private const string ApiEndpoint = $"https://api.github.com/repos/{Owner}/{Repository}/releases/latest";

    public UpdateCheckService(HttpClient httpClient, ILogger<UpdateCheckService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "ModbusForge-UpdateCheck");
        }
    }

    public async Task<UpdateCheckResult> CheckForUpdateAsync(string currentVersion, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(currentVersion))
        {
            return new UpdateCheckResult(false, currentVersion, string.Empty, string.Empty, "Current version is empty.");
        }

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, ApiEndpoint);
            request.Headers.Add("Accept", "application/vnd.github+json");

            var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

            var root = document.RootElement;
            var tagName = root.GetProperty("tag_name").GetString() ?? string.Empty;
            var htmlUrl = root.GetProperty("html_url").GetString() ?? string.Empty;

            var latestNormalized = NormalizeVersion(tagName);
            var currentNormalized = NormalizeVersion(currentVersion);

            if (!Version.TryParse(latestNormalized, out var latest))
            {
                _logger.LogWarning("Could not parse latest release version '{TagName}'", tagName);
                return new UpdateCheckResult(false, currentVersion, tagName, htmlUrl, $"Could not parse latest version '{tagName}'.");
            }

            if (!Version.TryParse(currentNormalized, out var current))
            {
                _logger.LogWarning("Could not parse current version '{CurrentVersion}'", currentVersion);
                return new UpdateCheckResult(false, currentVersion, tagName, htmlUrl, $"Could not parse current version '{currentVersion}'.");
            }

            var isUpdateAvailable = latest > current;

            _logger.LogInformation(
                "Update check complete: current {Current}, latest {Latest}, update available {IsUpdateAvailable}",
                current,
                latest,
                isUpdateAvailable);

            return new UpdateCheckResult(isUpdateAvailable, currentVersion, tagName, htmlUrl);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check for updates");
            return new UpdateCheckResult(false, currentVersion, string.Empty, string.Empty, ex.Message);
        }
    }

    internal static string NormalizeVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return string.Empty;

        var trimmed = version.Trim();

        // Strip a leading 'v' or 'V' (common GitHub tag prefix).
        if (trimmed.Length > 1 && (trimmed[0] == 'v' || trimmed[0] == 'V'))
        {
            trimmed = trimmed.Substring(1);
        }

        // GitHub release names / assembly informational versions sometimes include
        // '+<metadata>' (e.g. 5.8.12+0b0e429) or pre-release labels like '-beta'.
        // Version.TryParse cannot handle either, so keep only the numeric portion.
        var separatorIndex = trimmed.IndexOfAny(new[] { '-', '+' });
        if (separatorIndex >= 0)
        {
            trimmed = trimmed.Substring(0, separatorIndex);
        }

        return trimmed.Trim();
    }
}
