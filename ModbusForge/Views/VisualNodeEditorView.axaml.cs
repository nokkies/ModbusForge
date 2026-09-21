using Avalonia.Markup.Xaml;
using Avalonia.Interactivity;
using ModbusForge.Avalonia.ViewModels;

namespace ModbusForge.Avalonia.Views
{
    /// <summary>
    /// The Simulation tab's editor surface. All canvas interaction (node drag,
    /// marquee selection, connection drag-drop, pan/zoom, POU tree management,
    /// palette drag) lives in <see cref="FbdCanvasInteractionBase"/>; this class
    /// only loads the XAML and resets PLC-project mode on attach so the shared
    /// editor VM shows the full POU tree and simulation toolbar.
    /// </summary>
    public partial class VisualNodeEditorView : FbdCanvasInteractionBase
    {
        public VisualNodeEditorView()
        {
            AvaloniaXamlLoader.Load(this);
            OnViewContentLoaded();
        }

        /// <summary>
        /// When the Simulation tab re-attaches the shared editor VM, the PLC view
        /// may have left IsPlcProjectMode on. Reset it so the POU tree, palette,
        /// and simulation controls are visible again.
        /// </summary>
        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            if (ViewModel is { } vm)
            {
                vm.IsPlcProjectMode = false;
            }
        }
    }
}
