using ModbusForge.Core.Plc;
using static ModbusForge.Tests.Plc.XefBuilder;

namespace ModbusForge.Tests.Plc
{
    /// <summary>
    /// Blocks of the Control Expert Control library (CONT_CTL), as the Control Block
    /// Library manual describes them (Schneider Electric 33002535.16, 09/2020).
    /// </summary>
    public class PlcControlLibraryTests
    {
        private static PlcHarness Run(string network, params (string Name, string Type)[] variables)
        {
            var xef = new XefBuilder();
            foreach (var (name, type) in variables) xef.Variable(name, type);
            return new PlcHarness(xef.Section("S", network).Project());
        }

        private static void Set(PlcHarness plc, string reference, double value)
            => plc.Runtime.Write(reference, PlcValue.FromReal(value));

        private static double Real(PlcHarness plc, string reference) => plc.Runtime.Read(reference).AsDouble();

        // ---- COMP_DB (pp. 212-215) ----------------------------------------------

        [Fact]
        public void CompDb_WithoutDeadBand_IsAClassicComparison()
        {
            var plc = Run(Block("FBI_1", "COMP_DB", 2, 2, "IN1=A,IN2=B,DBAND=0.0,HYST=0.0|GREATER=G,EQUAL=E,LESS=L"),
                ("A", "REAL"), ("B", "REAL"), ("G", "BOOL"), ("E", "BOOL"), ("L", "BOOL"));

            Set(plc, "A", 5); Set(plc, "B", 3);
            plc.Scan();
            Assert.Equal((true, false, false), (plc.Bool("G"), plc.Bool("E"), plc.Bool("L")));

            Set(plc, "A", 3);
            plc.Scan();
            Assert.Equal((false, true, false), (plc.Bool("G"), plc.Bool("E"), plc.Bool("L")));

            Set(plc, "A", 1);
            plc.Scan();
            Assert.Equal((false, false, true), (plc.Bool("G"), plc.Bool("E"), plc.Bool("L")));
        }

        [Fact]
        public void CompDb_ReturnsToEqual_OnlyInsideDeadBandMinusHysteresis()
        {
            // DBAND 2, HYST 1: from GREATER, EQUAL needs |IN1 - IN2| <= 1; inside EQUAL it holds up to 2.
            var plc = Run(Block("FBI_1", "COMP_DB", 2, 2, "IN1=A,IN2=0.0,DBAND=2.0,HYST=1.0|GREATER=G,EQUAL=E,LESS=L"),
                ("A", "REAL"), ("G", "BOOL"), ("E", "BOOL"), ("L", "BOOL"));

            Set(plc, "A", 5);
            plc.Scan();
            Assert.True(plc.Bool("G"));

            Set(plc, "A", 1.5);
            plc.Scan();
            Assert.True(plc.Bool("G"));
            Assert.False(plc.Bool("E"));

            Set(plc, "A", 0.5);
            plc.Scan();
            Assert.True(plc.Bool("E"));

            Set(plc, "A", 1.5);
            plc.Scan();
            Assert.True(plc.Bool("E"));

            Set(plc, "A", -2.5);
            plc.Scan();
            Assert.Equal((false, false, true), (plc.Bool("G"), plc.Bool("E"), plc.Bool("L")));
        }

        // ---- AVGMV (pp. 232-235) ------------------------------------------------

        [Fact]
        public void Avgmv_AveragesTheLastNValues_AndIsReadyWhenTheWindowIsFull()
        {
            var plc = Run(Block("FBI_1", "AVGMV", 2, 2, "MAN=FALSE,X=X,N=3,YMAN=0.0|Y=Y,RDY=R"),
                ("X", "REAL"), ("Y", "REAL"), ("R", "BOOL"));

            Set(plc, "X", 3);
            plc.Scan();
            Assert.Equal(3, Real(plc, "Y"), 3);
            Assert.False(plc.Bool("R"));

            Set(plc, "X", 6);
            plc.Scan();
            Assert.Equal(4.5, Real(plc, "Y"), 3);

            Set(plc, "X", 9);
            plc.Scan();
            Assert.Equal(6, Real(plc, "Y"), 3);
            Assert.True(plc.Bool("R"));

            Set(plc, "X", 12);
            plc.Scan();
            Assert.Equal(9, Real(plc, "Y"), 3); // (6 + 9 + 12) / 3: the oldest value left the window
        }

        [Fact]
        public void Avgmv_InManual_OutputsYmanAndFillsTheWindow()
        {
            var plc = Run(Block("FBI_1", "AVGMV", 2, 2, "MAN=M,X=1.0,N=4,YMAN=7.0|Y=Y,RDY=R"),
                ("M", "BOOL"), ("Y", "REAL"), ("R", "BOOL"));

            plc.Runtime.Write("M", PlcOps.True);
            plc.Scan();
            Assert.Equal(7, Real(plc, "Y"), 3);
            Assert.True(plc.Bool("R"));

            // Back to automatic: the window still holds YMAN, so one new X moves the mean by a quarter.
            plc.Runtime.Write("M", PlcOps.False);
            plc.Scan();
            Assert.Equal((7 + 7 + 7 + 1) / 4.0, Real(plc, "Y"), 3);
        }

        [Fact]
        public void Avgmv_ChangingN_RestartsTheWindowWithTheInput()
        {
            var plc = Run(Block("FBI_1", "AVGMV", 2, 2, "MAN=FALSE,X=X,N=N,YMAN=0.0|Y=Y,RDY=R"),
                ("X", "REAL"), ("N", "INT"), ("Y", "REAL"), ("R", "BOOL"));
            plc.Runtime.Write("N", PlcValue.FromInteger(PlcType.Int, 2));
            Set(plc, "X", 2);
            plc.Scan(2);
            Assert.True(plc.Bool("R"));

            plc.Runtime.Write("N", PlcValue.FromInteger(PlcType.Int, 3));
            Set(plc, "X", 10);
            plc.Scan();

            Assert.Equal(10, Real(plc, "Y"), 3);
            Assert.False(plc.Bool("R"));
        }

        // ---- LOOKUP_TABLE1 (pp. 250-253) ----------------------------------------

        [Fact]
        public void LookupTable1_InterpolatesBetweenSupportPoints_AndFlagsTheEnds()
        {
            // Support points (0, 0), (10, 100), (20, 150).
            var plc = Run(Block(".1", "LOOKUP_TABLE1", 2, 2,
                    "X=X,XIYI1=0.0,XIYI2=0.0,XIYI3=10.0,XIYI4=100.0,XIYI5=20.0,XIYI6=150.0|Y=Y,QXHI=HI,QXLO=LO"),
                ("X", "REAL"), ("Y", "REAL"), ("HI", "BOOL"), ("LO", "BOOL"));

            Set(plc, "X", 5);
            plc.Scan();
            Assert.Equal(50, Real(plc, "Y"), 3);

            Set(plc, "X", 15);
            plc.Scan();
            Assert.Equal(125, Real(plc, "Y"), 3);
            Assert.False(plc.Bool("HI") || plc.Bool("LO"));

            Set(plc, "X", -1);
            plc.Scan();
            Assert.Equal(0, Real(plc, "Y"), 3);
            Assert.True(plc.Bool("LO"));

            Set(plc, "X", 25);
            plc.Scan();
            Assert.Equal(150, Real(plc, "Y"), 3);
            Assert.True(plc.Bool("HI"));
        }

        [Fact]
        public void LookupTable1_OnAVerticalStep_TakesTheMidpoint()
        {
            // (0, 0), (5, 10), (5, 20), (10, 30): X = 5 sits on both points of the step.
            var plc = Run(Block(".1", "LOOKUP_TABLE1", 2, 2,
                    "X=5.0,XIYI1=0.0,XIYI2=0.0,XIYI3=5.0,XIYI4=10.0,XIYI5=5.0,XIYI6=20.0,XIYI7=10.0,XIYI8=30.0|Y=Y"),
                ("Y", "REAL")).Scan();

            Assert.Equal(15, Real(plc, "Y"), 3);
        }

        // ---- SCALING (pp. 92-95) ------------------------------------------------

        [Fact]
        public void Scaling_MapsTheInputRange_AndClipsWhenAsked()
        {
            var plc = Run(Block("FBI_1", "SCALING", 2, 2, "IN=X,PARA=P|OUT=Y,STATUS=S"),
                ("X", "REAL"), ("P", "Para_SCALING"), ("Y", "REAL"), ("S", "WORD"));
            Set(plc, "P.in_min", 0); Set(plc, "P.in_max", 100);
            Set(plc, "P.out_min", 0); Set(plc, "P.out_max", 1000);

            Set(plc, "X", 50);
            plc.Scan();
            Assert.Equal(500, Real(plc, "Y"), 3);
            Assert.Equal(0, plc.Int("S"));

            Set(plc, "X", 150);
            plc.Scan();
            Assert.Equal(1500, Real(plc, "Y"), 3);

            plc.Runtime.Write("P.clip", PlcOps.True);
            plc.Scan();
            Assert.Equal(1000, Real(plc, "Y"), 3);
            Assert.Equal(0x0010, plc.Int("S"));
        }

        [Fact]
        public void Scaling_WithAnEmptyInputRange_KeepsItsOutput()
        {
            var plc = Run(Block("FBI_1", "SCALING", 2, 2, "IN=X,PARA=P|OUT=Y,STATUS=S"),
                ("X", "REAL"), ("P", "Para_SCALING"), ("Y", "REAL"), ("S", "WORD"));
            Set(plc, "P.in_max", 10); Set(plc, "P.out_max", 20);
            Set(plc, "X", 5);
            plc.Scan();
            Assert.Equal(10, Real(plc, "Y"), 3);

            Set(plc, "P.in_max", 0); // in_min = in_max
            Set(plc, "X", 7);
            plc.Scan();

            Assert.Equal(10, Real(plc, "Y"), 3);
            Assert.Equal(0x0080, plc.Int("S")); // the status table gives ENO = 1 for this case
            Assert.True(plc.BlockNamed("FBI_1").LastEno);
        }

        // ---- RAMP (pp. 330-334) -------------------------------------------------

        [Fact]
        public void Ramp_MovesTowardsTheTargetAtItsRate_AndSaysWhenDone()
        {
            var plc = Run(Block("FBI_1", "RAMP", 2, 2, "RSP=T,PARA=P,TR_I=0.0,TR_S=FALSE|SP=SP,DONE=D"),
                ("T", "REAL"), ("P", "Para_RAMP"), ("SP", "REAL"), ("D", "BOOL"));
            Set(plc, "P.inc_rate", 10); // 10 units per second
            Set(plc, "T", 3);

            plc.Scan(periodMs: 100); // first call: no time has passed yet
            plc.Scan(periodMs: 100);
            Assert.Equal(1, Real(plc, "SP"), 3);
            Assert.False(plc.Bool("D"));

            plc.Scan(2, periodMs: 100);
            Assert.Equal(3, Real(plc, "SP"), 3);
            Assert.True(plc.Bool("D"));
        }

        [Fact]
        public void Ramp_WithZeroRate_JumpsToTheTarget()
        {
            var plc = Run(Block("FBI_1", "RAMP", 2, 2, "RSP=T,PARA=P,TR_I=0.0,TR_S=FALSE|SP=SP,DONE=D"),
                ("T", "REAL"), ("P", "Para_RAMP"), ("SP", "REAL"), ("D", "BOOL"));
            Set(plc, "T", -4); // dec_rate is 0

            plc.Scan();

            Assert.Equal(-4, Real(plc, "SP"), 3);
            Assert.True(plc.Bool("D"));
        }

        [Fact]
        public void Ramp_Tracking_HoldsTheInitialValue_WithDoneOff()
        {
            var plc = Run(Block("FBI_1", "RAMP", 2, 2, "RSP=9.0,PARA=P,TR_I=2.5,TR_S=TR|SP=SP,DONE=D"),
                ("P", "Para_RAMP"), ("TR", "BOOL"), ("SP", "REAL"), ("D", "BOOL"));
            Set(plc, "P.inc_rate", 1);
            plc.Runtime.Write("TR", PlcOps.True);

            plc.Scan(3);

            Assert.Equal(2.5, Real(plc, "SP"), 3);
            Assert.False(plc.Bool("D"));
        }

        [Fact]
        public void Ramp_NegativeRate_CountsAsZero_WithAWarning()
        {
            var plc = Run(Block("FBI_1", "RAMP", 2, 2, "RSP=5.0,PARA=P,TR_I=0.0,TR_S=FALSE|SP=SP,STATUS=S"),
                ("P", "Para_RAMP"), ("SP", "REAL"), ("S", "WORD"));
            Set(plc, "P.inc_rate", -1);

            plc.Scan();

            Assert.Equal(5, Real(plc, "SP"), 3);
            Assert.Equal(0x0010, plc.Int("S"));
            Assert.True(plc.BlockNamed("FBI_1").LastEno);
        }

        // ---- Library structures ------------------------------------------------

        [Fact]
        public void LibraryParameterStructures_ResolveWithoutAnXefDefinition()
        {
            var plc = new PlcHarness(new XefBuilder().Variable("P", "Para_SCALING").Variable("R", "Para_RAMP").Project());

            plc.Runtime.Write("P.clip", PlcOps.True);
            Set(plc, "R.dec_rate", 2.5);

            Assert.True(plc.Bool("P.clip"));
            Assert.Equal(2.5, Real(plc, "R.dec_rate"), 3);
        }
    }
}
