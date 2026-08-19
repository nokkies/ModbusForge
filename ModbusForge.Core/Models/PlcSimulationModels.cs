using System;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ModbusForge.Models
{
    /// <summary>
    /// Types of PLC simulation elements
    /// </summary>
    public enum PlcElementType
    {
        Input,   // Input block - reads from a Modbus address
        Output,  // Output block - writes to a Modbus address
        InputBool,   // Boolean Input - reads coils/discrete inputs
        InputInt,    // Integer Input - reads holding/input registers
        OutputBool,  // Boolean Output - writes coils/discrete outputs  
        OutputInt,   // Integer Output - writes holding/input registers
        NOT,     // Boolean inversion
        AND,     // Logical AND of multiple inputs
        OR,      // Logical OR of multiple inputs
        RS,      // Set-Reset latch (flip-flop)
        TON,     // Timer On-Delay
        TOF,     // Timer Off-Delay
        TP,      // Timer Pulse
        // Counters
        CTU,     // Counter Up - count rising edges
        CTD,     // Counter Down - count falling edges
        CTC,     // Counter Up/Down - combined with direction
        // Comparators
        COMPARE_EQ,   // Equal
        COMPARE_NE,   // Not Equal
        COMPARE_GT,   // Greater Than
        COMPARE_LT,   // Less Than
        COMPARE_GE,   // Greater Than or Equal
        COMPARE_LE,   // Less Than or Equal
        // Math operations
        MATH_ADD,     // Addition
        MATH_SUB,     // Subtraction
        MATH_MUL,     // Multiplication
        MATH_DIV,      // Division
        // Real (double) math operations
        MATH_ADD_REAL,     // Addition (Real)
        MATH_SUB_REAL,     // Subtraction (Real)
        MATH_MUL_REAL,     // Multiplication (Real)
        MATH_DIV_REAL,     // Division (Real)
        // Real (double) comparators
        COMPARE_EQ_REAL,   // Equal (Real)
        COMPARE_NE_REAL,   // Not Equal (Real)
        COMPARE_GT_REAL,   // Greater Than (Real)
        COMPARE_LT_REAL,   // Less Than (Real)
        COMPARE_GE_REAL,   // Greater Than or Equal (Real)
        COMPARE_LE_REAL,   // Less Than or Equal (Real)
        SignalGenerator, // Signal Generator
        SignalGeneratorReal, // Signal Generator (Real output)
        // Industrial devices
        Valve,       // Motorised valve with open/close commands and travel time
        MotorDol,    // Direct-on-line motor with start/stop and pickup delay
        Vsd,         // Variable speed drive with ramped speed feedback
        // Signal conditioning
        // (appended, not inserted: simulation files persist the element type as
        // its numeric enum value, so existing members must keep their numbers)
        Scale,         // Linear scaling of an analog value (LIN)
        EdgeDetect,    // One-cycle pulse on the selected input transition
        MovingAverage  // Windowed moving average (MOVAVG)
    }

    /// <summary>
    /// Modbus address areas that can be referenced
    /// </summary>
    public enum PlcArea
    {
        HoldingRegister,
        Coil,
        InputRegister,
        DiscreteInput
    }

    /// <summary>
    /// Reference to a Modbus address with optional NOT bubble and symbolic addressing
    /// </summary>
    public partial class PlcAddressReference : ObservableObject
    {
        [ObservableProperty]
        private PlcArea _area = PlcArea.Coil;

        [ObservableProperty]
        private int _address = 1;

        [ObservableProperty]
        private bool _not = false;

        /// <summary>
        /// Symbolic tag name (optional). If set, overrides numeric area/address.
        /// </summary>
        [ObservableProperty]
        private string? _symbolicName;

        /// <summary>
        /// Display string showing either symbolic name or numeric address
        /// </summary>
        [JsonIgnore]
        public string DisplayAddress
        {
            get
            {
                if (!string.IsNullOrEmpty(SymbolicName))
                    return SymbolicName;
                return $"{Area}:{Address}";
            }
        }

        /// <summary>
        /// Returns true if this reference uses symbolic addressing
        /// </summary>
        [JsonIgnore]
        public bool IsSymbolic => !string.IsNullOrEmpty(SymbolicName);

        /// <summary>
        /// Creates a deep copy of this address reference so cloned owners do not
        /// share mutable address state.
        /// </summary>
        public PlcAddressReference Clone()
        {
            return new PlcAddressReference
            {
                Area = Area,
                Address = Address,
                Not = Not,
                SymbolicName = SymbolicName
            };
        }
    }

    /// <summary>
    /// A PLC simulation element that can read from inputs, process logic, and write to outputs
    /// </summary>
    public partial class PlcSimulationElement : ObservableObject
    {
        [ObservableProperty]
        private string _id = Guid.NewGuid().ToString();

        [ObservableProperty]
        private PlcElementType _elementType = PlcElementType.Input;

        // Input references (up to 2 for AND/OR, 1 for others)
        [ObservableProperty]
        private PlcAddressReference _input1 = new PlcAddressReference();

        [ObservableProperty]
        private PlcAddressReference _input2 = new PlcAddressReference();

        // Optional output address
        [ObservableProperty]
        private PlcAddressReference _output = new PlcAddressReference();

        // Timer preset in milliseconds
        [ObservableProperty]
        private int _timerPresetMs = 1000;

        // RS latch configuration: true = Set dominant, false = Reset dominant
        [ObservableProperty]
        private bool _setDominant = true;

        // Counter preset (for CTU, CTD, CTC)
        [ObservableProperty]
        private int _counterPreset = 10;

        // Compare value (for comparators)
        [ObservableProperty]
        private int _compareValue = 0;

        // For timer state tracking (not persisted)
        public int TimerAccumulatorMs { get; set; } = 0;
        public bool TimerLastInput { get; set; } = false;
        public bool TimerOutput { get; set; } = false;
        public bool RsState { get; set; } = false;

        // For counter state tracking (not persisted)
        public int CounterValue { get; set; } = 0;
        public bool CounterLastInput { get; set; } = false;
    }

    /// <summary>
    /// Configuration for the PLC simulation system
    /// </summary>
    public partial class PlcSimulationConfig : ObservableObject
    {
        [ObservableProperty]
        private bool _enabled = false;

        [ObservableProperty]
        private int _periodMs = 100;

        [ObservableProperty]
        private ObservableCollection<PlcSimulationElement> _elements = new ObservableCollection<PlcSimulationElement>();
    }
}
