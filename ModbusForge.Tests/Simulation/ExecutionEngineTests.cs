using System;
using System.Collections.Generic;
using ModbusForge.Core.Simulation.Blocks;
using ModbusForge.Core.Simulation.Core;
using ModbusForge.Core.Simulation.Engine;
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
            inA.InputBindings[PortNames.Value] = new PlcAddressReference { Area = PlcArea.HoldingRegister, Address = 1 };

            var inB = CreateNode("inB", new InputIntBlock());
            inB.InputBindings[PortNames.Value] = new PlcAddressReference { Area = PlcArea.HoldingRegister, Address = 2 };

            var add = CreateNode("add", new MathBlock(MathOperation.Add));
            var output = CreateNode("out", new OutputIntBlock());
            output.OutputBindings[PortNames.BoolOutput] = new PlcAddressReference { Area = PlcArea.HoldingRegister, Address = 20 };

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
            input.InputBindings[PortNames.Value] = new PlcAddressReference { Area = PlcArea.Coil, Address = 1 };

            var and = CreateNode("and", new AndBlock());
            var output = CreateNode("out", new OutputBoolBlock());
            output.OutputBindings[PortNames.BoolOutput] = new PlcAddressReference { Area = PlcArea.Coil, Address = 10 };

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
            input2.InputBindings[PortNames.Value] = new PlcAddressReference { Area = PlcArea.Coil, Address = 2 };
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

        [Fact]
        public void Execute_InvertedInput_ReadsInvertedValue()
        {
            var engine = new ExecutionEngine(_catalog);

            var input = CreateNode("in", new InputBoolBlock());
            input.InputBindings[PortNames.Value] = new PlcAddressReference { Area = PlcArea.Coil, Address = 1, Not = true };

            var output = CreateNode("out", new OutputBoolBlock());
            output.OutputBindings[PortNames.BoolOutput] = new PlcAddressReference { Area = PlcArea.Coil, Address = 10 };

            engine.LoadGraph(
                new[] { input, output },
                new[] { new SimulationConnection("in", "Output", "out", "Input1") });

            var dataStore = CreateDataStore();
            dataStore.CoilDiscretes[1] = true;

            engine.Execute(dataStore);

            Assert.False(dataStore.CoilDiscretes[10]);
        }

        [Fact]
        public void Execute_LoadGraph_ResetsElapsedTime()
        {
            var catalog = new FunctionBlockCatalog();
            catalog.Register(new InputBoolBlock());
            catalog.Register(new TonBlock());
            catalog.Register(new OutputBoolBlock());

            var engine = new ExecutionEngine(catalog);

            var input = CreateNode("in", new InputBoolBlock());
            input.InputBindings[PortNames.Value] = new PlcAddressReference { Area = PlcArea.Coil, Address = 1 };

            var ton = CreateNode("ton", new TonBlock());
            ton.Parameters["TimerPresetMs"] = 10000;

            var output = CreateNode("out", new OutputBoolBlock());
            output.OutputBindings[PortNames.BoolOutput] = new PlcAddressReference { Area = PlcArea.Coil, Address = 10 };

            engine.LoadGraph(
                new[] { input, ton, output },
                new[]
                {
                    new SimulationConnection("in", "Output", "ton", "Input1"),
                    new SimulationConnection("ton", "Output", "out", "Input1")
                });

            var dataStore = CreateDataStore();
            dataStore.CoilDiscretes[1] = true;

            engine.Execute(dataStore);

            Assert.False(dataStore.CoilDiscretes[10]);
        }

        [Fact]
        public void Execute_MathBlock_UsesInputBindingWhenNotConnected()
        {
            var engine = new ExecutionEngine(_catalog);

            var math = CreateNode("math", new MathBlock(MathOperation.Add));
            math.InputBindings[PortNames.OperandA] = new PlcAddressReference { Area = PlcArea.HoldingRegister, Address = 1 };
            math.InputBindings[PortNames.OperandB] = new PlcAddressReference { Area = PlcArea.HoldingRegister, Address = 2 };

            var output = CreateNode("out", new OutputIntBlock());
            output.OutputBindings[PortNames.BoolOutput] = new PlcAddressReference { Area = PlcArea.HoldingRegister, Address = 20 };

            engine.LoadGraph(
                new[] { math, output },
                new[] { new SimulationConnection("math", "Output", "out", "Input1") });

            var dataStore = CreateDataStore();
            dataStore.HoldingRegisters[1] = 10;
            dataStore.HoldingRegisters[2] = 3;

            engine.Execute(dataStore);

            Assert.Equal(13, dataStore.HoldingRegisters[20]);
        }

        [Fact]
        public void Execute_DolMotor_WireToInput1_MapsToStartPort()
        {
            // The editor wires to generic "Input1"/"Input2" connectors, but the DOL block
            // declares "Start"/"Stop". The engine must resolve the wire positionally.
            var catalog = new FunctionBlockCatalog();
            catalog.Register(new InputBoolBlock());
            catalog.Register(new MotorDolBlock());

            var engine = new ExecutionEngine(catalog);

            var input = CreateNode("in", new InputBoolBlock());
            input.InputBindings[PortNames.Value] = new PlcAddressReference { Area = PlcArea.Coil, Address = 1 };

            var motor = CreateNode("motor", new MotorDolBlock());
            motor.Parameters["MotorDolRunDelayMs"] = 0;

            engine.LoadGraph(
                new[] { input, motor },
                new[] { new SimulationConnection("in", "Output", "motor", "Input1") });

            var dataStore = CreateDataStore();
            dataStore.CoilDiscretes[1] = true;

            engine.Execute(dataStore);

            Assert.True(motor.OutputValues[PortNames.MotorRun].AsBool());
        }

        [Fact]
        public void Execute_DolMotor_Input1AddressBinding_BindsStartPort()
        {
            // Address bindings must also land on the declared port names, not "Input1".
            var catalog = new FunctionBlockCatalog();
            catalog.Register(new MotorDolBlock());

            var engine = new ExecutionEngine(catalog);

            var motor = CreateNode("motor", new MotorDolBlock());
            motor.Parameters["MotorDolRunDelayMs"] = 0;
            motor.InputBindings[PortNames.Start] = new PlcAddressReference { Area = PlcArea.Coil, Address = 5 };

            engine.LoadGraph(new[] { motor }, Array.Empty<SimulationConnection>());

            var dataStore = CreateDataStore();
            dataStore.CoilDiscretes[5] = true;

            engine.Execute(dataStore);

            Assert.True(motor.OutputValues[PortNames.MotorRun].AsBool());
        }

        [Fact]
        public void Execute_Vsd_WireFromOutput_MapsToRunningPort()
        {
            // The VSD declares "Running" (no literal "Output" port); the editor's "Output"
            // connector must resolve to the primary output port.
            var catalog = new FunctionBlockCatalog();
            catalog.Register(new OutputBoolBlock());
            catalog.Register(new VsdBlock());

            var engine = new ExecutionEngine(catalog);

            var vsd = CreateNode("vsd", new VsdBlock());
            vsd.InputBindings[PortNames.Run] = new PlcAddressReference { Area = PlcArea.Coil, Address = 1 };
            vsd.Parameters["VsdRampUpMs"] = 0;
            vsd.Parameters["VsdRampDownMs"] = 0;

            var output = CreateNode("out", new OutputBoolBlock());
            output.OutputBindings[PortNames.BoolOutput] = new PlcAddressReference { Area = PlcArea.Coil, Address = 10 };

            engine.LoadGraph(
                new[] { vsd, output },
                new[] { new SimulationConnection("vsd", "Output", "out", "Input1") });

            var dataStore = CreateDataStore();
            dataStore.CoilDiscretes[1] = true;

            engine.Execute(dataStore);

            Assert.True(dataStore.CoilDiscretes[10]);
        }

        [Fact]
        public void Execute_ThrowingBlock_SetsLastError_AndRestOfGraphStillRuns()
        {
            var throwing = new ThrowingBlock(shouldThrow: true, "simulated block fault");

            var engine = new ExecutionEngine(_catalog);
            var bad = CreateNode("bad", throwing);
            var healthy = CreateNode("ok", new InputIntBlock());
            healthy.InputBindings[PortNames.Value] = new PlcAddressReference { Area = PlcArea.HoldingRegister, Address = 1 };
            var output = CreateNode("out", new OutputIntBlock());
            output.OutputBindings[PortNames.BoolOutput] = new PlcAddressReference { Area = PlcArea.HoldingRegister, Address = 20 };

            engine.LoadGraph(
                new[] { bad, healthy, output },
                new[] { new SimulationConnection("ok", "Output", "out", "Input1") });

            var dataStore = CreateDataStore();
            dataStore.HoldingRegisters[1] = 7;

            engine.Execute(dataStore);

            // The failing node carries its error for the host to surface...
            Assert.Equal("simulated block fault", bad.LastError);
            // ...and it does not take the rest of the graph down with it.
            Assert.Equal(7, dataStore.HoldingRegisters[20]);
            Assert.Null(healthy.LastError);
            Assert.Null(output.LastError);
        }

        [Fact]
        public void Execute_ThrowingBlockRecovers_ClearsLastError()
        {
            var throwing = new ThrowingBlock(shouldThrow: true, "simulated block fault");

            var engine = new ExecutionEngine(_catalog);
            var bad = CreateNode("bad", throwing);
            engine.LoadGraph(new[] { bad }, Array.Empty<SimulationConnection>());

            engine.Execute(CreateDataStore());
            Assert.Equal("simulated block fault", bad.LastError);

            // The block heals on the next cycle: a clean run removes the marker so
            // the host stops rendering the error state.
            throwing.ShouldThrow = false;
            engine.Execute(CreateDataStore());
            Assert.Null(bad.LastError);
        }

        private static SimulationNode CreateNode(string id, IFunctionBlock block)
        {
            return new SimulationNode(id, id, block);
        }

        /// <summary>
        /// Test-only block whose failure is scriptable: throws while
        /// <see cref="ShouldThrow"/> is set, writes a constant output otherwise.
        /// </summary>
        private sealed class ThrowingBlock : IFunctionBlock
        {
            private readonly string _message;

            public ThrowingBlock(bool shouldThrow, string message)
            {
                ShouldThrow = shouldThrow;
                _message = message;
            }

            public bool ShouldThrow { get; set; }

            public string TypeId => "ThrowingBlock";
            public string DisplayName => "Throwing";
            public string Category => "Test";
            public IReadOnlyList<IPort> Ports { get; } = new List<IPort>
            {
                new PortDefinition("Q", PortDirection.Output, SimulationDataType.Int32)
            };
            public IReadOnlyList<BlockParameterDescriptor> Parameters { get; } =
                Array.Empty<BlockParameterDescriptor>();

            public void Execute(IExecutionContext context)
            {
                if (ShouldThrow)
                    throw new InvalidOperationException(_message);

                context.WriteOutput("Q", SimulationValue.Int32(1));
            }
        }

        private static ModbusForge.Data.DataStore CreateDataStore()
        {
            return ModbusForge.Data.DataStoreFactory.CreateDefaultDataStore();
        }
    }
}
