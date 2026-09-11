using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ModbusForge.Avalonia.Services;
using ModbusForge.Avalonia.ViewModels;
using ModbusForge.Models;

namespace ModbusForge.Avalonia.Views
{
    public partial class TagBrowserWindow : Window, IDockableTool
    {
        private const string TagDragFormat = "ModbusForge.Avalonia.Tag";
        private const string TagDragTextPrefix = "MF|Tag|";
        private const double DragThreshold = 4.0;

        private TagBrowserViewModel? _viewModel;
        private bool _initialized;
        private Point _treeDragStart;
        private IPointer? _treeDragPointer;
        private bool _treeDragStarted;
        private Control? _content;
        private Button? _dockToggleButton;
        private Action? _toggleDockCallback;

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
            _content = this.Content as Control;
            _dockToggleButton = this.FindControl<Button>("DockToggleButton");
        }

        public Action? ToggleDockCallback
        {
            get => _toggleDockCallback;
            set => _toggleDockCallback = value;
        }

        public void SetDocked(bool isDocked)
        {
            if (_dockToggleButton != null)
            {
                _dockToggleButton.Content = isDocked ? "Float" : "Dock";
            }
        }

        private void DockToggleButton_Click(object? sender, RoutedEventArgs e)
        {
            _toggleDockCallback?.Invoke();
        }

        private Window GetDialogOwner() => TopLevel.GetTopLevel(_content) as Window ?? this;

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

                var accepted = await dialog.ShowDialog<bool?>(GetDialogOwner());
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

        private void TagTree_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not TreeView treeView)
                return;

            var point = e.GetCurrentPoint(treeView);
            if (!point.Properties.IsLeftButtonPressed || _viewModel?.SelectedTag == null)
            {
                return;
            }

            _treeDragPointer = e.Pointer;
            _treeDragStart = e.GetPosition(treeView);
            _treeDragStarted = false;
            e.Pointer.Capture(treeView);
        }

        private async void TagTree_PointerMoved(object? sender, PointerEventArgs e)
        {
            if (_viewModel?.SelectedTag == null
                || _treeDragPointer != e.Pointer
                || sender is not TreeView treeView)
            {
                return;
            }

            if (!e.GetCurrentPoint(treeView).Properties.IsLeftButtonPressed)
            {
                ResetTreeDrag();
                return;
            }

            var current = e.GetPosition(treeView);
            var deltaX = current.X - _treeDragStart.X;
            var deltaY = current.Y - _treeDragStart.Y;
            if (_treeDragStarted || Math.Sqrt(deltaX * deltaX + deltaY * deltaY) < DragThreshold)
            {
                return;
            }

            _treeDragStarted = true;
            var tag = _viewModel.SelectedTag;
            var data = new DataObject();
            data.Set(TagDragFormat, tag);
            data.Set(DataFormats.Text, $"{TagDragTextPrefix}{tag.Id}");
            e.Pointer.Capture(null);

            try
            {
                await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy);
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
            {
                // async void would otherwise swallow the exception silently.
                _viewModel?.MessageBoxService.ShowAsync(
                    $"Tag drag failed: {ex.Message}",
                    "Drag Failed",
                    ModbusForge.Services.DialogButton.Ok,
                    ModbusForge.Services.DialogIcon.Error);
            }
            finally
            {
                ResetTreeDrag();
            }
        }

        private void TagTree_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (_treeDragPointer == e.Pointer)
            {
                ResetTreeDrag();
            }
        }

        private void ResetTreeDrag()
        {
            _treeDragPointer?.Capture(null);
            _treeDragPointer = null;
            _treeDragStarted = false;
        }
    }
}
