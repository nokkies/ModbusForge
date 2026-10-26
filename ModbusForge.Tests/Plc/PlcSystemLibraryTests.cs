using ModbusForge.Core.Plc;
using static ModbusForge.Tests.Plc.XefBuilder;

namespace ModbusForge.Tests.Plc
{
    /// <summary>
    /// Clock blocks of the Control Expert System library (System Block Library, Schneider
    /// Electric 33002539.25) and DT_AS_BYTE. DT values are BCD (35006144.27, p. 136).
    /// </summary>
    public class PlcSystemLibraryTests
    {
        private static readonly DateTime Clock = new(2026, 10, 22, 18, 42, 7, 345);

        private static PlcHarness Fbd(string network, params (string Name, string Type)[] variables)
        {
            var xef = new XefBuilder();
            foreach (var (name, type) in variables) xef.Variable(name, type);
            var plc = new PlcHarness(xef.Section("S", network).Project());
            plc.Runtime.LocalClock = () => Clock;
            return plc;
        }

        [Fact]
        public void RrtcDt_ReadsTheLocalClock_AsBcd()
        {
            var plc = Fbd(Block(".1", "RRTC_DT", 2, 2, "|OUT=T"), ("T", "DT")).Scan();

            Assert.Equal(0x2026_1022_1842_0700L, plc.Int("T"));
        }

        [Fact]
        public void RrtcDtMs_GivesTheDateWithMilliseconds_InBothFormats()
        {
            var plc = Fbd(Block(".1", "RRTC_DT_MS", 2, 2, "|OUT1=D,OUT2=C,STATUS=S"),
                ("D", "Display_NTPC"), ("C", "Calc_NTPC"), ("S", "INT")).Scan();

            Assert.Equal(0x2026_1022_1842_0700L, plc.Int("D.DT_value"));
            Assert.Equal(345, plc.Int("D.Milisecond"));
            Assert.Equal((long)(Clock - new DateTime(1980, 1, 1)).TotalSeconds, plc.Int("C.Seconds"));
            Assert.Equal(345, plc.Int("C.Fraction_Second"));
            Assert.Equal(0, plc.Int("S")); // time valid
        }

        [Fact]
        public void DtAsByte_SplitsTheDateFromItsLeastSignificantByte()
        {
            var plc = Fbd(Block(".1", "DT_AS_BYTE", 2, 2,
                    "IN=T|BYTE1=B1,BYTE2=B2,BYTE3=B3,BYTE4=B4,BYTE5=B5,BYTE6=B6,BYTE7=B7,BYTE8=B8"),
                ("T", "DT"), ("B1", "BYTE"), ("B2", "BYTE"), ("B3", "BYTE"), ("B4", "BYTE"),
                ("B5", "BYTE"), ("B6", "BYTE"), ("B7", "BYTE"), ("B8", "BYTE"));
            plc.Runtime.Write("T", PlcValue.FromInteger(PlcType.DateAndTime, 0x2026_1022_1842_0700L));

            plc.Scan();

            Assert.Equal(new long[] { 0x00, 0x07, 0x42, 0x18, 0x22, 0x10, 0x26, 0x20 },
                Enumerable.Range(1, 8).Select(i => plc.Int($"B{i}")).ToArray());
        }
    }
}
