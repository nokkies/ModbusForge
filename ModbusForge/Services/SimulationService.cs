using System;
using System.Windows.Threading;
using ModbusForge.ViewModels.Coordinators;
using Microsoft.Extensions.Logging;

namespace ModbusForge.Services
{
    public class SimulationService : ISimulationService, IDisposable
    {
        private SimulationCoordinator? _coordinator;
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

        public void Start(SimulationCoordinator coordinator)
        {
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            if (_simulationTimer == null)
            {
                _simulationTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(Math.Max(50, _coordinator.SimulationPeriodMs))
                };
                _simulationTimer.Tick += SimulationTimer_Tick;
            }
            else
            {
                _simulationTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(50, _coordinator.SimulationPeriodMs));
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
            if (_coordinator == null) return;
            if (_isSimulating) return;

            // Simulation only runs when server is running (connected)
            if (!_serverService.IsConnected) return;

            if (!_coordinator.SimulationEnabled) return;

            _isSimulating = true;
            try
            {
                var nowSim = DateTime.UtcNow;
                double dt = (nowSim - _lastSimTickUtc).TotalSeconds;
                if (dt < 0 || dt > 5) dt = 0;
                _simTimeSec += dt;
                _lastSimTickUtc = nowSim;

                if (_coordinator.SimHoldingsEnabled)
                {
                    int count = _coordinator.SimHoldingCount <= 0 ? 0 : _coordinator.SimHoldingCount;
                    int start = _coordinator.SimHoldingStart;
                    int min = _coordinator.SimHoldingMin;
                    int max = _coordinator.SimHoldingMax;
                    string wf = (_coordinator.SimHoldingWaveformType ?? "Ramp").ToLowerInvariant();
                    if ((wf == "sine" || wf == "triangle" || wf == "square") && _coordinator.SimHoldingFrequencyHz > 0)
                    {
                        double f = _coordinator.SimHoldingFrequencyHz;
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

                        double raw = _coordinator.SimHoldingOffset + (_coordinator.SimHoldingAmplitude * w);
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

                if (_coordinator.SimCoilsEnabled)
                {
                    var dataStore = _serverService.GetDataStore();
                    if (dataStore != null)
                    {
                        int count = _coordinator.SimCoilCount <= 0 ? 0 : _coordinator.SimCoilCount;
                        int start = _coordinator.SimCoilStart;
                        for (int i = 0; i < count; i++)
                        {
                            int addr = start + i;
                            if (addr >= 0 && addr < dataStore.CoilDiscretes.Count)
                                dataStore.CoilDiscretes[addr] = _simCoilState;
                        }
                        _simCoilState = !_simCoilState;
                    }
                }

                if (_coordinator.SimInputsEnabled)
                {
                    var dataStore = _serverService.GetDataStore();
                    if (dataStore != null)
                    {
                        int count = _coordinator.SimInputCount <= 0 ? 0 : _coordinator.SimInputCount;
                        int start = _coordinator.SimInputStart;
                        int min = _coordinator.SimInputMin;
                        int max = _coordinator.SimInputMax;
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

                if (_coordinator.SimDiscreteEnabled)
                {
                    var dataStore = _serverService.GetDataStore();
                    if (dataStore != null)
                    {
                        int count = _coordinator.SimDiscreteCount <= 0 ? 0 : _coordinator.SimDiscreteCount;
                        int start = _coordinator.SimDiscreteStart;
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
