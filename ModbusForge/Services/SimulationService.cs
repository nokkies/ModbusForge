using System;
using System.Windows.Threading;
using ModbusForge.ViewModels;
using Microsoft.Extensions.Logging;

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
                        var holdingRegs = _serverService.GetHoldingRegisterBuffer<short>();
                        for (int i = 0; i < count; i++)
                        {
                            holdingRegs[start + i] = (short)iv;
                        }
                    }
                    else
                    {
                        var holdingRegs = _serverService.GetHoldingRegisterBuffer<short>();
                        int range = Math.Max(1, max - min + 1);
                        _simHoldingPhase = (_simHoldingPhase + 1) % range;
                        for (int i = 0; i < count; i++)
                        {
                            int val = min + ((_simHoldingPhase + i) % range);
                            if (val < 0) val = 0;
                            if (val > 65535) val = 65535;
                            holdingRegs[start + i] = (short)val;
                        }
                    }
                }

                if (_viewModel.SimCoilsEnabled)
                {
                    var coilBuf = _serverService.GetCoilBuffer<byte>();
                    int count = _viewModel.SimCoilCount <= 0 ? 0 : _viewModel.SimCoilCount;
                    int start = _viewModel.SimCoilStart;
                    for (int i = 0; i < count; i++)
                    {
                        int address = start + i;
                        int byteIndex = address / 8;
                        int bitOffset = address % 8;
                        if (_simCoilState)
                            coilBuf[byteIndex] = (byte)(coilBuf[byteIndex] | (1 << bitOffset));
                        else
                            coilBuf[byteIndex] = (byte)(coilBuf[byteIndex] & ~(1 << bitOffset));
                    }
                    _simCoilState = !_simCoilState;
                }

                if (_viewModel.SimInputsEnabled)
                {
                    var inputRegs = _serverService.GetInputRegisterBuffer<short>();
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
                        inputRegs[start + i] = (short)val;
                    }
                }

                if (_viewModel.SimDiscreteEnabled)
                {
                    var discreteBuf = _serverService.GetDiscreteInputBuffer<byte>();
                    int count = _viewModel.SimDiscreteCount <= 0 ? 0 : _viewModel.SimDiscreteCount;
                    int start = _viewModel.SimDiscreteStart;
                    for (int i = 0; i < count; i++)
                    {
                        int address = start + i;
                        int byteIndex = address / 8;
                        int bitOffset = address % 8;
                        if (_simDiscreteState)
                            discreteBuf[byteIndex] = (byte)(discreteBuf[byteIndex] | (1 << bitOffset));
                        else
                            discreteBuf[byteIndex] = (byte)(discreteBuf[byteIndex] & ~(1 << bitOffset));
                    }
                    _simDiscreteState = !_simDiscreteState;
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

        public void Dispose()
        {
            Stop();
        }
    }
}
