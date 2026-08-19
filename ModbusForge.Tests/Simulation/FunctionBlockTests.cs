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
            context.SetInput(PortNames.GateInput1, SimulationValue.Bool(true));
            context.SetInput(PortNames.GateInput2, SimulationValue.Bool(true));

            block.Execute(context);

            Assert.True(context.GetOutput(PortNames.BoolOutput)!.AsBool());
        }

        [Fact]
        public void AndBlock_FalseWhenOneInputFalse()
        {
            var block = new AndBlock();
            var context = new TestExecutionContext();
            context.SetInput(PortNames.GateInput1, SimulationValue.Bool(true));
            context.SetInput(PortNames.GateInput2, SimulationValue.Bool(false));

            block.Execute(context);

            Assert.False(context.GetOutput(PortNames.BoolOutput)!.AsBool());
        }

        [Fact]
        public void MathBlock_AddsTwoIntegers()
        {
            var block = new MathBlock(MathOperation.Add);
            var context = new TestExecutionContext();
            context.SetInput(PortNames.OperandA, SimulationValue.Int32(10));
            context.SetInput(PortNames.OperandB, SimulationValue.Int32(3));

            block.Execute(context);

            Assert.Equal(13, context.GetOutput(PortNames.BoolOutput)!.AsInt32());
        }

        [Fact]
        public void MathBlock_DivisionByZero_ReturnsZero()
        {
            var block = new MathBlock(MathOperation.Divide);
            var context = new TestExecutionContext();
            context.SetInput(PortNames.OperandA, SimulationValue.Int32(10));
            context.SetInput(PortNames.OperandB, SimulationValue.Int32(0));

            block.Execute(context);

            Assert.Equal(0, context.GetOutput(PortNames.BoolOutput)!.AsInt32());
        }

        [Fact]
        public void CompareBlock_GreaterThan_ReturnsTrue()
        {
            var block = new CompareBlock(ComparisonOperation.GreaterThan);
            var context = new TestExecutionContext();
            context.SetInput(PortNames.OperandA, SimulationValue.Int32(7));
            context.SetInput(PortNames.OperandB, SimulationValue.Int32(2));

            block.Execute(context);

            Assert.True(context.GetOutput(PortNames.BoolOutput)!.AsBool());
        }

        [Fact]
        public void CompareBlock_FallsBackToParameter()
        {
            var block = new CompareBlock(ComparisonOperation.Equal);
            var context = new TestExecutionContext();
            context.SetInput(PortNames.OperandA, SimulationValue.Int32(5));
            context.Parameters["CompareValue"] = 5;

            block.Execute(context);

            Assert.True(context.GetOutput(PortNames.BoolOutput)!.AsBool());
        }

        [Fact]
        public void ValveBlock_OpenCmd_ReachesOpen_AfterTravelTime()
        {
            var block = new ValveBlock();
            var context = new TestExecutionContext();
            context.Parameters["ValveTravelTimeMs"] = 500;

            // Start transition with OpenCmd.
            context.SetInput(PortNames.OpenCmd, SimulationValue.Bool(true));
            context.SetInput(PortNames.CloseCmd, SimulationValue.Bool(false));

            // First scan starts the transition.
            block.Execute(context);
            Assert.False(context.GetOutput(PortNames.ValveOpen)!.AsBool());

            // Elapse half the travel time — still not open.
            context.OverrideElapsed(TimeSpan.FromMilliseconds(250));
            block.Execute(context);
            Assert.False(context.GetOutput(PortNames.ValveOpen)!.AsBool());

            // Elapse full travel time — now open.
            context.OverrideElapsed(TimeSpan.FromMilliseconds(500));
            block.Execute(context);
            Assert.True(context.GetOutput(PortNames.ValveOpen)!.AsBool());
        }

        [Fact]
        public void ValveBlock_BothCommandsActive_RaisesFault()
        {
            var block = new ValveBlock();
            var context = new TestExecutionContext();
            context.SetInput(PortNames.OpenCmd, SimulationValue.Bool(true));
            context.SetInput(PortNames.CloseCmd, SimulationValue.Bool(true));

            block.Execute(context);

            Assert.False(context.GetOutput(PortNames.ValveOpen)!.AsBool());
            Assert.True(context.GetOutput(PortNames.Fault)!.AsBool());
        }

        [Fact]
        public void MotorDolBlock_StartPulse_PicksUpAfterRunDelay()
        {
            var block = new MotorDolBlock();
            var context = new TestExecutionContext();
            context.Parameters["MotorDolRunDelayMs"] = 100;

            context.SetInput(PortNames.Start, SimulationValue.Bool(true));
            context.SetInput(PortNames.Stop, SimulationValue.Bool(false));

            // First scan starts the pickup delay.
            context.OverrideElapsed(TimeSpan.FromMilliseconds(10));
            block.Execute(context);
            Assert.False(context.GetOutput(PortNames.MotorRun)!.AsBool());

            // Still before run delay.
            context.OverrideElapsed(TimeSpan.FromMilliseconds(50));
            block.Execute(context);
            Assert.False(context.GetOutput(PortNames.MotorRun)!.AsBool());

            // Run delay elapsed.
            context.OverrideElapsed(TimeSpan.FromMilliseconds(50));
            block.Execute(context);
            Assert.True(context.GetOutput(PortNames.MotorRun)!.AsBool());
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

            context.SetInput(PortNames.Run, SimulationValue.Bool(true));
            context.SetInput(PortNames.SpeedReference, SimulationValue.Real(50.0));

            // Initial scan starts ramp.
            context.OverrideElapsed(TimeSpan.FromMilliseconds(10));
            block.Execute(context);
            Assert.True(context.GetOutput(PortNames.VsdRunning)!.AsBool());
            var speed = context.GetOutput(PortNames.SpeedFeedback)!.AsReal();
            Assert.Equal(1.0, speed, precision: 1);
            Assert.False(context.GetOutput(PortNames.AtSpeed)!.AsBool());

            // A quarter of the ramp time should reach ~25 (half the 50 reference).
            context.OverrideElapsed(TimeSpan.FromMilliseconds(250));
            block.Execute(context);
            var feedback = context.GetOutput(PortNames.SpeedFeedback)!.AsReal();
            Assert.True(feedback > 20 && feedback < 30);

            // Full ramp time should reach the 50 target.
            context.OverrideElapsed(TimeSpan.FromMilliseconds(1000));
            block.Execute(context);
            Assert.Equal(50.0, context.GetOutput(PortNames.SpeedFeedback)!.AsReal(), precision: 1);
            Assert.True(context.GetOutput(PortNames.AtSpeed)!.AsBool());
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

            context.SetInput(PortNames.Run, SimulationValue.Bool(true));
            context.SetInput(PortNames.SpeedReference, SimulationValue.Real(100.0));

            context.OverrideElapsed(TimeSpan.FromMilliseconds(0));
            block.Execute(context);
            Assert.Equal(100.0, context.GetOutput(PortNames.SpeedFeedback)!.AsReal(), precision: 0);

            context.SetInput(PortNames.Run, SimulationValue.Bool(false));
            context.OverrideElapsed(TimeSpan.FromMilliseconds(1000));
            block.Execute(context);

            Assert.False(context.GetOutput(PortNames.VsdRunning)!.AsBool());
            Assert.Equal(0.0, context.GetOutput(PortNames.SpeedFeedback)!.AsReal(), precision: 1);
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

            context.SetInput(PortNames.Run, SimulationValue.Bool(true));
            context.SetInput(PortNames.SpeedReference, SimulationValue.Real(50.0));

            block.Execute(context);

            Assert.Equal(50.0, context.GetOutput(PortNames.SpeedFeedback)!.AsReal(), precision: 0);
            Assert.True(context.GetOutput(PortNames.AtSpeed)!.AsBool());
        }

        [Fact]
        public void MotorDolBlock_StopActive_DropsRunning_WithoutFault()
        {
            // Stop is a normal stop command: it de-energises the starter and
            // de-asserts Running without tripping any fault output.
            var block = new MotorDolBlock();
            var context = new TestExecutionContext();
            context.Parameters["MotorDolRunDelayMs"] = 0;

            context.SetInput(PortNames.Start, SimulationValue.Bool(true));
            context.SetInput(PortNames.Stop, SimulationValue.Bool(false));

            block.Execute(context);
            Assert.True(context.GetOutput(PortNames.MotorRun)!.AsBool());

            context.SetInput(PortNames.Stop, SimulationValue.Bool(true));

            block.Execute(context);
            Assert.False(context.GetOutput(PortNames.MotorRun)!.AsBool());
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
            context.SetInput(PortNames.OpenCmd, SimulationValue.Bool(false));
            context.SetInput(PortNames.CloseCmd, SimulationValue.Bool(false));

            block.Execute(context);

            Assert.True(context.GetOutput(PortNames.ValveOpen)!.AsBool());
            Assert.False(context.GetOutput(PortNames.Fault)!.AsBool());
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
            context.SetInput(PortNames.OpenCmd, SimulationValue.Bool(true));
            context.SetInput(PortNames.CloseCmd, SimulationValue.Bool(false));
            block.Execute(context);
            Assert.True(context.GetOutput(PortNames.ValveOpen)!.AsBool());

            // De-assert the command: a latching valve stays open.
            context.SetInput(PortNames.OpenCmd, SimulationValue.Bool(false));
            block.Execute(context);
            Assert.True(context.GetOutput(PortNames.ValveOpen)!.AsBool());
            Assert.False(context.GetOutput(PortNames.Fault)!.AsBool());
        }

        [Fact]
        public void ScaleBlock_MapsRawValueIntoTargetRange()
        {
            var block = new ScaleBlock();
            var context = new TestExecutionContext();
            context.Parameters["ScaleFromMin"] = 0.0;
            context.Parameters["ScaleFromMax"] = 100.0;
            context.Parameters["ScaleToMin"] = 0.0;
            context.Parameters["ScaleToMax"] = 120.0;
            context.SetInput(PortNames.Value, SimulationValue.Real(50));

            block.Execute(context);

            Assert.Equal(60.0, context.GetOutput(PortNames.BoolOutput)!.AsReal(), precision: 10);
        }

        [Fact]
        public void ScaleBlock_AcceptsIntegerInput_FromRegisterSource()
        {
            var block = new ScaleBlock();
            var context = new TestExecutionContext();
            context.Parameters["ScaleFromMin"] = 0.0;
            context.Parameters["ScaleFromMax"] = 200.0;
            context.Parameters["ScaleToMin"] = 0.0;
            context.Parameters["ScaleToMax"] = 1.0;
            context.SetInput(PortNames.Value, SimulationValue.Int32(40));

            block.Execute(context);

            Assert.Equal(0.2, context.GetOutput(PortNames.BoolOutput)!.AsReal(), precision: 10);
        }

        [Fact]
        public void ScaleBlock_InvertedTargetRange_MapsInversely()
        {
            var block = new ScaleBlock();
            var context = new TestExecutionContext();
            context.Parameters["ScaleFromMin"] = 0.0;
            context.Parameters["ScaleFromMax"] = 100.0;
            context.Parameters["ScaleToMin"] = 100.0;
            context.Parameters["ScaleToMax"] = 0.0;

            context.SetInput(PortNames.Value, SimulationValue.Real(0));
            block.Execute(context);
            Assert.Equal(100.0, context.GetOutput(PortNames.BoolOutput)!.AsReal(), precision: 10);

            context.SetInput(PortNames.Value, SimulationValue.Real(100));
            block.Execute(context);
            Assert.Equal(0.0, context.GetOutput(PortNames.BoolOutput)!.AsReal(), precision: 10);
        }

        [Fact]
        public void ScaleBlock_DegenerateSourceSpan_YieldsTargetMin_WithoutNaN()
        {
            var block = new ScaleBlock();
            var context = new TestExecutionContext();
            context.Parameters["ScaleFromMin"] = 5.0;
            context.Parameters["ScaleFromMax"] = 5.0;
            context.Parameters["ScaleToMin"] = 42.0;
            context.Parameters["ScaleToMax"] = 42.0;
            context.SetInput(PortNames.Value, SimulationValue.Real(99));

            block.Execute(context);

            var output = context.GetOutput(PortNames.BoolOutput)!.AsReal();
            Assert.True(double.IsFinite(output));
            Assert.Equal(42.0, output, precision: 10);
        }

        [Fact]
        public void ScaleBlock_ClampsOutOfRangeRaw_WhenClampEnabled()
        {
            var block = new ScaleBlock();
            var context = new TestExecutionContext();
            context.Parameters["ScaleFromMin"] = 0.0;
            context.Parameters["ScaleFromMax"] = 100.0;
            context.Parameters["ScaleToMin"] = 0.0;
            context.Parameters["ScaleToMax"] = 120.0;
            context.Parameters["ScaleClamp"] = true;

            context.SetInput(PortNames.Value, SimulationValue.Real(150));
            block.Execute(context);
            Assert.Equal(120.0, context.GetOutput(PortNames.BoolOutput)!.AsReal(), precision: 10);

            context.SetInput(PortNames.Value, SimulationValue.Real(-10));
            block.Execute(context);
            Assert.Equal(0.0, context.GetOutput(PortNames.BoolOutput)!.AsReal(), precision: 10);
        }

        [Fact]
        public void ScaleBlock_KeepsOutOfRangeResult_WhenClampDisabled()
        {
            var block = new ScaleBlock();
            var context = new TestExecutionContext();
            context.Parameters["ScaleFromMin"] = 0.0;
            context.Parameters["ScaleFromMax"] = 100.0;
            context.Parameters["ScaleToMin"] = 0.0;
            context.Parameters["ScaleToMax"] = 120.0;
            context.Parameters["ScaleClamp"] = false;

            context.SetInput(PortNames.Value, SimulationValue.Real(150));
            block.Execute(context);

            Assert.Equal(180.0, context.GetOutput(PortNames.BoolOutput)!.AsReal(), precision: 10);
        }

        [Fact]
        public void ScaleBlock_NonFiniteInput_YieldsTargetMin()
        {
            var block = new ScaleBlock();
            var context = new TestExecutionContext();
            context.Parameters["ScaleToMax"] = 100.0;
            context.SetInput(PortNames.Value, SimulationValue.Real(double.NaN));

            block.Execute(context);

            var output = context.GetOutput(PortNames.BoolOutput)!.AsReal();
            Assert.True(double.IsFinite(output));
            Assert.Equal(0.0, output, precision: 10);
        }

        [Fact]
        public void EdgeDetectBlock_RisingEdge_PulsesForOneCycleOnly()
        {
            var block = new EdgeDetectBlock();
            var context = new TestExecutionContext();

            // First scan with the input already high: records the level, no pulse.
            context.SetInput(PortNames.TimerInput, SimulationValue.Bool(true));
            block.Execute(context);
            Assert.False(context.GetOutput(PortNames.BoolOutput)!.AsBool());

            // Still high: no pulse.
            block.Execute(context);
            Assert.False(context.GetOutput(PortNames.BoolOutput)!.AsBool());

            // Going low: no pulse (rising detector).
            context.SetInput(PortNames.TimerInput, SimulationValue.Bool(false));
            block.Execute(context);
            Assert.False(context.GetOutput(PortNames.BoolOutput)!.AsBool());

            // The real rising edge: one-cycle pulse.
            context.SetInput(PortNames.TimerInput, SimulationValue.Bool(true));
            block.Execute(context);
            Assert.True(context.GetOutput(PortNames.BoolOutput)!.AsBool());

            // Next cycle the pulse is gone even though the input is still high.
            block.Execute(context);
            Assert.False(context.GetOutput(PortNames.BoolOutput)!.AsBool());
        }

        [Fact]
        public void EdgeDetectBlock_FallingDirection_PulsesOnFallingTransition()
        {
            var block = new EdgeDetectBlock();
            var context = new TestExecutionContext();
            context.Parameters["EdgeDetectDirection"] = EdgeDetectBlock.Falling;

            // First scan (low): no pulse.
            context.SetInput(PortNames.TimerInput, SimulationValue.Bool(false));
            block.Execute(context);
            Assert.False(context.GetOutput(PortNames.BoolOutput)!.AsBool());

            // Rising: no pulse for a falling detector.
            context.SetInput(PortNames.TimerInput, SimulationValue.Bool(true));
            block.Execute(context);
            Assert.False(context.GetOutput(PortNames.BoolOutput)!.AsBool());

            // Falling edge: pulse.
            context.SetInput(PortNames.TimerInput, SimulationValue.Bool(false));
            block.Execute(context);
            Assert.True(context.GetOutput(PortNames.BoolOutput)!.AsBool());

            // Still low: no further pulse.
            block.Execute(context);
            Assert.False(context.GetOutput(PortNames.BoolOutput)!.AsBool());
        }

        [Fact]
        public void MovingAverageBlock_FillsTheWindowGradually()
        {
            var block = new MovingAverageBlock();
            var context = new TestExecutionContext();
            context.Parameters["MaWindowSize"] = 3;

            context.SetInput(PortNames.Value, SimulationValue.Real(1));
            block.Execute(context);
            Assert.Equal(1.0, context.GetOutput(PortNames.BoolOutput)!.AsReal(), precision: 10);

            context.SetInput(PortNames.Value, SimulationValue.Real(2));
            block.Execute(context);
            Assert.Equal(1.5, context.GetOutput(PortNames.BoolOutput)!.AsReal(), precision: 10);

            context.SetInput(PortNames.Value, SimulationValue.Real(3));
            block.Execute(context);
            Assert.Equal(2.0, context.GetOutput(PortNames.BoolOutput)!.AsReal(), precision: 10);
        }

        [Fact]
        public void MovingAverageBlock_FullWindow_AveragesTheLastNSamples()
        {
            var block = new MovingAverageBlock();
            var context = new TestExecutionContext();
            context.Parameters["MaWindowSize"] = 2;

            foreach (var sample in new[] { 10.0, 10.0, 20.0, 30.0 })
            {
                context.SetInput(PortNames.Value, SimulationValue.Real(sample));
                block.Execute(context);
            }

            // Window of the last two samples: (20 + 30) / 2.
            Assert.Equal(25.0, context.GetOutput(PortNames.BoolOutput)!.AsReal(), precision: 10);
        }

        [Fact]
        public void MovingAverageBlock_WindowChange_StartsFresh()
        {
            var block = new MovingAverageBlock();
            var context = new TestExecutionContext();
            context.Parameters["MaWindowSize"] = 2;

            context.SetInput(PortNames.Value, SimulationValue.Real(10));
            block.Execute(context);
            context.SetInput(PortNames.Value, SimulationValue.Real(20));
            block.Execute(context);
            Assert.Equal(15.0, context.GetOutput(PortNames.BoolOutput)!.AsReal(), precision: 10);

            context.Parameters["MaWindowSize"] = 3;
            context.SetInput(PortNames.Value, SimulationValue.Real(30));
            block.Execute(context);
            // The old two-sample buffer must not leak into the new window.
            Assert.Equal(30.0, context.GetOutput(PortNames.BoolOutput)!.AsReal(), precision: 10);
        }

        [Fact]
        public void MovingAverageBlock_SkipsNonFiniteSamples()
        {
            var block = new MovingAverageBlock();
            var context = new TestExecutionContext();
            context.Parameters["MaWindowSize"] = 2;

            context.SetInput(PortNames.Value, SimulationValue.Real(10));
            block.Execute(context);
            Assert.Equal(10.0, context.GetOutput(PortNames.BoolOutput)!.AsReal(), precision: 10);

            // A glitched register: skipped, the previous average holds.
            context.SetInput(PortNames.Value, SimulationValue.Real(double.PositiveInfinity));
            block.Execute(context);
            Assert.Equal(10.0, context.GetOutput(PortNames.BoolOutput)!.AsReal(), precision: 10);

            context.SetInput(PortNames.Value, SimulationValue.Real(20));
            block.Execute(context);
            Assert.Equal(15.0, context.GetOutput(PortNames.BoolOutput)!.AsReal(), precision: 10);
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
