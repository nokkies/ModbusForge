using System;
using System.Windows.Threading;
using ModbusForge.ViewModels;
using Microsoft.Extensions.Logging;
using ModbusForge.Models;
using Modbus.Data;

namespace ModbusForge.Services
{
    public class SimulationService : ISimulationService, IDisposable
    {
        private MainViewModel? _viewModel;
        private readonly ModbusServerService _serverService;
        private readonly ILogger<SimulationService> _logger;
        private DispatcherTimer? _simulationTimer;
        private bool _isSimulating;
        private int _simHoldingPhase;
        private bool _simCoilState;
        private bool _simDiscreteState;
        private DateTime _lastSimTickUtc;
        private double _simTimeSec;

        public SimulationService(ModbusServerService serverService, ILogger<SimulationService> logger)
        {
            _serverService = serverService ?? throw new ArgumentNullException(nameof(serverService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void Start(MainViewModel viewModel)
        {
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            if (_simulationTimer == null)
            {
                _simulationTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(Math.Max(50, _viewModel.SimulationPeriodMs))
                };
                _simulationTimer.Tick += SimulationTimer_Tick;
            }
            else
            {
                _simulationTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(50, _viewModel.SimulationPeriodMs));
            }
            _lastSimTickUtc = DateTime.UtcNow;
            _simulationTimer.Start();
        }

        public void Stop()
        {
            _simulationTimer?.Stop();
        }

        private void SimulationTimer_Tick(object? sender, EventArgs e)
        {
            if (_viewModel == null) return;
            if (_isSimulating) return;
            if (!_viewModel.IsConnected) return;
            if (!_viewModel.IsServerMode) return;
            if (!_viewModel.SimulationEnabled) return;

            _isSimulating = true;
            try
            {
                var nowSim = DateTime.UtcNow;
                double dt = (nowSim - _lastSimTickUtc).TotalSeconds;
                if (dt < 0 || dt > 5) dt = 0;
                _simTimeSec += dt;
                _lastSimTickUtc = nowSim;

                // Run legacy waveform simulation
                RunLegacySimulation(dt);

                // Run PLC simulation if enabled
                if (_viewModel.PlcSimulationEnabled)
                {
                    RunPlcSimulation((int)(dt * 1000));
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Simulation tick failed");
            }
            finally
            {
                _isSimulating = false;
            }
        }

        private void RunLegacySimulation(double dt)
        {
            if (_viewModel == null) return;

            if (_viewModel.SimHoldingsEnabled)
            {
                int count = _viewModel.SimHoldingCount <= 0 ? 0 : _viewModel.SimHoldingCount;
                int start = _viewModel.SimHoldingStart;
                int min = _viewModel.SimHoldingMin;
                int max = _viewModel.SimHoldingMax;
                string wf = (_viewModel.SimHoldingWaveformType ?? "Ramp").ToLowerInvariant();
                if ((wf == "sine" || wf == "triangle" || wf == "square") && _viewModel.SimHoldingFrequencyHz > 0)
                {
                    double f = _viewModel.SimHoldingFrequencyHz;
                    double x = 2.0 * Math.PI * f * _simTimeSec;
                    double w;
                    if (wf == "sine") w = Math.Sin(x);
                    else if (wf == "triangle")
                    {
                        double phase = f * _simTimeSec;
                        phase -= Math.Floor(phase);
                        w = 4.0 * Math.Abs(phase - 0.5) - 1.0;
                    }
                    else w = Math.Sin(x) >= 0 ? 1.0 : -1.0;

                    double raw = _viewModel.SimHoldingOffset + (_viewModel.SimHoldingAmplitude * w);
                    int iv = (int)Math.Round(raw);
                    if (iv < 0) iv = 0;
                    if (iv > 65535) iv = 65535;
                    var dataStore = _serverService.GetDataStore();
                    if (dataStore != null)
                    {
                        for (int i = 0; i < count; i++)
                        {
                            int addr = start + i;
                            if (addr >= 0 && addr < dataStore.HoldingRegisters.Count)
                                dataStore.HoldingRegisters[addr] = (ushort)iv;
                        }
                    }
                }
                else
                {
                    var dataStore = _serverService.GetDataStore();
                    if (dataStore != null)
                    {
                        int range = Math.Max(1, max - min + 1);
                        _simHoldingPhase = (_simHoldingPhase + 1) % range;
                        for (int i = 0; i < count; i++)
                        {
                            int val = min + ((_simHoldingPhase + i) % range);
                            if (val < 0) val = 0;
                            if (val > 65535) val = 65535;
                            int addr = start + i;
                            if (addr >= 0 && addr < dataStore.HoldingRegisters.Count)
                                dataStore.HoldingRegisters[addr] = (ushort)val;
                        }
                    }
                }
            }

            if (_viewModel.SimCoilsEnabled)
            {
                var dataStore = _serverService.GetDataStore();
                if (dataStore != null)
                {
                    int count = _viewModel.SimCoilCount <= 0 ? 0 : _viewModel.SimCoilCount;
                    int start = _viewModel.SimCoilStart;
                    for (int i = 0; i < count; i++)
                    {
                        int addr = start + i;
                        if (addr >= 0 && addr < dataStore.CoilDiscretes.Count)
                            dataStore.CoilDiscretes[addr] = _simCoilState;
                    }
                    _simCoilState = !_simCoilState;
                }
            }

            if (_viewModel.SimInputsEnabled)
            {
                var dataStore = _serverService.GetDataStore();
                if (dataStore != null)
                {
                    int count = _viewModel.SimInputCount <= 0 ? 0 : _viewModel.SimInputCount;
                    int start = _viewModel.SimInputStart;
                    int min = _viewModel.SimInputMin;
                    int max = _viewModel.SimInputMax;
                    int range = Math.Max(1, max - min + 1);
                    _simHoldingPhase = (_simHoldingPhase + 1) % range;
                    for (int i = 0; i < count; i++)
                    {
                        int val = min + ((_simHoldingPhase + i) % range);
                        if (val < 0) val = 0;
                        if (val > 65535) val = 65535;
                        int addr = start + i;
                        if (addr >= 0 && addr < dataStore.InputRegisters.Count)
                            dataStore.InputRegisters[addr] = (ushort)val;
                    }
                }
            }

            if (_viewModel.SimDiscreteEnabled)
            {
                var dataStore = _serverService.GetDataStore();
                if (dataStore != null)
                {
                    int count = _viewModel.SimDiscreteCount <= 0 ? 0 : _viewModel.SimDiscreteCount;
                    int start = _viewModel.SimDiscreteStart;
                    for (int i = 0; i < count; i++)
                    {
                        int addr = start + i;
                        if (addr >= 0 && addr < dataStore.InputDiscretes.Count)
                            dataStore.InputDiscretes[addr] = _simDiscreteState;
                    }
                    _simDiscreteState = !_simDiscreteState;
                }
            }
        }

        private void RunPlcSimulation(int elapsedMs)
        {
            if (_viewModel == null) return;

            var dataStore = _serverService.GetDataStore();
            if (dataStore == null) return;

            // Evaluate each PLC element
            foreach (var element in _viewModel.PlcSimulationElements)
            {
                try
                {
                    bool result = EvaluatePlcElement(element, dataStore, elapsedMs);

                    // Write result to output if specified
                    if (element.Output != null && element.Output.Address >= 0)
                    {
                        WritePlcOutput(element.Output, result, dataStore);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "PLC element evaluation failed for {ElementType}", element.ElementType);
                }
            }
        }

        private bool EvaluatePlcElement(PlcSimulationElement element, DataStore dataStore, int elapsedMs)
        {
            switch (element.ElementType)
            {
                case PlcElementType.Source:
                    return EvaluateSource(element.Input1, dataStore);

                case PlcElementType.NOT:
                    return !EvaluateSource(element.Input1, dataStore);

                case PlcElementType.AND:
                    return EvaluateSource(element.Input1, dataStore) && EvaluateSource(element.Input2, dataStore);

                case PlcElementType.OR:
                    return EvaluateSource(element.Input1, dataStore) || EvaluateSource(element.Input2, dataStore);

                case PlcElementType.RS:
                    return EvaluateRsLatch(element, dataStore);

                case PlcElementType.TON:
                    return EvaluateTonTimer(element, dataStore, elapsedMs);

                case PlcElementType.TOF:
                    return EvaluateTofTimer(element, dataStore, elapsedMs);

                case PlcElementType.TP:
                    return EvaluateTpTimer(element, dataStore, elapsedMs);

                // Counters
                case PlcElementType.CTU:
                    return EvaluateCtuCounter(element, dataStore);

                case PlcElementType.CTD:
                    return EvaluateCtdCounter(element, dataStore);

                case PlcElementType.CTC:
                    return EvaluateCtcCounter(element, dataStore);

                // Comparators
                case PlcElementType.COMPARE_EQ:
                    return EvaluateCompareEq(element, dataStore);

                case PlcElementType.COMPARE_NE:
                    return EvaluateCompareNe(element, dataStore);

                case PlcElementType.COMPARE_GT:
                    return EvaluateCompareGt(element, dataStore);

                case PlcElementType.COMPARE_LT:
                    return EvaluateCompareLt(element, dataStore);

                case PlcElementType.COMPARE_GE:
                    return EvaluateCompareGe(element, dataStore);

                case PlcElementType.COMPARE_LE:
                    return EvaluateCompareLe(element, dataStore);

                // Math operations
                case PlcElementType.MATH_ADD:
                    return EvaluateMathAdd(element, dataStore);

                case PlcElementType.MATH_SUB:
                    return EvaluateMathSub(element, dataStore);

                case PlcElementType.MATH_MUL:
                    return EvaluateMathMul(element, dataStore);

                case PlcElementType.MATH_DIV:
                    return EvaluateMathDiv(element, dataStore);

                default:
                    return false;
            }
        }

        private bool EvaluateSource(PlcAddressReference input, DataStore dataStore)
        {
            if (input == null) return false;
            bool value = ReadPlcInput(input, dataStore);
            return input.Not ? !value : value;
        }

        private bool ReadPlcInput(PlcAddressReference input, DataStore dataStore)
        {
            int addr = input.Address;
            if (addr < 0) return false;

            switch (input.Area)
            {
                case PlcArea.Coil:
                    return addr < dataStore.CoilDiscretes.Count && dataStore.CoilDiscretes[addr];

                case PlcArea.DiscreteInput:
                    return addr < dataStore.InputDiscretes.Count && dataStore.InputDiscretes[addr];

                case PlcArea.HoldingRegister:
                    if (addr < dataStore.HoldingRegisters.Count)
                    {
                        // Treat non-zero as true
                        return dataStore.HoldingRegisters[addr] != 0;
                    }
                    return false;

                case PlcArea.InputRegister:
                    if (addr < dataStore.InputRegisters.Count)
                    {
                        // Treat non-zero as true
                        return dataStore.InputRegisters[addr] != 0;
                    }
                    return false;

                default:
                    return false;
            }
        }

        private void WritePlcOutput(PlcAddressReference output, bool value, DataStore dataStore)
        {
            int addr = output.Address;
            if (addr < 0) return;

            bool finalValue = output.Not ? !value : value;

            switch (output.Area)
            {
                case PlcArea.Coil:
                    if (addr < dataStore.CoilDiscretes.Count)
                        dataStore.CoilDiscretes[addr] = finalValue;
                    break;

                case PlcArea.DiscreteInput:
                    if (addr < dataStore.InputDiscretes.Count)
                        dataStore.InputDiscretes[addr] = finalValue;
                    break;

                case PlcArea.HoldingRegister:
                    if (addr < dataStore.HoldingRegisters.Count)
                        dataStore.HoldingRegisters[addr] = finalValue ? (ushort)1 : (ushort)0;
                    break;

                case PlcArea.InputRegister:
                    if (addr < dataStore.InputRegisters.Count)
                        dataStore.InputRegisters[addr] = finalValue ? (ushort)1 : (ushort)0;
                    break;
            }
        }

        private bool EvaluateRsLatch(PlcSimulationElement element, DataStore dataStore)
        {
            bool setInput = EvaluateSource(element.Input1, dataStore);
            bool resetInput = EvaluateSource(element.Input2, dataStore);

            if (element.SetDominant)
            {
                // Set dominant: if both Set and Reset are true, Set wins
                if (setInput)
                    element.RsState = true;
                if (resetInput)
                    element.RsState = false;
            }
            else
            {
                // Reset dominant: if both Set and Reset are true, Reset wins
                if (resetInput)
                    element.RsState = false;
                if (setInput)
                    element.RsState = true;
            }

            return element.RsState;
        }

        private bool EvaluateTonTimer(PlcSimulationElement element, DataStore dataStore, int elapsedMs)
        {
            bool input = EvaluateSource(element.Input1, dataStore);

            if (input)
            {
                element.TimerAccumulatorMs += elapsedMs;
                if (element.TimerAccumulatorMs >= element.TimerPresetMs)
                {
                    element.TimerOutput = true;
                }
            }
            else
            {
                // Reset timer when input is false
                element.TimerAccumulatorMs = 0;
                element.TimerOutput = false;
            }

            element.TimerLastInput = input;
            return element.TimerOutput;
        }

        private bool EvaluateTofTimer(PlcSimulationElement element, DataStore dataStore, int elapsedMs)
        {
            bool input = EvaluateSource(element.Input1, dataStore);

            if (input)
            {
                // Input is true - reset the timer and set output true
                element.TimerAccumulatorMs = 0;
                element.TimerOutput = true;
            }
            else
            {
                // Input is false - start counting if output is true
                if (element.TimerOutput)
                {
                    element.TimerAccumulatorMs += elapsedMs;
                    if (element.TimerAccumulatorMs >= element.TimerPresetMs)
                    {
                        element.TimerOutput = false;
                        element.TimerAccumulatorMs = 0;
                    }
                }
            }

            element.TimerLastInput = input;
            return element.TimerOutput;
        }

        private bool EvaluateTpTimer(PlcSimulationElement element, DataStore dataStore, int elapsedMs)
        {
            bool input = EvaluateSource(element.Input1, dataStore);
            bool risingEdge = input && !element.TimerLastInput;

            if (risingEdge)
            {
                // Start pulse timer
                element.TimerAccumulatorMs = 0;
                element.TimerOutput = true;
            }

            if (element.TimerOutput)
            {
                element.TimerAccumulatorMs += elapsedMs;
                if (element.TimerAccumulatorMs >= element.TimerPresetMs)
                {
                    element.TimerOutput = false;
                }
            }

            element.TimerLastInput = input;
            return element.TimerOutput;
        }

        #region Counter Evaluators

        private bool EvaluateCtuCounter(PlcSimulationElement element, DataStore dataStore)
        {
            bool input = EvaluateSource(element.Input1, dataStore);
            bool risingEdge = input && !element.CounterLastInput;

            if (risingEdge)
            {
                element.CounterValue++;
            }

            element.CounterLastInput = input;
            // Output is true when counter >= preset
            return element.CounterValue >= element.CounterPreset;
        }

        private bool EvaluateCtdCounter(PlcSimulationElement element, DataStore dataStore)
        {
            bool input = EvaluateSource(element.Input1, dataStore);
            bool risingEdge = input && !element.CounterLastInput;

            if (risingEdge)
            {
                element.CounterValue--;
            }

            element.CounterLastInput = input;
            // Output is true when counter <= 0
            return element.CounterValue <= 0;
        }

        private bool EvaluateCtcCounter(PlcSimulationElement element, DataStore dataStore)
        {
            bool input = EvaluateSource(element.Input1, dataStore);
            bool direction = EvaluateSource(element.Input2, dataStore); // Input2 is direction (true=up, false=down)
            bool risingEdge = input && !element.CounterLastInput;

            if (risingEdge)
            {
                if (direction)
                    element.CounterValue++;
                else
                    element.CounterValue--;
            }

            element.CounterLastInput = input;
            // Output is true when counter >= preset
            return element.CounterValue >= element.CounterPreset;
        }

        #endregion

        #region Comparator Evaluators

        private int ReadPlcInputValue(PlcAddressReference input, DataStore dataStore)
        {
            int addr = input.Address;
            if (addr < 0) return 0;

            switch (input.Area)
            {
                case PlcArea.Coil:
                    return (addr < dataStore.CoilDiscretes.Count && dataStore.CoilDiscretes[addr]) ? 1 : 0;

                case PlcArea.DiscreteInput:
                    return (addr < dataStore.InputDiscretes.Count && dataStore.InputDiscretes[addr]) ? 1 : 0;

                case PlcArea.HoldingRegister:
                    if (addr < dataStore.HoldingRegisters.Count)
                        return dataStore.HoldingRegisters[addr];
                    return 0;

                case PlcArea.InputRegister:
                    if (addr < dataStore.InputRegisters.Count)
                        return dataStore.InputRegisters[addr];
                    return 0;

                default:
                    return 0;
            }
        }

        private bool EvaluateCompareEq(PlcSimulationElement element, DataStore dataStore)
        {
            int input1Val = ReadPlcInputValue(element.Input1, dataStore);
            int input2Val = element.Input2 != null ? ReadPlcInputValue(element.Input2, dataStore) : element.CompareValue;
            return input1Val == input2Val;
        }

        private bool EvaluateCompareNe(PlcSimulationElement element, DataStore dataStore)
        {
            int input1Val = ReadPlcInputValue(element.Input1, dataStore);
            int input2Val = element.Input2 != null ? ReadPlcInputValue(element.Input2, dataStore) : element.CompareValue;
            return input1Val != input2Val;
        }

        private bool EvaluateCompareGt(PlcSimulationElement element, DataStore dataStore)
        {
            int input1Val = ReadPlcInputValue(element.Input1, dataStore);
            int input2Val = element.Input2 != null ? ReadPlcInputValue(element.Input2, dataStore) : element.CompareValue;
            return input1Val > input2Val;
        }

        private bool EvaluateCompareLt(PlcSimulationElement element, DataStore dataStore)
        {
            int input1Val = ReadPlcInputValue(element.Input1, dataStore);
            int input2Val = element.Input2 != null ? ReadPlcInputValue(element.Input2, dataStore) : element.CompareValue;
            return input1Val < input2Val;
        }

        private bool EvaluateCompareGe(PlcSimulationElement element, DataStore dataStore)
        {
            int input1Val = ReadPlcInputValue(element.Input1, dataStore);
            int input2Val = element.Input2 != null ? ReadPlcInputValue(element.Input2, dataStore) : element.CompareValue;
            return input1Val >= input2Val;
        }

        private bool EvaluateCompareLe(PlcSimulationElement element, DataStore dataStore)
        {
            int input1Val = ReadPlcInputValue(element.Input1, dataStore);
            int input2Val = element.Input2 != null ? ReadPlcInputValue(element.Input2, dataStore) : element.CompareValue;
            return input1Val <= input2Val;
        }

        #endregion

        #region Math Evaluators

        private bool EvaluateMathAdd(PlcSimulationElement element, DataStore dataStore)
        {
            int input1Val = ReadPlcInputValue(element.Input1, dataStore);
            int input2Val = element.Input2 != null ? ReadPlcInputValue(element.Input2, dataStore) : element.CompareValue;
            int result = input1Val + input2Val;
            
            // Clamp to ushort range
            if (result < 0) result = 0;
            if (result > 65535) result = 65535;

            WriteNumericOutput(element.Output, (ushort)result, dataStore);
            return result != 0;
        }

        private bool EvaluateMathSub(PlcSimulationElement element, DataStore dataStore)
        {
            int input1Val = ReadPlcInputValue(element.Input1, dataStore);
            int input2Val = element.Input2 != null ? ReadPlcInputValue(element.Input2, dataStore) : element.CompareValue;
            int result = input1Val - input2Val;
            
            // Clamp to ushort range
            if (result < 0) result = 0;
            if (result > 65535) result = 65535;

            WriteNumericOutput(element.Output, (ushort)result, dataStore);
            return result != 0;
        }

        private bool EvaluateMathMul(PlcSimulationElement element, DataStore dataStore)
        {
            int input1Val = ReadPlcInputValue(element.Input1, dataStore);
            int input2Val = element.Input2 != null ? ReadPlcInputValue(element.Input2, dataStore) : element.CompareValue;
            int result = input1Val * input2Val;
            
            // Clamp to ushort range
            if (result < 0) result = 0;
            if (result > 65535) result = 65535;

            WriteNumericOutput(element.Output, (ushort)result, dataStore);
            return result != 0;
        }

        private bool EvaluateMathDiv(PlcSimulationElement element, DataStore dataStore)
        {
            int input1Val = ReadPlcInputValue(element.Input1, dataStore);
            int input2Val = element.Input2 != null ? ReadPlcInputValue(element.Input2, dataStore) : element.CompareValue;
            
            // Handle divide by zero
            if (input2Val == 0)
            {
                WriteNumericOutput(element.Output, 0, dataStore);
                return false;
            }

            int result = input1Val / input2Val;
            
            // Clamp to ushort range
            if (result < 0) result = 0;
            if (result > 65535) result = 65535;

            WriteNumericOutput(element.Output, (ushort)result, dataStore);
            return result != 0;
        }

        private void WriteNumericOutput(PlcAddressReference output, ushort value, DataStore dataStore)
        {
            if (output == null || output.Address < 0) return;

            int addr = output.Address;

            switch (output.Area)
            {
                case PlcArea.HoldingRegister:
                    if (addr < dataStore.HoldingRegisters.Count)
                        dataStore.HoldingRegisters[addr] = value;
                    break;

                case PlcArea.InputRegister:
                    if (addr < dataStore.InputRegisters.Count)
                        dataStore.InputRegisters[addr] = value;
                    break;
            }
        }

        #endregion

        public void Dispose()
        {
            Stop();
        }
    }
}
