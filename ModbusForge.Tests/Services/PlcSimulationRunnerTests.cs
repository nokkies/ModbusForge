using System;
using System.Diagnostics;
using System.Threading;
using ModbusForge.Data;
using ModbusForge.Core.Xef;
using ModbusForge.Models;
using Xunit;

namespace ModbusForge.Tests.Services
{
    /// <summary>
    /// End-to-end tests for <see cref="PlcSimulationRunner"/>: a hand-built visual graph
    /// (two InputBool nodes -> AND, plus an Unsupported passthrough downstream) run against a
    /// shared DataStore, driven on a short scan interval.
    /// </summary>
    public class PlcSimulationRunnerTests
    {
        private const int ScanIntervalMs = 20;
        private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);

        private static VisualNode NewNode(string id, string name, PlcElementType elementType)
        {
            return new VisualNode
            {
                Id = id,
                Name = name,
                ElementType = elementType
            };
        }

        private static VisualNodeEditorConfig BuildAndGraph()
        {
            var config = new VisualNodeEditorConfig { ScanIntervalMs = ScanIntervalMs };

            // IN_BOOL reads coil 1, drives AND.Input1.
            var inputNode = NewNode("in1", "IN1", PlcElementType.InputBool);
            inputNode.Input1Address = new PlcAddressReference { Area = PlcArea.Coil, Address = 1 };

            // IN_BOOL reads coil 2, drives AND.Input2.
            var input2Node = NewNode("in2", "IN2", PlcElementType.InputBool);
            input2Node.Input1Address = new PlcAddressReference { Area = PlcArea.Coil, Address = 2 };

            // AND: both inputs wired, output Q bound to coil 10.
            var andNode = NewNode("and1", "AND1", PlcElementType.AND);
            andNode.OutputAddress = new PlcAddressReference { Area = PlcArea.Coil, Address = 10 };

            // Unsupported marker node, fed by the AND output: must stay in the graph and not break it.
            var unsupportedNode = NewNode("unsup1", "UNSUPPORTED", PlcElementType.Unsupported);

            config.Nodes.Add(inputNode);
            config.Nodes.Add(input2Node);
            config.Nodes.Add(andNode);
            config.Nodes.Add(unsupportedNode);

            config.Connections.Add(new NodeConnection("in1", "and1", "Input1"));
            config.Connections.Add(new NodeConnection("in2", "and1", "Input2"));
            config.Connections.Add(new NodeConnection("and1", "unsup1", "Input1"));

            return config;
        }

        /// <summary>Waits until the predicate is true or the timeout elapses; returns the predicate result.</summary>
        private static bool WaitUntil(Func<bool> condition, TimeSpan? timeout = null)
        {
            var stopwatch = Stopwatch.StartNew();
            var limit = timeout ?? WaitTimeout;
            while (stopwatch.Elapsed < limit)
            {
                if (condition()) return true;
                Thread.Sleep(10);
            }
            return condition();
        }

        [Fact]
        public void Runs_AndGraph_AndWritesOutputCoil()
        {
            var config = BuildAndGraph();
            var dataStore = DataStoreFactory.CreateDefaultDataStore();
            using var runner = new PlcSimulationRunner(config, dataStore, ScanIntervalMs);

            Assert.False(runner.IsRunning);

            runner.Start();
            Assert.True(runner.IsRunning);

            // Start with only coil 1 on: AND output must stay false (coil 2 still off).
            lock (dataStore) { dataStore.CoilDiscretes[1] = true; }

            Assert.True(WaitUntil(() => runner.CycleCount >= 2),
                $"runner did not complete 2 cycles within {WaitTimeout.TotalSeconds:0}s (CycleCount={runner.CycleCount})");

            // Both inputs on now: AND output must go true.
            lock (dataStore) { dataStore.CoilDiscretes[2] = true; }
            Assert.True(WaitUntil(() =>
            {
                lock (dataStore) { return dataStore.CoilDiscretes[10]; }
            }), "AND output coil 10 should be TRUE (both inputs on)");

            // The live-value snapshot must show the AND node driving the output as 1.
            var values = runner.GetLatestNodeValues();
            Assert.True(values.TryGetValue("and1", out var andValue), "and1 missing from GetLatestNodeValues");
            Assert.Equal(1.0, andValue);

            // Drop coil 2: AND output must fall back to false.
            lock (dataStore) { dataStore.CoilDiscretes[2] = false; }
            Assert.True(WaitUntil(() =>
            {
                lock (dataStore) { return !dataStore.CoilDiscretes[10]; }
            }), "AND output coil 10 should be FALSE when only one input is on");

            runner.Stop();
            Assert.False(runner.IsRunning);
        }

        [Fact]
        public void CycleCount_Increments_And_Stops()
        {
            var config = BuildAndGraph();
            var dataStore = DataStoreFactory.CreateDefaultDataStore();
            using var runner = new PlcSimulationRunner(config, dataStore, ScanIntervalMs);

            Assert.Equal(0, runner.CycleCount);

            runner.Start();
            Assert.True(WaitUntil(() => runner.CycleCount >= 3),
                $"CycleCount did not reach 3 within {WaitTimeout.TotalSeconds:0}s (CycleCount={runner.CycleCount})");

            runner.Stop();

            // Give any in-flight tick a moment, then confirm the count has stabilized.
            Thread.Sleep(150);
            var frozen = runner.CycleCount;
            Thread.Sleep(150);
            Assert.Equal(frozen, runner.CycleCount);

            // Disposing after Stop must not throw.
            runner.Dispose();
        }

        [Fact]
        public void StartStop_IsRepeatable_And_CycleCompleted_Fires()
        {
            var config = BuildAndGraph();
            var dataStore = DataStoreFactory.CreateDefaultDataStore();
            using var runner = new PlcSimulationRunner(config, dataStore, ScanIntervalMs);

            using var fired = new ManualResetEventSlim(false);
            runner.CycleCompleted += (_, _) => fired.Set();

            // First start.
            runner.Start();
            Assert.True(fired.Wait(WaitTimeout), "CycleCompleted did not fire on first run");
            var firstCycles = runner.CycleCount;
            runner.Stop();
            Assert.True(firstCycles > 0);

            // Second start: cycle count resets and ticks again.
            fired.Reset();
            runner.Start();
            Assert.True(fired.Wait(WaitTimeout), "CycleCompleted did not fire on second run");
            runner.Stop();

            // Dispose does not throw after repeated start/stop.
            runner.Dispose();
        }
    }
}
