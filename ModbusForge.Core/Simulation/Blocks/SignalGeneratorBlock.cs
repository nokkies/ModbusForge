using System;
using System.Collections.Generic;
using ModbusForge.Core.Simulation.Core;

namespace ModbusForge.Core.Simulation.Blocks
{
    /// <summary>
    /// Shared implementation for the signal generator blocks (integer and real output variants).
    /// </summary>
    public abstract class SignalGeneratorBlockBase : IFunctionBlock
    {
        public static readonly IReadOnlyList<string> WaveformOptions = new[] { "Ramp", "Sine", "Triangle", "Square" };

        public abstract string TypeId { get; }
        public abstract string DisplayName { get; }
        public string Category => "Sources";

        protected abstract SimulationDataType OutputDataType { get; }

        public IReadOnlyList<IPort> Ports { get; }

        public IReadOnlyList<BlockParameterDescriptor> Parameters { get; } = new[]
        {
            new BlockParameterDescriptor
            {
                Name = "Waveform",
                DisplayName = "Waveform",
                Kind = BlockParameterKind.Choice,
                DefaultValue = "Ramp",
                Options = WaveformOptions
            },
            new BlockParameterDescriptor
            {
                Name = "PeriodMs",
                DisplayName = "Period",
                Kind = BlockParameterKind.Int32,
                DefaultValue = 1000,
                Minimum = 1,
                Maximum = 60000,
                Suffix = "ms"
            },
            new BlockParameterDescriptor
            {
                Name = "Amplitude",
                DisplayName = "Amplitude",
                Kind = BlockParameterKind.Real,
                DefaultValue = 100.0
            },
            new BlockParameterDescriptor
            {
                Name = "Offset",
                DisplayName = "Offset",
                Kind = BlockParameterKind.Real,
                DefaultValue = 0.0
            }
        };

        protected SignalGeneratorBlockBase()
        {
            Ports = new List<IPort>
            {
                new PortDefinition("Output", PortDirection.Output, OutputDataType)
            };
        }

        public void Execute(IExecutionContext context)
        {
            var waveform = context.ReadParameter("Waveform", "Ramp");
            var period = context.ReadParameter("PeriodMs", 1000);
            var amplitude = context.ReadParameter("Amplitude", 100.0);
            var offset = context.ReadParameter("Offset", 0.0);

            if (period <= 0)
                period = 1000;

            var state = context.State.GetOrCreate<SignalGeneratorState>("SignalGeneratorState");
            state.AccumulatorMs += (int)context.Elapsed.TotalMilliseconds;

            if (state.AccumulatorMs >= period)
            {
                state.AccumulatorMs %= period;
            }

            double progress = (double)state.AccumulatorMs / period;
            double value = WaveformMath.Evaluate(waveform, amplitude, offset, progress);

            var output = OutputDataType == SimulationDataType.Real
                ? SimulationValue.Real(value)
                : SimulationValue.Int32((int)Math.Round(value));

            context.WriteOutput("Output", output);
        }

        internal sealed class SignalGeneratorState
        {
            public int AccumulatorMs { get; set; }
        }
    }

    /// <summary>
    /// Signal generator with an integer (Int32) output.
    /// </summary>
    public sealed class SignalGeneratorBlock : SignalGeneratorBlockBase
    {
        public override string TypeId => "SignalGenerator";
        public override string DisplayName => "Signal Generator";
        protected override SimulationDataType OutputDataType => SimulationDataType.Int32;
    }

    /// <summary>
    /// Signal generator with a real (double) output, for feeding blocks that expect
    /// fractional values (e.g. the VSD SpeedReference port) without integer quantization.
    /// </summary>
    public sealed class SignalGeneratorRealBlock : SignalGeneratorBlockBase
    {
        public override string TypeId => "SignalGeneratorReal";
        public override string DisplayName => "Signal Generator (Real)";
        protected override SimulationDataType OutputDataType => SimulationDataType.Real;
    }
}
