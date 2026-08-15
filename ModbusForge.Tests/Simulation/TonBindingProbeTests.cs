using System;
using System.Collections.Generic;
using System.Threading;
using ModbusForge.Core.Simulation.Blocks;
using ModbusForge.Core.Simulation.Core;
using ModbusForge.Core.Simulation.Engine;
using ModbusForge.Models;
using Xunit;

namespace ModbusForge.Tests.Simulation
{
    /// <summary>
    /// Guards the engine-level half of the state-reuse contract: reloading a graph with the
    /// same SimulationNode instance (what the visual service does on in-place edits) must
    /// preserve block state such as timer accumulators.
    /// </summary>
    public class TonBindingProbeTests
    {
        [Fact]
        public async Task Ton_InputBinding_StateSurvivesGraphReload()
        {
            var catalog = new FunctionBlockCatalog();
            catalog.Register(new TonBlock());

            var engine = new ExecutionEngine(catalog);

            var ton = new SimulationNode("ton1", "ton1", new TonBlock());
            ton.Parameters["TimerPresetMs"] = 1000;
            ton.InputBindings["Input1"] = new PlcAddressReference { Area = PlcArea.Coil, Address = 1 };

            engine.LoadGraph(new[] { ton }, Array.Empty<SimulationConnection>());

            var dataStore = ModbusForge.Data.DataStoreFactory.CreateDefaultDataStore();
            dataStore.CoilDiscretes[1] = true;

            lock (dataStore) { engine.Execute(dataStore); }
            await Task.Delay(500);
            lock (dataStore) { engine.Execute(dataStore); }
            Assert.False(ton.OutputValues["Output"].AsBool());

            // A mid-run graph reload that reuses the SimulationNode (state must survive).
            engine.LoadGraph(new[] { ton }, Array.Empty<SimulationConnection>());
            lock (dataStore) { engine.Execute(dataStore); }
            await Task.Delay(600);
            lock (dataStore) { engine.Execute(dataStore); }

            // ~1100ms total (> preset): the TON completes only if the accumulator survived.
            Assert.True(ton.OutputValues["Output"].AsBool());
        }
    }
}
