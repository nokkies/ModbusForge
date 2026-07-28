using System.Collections.Generic;
using ModbusForge.Models;

namespace ModbusForge.Tests.Models
{
    public class TagScalingAndEnumTests
    {
        [Fact]
        public void FormattedValue_AppliesScaleOffsetAndUnits()
        {
            var tag = new Tag { Scale = 0.1, Offset = 2, Units = "Hz", CurrentValue = 300 };

            Assert.Equal(32.0, tag.ScaledValue);
            Assert.Equal("32.00 Hz", tag.FormattedValue);
        }

        [Fact]
        public void FormattedValue_PrefersTheEnumLabel()
        {
            var tag = new Tag
            {
                Units = "Hz",
                ValueEnum = new Dictionary<int, string> { [0] = "Off", [1] = "Run" },
                CurrentValue = 1,
            };

            Assert.Equal("Run", tag.FormattedValue);
        }

        [Fact]
        public void FormattedValue_FallsBackToScalingForUnmappedEnumValues()
        {
            var tag = new Tag
            {
                Units = "Hz",
                ValueEnum = new Dictionary<int, string> { [0] = "Off" },
                CurrentValue = 7,
            };

            Assert.Equal("7.00 Hz", tag.FormattedValue);
        }

        [Fact]
        public void ToRawValue_InvertsScalingForWrites()
        {
            var tag = new Tag { Scale = 0.1, Offset = 2 };

            Assert.Equal(300, tag.ToRawValue(32.0));
        }

        [Fact]
        public void ToRawValue_ResolvesEnumLabels()
        {
            var tag = new Tag
            {
                ValueEnum = new Dictionary<int, string> { [0] = "Off", [2] = "Auto" },
            };

            Assert.Equal(2, tag.ToRawValue("auto"));
        }

        [Fact]
        public void ToRawValue_TreatsZeroScaleAsUnity()
        {
            var tag = new Tag { Scale = 0, Offset = 1 };

            Assert.Equal(4, tag.ToRawValue(5.0));
        }
    }
}
