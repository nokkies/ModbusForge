using System.Collections.Generic;
using ModbusForge.Simulation.Core;

namespace ModbusForge.Simulation.Blocks
{
    /// <summary>
    /// Base for blocks that read a Modbus address into the simulation.
    /// The actual DataStore read is performed by the execution engine via InputBindings.
    /// </summary>
    public abstract class InputBlockBase : IFunctionBlock
    {
        public string TypeId { get; }
        public string DisplayName { get; }
        public string Category => "I/O";

        public SimulationDataType OutputDataType { get; }

        public IReadOnlyList<IPort> Ports { get; }

        protected InputBlockBase(string typeId, string displayName, SimulationDataType outputDataType)
        {
            TypeId = typeId;
            DisplayName = displayName;
            OutputDataType = outputDataType;
            Ports = new List<IPort>
            {
                new PortDefinition("Input1", PortDirection.Input, outputDataType),
                new PortDefinition("Output", PortDirection.Output, outputDataType)
            };
        }

        public void Execute(IExecutionContext context)
        {
            var input = context.ReadInput("Input1");
            if (input == null)
            {
                input = SimulationValue.FromObject(OutputDataType, 0);
            }

            context.WriteOutput("Output", input);
        }
    }

    public sealed class InputBoolBlock : InputBlockBase
    {
        public InputBoolBlock() : base("InputBool", "Input BOOL", SimulationDataType.Bool) { }
    }

    public sealed class InputIntBlock : InputBlockBase
    {
        public InputIntBlock() : base("InputInt", "Input INT", SimulationDataType.Int32) { }
    }

    public sealed class LegacyInputBlock : InputBlockBase
    {
        public LegacyInputBlock() : base("Input", "Input", SimulationDataType.Bool) { }
    }

    /// <summary>
    /// Base for blocks that write a Modbus address from the simulation.
    /// The execution engine writes the output value to the bound DataStore address.
    /// </summary>
    public abstract class OutputBlockBase : IFunctionBlock
    {
        public string TypeId { get; }
        public string DisplayName { get; }
        public string Category => "I/O";

        public SimulationDataType InputDataType { get; }

        public IReadOnlyList<IPort> Ports { get; }

        protected OutputBlockBase(string typeId, string displayName, SimulationDataType inputDataType)
        {
            TypeId = typeId;
            DisplayName = displayName;
            InputDataType = inputDataType;
            Ports = new List<IPort>
            {
                new PortDefinition("Input1", PortDirection.Input, inputDataType),
                new PortDefinition("Output", PortDirection.Output, inputDataType)
            };
        }

        public void Execute(IExecutionContext context)
        {
            var input = context.ReadInput("Input1");
            if (input == null)
            {
                input = SimulationValue.FromObject(InputDataType, 0);
            }

            context.WriteOutput("Output", input);
        }
    }

    public sealed class OutputBoolBlock : OutputBlockBase
    {
        public OutputBoolBlock() : base("OutputBool", "Output BOOL", SimulationDataType.Bool) { }
    }

    public sealed class OutputIntBlock : OutputBlockBase
    {
        public OutputIntBlock() : base("OutputInt", "Output INT", SimulationDataType.Int32) { }
    }

    public sealed class LegacyOutputBlock : OutputBlockBase
    {
        public LegacyOutputBlock() : base("Output", "Output", SimulationDataType.Int32) { }
    }
}
