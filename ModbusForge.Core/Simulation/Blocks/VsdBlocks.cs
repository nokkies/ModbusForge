using System;
using System.Collections.Generic;
using ModbusForge.Core.Simulation.Core;

namespace ModbusForge.Core.Simulation.Blocks
{
    /// <summary>
    /// Variable speed drive block with ramped speed reference and feedback.
    /// </summary>
    public sealed class VsdBlock : IFunctionBlock
    {
        public string TypeId => "Vsd";
        public string DisplayName => "VSD";
        public string Category => "Valves & Motors";

        public IReadOnlyList<IPort> Ports { get; } = new List<IPort>
        {
            new PortDefinition("Run", PortDirection.Input, SimulationDataType.Bool),
            new PortDefinition("SpeedReference", PortDirection.Input, SimulationDataType.Real),
            new PortDefinition("Output", PortDirection.Output, SimulationDataType.Bool),
            new PortDefinition("SpeedFeedback", PortDirection.Output, SimulationDataType.Real),
            new PortDefinition("AtSpeed", PortDirection.Output, SimulationDataType.Bool)
        };

        public void Execute(IExecutionContext context)
        {
            var run = context.ReadInput("Run")?.AsBool() ?? false;
            var speedReference = context.ReadInput("SpeedReference")?.AsReal() ?? 0.0;

            var maxSpeed = context.ReadParameter("VsdMaxSpeed", 100.0);
            var rampUpMs = context.ReadParameter("VsdRampUpMs", 2000);
            var rampDownMs = context.ReadParameter("VsdRampDownMs", 2000);
            var atSpeedTolerance = context.ReadParameter("VsdAtSpeedTolerance", 2.0);

            var state = context.State.GetOrCreate<VsdState>(nameof(VsdState));

            var targetSpeed = run ? Clamp(speedReference, 0, maxSpeed) : 0;

            if (state.CurrentSpeed < targetSpeed)
            {
                if (rampUpMs > 0)
                {
                    state.CurrentSpeed += maxSpeed * context.Elapsed.TotalMilliseconds / rampUpMs;
                }
                else
                {
                    state.CurrentSpeed = targetSpeed;
                }

                if (state.CurrentSpeed > targetSpeed)
                    state.CurrentSpeed = targetSpeed;
            }
            else if (state.CurrentSpeed > targetSpeed)
            {
                if (rampDownMs > 0)
                {
                    state.CurrentSpeed -= maxSpeed * context.Elapsed.TotalMilliseconds / rampDownMs;
                }
                else
                {
                    state.CurrentSpeed = targetSpeed;
                }

                if (state.CurrentSpeed < targetSpeed)
                    state.CurrentSpeed = targetSpeed;
            }

            var atSpeed = Math.Abs(state.CurrentSpeed - targetSpeed) <= atSpeedTolerance;

            context.WriteOutput("Output", SimulationValue.Bool(run));
            context.WriteOutput("SpeedFeedback", SimulationValue.Real(state.CurrentSpeed));
            context.WriteOutput("AtSpeed", SimulationValue.Bool(atSpeed));
        }

        private static double Clamp(double value, double min, double max)
        {
            return value < min ? min : value > max ? max : value;
        }

        private sealed class VsdState
        {
            public double CurrentSpeed { get; set; }
            public double TargetSpeed { get; set; }
            public bool LastRun { get; set; }
        }
    }
}
