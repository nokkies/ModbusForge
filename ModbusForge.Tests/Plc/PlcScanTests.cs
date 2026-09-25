using System.Linq;
using ModbusForge.Core.Plc;
using static ModbusForge.Tests.Plc.XefBuilder;

namespace ModbusForge.Tests.Plc
{
    /// <summary>
    /// What one scan runs and in which order: sections in task order (FAST before
    /// MAST, activation conditions honoured), blocks by links, "Execute after" and
    /// position; plus cold start initial values and the system bits.
    /// </summary>
    public class PlcScanTests
    {
        [Fact]
        public void LinkedBlocks_RunInSignalFlowOrder_WhateverTheirPosition()
        {
            // The consumer sits above and left of its producer, yet sees this scan's value.
            var plc = new PlcHarness(new XefBuilder()
                .Variable("Out", "INT")
                .Section("S",
                    Block(".2", "MOVE", 2, 2, "IN|OUT=Out") +
                    Block(".1", "ADD", 20, 20, "IN1=2,IN2=3|OUT") +
                    Link(".1", "OUT", ".2", "IN"))
                .Project()).Scan();

            Assert.Equal(5, plc.Int("Out"));
            Assert.Equal(new[] { ".1", ".2" }, plc.Runtime.Project.Sections.Single().Blocks.Select(b => b.InstanceName));
        }

        [Fact]
        public void UnlinkedBlocks_RunTopToBottom()
        {
            // The block below writes X; the block above reads it, so it sees last scan's value.
            var plc = new PlcHarness(new XefBuilder()
                .Variable("X", "INT")
                .Variable("Seen", "INT")
                .Section("S",
                    Block(".1", "MOVE", 2, 20, "IN=7|OUT=X") +
                    Block(".2", "MOVE", 30, 2, "IN=X|OUT=Seen"))
                .Project());

            plc.Scan();
            Assert.Equal(0, plc.Int("Seen"));
            plc.Scan();
            Assert.Equal(7, plc.Int("Seen"));
        }

        [Fact]
        public void ExecuteAfter_OverridesThePositionOrder()
        {
            var plc = new PlcHarness(new XefBuilder()
                .Variable("X", "INT")
                .Variable("Seen", "INT")
                .Section("S",
                    Block(".1", "MOVE", 2, 20, "IN=7|OUT=X") +
                    Block(".2", "MOVE", 30, 2, "IN=X|OUT=Seen", execAfter: ".1"))
                .Project()).Scan();

            Assert.Equal(7, plc.Int("Seen"));
        }

        [Fact]
        public void Sections_RunInTaskOrder_FastBeforeMast()
        {
            var xef = new XefBuilder()
                .Variable("Trace", "INT")
                .Variable("SeenByMast", "INT")
                .Section("MastFirst", Block(".1", "MOVE", 2, 2, "IN=Trace|OUT=SeenByMast"))
                .Section("FastOne", Block(".1", "MOVE", 2, 2, "IN=42|OUT=Trace"), task: "FAST");
            var plc = new PlcHarness(xef.Project(
                "<taskDesc task=\"MAST\" taskType=\"cyclic\"><sectionDesc name=\"MastFirst\"/></taskDesc>" +
                "<taskDesc task=\"FAST\" taskType=\"periodic\"><sectionDesc name=\"FastOne\"/></taskDesc>")).Scan();

            Assert.Equal(new[] { "FAST", "MAST" }, plc.Runtime.Project.Tasks.Select(t => t.Name));
            Assert.Equal(42, plc.Int("SeenByMast"));
        }

        [Fact]
        public void SectionWithAFalseActivationCondition_DoesNotRun()
        {
            var plc = new PlcHarness(new XefBuilder()
                .Variable("Enable", "BOOL")
                .Variable("Out", "INT")
                .Section("S", Block(".1", "MOVE", 2, 2, "IN=9|OUT=Out"), condition: "Enable")
                .Project());

            plc.Scan();
            Assert.Equal(0, plc.Int("Out"));

            plc.Runtime.Write("Enable", PlcOps.True);
            plc.Scan();
            Assert.Equal(9, plc.Int("Out"));
        }

        [Fact]
        public void ColdStart_AppliesDeclaredInitialValues()
        {
            var plc = new PlcHarness(new XefBuilder()
                .Raw(@"<DDTSource DDTName=""tLimits""><structure>
                    <variables name=""Low"" typeName=""INT""/><variables name=""High"" typeName=""INT""/>
                  </structure></DDTSource>")
                .Variable("Counter", "INT", init: "5")
                .Variable("Setpoint", "INT", "%MW10", init: "250")
                .Variable("Limits", "tLimits", inner: "<instanceElementDesc name=\"High\"><value>90</value></instanceElementDesc>")
                .Variable("Table", "ARRAY[0..2] OF INT", inner: "<instanceElementDesc name=\"[1]\"><value>11</value></instanceElementDesc>")
                .Project()).Scan();

            Assert.Equal(5, plc.Int("Counter"));
            Assert.Equal(250, plc.Store.HoldingRegisters[10]);
            Assert.Equal(90, plc.Int("Limits.High"));
            Assert.Equal(11, plc.Int("Table[1]"));
        }

        [Fact]
        public void LocatedInitialValues_ReachAStoreBoundAfterTheFirstScan()
        {
            // Run offline first, then against the started Modbus server's store.
            var plc = new PlcHarness(new XefBuilder().Variable("Setpoint", "INT", "%MW10", init: "250").Project());
            plc.Scan();
            var server = new ModbusForge.Data.DataStore();

            plc.Runtime.Scan(server, System.TimeSpan.FromMilliseconds(100));

            Assert.Equal(250, server.HoldingRegisters[10]);
        }

        [Fact]
        public void FirstScanBits_AreSetOnlyInTheFirstCycle()
        {
            var plc = new PlcHarness(new XefBuilder()
                .Variable("Cold", "BOOL")
                .Variable("FirstTask", "BOOL")
                .Section("S", Block(".1", "MOVE", 2, 2, "IN=%S13|OUT=Cold") + Block(".2", "MOVE", 2, 12, "IN=%S21|OUT=FirstTask"))
                .Project());

            plc.Scan();
            Assert.True(plc.Bool("Cold"));
            Assert.True(plc.Bool("FirstTask"));

            plc.Scan();
            Assert.False(plc.Bool("Cold"));
            Assert.False(plc.Bool("FirstTask"));

            // Going back to RUN is a first task cycle, not a cold start.
            plc.Runtime.Start();
            plc.Scan();
            Assert.False(plc.Bool("Cold"));
            Assert.True(plc.Bool("FirstTask"));
        }

        [Fact]
        public void OneSecondTimeBase_Toggles()
        {
            var plc = new PlcHarness(new XefBuilder()
                .Variable("Clock", "BOOL")
                .Section("S", Block(".1", "MOVE", 2, 2, "IN=%S6|OUT=Clock"))
                .Project());

            var seen = Enumerable.Range(0, 20).Select(_ => plc.Scan().Bool("Clock")).ToList();

            Assert.Contains(true, seen);
            Assert.Contains(false, seen);
        }
    }
}
