using System.Collections.Generic;
using ModbusForge.Core.Simulation.Core;

namespace ModbusForge.Core.Simulation.Blocks
{
    public sealed class CtuBlock : IFunctionBlock
    {
        public static readonly BlockParameterDescriptor[] CounterParameters =
        {
            new()
            {
                Name = "CounterPreset",
                DisplayName = "Preset",
                Kind = BlockParameterKind.Int32,
                DefaultValue = 10,
                Minimum = 0,
                Maximum = 100000
            }
        };

        public string TypeId => "CTU";
        public string DisplayName => "CTU Counter";
        public string Category => "Counters";

        public IReadOnlyList<IPort> Ports { get; } = new List<IPort>
        {
            new PortDefinition(PortNames.CountUp, PortDirection.Input, SimulationDataType.Bool),
            new PortDefinition(PortNames.BoolOutput, PortDirection.Output, SimulationDataType.Bool)
        };

        public IReadOnlyList<BlockParameterDescriptor> Parameters => CounterParameters;

        public void Execute(IExecutionContext context)
        {
            var input = context.ReadInput(PortNames.CountUp)?.AsBool() ?? false;
            var preset = context.ReadParameter("CounterPreset", 10);
            var state = context.State.GetOrCreate<CounterState>("CounterState");

            if (input && !state.LastInput)
            {
                state.Value++;
            }

            state.LastInput = input;
            context.WriteOutput(PortNames.BoolOutput, SimulationValue.Bool(state.Value >= preset));
        }
    }

    public sealed class CtdBlock : IFunctionBlock
    {
        public string TypeId => "CTD";
        public string DisplayName => "CTD Counter";
        public string Category => "Counters";

        public IReadOnlyList<IPort> Ports { get; } = new List<IPort>
        {
            new PortDefinition(PortNames.CountDown, PortDirection.Input, SimulationDataType.Bool),
            new PortDefinition(PortNames.BoolOutput, PortDirection.Output, SimulationDataType.Bool)
        };

        public IReadOnlyList<BlockParameterDescriptor> Parameters => CtuBlock.CounterParameters;

        public void Execute(IExecutionContext context)
        {
            var input = context.ReadInput(PortNames.CountDown)?.AsBool() ?? false;
            var preset = context.ReadParameter("CounterPreset", 10);
            var state = context.State.GetOrCreate<CounterState>("CounterState");

            if (input && !state.LastInput)
            {
                state.Value--;
            }

            state.LastInput = input;
            context.WriteOutput(PortNames.BoolOutput, SimulationValue.Bool(state.Value <= 0));
        }
    }

    public sealed class CtcBlock : IFunctionBlock
    {
        public string TypeId => "CTC";
        public string DisplayName => "CTC Counter";
        public string Category => "Counters";

        public IReadOnlyList<IPort> Ports { get; } = new List<IPort>
        {
            new PortDefinition(PortNames.CountUp, PortDirection.Input, SimulationDataType.Bool),
            new PortDefinition(PortNames.CountDown, PortDirection.Input, SimulationDataType.Bool),
            new PortDefinition(PortNames.BoolOutput, PortDirection.Output, SimulationDataType.Bool)
        };

        public IReadOnlyList<BlockParameterDescriptor> Parameters => CtuBlock.CounterParameters;

        public void Execute(IExecutionContext context)
        {
            var input = context.ReadInput(PortNames.CountUp)?.AsBool() ?? false;
            var direction = context.ReadInput(PortNames.CountDown)?.AsBool() ?? false;
            var preset = context.ReadParameter("CounterPreset", 10);
            var state = context.State.GetOrCreate<CounterState>("CounterState");

            if (input && !state.LastInput)
            {
                if (direction)
                    state.Value++;
                else
                    state.Value--;
            }

            state.LastInput = input;
            context.WriteOutput(PortNames.BoolOutput, SimulationValue.Bool(state.Value >= preset));
        }
    }

    internal sealed class CounterState
    {
        public int Value { get; set; }
        public bool LastInput { get; set; }
    }
}
