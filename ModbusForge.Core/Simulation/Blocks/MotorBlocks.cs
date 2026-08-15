using System.Collections.Generic;
using ModbusForge.Core.Simulation.Core;

namespace ModbusForge.Core.Simulation.Blocks
{
    /// <summary>
    /// Direct-on-line motor starter block with start/stop control and pickup delay.
    /// Stop is a normal stop command: it de-energises the starter without asserting
    /// a fault, so routine stop/start cycles do not trip alarm logic.
    /// </summary>
    public sealed class MotorDolBlock : IFunctionBlock
    {
        public string TypeId => "MotorDol";
        public string DisplayName => "DOL Motor";
        public string Category => "Valves & Motors";

        public IReadOnlyList<IPort> Ports { get; } = new List<IPort>
        {
            new PortDefinition("Start", PortDirection.Input, SimulationDataType.Bool),
            new PortDefinition("Stop", PortDirection.Input, SimulationDataType.Bool),
            new PortDefinition("Output", PortDirection.Output, SimulationDataType.Bool)
        };

        public IReadOnlyList<BlockParameterDescriptor> Parameters { get; } = new[]
        {
            new BlockParameterDescriptor
            {
                Name = "MotorDolRunDelayMs",
                DisplayName = "Run delay",
                Kind = BlockParameterKind.Int32,
                DefaultValue = 100,
                Minimum = 0,
                Maximum = 100000,
                Suffix = "ms"
            }
        };

        public void Execute(IExecutionContext context)
        {
            var start = context.ReadInput("Start")?.AsBool() ?? false;
            var stop = context.ReadInput("Stop")?.AsBool() ?? false;
            var runDelayMs = context.ReadParameter("MotorDolRunDelayMs", 100);

            var state = context.State.GetOrCreate<MotorDolState>(nameof(MotorDolState));

            if (stop)
            {
                // Normal stop: open the sealed contactor, no fault.
                state.Sealed = false;
                state.Running = false;
                state.DelayAccumulatorMs = 0;
            }
            else if (start)
            {
                state.Sealed = true;
            }

            if (state.Sealed && !state.Running)
            {
                state.DelayAccumulatorMs += context.Elapsed.TotalMilliseconds;

                if (state.DelayAccumulatorMs >= runDelayMs)
                {
                    state.Running = true;
                }
            }
            else if (!state.Sealed)
            {
                state.Running = false;
                state.DelayAccumulatorMs = 0;
            }

            context.WriteOutput("Output", SimulationValue.Bool(state.Running));
        }

        private sealed class MotorDolState
        {
            public bool Sealed { get; set; }
            public bool Running { get; set; }
            public double DelayAccumulatorMs { get; set; }
        }
    }
}
