using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModbusForge.Models;
using ModbusForge.Services;
using System;
using System.Collections.ObjectModel;

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

        // PLC simulation parameters
        [ObservableProperty]
        private bool _plcSimulationEnabled = false;

        [ObservableProperty]
        private int _plcSimulationPeriodMs = 100;

        [ObservableProperty]
        private ObservableCollection<PlcSimulationElement> _plcSimulationElements = new ObservableCollection<PlcSimulationElement>();

        [RelayCommand]
        private void AddPlcElement()
        {
            PlcSimulationElements.Add(new PlcSimulationElement());
        }

        [RelayCommand]
        private void RemovePlcElement(object param)
        {
            if (param is PlcSimulationElement element)
            {
                PlcSimulationElements.Remove(element);
            }
        }

        [RelayCommand]
        private void ClearPlcElements()
        {
            PlcSimulationElements.Clear();
        }

        // Enum collections for UI binding
        public PlcElementType[] PlcElementTypes => (PlcElementType[])Enum.GetValues(typeof(PlcElementType));
        public PlcArea[] PlcAreas => (PlcArea[])Enum.GetValues(typeof(PlcArea));
    }
}
