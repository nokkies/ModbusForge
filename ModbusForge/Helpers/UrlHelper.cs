using System;
using System.Diagnostics;

namespace ModbusForge.Helpers
{
    /// <summary>
    /// Helper class for URL validation and safe opening.
    /// </summary>
    public static class UrlHelper
    {
        /// <summary>
        /// Validates if a URL is safe to open via Process.Start.
        /// Only allows http, https, and mailto schemes.
        /// </summary>
        /// <param name="url">The URL to validate.</param>
        /// <returns>True if the URL is considered safe; otherwise, false.</returns>
        public static bool IsSafeUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return false;

            return uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                   uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                   uri.Scheme.Equals("mailto", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Opens a URL in the default browser safely after validation.
        /// </summary>
        /// <param name="url">The URL to open.</param>
        /// <exception cref="ArgumentException">Thrown if the URL is not safe.</exception>
        public static void OpenUrl(string? url)
        {
            if (!IsSafeUrl(url))
            {
                throw new ArgumentException("The provided URL is not safe to open.", nameof(url));
            }

            Process.Start(new ProcessStartInfo(url!) { UseShellExecute = true });
        }
    }
}
