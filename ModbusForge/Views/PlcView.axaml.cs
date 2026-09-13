using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml;
using ModbusForge.Avalonia.ViewModels;

namespace ModbusForge.Avalonia.Views
{
    /// <summary>
    /// PLC / XEF program view. The FBD (function block diagram) is rendered data-driven —
    /// blocks and wires are <c>ItemsControl</c> items over the view-model collections,
    /// positioned on a Canvas via the ContentPresenter style (matching the working node
    /// editor, which composites reliably). The canvas is zoomed with a ScaleTransform
    /// (toolbar: zoom in / out / 100% / fit). ST programs show their source text.
    /// </summary>
    public partial class PlcView : UserControl
    {
        private PlcViewModel? _vm;
        private bool _fitRequested;

        public PlcView()
        {
            InitializeComponent();
            DataContextChanged += (_, _) => BindViewModel();
            Loaded += (_, _) => BindViewModel();
            SizeChanged += (_, _) =>
            {
                // Re-fit only if a fit is already in effect (don't fight manual zoom).
                if (_vm != null && _vm.IsFbd && _vm.FbdZoom > 0)
                {
                    var scroll = this.FindControl<ScrollViewer>("FbdScroll");
                    if (scroll != null && scroll.Bounds.Width > 50 && scroll.Bounds.Height > 50)
                        FitToView();
                }
            };
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        /// <summary>Toolbar "Fit" button: fit the whole diagram into the viewport.</summary>
        private void FbdFitButton_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => FitToView();

        /// <summary>
        /// Ctrl + mouse-wheel zooms the FBD (a mainstream PLC-editor convention). Zooming
        /// toward the cursor keeps the point under the wheel stationary.
        /// </summary>
        private void FbdScroll_WheelZoom(object? sender, global::Avalonia.Input.PointerWheelEventArgs e)
        {
            if (_vm == null || !_vm.IsFbd) return;
            if (sender is not global::Avalonia.Controls.ScrollViewer scroll) return;
            if (scroll is not IScrollable sc) return;

            // Ctrl (or Alt) + wheel = zoom; otherwise fall through to normal scroll.
            if ((e.KeyModifiers & global::Avalonia.Input.KeyModifiers.Control) == 0 &&
                (e.KeyModifiers & global::Avalonia.Input.KeyModifiers.Alt) == 0)
            {
                return;
            }

            double old = _vm.FbdZoom;
            double factor = e.Delta.Y > 0 ? 1.1 : 1.0 / 1.1;
            double next = Math.Clamp(old * factor, 0.25, 3.0);

            // Keep the content point under the cursor stationary. The canvas is scaled by the
            // zoom factor, so the scroll offset is in *scaled* pixels. For a viewport position
            // v and old offset o, the content point is (o+v)/old; after zooming to `next` we
            // need newOffset + v = (o+v)/old * next  =>  newOffset = (o+v)*(next/old) - v.
            if (Math.Abs(next - old) > 0.001)
            {
                double ratio = next / old;
                var v = e.GetPosition(scroll);
                _vm.FbdZoom = next;
                double newX = (sc.Offset.X + v.X) * ratio - v.X;
                double newY = (sc.Offset.Y + v.Y) * ratio - v.Y;
                sc.Offset = new Vector(
                    Math.Clamp(newX, 0, Math.Max(0, sc.Extent.Width - sc.Viewport.Width)),
                    Math.Clamp(newY, 0, Math.Max(0, sc.Extent.Height - sc.Viewport.Height)));
            }

            e.Handled = true; // Ctrl+wheel zooms only; never scrolls (even at the zoom limit).
        }

        // ---- FBD drag-to-pan (left-drag the background to move the diagram, like a map view). ----

        private global::Avalonia.Point _panStart;
        private Vector _panOffsetStart;
        private bool _panning;

        private void FbdCanvas_PanStart(object? sender, global::Avalonia.Input.PointerPressedEventArgs e)
        {
            if (sender is not global::Avalonia.Controls.Canvas canvas) return;
            if (!_vm?.IsFbd == true) return;
            if (FbdScroll is not IScrollable sc) return;

            // Left-drag anywhere on the canvas pans the diagram (like a map view).
            _panning = true;
            _panStart = e.GetPosition(canvas);
            _panOffsetStart = sc.Offset;
        }

        private void FbdCanvas_PanMove(object? sender, global::Avalonia.Input.PointerEventArgs e)
        {
            if (!_panning || sender is not global::Avalonia.Controls.Canvas canvas) return;
            if (FbdScroll is not IScrollable sc) return;

            var pos = e.GetPosition(canvas);
            var delta = pos - _panStart;
            double newX = _panOffsetStart.X - delta.X;
            double newY = _panOffsetStart.Y - delta.Y;
            sc.Offset = new Vector(
                Math.Clamp(newX, 0, Math.Max(0, sc.Extent.Width - sc.Viewport.Width)),
                Math.Clamp(newY, 0, Math.Max(0, sc.Extent.Height - sc.Viewport.Height)));
        }

        private void FbdCanvas_PanEnd(object? sender, global::Avalonia.Input.PointerReleasedEventArgs e)
        {
            _panning = false;
        }

        /// <summary>
        /// Clicking a FBD block selects it: highlights it (thicker accent border) and fills the
        /// "Selected block" detail pane. Clicking an already-selected block or empty canvas
        /// deselects.
        /// </summary>
        private void BlockBox_Click(object? sender, global::Avalonia.Input.PointerPressedEventArgs e)
        {
            if (_vm == null) return;
            if (sender is not global::Avalonia.Controls.Border border) return;
            if (border.DataContext is not PlcBlockItem block) return;

            // Ignore if this press is the start of a pan (the canvas pan also fires on blocks).
            if (_panning) return;

            // Toggle: clicking the selected block deselects it; the VM syncs the highlight flags.
            var current = _vm.SelectedBlockItem;
            _vm.SelectedBlockItem = ReferenceEquals(current, block) ? null : block;
            e.Handled = true;
        }

        private void BindViewModel()
        {
            if (DataContext is not PlcViewModel vm) return;
            if (ReferenceEquals(vm, _vm))
            {
                RequestFit();
                return;
            }
            _vm = vm;
            vm.PropertyChanged += OnVmPropertyChanged;
        }

        private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PlcViewModel.IsFbd) || e.PropertyName == nameof(PlcViewModel.SelectedProgram))
            {
                RequestFit();
            }
        }

        /// <summary>
        /// Fit the whole diagram into the viewport: compute a zoom from the block bounds and
        /// the ScrollViewer size, apply it, then scroll to the top-left. Retried so it runs
        /// after the canvas is in the visual tree and the blocks are laid out.
        /// </summary>
        private void RequestFit()
        {
            if (_vm == null || !_vm.IsFbd) return;
            for (int i = 1; i <= 5; i++)
            {
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    await System.Threading.Tasks.Task.Delay(i * 400);
                    try { await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(FitToView); }
                    catch { }
                });
            }
        }

        /// <summary>
        /// Computes and applies the zoom that fits the block bounds into the viewport, with a
        /// small margin, clamped to the VM's zoom limits.
        /// </summary>
        private void FitToView()
        {
            if (_vm == null || !_vm.IsFbd || _vm.Blocks.Count == 0) return;

            var scroll = this.FindControl<ScrollViewer>("FbdScroll");
            if (scroll == null || scroll is not IScrollable sc) return;
            var vw = scroll.Bounds.Width - 20;
            var vh = scroll.Bounds.Height - 20;
            if (vw < 40 || vh < 40) return;

            double minX = _vm.Blocks.Min(b => b.X);
            double minY = _vm.Blocks.Min(b => b.Y);
            double maxX = _vm.Blocks.Max(b => b.X + b.Width);
            double maxY = _vm.Blocks.Max(b => b.Y + b.Height);
            double contentW = maxX - minX + 60;
            double contentH = maxY - minY + 60;
            if (contentW <= 0 || contentH <= 0) return;

            double fit = Math.Min(vw / contentW, vh / contentH);
            // Clamp to readable limits — don't zoom below 0.4 (text becomes illegible).
            const double minZ = 0.4, maxZ = 3.0;
            _vm.FbdZoom = Math.Clamp(fit, minZ, maxZ);

            // Scroll to the top-left of the content at the new zoom.
            sc.Offset = new Vector(Math.Max(0, minX * _vm.FbdZoom - 20), Math.Max(0, minY * _vm.FbdZoom - 20));
        }
    }
}
