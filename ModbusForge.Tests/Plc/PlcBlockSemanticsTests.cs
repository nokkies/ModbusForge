using ModbusForge.Core.Plc;
using static ModbusForge.Tests.Plc.XefBuilder;

namespace ModbusForge.Tests.Plc
{
    /// <summary>
    /// Standard blocks compute as the Unity descriptions embedded in the XEF say,
    /// reading the variables on their input pins and writing the ones on their outputs.
    /// </summary>
    public class PlcBlockSemanticsTests
    {
        private static PlcHarness Run(string network, params (string Name, string Type, string? Address)[] variables)
        {
            var xef = new XefBuilder();
            foreach (var (name, type, address) in variables) xef.Variable(name, type, address);
            return new PlcHarness(xef.Section("S", network).Project());
        }

        [Fact]
        public void Move_CopiesItsInputVariableToItsOutputVariable()
        {
            var plc = Run(Block(".1", "MOVE", 2, 2, "IN=Setpoint|OUT=Command"),
                ("Setpoint", "INT", "%MW5"), ("Command", "INT", "%MW10"));
            plc.Store.HoldingRegisters[5] = 321;

            plc.Scan();

            Assert.Equal(321, plc.Store.HoldingRegisters[10]);
        }

        [Fact]
        public void And_ReadsTheVariablesOnItsPins_AndWritesItsOutputVariable()
        {
            var plc = Run(Block(".1", "AND", 2, 2, "IN1=A,IN2=B|OUT=C"),
                ("A", "EBOOL", "%M1"), ("B", "EBOOL", "%M2"), ("C", "EBOOL", "%M3"));

            plc.Store.CoilDiscretes[1] = true;
            plc.Scan();
            Assert.False(plc.Store.CoilDiscretes[3]);

            plc.Store.CoilDiscretes[2] = true;
            plc.Scan();
            Assert.True(plc.Store.CoilDiscretes[3]);
        }

        [Fact]
        public void Or_WithThreeInputs_IsTrueWhenAnyInputIs()
        {
            var plc = Run(Block(".1", "OR", 2, 2, "IN1=FALSE,IN2=FALSE,IN3=A|OUT=C"),
                ("A", "BOOL", null), ("C", "BOOL", null));

            plc.Runtime.Write("A", PlcOps.True);
            plc.Scan();

            Assert.True(plc.Bool("C"));
        }

        [Fact]
        public void Xor_IsExclusive()
        {
            var plc = Run(Block(".1", "XOR", 2, 2, "IN1=TRUE,IN2=TRUE|OUT=C"), ("C", "BOOL", null)).Scan();

            Assert.False(plc.Bool("C"));
        }

        [Fact]
        public void AndOnWords_IsBitWise()
        {
            var plc = Run(Block(".1", "AND", 2, 2, "IN1=A,IN2=16#00FF|OUT=C"),
                ("A", "WORD", "%MW1"), ("C", "WORD", "%MW2"));
            plc.Store.HoldingRegisters[1] = 0x0F0F;

            plc.Scan();

            Assert.Equal(0x000F, plc.Store.HoldingRegisters[2]);
        }

        [Fact]
        public void NegatedInputPin_InvertsTheValueItReads()
        {
            var plc = Run(Block(".1", "AND", 2, 2, "!IN1=Stop,IN2=Run|OUT=Motor"),
                ("Stop", "BOOL", null), ("Run", "BOOL", null), ("Motor", "BOOL", null));
            plc.Runtime.Write("Run", PlcOps.True);

            plc.Scan();
            Assert.True(plc.Bool("Motor"));

            plc.Runtime.Write("Stop", PlcOps.True);
            plc.Scan();
            Assert.False(plc.Bool("Motor"));
        }

        [Fact]
        public void SetAndReset_ConditionedByEn_LatchTheirOutputVariable()
        {
            var plc = Run(
                Block(".1", "SET", 2, 2, "EN=Start|OUT=Motor", enEno: true) +
                Block(".2", "RESET", 2, 12, "EN=Stop|OUT=Motor", enEno: true),
                ("Start", "EBOOL", "%M1"), ("Stop", "EBOOL", "%M2"), ("Motor", "EBOOL", "%M10"));

            plc.Store.CoilDiscretes[1] = true;
            plc.Scan();
            Assert.True(plc.Store.CoilDiscretes[10]);

            plc.Store.CoilDiscretes[1] = false;
            plc.Scan(3);
            Assert.True(plc.Store.CoilDiscretes[10]);

            plc.Store.CoilDiscretes[2] = true;
            plc.Scan();
            Assert.False(plc.Store.CoilDiscretes[10]);
        }

        [Fact]
        public void FunctionWithEnZero_DrivesItsOutputLinkToZero_ButKeepsItsOutputVariable()
        {
            var plc = Run(
                Block(".1", "ADD", 2, 2, "EN=Enable,IN1=5,IN2=6|OUT=Sum", enEno: true) +
                Block(".2", "MOVE", 20, 2, "IN|OUT=Copy") +
                Link(".1", "OUT", ".2", "IN"),
                ("Enable", "BOOL", null), ("Sum", "INT", null), ("Copy", "INT", null));
            plc.Runtime.Write("Enable", PlcOps.True);
            plc.Scan();
            Assert.Equal(11, plc.Int("Copy"));

            plc.Runtime.Write("Enable", PlcOps.False);
            plc.Scan();

            Assert.Equal(0, plc.Int("Copy"));
            Assert.Equal(11, plc.Int("Sum"));
            Assert.False(plc.BlockNamed(".1").LastEno);
        }

        [Fact]
        public void EnoLinkedToEn_ChainsTheCalls()
        {
            var plc = Run(
                Block(".1", "MOVE", 2, 2, "EN=Go,IN=1|ENO,OUT=First", enEno: true) +
                Block(".2", "MOVE", 20, 2, "EN,IN=2|ENO,OUT=Second", enEno: true) +
                Link(".1", "ENO", ".2", "EN"),
                ("Go", "BOOL", null), ("First", "INT", null), ("Second", "INT", null));

            plc.Scan();
            Assert.Equal(0, plc.Int("Second"));

            plc.Runtime.Write("Go", PlcOps.True);
            plc.Scan();
            Assert.Equal(2, plc.Int("Second"));
        }

        [Fact]
        public void Ton_RaisesQ_OnlyAfterThePresetTime()
        {
            var plc = new PlcHarness(new XefBuilder()
                .Variable("Run", "BOOL")
                .Variable("Delayed", "BOOL")
                .Variable("Elapsed", "TIME")
                .Variable("FBI_1", "TON")
                .Section("S", Block("FBI_1", "TON", 2, 2, "IN=Run,PT=t#1s|Q=Delayed,ET=Elapsed"))
                .Raw(TonSignature)
                .Project());

            plc.Runtime.Write("Run", PlcOps.True);
            plc.Scan(); // edge: timing starts (ET = 0)
            plc.Scan(9);
            Assert.False(plc.Bool("Delayed"));
            Assert.Equal(900, plc.Int("Elapsed"));

            plc.Scan();
            Assert.True(plc.Bool("Delayed"));
            Assert.True(plc.Bool("FBI_1.Q"));

            plc.Runtime.Write("Run", PlcOps.False);
            plc.Scan();
            Assert.False(plc.Bool("Delayed"));
            Assert.Equal(0, plc.Int("Elapsed"));
        }

        [Fact]
        public void Ton_WithAFreePresetPin_UsesTheInstanceInitialValue()
        {
            var plc = new PlcHarness(new XefBuilder()
                .Raw(TonSignature)
                .Variable("Run", "BOOL")
                .Variable("Delayed", "BOOL")
                .Variable("FBI_1", "TON", inner: "<instanceElementDesc name=\"PT\"><value>t#300ms</value></instanceElementDesc>")
                .Section("S", Block("FBI_1", "TON", 2, 2, "IN=Run,PT|Q=Delayed,ET"))
                .Project());

            plc.Runtime.Write("Run", PlcOps.True);
            plc.Scan(3);
            Assert.False(plc.Bool("Delayed"));
            plc.Scan();
            Assert.True(plc.Bool("Delayed"));
        }

        [Fact]
        public void Tof_HoldsQForThePresetTimeAfterInFalls()
        {
            var plc = Run(Block("FBI_1", "TOF", 2, 2, "IN=Run,PT=t#500ms|Q=Out,ET"),
                ("Run", "BOOL", null), ("Out", "BOOL", null));

            plc.Runtime.Write("Run", PlcOps.True);
            plc.Scan();
            Assert.True(plc.Bool("Out"));

            plc.Runtime.Write("Run", PlcOps.False);
            plc.Scan(5);
            Assert.True(plc.Bool("Out"));
            plc.Scan();
            Assert.False(plc.Bool("Out"));
        }

        [Fact]
        public void Tp_GivesOnePulseOfThePresetLength()
        {
            var plc = Run(Block("FBI_1", "TP", 2, 2, "IN=Trig,PT=t#300ms|Q=Pulse,ET"),
                ("Trig", "BOOL", null), ("Pulse", "BOOL", null));

            plc.Runtime.Write("Trig", PlcOps.True);
            plc.Scan();
            Assert.True(plc.Bool("Pulse"));
            plc.Runtime.Write("Trig", PlcOps.False);
            plc.Scan(2);
            Assert.True(plc.Bool("Pulse"));
            plc.Scan();
            Assert.False(plc.Bool("Pulse"));
        }

        [Fact]
        public void RTrig_IsTrueForOneScanOnARisingEdge()
        {
            var plc = Run(Block("FBI_1", "R_TRIG", 2, 2, "CLK=In|Q=Pulse"), ("In", "BOOL", null), ("Pulse", "BOOL", null));

            plc.Runtime.Write("In", PlcOps.True);
            plc.Scan();
            Assert.True(plc.Bool("Pulse"));
            plc.Scan();
            Assert.False(plc.Bool("Pulse"));
        }

        [Fact]
        public void FTrig_IsTrueForOneScanOnAFallingEdge()
        {
            var plc = Run(Block("FBI_1", "F_TRIG", 2, 2, "CLK=In|Q=Pulse"), ("In", "BOOL", null), ("Pulse", "BOOL", null));

            plc.Scan();
            Assert.False(plc.Bool("Pulse"));
            plc.Runtime.Write("In", PlcOps.True);
            plc.Scan();
            plc.Runtime.Write("In", PlcOps.False);
            plc.Scan();
            Assert.True(plc.Bool("Pulse"));
            plc.Scan();
            Assert.False(plc.Bool("Pulse"));
        }

        [Fact]
        public void SrIsSetDominant_RsIsResetDominant()
        {
            var plc = Run(
                Block("FBI_1", "SR", 2, 2, "S1=TRUE,R=TRUE|Q1=SrOut") + Block("FBI_2", "RS", 2, 12, "S=TRUE,R1=TRUE|Q1=RsOut"),
                ("SrOut", "BOOL", null), ("RsOut", "BOOL", null)).Scan();

            Assert.True(plc.Bool("SrOut"));
            Assert.False(plc.Bool("RsOut"));
        }

        [Fact]
        public void Ctu_CountsRisingEdges_AndResets()
        {
            var plc = Run(Block("FBI_1", "CTU", 2, 2, "CU=Pulse,R=Clear,PV=2|Q=Done,CV=Count"),
                ("Pulse", "BOOL", null), ("Clear", "BOOL", null), ("Done", "BOOL", null), ("Count", "INT", null));

            for (var i = 0; i < 2; i++)
            {
                plc.Runtime.Write("Pulse", PlcOps.True);
                plc.Scan();
                plc.Runtime.Write("Pulse", PlcOps.False);
                plc.Scan();
            }
            Assert.Equal(2, plc.Int("Count"));
            Assert.True(plc.Bool("Done"));

            plc.Runtime.Write("Clear", PlcOps.True);
            plc.Scan();
            Assert.Equal(0, plc.Int("Count"));
        }

        [Theory]
        [InlineData("GT", "3,2,1", true)]
        [InlineData("GT", "3,3", false)]
        [InlineData("GE", "3,3,1", true)]
        [InlineData("LT", "1,2,3", true)]
        [InlineData("LE", "1,1,0", false)]
        [InlineData("EQ", "5,5,5", true)]
        [InlineData("EQ", "5,5,4", false)]
        [InlineData("NE", "5,4", true)]
        public void Comparisons_CheckSuccessiveInputs(string type, string values, bool expected)
        {
            var inputs = string.Join(",", values.Split(',').Select((v, i) => $"IN{i + 1}={v}"));
            var plc = Run(Block(".1", type, 2, 2, inputs + "|OUT=Result"), ("Result", "BOOL", null)).Scan();

            Assert.Equal(expected, plc.Bool("Result"));
        }

        [Fact]
        public void IntegerDivision_TruncatesTowardZero()
        {
            var plc = Run(Block(".1", "DIV", 2, 2, "IN1=A,IN2=3|OUT=Q"), ("A", "INT", null), ("Q", "INT", null));
            plc.Runtime.Write("A", PlcValue.FromInteger(PlcType.Int, -7));

            plc.Scan();

            Assert.Equal(-2, plc.Int("Q"));
        }

        [Fact]
        public void DivisionByZero_SetsEnoFalse_AndLeavesTheOutputVariable()
        {
            var plc = Run(Block(".1", "DIV", 2, 2, "IN1=10,IN2=D|ENO=Ok,OUT=Q", enEno: true),
                ("D", "INT", null), ("Q", "INT", null), ("Ok", "BOOL", null));
            plc.Runtime.Write("D", PlcValue.FromInteger(PlcType.Int, 5));
            plc.Scan();
            Assert.Equal(2, plc.Int("Q"));
            Assert.True(plc.Bool("Ok"));

            plc.Runtime.Write("D", PlcValue.FromInteger(PlcType.Int, 0));
            plc.Scan();

            Assert.Equal(2, plc.Int("Q"));
            Assert.False(plc.Bool("Ok"));
        }

        [Fact]
        public void IntAddition_WrapsLikeThe16BitController()
        {
            var plc = Run(Block(".1", "ADD", 2, 2, "IN1=A,IN2=1|OUT=Sum"), ("A", "INT", null), ("Sum", "INT", null));
            plc.Runtime.Write("A", PlcValue.FromInteger(PlcType.Int, 32767));

            plc.Scan();

            Assert.Equal(-32768, plc.Int("Sum"));
        }

        [Theory]
        [InlineData(2.5, 2)]
        [InlineData(3.5, 4)]
        [InlineData(1.4, 1)]
        [InlineData(-1.5, -2)]
        public void RealToInt_RoundsHalfToEven(double input, long expected)
        {
            var plc = Run(Block(".1", "REAL_TO_INT", 2, 2, "IN=R|OUT=I"), ("R", "REAL", null), ("I", "INT", null));
            plc.Runtime.Write("R", PlcValue.FromReal(input));

            plc.Scan();

            Assert.Equal(expected, plc.Int("I"));
        }

        [Fact]
        public void SelMuxLimitMax_PickTheRightValue()
        {
            var plc = Run(
                Block(".1", "SEL", 2, 2, "G=TRUE,IN0=10,IN1=20|OUT=Sel") +
                Block(".2", "MUX", 2, 12, "K=2,IN0=5,IN1=6,IN2=7|OUT=Mux") +
                Block(".3", "LIMIT", 2, 22, "MN=0,IN=150,MX=100|OUT=Lim") +
                Block(".4", "MAX", 2, 32, "IN1=3,IN2=9,IN3=4|OUT=Max"),
                ("Sel", "INT", null), ("Mux", "INT", null), ("Lim", "INT", null), ("Max", "INT", null)).Scan();

            Assert.Equal(20, plc.Int("Sel"));
            Assert.Equal(7, plc.Int("Mux"));
            Assert.Equal(100, plc.Int("Lim"));
            Assert.Equal(9, plc.Int("Max"));
        }

        [Fact]
        public void WordPacking_RoundTripsARealThroughTwoWords()
        {
            var plc = Run(
                Block(".1", "REAL_AS_WORD", 2, 2, "IN=12.5|LOW=Lo,HIGH=Hi") +
                Block(".2", "WORD_AS_REAL", 2, 12, "LOW=Lo,HIGH=Hi|OUT=Back") +
                Block(".3", "WORD_AS_DWORD", 2, 22, "LOW=16#5678,HIGH=16#1234|OUT=Dw"),
                ("Lo", "WORD", null), ("Hi", "WORD", null), ("Back", "REAL", null), ("Dw", "DWORD", null)).Scan();

            Assert.Equal(12.5, plc.Real("Back"));
            Assert.Equal(0x12345678, plc.Int("Dw"));
        }

        [Fact]
        public void BitToWord_AndWordToBit_UseBit0AsTheLeastSignificant()
        {
            var plc = Run(
                Block(".1", "BIT_TO_WORD", 2, 2, "BIT0=TRUE,BIT15=TRUE|OUT=W") +
                Block(".2", "WORD_TO_BIT", 2, 12, "IN=W|BIT0=B0,BIT1=B1,BIT15=B15"),
                ("W", "WORD", null), ("B0", "BOOL", null), ("B1", "BOOL", null), ("B15", "BOOL", null)).Scan();

            Assert.Equal(0x8001, plc.Int("W"));
            Assert.True(plc.Bool("B0"));
            Assert.False(plc.Bool("B1"));
            Assert.True(plc.Bool("B15"));
        }

        [Fact]
        public void UserFunctionBlock_IsNotSimulated_AndKeepsItsOutputs()
        {
            var plc = new PlcHarness(new XefBuilder()
                .Raw(@"<FBSource nameOfFBType=""xDIG101""><crypted>00</crypted><ExternalToolsOnly>
                    <inputParameters><variables name=""fInput"" typeName=""BOOL""/></inputParameters>
                    <outputParameters><variables name=""sOn"" typeName=""BOOL""/></outputParameters>
                  </ExternalToolsOnly></FBSource>")
                .Variable("Dig1", "xDIG101")
                .Variable("On", "BOOL")
                .Section("S", Block("Dig1", "xDIG101", 2, 2, "fInput=TRUE|sOn=On"))
                .Project()).Scan();

            var block = plc.BlockNamed("Dig1");
            Assert.Equal(PlcBlockKind.UserFunctionBlock, block.Kind);
            Assert.False(block.IsSimulated);
            Assert.False(plc.Bool("On"));
            Assert.True(plc.Bool("Dig1.fInput"));
        }

        [Fact]
        public void UnknownStandardType_IsInert()
        {
            var plc = Run(Block("FBI_1", "PID_FANCY", 2, 2, "IN=5|OUT=Y"), ("Y", "INT", null)).Scan();

            Assert.Equal(PlcBlockKind.Unsupported, plc.BlockNamed("FBI_1").Kind);
            Assert.Equal(0, plc.Int("Y"));
        }

        private const string TonSignature = @"<EFBSource nameOfEFBType=""TON""><ExternalToolsOnly>
            <inputParameters><variables name=""IN"" typeName=""BOOL""/><variables name=""PT"" typeName=""TIME""/></inputParameters>
            <outputParameters><variables name=""Q"" typeName=""BOOL""/><variables name=""ET"" typeName=""TIME""/></outputParameters>
          </ExternalToolsOnly></EFBSource>";
    }
}
