using System;
using System.Diagnostics;

namespace ModbusForge.Helpers
{
    /// <summary>
    /// Helper class for safely opening URLs and preventing command injection or open redirects.
    /// </summary>
    public static class UrlHelper
    {
        /// <summary>
        /// Validates and opens the specified URL using the system's default handler.
        /// </summary>
        /// <param name="url">The URL to open.</param>
        public static void OpenUrl(string? url)
        {
            if (IsValidUrl(url))
            {
                Process.Start(new ProcessStartInfo(url!) { UseShellExecute = true });
            }
        }

        /// <summary>
        /// Validates if a URL is an absolute URI and uses an allowed scheme (http, https, or mailto).
        /// </summary>
        /// <param name="url">The URL to validate.</param>
        /// <returns>True if the URL is valid and safe to open; otherwise, false.</returns>
        public static bool IsValidUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return false;

            return uri.Scheme == Uri.UriSchemeHttp ||
                   uri.Scheme == Uri.UriSchemeHttps ||
                   uri.Scheme == "mailto";
        }
    }
}
