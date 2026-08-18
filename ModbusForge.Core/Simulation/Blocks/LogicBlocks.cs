using System.Collections.Generic;
using ModbusForge.Core.Simulation.Core;

namespace ModbusForge.Core.Simulation.Blocks
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
            new PortDefinition(PortNames.GateInput1, PortDirection.Input, SimulationDataType.Bool),
            new PortDefinition(PortNames.GateInput2, PortDirection.Input, SimulationDataType.Bool),
            new PortDefinition(PortNames.BoolOutput, PortDirection.Output, SimulationDataType.Bool)
        };

        public IReadOnlyList<BlockParameterDescriptor> Parameters => EmptyParameters;

        public static readonly BlockParameterDescriptor[] EmptyParameters = System.Array.Empty<BlockParameterDescriptor>();

        public void Execute(IExecutionContext context)
        {
            var in1 = context.ReadInput(PortNames.GateInput1)?.AsBool() ?? false;
            var in2 = context.ReadInput(PortNames.GateInput2)?.AsBool() ?? false;
            context.WriteOutput(PortNames.BoolOutput, SimulationValue.Bool(Compute(in1, in2)));
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
            new PortDefinition(PortNames.TimerInput, PortDirection.Input, SimulationDataType.Bool),
            new PortDefinition(PortNames.BoolOutput, PortDirection.Output, SimulationDataType.Bool)
        };

        public IReadOnlyList<BlockParameterDescriptor> Parameters => BooleanLogicBlock.EmptyParameters;

        public void Execute(IExecutionContext context)
        {
            var value = context.ReadInput(PortNames.TimerInput)?.AsBool() ?? false;
            context.WriteOutput(PortNames.BoolOutput, SimulationValue.Bool(!value));
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
            new PortDefinition(PortNames.LatchSet, PortDirection.Input, SimulationDataType.Bool),
            new PortDefinition(PortNames.LatchReset, PortDirection.Input, SimulationDataType.Bool),
            new PortDefinition(PortNames.BoolOutput, PortDirection.Output, SimulationDataType.Bool)
        };

        public IReadOnlyList<BlockParameterDescriptor> Parameters { get; } = new[]
        {
            new BlockParameterDescriptor
            {
                Name = "SetDominant",
                DisplayName = "Set dominant",
                Kind = BlockParameterKind.Bool,
                DefaultValue = true
            }
        };

        public void Execute(IExecutionContext context)
        {
            var set = context.ReadInput(PortNames.LatchSet)?.AsBool() ?? false;
            var reset = context.ReadInput(PortNames.LatchReset)?.AsBool() ?? false;
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

            context.WriteOutput(PortNames.BoolOutput, SimulationValue.Bool(state.Value));
        }
    }

    internal sealed class RsLatchState
    {
        public bool Value { get; set; }
    }
}
