using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Modbus.Data;
using ModbusForge.Models;
using ModbusForge.Services;
using ModbusForge.ViewModels;
using Moq;
using Xunit;

namespace ModbusForge.Tests.Services
{
    /// <summary>
    /// Regression tests for the refactored VisualSimulationService.
    /// Validates two-phase evaluate-then-write, topological sort,
    /// NodeResult, area guards, and bool/int isolation.
    /// </summary>
    public class VisualSimulationServiceTests : IAsyncLifetime, IDisposable
    {
        private readonly Mock<ILogger<VisualSimulationService>> _simLoggerMock;
        private readonly Mock<ILogger<ModbusServerService>> _srvLoggerMock;
        private readonly ModbusServerService _serverService;
        private readonly VisualSimulationService _simService;
        private readonly VisualNodeEditorViewModel _viewModel;
        private readonly int _testPort;

        public VisualSimulationServiceTests()
        {
            _simLoggerMock = new Mock<ILogger<VisualSimulationService>>();
            _srvLoggerMock = new Mock<ILogger<ModbusServerService>>();
            _serverService = new ModbusServerService(_srvLoggerMock.Object);
            _simService = new VisualSimulationService(_simLoggerMock.Object, _serverService);
            _viewModel = new VisualNodeEditorViewModel();
            _testPort = GetFreePort();
        }

        public async Task InitializeAsync()
        {
            // Start server so we have a DataStore to work with
            await _serverService.ConnectAsync("127.0.0.1", _testPort, "1");
        }

        public async Task DisposeAsync()
        {
            await _serverService.DisconnectAsync();
        }

        public void Dispose()
        {
            _simService.Dispose();
        }

        private static int GetFreePort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private DataStore GetDataStore() => _serverService.GetDataStore();

        // ───────── Helper: build nodes + connections and call UpdateNodeValues ─────────

        private void SetupAndRun()
        {
            _viewModel.ShowLiveValues = true;
            _simService.Start(_viewModel);
            // Call UpdateNodeValues directly (bypasses DispatcherTimer)
            _simService.UpdateNodeValues();
        }

        private VisualNode MakeNode(PlcElementType type, string id = null)
        {
            var node = new VisualNode
            {
                Id = id ?? Guid.NewGuid().ToString(),
                ElementType = type,
                Name = type.ToString()
            };
            return node;
        }

        private void Connect(string sourceId, string targetId, string targetConnector = "Input1")
        {
            _viewModel.Connections.Add(new NodeConnection(sourceId, targetId, targetConnector));
        }

        // ═══════════════════════════════════════════════════════════════════
        // Test 1: Bool output to Coil does NOT affect HoldingRegister at same index
        // ═══════════════════════════════════════════════════════════════════

        [Fact]
        public void BoolOutputToCoil_DoesNotAffectHoldingRegister()
        {
            var ds = GetDataStore();
            // Pre-set HoldingRegister[5] to a known value
            ds.HoldingRegisters[5] = 42;
            // Pre-set Coil[5] input to true
            ds.CoilDiscretes[5] = true;

            // InputBool reads Coil:5 → OutputBool writes Coil:5
            var inNode = MakeNode(PlcElementType.InputBool, "in1");
            inNode.Input1Address = new PlcAddressReference { Area = PlcArea.Coil, Address = 5 };

            var outNode = MakeNode(PlcElementType.OutputBool, "out1");
            outNode.OutputAddress = new PlcAddressReference { Area = PlcArea.Coil, Address = 5 };

            _viewModel.Nodes.Add(inNode);
            _viewModel.Nodes.Add(outNode);
            Connect("in1", "out1");

            SetupAndRun();

            // Coil[5] should be true (written by OutputBool)
            Assert.True(ds.CoilDiscretes[5]);
            // HoldingRegister[5] must still be 42 — no cross-contamination
            Assert.Equal(42, ds.HoldingRegisters[5]);
        }

        // ═══════════════════════════════════════════════════════════════════
        // Test 2: Int output to HoldingRegister does NOT affect Coil at same index
        // ═══════════════════════════════════════════════════════════════════

        [Fact]
        public void IntOutputToHoldingRegister_DoesNotAffectCoil()
        {
            var ds = GetDataStore();
            // Pre-set Coil[10] to true
            ds.CoilDiscretes[10] = true;
            // Pre-set HoldingRegister[3] with a source value
            ds.HoldingRegisters[3] = 999;

            // InputInt reads HR:3 → OutputInt writes HR:10
            var inNode = MakeNode(PlcElementType.InputInt, "in1");
            inNode.Input1Address = new PlcAddressReference { Area = PlcArea.HoldingRegister, Address = 3 };

            var outNode = MakeNode(PlcElementType.OutputInt, "out1");
            outNode.OutputAddress = new PlcAddressReference { Area = PlcArea.HoldingRegister, Address = 10 };

            _viewModel.Nodes.Add(inNode);
            _viewModel.Nodes.Add(outNode);
            Connect("in1", "out1");

            SetupAndRun();

            // HR[10] should be 999 (from InputInt → OutputInt)
            Assert.Equal(999, ds.HoldingRegisters[10]);
            // Coil[10] must still be true — not overwritten
            Assert.True(ds.CoilDiscretes[10]);
        }

        // ═══════════════════════════════════════════════════════════════════
        // Test 3: MATH chain produces correct integer value
        // ═══════════════════════════════════════════════════════════════════

        [Fact]
        public void MathChain_ProducesCorrectIntegerResult()
        {
            var ds = GetDataStore();
            ds.HoldingRegisters[1] = 10;
            ds.HoldingRegisters[2] = 3;

            // InputInt(HR:1) ─┐
            //                 ├─ MATH_ADD ─── OutputInt → HR:20
            // InputInt(HR:2) ─┘
            var inA = MakeNode(PlcElementType.InputInt, "inA");
            inA.Input1Address = new PlcAddressReference { Area = PlcArea.HoldingRegister, Address = 1 };

            var inB = MakeNode(PlcElementType.InputInt, "inB");
            inB.Input1Address = new PlcAddressReference { Area = PlcArea.HoldingRegister, Address = 2 };

            var addNode = MakeNode(PlcElementType.MATH_ADD, "add1");

            var outNode = MakeNode(PlcElementType.OutputInt, "out1");
            outNode.OutputAddress = new PlcAddressReference { Area = PlcArea.HoldingRegister, Address = 20 };

            _viewModel.Nodes.Add(inA);
            _viewModel.Nodes.Add(inB);
            _viewModel.Nodes.Add(addNode);
            _viewModel.Nodes.Add(outNode);
            Connect("inA", "add1", "Input1");
            Connect("inB", "add1", "Input2");
            Connect("add1", "out1", "Input1");

            SetupAndRun();

            Assert.Equal(13, ds.HoldingRegisters[20]); // 10 + 3 = 13
        }

        // ═══════════════════════════════════════════════════════════════════
        // Test 4: Mixed graph — bool and int paths don't contaminate each other
        // ═══════════════════════════════════════════════════════════════════

        [Fact]
        public void MixedGraph_NoCrossContamination()
        {
            var ds = GetDataStore();
            ds.CoilDiscretes[1] = true;
            ds.HoldingRegisters[1] = 50;
            ds.HoldingRegisters[2] = 25;

            // Bool path: InputBool(Coil:1) → NOT → OutputBool(Coil:2)
            var boolIn = MakeNode(PlcElementType.InputBool, "bIn");
            boolIn.Input1Address = new PlcAddressReference { Area = PlcArea.Coil, Address = 1 };

            var notNode = MakeNode(PlcElementType.NOT, "not1");

            var boolOut = MakeNode(PlcElementType.OutputBool, "bOut");
            boolOut.OutputAddress = new PlcAddressReference { Area = PlcArea.Coil, Address = 2 };

            // Int path: InputInt(HR:1) + InputInt(HR:2) → MATH_ADD → OutputInt(HR:10)
            var intInA = MakeNode(PlcElementType.InputInt, "iA");
            intInA.Input1Address = new PlcAddressReference { Area = PlcArea.HoldingRegister, Address = 1 };

            var intInB = MakeNode(PlcElementType.InputInt, "iB");
            intInB.Input1Address = new PlcAddressReference { Area = PlcArea.HoldingRegister, Address = 2 };

            var addNode = MakeNode(PlcElementType.MATH_ADD, "add");

            var intOut = MakeNode(PlcElementType.OutputInt, "iOut");
            intOut.OutputAddress = new PlcAddressReference { Area = PlcArea.HoldingRegister, Address = 10 };

            // Add nodes
            foreach (var n in new[] { boolIn, notNode, boolOut, intInA, intInB, addNode, intOut })
                _viewModel.Nodes.Add(n);

            // Bool connections
            Connect("bIn", "not1");
            Connect("not1", "bOut");

            // Int connections
            Connect("iA", "add", "Input1");
            Connect("iB", "add", "Input2");
            Connect("add", "iOut");

            SetupAndRun();

            // Bool path: NOT(true) = false → Coil[2] = false
            Assert.False(ds.CoilDiscretes[2]);
            // Int path: 50 + 25 = 75 → HR[10] = 75
            Assert.Equal(75, ds.HoldingRegisters[10]);

            // Cross-contamination checks:
            // HR[2] should still be 25 (bool path didn't touch it)
            Assert.Equal(25, ds.HoldingRegisters[2]);
            // Coil[10] should still be false/default (int path didn't touch it)
            Assert.False(ds.CoilDiscretes[10]);
        }

        // ═══════════════════════════════════════════════════════════════════
        // Test 5: Topological evaluation order is correct
        // ═══════════════════════════════════════════════════════════════════

        [Fact]
        public void TopologicalOrder_EvaluatesUpstreamBeforeDownstream()
        {
            var ds = GetDataStore();
            ds.HoldingRegisters[1] = 7;

            // InputInt(HR:1) → MATH_MUL(×2, via CompareValue) → OutputInt(HR:30)
            // Add nodes in REVERSE order to prove topo sort fixes evaluation order
            var outNode = MakeNode(PlcElementType.OutputInt, "out");
            outNode.OutputAddress = new PlcAddressReference { Area = PlcArea.HoldingRegister, Address = 30 };

            var mulNode = MakeNode(PlcElementType.MATH_MUL, "mul");
            mulNode.CompareValue = 2; // Input2 fallback: Const = 2
            mulNode.Input2Address = new PlcAddressReference { Area = PlcArea.HoldingRegister, Address = -1 };

            var inNode = MakeNode(PlcElementType.InputInt, "in");
            inNode.Input1Address = new PlcAddressReference { Area = PlcArea.HoldingRegister, Address = 1 };

            // Add in reverse dependency order
            _viewModel.Nodes.Add(outNode);
            _viewModel.Nodes.Add(mulNode);
            _viewModel.Nodes.Add(inNode);

            Connect("in", "mul", "Input1");
            Connect("mul", "out", "Input1");

            SetupAndRun();

            // 7 × 2 = 14
            Assert.Equal(14, ds.HoldingRegisters[30]);
        }
    }
}
