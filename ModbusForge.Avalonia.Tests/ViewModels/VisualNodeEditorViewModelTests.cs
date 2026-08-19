using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using ModbusForge.Core.Simulation.Blocks;
using ModbusForge.Core.Simulation.Core;
using ModbusForge.Models;
using ModbusForge.Avalonia.ViewModels;
using ModbusForge.Services;
using Xunit;

namespace ModbusForge.Avalonia.Tests.ViewModels
{
    /// <summary>
    /// Headless view-model tests for the visual node editor: parameter-edit undo/redo,
    /// the coalescing rules, demo loading, and connection validation.
    /// </summary>
    public class VisualNodeEditorViewModelTests
    {
        [Fact]
        public void ParameterEdit_UndoRestoresValue_RedoReapplies()
        {
            using var vm = CreateVm();
            var ton = vm.AddNodeAt(PlcElementType.TON, 0, 0)!;
            var field = Field(ton, "TimerPresetMs");
            var original = ton.TimerPresetMs;

            field.Numeric = original + 1000;
            Assert.Equal(original + 1000, ton.TimerPresetMs);

            vm.UndoCommand.Execute(null);
            Assert.Equal(original, ton.TimerPresetMs);
            Assert.Equal(original, field.Numeric); // the editor field is re-synced too

            vm.RedoCommand.Execute(null);
            Assert.Equal(original + 1000, ton.TimerPresetMs);
            Assert.Equal(original + 1000, field.Numeric);
        }

        [Fact]
        public void ParameterEdit_MultipleChangesCoalesceIntoOneUndoStep()
        {
            using var vm = CreateVm();
            var ton = vm.AddNodeAt(PlcElementType.TON, 0, 0)!;
            var field = Field(ton, "TimerPresetMs");
            var original = ton.TimerPresetMs;

            // Drag-style sequence: several changes to the same parameter.
            field.Numeric = original + 100;
            field.Numeric = original + 200;
            field.Numeric = original + 300;
            Assert.Equal(original + 300, ton.TimerPresetMs);

            // The node-add command is still on the stack underneath the one coalesced
            // parameter series.
            vm.UndoCommand.Execute(null);
            Assert.Equal(original, ton.TimerPresetMs);
            Assert.True(vm.CanUndo); // the add-node command remains

            vm.UndoCommand.Execute(null);
            Assert.DoesNotContain(ton, vm.Nodes); // the node itself is gone
        }

        [Fact]
        public void ParameterEdit_DifferentNodes_AreSeparateUndoSteps()
        {
            using var vm = CreateVm();
            var ton = vm.AddNodeAt(PlcElementType.TON, 0, 0)!;
            var valve = vm.AddNodeAt(PlcElementType.Valve, 0, 200)!;

            var tonField = Field(ton, "TimerPresetMs");
            var valveField = Field(valve, "ValveTravelTimeMs");
            var originalTon = ton.TimerPresetMs;
            var originalValve = valve.ValveTravelTimeMs;

            tonField.Numeric = originalTon + 500;
            valveField.Numeric = originalValve + 500;

            // Newest first: the valve edit is undone, the TON edit survives.
            vm.UndoCommand.Execute(null);
            Assert.Equal(originalValve, valve.ValveTravelTimeMs);
            Assert.Equal(originalTon + 500, ton.TimerPresetMs);

            vm.UndoCommand.Execute(null);
            Assert.Equal(originalTon, ton.TimerPresetMs);
        }

        [Fact]
        public void WaveformApply_UndoRestoresPreviousWaveform()
        {
            using var vm = CreateVm();
            var generator = vm.AddNodeAt(PlcElementType.SignalGeneratorReal, 0, 0)!;
            var original = (generator.Waveform, generator.PeriodMs, generator.Amplitude, generator.Offset);

            vm.SelectedNode = generator;
            vm.SelectedWaveform = "Sine";
            vm.WaveformPeriodMs = original.PeriodMs + 250;
            vm.WaveformAmplitude = original.Amplitude * 2;
            vm.WaveformOffset = 7;

            vm.ApplyWaveformCommand.Execute(null);
            Assert.Equal("Sine", generator.Waveform);
            Assert.Equal(original.PeriodMs + 250, generator.PeriodMs);
            Assert.Equal(original.Amplitude * 2, generator.Amplitude);
            Assert.Equal(7, generator.Offset);

            vm.UndoCommand.Execute(null);
            Assert.Equal(original, (generator.Waveform, generator.PeriodMs, generator.Amplitude, generator.Offset));
        }

        [Fact]
        public void Rename_UndoRestoresOriginalName()
        {
            using var vm = CreateVm();
            var node = vm.AddNodeAt(PlcElementType.TON, 0, 0)!;
            var original = node.Name;

            // Keystroke-style sequence on the name editor.
            node.Name = "T";
            node.Name = "TO";
            node.Name = "Timer 2";

            // All keystrokes coalesce into one undo step.
            vm.UndoCommand.Execute(null);
            Assert.Equal(original, node.Name);

            vm.RedoCommand.Execute(null);
            Assert.Equal("Timer 2", node.Name);
        }

        [Fact]
        public void AddressEdit_UndoRestoresOriginalBinding()
        {
            using var vm = CreateVm();
            var node = vm.AddNodeAt(PlcElementType.InputBool, 0, 0)!;
            var originalArea = node.Input1Address.Area;
            var originalAddress = node.Input1Address.Address;

            node.Input1Address.Address = 17;
            node.Input1Address.Area = PlcArea.HoldingRegister;

            vm.UndoCommand.Execute(null);

            Assert.Equal(originalAddress, node.Input1Address.Address);
            Assert.Equal(originalArea, node.Input1Address.Area);

            vm.RedoCommand.Execute(null);
            Assert.Equal(17, node.Input1Address.Address);
            Assert.Equal(PlcArea.HoldingRegister, node.Input1Address.Area);
        }

        [Fact]
        public void AddressReferenceSwap_UndoRestoresOriginalBinding()
        {
            using var vm = CreateVm();
            var node = vm.AddNodeAt(PlcElementType.InputBool, 0, 0)!;
            var original = node.Input1Address.Clone();

            // Simulates BindTagToNodeInput: the reference object is replaced.
            node.Input1Address = new PlcAddressReference { Area = PlcArea.HoldingRegister, Address = 42 };

            vm.UndoCommand.Execute(null);
            Assert.Equal(original.Address, node.Input1Address.Address);
            Assert.Equal(original.Area, node.Input1Address.Area);

            // Edits on the (re-attached) replacement reference are tracked too: the swap
            // and the follow-up address change coalesce into one undo step.
            node.Input1Address = new PlcAddressReference { Area = PlcArea.HoldingRegister, Address = 42 };
            node.Input1Address.Address = 7;

            vm.UndoCommand.Execute(null);
            Assert.Equal(original.Address, node.Input1Address.Address);
            Assert.Equal(original.Area, node.Input1Address.Area);

            vm.RedoCommand.Execute(null);
            Assert.Equal(7, node.Input1Address.Address);
            Assert.Equal(PlcArea.HoldingRegister, node.Input1Address.Area);
        }

        [Fact]
        public void MixedEdits_SameNode_CoalesceIntoOneUndoStep()
        {
            using var vm = CreateVm();
            var node = vm.AddNodeAt(PlcElementType.TON, 0, 0)!;
            var originalName = node.Name;
            var originalPreset = node.TimerPresetMs;

            node.Name = "Renamed";
            node.IsEnabled = false;
            Field(node, "TimerPresetMs").Numeric = originalPreset + 100;

            vm.UndoCommand.Execute(null);

            Assert.Equal(originalName, node.Name);
            Assert.True(node.IsEnabled);
            Assert.Equal(originalPreset, node.TimerPresetMs);
        }

        [Fact]
        public async Task LoadDemo_WhileRunning_KeepsSimulationRunning()
        {
            using var vm = CreateVm();
            Assert.True(vm.IsRunning); // the editor auto-starts with Live on

            await ((IAsyncRelayCommand)vm.LoadDemoCommand).ExecuteAsync(null);

            Assert.True(vm.IsRunning);
            Assert.Equal(4, vm.Nodes.Count);
            Assert.All(vm.Nodes, node => Assert.True(node.ShowLiveValues));
        }

        [Fact]
        public async Task LoadDemo_WhileStopped_KeepsSimulationStopped()
        {
            using var vm = CreateVm();
            vm.StopCommand.Execute(null);
            Assert.False(vm.IsRunning);

            await ((IAsyncRelayCommand)vm.LoadDemoCommand).ExecuteAsync(null);

            Assert.False(vm.IsRunning);
            Assert.Equal(4, vm.Nodes.Count);
        }

        [Fact]
        public void Connect_SecondDriverToSameInput_IsRejected()
        {
            using var vm = CreateVm();
            var inputA = vm.AddNodeAt(PlcElementType.InputBool, 0, 0)!;
            var inputB = vm.AddNodeAt(PlcElementType.InputBool, 0, 200)!;
            var and = vm.AddNodeAt(PlcElementType.AND, 300, 100)!;

            Assert.True(vm.TryConnectNodes(inputA, and, "Input1"));

            var rejected = vm.TryConnectNodes(inputB, and, "Input1");

            Assert.False(rejected);
            Assert.Contains("already has a driver", vm.StatusText);
            Assert.Single(vm.Connections);
        }

        [Fact]
        public void CyclesChanged_MarksLockedNodes_AndUnmarksWhenLoopIsBroken()
        {
            using var service = new FakeVisualSimulationService();
            using var vm = new VisualNodeEditorViewModel(service, NoopTagWindowService.Instance);

            var a = vm.AddNodeAt(PlcElementType.TON, 0, 0)!;
            var b = vm.AddNodeAt(PlcElementType.AND, 0, 100)!;

            service.RaiseCycles(new[] { a.Id });

            Assert.Equal(VisualNodeEditorViewModel.CycleLockErrorText, a.ErrorText);
            Assert.Null(b.ErrorText);
            Assert.Contains("loop", vm.StatusText);

            // The graph edit dissolves the loop: the marker comes off.
            service.RaiseCycles(Array.Empty<string>());

            Assert.Null(a.ErrorText);
            Assert.Null(b.ErrorText);
        }

        [Fact]
        public void Start_RaisingCyclesDuringStart_LoopWarningSurvivesStatusUpdate()
        {
            // The real service rebuilds the graph during Start and fires
            // CyclesChanged synchronously; the subsequent "Simulation running"
            // status update must not swallow the loop warning.
            using var service = new FakeVisualSimulationService();
            using var vm = new VisualNodeEditorViewModel(service, NoopTagWindowService.Instance);

            var a = vm.AddNodeAt(PlcElementType.TON, 0, 0)!;
            service.CyclesOnStart = new[] { a.Id };

            vm.StopCommand.Execute(null);
            vm.RunCommand.Execute(null);

            Assert.Contains("loop", vm.StatusText);
            Assert.Equal(VisualNodeEditorViewModel.CycleLockErrorText, a.ErrorText);
        }

        [Fact]
        public void DuplicateProgram_CopiesEveryNodeParameter()
        {
            // Regression: node cloning used to copy only the first generation of
            // parameter properties, so VSD/valve/real-math nodes — and any block
            // added later — silently reset to defaults in the duplicated program.
            using var vm = CreateVm();

            var program = new ProgramModel
            {
                Name = "Original",
                Nodes =
                {
                    new VisualNode
                    {
                        Id = "vsd1",
                        Name = "VSD",
                        ElementType = PlcElementType.Vsd,
                        VsdMaxSpeed = 55.0,
                        VsdRampUpMs = 333,
                        VsdRampDownMs = 444,
                        VsdAtSpeedTolerance = 7.5
                    },
                    new VisualNode
                    {
                        Id = "scale1",
                        Name = "Scale",
                        ElementType = PlcElementType.Scale,
                        ScaleFromMax = 50.0,
                        ScaleToMax = 7.5,
                        ScaleClamp = false
                    },
                    new VisualNode
                    {
                        Id = "edge1",
                        Name = "Edge",
                        ElementType = PlcElementType.EdgeDetect,
                        EdgeDetectDirection = "Falling"
                    }
                }
            };

            vm.DuplicateProgramCommand.Execute(program);

            var duplicate = vm.ProgramTree.Programs.Single(p => p.Name == "Original_Copy");

            var vsd = duplicate.Nodes.Single(n => n.ElementType == PlcElementType.Vsd);
            Assert.Equal(55.0, vsd.VsdMaxSpeed);
            Assert.Equal(333, vsd.VsdRampUpMs);
            Assert.Equal(444, vsd.VsdRampDownMs);
            Assert.Equal(7.5, vsd.VsdAtSpeedTolerance);

            var scale = duplicate.Nodes.Single(n => n.ElementType == PlcElementType.Scale);
            Assert.Equal(50.0, scale.ScaleFromMax);
            Assert.Equal(7.5, scale.ScaleToMax);
            Assert.False(scale.ScaleClamp);

            var edge = duplicate.Nodes.Single(n => n.ElementType == PlcElementType.EdgeDetect);
            Assert.Equal("Falling", edge.EdgeDetectDirection);

            // The copies are new nodes, not shared references.
            Assert.NotEqual("vsd1", vsd.Id);
        }

        private static VisualNodeEditorViewModel CreateVm()
            => new(new AvaloniaVisualSimulationService(), NoopTagWindowService.Instance);

        private static ParameterField Field(VisualNode node, string name)
            => node.ParameterFields!.Single(field => field.Name == name);

        private sealed class NoopTagWindowService : ITagWindowService
        {
            public static readonly NoopTagWindowService Instance = new();
            public void ShowTagBrowser() { }
            public void ShowWatchWindow() { }
        }

        /// <summary>
        /// Scriptable stand-in for the simulation service: no timer, no engine,
        /// and a manually raisable CyclesChanged event (optionally during Start,
        /// mirroring the real service's synchronous graph-rebuild notification).
        /// </summary>
        private sealed class FakeVisualSimulationService : IVisualSimulationService
        {
            public bool IsRunning { get; private set; }

            /// <summary>
            /// The editor builds parameter fields from the catalog on node add,
            /// so the fake must know the block types the tests create.
            /// </summary>
            public FunctionBlockCatalog Catalog { get; } = CreateCatalog();

            private static FunctionBlockCatalog CreateCatalog()
            {
                var catalog = new FunctionBlockCatalog();
                catalog.Register(new TonBlock());
                catalog.Register(new AndBlock());
                return catalog;
            }
            public int ScanIntervalMs => 100;
            public string StoreMode => "local";

            public IReadOnlyList<string>? CyclesOnStart { get; set; }

            public event Action<IReadOnlyList<string>>? CyclesChanged;

            public void Start(VisualNodeEditorConfig config)
            {
                IsRunning = true;
                if (CyclesOnStart is { Count: > 0 })
                    CyclesChanged?.Invoke(CyclesOnStart);
            }

            public void Stop() => IsRunning = false;
            public void UpdateNodeValues() { }
            public void SetScanIntervalMs(int ms) { }
            public bool GetNodeValue(string nodeId) => false;
            public void WriteNodeValue(string nodeId, double value) { }
            public void Dispose() { }

            public void RaiseCycles(IReadOnlyList<string> nodeIds) => CyclesChanged?.Invoke(nodeIds);
        }
    }
}
