using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Controls.ApplicationLifetimes;
using global::Avalonia.Platform.Storage;
using ModbusForge.Services;

namespace ModbusForge.Avalonia.Services
{
    /// <summary>
    /// Avalonia file dialog implementation backed by the TopLevel StorageProvider.
    /// </summary>
    public sealed class AvaloniaFileDialogService : IFileDialogService
    {
        public string? ShowOpenFileDialog(string title, string filter)
        {
            // Avalonia file pickers are async; the sync overload is not available.
            return null;
        }

        public string? ShowSaveFileDialog(string title, string filter, string defaultFileName)
        {
            // Avalonia file pickers are async; the sync overload is not available.
            return null;
        }

        public async Task<string?> ShowOpenFileDialogAsync(string title, string filter)
        {
            var provider = GetStorageProvider();
            if (provider == null) return null;

            var options = new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter = ParseFilter(filter) as IReadOnlyList<FilePickerFileType>
            };

            var files = await provider.OpenFilePickerAsync(options);
            return files?.FirstOrDefault()?.TryGetLocalPath();
        }

        public async Task<string?> ShowSaveFileDialogAsync(string title, string filter, string defaultFileName)
        {
            var provider = GetStorageProvider();
            if (provider == null) return null;

            var fileTypes = ParseFilter(filter);
            var options = new FilePickerSaveOptions
            {
                Title = title,
                SuggestedFileName = defaultFileName,
                FileTypeChoices = fileTypes
            };

            var file = await provider.SaveFilePickerAsync(options);
            return file?.TryGetLocalPath();
        }

        private static IStorageProvider? GetStorageProvider()
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow is { } window)
            {
                return window.StorageProvider;
            }

            return null;
        }

        private static IReadOnlyList<FilePickerFileType>? ParseFilter(string filter)
        {
            if (string.IsNullOrWhiteSpace(filter))
            {
                return null;
            }

            var parts = filter.Split('|');
            var types = new List<FilePickerFileType>();

            for (int i = 0; i + 1 < parts.Length; i += 2)
            {
                var name = parts[i].Trim();
                var pattern = parts[i + 1].Trim();

                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(pattern))
                {
                    continue;
                }

                var patterns = pattern.Split(';', StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Trim())
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .ToList();

                if (patterns.Count == 0)
                {
                    patterns.Add("*.*");
                }

                types.Add(new FilePickerFileType(name) { Patterns = patterns });
            }

            return types.Count > 0 ? types : null;
        }
    }
}
