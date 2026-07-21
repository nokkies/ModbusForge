using System;
using System.Collections.Generic;
using ModbusForge.Simulation.Blocks;
using ModbusForge.Simulation.Core;
using ModbusForge.Simulation.Engine;
using ModbusForge.Models;
using Xunit;

namespace ModbusForge.Tests.Simulation
{
    public class ExecutionEngineTests
    {
        private readonly FunctionBlockCatalog _catalog;

        public ExecutionEngineTests()
        {
            _catalog = new FunctionBlockCatalog();
            _catalog.Register(new InputIntBlock());
            _catalog.Register(new InputBoolBlock());
            _catalog.Register(new OutputIntBlock());
            _catalog.Register(new OutputBoolBlock());
            _catalog.Register(new MathBlock(MathOperation.Add));
            _catalog.Register(new AndBlock());
        }

        [Fact]
        public void Execute_MathChain_RespectsTopologicalOrder()
        {
            var engine = new ExecutionEngine(_catalog);

            var inA = CreateNode("inA", new InputIntBlock());
            inA.InputBindings["Input1"] = new PlcAddressReference { Area = PlcArea.HoldingRegister, Address = 1 };

            var inB = CreateNode("inB", new InputIntBlock());
            inB.InputBindings["Input1"] = new PlcAddressReference { Area = PlcArea.HoldingRegister, Address = 2 };

            var add = CreateNode("add", new MathBlock(MathOperation.Add));
            var output = CreateNode("out", new OutputIntBlock());
            output.OutputBindings["Output"] = new PlcAddressReference { Area = PlcArea.HoldingRegister, Address = 20 };

            var dataStore = CreateDataStore();
            dataStore.HoldingRegisters[1] = 10;
            dataStore.HoldingRegisters[2] = 3;

            // Pass nodes in reverse dependency order to prove the engine topologically sorts.
            engine.LoadGraph(
                new List<SimulationNode> { output, add, inA, inB },
                new[]
                {
                    new SimulationConnection("inA", "Output", "add", "Input1"),
                    new SimulationConnection("inB", "Output", "add", "Input2"),
                    new SimulationConnection("add", "Output", "out", "Input1")
                });

            engine.Execute(dataStore);

            Assert.Equal(13, dataStore.HoldingRegisters[20]);
        }

        [Fact]
        public void Execute_Cycle_ExcludesCyclicNodes()
        {
            var engine = new ExecutionEngine(_catalog);

            var a = CreateNode("a", new AndBlock());
            var b = CreateNode("b", new AndBlock());

            engine.LoadGraph(
                new[] { a, b },
                new[]
                {
                    new SimulationConnection("a", "Output", "b", "Input1"),
                    new SimulationConnection("b", "Output", "a", "Input1")
                });

            var dataStore = CreateDataStore();
            engine.Execute(dataStore);

            Assert.Contains("a", engine.CycleNodeIds);
            Assert.Contains("b", engine.CycleNodeIds);
        }

        [Fact]
        public void Execute_AndGate_PassesValueToOutput()
        {
            var engine = new ExecutionEngine(_catalog);

            var input = CreateNode("in", new InputBoolBlock());
            input.InputBindings["Input1"] = new PlcAddressReference { Area = PlcArea.Coil, Address = 1 };

            var and = CreateNode("and", new AndBlock());
            var output = CreateNode("out", new OutputBoolBlock());
            output.OutputBindings["Output"] = new PlcAddressReference { Area = PlcArea.Coil, Address = 10 };

            var dataStore = CreateDataStore();
            dataStore.CoilDiscretes[1] = true;

            engine.LoadGraph(
                new[] { input, and, output },
                new[]
                {
                    new SimulationConnection("in", "Output", "and", "Input1"),
                    new SimulationConnection("and", "Output", "out", "Input1")
                });

            engine.Execute(dataStore);

            Assert.False(dataStore.CoilDiscretes[10]);

            // Now connect the second input to true as well.
            var input2 = CreateNode("in2", new InputBoolBlock());
            input2.InputBindings["Input1"] = new PlcAddressReference { Area = PlcArea.Coil, Address = 2 };
            dataStore.CoilDiscretes[2] = true;

            engine.LoadGraph(
                new[] { input, input2, and, output },
                new[]
                {
                    new SimulationConnection("in", "Output", "and", "Input1"),
                    new SimulationConnection("in2", "Output", "and", "Input2"),
                    new SimulationConnection("and", "Output", "out", "Input1")
                });

            engine.Execute(dataStore);

            Assert.True(dataStore.CoilDiscretes[10]);
        }

        private static SimulationNode CreateNode(string id, IFunctionBlock block)
        {
            return new SimulationNode(id, id, block);
        }

        private static Modbus.Data.DataStore CreateDataStore()
        {
            return Modbus.Data.DataStoreFactory.CreateDefaultDataStore();
        }
    }
}
