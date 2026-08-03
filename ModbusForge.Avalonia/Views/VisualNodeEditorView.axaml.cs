using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ModbusForge.Avalonia.ViewModels;
using ModbusForge.Models;

namespace ModbusForge.Avalonia.Views
{
    public partial class VisualNodeEditorView : UserControl
    {
        private const string PaletteDragFormat = "ModbusForge.VisualNodeType";
        private const double DragThreshold = 4.0;

        private Canvas? _nodeCanvas;
        private IReadOnlyList<VisualNode> _draggedNodes = Array.Empty<VisualNode>();
        private Dictionary<VisualNode, (double X, double Y)> _dragStartPositions = new();
        private Point _dragStartPoint;
        private IPointer? _dragPointer;
        private bool _isDraggingNode;

        private PaletteItem? _paletteDragItem;
        private Point _paletteDragStart;
        private IPointer? _palettePointer;
        private bool _paletteDragStarted;

        public VisualNodeEditorView()
        {
            InitializeComponent();
            _nodeCanvas = this.FindControl<Canvas>("NodeCanvas");
            AddHandler(KeyDownEvent, View_KeyDown, RoutingStrategies.Tunnel);
            _nodeCanvas?.AddHandler(DragDrop.DragOverEvent, Canvas_DragOver, RoutingStrategies.Bubble);
            _nodeCanvas?.AddHandler(DragDrop.DropEvent, Canvas_Drop, RoutingStrategies.Bubble);
        }

        private VisualNodeEditorViewModel? ViewModel => DataContext as VisualNodeEditorViewModel;

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void Canvas_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.Source == sender && ViewModel != null)
            {
                ViewModel.ClearSelection();
                if (ViewModel.IsConnectMode)
                {
                    ViewModel.CancelConnectCommand.Execute(null);
                }

                ViewModel.SelectedConnection = null;
                e.Handled = true;
            }
        }

        private void Canvas_DragOver(object? sender, DragEventArgs e)
        {
            e.DragEffects = TryGetDraggedElementType(e.Data, out _)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            e.Handled = true;
        }

        private void Canvas_Drop(object? sender, DragEventArgs e)
        {
            if (ViewModel == null || sender is not Canvas canvas
                || !TryGetDraggedElementType(e.Data, out var elementType))
            {
                return;
            }

            var point = e.GetPosition(canvas);
            var zoom = Math.Max(ViewModel.ZoomLevel, 0.01);
            // The canvas is rendered through a ScaleTransform. Use the same
            // logical-coordinate conversion as node dragging.
            var node = ViewModel.AddNodeAt(elementType, point.X / zoom, point.Y / zoom);
            if (node != null)
            {
                e.DragEffects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        private void Palette_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (ViewModel == null || sender is not ListBox listBox)
            {
                return;
            }

            var point = e.GetCurrentPoint(listBox);
            if (!point.Properties.IsLeftButtonPressed
                || FindPaletteItem(e.Source) is not { } item)
            {
                return;
            }

            ViewModel.SelectedPaletteItem = item;
            _paletteDragItem = item;
            _paletteDragStart = e.GetPosition(listBox);
            _palettePointer = e.Pointer;
            _paletteDragStarted = false;
            e.Pointer.Capture(listBox);
        }

        private async void Palette_PointerMoved(object? sender, PointerEventArgs e)
        {
            if (_paletteDragItem == null || _palettePointer != e.Pointer
                || sender is not ListBox listBox)
            {
                return;
            }

            if (!e.GetCurrentPoint(listBox).Properties.IsLeftButtonPressed)
            {
                ResetPaletteDrag(e.Pointer);
                return;
            }

            var current = e.GetPosition(listBox);
            var deltaX = current.X - _paletteDragStart.X;
            var deltaY = current.Y - _paletteDragStart.Y;
            if (_paletteDragStarted || Math.Sqrt(deltaX * deltaX + deltaY * deltaY) < DragThreshold)
            {
                return;
            }

            _paletteDragStarted = true;
            var item = _paletteDragItem;
            var data = new DataObject();
            data.Set(PaletteDragFormat, item.ElementType.ToString());
            data.Set(DataFormats.Text, item.ElementType.ToString());
            e.Pointer.Capture(null);

            try
            {
                await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy);
            }
            finally
            {
                ResetPaletteDrag(e.Pointer);
            }
        }

        private void Palette_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (_palettePointer == e.Pointer)
            {
                ResetPaletteDrag(e.Pointer);
            }
        }

        private static PaletteItem? FindPaletteItem(object? source)
        {
            for (var control = source as Control; control != null; control = control.Parent as Control)
            {
                if (control.DataContext is PaletteItem item)
                {
                    return item;
                }
            }

            return null;
        }

        private static bool TryGetDraggedElementType(IDataObject data, out PlcElementType elementType)
        {
            elementType = default;
            object? value = null;
            if (data.Contains(PaletteDragFormat))
            {
                value = data.Get(PaletteDragFormat);
            }
            else if (data.Contains(DataFormats.Text))
            {
                value = data.GetText();
            }

            if (value is PlcElementType directType)
            {
                elementType = directType;
                return true;
            }

            if (value is not string text || !Enum.TryParse(text, ignoreCase: true, out PlcElementType parsedType))
            {
                return false;
            }

            if (!NodeDescriptors.All.Any(descriptor => descriptor.ElementType == parsedType))
            {
                return false;
            }

            elementType = parsedType;
            return true;
        }

        private void ResetPaletteDrag(IPointer? pointer)
        {
            pointer?.Capture(null);
            _palettePointer = null;
            _paletteDragItem = null;
            _paletteDragStarted = false;
        }

        private void Node_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not Border border || e.Source is TextBox || ViewModel == null)
            {
                return;
            }

            var point = e.GetCurrentPoint(border);
            if (!point.Properties.IsLeftButtonPressed || border.DataContext is not VisualNode node)
            {
                return;
            }

            var extendsSelection = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Shift)) != 0;
            var wasSelected = node.IsSelected;
            var wasConnectMode = ViewModel.IsConnectMode;
            ViewModel.SelectNode(node, extendsSelection);
            if (wasConnectMode || (extendsSelection && wasSelected && !node.IsSelected))
            {
                e.Handled = true;
                return;
            }

            _nodeCanvas ??= this.FindControl<Canvas>("NodeCanvas");
            _draggedNodes = ViewModel.GetNodesForDrag(node);
            _dragStartPositions = _draggedNodes.ToDictionary(
                selectedNode => selectedNode,
                selectedNode => (selectedNode.X, selectedNode.Y));
            _dragStartPoint = _nodeCanvas is { } canvas
                ? e.GetPosition(canvas)
                : e.GetPosition(border);
            _dragPointer = e.Pointer;
            _isDraggingNode = true;
            e.Pointer.Capture(border);
            e.Handled = true;
        }

        private void Node_PointerMoved(object? sender, PointerEventArgs e)
        {
            if (!_isDraggingNode || _draggedNodes.Count == 0 || ViewModel == null)
            {
                return;
            }

            if (!e.GetCurrentPoint(sender as Visual ?? this).Properties.IsLeftButtonPressed)
            {
                FinishNodeDrag(e.Pointer, commit: true);
                return;
            }

            var currentPoint = _nodeCanvas is { } canvas
                ? e.GetPosition(canvas)
                : e.GetPosition(this);
            var zoom = Math.Max(ViewModel.ZoomLevel, 0.01);
            var deltaX = (currentPoint.X - _dragStartPoint.X) / zoom;
            var deltaY = (currentPoint.Y - _dragStartPoint.Y) / zoom;
            foreach (var node in _draggedNodes)
            {
                if (_dragStartPositions.TryGetValue(node, out var start))
                {
                    ViewModel.SetNodePosition(node, start.X + deltaX, start.Y + deltaY);
                }
            }

            e.Handled = true;
        }

        private void Node_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (!_isDraggingNode) return;
            FinishNodeDrag(e.Pointer, commit: true);
            e.Handled = true;
        }

        private void Node_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        {
            if (_isDraggingNode)
            {
                FinishNodeDrag(null, commit: true);
            }
        }

        private void FinishNodeDrag(IPointer? pointer, bool commit)
        {
            if (!_isDraggingNode || ViewModel == null)
            {
                _isDraggingNode = false;
                _draggedNodes = Array.Empty<VisualNode>();
                _dragStartPositions = new Dictionary<VisualNode, (double X, double Y)>();
                _dragPointer = null;
                return;
            }

            var capturedPointer = pointer ?? _dragPointer;
            var nodes = _draggedNodes;
            var oldPositions = _dragStartPositions;
            _isDraggingNode = false;
            _draggedNodes = Array.Empty<VisualNode>();
            _dragStartPositions = new Dictionary<VisualNode, (double X, double Y)>();
            _dragPointer = null;
            capturedPointer?.Capture(null);

            if (commit)
            {
                ViewModel.CommitNodeMoves(oldPositions);
            }
            else
            {
                foreach (var node in nodes)
                {
                    if (oldPositions.TryGetValue(node, out var start))
                    {
                        ViewModel.SetNodePosition(node, start.X, start.Y);
                    }
                }
            }
        }

        private void LiveValue_GotFocus(object? sender, GotFocusEventArgs e)
        {
            if (sender is Control control && control.DataContext is VisualNode node)
            {
                node.IsEditingLiveValue = true;
            }
        }

        private void LiveValue_LostFocus(object? sender, RoutedEventArgs e)
        {
            if (sender is Control control && control.DataContext is VisualNode node)
            {
                node.IsEditingLiveValue = false;
            }
        }

        private void View_KeyDown(object? sender, KeyEventArgs e)
        {
            if (ViewModel == null) return;

            if ((e.KeyModifiers & KeyModifiers.Control) != 0 && e.Key == Key.Z)
            {
                if ((e.KeyModifiers & KeyModifiers.Shift) != 0)
                {
                    ViewModel.RedoCommand.Execute(null);
                }
                else
                {
                    ViewModel.UndoCommand.Execute(null);
                }

                e.Handled = true;
                return;
            }

            if ((e.KeyModifiers & KeyModifiers.Control) != 0 && e.Key == Key.Y)
            {
                ViewModel.RedoCommand.Execute(null);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape)
            {
                if (_isDraggingNode)
                {
                    FinishNodeDrag(_dragPointer, commit: false);
                    e.Handled = true;
                }
                else if (ViewModel.IsConnectMode)
                {
                    ViewModel.CancelConnectCommand.Execute(null);
                    e.Handled = true;
                }
            }
        }
    }
}
