using System.Collections.Generic;
using ModbusForge.Core.Simulation.Core;

namespace ModbusForge.Core.Simulation.Blocks
{
    public sealed class TonBlock : IFunctionBlock
    {
        public static readonly BlockParameterDescriptor[] TimerParameters =
        {
            new()
            {
                Name = "TimerPresetMs",
                DisplayName = "Preset",
                Kind = BlockParameterKind.Int32,
                DefaultValue = 1000,
                Minimum = 0,
                Maximum = 100000,
                Suffix = "ms"
            }
        };

        public string TypeId => "TON";
        public string DisplayName => "TON Timer";
        public string Category => "Timers";

        public IReadOnlyList<IPort> Ports { get; } = new List<IPort>
        {
            new PortDefinition(PortNames.TimerInput, PortDirection.Input, SimulationDataType.Bool),
            new PortDefinition(PortNames.BoolOutput, PortDirection.Output, SimulationDataType.Bool)
        };

        public IReadOnlyList<BlockParameterDescriptor> Parameters => TimerParameters;

        public void Execute(IExecutionContext context)
        {
            var input = context.ReadInput(PortNames.TimerInput)?.AsBool() ?? false;
            var preset = context.ReadParameter("TimerPresetMs", 1000);
            var state = context.State.GetOrCreate<TimerState>("TimerState");

            if (input)
            {
                state.AccumulatorMs += (int)context.Elapsed.TotalMilliseconds;
                if (state.AccumulatorMs >= preset)
                    state.Output = true;
            }
            else
            {
                state.AccumulatorMs = 0;
                state.Output = false;
            }

            state.LastInput = input;
            context.WriteOutput(PortNames.BoolOutput, SimulationValue.Bool(state.Output));
        }
    }

    public sealed class TofBlock : IFunctionBlock
    {
        public string TypeId => "TOF";
        public string DisplayName => "TOF Timer";
        public string Category => "Timers";

        public IReadOnlyList<IPort> Ports { get; } = new List<IPort>
        {
            new PortDefinition(PortNames.TimerInput, PortDirection.Input, SimulationDataType.Bool),
            new PortDefinition(PortNames.BoolOutput, PortDirection.Output, SimulationDataType.Bool)
        };

        public IReadOnlyList<BlockParameterDescriptor> Parameters => TonBlock.TimerParameters;

        public void Execute(IExecutionContext context)
        {
            var input = context.ReadInput(PortNames.TimerInput)?.AsBool() ?? false;
            var preset = context.ReadParameter("TimerPresetMs", 1000);
            var state = context.State.GetOrCreate<TimerState>("TimerState");

            if (input)
            {
                state.AccumulatorMs = 0;
                state.Output = true;
            }
            else if (state.Output)
            {
                state.AccumulatorMs += (int)context.Elapsed.TotalMilliseconds;
                if (state.AccumulatorMs >= preset)
                {
                    state.Output = false;
                    state.AccumulatorMs = 0;
                }
            }

            state.LastInput = input;
            context.WriteOutput(PortNames.BoolOutput, SimulationValue.Bool(state.Output));
        }
    }

    public sealed class TpBlock : IFunctionBlock
    {
        public string TypeId => "TP";
        public string DisplayName => "TP Timer";
        public string Category => "Timers";

        public IReadOnlyList<IPort> Ports { get; } = new List<IPort>
        {
            new PortDefinition(PortNames.TimerInput, PortDirection.Input, SimulationDataType.Bool),
            new PortDefinition(PortNames.BoolOutput, PortDirection.Output, SimulationDataType.Bool)
        };

        public IReadOnlyList<BlockParameterDescriptor> Parameters => TonBlock.TimerParameters;

        public void Execute(IExecutionContext context)
        {
            var input = context.ReadInput(PortNames.TimerInput)?.AsBool() ?? false;
            var preset = context.ReadParameter("TimerPresetMs", 1000);
            var state = context.State.GetOrCreate<TimerState>("TimerState");

            var risingEdge = input && !state.LastInput;
            if (risingEdge)
            {
                state.AccumulatorMs = 0;
                state.Output = true;
            }

            if (state.Output)
            {
                state.AccumulatorMs += (int)context.Elapsed.TotalMilliseconds;
                if (state.AccumulatorMs >= preset)
                    state.Output = false;
            }

            state.LastInput = input;
            context.WriteOutput(PortNames.BoolOutput, SimulationValue.Bool(state.Output));
        }
    }

    internal sealed class TimerState
    {
        public int AccumulatorMs { get; set; }
        public bool LastInput { get; set; }
        public bool Output { get; set; }
    }
}
