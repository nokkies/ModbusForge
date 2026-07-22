using System;
using System.Collections.Generic;
using ModbusForge.Simulation.Core;

namespace ModbusForge.Simulation.Blocks
{
    public sealed class SignalGeneratorBlock : IFunctionBlock
    {
        public string TypeId => "SignalGenerator";
        public string DisplayName => "Signal Generator";
        public string Category => "Sources";

        public IReadOnlyList<IPort> Ports { get; } = new List<IPort>
        {
            new PortDefinition("Output", PortDirection.Output, SimulationDataType.Int32)
        };

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
            double value = waveform switch
            {
                "Sine" => amplitude * Math.Sin(2 * Math.PI * progress) + offset,
                "Triangle" => amplitude * (1.0 - 4.0 * Math.Abs(progress - 0.5)) + offset,
                "Square" => (progress < 0.5 ? amplitude : 0) + offset,
                "Ramp" or _ => amplitude * progress + offset
            };

            int intValue = (int)Math.Round(value);
            context.WriteOutput("Output", SimulationValue.Int32(intValue));
        }

        internal sealed class SignalGeneratorState
        {
            public int AccumulatorMs { get; set; }
        }
    }
}
