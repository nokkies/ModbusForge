using ModbusForge.Core.Simulation;
using Xunit;

namespace ModbusForge.Tests.Simulation
{
    public class WaveformMathTests
    {
        [Theory]
        [InlineData("Sine", 0.0, 0.0)]
        [InlineData("Sine", 0.5, 0.0)]
        [InlineData("Sine", 1.0, 0.0)]
        public void Sine_StartsZero_CrossesZeroAtHalf_AndEndsZero(string waveform, double progress, double expected)
        {
            var value = WaveformMath.Evaluate(waveform, 100.0, 0.0, progress);
            Assert.Equal(expected, value, 3);
        }

        [Theory]
        [InlineData(0.25, 100.0)]
        [InlineData(0.75, -100.0)]
        public void Sine_ReachsesPeakAndTroughAtQuarterPeriods(double progress, double expected)
        {
            var value = WaveformMath.Evaluate("Sine", 100.0, 0.0, progress);
            Assert.Equal(expected, value, 3);
        }

        [Fact]
        public void Sine_IsPeriodic()
        {
            var atStart = WaveformMath.Evaluate("Sine", 100.0, 0.0, 0.0);
            var atEnd = WaveformMath.Evaluate("Sine", 100.0, 0.0, 1.0);
            Assert.Equal(atStart, atEnd, 3);
        }

        [Fact]
        public void Ramp_GoesFromZeroToAmplitude()
        {
            Assert.Equal(0.0, WaveformMath.Evaluate("Ramp", 100.0, 0.0, 0.0), 3);
            Assert.Equal(50.0, WaveformMath.Evaluate("Ramp", 100.0, 0.0, 0.5), 3);
            Assert.Equal(100.0, WaveformMath.Evaluate("Ramp", 100.0, 0.0, 1.0), 3);
        }

        [Theory]
        [InlineData(0.0, -100.0)]
        [InlineData(0.5, 100.0)]
        [InlineData(1.0, -100.0)]
        public void Triangle_StartsAndEndsAtNegativeAmplitude_PeaksInMiddle(double progress, double expected)
        {
            var value = WaveformMath.Evaluate("Triangle", 100.0, 0.0, progress);
            Assert.Equal(expected, value, 3);
        }

        [Theory]
        [InlineData(0.0, 100.0)]
        [InlineData(0.49, 100.0)]
        [InlineData(0.5, 0.0)]
        [InlineData(0.99, 0.0)]
        public void Square_StaysHighForFirstHalf_LowForSecondHalf(double progress, double expected)
        {
            var value = WaveformMath.Evaluate("Square", 100.0, 0.0, progress);
            Assert.Equal(expected, value, 3);
        }

        [Theory]
        [InlineData("Sine")]
        [InlineData("Ramp")]
        [InlineData("Triangle")]
        [InlineData("Square")]
        public void Offset_ShiftsEveryWaveform(string waveform)
        {
            var plain = WaveformMath.Evaluate(waveform, 100.0, 0.0, 0.25);
            var shifted = WaveformMath.Evaluate(waveform, 100.0, 50.0, 0.25);
            Assert.Equal(plain + 50.0, shifted, 3);
        }

        [Fact]
        public void UnknownWaveform_FallsBackToRamp()
        {
            Assert.Equal(WaveformMath.Evaluate("Ramp", 100.0, 0.0, 0.5),
                         WaveformMath.Evaluate("Bogus", 100.0, 0.0, 0.5), 3);
            Assert.Equal(WaveformMath.Evaluate("Ramp", 100.0, 0.0, 0.5),
                         WaveformMath.Evaluate(null, 100.0, 0.0, 0.5), 3);
        }
    }
}
