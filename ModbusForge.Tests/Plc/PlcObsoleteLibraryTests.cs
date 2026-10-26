using ModbusForge.Core.Plc;
using static ModbusForge.Tests.Plc.XefBuilder;

namespace ModbusForge.Tests.Plc
{
    /// <summary>
    /// Blocks of the Control Expert Obsolete library, as the Obsolete Block Library manual
    /// describes them (Schneider Electric 33002543.19, 10/2019).
    /// </summary>
    public class PlcObsoleteLibraryTests
    {
        private static PlcHarness Fbd(string network, params (string Name, string Type)[] variables)
        {
            var xef = new XefBuilder();
            foreach (var (name, type) in variables) xef.Variable(name, type);
            return new PlcHarness(xef.Section("S", network).Project());
        }

        private static void Set(PlcHarness plc, string reference, long value)
            => plc.Runtime.Write(reference, PlcValue.FromInteger(PlcType.AnyInteger, value));

        private static void SetTime(PlcHarness plc, string reference, long ms)
            => plc.Runtime.Write(reference, PlcValue.FromTime(ms));

        private static long[] Ints(PlcHarness plc, string array, int lower, int count)
            => Enumerable.Range(lower, count).Select(i => plc.Int($"{array}[{i}]")).ToArray();

        // ---- COMPARE (p. 415) --------------------------------------------------------

        [Fact]
        public void Compare_SetsAllFourResults_WhileValidated()
        {
            var plc = Fbd(Block(".1", "COMPARE", 2, 2, "VAL=V,IN1=A,IN2=B|SUP=G,EQU=E,INF=L,DIF=D"),
                ("V", "BOOL"), ("A", "INT"), ("B", "INT"), ("G", "BOOL"), ("E", "BOOL"), ("L", "BOOL"), ("D", "BOOL"));
            plc.Runtime.Write("V", PlcOps.True);
            Set(plc, "A", 5);
            Set(plc, "B", 3);

            plc.Scan();

            Assert.Equal((true, false, false, true), (plc.Bool("G"), plc.Bool("E"), plc.Bool("L"), plc.Bool("D")));
        }

        [Fact]
        public void Compare_WithoutValidation_DoesNotCompare()
        {
            var plc = Fbd(Block(".1", "COMPARE", 2, 2, "VAL=FALSE,IN1=1,IN2=1|SUP=G,EQU=E,INF=L,DIF=D"),
                ("G", "BOOL"), ("E", "BOOL"), ("L", "BOOL"), ("D", "BOOL")).Scan();

            Assert.False(plc.Bool("E"));
        }

        // ---- R2T_* (pp. 541-546) ------------------------------------------------------

        [Fact]
        public void R2t_FillsTheTableOneValuePerCycle_ThenReportsItFull()
        {
            var plc = Fbd(Block("FBI_1", "R2T_INT", 2, 2, "NOINC=FALSE,R=R,SRC=X,OFF=P|END=F,DEST=T,OFF=P"),
                ("R", "BOOL"), ("X", "INT"), ("P", "UINT"), ("F", "BOOL"), ("T", "ARRAY[1..3] OF INT"));

            plc.Runtime.Write("R", PlcOps.True); // OFF to 1, then up by 1 in the same cycle
            Set(plc, "X", 10);
            plc.Scan();
            Assert.Equal(2, plc.Int("P"));

            plc.Runtime.Write("R", PlcOps.False);
            Set(plc, "X", 20);
            plc.Scan();
            Set(plc, "X", 30);
            plc.Scan();
            Assert.Equal(new long[] { 10, 20, 30 }, Ints(plc, "T", 1, 3));
            Assert.True(plc.Bool("F"));
            Assert.Equal(3, plc.Int("P"));

            Set(plc, "X", 40); // full: no further copies
            plc.Scan();
            Assert.Equal(new long[] { 10, 20, 30 }, Ints(plc, "T", 1, 3));
        }

        [Fact]
        public void R2t_WithNoinc_KeepsWritingTheSamePosition()
        {
            var plc = Fbd(Block("FBI_1", "R2T_INT", 2, 2, "NOINC=TRUE,R=FALSE,SRC=X,OFF=P|END=F,DEST=T,OFF=P"),
                ("X", "INT"), ("P", "UINT"), ("F", "BOOL"), ("T", "ARRAY[1..3] OF INT"));
            Set(plc, "P", 2);
            Set(plc, "X", 7);

            plc.Scan(2);

            Assert.Equal(new long[] { 0, 7, 0 }, Ints(plc, "T", 1, 3));
            Assert.Equal(2, plc.Int("P"));
        }

        // ---- T2T (pp. 589-594) ------------------------------------------------------

        [Fact]
        public void T2t_CopiesSizeWordsAtOff_AndMovesOffOn()
        {
            var plc = Fbd(Block("FBI_1", "T2T", 2, 2, "NOINC=FALSE,R=FALSE,SRC=S,SIZE=2,OFF=P|END=F,DEST=D,OFF=P"),
                ("S", "ARRAY[1..4] OF INT"), ("P", "UINT"), ("F", "BOOL"), ("D", "ARRAY[1..4] OF INT"));
            for (var i = 1; i <= 4; i++) Set(plc, $"S[{i}]", i * 11);
            Set(plc, "P", 1);

            plc.Scan();
            Assert.Equal(new long[] { 11, 22, 0, 0 }, Ints(plc, "D", 1, 4));
            Assert.Equal(3, plc.Int("P"));

            plc.Scan();
            Assert.Equal(new long[] { 11, 22, 33, 44 }, Ints(plc, "D", 1, 4));
            Assert.False(plc.Bool("F"));

            plc.Scan(); // words 5 and 6 are outside the tables
            Assert.True(plc.Bool("F"));
            Assert.Equal(5, plc.Int("P"));
        }

        [Fact]
        public void T2t_WithOffZero_BlocksTheCopy()
        {
            var plc = Fbd(Block("FBI_1", "T2T", 2, 2, "NOINC=FALSE,R=FALSE,SRC=S,SIZE=1,OFF=P|END=F,DEST=D,OFF=P"),
                ("S", "ARRAY[1..2] OF INT"), ("P", "UINT"), ("F", "BOOL"), ("D", "ARRAY[1..2] OF INT"));
            Set(plc, "S[1]", 5);

            plc.Scan();

            Assert.True(plc.Bool("F"));
            Assert.Equal(0, plc.Int("D[1]"));
        }

        // ---- PWM (pp. 339-347) ------------------------------------------------------

        [Fact]
        public void Pwm_OutputsAPulseProportionalToX_InEachPeriod()
        {
            // Period 1 s, X = 30 of up_pos 100: Y_POS is 1 for the first 300 ms of each period.
            var plc = Fbd(Block("FBI_1", "PWM", 2, 2, "X=30.0,R=FALSE,PARA=P|Y_POS=YP,Y_NEG=YN"),
                ("P", "Para_PWM"), ("YP", "BOOL"), ("YN", "BOOL"));
            SetTime(plc, "P.t_period", 1000);
            SetTime(plc, "P.t_max", 1000);
            plc.Runtime.Write("P.up_pos", PlcValue.FromReal(100));
            plc.Runtime.Write("P.up_neg", PlcValue.FromReal(100));

            var pattern = Enumerable.Range(0, 12).Select(_ => plc.Scan().Bool("YP")).ToArray();

            // Scans at 0, 100, ..., 1100 ms of controller time after the first call.
            Assert.Equal(new[] { true, true, true, false, false, false, false, false, false, false, true, true }, pattern);
            Assert.False(plc.Bool("YN"));
        }

        [Fact]
        public void Pwm_ShorterThanTmin_GivesNoPulse()
        {
            var plc = Fbd(Block("FBI_1", "PWM", 2, 2, "X=-5.0,R=FALSE,PARA=P|Y_POS=YP,Y_NEG=YN"),
                ("P", "Para_PWM"), ("YP", "BOOL"), ("YN", "BOOL"));
            SetTime(plc, "P.t_period", 1000);
            SetTime(plc, "P.t_min", 100);
            SetTime(plc, "P.t_max", 1000);
            plc.Runtime.Write("P.up_neg", PlcValue.FromReal(100)); // 50 ms < t_min

            Assert.DoesNotContain(true, Enumerable.Range(0, 10).Select(_ => plc.Scan().Bool("YN")));
        }

        // ---- FGEN (pp. 189-203) ------------------------------------------------------

        [Fact]
        public void Fgen_Jump_FollowsStart_AndCountsStarts()
        {
            var plc = Fbd(Block("FBI_1", "FGEN", 2, 2, "R=FALSE,START=S,PARA=P,YOFF=1.0|Y=Y,ACTIVE=A,N=N"),
                ("S", "BOOL"), ("P", "Para_FGEN"), ("Y", "REAL"), ("A", "BOOL"), ("N", "INT"));
            Set(plc, "P.func_no", 1);
            plc.Runtime.Write("P.amplitude", PlcValue.FromReal(4));

            plc.Scan();
            Assert.Equal(1, plc.Real("Y"), 3);

            plc.Runtime.Write("S", PlcOps.True);
            plc.Scan();
            Assert.Equal(5, plc.Real("Y"), 3);
            Assert.Equal(1, plc.Int("N"));
        }

        [Fact]
        public void Fgen_Ramp_RisesOverTrise_AndDropsBackWithStart()
        {
            var plc = Fbd(Block("FBI_1", "FGEN", 2, 2, "R=FALSE,START=S,PARA=P,YOFF=0.0|Y=Y"),
                ("S", "BOOL"), ("P", "Para_FGEN"), ("Y", "REAL"));
            Set(plc, "P.func_no", 2);
            plc.Runtime.Write("P.amplitude", PlcValue.FromReal(10));
            SetTime(plc, "P.t_rise", 400);
            plc.Runtime.Write("S", PlcOps.True);

            plc.Scan();         // starts
            plc.Scan(2);        // 200 ms in
            Assert.Equal(5, plc.Real("Y"), 3);
            plc.Scan(3);        // past t_rise
            Assert.Equal(10, plc.Real("Y"), 3);

            plc.Runtime.Write("S", PlcOps.False);
            plc.Scan();
            Assert.Equal(0, plc.Real("Y"), 3);
        }

        [Fact]
        public void Fgen_BipolarSquare_AlternatesHalfWaves_WithIdleTime()
        {
            // Half period 400 ms with 100 ms idle: +amplitude for 300 ms, 0 for 100 ms, then the negative half.
            var plc = Fbd(Block("FBI_1", "FGEN", 2, 2, "R=FALSE,START=TRUE,PARA=P,YOFF=0.0|Y=Y,ACTIVE=A"),
                ("P", "Para_FGEN"), ("Y", "REAL"), ("A", "BOOL"));
            Set(plc, "P.func_no", 5);
            plc.Runtime.Write("P.amplitude", PlcValue.FromReal(2));
            SetTime(plc, "P.halfperiod", 400);
            SetTime(plc, "P.t_off", 100);

            var values = Enumerable.Range(0, 8).Select(_ => plc.Scan().Real("Y")).ToArray();

            Assert.Equal(new double[] { 2, 2, 2, 0, -2, -2, -2, 0 }, values);
            Assert.True(plc.Bool("A"));
        }

        [Fact]
        public void Fgen_Reset_ReturnsToTheOffset()
        {
            var plc = Fbd(Block("FBI_1", "FGEN", 2, 2, "R=R,START=TRUE,PARA=P,YOFF=3.0|Y=Y,ACTIVE=A,N=N"),
                ("R", "BOOL"), ("P", "Para_FGEN"), ("Y", "REAL"), ("A", "BOOL"), ("N", "INT"));
            Set(plc, "P.func_no", 5);
            plc.Runtime.Write("P.amplitude", PlcValue.FromReal(2));
            SetTime(plc, "P.halfperiod", 400);
            plc.Scan(2);

            plc.Runtime.Write("R", PlcOps.True);
            plc.Scan();

            Assert.Equal(3, plc.Real("Y"), 3);
            Assert.False(plc.Bool("A"));
            Assert.Equal(0, plc.Int("N"));
        }
    }
}
