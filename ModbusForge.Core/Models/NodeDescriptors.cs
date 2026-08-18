using System;
using System.Collections.Generic;

namespace ModbusForge.Models
{
    /// <summary>
    /// Display metadata for a single PLC element / function block type.
    /// This replaces the scattered switches in <see cref="VisualNode"/> and the UI code-behind.
    /// </summary>
    public sealed class NodeDescriptor
    {
        public required PlcElementType ElementType { get; init; }
        public required string TypeId { get; init; }
        public required string DisplayName { get; init; }
        public required string PaletteName { get; init; }
        public required string Category { get; init; }
        public required RgbColor HeaderColor { get; init; }
        public string Icon { get; init; } = "?";
        public bool ShowInPalette { get; init; } = true;
        public bool IsInput { get; init; }
        public bool IsOutput { get; init; }
        public bool HasSecondInput { get; init; }
        public bool HasParameters { get; init; }
        public bool HasSetDominant { get; init; }

        /// <summary>
        /// The node exposes (and the simulation honors) a Modbus address binding for its
        /// first input slot. Types without this flag are driven by wires only, so their
        /// default (unedited) address references stay inert.
        /// </summary>
        public bool HasInput1Address { get; init; }

        /// <summary>
        /// The node exposes a Modbus address binding for its second input slot.
        /// </summary>
        public bool HasInput2Address { get; init; }

        /// <summary>
        /// The node exposes a Modbus address binding for its primary output.
        /// </summary>
        public bool HasOutputAddress { get; init; }
        public Func<VisualNode, string>? DisplayNameFormatter { get; init; }
        public Func<VisualNode, string>? ParameterDisplayFormatter { get; init; }

        public bool IsIo => IsInput || IsOutput;
        public bool HasFooter => HasParameters || HasSetDominant;

        public string GetDisplayName(VisualNode node) => DisplayNameFormatter?.Invoke(node) ?? DisplayName;

        public string GetParameterDisplay(VisualNode node) => ParameterDisplayFormatter?.Invoke(node) ?? string.Empty;
    }

    /// <summary>
    /// Central registry of display metadata for all known node types.
    /// </summary>
    public static class NodeDescriptors
    {
        private static readonly Dictionary<PlcElementType, NodeDescriptor> Descriptors = new();

        static NodeDescriptors()
        {
            // I/O
            Add(PlcElementType.Input, "Input", "IN", "Input", "I/O", RgbColor.FromRgb(76, 175, 80), "?",
                showInPalette: false, isInput: true, hasInput1Address: true);
            Add(PlcElementType.Output, "Output", "OUT", "Output", "I/O", RgbColor.FromRgb(255, 87, 34), "?",
                showInPalette: false, isOutput: true, hasOutputAddress: true);
            Add(PlcElementType.InputBool, "InputBool", "IN BOOL", "Input BOOL", "I/O", RgbColor.FromRgb(76, 175, 80), "B",
                isInput: true, hasInput1Address: true);
            Add(PlcElementType.InputInt, "InputInt", "IN INT", "Input INT", "I/O", RgbColor.FromRgb(76, 175, 80), "I",
                isInput: true, hasInput1Address: true);
            Add(PlcElementType.OutputBool, "OutputBool", "OUT BOOL", "Output BOOL", "I/O", RgbColor.FromRgb(255, 87, 34), "B",
                isOutput: true, hasOutputAddress: true);
            Add(PlcElementType.OutputInt, "OutputInt", "OUT INT", "Output INT", "I/O", RgbColor.FromRgb(255, 87, 34), "I",
                isOutput: true, hasOutputAddress: true);

            // Logic
            Add(PlcElementType.NOT, "NOT", "NOT", "NOT Gate", "Logic Gates", RgbColor.FromRgb(156, 39, 176), "NOT");
            Add(PlcElementType.AND, "AND", "AND", "AND Gate", "Logic Gates", RgbColor.FromRgb(33, 150, 243), "AND",
                hasSecondInput: true);
            Add(PlcElementType.OR, "OR", "OR", "OR Gate", "Logic Gates", RgbColor.FromRgb(255, 152, 0), "OR",
                hasSecondInput: true);
            Add(PlcElementType.RS, "RS", "RS Latch", "RS Latch", "Logic Gates", RgbColor.FromRgb(244, 67, 54), "RS",
                hasSecondInput: true, hasSetDominant: true);

            // Timers
            Add(PlcElementType.TON, "TON", "TON", "TON Timer", "Timers", RgbColor.FromRgb(255, 193, 7), "TON",
                hasParameters: true,
                displayNameFormatter: n => $"TON ({n.TimerPresetMs}ms)",
                parameterDisplayFormatter: n => $"{n.TimerPresetMs}ms");
            Add(PlcElementType.TOF, "TOF", "TOF", "TOF Timer", "Timers", RgbColor.FromRgb(0, 150, 136), "TOF",
                hasParameters: true,
                displayNameFormatter: n => $"TOF ({n.TimerPresetMs}ms)",
                parameterDisplayFormatter: n => $"{n.TimerPresetMs}ms");
            Add(PlcElementType.TP, "TP", "TP", "TP Timer", "Timers", RgbColor.FromRgb(96, 125, 139), "TP",
                hasParameters: true,
                displayNameFormatter: n => $"TP ({n.TimerPresetMs}ms)",
                parameterDisplayFormatter: n => $"{n.TimerPresetMs}ms");

            // Counters
            Add(PlcElementType.CTU, "CTU", "CTU", "CTU Counter", "Counters", RgbColor.FromRgb(139, 195, 74), "CTU",
                hasParameters: true,
                displayNameFormatter: n => $"CTU ({n.CounterPreset})",
                parameterDisplayFormatter: n => $"Preset: {n.CounterPreset}");
            Add(PlcElementType.CTD, "CTD", "CTD", "CTD Counter", "Counters", RgbColor.FromRgb(205, 220, 57), "CTD",
                hasParameters: true,
                displayNameFormatter: n => $"CTD ({n.CounterPreset})",
                parameterDisplayFormatter: n => $"Preset: {n.CounterPreset}");
            Add(PlcElementType.CTC, "CTC", "CTC", "CTC Counter", "Counters", RgbColor.FromRgb(255, 235, 59), "CTC",
                hasSecondInput: true, hasParameters: true,
                displayNameFormatter: n => $"CTC ({n.CounterPreset})",
                parameterDisplayFormatter: n => $"Preset: {n.CounterPreset}");

            // Comparators (the second input can be a wire or a bound Modbus address)
            Add(PlcElementType.COMPARE_EQ, "COMPARE_EQ", "EQ", "Equal (==)", "Comparators", RgbColor.FromRgb(255, 87, 34), "==",
                hasSecondInput: true, hasParameters: true, hasInput1Address: true, hasInput2Address: true,
                parameterDisplayFormatter: n => $"Value: {n.CompareValue}");
            Add(PlcElementType.COMPARE_NE, "COMPARE_NE", "NE", "Not Equal (!=)", "Comparators", RgbColor.FromRgb(255, 87, 34), "!=",
                hasSecondInput: true, hasParameters: true, hasInput1Address: true, hasInput2Address: true,
                parameterDisplayFormatter: n => $"Value: {n.CompareValue}");
            Add(PlcElementType.COMPARE_GT, "COMPARE_GT", "GT", "Greater Than (>)", "Comparators", RgbColor.FromRgb(233, 30, 99), ">",
                hasSecondInput: true, hasParameters: true, hasInput1Address: true, hasInput2Address: true,
                parameterDisplayFormatter: n => $"Value: {n.CompareValue}");
            Add(PlcElementType.COMPARE_LT, "COMPARE_LT", "LT", "Less Than (<)", "Comparators", RgbColor.FromRgb(233, 30, 99), "<",
                hasSecondInput: true, hasParameters: true, hasInput1Address: true, hasInput2Address: true,
                parameterDisplayFormatter: n => $"Value: {n.CompareValue}");
            Add(PlcElementType.COMPARE_GE, "COMPARE_GE", "GE", "Greater Equal (>=)", "Comparators", RgbColor.FromRgb(156, 39, 176), ">=",
                hasSecondInput: true, hasParameters: true, hasInput1Address: true, hasInput2Address: true,
                parameterDisplayFormatter: n => $"Value: {n.CompareValue}");
            Add(PlcElementType.COMPARE_LE, "COMPARE_LE", "LE", "Less Equal (<=)", "Comparators", RgbColor.FromRgb(156, 39, 176), "<=",
                hasSecondInput: true, hasParameters: true, hasInput1Address: true, hasInput2Address: true,
                parameterDisplayFormatter: n => $"Value: {n.CompareValue}");

            // Real (double) comparators
            Add(PlcElementType.COMPARE_EQ_REAL, "COMPARE_EQ_REAL", "EQ (R)", "Equal (==) (Real)", "Comparators (Real)", RgbColor.FromRgb(255, 87, 34), "==",
                hasSecondInput: true, hasParameters: true,
                parameterDisplayFormatter: n => $"Value: {n.CompareValue}");
            Add(PlcElementType.COMPARE_NE_REAL, "COMPARE_NE_REAL", "NE (R)", "Not Equal (!=) (Real)", "Comparators (Real)", RgbColor.FromRgb(255, 87, 34), "!=",
                hasSecondInput: true, hasParameters: true,
                parameterDisplayFormatter: n => $"Value: {n.CompareValue}");
            Add(PlcElementType.COMPARE_GT_REAL, "COMPARE_GT_REAL", "GT (R)", "Greater Than (>) (Real)", "Comparators (Real)", RgbColor.FromRgb(233, 30, 99), ">",
                hasSecondInput: true, hasParameters: true,
                parameterDisplayFormatter: n => $"Value: {n.CompareValue}");
            Add(PlcElementType.COMPARE_LT_REAL, "COMPARE_LT_REAL", "LT (R)", "Less Than (<) (Real)", "Comparators (Real)", RgbColor.FromRgb(233, 30, 99), "<",
                hasSecondInput: true, hasParameters: true,
                parameterDisplayFormatter: n => $"Value: {n.CompareValue}");
            Add(PlcElementType.COMPARE_GE_REAL, "COMPARE_GE_REAL", "GE (R)", "Greater Equal (>=) (Real)", "Comparators (Real)", RgbColor.FromRgb(156, 39, 176), ">=",
                hasSecondInput: true, hasParameters: true,
                parameterDisplayFormatter: n => $"Value: {n.CompareValue}");
            Add(PlcElementType.COMPARE_LE_REAL, "COMPARE_LE_REAL", "LE (R)", "Less Equal (<=) (Real)", "Comparators (Real)", RgbColor.FromRgb(156, 39, 176), "<=",
                hasSecondInput: true, hasParameters: true,
                parameterDisplayFormatter: n => $"Value: {n.CompareValue}");

            // Math (inputs/outputs can be wires or bound Modbus addresses)
            Add(PlcElementType.MATH_ADD, "MATH_ADD", "ADD", "Add (+)", "Math Operations", RgbColor.FromRgb(63, 81, 181), "+",
                hasSecondInput: true, hasParameters: true, hasInput1Address: true, hasInput2Address: true, hasOutputAddress: true,
                parameterDisplayFormatter: n => $"Const: {n.CompareValue}");
            Add(PlcElementType.MATH_SUB, "MATH_SUB", "SUB", "Subtract (-)", "Math Operations", RgbColor.FromRgb(63, 81, 181), "-",
                hasSecondInput: true, hasParameters: true, hasInput1Address: true, hasInput2Address: true, hasOutputAddress: true,
                parameterDisplayFormatter: n => $"Const: {n.CompareValue}");
            Add(PlcElementType.MATH_MUL, "MATH_MUL", "MUL", "Multiply (*)", "Math Operations", RgbColor.FromRgb(121, 85, 72), "x",
                hasSecondInput: true, hasParameters: true, hasInput1Address: true, hasInput2Address: true, hasOutputAddress: true,
                parameterDisplayFormatter: n => $"Const: {n.CompareValue}");
            Add(PlcElementType.MATH_DIV, "MATH_DIV", "DIV", "Divide (/)", "Math Operations", RgbColor.FromRgb(121, 85, 72), "/",
                hasSecondInput: true, hasParameters: true, hasInput1Address: true, hasInput2Address: true, hasOutputAddress: true,
                parameterDisplayFormatter: n => $"Const: {n.CompareValue}");

            // Real (double) math
            Add(PlcElementType.MATH_ADD_REAL, "MATH_ADD_REAL", "ADD (R)", "Add (+) (Real)", "Math Operations (Real)", RgbColor.FromRgb(63, 81, 181), "+",
                hasSecondInput: true, hasParameters: true, hasInput1Address: true, hasInput2Address: true, hasOutputAddress: true,
                parameterDisplayFormatter: n => $"Const: {n.CompareValueReal:0.##}");
            Add(PlcElementType.MATH_SUB_REAL, "MATH_SUB_REAL", "SUB (R)", "Subtract (-) (Real)", "Math Operations (Real)", RgbColor.FromRgb(63, 81, 181), "-",
                hasSecondInput: true, hasParameters: true, hasInput1Address: true, hasInput2Address: true, hasOutputAddress: true,
                parameterDisplayFormatter: n => $"Const: {n.CompareValueReal:0.##}");
            Add(PlcElementType.MATH_MUL_REAL, "MATH_MUL_REAL", "MUL (R)", "Multiply (*) (Real)", "Math Operations (Real)", RgbColor.FromRgb(121, 85, 72), "x",
                hasSecondInput: true, hasParameters: true, hasInput1Address: true, hasInput2Address: true, hasOutputAddress: true,
                parameterDisplayFormatter: n => $"Const: {n.CompareValueReal:0.##}");
            Add(PlcElementType.MATH_DIV_REAL, "MATH_DIV_REAL", "DIV (R)", "Divide (/) (Real)", "Math Operations (Real)", RgbColor.FromRgb(121, 85, 72), "/",
                hasSecondInput: true, hasParameters: true, hasInput1Address: true, hasInput2Address: true, hasOutputAddress: true,
                parameterDisplayFormatter: n => $"Const: {n.CompareValueReal:0.##}");

            // Industrial devices (command + feedback ports can be wires or bound addresses)
            Add(PlcElementType.Valve, "Valve", "Valve", "Valve", "Valves & Motors", RgbColor.FromRgb(69, 90, 100), "VLV",
                hasSecondInput: true, hasParameters: true, hasInput1Address: true, hasInput2Address: true, hasOutputAddress: true,
                displayNameFormatter: n => $"Valve ({n.ValveTravelTimeMs}ms)",
                parameterDisplayFormatter: n => $"Travel: {n.ValveTravelTimeMs}ms, Rest: {(n.ValveNormallyOpen ? "open" : "closed")}, {(n.ValveLatching ? "latching" : "spring-return")}");

            Add(PlcElementType.MotorDol, "MotorDol", "DOL Motor", "DOL Motor", "Valves & Motors", RgbColor.FromRgb(55, 71, 79), "MTR",
                hasSecondInput: true, hasParameters: true, hasInput1Address: true, hasInput2Address: true, hasOutputAddress: true,
                displayNameFormatter: n => $"DOL Motor ({n.MotorDolRunDelayMs}ms)",
                parameterDisplayFormatter: n => $"Run delay: {n.MotorDolRunDelayMs}ms");

            Add(PlcElementType.Vsd, "Vsd", "VSD", "VSD", "Valves & Motors", RgbColor.FromRgb(121, 85, 72), "VSD",
                hasSecondInput: true, hasParameters: true, hasInput1Address: true, hasInput2Address: true, hasOutputAddress: true,
                displayNameFormatter: n => $"VSD ({n.VsdMaxSpeed}max)",
                parameterDisplayFormatter: n => $"Max: {n.VsdMaxSpeed}, RUp: {n.VsdRampUpMs}ms, RDown: {n.VsdRampDownMs}ms, Tol: {n.VsdAtSpeedTolerance}");

            // Signal conditioning (analog in, analog/bool out; the input can be a
            // wire or a bound Modbus address)
            Add(PlcElementType.Scale, "Scale", "Scale", "Scale (LIN)", "Signal Conditioning", RgbColor.FromRgb(0, 131, 143), "LIN",
                hasParameters: true, hasInput1Address: true, hasOutputAddress: true,
                displayNameFormatter: n => $"Scale ({n.ScaleFromMin:0.##}..{n.ScaleFromMax:0.##} → {n.ScaleToMin:0.##}..{n.ScaleToMax:0.##})",
                parameterDisplayFormatter: n => $"{n.ScaleFromMin:0.##}..{n.ScaleFromMax:0.##} → {n.ScaleToMin:0.##}..{n.ScaleToMax:0.##}");
            Add(PlcElementType.EdgeDetect, "EdgeDetect", "Edge Detect", "Edge Detect", "Signal Conditioning", RgbColor.FromRgb(230, 74, 25), "EDGE",
                hasParameters: true, hasInput1Address: true, hasOutputAddress: true,
                displayNameFormatter: n => $"Edge ({n.EdgeDetectDirection})",
                parameterDisplayFormatter: n => $"Edge: {n.EdgeDetectDirection}");
            Add(PlcElementType.MovingAverage, "MovingAverage", "Moving Avg", "Moving Average", "Signal Conditioning", RgbColor.FromRgb(56, 142, 60), "MAVG",
                hasParameters: true, hasInput1Address: true, hasOutputAddress: true,
                displayNameFormatter: n => $"MAVG ({n.MaWindowSize})",
                parameterDisplayFormatter: n => $"Window: {n.MaWindowSize}");

            // Sources (the signal generator has no input ports - only an output).
            Add(PlcElementType.SignalGenerator, "SignalGenerator", "SignalGen", "Signal Generator", "Sources", RgbColor.FromRgb(141, 110, 189), "SIG",
                hasParameters: true,
                displayNameFormatter: n => $"SignalGen ({n.Waveform}, {n.PeriodMs}ms)",
                parameterDisplayFormatter: n => $"{n.Waveform}: H={n.Amplitude}, T={n.PeriodMs}ms");
            Add(PlcElementType.SignalGeneratorReal, "SignalGeneratorReal", "SignalGen (R)", "Signal Generator (Real)", "Sources", RgbColor.FromRgb(141, 110, 189), "SIG",
                hasParameters: true,
                displayNameFormatter: n => $"SignalGen (R) ({n.Waveform}, {n.PeriodMs}ms)",
                parameterDisplayFormatter: n => $"{n.Waveform}: H={n.Amplitude}, T={n.PeriodMs}ms");
        }

        private static void Add(
            PlcElementType elementType,
            string typeId,
            string displayName,
            string paletteName,
            string category,
            RgbColor headerColor,
            string icon,
            bool showInPalette = true,
            bool isInput = false,
            bool isOutput = false,
            bool hasSecondInput = false,
            bool hasParameters = false,
            bool hasSetDominant = false,
            bool hasInput1Address = false,
            bool hasInput2Address = false,
            bool hasOutputAddress = false,
            Func<VisualNode, string>? displayNameFormatter = null,
            Func<VisualNode, string>? parameterDisplayFormatter = null)
        {
            Descriptors[elementType] = new NodeDescriptor
            {
                ElementType = elementType,
                TypeId = typeId,
                DisplayName = displayName,
                PaletteName = paletteName,
                Category = category,
                HeaderColor = headerColor,
                Icon = icon,
                ShowInPalette = showInPalette,
                IsInput = isInput,
                IsOutput = isOutput,
                HasSecondInput = hasSecondInput,
                HasParameters = hasParameters,
                HasSetDominant = hasSetDominant,
                HasInput1Address = hasInput1Address,
                HasInput2Address = hasInput2Address,
                HasOutputAddress = hasOutputAddress,
                DisplayNameFormatter = displayNameFormatter,
                ParameterDisplayFormatter = parameterDisplayFormatter
            };
        }

        public static NodeDescriptor Get(PlcElementType elementType)
        {
            return TryGet(elementType, out var descriptor)
                ? descriptor
                : Descriptors[PlcElementType.Input];
        }

        /// <summary>
        /// Returns the descriptor for <paramref name="elementType"/>, or null when the type
        /// is unknown. Use this (instead of <see cref="Get"/>) when "no descriptor" must be
        /// distinguishable, e.g. when gating simulation behavior.
        /// </summary>
        public static bool TryGet(PlcElementType elementType, out NodeDescriptor descriptor)
        {
            return Descriptors.TryGetValue(elementType, out descriptor!);
        }

        public static IReadOnlyCollection<NodeDescriptor> All => Descriptors.Values;
    }
}
