using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
        
        public string DisplayName
        {
            get
            {
                return ElementType switch
                {
                    PlcElementType.Input => "IN",
                    PlcElementType.Output => "OUT",
                    PlcElementType.InputBool => "IN BOOL",
                    PlcElementType.InputInt => "IN INT",
                    PlcElementType.OutputBool => "OUT BOOL",
                    PlcElementType.OutputInt => "OUT INT",
                    PlcElementType.NOT => "NOT",
                    PlcElementType.AND => "AND",
                    PlcElementType.OR => "OR",
                    PlcElementType.RS => "RS Latch",
                    PlcElementType.TON => $"TON ({TimerPresetMs}ms)",
                    PlcElementType.TOF => $"TOF ({TimerPresetMs}ms)",
                    PlcElementType.TP => $"TP ({TimerPresetMs}ms)",
                    PlcElementType.CTU => $"CTU ({CounterPreset})",
                    PlcElementType.CTD => $"CTD ({CounterPreset})",
                    PlcElementType.CTC => $"CTC ({CounterPreset})",
                    PlcElementType.COMPARE_EQ => "EQ",
                    PlcElementType.COMPARE_NE => "NE",
                    PlcElementType.COMPARE_GT => "GT",
                    PlcElementType.COMPARE_LT => "LT",
                    PlcElementType.COMPARE_GE => "GE",
                    PlcElementType.COMPARE_LE => "LE",
                    PlcElementType.MATH_ADD => "ADD",
                    PlcElementType.MATH_SUB => "SUB",
                    PlcElementType.MATH_MUL => "MUL",
                    PlcElementType.MATH_DIV => "DIV",
                    _ => "Unknown"
                };
            }
        }
        
        public string AddressDisplay
        {
            get
            {
                return ElementType switch
                {
                    PlcElementType.Input => Input1Address.Address >= 0 ? $"{Input1Address.Area}:{Input1Address.Address}" : "[Not Configured]",
                    PlcElementType.Output => OutputAddress.Address >= 0 ? $"{OutputAddress.Area}:{OutputAddress.Address}" : "[Not Configured]",
                    PlcElementType.InputBool => Input1Address.Address >= 0 ? $"{Input1Address.Area}:{Input1Address.Address}" : "[Not Configured]",
                    PlcElementType.InputInt => Input1Address.Address >= 0 ? $"{Input1Address.Area}:{Input1Address.Address}" : "[Not Configured]",
                    PlcElementType.OutputBool => OutputAddress.Address >= 0 ? $"{OutputAddress.Area}:{OutputAddress.Address}" : "[Not Configured]",
                    PlcElementType.OutputInt => OutputAddress.Address >= 0 ? $"{OutputAddress.Area}:{OutputAddress.Address}" : "[Not Configured]",
                    _ => ""
                };
            }
        }
        
        public bool HasSecondInput
        {
            get
            {
                return ElementType == PlcElementType.AND || 
                       ElementType == PlcElementType.OR || 
                       ElementType == PlcElementType.RS ||
                       ElementType == PlcElementType.CTC ||
                       ElementType == PlcElementType.COMPARE_EQ ||
                       ElementType == PlcElementType.COMPARE_NE ||
                       ElementType == PlcElementType.COMPARE_GT ||
                       ElementType == PlcElementType.COMPARE_LT ||
                       ElementType == PlcElementType.COMPARE_GE ||
                       ElementType == PlcElementType.COMPARE_LE ||
                       ElementType == PlcElementType.MATH_ADD ||
                       ElementType == PlcElementType.MATH_SUB ||
                       ElementType == PlcElementType.MATH_MUL ||
                       ElementType == PlcElementType.MATH_DIV;
            }
        }
        
        public bool HasParameters
        {
            get
            {
                return ElementType == PlcElementType.TON ||
                       ElementType == PlcElementType.TOF ||
                       ElementType == PlcElementType.TP ||
                       ElementType == PlcElementType.CTU ||
                       ElementType == PlcElementType.CTD ||
                       ElementType == PlcElementType.CTC ||
                       ElementType == PlcElementType.COMPARE_EQ ||
                       ElementType == PlcElementType.COMPARE_NE ||
                       ElementType == PlcElementType.COMPARE_GT ||
                       ElementType == PlcElementType.COMPARE_LT ||
                       ElementType == PlcElementType.COMPARE_GE ||
                       ElementType == PlcElementType.COMPARE_LE ||
                       ElementType == PlcElementType.MATH_ADD ||
                       ElementType == PlcElementType.MATH_SUB ||
                       ElementType == PlcElementType.MATH_MUL ||
                       ElementType == PlcElementType.MATH_DIV;
            }
        }
        
        public string ParameterDisplay
        {
            get
            {
                return ElementType switch
                {
                    PlcElementType.TON or PlcElementType.TOF or PlcElementType.TP => $"{TimerPresetMs}ms",
                    PlcElementType.CTU or PlcElementType.CTD or PlcElementType.CTC => $"Preset: {CounterPreset}",
                    PlcElementType.COMPARE_EQ or PlcElementType.COMPARE_NE or PlcElementType.COMPARE_GT or 
                    PlcElementType.COMPARE_LT or PlcElementType.COMPARE_GE or PlcElementType.COMPARE_LE => $"Value: {CompareValue}",
                    PlcElementType.MATH_ADD or PlcElementType.MATH_SUB or PlcElementType.MATH_MUL or PlcElementType.MATH_DIV => $"Const: {CompareValue}",
                    _ => ""
                };
            }
        }
        
        public bool HasOutput => ElementType != PlcElementType.Input;
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
