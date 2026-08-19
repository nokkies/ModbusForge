using ModbusForge.Core.Simulation.Blocks;
using ModbusForge.Core.Simulation.Core;
using Xunit;

namespace ModbusForge.Tests.Simulation
{
    /// <summary>
    /// Pins the function block port naming contract: every block declares meaningful,
    /// IEC 61131-3-style port names (IN, Q, S/R, A/B, CU/CD, Start/Stop/Run, ...) so the
    /// node editor can show them as canvas pin labels. No block may fall back to the
    /// generic "Input1"/"Input2"/"Output" names — those belong to the editor's connector
    /// slots in the wire format, which the engine maps onto the declared ports positionally.
    /// </summary>
    public class FunctionBlockPortNamingTests
    {
        private static FunctionBlockCatalog CreateCatalog()
        {
            var catalog = new FunctionBlockCatalog();

            // I/O
            catalog.Register(new LegacyInputBlock());
            catalog.Register(new InputBoolBlock());
            catalog.Register(new InputIntBlock());
            catalog.Register(new LegacyOutputBlock());
            catalog.Register(new OutputBoolBlock());
            catalog.Register(new OutputIntBlock());

            // Logic
            catalog.Register(new NotBlock());
            catalog.Register(new AndBlock());
            catalog.Register(new OrBlock());
            catalog.Register(new RsLatchBlock());

            // Timers
            catalog.Register(new TonBlock());
            catalog.Register(new TofBlock());
            catalog.Register(new TpBlock());

            // Counters
            catalog.Register(new CtuBlock());
            catalog.Register(new CtdBlock());
            catalog.Register(new CtcBlock());

            // Comparators (int + real)
            foreach (var operation in System.Enum.GetValues<ComparisonOperation>())
                catalog.Register(new CompareBlock(operation));
            foreach (var operation in System.Enum.GetValues<ComparisonOperation>())
                catalog.Register(new CompareBlock(operation, isReal: true));

            // Math (int + real)
            foreach (var operation in System.Enum.GetValues<MathOperation>())
                catalog.Register(new MathBlock(operation));
            foreach (var operation in System.Enum.GetValues<MathOperation>())
                catalog.Register(new MathBlock(operation, isReal: true));

            // Sources
            catalog.Register(new SignalGeneratorBlock());
            catalog.Register(new SignalGeneratorRealBlock());

            // Industrial devices
            catalog.Register(new ValveBlock());
            catalog.Register(new MotorDolBlock());
            catalog.Register(new VsdBlock());

            // Signal conditioning
            catalog.Register(new ScaleBlock());
            catalog.Register(new EdgeDetectBlock());
            catalog.Register(new MovingAverageBlock());

            return catalog;
        }

        [Fact]
        public void EveryBlock_DeclaresOnlyMeaningfulPortNames()
        {
            // The generic slot names are reserved for the editor's wire format; a block
            // declaring one of them would show "Input 1" as its pin label instead of a
            // meaningful name.
            var genericNames = new[] { "Input1", "Input2", "Output" };

            foreach (var descriptor in CreateCatalog().Descriptors)
            {
                Assert.True(descriptor.Ports.Count > 0, $"{descriptor.TypeId} declares no ports.");
                foreach (var port in descriptor.Ports)
                {
                    Assert.False(string.IsNullOrWhiteSpace(port.Name),
                        $"{descriptor.TypeId} declares an unnamed port.");
                    Assert.False(genericNames.Contains(port.Name),
                        $"{descriptor.TypeId} declares generic port '{port.Name}'.");
                }
            }
        }

        [Theory]
        [InlineData("TON", new[] { "IN" }, new[] { "Q" })]
        [InlineData("TOF", new[] { "IN" }, new[] { "Q" })]
        [InlineData("TP", new[] { "IN" }, new[] { "Q" })]
        [InlineData("CTU", new[] { "CU" }, new[] { "Q" })]
        [InlineData("CTD", new[] { "CD" }, new[] { "Q" })]
        [InlineData("CTC", new[] { "CU", "CD" }, new[] { "Q" })]
        [InlineData("NOT", new[] { "IN" }, new[] { "Q" })]
        [InlineData("AND", new[] { "IN1", "IN2" }, new[] { "Q" })]
        [InlineData("OR", new[] { "IN1", "IN2" }, new[] { "Q" })]
        [InlineData("RS", new[] { "S", "R" }, new[] { "Q" })]
        [InlineData("MATH_ADD", new[] { "A", "B" }, new[] { "Q" })]
        [InlineData("MATH_DIV_REAL", new[] { "A", "B" }, new[] { "Q" })]
        [InlineData("COMPARE_GT", new[] { "A", "B" }, new[] { "Q" })]
        [InlineData("SignalGenerator", new string[0], new[] { "Value" })]
        [InlineData("InputBool", new[] { "Value" }, new[] { "Q" })]
        [InlineData("OutputInt", new[] { "Value" }, new[] { "Q" })]
        [InlineData("Valve", new[] { "OpenCmd", "CloseCmd" }, new[] { "Open", "Fault" })]
        [InlineData("MotorDol", new[] { "Start", "Stop" }, new[] { "Run" })]
        [InlineData("Vsd", new[] { "Run", "SpeedReference" }, new[] { "Running", "SpeedFeedback", "AtSpeed" })]
        [InlineData("Scale", new[] { "Value" }, new[] { "Q" })]
        [InlineData("EdgeDetect", new[] { "IN" }, new[] { "Q" })]
        [InlineData("MovingAverage", new[] { "Value" }, new[] { "Q" })]
        public void Block_Ports_UseTheCanonicalNames(string typeId, string[] expectedInputs, string[] expectedOutputs)
        {
            var descriptor = CreateCatalog().GetDescriptor(typeId);
            Assert.NotNull(descriptor);

            Assert.Equal(expectedInputs, BlockPorts.Inputs(descriptor!.Ports).Select(p => p.Name).ToArray());
            Assert.Equal(expectedOutputs, BlockPorts.Outputs(descriptor.Ports).Select(p => p.Name).ToArray());
        }

        [Fact]
        public void PrimaryOutput_FallsBackToFirstDeclaredOutput()
        {
            var catalog = CreateCatalog();

            // No block declares a literal "Output" port anymore, so the primary output is
            // always the first declared output port.
            Assert.Equal("Running", BlockPorts.PrimaryOutput(catalog.GetDescriptor("Vsd")!.Ports));
            Assert.Equal("Open", BlockPorts.PrimaryOutput(catalog.GetDescriptor("Valve")!.Ports));
            Assert.Equal("Q", BlockPorts.PrimaryOutput(catalog.GetDescriptor("TON")!.Ports));
            Assert.Equal("Value", BlockPorts.PrimaryOutput(catalog.GetDescriptor("SignalGenerator")!.Ports));
        }
    }
}
