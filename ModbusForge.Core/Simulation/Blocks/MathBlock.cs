using System;
using System.Collections.Generic;
using ModbusForge.Core.Simulation.Core;

namespace ModbusForge.Core.Simulation.Blocks
{
    public enum MathOperation
    {
        Add,
        Subtract,
        Multiply,
        Divide
    }

    /// <summary>
    /// Binary math block. Integer variant (Int32 ports) and real variant (Real ports);
    /// the unconnected second input falls back to the "Constant" parameter.
    /// </summary>
    public sealed class MathBlock : IFunctionBlock
    {
        private static readonly Dictionary<MathOperation, (string Int, string Real, string Name)> Operations = new()
        {
            [MathOperation.Add] = ("MATH_ADD", "MATH_ADD_REAL", "Add (+)"),
            [MathOperation.Subtract] = ("MATH_SUB", "MATH_SUB_REAL", "Subtract (-)"),
            [MathOperation.Multiply] = ("MATH_MUL", "MATH_MUL_REAL", "Multiply (*)"),
            [MathOperation.Divide] = ("MATH_DIV", "MATH_DIV_REAL", "Divide (/)")
        };

        public MathOperation Operation { get; }
        public bool IsReal { get; }

        public string TypeId { get; }
        public string DisplayName { get; }
        public string Category => IsReal ? "Math Operations (Real)" : "Math Operations";

        public IReadOnlyList<IPort> Ports { get; }

        public IReadOnlyList<BlockParameterDescriptor> Parameters { get; }

        public MathBlock(MathOperation operation, bool isReal = false)
        {
            Operation = operation;
            IsReal = isReal;

            if (!Operations.TryGetValue(operation, out var op))
                throw new ArgumentOutOfRangeException(nameof(operation));
            var (intId, realId, name) = op;

            TypeId = isReal ? realId : intId;
            DisplayName = isReal ? $"{name} (Real)" : name;

            var dataType = isReal ? SimulationDataType.Real : SimulationDataType.Int32;
            Ports = new List<IPort>
            {
                new PortDefinition("Input1", PortDirection.Input, dataType),
                new PortDefinition("Input2", PortDirection.Input, dataType),
                new PortDefinition("Output", PortDirection.Output, dataType)
            };

            Parameters = new[]
            {
                new BlockParameterDescriptor
                {
                    // Distinct name for the real variant: the int and real constants live in
                    // separate VisualNode properties (CompareValue vs CompareValueReal).
                    Name = isReal ? "ConstantReal" : "Constant",
                    DisplayName = "Constant",
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
                    ? context.ReadParameter("ConstantReal", 0.0)
                    : context.ReadParameter("Constant", 0);

            double result = Operation switch
            {
                MathOperation.Add => in1 + in2,
                MathOperation.Subtract => in1 - in2,
                MathOperation.Multiply => in1 * in2,
                MathOperation.Divide => in2 != 0 ? in1 / in2 : 0.0,
                _ => 0.0
            };

            var output = IsReal
                ? SimulationValue.Real(result)
                : SimulationValue.Int32((int)Math.Round(result));

            context.WriteOutput("Output", output);
        }
    }
}
