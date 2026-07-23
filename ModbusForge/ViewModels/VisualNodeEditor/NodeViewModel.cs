using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModbusForge.Helpers;
using ModbusForge.Models;

namespace ModbusForge.ViewModels.VisualNodeEditor
{
    /// <summary>
    /// Display wrapper for a <see cref="VisualNode"/> that is used by the XAML node editor.
    /// Keeps the model intact while adding view-specific state (ports, live value, commands).
    /// </summary>
    public partial class NodeViewModel : ObservableObject
    {
        [ObservableProperty]
        private VisualNode _node;

        [ObservableProperty]
        private string _liveValueText = string.Empty;

        [ObservableProperty]
        private bool _showLiveValues;

        public ObservableCollection<PortViewModel> Ports { get; } = new ObservableCollection<PortViewModel>();

        public string Id => Node.Id;

        public string DisplayName => Node.DisplayName;

        public string AddressDisplay => Node.AddressDisplay;

        public string ParameterDisplay => Node.ParameterDisplay;

        public Brush HeaderBrush => BrushCache.GetBrush(NodeDescriptors.Get(Node.ElementType).HeaderColor);

        public bool IsSelected
        {
            get => Node.IsSelected;
            set => Node.IsSelected = value;
        }

        public ICommand ConfigureCommand { get; }

        public ICommand DeleteCommand { get; }

        public NodeViewModel(VisualNode node)
        {
            Node = node ?? throw new ArgumentNullException(nameof(node));

            Node.PropertyChanged += OnNodePropertyChanged;

            ConfigureCommand = new RelayCommand(() => { /* wired by the view */ });
            DeleteCommand = new RelayCommand(() => { /* wired by the view */ });

            RebuildPorts();
        }

        private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(VisualNode.ElementType))
            {
                RebuildPorts();
                OnPropertyChanged(nameof(HeaderBrush));
            }

            if (e.PropertyName is nameof(VisualNode.DisplayName) or nameof(VisualNode.AddressDisplay) or nameof(VisualNode.ParameterDisplay))
            {
                OnPropertyChanged(e.PropertyName);
            }

            if (e.PropertyName is nameof(VisualNode.IsSelected))
            {
                OnPropertyChanged(nameof(IsSelected));
            }

            if (e.PropertyName is nameof(VisualNode.CurrentValue) or nameof(VisualNode.CurrentValueDouble) or nameof(VisualNode.IntValue))
            {
                OnPropertyChanged(nameof(LiveValueText));
            }
        }

        private void RebuildPorts()
        {
            Ports.Clear();

            var descriptor = NodeDescriptors.Get(Node.ElementType);

            if (Node.ElementType != PlcElementType.SignalGenerator)
            {
                Ports.Add(new PortViewModel(Node, "Input1", true));
            }

            if (descriptor.HasSecondInput)
            {
                Ports.Add(new PortViewModel(Node, "Input2", true));
            }

            Ports.Add(new PortViewModel(Node, "Output", false));
        }
    }
}
