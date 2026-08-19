using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ModbusForge.Models
{
    /// <summary>
    /// Visual representation of a PLC element in the node editor
    /// </summary>
    public partial class VisualNode : ObservableObject
    {
        /// <summary>
        /// Available Modbus areas for the inline I/O address editor.
        /// </summary>
        public static IReadOnlyList<PlcArea> PlcAreaOptions { get; } =
            Enum.GetValues(typeof(PlcArea)).Cast<PlcArea>().ToList();

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

        /// <summary>
        /// Secondary output port Modbus address bindings keyed by port name.
        /// </summary>
        [ObservableProperty]
        private Dictionary<string, PlcAddressReference> _outputPortBindings = new();

        /// <summary>
        /// Names of the output ports exposed by this node. Rendered by the canvas.
        /// </summary>
        [ObservableProperty]
        private ObservableCollection<string> _outputPortNames = new(new[] { "Output" });
        
        // Timer/Counter parameters
        [ObservableProperty]
        private int _timerPresetMs = 1000;
        
        [ObservableProperty]
        private bool _setDominant = true;
        
        [ObservableProperty]
        private int _counterPreset = 10;
        
        [ObservableProperty]
        private int _compareValue = 0;

        /// <summary>
        /// Constant/value parameter for the Real (double) comparator and math blocks.
        /// Kept separate from <see cref="CompareValue"/> (int) so both variants can coexist
        /// with full precision.
        /// </summary>
        [ObservableProperty]
        private double _compareValueReal = 0.0;

        // Industrial device parameters
        [ObservableProperty]
        private int _valveTravelTimeMs = 5000;

        [ObservableProperty]
        private bool _valveNormallyOpen = false;

        /// <summary>
        /// Valve rest behavior: true = hold last commanded position (motor valve),
        /// false = spring-return to the rest position when no command is active.
        /// </summary>
        [ObservableProperty]
        private bool _valveLatching = true;

        [ObservableProperty]
        private int _motorDolRunDelayMs = 100;

        // VSD parameters
        [ObservableProperty]
        private double _vsdMaxSpeed = 100.0;

        [ObservableProperty]
        private int _vsdRampUpMs = 2000;

        [ObservableProperty]
        private int _vsdRampDownMs = 2000;

        [ObservableProperty]
        private double _vsdAtSpeedTolerance = 2.0;

        // Signal conditioning parameters
        [ObservableProperty]
        private double _scaleFromMin = 0.0;

        [ObservableProperty]
        private double _scaleFromMax = 100.0;

        [ObservableProperty]
        private double _scaleToMin = 0.0;

        [ObservableProperty]
        private double _scaleToMax = 100.0;

        [ObservableProperty]
        private bool _scaleClamp = true;

        [ObservableProperty]
        private string _edgeDetectDirection = "Rising";

        [ObservableProperty]
        private int _maWindowSize = 10;

        /// <summary>
        /// Formatted display of the node's secondary output ports (e.g. "Fault: OFF · Speed: 42.5"),
        /// refreshed by the simulation service each tick. Empty when the block has a single output.
        /// </summary>
        [ObservableProperty]
        private string _secondaryOutputText = string.Empty;

        /// <summary>
        /// Runtime-only reason this block is not producing fresh output: a per-tick
        /// evaluation failure ("..."), a failed output write, or the editor's loop-lock
        /// marker. Null when the block is healthy. Rendered as a red node border and a
        /// warning badge with a tooltip. Refreshed by the simulation service each tick
        /// (cleared on stop), so it is never persisted.
        /// </summary>
        [JsonIgnore]
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasError))]
        private string? _errorText;

        /// <summary>
        /// True while <see cref="ErrorText"/> carries a reason; drives the node's
        /// error styling without a null-check converter in XAML.
        /// </summary>
        public bool HasError => !string.IsNullOrEmpty(ErrorText);

        /// <summary>
        /// Replaces the error reason without raising a change when the text is
        /// unchanged (the simulation service pushes it every tick).
        /// </summary>
        public void SetErrorText(string? errorText)
        {
            if (ErrorText == errorText) return;
            ErrorText = errorText;
        }

        /// <summary>
        /// Cached integer value from the last simulation tick (used by the two-phase evaluator).
        /// </summary>
        public int IntValue { get; set; } = 0;

        /// <summary>
        /// Editor fields for this node's configurable parameters, built by the editor view model
        /// from the function block's <c>IFunctionBlock.Parameters</c> declaration.
        /// </summary>
        [JsonIgnore]
        public IReadOnlyList<ParameterField>? ParameterFields { get; set; }

        /// <summary>
        /// The block's real port names for the node's connector slots, resolved by the editor
        /// view model from the function block catalog. Rendered as canvas pin labels and
        /// tooltips. Rebuilt when the node's block type changes.
        /// </summary>
        [ObservableProperty]
        private NodePortLabels? _portLabels;

        /// <summary>
        /// Replaces the secondary-output display text (no-op when unchanged).
        /// </summary>
        public void SetSecondaryOutputs(IReadOnlyList<KeyValuePair<string, string>> namedValues)
        {
            var text = string.Join("  \u00B7  ", namedValues.Select(kv => $"{kv.Key}: {kv.Value}"));
            if (SecondaryOutputText != text)
                SecondaryOutputText = text;
        }
        
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

        public bool HasOutput => !NodeDescriptors.Get(ElementType).IsOutput;

        /// <summary>
        /// True when a wire can leave this node from the given connector: the generic
        /// "Output" connector (which stands for the block's primary output port, e.g.
        /// "Q" or "Running") or one of the block's declared output port names.
        /// </summary>
        public bool HasOutputConnector(string? connectorName)
        {
            if (connectorName is null) return false;
            if (connectorName == "Output") return OutputPortNames.Count > 0;
            return OutputPortNames.Contains(connectorName, StringComparer.Ordinal);
        }

        // Cached handler so we can unsubscribe from the previous PlcAddressReference instance.
        private PropertyChangedEventHandler? _addressPropertyChangedHandler;
        private PlcAddressReference? _subscribedInput1Address;
        private PlcAddressReference? _subscribedOutputAddress;

        partial void OnElementTypeChanged(PlcElementType value)
        {
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(AddressDisplay));
            OnPropertyChanged(nameof(HasOutput));
            OnPropertyChanged(nameof(HasSecondInput));
            OnPropertyChanged(nameof(HasParameters));
            OnPropertyChanged(nameof(ParameterDisplay));

            // Reset to the default single output port. The view model will refresh
            // this from the catalog descriptor once the node is attached/loaded.
            OutputPortNames = new ObservableCollection<string>(new[] { "Output" });
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

        partial void OnValveTravelTimeMsChanged(int value)
        {
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(ParameterDisplay));
        }

        partial void OnValveNormallyOpenChanged(bool value)
        {
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(ParameterDisplay));
        }

        partial void OnMotorDolRunDelayMsChanged(int value)
        {
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(ParameterDisplay));
        }

        partial void OnVsdMaxSpeedChanged(double value)
        {
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(ParameterDisplay));
        }

        partial void OnVsdRampUpMsChanged(int value)
        {
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(ParameterDisplay));
        }

        partial void OnVsdRampDownMsChanged(int value)
        {
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(ParameterDisplay));
        }

        partial void OnVsdAtSpeedToleranceChanged(double value)
        {
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(ParameterDisplay));
        }

        partial void OnCompareValueChanged(int value)
        {
            OnPropertyChanged(nameof(ParameterDisplay));
        }

        partial void OnScaleFromMinChanged(double value)
        {
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(ParameterDisplay));
        }

        partial void OnScaleFromMaxChanged(double value)
        {
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(ParameterDisplay));
        }

        partial void OnScaleToMinChanged(double value)
        {
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(ParameterDisplay));
        }

        partial void OnScaleToMaxChanged(double value)
        {
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(ParameterDisplay));
        }

        partial void OnScaleClampChanged(bool value)
        {
            OnPropertyChanged(nameof(ParameterDisplay));
        }

        partial void OnEdgeDetectDirectionChanged(string value)
        {
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(ParameterDisplay));
        }

        partial void OnMaWindowSizeChanged(int value)
        {
            OnPropertyChanged(nameof(DisplayName));
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
        private bool _showLiveValues = true;

        /// <summary>
        /// Simulation scan period in milliseconds (clamped to the supported range by the service).
        /// </summary>
        [ObservableProperty]
        private int _scanIntervalMs = 100;

        
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
