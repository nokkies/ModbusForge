using System;
using System.Collections.Generic;
using ModbusForge.Core.Simulation.Core;

namespace ModbusForge.Core.Simulation.Blocks
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

    /// <summary>
    /// Binary comparison block. Integer variant (Int32 inputs) and real variant (Real inputs);
    /// the unconnected second input falls back to the "CompareValue" parameter. Output is Bool.
    /// </summary>
    public sealed class CompareBlock : IFunctionBlock
    {
        private static readonly Dictionary<ComparisonOperation, (string Int, string Real, string Name)> Operations = new()
        {
            [ComparisonOperation.Equal] = ("COMPARE_EQ", "COMPARE_EQ_REAL", "Equal (==)"),
            [ComparisonOperation.NotEqual] = ("COMPARE_NE", "COMPARE_NE_REAL", "Not Equal (!=)"),
            [ComparisonOperation.GreaterThan] = ("COMPARE_GT", "COMPARE_GT_REAL", "Greater Than (>)"),
            [ComparisonOperation.LessThan] = ("COMPARE_LT", "COMPARE_LT_REAL", "Less Than (<)"),
            [ComparisonOperation.GreaterThanOrEqual] = ("COMPARE_GE", "COMPARE_GE_REAL", "Greater Equal (>=)"),
            [ComparisonOperation.LessThanOrEqual] = ("COMPARE_LE", "COMPARE_LE_REAL", "Less Equal (<=)")
        };

        public ComparisonOperation Operation { get; }
        public bool IsReal { get; }

        public string TypeId { get; }
        public string DisplayName { get; }
        public string Category => IsReal ? "Comparators (Real)" : "Comparators";

        public IReadOnlyList<IPort> Ports { get; }

        public IReadOnlyList<BlockParameterDescriptor> Parameters { get; }

        public CompareBlock(ComparisonOperation operation, bool isReal = false)
        {
            Operation = operation;
            IsReal = isReal;

            if (!Operations.TryGetValue(operation, out var op))
                throw new ArgumentOutOfRangeException(nameof(operation));
            var (intId, realId, name) = op;

            TypeId = isReal ? realId : intId;
            DisplayName = isReal ? $"{name} (Real)" : name;

            var inputType = isReal ? SimulationDataType.Real : SimulationDataType.Int32;
            Ports = new List<IPort>
            {
                new PortDefinition("Input1", PortDirection.Input, inputType),
                new PortDefinition("Input2", PortDirection.Input, inputType),
                new PortDefinition("Output", PortDirection.Output, SimulationDataType.Bool)
            };

            Parameters = new[]
            {
                new BlockParameterDescriptor
                {
                    // Distinct name for the real variant: the int and real constants live in
                    // separate VisualNode properties (CompareValue vs CompareValueReal).
                    Name = isReal ? "CompareValueReal" : "CompareValue",
                    DisplayName = "Value",
                    Kind = isReal ? BlockParameterKind.Real : BlockParameterKind.Int32,
                    DefaultValue = isReal ? 0.0 : 0
                }
            };
        }

        public void Execute(IExecutionContext context)
        {
            double in1 = context.ReadInput("Input1")?.AsReal() ?? 0.0;
            double in2 = context.ReadInput("Input2") is not null
                ? context.ReadInput("Input2")!.AsReal()
                : IsReal
                    ? context.ReadParameter("CompareValueReal", 0.0)
                    : context.ReadParameter("CompareValue", 0);

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
