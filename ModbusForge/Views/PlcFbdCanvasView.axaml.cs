using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ModbusForge.Avalonia.ViewModels;

namespace ModbusForge.Avalonia.Views
{
    /// <summary>
    /// The PLC-project rendering of an imported FBD program: the canvas, toolbar,
    /// controls panel and an inline block list, without the Simulation tab's
    /// POU tree / palette panels (the PLC project navigator owns program
    /// selection). It binds the PLC tab's own editor
    /// (<see cref="PlcEditorViewModel"/>), never the Simulation's. Canvas
    /// interaction is inherited from <see cref="FbdCanvasInteractionBase"/>.
    /// </summary>
    public partial class PlcFbdCanvasView : FbdCanvasInteractionBase
    {
        public PlcFbdCanvasView()
        {
            AvaloniaXamlLoader.Load(this);
            // The base ctor wires node/dragn-drop handlers via FindControl, which
            // only works AFTER the derived class has loaded its own XAML.
            OnViewContentLoaded();
        }

        /// <summary>
        /// Double-clicking a block in the inline list adds it to the canvas
        /// (same as the Simulation palette's double-tap).
        /// </summary>
        private void BlockList_DoubleTapped(object? sender, TappedEventArgs e)
        {
            if (ViewModel == null || sender is not ListBox listBox)
            {
                return;
            }

            var item = FindBlockItem(e.Source);
            if (item == null)
            {
                return;
            }

            ViewModel.SelectedPaletteItem = item;
            ViewModel.AddNode();
            e.Handled = true;
        }

        private static PaletteItem? FindBlockItem(object? source)
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
    }
}
