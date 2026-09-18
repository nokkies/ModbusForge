using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ModbusForge.Models
{
    /// <summary>
    /// IEC 61131-3 Data Types
    /// </summary>
    public enum IecDataType
    {
        BOOL,
        INT,
        DINT,
        REAL,
        STRING,
        WORD,
        DWORD,
        TIME,
        DATE,
        TOD,
        DT
    }

    /// <summary>
    /// Represents a tag with force state and runtime information for online editing
    /// </summary>
    public partial class PlcTag : ObservableObject
    {
        [ObservableProperty]
        private string _id = Guid.NewGuid().ToString();

        [ObservableProperty]
        private string _name = "";

        [ObservableProperty]
        private string _comment = "";

        [ObservableProperty]
        private IecDataType _dataType = IecDataType.BOOL;

        [ObservableProperty]
        private PlcArea _area = PlcArea.Coil;

        [ObservableProperty]
        private int _address = 0;

        [ObservableProperty]
        private object? _currentValue;

        [ObservableProperty]
        private object? _forcedValue;

        [ObservableProperty]
        private bool _isForced;

        [ObservableProperty]
        private bool _isOnline;

        [ObservableProperty]
        private DateTime _lastUpdated;

        [ObservableProperty]
        private TagQuality _quality = TagQuality.Good;

        [JsonIgnore]
        public string DisplayValue => CurrentValue?.ToString() ?? "null";

        [JsonIgnore]
        public string FullAddress => $"{Area}:{Address}";

        public void SetCurrentValue(object value)
        {
            CurrentValue = value;
            LastUpdated = DateTime.Now;
            Quality = TagQuality.Good;
        }

        public void ForceValue(object value)
        {
            ForcedValue = value;
            IsForced = true;
            CurrentValue = value;
        }

        public void ReleaseForce()
        {
            IsForced = false;
            ForcedValue = null;
        }

        public void ToggleBoolValue()
        {
            if (DataType == IecDataType.BOOL && CurrentValue is bool b)
            {
                SetCurrentValue(!b);
            }
        }
    }

    /// <summary>
    /// Tag quality indicator for online operations
    /// </summary>
    public enum TagQuality
    {
        Good,
        Bad,
        Uncertain,
        NotConnected
    }

    /// <summary>
    /// Ladder diagram rung representation
    /// </summary>
    public partial class LadderRung : ObservableObject
    {
        [ObservableProperty]
        private string _id = Guid.NewGuid().ToString();

        [ObservableProperty]
        private int _rungNumber;

        [ObservableProperty]
        private string _comment = "";

        [ObservableProperty]
        private ObservableCollection<LadderElement> _elements = new();

        [ObservableProperty]
        private CoilElement? _coil;

        [JsonIgnore]
        public bool HasCoil => Coil != null;

        public void AddContact(PlcElementType type, string address, bool normallyClosed = false, int position = -1)
        {
            var contact = new ContactElement
            {
                ElementType = type,
                Address = address,
                NormallyClosed = normallyClosed,
                Position = position >= 0 ? position : Elements.Count
            };
            
            if (position >= 0 && position < Elements.Count)
                Elements.Insert(position, contact);
            else
                Elements.Add(contact);
        }

        public void SetCoil(CoilElement coil)
        {
            Coil = coil;
        }
    }

    /// <summary>
    /// Base class for ladder diagram elements
    /// </summary>
    public abstract partial class LadderElement : ObservableObject
    {
        [ObservableProperty]
        private string _id = Guid.NewGuid().ToString();

        [ObservableProperty]
        private int _position;

        [ObservableProperty]
        private string _address = "";

        [ObservableProperty]
        private bool _isActive;
    }

    /// <summary>
    /// Contact element (NO or NC) in ladder logic
    /// </summary>
    public partial class ContactElement : LadderElement
    {
        [ObservableProperty]
        private PlcElementType _elementType = PlcElementType.Input;

        [ObservableProperty]
        private bool _normallyClosed;

        [JsonIgnore]
        public bool IsNormallyOpen => !NormallyClosed;
    }

    /// <summary>
    /// Coil element in ladder logic
    /// </summary>
    public partial class CoilElement : LadderElement
    {
        [ObservableProperty]
        private PlcElementType _elementType = PlcElementType.Output;

        [ObservableProperty]
        private bool _negated;

        [ObservableProperty]
        private bool _setReset; // True for set/reset coils

        [ObservableProperty]
        private bool _isSetCoil; // True for set, false for reset
    }

    /// <summary>
    /// Function block element in ladder logic
    /// </summary>
    public partial class FunctionBlockElement : LadderElement
    {
        [ObservableProperty]
        private PlcElementType _blockType = PlcElementType.TON;

        [ObservableProperty]
        private Dictionary<string, object> _parameters = new();

        [ObservableProperty]
        private string _instanceName = "";
    }

    /// <summary>
    /// SFC Step representation
    /// </summary>
    public partial class SfcStep : ObservableObject
    {
        [ObservableProperty]
        private string _id = Guid.NewGuid().ToString();

        [ObservableProperty]
        private string _name = "";

        [ObservableProperty]
        private int _stepNumber;

        [ObservableProperty]
        private bool _isActive;

        [ObservableProperty]
        private bool _isInitial;

        [ObservableProperty]
        private ObservableCollection<SfcAction> _actions = new();

        [ObservableProperty]
        private List<string> _transitionsTo = new();

        [ObservableProperty]
        private double _x;

        [ObservableProperty]
        private double _y;

        public void AddAction(string actionName, SfcActionQualifier qualifier = SfcActionQualifier.NonStored)
        {
            Actions.Add(new SfcAction
            {
                Name = actionName,
                Qualifier = qualifier
            });
        }
    }

    /// <summary>
    /// SFC Transition representation
    /// </summary>
    public partial class SfcTransition : ObservableObject
    {
        [ObservableProperty]
        private string _id = Guid.NewGuid().ToString();

        [ObservableProperty]
        private string _condition = "";

        [ObservableProperty]
        private bool _isActive;

        [ObservableProperty]
        private string _sourceStepId = "";

        [ObservableProperty]
        private string _targetStepId = "";

        [ObservableProperty]
        private double _x;

        [ObservableProperty]
        private double _y;
    }

    /// <summary>
    /// SFC Action with qualifier
    /// </summary>
    public partial class SfcAction : ObservableObject
    {
        [ObservableProperty]
        private string _name = "";

        [ObservableProperty]
        private SfcActionQualifier _qualifier = SfcActionQualifier.NonStored;

        [ObservableProperty]
        private bool _isActive;

        [ObservableProperty]
        private TimeSpan _delayTime;

        [ObservableProperty]
        private TimeSpan _durationTime;
    }

    /// <summary>
    /// IEC 61131-3 SFC Action Qualifiers
    /// </summary>
    public enum SfcActionQualifier
    {
        NonStored,    // N - Normal
        Stored,       // S - Set
        Reset,        // R - Reset
        LeadingEdge,  // P - Pulse at rising edge
        TrailingEdge, // M - Pulse at falling edge
        Delayed,      // D - Time delay
        Limited,      // L - Time limited
        Pulse         // P - Pulse
    }

    /// <summary>
    /// Complete SFC Chart
    /// </summary>
    public partial class SfcChart : ObservableObject
    {
        [ObservableProperty]
        private string _id = Guid.NewGuid().ToString();

        [ObservableProperty]
        private string _name = "";

        [ObservableProperty]
        private ObservableCollection<SfcStep> _steps = new();

        [ObservableProperty]
        private ObservableCollection<SfcTransition> _transitions = new();

        [ObservableProperty]
        private string _initialStepId = "";

        public SfcStep? GetInitialStep()
        {
            if (string.IsNullOrEmpty(InitialStepId))
                return Steps.FirstOrDefault(s => s.IsInitial);
            return Steps.FirstOrDefault(s => s.Id == InitialStepId);
        }
    }

    /// <summary>
    /// Program Organization Unit (POU) types per IEC 61131-3
    /// </summary>
    public enum PouType
    {
        Program,
        FunctionBlock,
        Function
    }

    /// <summary>
    /// IEC 61131-3 Program Organization Unit
    /// </summary>
    public partial class Pou : ObservableObject
    {
        [ObservableProperty]
        private string _id = Guid.NewGuid().ToString();

        [ObservableProperty]
        private string _name = "";

        [ObservableProperty]
        private PouType _type = PouType.Program;

        [ObservableProperty]
        private string _implementationLanguage = "FBD"; // FBD, LD, SFC, ST, IL

        [ObservableProperty]
        private ObservableCollection<PlcTag> _localTags = new();

        [ObservableProperty]
        private VisualNodeEditorConfig? _fbdImplementation;

        [ObservableProperty]
        private ObservableCollection<LadderRung> _ladderImplementation = new();

        [ObservableProperty]
        private SfcChart? _sfcImplementation;

        [ObservableProperty]
        private string _stImplementation = "";

        [ObservableProperty]
        private bool _isPersistent;

        [ObservableProperty]
        private string _comment = "";
    }

    /// <summary>
    /// XEF Project file structure
    /// </summary>
    public partial class XefProject
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = "1.0";

        [JsonPropertyName("projectInfo")]
        public XefProjectInfo ProjectInfo { get; set; } = new();

        [JsonPropertyName("globalVariables")]
        public ObservableCollection<PlcTag> GlobalVariables { get; set; } = new();

        [JsonPropertyName("pous")]
        public ObservableCollection<Pou> Pous { get; set; } = new();

        [JsonPropertyName("configurations")]
        public ObservableCollection<XefConfiguration> Configurations { get; set; } = new();
    }

    /// <summary>
    /// XEF Project metadata
    /// </summary>
    public partial class XefProjectInfo
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "Untitled";

        [JsonPropertyName("description")]
        public string Description { get; set; } = "";

        [JsonPropertyName("vendor")]
        public string Vendor { get; set; } = "ModbusForge";

        [JsonPropertyName("created")]
        public DateTime Created { get; set; } = DateTime.Now;

        [JsonPropertyName("modified")]
        public DateTime Modified { get; set; } = DateTime.Now;

        [JsonPropertyName("iecVersion")]
        public string IecVersion { get; set; } = "61131-3";
    }

    /// <summary>
    /// XEF Configuration (resources, tasks, etc.)
    /// </summary>
    public partial class XefConfiguration
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "Config1";

        [JsonPropertyName("resources")]
        public ObservableCollection<XefResource> Resources { get; set; } = new();
    }

    /// <summary>
    /// XEF Resource definition
    /// </summary>
    public partial class XefResource
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "Resource1";

        [JsonPropertyName("tasks")]
        public ObservableCollection<XefTask> Tasks { get; set; } = new();

        [JsonPropertyName("globalVars")]
        public ObservableCollection<PlcTag> GlobalVars { get; set; } = new();
    }

    /// <summary>
    /// XEF Task definition
    /// </summary>
    public partial class XefTask
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "Task1";

        [JsonPropertyName("intervalMs")]
        public int IntervalMs { get; set; } = 100;

        [JsonPropertyName("priority")]
        public int Priority { get; set; } = 1;

        [JsonPropertyName("programName")]
        public string ProgramName { get; set; } = "";
    }
}
