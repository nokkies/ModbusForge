using System;
using System.Diagnostics;

namespace ModbusForge.Helpers
{
    public static class UrlHelper
    {
        public static void OpenUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new ArgumentException("URL cannot be null or empty.", nameof(url));
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
            {
                throw new ArgumentException("Invalid URL format.", nameof(url));
            }

            if (uri.Scheme != Uri.UriSchemeHttp &&
                uri.Scheme != Uri.UriSchemeHttps &&
                uri.Scheme != Uri.UriSchemeMailto)
            {
                throw new InvalidOperationException($"URL scheme '{uri.Scheme}' is not allowed.");
            }

            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
    }
}
