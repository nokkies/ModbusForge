using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ModbusForge.Core.Simulation.Engine;
using ModbusForge.Data;
using ModbusForge.Models;
using ModbusForge.Services;

namespace ModbusForge.Avalonia.Tests.Services
{
    public class VisualSimulationServiceTests
    {
        [Fact]
        public void WriteNodeValue_InputIntAddressOne_WritesFirstHoldingRegister()
        {
            var service = new AvaloniaVisualSimulationService();
            var config = new VisualNodeEditorConfig
            {
                Nodes = new ObservableCollection<VisualNode>
                {
                    new()
                    {
                        Id = "input1",
                        Name = "Input1",
                        ElementType = PlcElementType.InputInt,
                        Input1Address = new PlcAddressReference
                        {
                            Area = PlcArea.HoldingRegister,
                            Address = 1
                        }
                    }
                }
            };

            service.Start(config);

            var ex = Record.Exception(() => service.WriteNodeValue("input1", 42));

            Assert.Null(ex);
            var dataStore = GetDataStore(service);
            Assert.NotNull(dataStore);
            Assert.Equal((ushort)42, dataStore!.HoldingRegisters[1]);
        }

        [Fact]
        public void WriteNodeValue_MathAddOutputAddressTwo_WritesSecondHoldingRegister()
        {
            var service = new AvaloniaVisualSimulationService();
            var config = new VisualNodeEditorConfig
            {
                Nodes = new ObservableCollection<VisualNode>
                {
                    new()
                    {
                        Id = "add1",
                        Name = "Add",
                        ElementType = PlcElementType.MATH_ADD,
                        OutputAddress = new PlcAddressReference
                        {
                            Area = PlcArea.HoldingRegister,
                            Address = 2
                        },
                        Input2Address = new PlcAddressReference
                        {
                            Area = PlcArea.HoldingRegister,
                            Address = -1
                        }
                    }
                }
            };

            service.Start(config);

            var ex = Record.Exception(() => service.WriteNodeValue("add1", 123));

            Assert.Null(ex);
            var dataStore = GetDataStore(service);
            Assert.NotNull(dataStore);
            Assert.Equal((ushort)123, dataStore!.HoldingRegisters[2]);
        }

        [Fact]
        public void WriteNodeValue_ZeroAddress_IsIgnored()
        {
            var service = new AvaloniaVisualSimulationService();
            var config = new VisualNodeEditorConfig
            {
                Nodes = new ObservableCollection<VisualNode>
                {
                    new()
                    {
                        Id = "input1",
                        Name = "Input1",
                        ElementType = PlcElementType.InputInt,
                        Input1Address = new PlcAddressReference
                        {
                            Area = PlcArea.HoldingRegister,
                            Address = 0
                        }
                    }
                }
            };

            service.Start(config);

            var ex = Record.Exception(() => service.WriteNodeValue("input1", 42));

            Assert.Null(ex);
        }

        [Fact]
        public async Task Start_EditNodeWhileRunning_PreservesBlockState()
        {
            // Renaming a node changes the graph hash and triggers a rebuild. The rebuild
            // must REUSE the SimulationNode (keyed by visual node id) so timer state
            // survives the edit instead of being silently reset.
            var service = new AvaloniaVisualSimulationService();
            var config = new VisualNodeEditorConfig
            {
                Nodes = new ObservableCollection<VisualNode>
                {
                    new()
                    {
                        Id = "in1",
                        Name = "IN",
                        ElementType = PlcElementType.InputBool,
                        Input1Address = new PlcAddressReference
                        {
                            Area = PlcArea.Coil,
                            Address = 1
                        }
                    },
                    new()
                    {
                        Id = "ton1",
                        Name = "Timer1",
                        ElementType = PlcElementType.TON,
                        TimerPresetMs = 1000
                    }
                },
                Connections = new ObservableCollection<NodeConnection>
                {
                    new NodeConnection("in1", "ton1", "Input1")
                }
            };

            service.Start(config);

            var dataStore = GetDataStore(service);
            Assert.NotNull(dataStore);
            dataStore!.CoilDiscretes[1] = true;

            // Let ~500ms accumulate toward the 1000ms preset.
            service.UpdateNodeValues();
            await Task.Delay(500);
            service.UpdateNodeValues();
            Assert.False(service.GetNodeValue("ton1"));

            // The rename forces a graph rebuild mid-run.
            config.Nodes[1].Name = "Timer renamed";
            service.UpdateNodeValues(); // rebuild happens here (fresh engine tick clock)
            await Task.Delay(600);
            service.UpdateNodeValues();

            // ~1100ms total (> preset) => the TON completes only if its accumulator
            // survived the rebuild. A state reset would leave ~600ms (< preset).
            Assert.True(service.GetNodeValue("ton1"));

            service.Stop();
        }

        /// <summary>
        /// Exposes the protected IsRunning setter so tests can run ticks
        /// without starting the background timer.
        /// </summary>
        private sealed class TestableVisualSimulationService : AvaloniaVisualSimulationService
        {
            public bool IsRunningForTest
            {
                get => IsRunning;
                set => IsRunning = value;
            }
        }

        private static DataStore? GetDataStore(AvaloniaVisualSimulationService service)
        {
            var baseType = typeof(AvaloniaVisualSimulationService).BaseType;
            var field = baseType?.GetField("_dataStore", BindingFlags.NonPublic | BindingFlags.Instance);

            return field?.GetValue(service) as DataStore;
        }

        private static void SetConfig(VisualSimulationServiceBase<AvaloniaVisualSimulationService> service, VisualNodeEditorConfig config)
        {
            var baseType = typeof(AvaloniaVisualSimulationService).BaseType;
            var field = baseType!.GetField("_config", BindingFlags.NonPublic | BindingFlags.Instance);
            field!.SetValue(service, config);
        }

        private static int GetEngineCycleCount(VisualSimulationServiceBase<AvaloniaVisualSimulationService> service)
        {
            var baseType = typeof(AvaloniaVisualSimulationService).BaseType;
            var field = baseType!.GetField("_engine", BindingFlags.NonPublic | BindingFlags.Instance);
            return ((ExecutionEngine)field!.GetValue(service)!).CycleCount;
        }

        [Fact]
        public async Task UpdateNodeValues_ConcurrentTicks_RunEngineExactlyOnce()
        {
            // The host timer can raise a tick while the previous one is still
            // running (System.Timers.Timer, AutoReset). The shared engine and
            // node state must therefore tolerate overlapping ticks: at most one
            // executes, the rest are dropped.
            var service = new TestableVisualSimulationService();
            var config = new VisualNodeEditorConfig
            {
                Nodes = new ObservableCollection<VisualNode>
                {
                    new() { Id = "n1", Name = "N1", ElementType = PlcElementType.InputBool }
                }
            };
            SetConfig(service, config);
            service.IsRunningForTest = true;

            var dataStore = GetDataStore(service)!;

            // Holding the store lock makes the first tick (the one that wins the
            // re-entrancy guard) block inside it, while every tick started in the
            // same window must be dropped instead of queueing a second engine run.
            var tasks = new List<Task>();
            lock (dataStore)
            {
                for (int i = 0; i < 4; i++)
                    tasks.Add(Task.Run(() => service.UpdateNodeValues()));
                Thread.Sleep(2000);
            }
            await Task.WhenAll(tasks);

            Assert.Equal(1, GetEngineCycleCount(service));
        }

        [Fact]
        public void Stop_ClearsLiveNodeValues()
        {
            var service = new TestableVisualSimulationService();
            var config = new VisualNodeEditorConfig
            {
                Nodes = new ObservableCollection<VisualNode>
                {
                    new()
                    {
                        Id = "in1",
                        Name = "IN",
                        ElementType = PlcElementType.InputBool,
                        Input1Address = new PlcAddressReference
                        {
                            Area = PlcArea.Coil,
                            Address = 1
                        }
                    }
                }
            };
            SetConfig(service, config);

            var dataStore = GetDataStore(service)!;
            dataStore.CoilDiscretes[1] = true;

            service.IsRunningForTest = true;
            service.UpdateNodeValues();
            Assert.True(service.GetNodeValue("in1"));

            service.Stop();

            Assert.False(service.GetNodeValue("in1"));
        }
    }
}
