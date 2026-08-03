using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModbusForge.Avalonia.ViewModels;
using ModbusForge.Avalonia.Views;
using ModbusForge.Services;

namespace ModbusForge.Avalonia.Services
{
    public sealed class AvaloniaTagWindowService : ITagWindowService
    {
        private readonly TagService _tagService;
        private readonly IRegisterTemplateImportService _registerTemplateImportService;
        private readonly IRegisterTemplateStore _registerTemplateStore;
        private readonly IFileDialogService _fileDialogService;
        private readonly IFileSystem _fileSystem;
        private readonly IMessageBoxService _messageBoxService;
        private readonly ILogger<AvaloniaTagWindowService> _logger;

        public AvaloniaTagWindowService(
            TagService tagService,
            IRegisterTemplateImportService registerTemplateImportService,
            IRegisterTemplateStore registerTemplateStore,
            IFileDialogService fileDialogService,
            IFileSystem fileSystem,
            IMessageBoxService messageBoxService,
            ILogger<AvaloniaTagWindowService>? logger = null)
        {
            _tagService = tagService ?? throw new ArgumentNullException(nameof(tagService));
            _registerTemplateImportService = registerTemplateImportService ?? throw new ArgumentNullException(nameof(registerTemplateImportService));
            _registerTemplateStore = registerTemplateStore ?? throw new ArgumentNullException(nameof(registerTemplateStore));
            _fileDialogService = fileDialogService ?? throw new ArgumentNullException(nameof(fileDialogService));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _messageBoxService = messageBoxService ?? throw new ArgumentNullException(nameof(messageBoxService));
            _logger = logger ?? NullLogger<AvaloniaTagWindowService>.Instance;
        }

        public void ShowTagBrowser()
        {
            try
            {
                var viewModel = CreateTagBrowserViewModel(selectionMode: false);
                var window = new TagBrowserWindow(viewModel);
                ShowWindow(window);
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
            {
                _logger.LogError(ex, "Failed to open the Avalonia tag browser");
                _ = ShowErrorAsync($"Error opening Tag Browser: {ex.Message}");
            }
        }

        public void ShowWatchWindow()
        {
            try
            {
                var window = new WatchWindow(
                    _tagService,
                    _registerTemplateImportService,
                    _registerTemplateStore,
                    _fileDialogService,
                    _fileSystem,
                    _messageBoxService);
                ShowWindow(window);
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
            {
                _logger.LogError(ex, "Failed to open the Avalonia watch window");
                _ = ShowErrorAsync($"Error opening Watch Window: {ex.Message}");
            }
        }

        private TagBrowserViewModel CreateTagBrowserViewModel(bool selectionMode) => new(
            _tagService,
            _registerTemplateImportService,
            _registerTemplateStore,
            _fileDialogService,
            _fileSystem,
            _messageBoxService,
            logger: null,
            selectionMode: selectionMode);

        private static void ShowWindow(Window window)
        {
            if (GetOwner() is { } owner)
                window.Show(owner);
            else
                window.Show();
        }

        private static Window? GetOwner()
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                return desktop.MainWindow;

            return null;
        }

        private async System.Threading.Tasks.Task ShowErrorAsync(string message) =>
            await _messageBoxService.ShowAsync(message, "Error", DialogButton.Ok, DialogIcon.Error);
    }
}
