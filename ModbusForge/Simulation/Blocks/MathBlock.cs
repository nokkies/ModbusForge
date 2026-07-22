using System;
using System.Collections.Generic;
using ModbusForge.Simulation.Core;

namespace ModbusForge.Simulation.Blocks
{
    public enum MathOperation
    {
        Add,
        Subtract,
        Multiply,
        Divide
    }

    public sealed class MathBlock : IFunctionBlock
    {
        public string TypeId { get; }
        public string DisplayName { get; }
        public string Category => "Math Operations";

        public MathOperation Operation { get; }

        public IReadOnlyList<IPort> Ports { get; } = new List<IPort>
        {
            new PortDefinition("Input1", PortDirection.Input, SimulationDataType.Int32),
            new PortDefinition("Input2", PortDirection.Input, SimulationDataType.Int32),
            new PortDefinition("Output", PortDirection.Output, SimulationDataType.Int32)
        };

        public MathBlock(MathOperation operation)
        {
            Operation = operation;
            TypeId = operation switch
            {
                MathOperation.Add => "MATH_ADD",
                MathOperation.Subtract => "MATH_SUB",
                MathOperation.Multiply => "MATH_MUL",
                MathOperation.Divide => "MATH_DIV",
                _ => throw new ArgumentOutOfRangeException(nameof(operation))
            };
            DisplayName = operation switch
            {
                MathOperation.Add => "Add (+)",
                MathOperation.Subtract => "Subtract (-)",
                MathOperation.Multiply => "Multiply (*)",
                MathOperation.Divide => "Divide (/)",
                _ => throw new ArgumentOutOfRangeException(nameof(operation))
            };
        }

        public void Execute(IExecutionContext context)
        {
            var in1 = context.ReadInput("Input1")?.AsInt32() ?? 0;
            var in2 = context.ReadInput("Input2")?.AsInt32() ?? context.ReadParameter("CompareValue", 0);

            int result = Operation switch
            {
                MathOperation.Add => in1 + in2,
                MathOperation.Subtract => in1 - in2,
                MathOperation.Multiply => in1 * in2,
                MathOperation.Divide => in2 != 0 ? in1 / in2 : 0,
                _ => 0
            };

            context.WriteOutput("Output", SimulationValue.Int32(result));
        }
    }
}
