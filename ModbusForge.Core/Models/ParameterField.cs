using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using ModbusForge.Core.Simulation.Core;

namespace ModbusForge.Models
{
    /// <summary>
    /// One editable parameter of a visual node, bridging a UI editor control and the
    /// node's backing property. Instances are created by the editor view model from the
    /// function block's declarative parameter list, so editors are data-driven.
    /// </summary>
    public sealed partial class ParameterField : ObservableObject
    {
        private readonly VisualNode _node;
        private readonly Action<VisualNode, object?>? _setter;
        private readonly Func<VisualNode, object?> _getter;

        public string Name { get; }
        public string DisplayName { get; }
        public BlockParameterKind Kind { get; }
        public IReadOnlyList<string>? Options { get; }
        public double? Minimum { get; }
        public double? Maximum { get; }
        public string? Suffix { get; }

        public bool IsNumeric => Kind is BlockParameterKind.Int32 or BlockParameterKind.Real;
        public bool IsInteger => Kind == BlockParameterKind.Int32;
        public bool IsReal => Kind == BlockParameterKind.Real;
        public bool IsBool => Kind == BlockParameterKind.Bool;
        public bool IsChoice => Kind == BlockParameterKind.Choice;

        /// <summary>
        /// NumericUpDown format: integers render without decimals, reals keep up to two.
        /// </summary>
        public string NumericFormat => IsReal ? "0.##" : "0";

        /// <summary>
        /// Editor bounds. Falls back to a generous symmetric range when the block's
        /// descriptor leaves a bound open, matching the limits the per-type editors used.
        /// </summary>
        public double EditorMinimum => Minimum ?? -100000;
        public double EditorMaximum => Maximum ?? 100000;

        /// <summary>Editor value for Int32/Real parameters.</summary>
        [ObservableProperty]
        private double _numeric;

        /// <summary>Editor value for Bool parameters.</summary>
        [ObservableProperty]
        private bool _flag;

        /// <summary>Editor value for Choice parameters.</summary>
        [ObservableProperty]
        private string _choice = string.Empty;

        public ParameterField(
            VisualNode node,
            BlockParameterDescriptor spec,
            Func<VisualNode, object?> getter,
            Action<VisualNode, object?>? setter)
        {
            _node = node ?? throw new ArgumentNullException(nameof(node));
            _getter = getter ?? throw new ArgumentNullException(nameof(getter));
            _setter = setter;

            Name = spec.Name;
            DisplayName = spec.DisplayName;
            Kind = spec.Kind;
            Options = spec.Options;
            Minimum = spec.Minimum;
            Maximum = spec.Maximum;
            Suffix = spec.Suffix;

            LoadFromNode();
        }

        /// <summary>
        /// Re-reads the current values from the node (e.g. after undo or program load).
        /// </summary>
        public void LoadFromNode()
        {
            var raw = _getter(_node);
            switch (Kind)
            {
                case BlockParameterKind.Int32:
                    Numeric = raw is int i ? i : Convert.ToDouble(raw, System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case BlockParameterKind.Real:
                    Numeric = Convert.ToDouble(raw, System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case BlockParameterKind.Bool:
                    Flag = raw is bool b && b;
                    break;
                case BlockParameterKind.Choice:
                    Choice = raw as string ?? string.Empty;
                    break;
            }
        }

        partial void OnNumericChanged(double value)
        {
            if (Minimum.HasValue && value < Minimum.Value)
                value = Minimum.Value;
            if (Maximum.HasValue && value > Maximum.Value)
                value = Maximum.Value;

            if (_setter != null)
                _setter(_node, Kind == BlockParameterKind.Int32 ? (object)Math.Round(value) : value);
        }

        partial void OnFlagChanged(bool value)
        {
            _setter?.Invoke(_node, value);
        }

        partial void OnChoiceChanged(string value)
        {
            _setter?.Invoke(_node, value);
        }
    }
}
