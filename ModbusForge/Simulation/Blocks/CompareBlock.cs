using System;
using System.Collections.Generic;
using ModbusForge.Simulation.Core;

namespace ModbusForge.Simulation.Blocks
{
    public enum ComparisonOperation
    {
        Equal,
        NotEqual,
        GreaterThan,
        LessThan,
        GreaterThanOrEqual,
        LessThanOrEqual
    }

    public sealed class CompareBlock : IFunctionBlock
    {
        public string TypeId { get; }
        public string DisplayName { get; }
        public string Category => "Comparators";

        public ComparisonOperation Operation { get; }

        public IReadOnlyList<IPort> Ports { get; } = new List<IPort>
        {
            new PortDefinition("Input1", PortDirection.Input, SimulationDataType.Int32),
            new PortDefinition("Input2", PortDirection.Input, SimulationDataType.Int32),
            new PortDefinition("Output", PortDirection.Output, SimulationDataType.Bool)
        };

        public CompareBlock(ComparisonOperation operation)
        {
            Operation = operation;
            TypeId = operation switch
            {
                ComparisonOperation.Equal => "COMPARE_EQ",
                ComparisonOperation.NotEqual => "COMPARE_NE",
                ComparisonOperation.GreaterThan => "COMPARE_GT",
                ComparisonOperation.LessThan => "COMPARE_LT",
                ComparisonOperation.GreaterThanOrEqual => "COMPARE_GE",
                ComparisonOperation.LessThanOrEqual => "COMPARE_LE",
                _ => throw new ArgumentOutOfRangeException(nameof(operation))
            };
            DisplayName = operation switch
            {
                ComparisonOperation.Equal => "Equal (==)",
                ComparisonOperation.NotEqual => "Not Equal (!=)",
                ComparisonOperation.GreaterThan => "Greater Than (>)",
                ComparisonOperation.LessThan => "Less Than (<)",
                ComparisonOperation.GreaterThanOrEqual => "Greater Equal (>=)",
                ComparisonOperation.LessThanOrEqual => "Less Equal (<=)",
                _ => throw new ArgumentOutOfRangeException(nameof(operation))
            };
        }

        public void Execute(IExecutionContext context)
        {
            var in1 = context.ReadInput("Input1")?.AsInt32() ?? 0;
            var in2 = context.ReadInput("Input2")?.AsInt32() ?? context.ReadParameter("CompareValue", 0);

            bool result = Operation switch
            {
                ComparisonOperation.Equal => in1 == in2,
                ComparisonOperation.NotEqual => in1 != in2,
                ComparisonOperation.GreaterThan => in1 > in2,
                ComparisonOperation.LessThan => in1 < in2,
                ComparisonOperation.GreaterThanOrEqual => in1 >= in2,
                ComparisonOperation.LessThanOrEqual => in1 <= in2,
                _ => false
            };

            context.WriteOutput("Output", SimulationValue.Bool(result));
        }
    }
}
