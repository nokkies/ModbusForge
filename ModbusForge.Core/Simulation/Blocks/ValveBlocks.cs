using System;
using System.Collections.Generic;
using ModbusForge.Core.Simulation.Core;

namespace ModbusForge.Core.Simulation.Blocks
{
    /// <summary>
    /// Motorised valve block with open/close commands and configurable stroke time.
    ///
    /// Two rest behaviors, selected by the "Latching" parameter:
    /// - Latching (default, real motor valve): with no command the valve HOLDS its
    ///   last commanded position.
    /// - Non-latching (spring-return style): with no command the valve moves back to
    ///   its rest position ("Normally open").
    /// Simultaneous Open + Close commands latch a fault until the commands are resolved.
    /// </summary>
    public sealed class ValveBlock : IFunctionBlock
    {
        public string TypeId => "Valve";
        public string DisplayName => "Valve";
        public string Category => "Valves & Motors";

        public IReadOnlyList<IPort> Ports { get; } = new List<IPort>
        {
            new PortDefinition("OpenCmd", PortDirection.Input, SimulationDataType.Bool),
            new PortDefinition("CloseCmd", PortDirection.Input, SimulationDataType.Bool),
            new PortDefinition("Output", PortDirection.Output, SimulationDataType.Bool),
            new PortDefinition("Fault", PortDirection.Output, SimulationDataType.Bool)
        };

        public IReadOnlyList<BlockParameterDescriptor> Parameters { get; } = new[]
        {
            new BlockParameterDescriptor
            {
                Name = "ValveTravelTimeMs",
                DisplayName = "Travel",
                Kind = BlockParameterKind.Int32,
                DefaultValue = 5000,
                Minimum = 0,
                Maximum = 100000,
                Suffix = "ms"
            },
            new BlockParameterDescriptor
            {
                Name = "ValveNormallyOpen",
                DisplayName = "Rest open",
                Kind = BlockParameterKind.Bool,
                DefaultValue = false
            },
            new BlockParameterDescriptor
            {
                Name = "ValveLatching",
                DisplayName = "Latching",
                Kind = BlockParameterKind.Bool,
                DefaultValue = true
            }
        };

        public void Execute(IExecutionContext context)
        {
            var openCmd = context.ReadInput("OpenCmd")?.AsBool() ?? false;
            var closeCmd = context.ReadInput("CloseCmd")?.AsBool() ?? false;

            var travelTimeMs = context.ReadParameter("ValveTravelTimeMs", 5000);
            var normallyOpen = context.ReadParameter("ValveNormallyOpen", false);
            var latching = context.ReadParameter("ValveLatching", true);

            var state = context.State.GetOrCreate<ValveState>(nameof(ValveState));

            // Determine target open state and handle invalid simultaneous commands.
            bool? newTarget = null;
            if (openCmd && closeCmd)
            {
                state.FaultActive = true;
                // Keep previous target when both commands are active.
            }
            else
            {
                state.FaultActive = false;

                if (openCmd && !closeCmd)
                    newTarget = true;
                else if (closeCmd && !openCmd)
                    newTarget = false;
                else if (!latching)
                    // No command: spring-return to the rest position. Latching valves hold.
                    newTarget = normallyOpen;
            }

            if (newTarget.HasValue)
            {
                if (state.TargetOpen != newTarget.Value)
                {
                    state.TargetOpen = newTarget.Value;
                    // Start a new transition whenever the command changes.
                    state.InTransit = true;
                    state.AccumulatorMs = 0;
                }
            }

            // Execute the transition.
            if (state.InTransit)
            {
                state.AccumulatorMs += Math.Max(context.Elapsed.TotalMilliseconds, 0);

                if (state.AccumulatorMs >= travelTimeMs)
                {
                    state.CurrentOpen = state.TargetOpen;
                    state.InTransit = false;
                    state.AccumulatorMs = 0;
                }
            }

            context.WriteOutput("Output", SimulationValue.Bool(state.CurrentOpen));
            context.WriteOutput("Fault", SimulationValue.Bool(state.FaultActive));
        }

        private sealed class ValveState
        {
            public bool CurrentOpen { get; set; }
            public bool TargetOpen { get; set; }
            public bool InTransit { get; set; }
            public double AccumulatorMs { get; set; }
            public bool FaultActive { get; set; }
        }
    }
}
