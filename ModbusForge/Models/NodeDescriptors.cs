using System;
using System.Collections.Generic;
using System.Windows.Media;

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
        public required Color HeaderColor { get; init; }
        public string Icon { get; init; } = "?";
        public bool ShowInPalette { get; init; } = true;
        public bool IsInput { get; init; }
        public bool IsOutput { get; init; }
        public bool HasSecondInput { get; init; }
        public bool HasParameters { get; init; }
        public bool HasSetDominant { get; init; }
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
            Add(PlcElementType.Input, "Input", "IN", "Input", "I/O", Color.FromRgb(76, 175, 80), "?",
                showInPalette: false, isInput: true);
            Add(PlcElementType.Output, "Output", "OUT", "Output", "I/O", Color.FromRgb(255, 87, 34), "?",
                showInPalette: false, isOutput: true);
            Add(PlcElementType.InputBool, "InputBool", "IN BOOL", "Input BOOL", "I/O", Color.FromRgb(76, 175, 80), "B",
                isInput: true);
            Add(PlcElementType.InputInt, "InputInt", "IN INT", "Input INT", "I/O", Color.FromRgb(76, 175, 80), "I",
                isInput: true);
            Add(PlcElementType.OutputBool, "OutputBool", "OUT BOOL", "Output BOOL", "I/O", Color.FromRgb(255, 87, 34), "B",
                isOutput: true);
            Add(PlcElementType.OutputInt, "OutputInt", "OUT INT", "Output INT", "I/O", Color.FromRgb(255, 87, 34), "I",
                isOutput: true);

            // Logic
            Add(PlcElementType.NOT, "NOT", "NOT", "NOT Gate", "Logic Gates", Color.FromRgb(156, 39, 176), "NOT");
            Add(PlcElementType.AND, "AND", "AND", "AND Gate", "Logic Gates", Color.FromRgb(33, 150, 243), "AND",
                hasSecondInput: true);
            Add(PlcElementType.OR, "OR", "OR", "OR Gate", "Logic Gates", Color.FromRgb(255, 152, 0), "OR",
                hasSecondInput: true);
            Add(PlcElementType.RS, "RS", "RS Latch", "RS Latch", "Logic Gates", Color.FromRgb(244, 67, 54), "RS",
                hasSecondInput: true, hasSetDominant: true);

            // Timers
            Add(PlcElementType.TON, "TON", "TON", "TON Timer", "Timers", Color.FromRgb(255, 193, 7), "TON",
                hasParameters: true,
                displayNameFormatter: n => $"TON ({n.TimerPresetMs}ms)",
                parameterDisplayFormatter: n => $"{n.TimerPresetMs}ms");
            Add(PlcElementType.TOF, "TOF", "TOF", "TOF Timer", "Timers", Color.FromRgb(0, 150, 136), "TOF",
                hasParameters: true,
                displayNameFormatter: n => $"TOF ({n.TimerPresetMs}ms)",
                parameterDisplayFormatter: n => $"{n.TimerPresetMs}ms");
            Add(PlcElementType.TP, "TP", "TP", "TP Timer", "Timers", Color.FromRgb(96, 125, 139), "TP",
                hasParameters: true,
                displayNameFormatter: n => $"TP ({n.TimerPresetMs}ms)",
                parameterDisplayFormatter: n => $"{n.TimerPresetMs}ms");

            // Counters
            Add(PlcElementType.CTU, "CTU", "CTU", "CTU Counter", "Counters", Color.FromRgb(139, 195, 74), "CTU",
                hasParameters: true,
                displayNameFormatter: n => $"CTU ({n.CounterPreset})",
                parameterDisplayFormatter: n => $"Preset: {n.CounterPreset}");
            Add(PlcElementType.CTD, "CTD", "CTD", "CTD Counter", "Counters", Color.FromRgb(205, 220, 57), "CTD",
                hasParameters: true,
                displayNameFormatter: n => $"CTD ({n.CounterPreset})",
                parameterDisplayFormatter: n => $"Preset: {n.CounterPreset}");
            Add(PlcElementType.CTC, "CTC", "CTC", "CTC Counter", "Counters", Color.FromRgb(255, 235, 59), "CTC",
                hasSecondInput: true, hasParameters: true,
                displayNameFormatter: n => $"CTC ({n.CounterPreset})",
                parameterDisplayFormatter: n => $"Preset: {n.CounterPreset}");

            // Comparators
            Add(PlcElementType.COMPARE_EQ, "COMPARE_EQ", "EQ", "Equal (==)", "Comparators", Color.FromRgb(255, 87, 34), "==",
                hasSecondInput: true, hasParameters: true,
                parameterDisplayFormatter: n => $"Value: {n.CompareValue}");
            Add(PlcElementType.COMPARE_NE, "COMPARE_NE", "NE", "Not Equal (!=)", "Comparators", Color.FromRgb(255, 87, 34), "!=",
                hasSecondInput: true, hasParameters: true,
                parameterDisplayFormatter: n => $"Value: {n.CompareValue}");
            Add(PlcElementType.COMPARE_GT, "COMPARE_GT", "GT", "Greater Than (>)", "Comparators", Color.FromRgb(233, 30, 99), ">",
                hasSecondInput: true, hasParameters: true,
                parameterDisplayFormatter: n => $"Value: {n.CompareValue}");
            Add(PlcElementType.COMPARE_LT, "COMPARE_LT", "LT", "Less Than (<)", "Comparators", Color.FromRgb(233, 30, 99), "<",
                hasSecondInput: true, hasParameters: true,
                parameterDisplayFormatter: n => $"Value: {n.CompareValue}");
            Add(PlcElementType.COMPARE_GE, "COMPARE_GE", "GE", "Greater Equal (>=)", "Comparators", Color.FromRgb(156, 39, 176), ">=",
                hasSecondInput: true, hasParameters: true,
                parameterDisplayFormatter: n => $"Value: {n.CompareValue}");
            Add(PlcElementType.COMPARE_LE, "COMPARE_LE", "LE", "Less Equal (<=)", "Comparators", Color.FromRgb(156, 39, 176), "<=",
                hasSecondInput: true, hasParameters: true,
                parameterDisplayFormatter: n => $"Value: {n.CompareValue}");

            // Math
            Add(PlcElementType.MATH_ADD, "MATH_ADD", "ADD", "Add (+)", "Math Operations", Color.FromRgb(63, 81, 181), "+",
                hasSecondInput: true, hasParameters: true,
                parameterDisplayFormatter: n => $"Const: {n.CompareValue}");
            Add(PlcElementType.MATH_SUB, "MATH_SUB", "SUB", "Subtract (-)", "Math Operations", Color.FromRgb(63, 81, 181), "-",
                hasSecondInput: true, hasParameters: true,
                parameterDisplayFormatter: n => $"Const: {n.CompareValue}");
            Add(PlcElementType.MATH_MUL, "MATH_MUL", "MUL", "Multiply (*)", "Math Operations", Color.FromRgb(121, 85, 72), "x",
                hasSecondInput: true, hasParameters: true,
                parameterDisplayFormatter: n => $"Const: {n.CompareValue}");
            Add(PlcElementType.MATH_DIV, "MATH_DIV", "DIV", "Divide (/)", "Math Operations", Color.FromRgb(121, 85, 72), "/",
                hasSecondInput: true, hasParameters: true,
                parameterDisplayFormatter: n => $"Const: {n.CompareValue}");

            // Sources
            Add(PlcElementType.SignalGenerator, "SignalGenerator", "SignalGen", "Signal Generator", "Sources", Color.FromRgb(141, 110, 189), "SIG",
                hasSecondInput: true,
                displayNameFormatter: n => $"SignalGen ({n.Waveform}, {n.PeriodMs}ms)",
                parameterDisplayFormatter: n => $"{n.Waveform}: H={n.Amplitude}, T={n.PeriodMs}ms");
        }

        private static void Add(
            PlcElementType elementType,
            string typeId,
            string displayName,
            string paletteName,
            string category,
            Color headerColor,
            string icon,
            bool showInPalette = true,
            bool isInput = false,
            bool isOutput = false,
            bool hasSecondInput = false,
            bool hasParameters = false,
            bool hasSetDominant = false,
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
                DisplayNameFormatter = displayNameFormatter,
                ParameterDisplayFormatter = parameterDisplayFormatter
            };
        }

        public static NodeDescriptor Get(PlcElementType elementType)
        {
            return Descriptors.TryGetValue(elementType, out var descriptor)
                ? descriptor
                : Descriptors[PlcElementType.Input];
        }

        public static IReadOnlyCollection<NodeDescriptor> All => Descriptors.Values;
    }
}
