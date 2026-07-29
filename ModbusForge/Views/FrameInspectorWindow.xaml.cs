using System.Windows;
using ModbusForge.ViewModels;

namespace ModbusForge.Views
{
    /// <summary>
    /// Byte-level Modbus PDU frame inspector.
    /// </summary>
    public partial class FrameInspectorWindow : Window
    {
        public FrameInspectorWindow(FrameInspectorViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
