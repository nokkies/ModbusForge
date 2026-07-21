using System;
using System.Collections.Generic;
using System.Linq;
using ModbusForge.Simulation.Core;

namespace ModbusForge.Simulation.Blocks
{
    /// <summary>
    /// Base class for boolean logic blocks.
    /// </summary>
    public abstract class BooleanLogicBlock : IFunctionBlock
    {
        public abstract string TypeId { get; }
        public abstract string DisplayName { get; }
        public string Category => "Logic Gates";

        public IReadOnlyList<IPort> Ports { get; } = new List<IPort>
        {
            new PortDefinition("Input1", PortDirection.Input, SimulationDataType.Bool),
            new PortDefinition("Input2", PortDirection.Input, SimulationDataType.Bool),
            new PortDefinition("Output", PortDirection.Output, SimulationDataType.Bool)
        };

        public void Execute(IExecutionContext context)
        {
            var in1 = context.ReadInput("Input1")?.AsBool() ?? false;
            var in2 = context.ReadInput("Input2")?.AsBool() ?? false;
            context.WriteOutput("Output", SimulationValue.Bool(Compute(in1, in2)));
        }

        protected abstract bool Compute(bool in1, bool in2);
    }

    public sealed class NotBlock : IFunctionBlock
    {
        public string TypeId => "NOT";
        public string DisplayName => "NOT Gate";
        public string Category => "Logic Gates";

        public IReadOnlyList<IPort> Ports { get; } = new List<IPort>
        {
            new PortDefinition("Input1", PortDirection.Input, SimulationDataType.Bool),
            new PortDefinition("Output", PortDirection.Output, SimulationDataType.Bool)
        };

        public void Execute(IExecutionContext context)
        {
            var value = context.ReadInput("Input1")?.AsBool() ?? false;
            context.WriteOutput("Output", SimulationValue.Bool(!value));
        }
    }

    public sealed class AndBlock : BooleanLogicBlock
    {
        public override string TypeId => "AND";
        public override string DisplayName => "AND Gate";
        protected override bool Compute(bool in1, bool in2) => in1 && in2;
    }

    public sealed class OrBlock : BooleanLogicBlock
    {
        public override string TypeId => "OR";
        public override string DisplayName => "OR Gate";
        protected override bool Compute(bool in1, bool in2) => in1 || in2;
    }

    public sealed class RsLatchBlock : IFunctionBlock
    {
        public string TypeId => "RS";
        public string DisplayName => "RS Latch";
        public string Category => "Logic Gates";

        public IReadOnlyList<IPort> Ports { get; } = new List<IPort>
        {
            new PortDefinition("Input1", PortDirection.Input, SimulationDataType.Bool),
            new PortDefinition("Input2", PortDirection.Input, SimulationDataType.Bool),
            new PortDefinition("Output", PortDirection.Output, SimulationDataType.Bool)
        };

        public void Execute(IExecutionContext context)
        {
            var set = context.ReadInput("Input1")?.AsBool() ?? false;
            var reset = context.ReadInput("Input2")?.AsBool() ?? false;
            var setDominant = context.ReadParameter("SetDominant", true);

            var state = context.State.GetOrCreate<RsLatchState>("RsState");

            if (setDominant)
            {
                if (reset) state.Value = false;
                if (set) state.Value = true;
            }
            else
            {
                if (set) state.Value = true;
                if (reset) state.Value = false;
            }

            context.WriteOutput("Output", SimulationValue.Bool(state.Value));
        }

    }

    internal sealed class RsLatchState
    {
        public bool Value { get; set; }
    }
}
