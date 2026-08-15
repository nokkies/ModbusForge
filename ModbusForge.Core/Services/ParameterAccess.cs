using System;
using System.Collections.Generic;
using ModbusForge.Models;

namespace ModbusForge.Services
{
    /// <summary>
    /// Maps function-block parameter names to the backing properties on <see cref="VisualNode"/>.
    /// Used both by the simulation service (to populate engine parameters) and by the editor
    /// view model (to build data-driven parameter fields), so the two never drift apart.
    /// </summary>
    public static class ParameterAccess
    {
        private static readonly Dictionary<string, Func<VisualNode, object?>> Getters = new(StringComparer.Ordinal)
        {
            ["TimerPresetMs"] = n => n.TimerPresetMs,
            ["CounterPreset"] = n => n.CounterPreset,
            ["CompareValue"] = n => n.CompareValue,
            ["Constant"] = n => n.CompareValue,
            ["CompareValueReal"] = n => n.CompareValueReal,
            ["ConstantReal"] = n => n.CompareValueReal,
            ["SetDominant"] = n => n.SetDominant,
            ["Waveform"] = n => n.Waveform,
            ["PeriodMs"] = n => n.PeriodMs,
            ["Amplitude"] = n => n.Amplitude,
            ["Offset"] = n => n.Offset,
            ["ValveTravelTimeMs"] = n => n.ValveTravelTimeMs,
            ["ValveNormallyOpen"] = n => n.ValveNormallyOpen,
            ["ValveLatching"] = n => n.ValveLatching,
            ["MotorDolRunDelayMs"] = n => n.MotorDolRunDelayMs,
            ["VsdMaxSpeed"] = n => n.VsdMaxSpeed,
            ["VsdRampUpMs"] = n => n.VsdRampUpMs,
            ["VsdRampDownMs"] = n => n.VsdRampDownMs,
            ["VsdAtSpeedTolerance"] = n => n.VsdAtSpeedTolerance,
        };

        private static readonly Dictionary<string, Action<VisualNode, object?>> Setters = new(StringComparer.Ordinal)
        {
            ["TimerPresetMs"] = (n, v) => n.TimerPresetMs = ToInt(v),
            ["CounterPreset"] = (n, v) => n.CounterPreset = ToInt(v),
            ["CompareValue"] = (n, v) => n.CompareValue = ToInt(v),
            ["Constant"] = (n, v) => n.CompareValue = ToInt(v),
            ["CompareValueReal"] = (n, v) => n.CompareValueReal = ToDouble(v),
            ["ConstantReal"] = (n, v) => n.CompareValueReal = ToDouble(v),
            ["SetDominant"] = (n, v) => n.SetDominant = v is bool b && b,
            ["Waveform"] = (n, v) => n.Waveform = v as string ?? "Ramp",
            ["PeriodMs"] = (n, v) => n.PeriodMs = ToInt(v),
            ["Amplitude"] = (n, v) => n.Amplitude = ToDouble(v),
            ["Offset"] = (n, v) => n.Offset = ToDouble(v),
            ["ValveTravelTimeMs"] = (n, v) => n.ValveTravelTimeMs = ToInt(v),
            ["ValveNormallyOpen"] = (n, v) => n.ValveNormallyOpen = v is bool b && b,
            ["ValveLatching"] = (n, v) => n.ValveLatching = v is bool b && b,
            ["MotorDolRunDelayMs"] = (n, v) => n.MotorDolRunDelayMs = ToInt(v),
            ["VsdMaxSpeed"] = (n, v) => n.VsdMaxSpeed = ToDouble(v),
            ["VsdRampUpMs"] = (n, v) => n.VsdRampUpMs = ToInt(v),
            ["VsdRampDownMs"] = (n, v) => n.VsdRampDownMs = ToInt(v),
            ["VsdAtSpeedTolerance"] = (n, v) => n.VsdAtSpeedTolerance = ToDouble(v),
        };

        /// <summary>
        /// Returns the getter/setter pair for the given parameter name, or null when unknown.
        /// </summary>
        public static (Func<VisualNode, object?> Getter, Action<VisualNode, object?> Setter)? TryGet(string parameterName)
        {
            if (Getters.TryGetValue(parameterName, out var getter) && Setters.TryGetValue(parameterName, out var setter))
                return (getter, setter);

            return null;
        }

        private static int ToInt(object? value) => value switch
        {
            int i => i,
            double d => (int)Math.Round(d),
            IConvertible c => c.ToInt32(System.Globalization.CultureInfo.InvariantCulture),
            _ => 0
        };

        private static double ToDouble(object? value) => value switch
        {
            double d => d,
            int i => i,
            IConvertible c => c.ToDouble(System.Globalization.CultureInfo.InvariantCulture),
            _ => 0.0
        };
    }
}
