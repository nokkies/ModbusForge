using ModbusForge.Simulation.Blocks;
using ModbusForge.Simulation.Core;
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

        private sealed class TestExecutionContext : IExecutionContext
        {
            private readonly Dictionary<string, ISimulationValue> _inputs = new();
            private readonly Dictionary<string, ISimulationValue> _outputs = new();

            public DateTimeOffset CurrentTime => DateTimeOffset.UtcNow;
            public TimeSpan Elapsed => TimeSpan.FromMilliseconds(100);
            public int CycleCount => 0;
            public bool IsFirstScan => true;
            public Modbus.Data.DataStore? DataStore => null;
            public Microsoft.Extensions.Logging.ILogger Logger => Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
            public IStateBag State { get; } = new StateBag();
            public Dictionary<string, object?> Parameters { get; } = new();

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
