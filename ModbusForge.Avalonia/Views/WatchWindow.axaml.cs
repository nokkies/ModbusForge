using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ModbusForge.Avalonia.ViewModels;
using ModbusForge.Services;

namespace ModbusForge.Avalonia.Views
{
    public partial class WatchWindow : Window
    {
        private WatchViewModel? _viewModel;
        private readonly IRegisterTemplateImportService? _registerTemplateImportService;
        private readonly IRegisterTemplateStore? _registerTemplateStore;
        private readonly IFileDialogService? _fileDialogService;
        private readonly IFileSystem? _fileSystem;
        private readonly IMessageBoxService? _messageBoxService;
        private bool _initialized;

        public WatchWindow()
        {
            _registerTemplateImportService = null;
            _registerTemplateStore = null;
            _fileDialogService = null;
            _fileSystem = null;
            _messageBoxService = null;
            InitializeComponent();
        }

        public WatchWindow(
            TagService tagService,
            IRegisterTemplateImportService registerTemplateImportService,
            IRegisterTemplateStore registerTemplateStore,
            IFileDialogService fileDialogService,
            IFileSystem fileSystem,
            IMessageBoxService messageBoxService)
            : this(new WatchViewModel(tagService, messageBoxService), registerTemplateImportService, registerTemplateStore,
                fileDialogService, fileSystem, messageBoxService)
        {
        }

        public WatchWindow(
            WatchViewModel viewModel,
            IRegisterTemplateImportService? registerTemplateImportService = null,
            IRegisterTemplateStore? registerTemplateStore = null,
            IFileDialogService? fileDialogService = null,
            IFileSystem? fileSystem = null,
            IMessageBoxService? messageBoxService = null)
            : this()
        {
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            _registerTemplateImportService = registerTemplateImportService;
            _registerTemplateStore = registerTemplateStore;
            _fileDialogService = fileDialogService;
            _fileSystem = fileSystem;
            _messageBoxService = messageBoxService ?? viewModel.MessageBoxService;
            DataContext = viewModel;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            if (_viewModel != null)
                _viewModel.RequestTagSelection -= ViewModel_RequestTagSelection;

            base.OnDataContextChanged(e);
            _viewModel = DataContext as WatchViewModel;

            if (_viewModel != null)
                _viewModel.RequestTagSelection += ViewModel_RequestTagSelection;
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            if (!_initialized && _viewModel != null)
            {
                _initialized = true;
                _ = _viewModel.InitializeAsync();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_viewModel != null)
                _viewModel.RequestTagSelection -= ViewModel_RequestTagSelection;
            _viewModel?.Dispose();
            base.OnClosed(e);
        }

        private async void ViewModel_RequestTagSelection(object? sender, EventArgs e)
        {
            if (_viewModel == null || _registerTemplateImportService == null || _registerTemplateStore == null ||
                _fileDialogService == null || _fileSystem == null || _messageBoxService == null)
            {
                if (_messageBoxService != null)
                {
                    await _messageBoxService.ShowAsync(
                        "Tag selection is not configured for this window.",
                        "Watch Window",
                        DialogButton.Ok,
                        DialogIcon.Warning);
                }
                return;
            }

            try
            {
                var browserViewModel = new TagBrowserViewModel(
                    _viewModel.TagService,
                    _registerTemplateImportService,
                    _registerTemplateStore,
                    _fileDialogService,
                    _fileSystem,
                    _messageBoxService,
                    selectionMode: true);
                var browser = new TagBrowserWindow(browserViewModel);
                var accepted = await browser.ShowDialog<bool?>(this);
                if (accepted == true && browserViewModel.SelectedTag != null)
                    _viewModel.AddTag(browserViewModel.SelectedTag.Id);
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
            {
                if (_messageBoxService != null)
                {
                    await _messageBoxService.ShowAsync(
                        $"Could not open the tag selector: {ex.Message}",
                        "Watch Window",
                        DialogButton.Ok,
                        DialogIcon.Error);
                }
            }
        }
    }
}
