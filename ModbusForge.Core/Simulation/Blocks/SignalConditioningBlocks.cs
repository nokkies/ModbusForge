using System;
using System.Collections.Generic;
using ModbusForge.Core.Simulation.Core;

namespace ModbusForge.Core.Simulation.Blocks
{
    /// <summary>
    /// Linear scaling (IEC 61131-3 LIN): maps a raw analog value from one range to
    /// another, e.g. a 0..100 register count to 0..120 °C. The mapping is
    /// <c>toMin + (raw - fromMin) * (toMax - toMin) / (fromMax - fromMin)</c>;
    /// a degenerate source span (fromMin == fromMax) yields toMin, and out-of-range
    /// results are clamped to the target range unless "Clamp" is disabled.
    /// Non-finite input never produces NaN/Infinity downstream.
    /// </summary>
    public sealed class ScaleBlock : IFunctionBlock
    {
        public string TypeId => "Scale";
        public string DisplayName => "Scale";
        public string Category => "Signal Conditioning";

        public IReadOnlyList<IPort> Ports { get; } = new List<IPort>
        {
            new PortDefinition(PortNames.Value, PortDirection.Input, SimulationDataType.Real),
            new PortDefinition(PortNames.BoolOutput, PortDirection.Output, SimulationDataType.Real)
        };

        public IReadOnlyList<BlockParameterDescriptor> Parameters { get; } = new[]
        {
            new BlockParameterDescriptor
            {
                Name = "ScaleFromMin",
                DisplayName = "From Min",
                Kind = BlockParameterKind.Real,
                DefaultValue = 0.0
            },
            new BlockParameterDescriptor
            {
                Name = "ScaleFromMax",
                DisplayName = "From Max",
                Kind = BlockParameterKind.Real,
                DefaultValue = 100.0
            },
            new BlockParameterDescriptor
            {
                Name = "ScaleToMin",
                DisplayName = "To Min",
                Kind = BlockParameterKind.Real,
                DefaultValue = 0.0
            },
            new BlockParameterDescriptor
            {
                Name = "ScaleToMax",
                DisplayName = "To Max",
                Kind = BlockParameterKind.Real,
                DefaultValue = 100.0
            },
            new BlockParameterDescriptor
            {
                Name = "ScaleClamp",
                DisplayName = "Clamp",
                Kind = BlockParameterKind.Bool,
                DefaultValue = true
            }
        };

        public void Execute(IExecutionContext context)
        {
            var fromMin = context.ReadParameter("ScaleFromMin", 0.0);
            var fromMax = context.ReadParameter("ScaleFromMax", 100.0);
            var toMin = context.ReadParameter("ScaleToMin", 0.0);
            var toMax = context.ReadParameter("ScaleToMax", 100.0);
            var clamp = context.ReadParameter("ScaleClamp", true);

            var raw = context.ReadInput(PortNames.Value)?.AsReal() ?? 0.0;

            double result;
            var span = fromMax - fromMin;
            if (!double.IsFinite(raw) || span == 0.0)
            {
                // Degenerate mapping (or unusable input): settle on the target
                // minimum instead of producing NaN/Infinity.
                result = toMin;
            }
            else
            {
                result = toMin + (raw - fromMin) * (toMax - toMin) / span;
                if (clamp)
                {
                    // Supports inverted target ranges (e.g. 100..0) as well.
                    result = Math.Clamp(result, Math.Min(toMin, toMax), Math.Max(toMin, toMax));
                }
                if (!double.IsFinite(result))
                    result = toMin;
            }

            context.WriteOutput(PortNames.BoolOutput, SimulationValue.Real(result));
        }
    }

    /// <summary>
    /// Edge detector: emits a one-cycle pulse on the selected transition of its
    /// boolean input (rising by default). The first scan only records the input
    /// and never pulses, so a block started while the input is already active
    /// does not fire spuriously.
    /// </summary>
    public sealed class EdgeDetectBlock : IFunctionBlock
    {
        public const string Rising = "Rising";
        public const string Falling = "Falling";

        public string TypeId => "EdgeDetect";
        public string DisplayName => "Edge Detect";
        public string Category => "Signal Conditioning";

        public IReadOnlyList<IPort> Ports { get; } = new List<IPort>
        {
            new PortDefinition(PortNames.TimerInput, PortDirection.Input, SimulationDataType.Bool),
            new PortDefinition(PortNames.BoolOutput, PortDirection.Output, SimulationDataType.Bool)
        };

        public IReadOnlyList<BlockParameterDescriptor> Parameters { get; } = new[]
        {
            new BlockParameterDescriptor
            {
                Name = "EdgeDetectDirection",
                DisplayName = "Edge",
                Kind = BlockParameterKind.Choice,
                DefaultValue = Rising,
                Options = new[] { Rising, Falling }
            }
        };

        public void Execute(IExecutionContext context)
        {
            var input = context.ReadInput(PortNames.TimerInput)?.AsBool() ?? false;
            var direction = context.ReadParameter("EdgeDetectDirection", Rising);
            var falling = string.Equals(direction, Falling, StringComparison.OrdinalIgnoreCase);

            var hasLast = context.State.TryGet<bool>(StateHasLast, out var _);
            var lastInput = context.State.TryGet<bool>(StateLastInput, out var stored) ? stored : false;

            var pulse = hasLast && (falling ? lastInput && !input : !lastInput && input);

            context.State.Set(StateLastInput, input);
            context.State.Set(StateHasLast, true);

            context.WriteOutput(PortNames.BoolOutput, SimulationValue.Bool(pulse));
        }

        private const string StateLastInput = "LastInput";
        private const string StateHasLast = "HasLast";
    }

    /// <summary>
    /// Moving average over the last N samples (IEC 61131-3 MOVAVG style): while
    /// the window fills, the output is the mean of the samples collected so far;
    /// once full, the oldest sample is replaced each cycle. Non-finite samples
    /// are skipped so a glitched register cannot poison the average.
    /// </summary>
    public sealed class MovingAverageBlock : IFunctionBlock
    {
        private const int MaxWindowSize = 1024;

        public string TypeId => "MovingAverage";
        public string DisplayName => "Moving Average";
        public string Category => "Signal Conditioning";

        public IReadOnlyList<IPort> Ports { get; } = new List<IPort>
        {
            new PortDefinition(PortNames.Value, PortDirection.Input, SimulationDataType.Real),
            new PortDefinition(PortNames.BoolOutput, PortDirection.Output, SimulationDataType.Real)
        };

        public IReadOnlyList<BlockParameterDescriptor> Parameters { get; } = new[]
        {
            new BlockParameterDescriptor
            {
                Name = "MaWindowSize",
                DisplayName = "Window",
                Kind = BlockParameterKind.Int32,
                DefaultValue = 10,
                Minimum = 1,
                Maximum = MaxWindowSize
            }
        };

        public void Execute(IExecutionContext context)
        {
            var window = (int)Math.Round(context.ReadParameter<double>("MaWindowSize", 10.0));
            if (window < 1) window = 1;
            if (window > MaxWindowSize) window = MaxWindowSize;

            var buffer = context.State.TryGet<double[]>(StateBuffer, out var storedBuffer)
                ? storedBuffer
                : null;
            if (buffer is null || buffer.Length != window)
            {
                // No buffer yet, or the window size changed: start fresh.
                buffer = new double[window];
                context.State.Set(StateBuffer, buffer);
                context.State.Set(StateCount, 0);
            }

            var count = context.State.TryGet<int>(StateCount, out var storedCount) ? storedCount : 0;

            var sample = context.ReadInput(PortNames.Value)?.AsReal() ?? 0.0;
            if (double.IsFinite(sample))
            {
                if (count < window)
                {
                    buffer[count] = sample;
                    count++;
                    context.State.Set(StateCount, count);
                }
                else
                {
                    var index = context.State.TryGet<int>(StateIndex, out var storedIndex) ? storedIndex : 0;
                    buffer[index] = sample;
                    context.State.Set(StateIndex, (index + 1) % window);
                }
            }

            double sum = 0.0;
            for (var i = 0; i < count; i++)
                sum += buffer[i];

            context.WriteOutput(PortNames.BoolOutput, SimulationValue.Real(count > 0 ? sum / count : 0.0));
        }

        private const string StateBuffer = "Buffer";
        private const string StateCount = "Count";
        private const string StateIndex = "Index";
    }
}
