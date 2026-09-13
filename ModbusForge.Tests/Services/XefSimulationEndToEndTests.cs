using System.Diagnostics;
using System.Threading;
using System.Xml.Linq;
using ModbusForge.Core.Xef;
using ModbusForge.Data;
using ModbusForge.Models;
using Xunit;

namespace ModbusForge.Tests.Services
{
    /// <summary>
    /// The end-to-end chain the PLC screen runs: parse a real-shaped XEF → translate it to the
    /// visual-node graph → execute it with <see cref="PlcSimulationRunner"/> → verify the output
    /// tag flips in the shared DataStore when the input tags change. This is the "true simulation"
    /// assertion, not a hand-built graph.
    /// </summary>
    public sealed class XefSimulationEndToEndTests
    {
        // A minimal, deterministic FBD program: two BOOL inputs (Start=coil 10, RunEnable=coil 11)
        // feed an AND whose output (RunOut) writes coil 20. Both inputs must be TRUE for the AND
        // to fire. No timers, so it is cycle-deterministic.
        private const string Program = @"
<RSLogixContent>
  <contentHeader name=""E2E""/>
  <dataBlock>
    <variables name=""Start"" typeName=""BOOL"" topologicalAddress=""10"">
      <variableInit value=""true""/>
    </variables>
    <variables name=""RunEnable"" typeName=""BOOL"" topologicalAddress=""11"">
      <variableInit value=""true""/>
    </variables>
    <variables name=""RunOut"" typeName=""BOOL"" topologicalAddress=""20"">
      <variableInit value=""false""/>
    </variables>
  </dataBlock>
  <program>
    <identProgram name=""Main""/>
    <FBDSource>
      <networkFBD>
        <FFBBlock instanceName=""AND1"" typeName=""AND"" enEnO=""false"">
          <objPosition posX=""200"" posY=""120""/>
          <inputVariable formalParameter=""IN1"" effectiveParameter=""Start""/>
          <inputVariable formalParameter=""IN2"" effectiveParameter=""RunEnable""/>
          <outputVariable formalParameter=""Q"" effectiveParameter=""RunOut""/>
        </FFBBlock>
      </networkFBD>
    </FBDSource>
  </program>
</RSLogixContent>";

        private static XefProject Parse() => XefParser.Parse(XElement.Parse(Program));

        private static bool WaitUntil(Func<bool> condition, int timeoutMs = 5000)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (condition()) return true;
                Thread.Sleep(10);
            }
            return condition();
        }

        [Fact]
        public void XefToGraphToRunner_RunsAndFlipsOutputTag()
        {
            var project = Parse();
            var program = project.FindProgram("Main");
            Assert.NotNull(program);

            // 1. Translate the parsed program into the visual-node graph. The AND's two tag inputs
            //    (Start, Stop) and tag output (RunOut) become helper I/O nodes wired into the AND,
            //    because the engine's AND block has no address bindings of its own.
            var graph = XefToSimulationConfig.Translate(program, project);
            Assert.False(graph.IsStProgram);
            Assert.Empty(graph.UnsupportedBlocks);

            var andNode = graph.Config.Nodes.First(n => n.Id == "AND1");
            Assert.NotNull(andNode);

            // The RunOut helper (an Output block) must be bound to coil 20.
            var runOutHelper = graph.Config.Nodes.First(n => n.Name == "RunOut");
            Assert.True(runOutHelper.ElementType == PlcElementType.OutputBool
                             || runOutHelper.ElementType == PlcElementType.OutputInt,
                $"RunOut helper is {runOutHelper.ElementType}");
            Assert.NotNull(runOutHelper.OutputAddress);
            Assert.Equal(PlcArea.Coil, runOutHelper.OutputAddress!.Area);
            Assert.Equal(20, runOutHelper.OutputAddress.Address);

            // The Start / RunEnable helpers must be bound to coils 10 / 11.
            var startHelper = graph.Config.Nodes.First(n => n.Name == "Start");
            Assert.Equal(PlcArea.Coil, startHelper.Input1Address!.Area);
            Assert.Equal(10, startHelper.Input1Address.Address);
            var enableHelper = graph.Config.Nodes.First(n => n.Name == "RunEnable");
            Assert.Equal(PlcArea.Coil, enableHelper.Input1Address!.Area);
            Assert.Equal(11, enableHelper.Input1Address.Address);

            // 2. Run the graph against a fresh DataStore seeded from the XEF variableInit values
            //    (Start=true, RunEnable=true → AND true → RunOut should become true).
            var dataStore = new DataStore();
            dataStore.CoilDiscretes[10] = true;  // Start
            dataStore.CoilDiscretes[11] = true;  // RunEnable

            using var runner = new PlcSimulationRunner(graph.Config, dataStore, 20);
            runner.Start();

            // The output tag must flip to TRUE within a few cycles.
            Assert.True(WaitUntil(() =>
            {
                lock (dataStore) { return dataStore.CoilDiscretes[20]; }
            }), "RunOut (coil 20) should be TRUE when Start=true and RunEnable=true");

            // The live-value snapshot must show the AND node driving TRUE.
            Assert.True(WaitUntil(() =>
            {
                var v = runner.GetLatestNodeValues();
                return v.TryGetValue("AND1", out var x) && x == 1.0;
            }), "AND1 should report TRUE in the live-value snapshot");

            // 3. Drop RunEnable → AND must fall to FALSE and the output tag clear.
            lock (dataStore) { dataStore.CoilDiscretes[11] = false; }
            Assert.True(WaitUntil(() =>
            {
                lock (dataStore) { return !dataStore.CoilDiscretes[20]; }
            }), "RunOut (coil 20) should be FALSE when RunEnable=false");

            runner.Stop();
            Assert.False(runner.IsRunning);
        }

        [Fact]
        public void XefToGraph_SeedsFromVariableInit_And_RunnerHonoursSeed()
        {
            var project = Parse();
            var program = project.FindProgram("Main");
            var graph = XefToSimulationConfig.Translate(program, project);

            // Simulate the VM's SeedDataStore: apply the variableInit values to a fresh store.
            var dataStore = new DataStore();
            foreach (var v in project.Variables)
            {
                var a = v.TryParseAddress();
                if (a == null) continue;
                if (a.Value.Area == PlcArea.Coil)
                {
                    dataStore.CoilDiscretes[a.Value.Address] =
                        v.Value?.Equals("true", System.StringComparison.OrdinalIgnoreCase) == true
                        || v.Value == "1";
                }
            }

            // The seed must put both Start (coil 10) and RunEnable (coil 11) TRUE.
            Assert.True(dataStore.CoilDiscretes[10]);
            Assert.True(dataStore.CoilDiscretes[11]);

            // Running from the seed alone (no manual writes) must drive RunOut TRUE.
            using var runner = new PlcSimulationRunner(graph.Config, dataStore, 20);
            runner.Start();
            Assert.True(WaitUntil(() =>
            {
                lock (dataStore) { return dataStore.CoilDiscretes[20]; }
            }), "Seeded Start=true + RunEnable=true must drive RunOut TRUE");
            runner.Stop();
        }
    }
}
