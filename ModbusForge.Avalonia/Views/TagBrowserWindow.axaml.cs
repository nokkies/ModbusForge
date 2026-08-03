using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using ModbusForge.Avalonia.ViewModels;
using ModbusForge.Models;

namespace ModbusForge.Avalonia.Views
{
    public partial class TagBrowserWindow : Window
    {
        private TagBrowserViewModel? _viewModel;
        private bool _initialized;

        public TagBrowserWindow()
        {
            InitializeComponent();
        }

        public TagBrowserWindow(TagBrowserViewModel viewModel) : this()
        {
            DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        }

        public Tag? SelectedTag => _viewModel?.SelectedTag;

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            if (_viewModel != null)
            {
                _viewModel.RequestClose -= ViewModel_RequestClose;
                _viewModel.TemplateImportRequested -= ViewModel_TemplateImportRequested;
            }

            base.OnDataContextChanged(e);
            _viewModel = DataContext as TagBrowserViewModel;

            if (_viewModel != null)
            {
                _viewModel.RequestClose += ViewModel_RequestClose;
                _viewModel.TemplateImportRequested += ViewModel_TemplateImportRequested;
            }
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
            {
                _viewModel.RequestClose -= ViewModel_RequestClose;
                _viewModel.TemplateImportRequested -= ViewModel_TemplateImportRequested;
                _viewModel.Dispose();
            }

            base.OnClosed(e);
        }

        private void ViewModel_RequestClose(object? sender, bool accepted)
        {
            Close(accepted);
        }

        private async void ViewModel_TemplateImportRequested(
            object? sender,
            TagBrowserViewModel.TemplateImportRequestedEventArgs e)
        {
            if (_viewModel == null)
                return;

            try
            {
                var dialog = new RegisterTemplateImportDialog(
                    _viewModel.RegisterTemplateImportService,
                    _viewModel.FileDialogService,
                    _viewModel.FileSystem,
                    _viewModel.MessageBoxService,
                    e.FilePath);

                var accepted = await dialog.ShowDialog<bool?>(this);
                if (accepted == true && dialog.ImportedTemplate != null)
                {
                    await _viewModel.MergeImportedTemplateAsync(
                        dialog.ImportedTemplate,
                        dialog.Entries,
                        dialog.SaveTemplate);
                }
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
            {
                await _viewModel.MessageBoxService.ShowAsync(
                    $"Import failed: {ex.Message}",
                    "Import Failed",
                    ModbusForge.Services.DialogButton.Ok,
                    ModbusForge.Services.DialogIcon.Error);
            }
        }

        private void TagTree_DoubleTapped(object? sender, TappedEventArgs e)
        {
            if (_viewModel?.SelectionMode == true)
                _viewModel.AcceptSelectedTag();
        }
    }
}
