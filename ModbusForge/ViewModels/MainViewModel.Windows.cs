using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ModbusForge.Avalonia.Services;
using ModbusForge.Helpers;
using ModbusForge.Models;
using ModbusForge.Services;

namespace ModbusForge.Avalonia.ViewModels
{
    /// <summary>
    /// MainViewModel - Windows partial (split for navigability; behavior unchanged).
    /// </summary>
    public partial class MainViewModel
    {

        public ICommand OpenPreferencesCommand { get; }

        public ICommand OpenAboutCommand { get; }

        public ICommand OpenHelpCommand { get; }

        public ICommand OpenKeyboardShortcutsCommand { get; }

        public ICommand OpenTroubleshootingCommand { get; }

        public ICommand CheckForUpdatesCommand { get; }

        public ICommand ExitCommand { get; }

        public ICommand OpenTrendsCommand { get; }

        public ICommand OpenFrameInspectorCommand { get; }

        public ICommand OpenScriptEditorCommand { get; }

        public ICommand OpenPcapCommand { get; }

        public ICommand OpenTagBrowserCommand { get; }

        public ICommand OpenWatchWindowCommand { get; }

        public ICommand OpenConnectionManagerCommand { get; }


        private async Task CheckForUpdatesAsync()
        {
            if (_updateService == null) return;

            try
            {
                var result = await _updateService.CheckForUpdateAsync();
                if (result.IsUpdateAvailable)
                {
                    var msg = $"A newer version is available: {result.LatestVersion}\nCurrent: {result.CurrentVersion}\n\nDownload and install it now?";
                    if (_messageBoxService != null)
                    {
                        var dialogResult = await _messageBoxService.ShowAsync(msg, "Update Available", DialogButton.YesNo, DialogIcon.Question);
                        if (dialogResult == DialogResult.Yes)
                        {
                            if (string.IsNullOrWhiteSpace(result.AssetDownloadUrl))
                            {
                                OpenUrl(result.ReleaseUrl);
                                return;
                            }

                            var installerPath = Path.Combine(
                                Path.GetTempPath(),
                                $"ModbusForge-{result.LatestVersion}-setup.exe");
                            var progress = new Progress<double>(value =>
                            {
                                StatusMessage = $"Downloading update... {value:P0}";
                            });

                            StatusMessage = "Downloading update...";
                            var downloaded = await _updateService.DownloadInstallerAsync(
                                result.AssetDownloadUrl,
                                installerPath,
                                progress);
                            if (!downloaded)
                            {
                                StatusMessage = "Update download failed.";
                                return;
                            }

                            StatusMessage = "Launching update installer...";
                            _updateService.LaunchInstaller(installerPath);
                            _applicationLifetime?.Shutdown();
                        }
                    }
                    else
                    {
                        StatusMessage = $"Update available: {result.LatestVersion}";
                    }
                }
                else
                {
                    StatusMessage = $"Up to date ({result.CurrentVersion}).";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Update check failed: {ex.Message}";
                _logger.LogWarning(ex, "Update check failed");
            }
        }


        private void OpenUrl(string url)
        {
            // Only open absolute http(s) URLs - never an arbitrary string (which
            // UseShellExecute would hand to the shell as a document/protocol).
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                _logger.LogWarning("Refusing to open non-http(s) URL: {Url}", url);
                StatusMessage = "The update URL is invalid.";
                return;
            }

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to open URL {Url}", url);
                StatusMessage = $"Could not open the browser: {ex.Message}";
            }
        }

    }
}
