using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json.Serialization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ModbusForge.Models
{
    /// <summary>
    /// Visual representation of a PLC element in the node editor
    /// </summary>
    public partial class VisualNode : ObservableObject
    {
        [ObservableProperty]
        private string _id = Guid.NewGuid().ToString();
        
        [ObservableProperty]
        private string _name = "";
        
        [ObservableProperty]
        private PlcElementType _elementType = PlcElementType.Input;
        
        [ObservableProperty]
        private double _x = 100;
        
        [ObservableProperty]
        private double _y = 100;
        
        [ObservableProperty]
        private double _width = 240;
        
        [ObservableProperty]
        private double _height = 140;
        
        [ObservableProperty]
        private bool _isSelected = false;
        
        [ObservableProperty]
        private bool _currentValue = false;
        
        [ObservableProperty]
        private double _currentValueDouble = 0;

        /// <summary>
        /// When true, changes to <see cref="CurrentValueDouble"/> do NOT trigger
        /// the <see cref="ValueChangedCallback"/>. Used by the simulation service
        /// when it pushes live values into the property so we don't echo writes
        /// back to the DataStore for values the simulation just produced.
        /// </summary>
        [JsonIgnore]
        public bool SuppressWriteBack { get; set; }

        /// <summary>
        /// Callback invoked when the user manually edits the Live Values TextBox.
        /// The ViewModel subscribes to this and forwards the value to the
        /// simulation service so it gets written to the DataStore.
        /// </summary>
        [JsonIgnore]
        public Action<VisualNode, double>? ValueChangedCallback { get; set; }

        /// <summary>
        /// When true, the user is actively editing the live value on the node canvas,
        /// so the simulation service should not overwrite <see cref="CurrentValueDouble"/>.
        /// </summary>
        [JsonIgnore]
        public bool IsEditingLiveValue { get; set; }

        partial void OnCurrentValueDoubleChanged(double value)
        {
            if (!SuppressWriteBack)
            {
                ValueChangedCallback?.Invoke(this, value);
            }
        }
        
        [ObservableProperty]
        private bool _showLiveValues = false;
        
        [ObservableProperty]
        private bool _isEnabled = true;
        
        [ObservableProperty]
        private string? _waveform = "Ramp";
        
        [ObservableProperty]
        private int _periodMs = 1000;
        
        [ObservableProperty]
        private double _amplitude = 100;
        
        [ObservableProperty]
        private double _offset = 0;
        
        [ObservableProperty]
        private PlcAddressReference _input1Address = new PlcAddressReference();
        
        [ObservableProperty]
        private PlcAddressReference _input2Address = new PlcAddressReference();
        
        [ObservableProperty]
        private PlcAddressReference _outputAddress = new PlcAddressReference();
        
        // Timer/Counter parameters
        [ObservableProperty]
        private int _timerPresetMs = 1000;
        
        [ObservableProperty]
        private bool _setDominant = true;
        
        [ObservableProperty]
        private int _counterPreset = 10;
        
        [ObservableProperty]
        private int _compareValue = 0;
        
        // Runtime state (not persisted)
        public int TimerAccumulatorMs { get; set; } = 0;
        public bool TimerLastInput { get; set; } = false;
        public bool TimerOutput { get; set; } = false;
        public bool RsState { get; set; } = false;
        public int CounterValue { get; set; } = 0;
        public bool CounterLastInput { get; set; } = false;
        
        /// <summary>
        /// Cached integer value from the last simulation tick (used by the two-phase evaluator).
        /// </summary>
        public int IntValue { get; set; } = 0;
        
        public string DisplayName => NodeDescriptors.Get(ElementType).GetDisplayName(this);

        public string AddressDisplay
        {
            get
            {
                var descriptor = NodeDescriptors.Get(ElementType);
                if (descriptor.IsInput && Input1Address != null)
                {
                    return Input1Address.Address >= 0
                        ? $"{Input1Address.Area}:{Input1Address.Address}"
                        : "[Not Configured]";
                }

                if (descriptor.IsOutput && OutputAddress != null)
                {
                    return OutputAddress.Address >= 0
                        ? $"{OutputAddress.Area}:{OutputAddress.Address}"
                        : "[Not Configured]";
                }

                return string.Empty;
            }
        }

        public bool HasSecondInput => NodeDescriptors.Get(ElementType).HasSecondInput;

        public bool HasParameters => NodeDescriptors.Get(ElementType).HasParameters;

        public string ParameterDisplay => NodeDescriptors.Get(ElementType).GetParameterDisplay(this);

        public bool HasOutput => ElementType != PlcElementType.Input;

        // Cached handler so we can unsubscribe from the previous PlcAddressReference instance.
        private PropertyChangedEventHandler? _addressPropertyChangedHandler;
        private PlcAddressReference? _subscribedInput1Address;
        private PlcAddressReference? _subscribedOutputAddress;

        partial void OnElementTypeChanged(PlcElementType value)
        {
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(AddressDisplay));
            OnPropertyChanged(nameof(HasSecondInput));
            OnPropertyChanged(nameof(HasParameters));
            OnPropertyChanged(nameof(ParameterDisplay));
        }

        partial void OnInput1AddressChanged(PlcAddressReference value)
        {
            if (_subscribedInput1Address != null && _addressPropertyChangedHandler != null)
                _subscribedInput1Address.PropertyChanged -= _addressPropertyChangedHandler;

            _subscribedInput1Address = value;
            if (value is null)
                return;

            _addressPropertyChangedHandler ??= OnAddressPropertyChanged;
            value.PropertyChanged += _addressPropertyChangedHandler;

            OnPropertyChanged(nameof(AddressDisplay));
        }

        partial void OnOutputAddressChanged(PlcAddressReference value)
        {
            if (_subscribedOutputAddress != null && _addressPropertyChangedHandler != null)
                _subscribedOutputAddress.PropertyChanged -= _addressPropertyChangedHandler;

            _subscribedOutputAddress = value;
            if (value is null)
                return;

            _addressPropertyChangedHandler ??= OnAddressPropertyChanged;
            value.PropertyChanged += _addressPropertyChangedHandler;

            OnPropertyChanged(nameof(AddressDisplay));
        }

        partial void OnTimerPresetMsChanged(int value)
        {
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(ParameterDisplay));
        }

        partial void OnCounterPresetChanged(int value)
        {
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(ParameterDisplay));
        }

        partial void OnCompareValueChanged(int value)
        {
            OnPropertyChanged(nameof(ParameterDisplay));
        }

        partial void OnWaveformChanged(string? value)
        {
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(ParameterDisplay));
        }

        partial void OnPeriodMsChanged(int value)
        {
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(ParameterDisplay));
        }

        partial void OnAmplitudeChanged(double value)
        {
            OnPropertyChanged(nameof(ParameterDisplay));
        }

        partial void OnOffsetChanged(double value)
        {
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(ParameterDisplay));
        }

        private void OnAddressPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(PlcAddressReference.Area) or nameof(PlcAddressReference.Address))
            {
                OnPropertyChanged(nameof(AddressDisplay));
            }
        }
    }
    
    /// <summary>
    /// Connection between two visual nodes
    /// </summary>
    public partial class NodeConnection : ObservableObject
    {
        [ObservableProperty]
        private string _id = Guid.NewGuid().ToString();
        
        [ObservableProperty]
        private string _sourceNodeId = "";
        
        [ObservableProperty]
        private string _targetNodeId = "";
        
        [ObservableProperty]
        private string _sourceConnector = "Output"; // Always output for now
        
        [ObservableProperty]
        private string _targetConnector = "Input1"; // Input1 or Input2
        
        [ObservableProperty]
        private double _startX = 0;
        
        [ObservableProperty]
        private double _startY = 0;
        
        [ObservableProperty]
        private double _endX = 0;
        
        [ObservableProperty]
        private double _endY = 0;
        
        [ObservableProperty]
        private bool _isConnected = true;
        
        public NodeConnection(string sourceNodeId, string targetNodeId, string targetConnector = "Input1")
        {
            SourceNodeId = sourceNodeId;
            TargetNodeId = targetNodeId;
            TargetConnector = targetConnector;
        }
    }
    
    /// <summary>
    /// Configuration for a connector (input/output) that links to a Modbus address
    /// </summary>
    public partial class ConnectorConfiguration : ObservableObject
    {
        [ObservableProperty]
        private string _nodeId = "";
        
        [ObservableProperty]
        private string _connectorType = ""; // "Input1", "Input2", "Output"
        
        [ObservableProperty]
        private bool _isConfigured = false;
        
        [ObservableProperty]
        private PlcArea _area = PlcArea.Coil;
        
        [ObservableProperty]
        private int _address = 1;
        
        [ObservableProperty]
        private bool _not = false;
        
        [ObservableProperty]
        private string _tag = ""; // User-friendly tag name
        
        public string DisplayAddress => $"{Area}:{Address}{(Not ? " (NOT)" : "")}";
    }
    
    /// <summary>
    /// Visual node editor configuration
    /// </summary>
    public partial class VisualNodeEditorConfig : ObservableObject
    {
        [ObservableProperty]
        private double _canvasWidth = 2000;
        
        [ObservableProperty]
        private double _canvasHeight = 2000;
        
        [ObservableProperty]
        private double _zoomLevel = 1.0;
        
        [ObservableProperty]
        private bool _showLiveValues = false;
        
        [ObservableProperty]
        private bool _showGrid = true;
        
        [ObservableProperty]
        private bool _snapToGrid = true;
        
        [ObservableProperty]
        private double _gridSize = 20;
        
        [ObservableProperty]
        private ObservableCollection<VisualNode> _nodes = new ObservableCollection<VisualNode>();
        
        [ObservableProperty]
        private ObservableCollection<NodeConnection> _connections = new ObservableCollection<NodeConnection>();
        
        [ObservableProperty]
        private ObservableCollection<ConnectorConfiguration> _connectorConfigs = new ObservableCollection<ConnectorConfiguration>();
    }
}
