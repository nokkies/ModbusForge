using System.Collections.Generic;
using ModbusForge.Simulation.Core;

namespace ModbusForge.Simulation.Blocks
{
    public sealed class CtuBlock : IFunctionBlock
    {
        public string TypeId => "CTU";
        public string DisplayName => "CTU Counter";
        public string Category => "Counters";

        public IReadOnlyList<IPort> Ports { get; } = new List<IPort>
        {
            new PortDefinition("Input1", PortDirection.Input, SimulationDataType.Bool),
            new PortDefinition("Output", PortDirection.Output, SimulationDataType.Bool)
        };

        public void Execute(IExecutionContext context)
        {
            var input = context.ReadInput("Input1")?.AsBool() ?? false;
            var preset = context.ReadParameter("CounterPreset", 10);
            var state = context.State.GetOrCreate<CounterState>("CounterState");

            if (input && !state.LastInput)
            {
                state.Value++;
            }

            state.LastInput = input;
            context.WriteOutput("Output", SimulationValue.Bool(state.Value >= preset));
        }
    }

    public sealed class CtdBlock : IFunctionBlock
    {
        public string TypeId => "CTD";
        public string DisplayName => "CTD Counter";
        public string Category => "Counters";

        public IReadOnlyList<IPort> Ports { get; } = new List<IPort>
        {
            new PortDefinition("Input1", PortDirection.Input, SimulationDataType.Bool),
            new PortDefinition("Output", PortDirection.Output, SimulationDataType.Bool)
        };

        public void Execute(IExecutionContext context)
        {
            var input = context.ReadInput("Input1")?.AsBool() ?? false;
            var preset = context.ReadParameter("CounterPreset", 10);
            var state = context.State.GetOrCreate<CounterState>("CounterState");

            if (input && !state.LastInput)
            {
                state.Value--;
            }

            state.LastInput = input;
            context.WriteOutput("Output", SimulationValue.Bool(state.Value <= 0));
        }
    }

    public sealed class CtcBlock : IFunctionBlock
    {
        public string TypeId => "CTC";
        public string DisplayName => "CTC Counter";
        public string Category => "Counters";

        public IReadOnlyList<IPort> Ports { get; } = new List<IPort>
        {
            new PortDefinition("Input1", PortDirection.Input, SimulationDataType.Bool),
            new PortDefinition("Input2", PortDirection.Input, SimulationDataType.Bool),
            new PortDefinition("Output", PortDirection.Output, SimulationDataType.Bool)
        };

        public void Execute(IExecutionContext context)
        {
            var input = context.ReadInput("Input1")?.AsBool() ?? false;
            var direction = context.ReadInput("Input2")?.AsBool() ?? false;
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
            context.WriteOutput("Output", SimulationValue.Bool(state.Value >= preset));
        }
    }

    internal sealed class CounterState
    {
        public int Value { get; set; }
        public bool LastInput { get; set; }
    }
}
