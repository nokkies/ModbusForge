using CommunityToolkit.Mvvm.ComponentModel;
using ModbusForge.Services;
using System;

namespace ModbusForge.ViewModels.Coordinators
{
    public partial class SimulationCoordinator : ViewModelBase
    {
        private readonly ISimulationService _simulationService;

        public SimulationCoordinator(ISimulationService simulationService)
        {
            _simulationService = simulationService ?? throw new ArgumentNullException(nameof(simulationService));
        }

        public void Start()
        {
            _simulationService.Start(this);
        }

        public void Stop()
        {
            _simulationService.Stop();
        }

        // Simulation configuration
        [ObservableProperty]
        private bool _simulationEnabled = false;

        [ObservableProperty]
        private int _simulationPeriodMs = 500;

        // Holding Registers ramp
        [ObservableProperty]
        private bool _simHoldingsEnabled = false;

        [ObservableProperty]
        private int _simHoldingStart = 1;

        [ObservableProperty]
        private int _simHoldingCount = 4;

        [ObservableProperty]
        private int _simHoldingMin = 0;

        [ObservableProperty]
        private int _simHoldingMax = 100;

        // Holding Registers waveform parameters
        [ObservableProperty]
        private string _simHoldingWaveformType = "Ramp"; // Ramp, Sine, Triangle, Square

        [ObservableProperty]
        private double _simHoldingAmplitude = 1000.0;

        [ObservableProperty]
        private double _simHoldingFrequencyHz = 0.5;

        [ObservableProperty]
        private double _simHoldingOffset = 0.0;

        // Coils toggle
        [ObservableProperty]
        private bool _simCoilsEnabled = false;

        [ObservableProperty]
        private int _simCoilStart = 1;

        [ObservableProperty]
        private int _simCoilCount = 8;

        // Input Registers ramp
        [ObservableProperty]
        private bool _simInputsEnabled = false;

        [ObservableProperty]
        private int _simInputStart = 1;

        [ObservableProperty]
        private int _simInputCount = 4;

        [ObservableProperty]
        private int _simInputMin = 0;

        [ObservableProperty]
        private int _simInputMax = 100;

        // Discrete Inputs toggle
        [ObservableProperty]
        private bool _simDiscreteEnabled = false;

        [ObservableProperty]
        private int _simDiscreteStart = 1;

        [ObservableProperty]
        private int _simDiscreteCount = 8;
    }
}
