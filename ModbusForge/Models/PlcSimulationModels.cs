using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ModbusForge.Models
{
    /// <summary>
    /// Types of PLC simulation elements
    /// </summary>
    public enum PlcElementType
    {
        Source,  // Read value from a Modbus address
        NOT,     // Boolean inversion
        AND,     // Logical AND of multiple inputs
        OR,      // Logical OR of multiple inputs
        RS,      // Set-Reset latch (flip-flop)
        TON,     // Timer On-Delay
        TOF,     // Timer Off-Delay
        TP       // Timer Pulse
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
    /// Reference to a Modbus address with optional NOT bubble
    /// </summary>
    public partial class PlcAddressReference : ObservableObject
    {
        [ObservableProperty]
        private PlcArea _area = PlcArea.Coil;

        [ObservableProperty]
        private int _address = 0;

        [ObservableProperty]
        private bool _not = false;
    }

    /// <summary>
    /// A PLC simulation element that can read from inputs, process logic, and write to outputs
    /// </summary>
    public partial class PlcSimulationElement : ObservableObject
    {
        [ObservableProperty]
        private string _id = Guid.NewGuid().ToString();

        [ObservableProperty]
        private PlcElementType _elementType = PlcElementType.Source;

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

        // For timer state tracking (not persisted)
        public int TimerAccumulatorMs { get; set; } = 0;
        public bool TimerLastInput { get; set; } = false;
        public bool TimerOutput { get; set; } = false;
        public bool RsState { get; set; } = false;
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
