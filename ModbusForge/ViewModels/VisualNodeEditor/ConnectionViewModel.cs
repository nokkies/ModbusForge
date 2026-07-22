using System;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using ModbusForge.Models;

namespace ModbusForge.ViewModels.VisualNodeEditor
{
    /// <summary>
    /// Display wrapper for a connection between two node ports.
    /// </summary>
    public partial class ConnectionViewModel : ObservableObject
    {
        [ObservableProperty]
        private NodeConnection _model;

        [ObservableProperty]
        private NodeViewModel? _sourceNode;

        [ObservableProperty]
        private PortViewModel? _sourcePort;

        [ObservableProperty]
        private NodeViewModel? _targetNode;

        [ObservableProperty]
        private PortViewModel? _targetPort;

        [ObservableProperty]
        private Geometry? _geometry;

        public ConnectionViewModel(NodeConnection model)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
        }
    }
}
