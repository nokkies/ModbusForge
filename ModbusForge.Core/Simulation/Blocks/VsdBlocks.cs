using System;
using System.Collections.Generic;
using ModbusForge.Core.Simulation.Core;

namespace ModbusForge.Core.Simulation.Blocks
{
    /// <summary>
    /// Variable speed drive block with ramped speed reference and feedback.
    /// "Running" is the drive command state (true while Run is active, including ramp),
    /// "SpeedFeedback" is the ramped speed and "AtSpeed" flags reference tracking.
    /// </summary>
    public sealed class VsdBlock : IFunctionBlock
    {
        public string TypeId => "Vsd";
        public string DisplayName => "VSD";
        public string Category => "Valves & Motors";

        public IReadOnlyList<IPort> Ports { get; } = new List<IPort>
        {
            new PortDefinition(PortNames.Run, PortDirection.Input, SimulationDataType.Bool),
            new PortDefinition(PortNames.SpeedReference, PortDirection.Input, SimulationDataType.Real),
            new PortDefinition(PortNames.VsdRunning, PortDirection.Output, SimulationDataType.Bool),
            new PortDefinition(PortNames.SpeedFeedback, PortDirection.Output, SimulationDataType.Real),
            new PortDefinition(PortNames.AtSpeed, PortDirection.Output, SimulationDataType.Bool)
        };

        public IReadOnlyList<BlockParameterDescriptor> Parameters { get; } = new[]
        {
            new BlockParameterDescriptor
            {
                Name = "VsdMaxSpeed",
                DisplayName = "Max speed",
                Kind = BlockParameterKind.Real,
                DefaultValue = 100.0,
                Minimum = 0.0,
                Maximum = 100000.0
            },
            new BlockParameterDescriptor
            {
                Name = "VsdRampUpMs",
                DisplayName = "Ramp up",
                Kind = BlockParameterKind.Int32,
                DefaultValue = 2000,
                Minimum = 0,
                Maximum = 100000,
                Suffix = "ms"
            },
            new BlockParameterDescriptor
            {
                Name = "VsdRampDownMs",
                DisplayName = "Ramp down",
                Kind = BlockParameterKind.Int32,
                DefaultValue = 2000,
                Minimum = 0,
                Maximum = 100000,
                Suffix = "ms"
            },
            new BlockParameterDescriptor
            {
                Name = "VsdAtSpeedTolerance",
                DisplayName = "Tolerance",
                Kind = BlockParameterKind.Real,
                DefaultValue = 2.0,
                Minimum = 0.0,
                Maximum = 100000.0
            }
        };

        public void Execute(IExecutionContext context)
        {
            var run = context.ReadInput(PortNames.Run)?.AsBool() ?? false;
            var speedReference = context.ReadInput(PortNames.SpeedReference)?.AsReal() ?? 0.0;

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

            context.WriteOutput(PortNames.VsdRunning, SimulationValue.Bool(run));
            context.WriteOutput(PortNames.SpeedFeedback, SimulationValue.Real(state.CurrentSpeed));
            context.WriteOutput(PortNames.AtSpeed, SimulationValue.Bool(atSpeed));
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
