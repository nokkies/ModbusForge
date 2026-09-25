using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ModbusForge.Models;
using ModbusForge.Services;
using static ModbusForge.Tests.Plc.XefBuilder;

namespace ModbusForge.Tests.Plc
{
    /// <summary>
    /// The PLC tab's Run service: scans the project and tags the blocks on screen
    /// with their values, one tag per value.
    /// </summary>
    public class PlcRuntimeServiceTests
    {
        private static readonly TimeSpan Scan = TimeSpan.FromMilliseconds(100);

        private static (PlcRuntimeService Service, ObservableCollection<VisualNode> Nodes) Running(string network, params (string Name, string Type)[] variables)
        {
            var xef = new XefBuilder();
            foreach (var (name, type) in variables) xef.Variable(name, type);
            var result = new PlcXmlImporter().ImportFromXml(xef.Section("S", network).Build(), "test.xef");
            var nodes = new ObservableCollection<VisualNode>(result.Sections.Single().Nodes);
            var service = new PlcRuntimeService();
            service.Load(result.Project);
            service.SetScanIntervalMs(10_000);
            service.Start(new VisualNodeEditorConfig { Nodes = nodes, ScanIntervalMs = 10_000 });
            return (service, nodes);
        }

        private static VisualNode Node(ObservableCollection<VisualNode> nodes, string type)
            => nodes.Single(n => n.Plc?.TypeName == type);

        [Fact]
        public void ALinkShowsItsValueOnce_AtTheSourceOutput()
        {
            var (service, nodes) = Running(
                Block(".1", "AND", 2, 2, "IN1=A,IN2=TRUE|OUT") +
                Block(".2", "NOT", 20, 2, "IN|OUT=B") +
                Link(".1", "OUT", ".2", "IN"),
                ("A", "BOOL"), ("B", "BOOL"));
            using var _ = service;

            service.Tick(Scan);

            Assert.Equal("FALSE", Node(nodes, "AND").PlcLive!.Outputs["OUT"]);
            Assert.False(Node(nodes, "NOT").PlcLive!.Inputs.ContainsKey("IN"));
            Assert.Equal("FALSE", Node(nodes, "AND").PlcLive!.Inputs["IN1"]);
        }

        [Fact]
        public void Stop_ClearsTheTags()
        {
            var (service, nodes) = Running(Block(".1", "MOVE", 2, 2, "IN=A|OUT=B"), ("A", "INT"), ("B", "INT"));
            using var _ = service;
            service.Tick(Scan);
            Assert.NotNull(nodes.Single().PlcLive);

            service.Stop();

            Assert.Null(nodes.Single().PlcLive);
            Assert.False(service.IsRunning);
        }

        [Fact]
        public void TickWhileStopped_DoesNotScan()
        {
            var (service, _) = Running(Block(".1", "MOVE", 2, 2, "IN=5|OUT=B"), ("B", "INT"));
            using var _ = service;
            service.Stop();

            service.Tick(Scan);

            Assert.Equal(0, service.Runtime!.ScanCount);
        }

        // A DFB with an ST section the runtime runs and an LD section it cannot.
        private const string MixedDfb = @"<FBSource nameOfFBType=""Mixed"">
  <inputParameters><variables name=""A"" typeName=""BOOL""/></inputParameters>
  <outputParameters><variables name=""Q"" typeName=""BOOL""/></outputParameters>
  <FBProgram name=""Code""><STSource>Q := A;</STSource></FBProgram>
  <FBProgram name=""Rungs""><LDSource></LDSource></FBProgram>
</FBSource>";

        private static (PlcRuntimeService Service, List<string> Reports) RunningWithReports(XefBuilder xef)
        {
            var result = new PlcXmlImporter().ImportFromXml(xef.Build(), "test.xef");
            var reports = new List<string>();
            var service = new PlcRuntimeService();
            service.ProblemReported += reports.Add;
            service.Load(result.Project);
            service.SetScanIntervalMs(10_000);
            service.Start(new VisualNodeEditorConfig
            {
                Nodes = new ObservableCollection<VisualNode>(result.Sections.SelectMany(s => s.Nodes)),
                ScanIntervalMs = 10_000
            });
            return (service, reports);
        }

        [Fact]
        public void DfbCodeTheRuntimeCannotRun_IsReportedOnce()
        {
            var (service, reports) = RunningWithReports(new XefBuilder().Raw(MixedDfb)
                .Variable("M1", "Mixed").Variable("X", "BOOL")
                .Section("S", Block("M1", "Mixed", 2, 2, "A=TRUE|Q=X")));
            using var _ = service;

            service.Tick(Scan);
            service.Tick(Scan);

            var report = Assert.Single(reports, m => m.Contains("Rungs"));
            Assert.Contains("Mixed", report);
            Assert.Contains("LD", report);
            Assert.True(service.Runtime!.Read("X").AsBool()); // the ST section still ran
        }

        [Fact]
        public void AnStRunTimeError_IsReportedOnce()
        {
            var (service, reports) = RunningWithReports(new XefBuilder()
                .Variable("Y", "INT").Variable("Z", "INT")
                .StSection("Code", "Y := 10 / Z;"));
            using var _ = service;

            service.Tick(Scan);
            service.Tick(Scan);
            service.Tick(Scan);

            Assert.Equal(3, service.Runtime!.StErrors);
            Assert.Single(reports, m => m.Contains("ST") && m.Contains("error"));
        }
    }
}
