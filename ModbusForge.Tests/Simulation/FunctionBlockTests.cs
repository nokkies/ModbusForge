using ModbusForge.Core.Simulation.Blocks;
using ModbusForge.Core.Simulation.Core;
using Xunit;

namespace ModbusForge.Tests.Simulation
{
    public class FunctionBlockTests
    {
        [Fact]
        public void AndBlock_ComputesLogicalAnd()
        {
            var block = new AndBlock();
            var context = new TestExecutionContext();
            context.SetInput("Input1", SimulationValue.Bool(true));
            context.SetInput("Input2", SimulationValue.Bool(true));

            block.Execute(context);

            Assert.True(context.GetOutput("Output")!.AsBool());
        }

        [Fact]
        public void AndBlock_FalseWhenOneInputFalse()
        {
            var block = new AndBlock();
            var context = new TestExecutionContext();
            context.SetInput("Input1", SimulationValue.Bool(true));
            context.SetInput("Input2", SimulationValue.Bool(false));

            block.Execute(context);

            Assert.False(context.GetOutput("Output")!.AsBool());
        }

        [Fact]
        public void MathBlock_AddsTwoIntegers()
        {
            var block = new MathBlock(MathOperation.Add);
            var context = new TestExecutionContext();
            context.SetInput("Input1", SimulationValue.Int32(10));
            context.SetInput("Input2", SimulationValue.Int32(3));

            block.Execute(context);

            Assert.Equal(13, context.GetOutput("Output")!.AsInt32());
        }

        [Fact]
        public void MathBlock_DivisionByZero_ReturnsZero()
        {
            var block = new MathBlock(MathOperation.Divide);
            var context = new TestExecutionContext();
            context.SetInput("Input1", SimulationValue.Int32(10));
            context.SetInput("Input2", SimulationValue.Int32(0));

            block.Execute(context);

            Assert.Equal(0, context.GetOutput("Output")!.AsInt32());
        }

        [Fact]
        public void CompareBlock_GreaterThan_ReturnsTrue()
        {
            var block = new CompareBlock(ComparisonOperation.GreaterThan);
            var context = new TestExecutionContext();
            context.SetInput("Input1", SimulationValue.Int32(7));
            context.SetInput("Input2", SimulationValue.Int32(2));

            block.Execute(context);

            Assert.True(context.GetOutput("Output")!.AsBool());
        }

        [Fact]
        public void CompareBlock_FallsBackToParameter()
        {
            var block = new CompareBlock(ComparisonOperation.Equal);
            var context = new TestExecutionContext();
            context.SetInput("Input1", SimulationValue.Int32(5));
            context.Parameters["CompareValue"] = 5;

            block.Execute(context);

            Assert.True(context.GetOutput("Output")!.AsBool());
        }

        [Fact]
        public void ValveBlock_OpenCmd_ReachesOpen_AfterTravelTime()
        {
            var block = new ValveBlock();
            var context = new TestExecutionContext();
            context.Parameters["ValveTravelTimeMs"] = 500;

            // Start transition with OpenCmd.
            context.SetInput("OpenCmd", SimulationValue.Bool(true));
            context.SetInput("CloseCmd", SimulationValue.Bool(false));

            // First scan starts the transition.
            block.Execute(context);
            Assert.False(context.GetOutput("Output")!.AsBool());

            // Elapse half the travel time — still not open.
            context.OverrideElapsed(TimeSpan.FromMilliseconds(250));
            block.Execute(context);
            Assert.False(context.GetOutput("Output")!.AsBool());

            // Elapse full travel time — now open.
            context.OverrideElapsed(TimeSpan.FromMilliseconds(500));
            block.Execute(context);
            Assert.True(context.GetOutput("Output")!.AsBool());
        }

        [Fact]
        public void ValveBlock_BothCommandsActive_RaisesFault()
        {
            var block = new ValveBlock();
            var context = new TestExecutionContext();
            context.SetInput("OpenCmd", SimulationValue.Bool(true));
            context.SetInput("CloseCmd", SimulationValue.Bool(true));

            block.Execute(context);

            Assert.False(context.GetOutput("Output")!.AsBool());
            Assert.True(context.GetOutput("Fault")!.AsBool());
        }

        [Fact]
        public void MotorDolBlock_StartPulse_PicksUpAfterRunDelay()
        {
            var block = new MotorDolBlock();
            var context = new TestExecutionContext();
            context.Parameters["MotorDolRunDelayMs"] = 100;

            context.SetInput("Start", SimulationValue.Bool(true));
            context.SetInput("Stop", SimulationValue.Bool(false));

            // First scan starts the pickup delay.
            context.OverrideElapsed(TimeSpan.FromMilliseconds(10));
            block.Execute(context);
            Assert.False(context.GetOutput("Output")!.AsBool());

            // Still before run delay.
            context.OverrideElapsed(TimeSpan.FromMilliseconds(50));
            block.Execute(context);
            Assert.False(context.GetOutput("Output")!.AsBool());

            // Run delay elapsed.
            context.OverrideElapsed(TimeSpan.FromMilliseconds(50));
            block.Execute(context);
            Assert.True(context.GetOutput("Output")!.AsBool());
        }

        [Fact]
        public void VsdBlock_RunTrue_RampsUpToReference()
        {
            var block = new VsdBlock();
            var context = new TestExecutionContext();
            context.Parameters["VsdMaxSpeed"] = 100.0;
            context.Parameters["VsdRampUpMs"] = 1000;
            context.Parameters["VsdRampDownMs"] = 1000;
            context.Parameters["VsdAtSpeedTolerance"] = 2.0;

            context.SetInput("Run", SimulationValue.Bool(true));
            context.SetInput("SpeedReference", SimulationValue.Real(50.0));

            // Initial scan starts ramp.
            context.OverrideElapsed(TimeSpan.FromMilliseconds(10));
            block.Execute(context);
            Assert.True(context.GetOutput("Running")!.AsBool());
            var speed = context.GetOutput("SpeedFeedback")!.AsReal();
            Assert.Equal(1.0, speed, precision: 1);
            Assert.False(context.GetOutput("AtSpeed")!.AsBool());

            // A quarter of the ramp time should reach ~25 (half the 50 reference).
            context.OverrideElapsed(TimeSpan.FromMilliseconds(250));
            block.Execute(context);
            var feedback = context.GetOutput("SpeedFeedback")!.AsReal();
            Assert.True(feedback > 20 && feedback < 30);

            // Full ramp time should reach the 50 target.
            context.OverrideElapsed(TimeSpan.FromMilliseconds(1000));
            block.Execute(context);
            Assert.Equal(50.0, context.GetOutput("SpeedFeedback")!.AsReal(), precision: 1);
            Assert.True(context.GetOutput("AtSpeed")!.AsBool());
        }

        [Fact]
        public void VsdBlock_RunFalse_RampsDownToZero()
        {
            var block = new VsdBlock();
            var context = new TestExecutionContext();
            context.Parameters["VsdMaxSpeed"] = 100.0;
            context.Parameters["VsdRampUpMs"] = 0;
            context.Parameters["VsdRampDownMs"] = 1000;
            context.Parameters["VsdAtSpeedTolerance"] = 2.0;

            context.SetInput("Run", SimulationValue.Bool(true));
            context.SetInput("SpeedReference", SimulationValue.Real(100.0));

            context.OverrideElapsed(TimeSpan.FromMilliseconds(0));
            block.Execute(context);
            Assert.Equal(100.0, context.GetOutput("SpeedFeedback")!.AsReal(), precision: 0);

            context.SetInput("Run", SimulationValue.Bool(false));
            context.OverrideElapsed(TimeSpan.FromMilliseconds(1000));
            block.Execute(context);

            Assert.False(context.GetOutput("Running")!.AsBool());
            Assert.Equal(0.0, context.GetOutput("SpeedFeedback")!.AsReal(), precision: 1);
        }

        [Fact]
        public void VsdBlock_ReachesReference_AtSpeedTrue()
        {
            var block = new VsdBlock();
            var context = new TestExecutionContext();
            context.Parameters["VsdMaxSpeed"] = 100.0;
            context.Parameters["VsdRampUpMs"] = 0;
            context.Parameters["VsdRampDownMs"] = 0;
            context.Parameters["VsdAtSpeedTolerance"] = 2.0;

            context.SetInput("Run", SimulationValue.Bool(true));
            context.SetInput("SpeedReference", SimulationValue.Real(50.0));

            block.Execute(context);

            Assert.Equal(50.0, context.GetOutput("SpeedFeedback")!.AsReal(), precision: 0);
            Assert.True(context.GetOutput("AtSpeed")!.AsBool());
        }

        [Fact]
        public void MotorDolBlock_StopActive_DropsRunning_WithoutFault()
        {
            // Stop is a normal stop command: it de-energises the starter and
            // de-asserts Running without tripping any fault output.
            var block = new MotorDolBlock();
            var context = new TestExecutionContext();
            context.Parameters["MotorDolRunDelayMs"] = 0;

            context.SetInput("Start", SimulationValue.Bool(true));
            context.SetInput("Stop", SimulationValue.Bool(false));

            block.Execute(context);
            Assert.True(context.GetOutput("Output")!.AsBool());

            context.SetInput("Stop", SimulationValue.Bool(true));

            block.Execute(context);
            Assert.False(context.GetOutput("Output")!.AsBool());
        }

        [Fact]
        public void ValveBlock_NormallyOpen_SpringReturn_RestPositionIsOpen()
        {
            // Non-latching (spring-return) normally-open valve returns to its
            // rest (open) position when no command is active.
            var block = new ValveBlock();
            var context = new TestExecutionContext();
            context.Parameters["ValveTravelTimeMs"] = 0;
            context.Parameters["ValveNormallyOpen"] = true;
            context.Parameters["ValveLatching"] = false;

            // No commands active.
            context.SetInput("OpenCmd", SimulationValue.Bool(false));
            context.SetInput("CloseCmd", SimulationValue.Bool(false));

            block.Execute(context);

            Assert.True(context.GetOutput("Output")!.AsBool());
            Assert.False(context.GetOutput("Fault")!.AsBool());
        }

        [Fact]
        public void ValveBlock_Latching_NoCommand_HoldsLastPosition()
        {
            // Latching (default) valve holds its last commanded position when
            // both commands are de-asserted — it does not spring back.
            var block = new ValveBlock();
            var context = new TestExecutionContext();
            context.Parameters["ValveTravelTimeMs"] = 0;
            context.Parameters["ValveNormallyOpen"] = false;
            context.Parameters["ValveLatching"] = true;

            // Command the valve open.
            context.SetInput("OpenCmd", SimulationValue.Bool(true));
            context.SetInput("CloseCmd", SimulationValue.Bool(false));
            block.Execute(context);
            Assert.True(context.GetOutput("Output")!.AsBool());

            // De-assert the command: a latching valve stays open.
            context.SetInput("OpenCmd", SimulationValue.Bool(false));
            block.Execute(context);
            Assert.True(context.GetOutput("Output")!.AsBool());
            Assert.False(context.GetOutput("Fault")!.AsBool());
        }

        private sealed class TestExecutionContext : IExecutionContext
        {
            private readonly Dictionary<string, ISimulationValue> _inputs = new();
            private readonly Dictionary<string, ISimulationValue> _outputs = new();

            public DateTimeOffset CurrentTime => DateTimeOffset.UtcNow;
            public TimeSpan Elapsed { get; private set; } = TimeSpan.FromMilliseconds(100);
            public int CycleCount => 0;
            public bool IsFirstScan => true;
            public ModbusForge.Data.DataStore? DataStore => null;
            public Microsoft.Extensions.Logging.ILogger Logger => Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
            public IStateBag State { get; } = new StateBag();
            public Dictionary<string, object?> Parameters { get; } = new();

            public void OverrideElapsed(TimeSpan elapsed) => Elapsed = elapsed;

            public void SetInput(string name, ISimulationValue value) => _inputs[name] = value;
            public ISimulationValue? GetOutput(string name) => _outputs.TryGetValue(name, out var value) ? value : null;

            public ISimulationValue? ReadInput(string portName) => _inputs.TryGetValue(portName, out var value) ? value : null;

            public T ReadParameter<T>(string parameterName, T defaultValue)
            {
                if (Parameters.TryGetValue(parameterName, out var raw) && raw is T value)
                    return value;
                if (raw is IConvertible c)
                {
                    try
                    {
                        return (T)Convert.ChangeType(c, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
                    }
                    catch
                    {
                        return defaultValue;
                    }
                }
                return defaultValue;
            }

            public T? ReadParameter<T>(string parameterName)
            {
                return ReadParameter(parameterName, default(T)!);
            }

            public void WriteOutput(string portName, ISimulationValue value) => _outputs[portName] = value;
        }
    }
}
