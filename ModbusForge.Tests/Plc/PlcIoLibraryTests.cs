using System.Text;
using ModbusForge.Core.Plc;
using static ModbusForge.Tests.Plc.XefBuilder;

namespace ModbusForge.Tests.Plc
{
    /// <summary>
    /// The Quantum analog input blocks of the I/O Management library (Schneider Electric
    /// 33002531.26): QUANTUM and AVI030 in the configuration section give each channel of
    /// an AVI 030 00 module its input word from the XEF's I/O map, and I_SCALE_WARN scales
    /// that word as the module's 16-bit data format defines it (Discrete and Analog I/O
    /// Reference Manual, 35010516.17, pp. 117-122).
    /// </summary>
    public class PlcIoLibraryTests
    {
        // Parameter words of an AVI 030 00 as LCPLC001 exports them: every channel 4..20 mA.
        private static readonly int[] FourTo20mA = { 0x0701, 0x0707, 0x0707, 0x0707, 0x0007 };

        // An AVI 030 00 left at its defaults: every channel +/-10 V (35010516.17, p. 122).
        private static readonly int[] Defaults = { 0x0001, 0, 0, 0, 0 };

        /// <summary>A local Quantum rack (bus 1, drop 1, rack 1) as an XEF's IOConf carries it.</summary>
        private static string LocalRack(int slot, string partNumber, int inputStart, int[] parameters)
        {
            var sb = new StringBuilder("<IOConf><PLC><configQuantum><IOMapBlock><busQuantum busNumber=\"1\"><dropQuantum dropNumber=\"1\"><rackQuantum>");
            sb.Append($"<moduleQuantum><partItem family=\"Analog\" partNumber=\"{partNumber}\"/><equipInfo topoAddress=\"\\1.1\\1.{slot}\" position=\"{slot}\"/>");
            sb.Append($"<moduleInfo inputRefOffset=\"{inputStart}\" outputRefOffset=\"0\" inputIsRegister=\"0\" outputIsRegister=\"0\"><modData lengthOfModParamWords=\"{parameters.Length}\">");
            for (var i = 0; i < parameters.Length; i++) sb.Append($"<parameterWords parameterWordNumber=\"{i + 1}\" parameterValue=\"{parameters[i]}\"/>");
            return sb.Append("</modData></moduleInfo></moduleQuantum></rackQuantum></dropQuantum></busQuantum></IOMapBlock></configQuantum></PLC></IOConf>").ToString();
        }

        /// <summary>
        /// The configuration section (QUANTUM, AVI030 for the module in <paramref name="slot"/>)
        /// and a scaling section with I_SCALE_WARN on channel 2, in that order unless
        /// <paramref name="scalingFirst"/>.
        /// </summary>
        private static PlcHarness Analog(int[] parameters, string partNumber = "140AVI03000", int slot = 8, int inputStart = 1,
            bool coldStartOnly = false, bool scalingFirst = false)
        {
            var configuration = Block("FBI_1", "QUANTUM", 2, 2, $"|SLOT{slot}")
                + Block("FBI_2", "AVI030", 20, 2, (coldStartOnly ? "EN," : "") + "SLOT|CHANNEL1=C1,CHANNEL2=C2", enEno: coldStartOnly)
                + Link("FBI_1", $"SLOT{slot}", "FBI_2", "SLOT");
            if (coldStartOnly) configuration += Block("FBI_0", "SYSSTATE", 2, 20, "|COLD") + Link("FBI_0", "COLD", "FBI_2", "EN");
            var scaling = Block("FBI_3", "I_SCALE_WARN", 2, 2, "CHANNEL=C2,MN=0.0,MX=4095.0|Y=Y,WARN=W");

            var xef = new XefBuilder()
                .Raw(LocalRack(slot, partNumber, inputStart, parameters))
                .Variable("C1", "ANL_IN").Variable("C2", "ANL_IN").Variable("Y", "REAL").Variable("W", "BOOL");
            if (scalingFirst) xef.Section("Scaling", scaling);
            xef.Section("CfgAnaIo", configuration);
            if (!scalingFirst) xef.Section("Scaling", scaling);
            return new PlcHarness(xef.Project());
        }

        private static bool Eno(PlcHarness plc) => plc.BlockNamed("FBI_3").LastEno;

        [Fact]
        public void Avi030_GivesEachChannelItsInputWordFromTheIoMap()
        {
            var plc = Analog(FourTo20mA, inputStart: 10).Scan();

            // ANL_IN: valuePtr is the raw value register, channel the channel number (33002531.26, p. 331).
            Assert.Equal(10, plc.Int("C1.valuePtr"));
            Assert.Equal(11, plc.Int("C2.valuePtr"));
            Assert.Equal(2, plc.Int("C2.channel"));
        }

        [Fact]
        public void IScaleWarn_ScalesA4To20mAChannel_FromMnAt4mAToMxAt20mA()
        {
            var plc = Analog(FourTo20mA);

            plc.Store.InputRegisters[2] = 6400; // channel 2 is %IW2; 4 mA (35010516.17, p. 119)
            plc.Scan();
            Assert.Equal(0.0, plc.Real("Y"), 3);

            plc.Store.InputRegisters[2] = 32000; // 20 mA
            plc.Scan();
            Assert.Equal(4095.0, plc.Real("Y"), 3);

            plc.Store.InputRegisters[2] = 19200; // 12 mA
            plc.Scan();
            Assert.Equal(2047.5, plc.Real("Y"), 3);
            Assert.False(plc.Bool("W"));
            Assert.True(Eno(plc));
        }

        [Fact]
        public void IScaleWarn_OnAChannelLeftAtItsDefaults_ScalesPlusMinus10V()
        {
            var plc = Analog(Defaults);

            plc.Store.InputRegisters[2] = 768; // -10 V
            plc.Scan();
            Assert.Equal(0.0, plc.Real("Y"), 3);

            plc.Store.InputRegisters[2] = 64768; // +10 V
            plc.Scan();
            Assert.Equal(4095.0, plc.Real("Y"), 3);

            plc.Store.InputRegisters[2] = 32768; // 0 V
            plc.Scan();
            Assert.Equal(2047.5, plc.Real("Y"), 3);
        }

        [Fact]
        public void IScaleWarn_ScalesEachChannelByItsOwnRange()
        {
            // GGPLC005's module in \2.4\1.15: channel 3 is 4..20 mA, the others -20..+20 mA.
            var plc = Analog(new[] { 0x0501, 0x0705, 0x0505, 0x0505, 0x0005 });

            plc.Store.InputRegisters[2] = 32768; // channel 2 at 0 mA, the middle of -20..+20 mA
            plc.Scan();

            Assert.Equal(2047.5, plc.Real("Y"), 3);
            Assert.False(plc.Bool("W"));
        }

        [Fact]
        public void IScaleWarn_OutsideTheRatedRange_WarnsAndScalesPastTheEnds()
        {
            var plc = Analog(FourTo20mA);

            plc.Store.InputRegisters[2] = 32640; // 20.4 mA: over the rated 20 mA, under 20.48 mA
            plc.Scan();
            Assert.True(plc.Bool("W"));
            Assert.Equal(4095.0 * (32640 - 6400) / 25600, plc.Real("Y"), 3);
            Assert.True(Eno(plc));

            plc.Store.InputRegisters[2] = 4800; // 3 mA: under 4 mA, above the 2.08 mA broken wire limit
            plc.Scan();
            Assert.True(plc.Bool("W"));
            Assert.Equal(4095.0 * (4800 - 6400) / 25600, plc.Real("Y"), 3);
            Assert.True(Eno(plc));
        }

        [Fact]
        public void IScaleWarn_OutOfRangeOrBrokenWire_IsAnError()
        {
            var plc = Analog(FourTo20mA);

            plc.Store.InputRegisters[2] = 3000; // under 2.08 mA: broken wire (35010516.17, p. 120)
            plc.Scan();
            Assert.False(Eno(plc));
            Assert.True(plc.Bool("W"));

            plc.Store.InputRegisters[2] = 33000; // over 20.48 mA
            plc.Scan();
            Assert.False(Eno(plc));

            plc.Store.InputRegisters[2] = 19200;
            plc.Scan();
            Assert.True(Eno(plc));
            Assert.False(plc.Bool("W"));
            Assert.Contains(plc.Runtime.Notices, n => n.StartsWith("I_SCALE_WARN") && n.Contains("out of range"));
        }

        [Fact]
        public void IScaleWarn_ReadsItsChannelsBitsInTheModuleStatusWord()
        {
            var plc = Analog(FourTo20mA);
            plc.Store.InputRegisters[2] = 19200;

            plc.Store.InputRegisters[9] = 0x0002; // word 9, bit 15 of 1 (MSB) to 16: range warning on channel 2 (p. 120)
            plc.Scan();
            Assert.True(plc.Bool("W"));
            Assert.True(Eno(plc));

            plc.Store.InputRegisters[9] = 0x0200; // channel 2 out of range or broken wire
            plc.Scan();
            Assert.True(plc.Bool("W"));
            Assert.False(Eno(plc));

            plc.Store.InputRegisters[9] = 0x0101; // channel 1's bits only
            plc.Scan();
            Assert.False(plc.Bool("W"));
            Assert.True(Eno(plc));
        }

        [Fact]
        public void ChannelsConfiguredAtColdStart_KeepTheirConfiguration()
        {
            var plc = Analog(FourTo20mA, coldStartOnly: true).Scan(2);
            Assert.False(plc.BlockNamed("FBI_2").ExecutedLastScan); // the configuration section ran once

            plc.Store.InputRegisters[2] = 19200;
            plc.Scan();

            Assert.Equal(2047.5, plc.Real("Y"), 3);
            Assert.True(Eno(plc));
        }

        [Fact]
        public void Avi030_InASlotWithoutAnAvi03000_ReportsIt_AndItsChannelsStayUnconfigured()
        {
            var plc = Analog(Defaults, partNumber: "140DDI84100");
            plc.Store.InputRegisters[2] = 19200;
            plc.Scan();

            Assert.False(plc.BlockNamed("FBI_2").LastEno);
            Assert.False(Eno(plc));
            Assert.Equal(0.0, plc.Real("Y"));
            Assert.Contains(plc.Runtime.Notices, n => n.StartsWith("AVI030") && n.Contains("slot 8"));
            Assert.Contains(plc.Runtime.Notices, n => n.StartsWith("I_SCALE_WARN") && n.Contains("not configured"));
        }

        [Fact]
        public void Notices_OfOneBlockType_AreEachToldOnce()
        {
            // As in LCPLC001, the scaling section runs before the configuration section:
            // the first scan finds the channel not configured yet.
            var plc = Analog(FourTo20mA, scalingFirst: true);
            plc.Store.InputRegisters[2] = 19200;
            plc.Scan(2);
            Assert.True(Eno(plc));

            plc.Store.InputRegisters[2] = 0;
            plc.Scan(2);

            Assert.Equal(2, plc.Runtime.Notices.Count(n => n.StartsWith("I_SCALE_WARN")));
        }
    }
}
