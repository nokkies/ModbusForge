using System;

namespace ModbusForge.Core.Simulation
{
    /// <summary>
    /// Pure waveform math shared by the simulation signal generator blocks and the
    /// application-level signal generator tab, so both produce identical shapes.
    /// </summary>
    public static class WaveformMath
    {
        /// <summary>
        /// Evaluates a waveform at the given phase of its period.
        /// </summary>
        /// <param name="waveform">One of <c>Ramp</c>, <c>Sine</c>, <c>Triangle</c>, <c>Square</c>.</param>
        /// <param name="amplitude">Peak amplitude (Ramp/Triangle/Square reach this value; Sine reaches ±this value).</param>
        /// <param name="offset">Constant added to the result.</param>
        /// <param name="progress">Phase within the period, normally in [0, 1).</param>
        public static double Evaluate(string? waveform, double amplitude, double offset, double progress)
        {
            return waveform switch
            {
                "Sine" => amplitude * Math.Sin(2 * Math.PI * progress) + offset,
                "Triangle" => amplitude * (1.0 - 4.0 * Math.Abs(progress - 0.5)) + offset,
                "Square" => (progress < 0.5 ? amplitude : 0) + offset,
                _ => amplitude * progress + offset
            };
        }
    }
}
