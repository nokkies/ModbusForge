using System.Collections.Generic;
using ModbusForge.Core.Simulation.Core;

namespace ModbusForge.Core.Simulation.Blocks
{
    /// <summary>
    /// Direct-on-line motor starter block with start/stop control and pickup delay.
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
            new PortDefinition("Output", PortDirection.Output, SimulationDataType.Bool),
            new PortDefinition("Fault", PortDirection.Output, SimulationDataType.Bool)
        };

        public void Execute(IExecutionContext context)
        {
            var start = context.ReadInput("Start")?.AsBool() ?? false;
            var stop = context.ReadInput("Stop")?.AsBool() ?? false;
            var runDelayMs = context.ReadParameter("MotorDolRunDelayMs", 100);

            var state = context.State.GetOrCreate<MotorDolState>(nameof(MotorDolState));

            if (stop)
            {
                state.Sealed = false;
                state.FaultActive = true;
                state.Running = false;
                state.DelayAccumulatorMs = 0;
            }
            else
            {
                if (start && !state.FaultActive)
                {
                    state.Sealed = true;
                }
                else if (start && state.FaultActive)
                {
                    state.FaultActive = false;
                    state.Sealed = false;
                }

                state.LastStart = start;
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
            context.WriteOutput("Fault", SimulationValue.Bool(state.FaultActive));
        }

        private sealed class MotorDolState
        {
            public bool Sealed { get; set; }
            public bool LastStart { get; set; }
            public bool Running { get; set; }
            public bool FaultActive { get; set; }
            public double DelayAccumulatorMs { get; set; }
        }
    }
}
