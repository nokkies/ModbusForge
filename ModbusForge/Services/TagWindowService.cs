using System;
using System.Windows;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModbusForge.Views;

namespace ModbusForge.Services
{
    /// <summary>
    /// Production implementation of <see cref="ITagWindowService"/>.
    /// Creates and shows TagBrowserWindow / WatchWindow, wiring the main
    /// window as owner via an injected <see cref="IWindowOwnerProvider"/>.
    /// </summary>
    public class TagWindowService : ITagWindowService
    {
        private readonly TagService _tagService;
        private readonly IDialogService _dialogService;
        private readonly IWindowOwnerProvider _ownerProvider;
        private readonly ILogger<TagWindowService> _logger;

        public TagWindowService(
            TagService tagService,
            IDialogService dialogService,
            IWindowOwnerProvider ownerProvider,
            ILogger<TagWindowService>? logger = null)
        {
            _tagService = tagService;
            _dialogService = dialogService;
            _ownerProvider = ownerProvider;
            _logger = logger ?? NullLogger<TagWindowService>.Instance;
        }

        public void ShowTagBrowser()
        {
            try
            {
                var browser = new TagBrowserWindow(_tagService, _dialogService);
                if (_ownerProvider.GetMainWindow() is Window owner)
                {
                    browser.Owner = owner;
                }
                browser.Show();
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                _logger.LogError(ex, "Failed to open tag browser");
                _dialogService.Show(
                    $"Error opening Tag Browser: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        public void ShowWatchWindow()
        {
            try
            {
                var watchWindow = new WatchWindow(_tagService, _dialogService);
                if (_ownerProvider.GetMainWindow() is Window owner)
                {
                    watchWindow.Owner = owner;
                }
                watchWindow.Show();
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                _logger.LogError(ex, "Failed to open watch window");
                _dialogService.Show(
                    $"Error opening Watch Window: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}
