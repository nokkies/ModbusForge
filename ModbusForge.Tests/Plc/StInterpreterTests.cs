using ModbusForge.Core.Plc;

namespace ModbusForge.Tests.Plc
{
    /// <summary>
    /// ST sections run each scan in task order, with IEC 61131-3 statements and
    /// operators, the standard functions, function block calls and Unity's RE/FE.
    /// </summary>
    public class StInterpreterTests
    {
        private static PlcHarness Run(string source, params (string Name, string Type, string? Address)[] variables)
            => RunWith(new XefBuilder(), source, variables);

        private static PlcHarness RunWith(XefBuilder xef, string source, params (string Name, string Type, string? Address)[] variables)
        {
            foreach (var (name, type, address) in variables) xef.Variable(name, type, address);
            var project = xef.StSection("Code", source).Project();
            Assert.Empty(project.Warnings);
            return new PlcHarness(project);
        }

        [Fact]
        public void Operators_FollowIecPrecedence()
        {
            var plc = Run("a := 2 + 3 * 4; b := (2 + 3) * 4; c := 10 - 4 - 3; d := NOT TRUE OR TRUE; e := 7 MOD 3; f := -2 * 3;",
                ("a", "INT", null), ("b", "INT", null), ("c", "INT", null), ("d", "BOOL", null), ("e", "INT", null), ("f", "INT", null)).Scan();

            Assert.Equal(14, plc.Int("a"));
            Assert.Equal(20, plc.Int("b"));
            Assert.Equal(3, plc.Int("c"));
            Assert.True(plc.Bool("d"));
            Assert.Equal(1, plc.Int("e"));
            Assert.Equal(-6, plc.Int("f"));
        }

        [Fact]
        public void IfElsifElse_PicksTheFirstTrueBranch()
        {
            var plc = Run(@"
IF x > 10 THEN r := 1;
ELSIF x > 5 THEN r := 2;
ELSE r := 3;
END_IF;", ("x", "INT", null), ("r", "INT", null));

            plc.Runtime.Write("x", PlcValue.FromInteger(PlcType.Int, 7));
            plc.Scan();
            Assert.Equal(2, plc.Int("r"));

            plc.Runtime.Write("x", PlcValue.FromInteger(PlcType.Int, 1));
            plc.Scan();
            Assert.Equal(3, plc.Int("r"));
        }

        [Fact]
        public void KeywordsAreCaseInsensitive_AndCommentsDoNotNest()
        {
            var plc = Run(@"(* outer (* inner *) r := 1;
if TRUE then r := r + 10; end_if;", ("r", "INT", null)).Scan();

            Assert.Equal(11, plc.Int("r"));
        }

        [Fact]
        public void ForLoop_WalksAnArray_AndExitLeavesIt()
        {
            var plc = Run(@"
Total := 0;
FOR i := 1 TO 5 DO
    Total := Total + Tab[i];
END_FOR;
FOR i := 5 TO 1 BY -1 DO
    IF Tab[i] = 30 THEN EXIT; END_IF;
    Last := i;
END_FOR;", ("Tab", "ARRAY[1..5] OF INT", null), ("Total", "INT", null), ("i", "INT", null), ("Last", "INT", null));
            for (var k = 1; k <= 5; k++) plc.Runtime.Write($"Tab[{k}]", PlcValue.FromInteger(PlcType.Int, k * 10));

            plc.Scan();

            Assert.Equal(150, plc.Int("Total"));
            Assert.Equal(4, plc.Int("Last"));
        }

        [Fact]
        public void WhileAndRepeat_LoopUntilTheirCondition()
        {
            var plc = Run(@"
n := 0; WHILE n < 7 DO n := n + 2; END_WHILE;
m := 0; REPEAT m := m + 5; UNTIL m >= 12 END_REPEAT;", ("n", "INT", null), ("m", "INT", null)).Scan();

            Assert.Equal(8, plc.Int("n"));
            Assert.Equal(15, plc.Int("m"));
        }

        [Fact]
        public void Case_MatchesValuesRangesAndElse()
        {
            var plc = Run(@"
CASE s OF
    1, 2: r := 10;
    3..5: r := 20;
ELSE r := 99;
END_CASE;", ("s", "INT", null), ("r", "INT", null));

            plc.Runtime.Write("s", PlcValue.FromInteger(PlcType.Int, 4));
            plc.Scan();
            Assert.Equal(20, plc.Int("r"));
            plc.Runtime.Write("s", PlcValue.FromInteger(PlcType.Int, 9));
            plc.Scan();
            Assert.Equal(99, plc.Int("r"));
        }

        [Fact]
        public void DirectAddresses_ReadAndWriteTheModbusTables()
        {
            var plc = Run("%MW10 := %MW5 + 1; %M3 := %I2; Status.3 := TRUE; FirstScan := %s13;",
                ("Status", "WORD", "%MW20"), ("FirstScan", "BOOL", null));
            plc.Store.HoldingRegisters[5] = 41;
            plc.Store.InputDiscretes[2] = true;

            plc.Scan();

            Assert.Equal(42, plc.Store.HoldingRegisters[10]);
            Assert.True(plc.Store.CoilDiscretes[3]);
            Assert.Equal(8, plc.Store.HoldingRegisters[20]);
            Assert.True(plc.Bool("FirstScan"));
        }

        [Fact]
        public void Functions_TakeArgumentsByPositionOrName()
        {
            var xef = new XefBuilder().Raw(@"<EFSource nameOfEFType=""LIMIT""><ExternalToolsOnly><inputParameters>
                <variables name=""MN"" typeName=""ANY""/><variables name=""IN"" typeName=""ANY""/><variables name=""MX"" typeName=""ANY""/>
              </inputParameters><outputParameters><variables name=""OUT"" typeName=""ANY""/></outputParameters></ExternalToolsOnly></EFSource>
              <EFSource nameOfEFType=""MAX""><ExternalToolsOnly><inputParameters>
                <variables name=""IN1"" typeName=""ANY""/><variables name=""nin"" typeName=""DWORD""/>
              </inputParameters><outputParameters><variables name=""OUT"" typeName=""ANY""/></outputParameters></ExternalToolsOnly></EFSource>");
            var plc = RunWith(xef, @"
r := INT_TO_REAL(i) / 4.0;
lim := LIMIT(0, i, 5);
big := MAX(3, i, 9, 4);
WORD_AS_BYTE(IN := w, LOW => lo, HIGH => hi);
SET (OUT => flag);",
                ("i", "INT", null), ("r", "REAL", null), ("lim", "INT", null), ("big", "INT", null),
                ("w", "WORD", null), ("lo", "BYTE", null), ("hi", "BYTE", null), ("flag", "BOOL", null));
            plc.Runtime.Write("i", PlcValue.FromInteger(PlcType.Int, 10));
            plc.Runtime.Write("w", PlcValue.FromInteger(PlcType.Word, 0x1234));

            plc.Scan();

            Assert.Equal(2.5, plc.Real("r"));
            Assert.Equal(5, plc.Int("lim"));
            Assert.Equal(10, plc.Int("big"));
            Assert.Equal(0x34, plc.Int("lo"));
            Assert.Equal(0x12, plc.Int("hi"));
            Assert.True(plc.Bool("flag"));
        }

        [Fact]
        public void InformalProcedureCall_BindsOutputsAfterTheInputs()
        {
            var xef = new XefBuilder().Raw(@"<EFSource nameOfEFType=""WORD_AS_BYTE""><ExternalToolsOnly>
                <inputParameters><variables name=""IN"" typeName=""WORD""/></inputParameters>
                <outputParameters><variables name=""LOW"" typeName=""BYTE""/><variables name=""HIGH"" typeName=""BYTE""/></outputParameters>
              </ExternalToolsOnly></EFSource>");
            var plc = RunWith(xef, "WORD_AS_BYTE(w, lo, hi);", ("w", "WORD", null), ("lo", "BYTE", null), ("hi", "BYTE", null));
            plc.Runtime.Write("w", PlcValue.FromInteger(PlcType.Word, 0xABCD));

            plc.Scan();

            Assert.Equal(0xCD, plc.Int("lo"));
            Assert.Equal(0xAB, plc.Int("hi"));
        }

        [Fact]
        public void InformalInstanceCall_SkipsEmptyFields_AndBindsOutputsLast()
        {
            // CTU: inputs CU, R, PV; outputs Q, CV (35006144: MY_COUNT (var1, , 100, out, current)).
            var xef = new XefBuilder().Raw(@"<EFBSource nameOfEFBType=""CTU""><ExternalToolsOnly>
                <inputParameters><variables name=""CU"" typeName=""BOOL""/><variables name=""R"" typeName=""BOOL""/><variables name=""PV"" typeName=""INT""/></inputParameters>
                <outputParameters><variables name=""Q"" typeName=""BOOL""/><variables name=""CV"" typeName=""INT""/></outputParameters>
              </ExternalToolsOnly></EFBSource>");
            var plc = RunWith(xef, "C1(Pulse, , 2, Done, Count);",
                ("C1", "CTU", null), ("Pulse", "BOOL", null), ("Done", "BOOL", null), ("Count", "INT", null));

            for (var i = 0; i < 2; i++)
            {
                plc.Runtime.Write("Pulse", PlcOps.True);
                plc.Scan();
                plc.Runtime.Write("Pulse", PlcOps.False);
                plc.Scan();
            }

            Assert.Equal(2, plc.Int("Count"));
            Assert.True(plc.Bool("Done"));
        }

        [Fact]
        public void TableFunctions_FillSumAndMeasureArrays()
        {
            var plc = Run("MOVE_INT_ARINT(4, Tab); Total := SUM_ARINT(Tab); Size := LENGTH_ARINT(Tab);",
                ("Tab", "ARRAY[0..4] OF INT", null), ("Total", "INT", null), ("Size", "INT", null)).Scan();

            Assert.Equal(4, plc.Int("Tab[3]"));
            Assert.Equal(20, plc.Int("Total"));
            Assert.Equal(5, plc.Int("Size"));
        }

        [Fact]
        public void FunctionBlockCall_KeepsItsStateBetweenScans()
        {
            var plc = RunWith(new XefBuilder().Raw(TonSignature), "T1(IN := Run, PT := t#300ms); Done := T1.Q; Elapsed := T1.ET;",
                ("T1", "TON", null), ("Run", "BOOL", null), ("Done", "BOOL", null), ("Elapsed", "TIME", null));

            plc.Runtime.Write("Run", PlcOps.True);
            plc.Scan(3);
            Assert.False(plc.Bool("Done"));
            Assert.Equal(200, plc.Int("Elapsed"));
            plc.Scan();
            Assert.True(plc.Bool("Done"));
        }

        [Fact]
        public void RisingEdge_OnAnEboolWrittenEveryScan_IsOneScanLong()
        {
            var plc = Run("Btn := In; IF RE(Btn) THEN Count := Count + 1; END_IF;",
                ("Btn", "EBOOL", null), ("In", "BOOL", null), ("Count", "INT", null));

            plc.Runtime.Write("In", PlcOps.True);
            plc.Scan(3);
            Assert.Equal(1, plc.Int("Count"));

            plc.Runtime.Write("In", PlcOps.False);
            plc.Scan();
            plc.Runtime.Write("In", PlcOps.True);
            plc.Scan();
            Assert.Equal(2, plc.Int("Count"));
        }

        [Fact]
        public void FallingEdge_OnADiscreteInput_IsOneScanLong()
        {
            var plc = Run("IF FE(%I4) THEN Count := Count + 1; END_IF;", ("Count", "INT", null));
            plc.Store.InputDiscretes[4] = true;
            plc.Scan(2);

            plc.Store.InputDiscretes[4] = false;
            plc.Scan(3);

            Assert.Equal(1, plc.Int("Count"));
        }

        [Fact]
        public void DivisionByZero_CountsAnError_AndGivesZero()
        {
            var plc = Run("q := 10 / d; after := 1;", ("q", "INT", null), ("d", "INT", null), ("after", "INT", null)).Scan();

            Assert.Equal(0, plc.Int("q"));
            Assert.Equal(1, plc.Int("after"));
            Assert.Equal(1, plc.Runtime.StErrors);
        }

        [Fact]
        public void ALoopThatNeverEnds_IsCutOffWithoutHangingTheScan()
        {
            var plc = Run("WHILE TRUE DO n := n + 1; END_WHILE; after := 1;", ("n", "DINT", null), ("after", "INT", null)).Scan();

            Assert.Equal(1, plc.Int("after"));
            Assert.Equal(1, plc.Runtime.StErrors);
        }

        [Fact]
        public void AStatementThatDoesNotCompile_IsReported_AndTheRestRuns()
        {
            var xef = new XefBuilder().Variable("a", "INT").Variable("b", "INT");
            var project = xef.StSection("Code", "a := 1; b := Undeclared + 1; b := 2;").Project();
            var plc = new PlcHarness(project).Scan();

            Assert.Contains(project.Warnings, w => w.Contains("Undeclared"));
            Assert.Equal(1, plc.Int("a"));
            Assert.Equal(2, plc.Int("b"));
        }

        private const string TonSignature = @"<EFBSource nameOfEFBType=""TON""><ExternalToolsOnly>
            <inputParameters><variables name=""IN"" typeName=""BOOL""/><variables name=""PT"" typeName=""TIME""/></inputParameters>
            <outputParameters><variables name=""Q"" typeName=""BOOL""/><variables name=""ET"" typeName=""TIME""/></outputParameters>
          </ExternalToolsOnly></EFBSource>";
    }
}
