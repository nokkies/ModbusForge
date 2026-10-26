using Avalonia.Markup.Xaml;

namespace ModbusForge.Avalonia.Views
{
    /// <summary>
    /// The Simulation tab's editor surface. All canvas interaction (node drag,
    /// marquee selection, connection drag-drop, pan/zoom, POU tree management,
    /// palette drag) lives in <see cref="FbdCanvasInteractionBase"/>; this class
    /// only loads the XAML. It binds the Simulation's own editor; the PLC tab uses
    /// <see cref="PlcFbdCanvasView"/> with a separate editor.
    /// </summary>
    public partial class VisualNodeEditorView : FbdCanvasInteractionBase
    {
        public VisualNodeEditorView()
        {
            AvaloniaXamlLoader.Load(this);
            OnViewContentLoaded();
        }
    }
}
