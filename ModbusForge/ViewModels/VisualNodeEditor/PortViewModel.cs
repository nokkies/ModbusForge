using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModbusForge.Models;

namespace ModbusForge.ViewModels.VisualNodeEditor
{
    /// <summary>
    /// Display wrapper for a node's input or output port.
    /// </summary>
    public partial class PortViewModel : ObservableObject
    {
        private readonly VisualNode _node;

        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        private bool _isInput;

        [ObservableProperty]
        private bool _isVisible = true;

        /// <summary>
        /// The parent node this port belongs to.
        /// </summary>
        public VisualNode Node => _node;

        public PortViewModel(VisualNode node, string name, bool isInput)
        {
            _node = node;
            _name = name;
            _isInput = isInput;
        }
    }
}
