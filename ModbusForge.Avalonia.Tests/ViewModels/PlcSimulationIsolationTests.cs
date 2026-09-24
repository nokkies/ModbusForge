using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ModbusForge.Avalonia.ViewModels;
using ModbusForge.Models;
using ModbusForge.Services;
using Xunit;

namespace ModbusForge.Avalonia.Tests.ViewModels
{
    /// <summary>
    /// The PLC tab (imported XEF logic, the controller) and the Simulation tab
    /// (hand-built plant model) are separate programs that only meet on the Modbus
    /// registers. Importing or browsing a PLC project must never rewrite the
    /// Simulation's programs, and a tab-visibility change on one must not affect
    /// the other.
    /// </summary>
    public sealed class PlcSimulationIsolationTests : IDisposable
    {
        private const string SampleXef = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<FEFExchangeFile>
  <dataBlock>
    <variables name=""Pump_Start"" typeName=""BOOL"" topologicalAddress=""%QB0.1""/>
    <variables name=""Pump_Run"" typeName=""BOOL"" topologicalAddress=""%QB0.2""/>
    <variables name=""Pump_Fault"" typeName=""BOOL"" topologicalAddress=""%IB0.3""/>
  </dataBlock>
  <program>
    <identProgram name=""PumpControl"" type=""section"" task=""MAST""/>
    <FBDSource>
      <networkFBD>
        <FFBBlock instanceName=""AND1"" typeName=""AND"" width=""7"" height=""5"">
          <objPosition posX=""10"" posY=""2""/>
          <descriptionFFB>
            <inputVariable formalParameter=""IN1"" effectiveParameter=""Pump_Start""/>
            <inputVariable formalParameter=""IN2"" effectiveParameter=""Pump_Run""/>
            <outputVariable formalParameter=""OUT"" effectiveParameter=""Pump_Fault""/>
          </descriptionFFB>
        </FFBBlock>
      </networkFBD>
    </FBDSource>
  </program>
  <program>
    <identProgram name=""PumpAlarms"" type=""section"" task=""MAST""/>
    <FBDSource>
      <networkFBD>
        <FFBBlock instanceName=""OR1"" typeName=""OR"" width=""7"" height=""5"">
          <objPosition posX=""10"" posY=""2""/>
          <descriptionFFB>
            <inputVariable formalParameter=""IN1"" effectiveParameter=""Pump_Fault""/>
            <inputVariable formalParameter=""IN2"" effectiveParameter=""Pump_Run""/>
            <outputVariable formalParameter=""OUT"" effectiveParameter=""Pump_Start""/>
          </descriptionFFB>
        </FFBBlock>
      </networkFBD>
    </FBDSource>
  </program>
</FEFExchangeFile>";

        // TabItem order in MainView.axaml: ... Signal Generator(6), PLC(7),
        // Simulation(8), Holding(9), Coils(10), Input(11), Discrete(12),
        // Custom Watch(13), Decode(14), Console(15), Debug(16).
        private const int PlcTabIndex = 7;

        // The names MainViewModel persists for each hideable tab (GetVisibleTabs).
        private static readonly string[] AllTabNames =
        {
            "Registers", "InputRegisters", "Coils", "DiscreteInputs", "CustomWatch",
            "Simulation", "Decode", "Trend", "Console", "Debug"
        };

        private readonly string _xefPath;

        public PlcSimulationIsolationTests()
        {
            _xefPath = Path.Combine(Path.GetTempPath(), $"plc-isolation-{Guid.NewGuid():N}.xef");
            File.WriteAllText(_xefPath, SampleXef);
        }

        public void Dispose()
        {
            try { File.Delete(_xefPath); } catch (IOException) { }
        }

        [Fact]
        public async Task LoadingXef_LeavesTheSimulationProgramsUntouched()
        {
            using var simulation = CreateSimulationEditor();
            var tank = simulation.AddNodeAt(PlcElementType.Valve, 100, 100)!;
            var treeBefore = simulation.ProgramTree;
            var programBefore = simulation.SelectedProgram;
            using var plc = CreatePlcEditor();

            using var main = CreateMainViewModel(simulation, plc);
            await main.LoadPlcXmlCommand.ExecuteAsync(null);

            Assert.True(main.PlcProjectViewModel.HasProject, "the XEF must still import");
            Assert.Same(treeBefore, simulation.ProgramTree);
            Assert.Same(programBefore, simulation.SelectedProgram);
            Assert.Equal(new[] { tank }, simulation.Nodes.ToArray());
        }

        [Fact]
        public async Task LoadingXef_KeepsTheSimulationRunning()
        {
            using var simulation = CreateSimulationEditor();
            simulation.RunCommand.Execute(null);
            Assert.True(simulation.IsRunning, "precondition: the plant model is running");
            using var plc = CreatePlcEditor();

            using var main = CreateMainViewModel(simulation, plc);
            await main.LoadPlcXmlCommand.ExecuteAsync(null);

            Assert.True(simulation.IsRunning);
        }

        [Fact]
        public async Task LoadingXef_ShowsOnlyTheImportedProgramsInThePlcEditor()
        {
            using var simulation = CreateSimulationEditor();
            using var plc = CreatePlcEditor();

            using var main = CreateMainViewModel(simulation, plc);
            await main.LoadPlcXmlCommand.ExecuteAsync(null);

            Assert.Equal(new[] { "PumpControl", "PumpAlarms" }, ProgramNames(plc.ProgramTree));
        }

        [Fact]
        public async Task SelectingAPlcProgram_OpensItInThePlcEditorOnly()
        {
            using var simulation = CreateSimulationEditor();
            var simulationProgram = simulation.SelectedProgram;
            using var plc = CreatePlcEditor();
            using var main = CreateMainViewModel(simulation, plc);
            await main.LoadPlcXmlCommand.ExecuteAsync(null);

            main.PlcProjectViewModel.SelectedNode = NavigatorProgram(main, "PumpAlarms");

            Assert.Equal("PumpAlarms", plc.SelectedProgram?.Name);
            Assert.Same(simulationProgram, simulation.SelectedProgram);
        }

        [Fact]
        public async Task DoubleClickingAPlcProgram_OpensItOnThePlcTab()
        {
            using var simulation = CreateSimulationEditor();
            var simulationProgram = simulation.SelectedProgram;
            using var plc = CreatePlcEditor();
            using var main = CreateMainViewModel(simulation, plc);
            await main.LoadPlcXmlCommand.ExecuteAsync(null);

            main.OpenPlcProgram(NavigatorProgram(main, "PumpAlarms"));

            Assert.Equal("PumpAlarms", plc.SelectedProgram?.Name);
            Assert.Same(simulationProgram, simulation.SelectedProgram);
            Assert.Equal(PlcTabIndex, main.SelectedTabIndex);
        }

        [Fact]
        public async Task PlcEditorStatus_ReachesTheConsoleLabelledAsPlc()
        {
            using var simulation = CreateSimulationEditor();
            using var plc = CreatePlcEditor();
            using var main = CreateMainViewModel(simulation, plc);
            var plcMessages = new List<string>();
            plc.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(plc.StatusText)) plcMessages.Add(plc.StatusText);
            };

            await main.LoadPlcXmlCommand.ExecuteAsync(null);

            // Both editors share the status wording ("Simulation stopped" when the
            // PLC import halts the PLC engine), so an unlabelled line would claim
            // the still-running Simulation stopped.
            Assert.NotEmpty(plcMessages);
            Assert.All(plcMessages, m => Assert.Contains($"PLC: {m}", main.ConsoleMessages));
            Assert.All(plcMessages, m => Assert.DoesNotContain(m, main.ConsoleMessages));
        }

        [Fact]
        public async Task LoadingALargeXef_ReturnsControlWhileTheFileIsParsed()
        {
            // Corpus exports reach 16.8 MB; parsing on the UI thread froze the window.
            var blocks = string.Concat(Enumerable.Range(0, 3000).Select(i => $@"
        <FFBBlock instanceName="".{i}"" typeName=""AND"" additionnalPinNumber=""0"" enEnO=""false"" width=""7"" height=""6"">
          <objPosition posX=""{i % 20 * 10}"" posY=""{i / 20 * 8}""/>
          <descriptionFFB execAfter="""">
            <inputVariable invertedPin=""false"" formalParameter=""IN1"" effectiveParameter=""Pump_Run""/>
            <inputVariable invertedPin=""false"" formalParameter=""IN2"" effectiveParameter=""Pump_Start""/>
            <outputVariable invertedPin=""false"" formalParameter=""OUT""/>
          </descriptionFFB>
        </FFBBlock>"));
            File.WriteAllText(_xefPath, SampleXef.Replace("</networkFBD>", blocks + "</networkFBD>"));
            using var simulation = CreateSimulationEditor();
            using var plc = CreatePlcEditor();
            using var main = CreateMainViewModel(simulation, plc);

            var load = main.LoadPlcXmlCommand.ExecuteAsync(null);

            Assert.False(load.IsCompleted, "the parse must not run on the calling thread");
            await load;
            Assert.True(main.PlcProjectViewModel.HasProject);
        }

        [Fact]
        public void HidingTheSimulationTab_KeepsThePlcTabOpen()
        {
            using var main = CreateMainViewModel(simulation: null, plc: null);
            main.SelectedTabIndex = PlcTabIndex;

            main.IsSimulationTabVisible = false;

            Assert.Equal(PlcTabIndex, main.SelectedTabIndex);
        }

        [Theory]
        [InlineData(8, "Simulation")]
        [InlineData(9, "Registers")]
        [InlineData(10, "Coils")]
        [InlineData(11, "InputRegisters")]
        [InlineData(12, "DiscreteInputs")]
        [InlineData(13, "CustomWatch")]
        [InlineData(14, "Decode")]
        [InlineData(15, "Console")]
        [InlineData(16, "Debug")]
        public void HidingATab_MovesTheSelectionOffIt(int tabIndex, string hiddenTab)
        {
            using var main = CreateMainViewModel(simulation: null, plc: null);
            main.SelectedTabIndex = tabIndex;

            main.SetVisibleTabs(AllTabNames.Where(name => name != hiddenTab).ToList());

            Assert.NotEqual(tabIndex, main.SelectedTabIndex);
        }

        private MainViewModel CreateMainViewModel(VisualNodeEditorViewModel? simulation, PlcEditorViewModel? plc)
            => new(
                new NullConnectionManager(),
                NullLogger<MainViewModel>.Instance,
                new SyncDispatcher(),
                fileDialogService: new FixedPathFileDialogService(_xefPath),
                visualNodeEditorViewModel: simulation,
                plcEditorViewModel: plc);

        private static VisualNodeEditorViewModel CreateSimulationEditor()
            => new(new AvaloniaVisualSimulationService(), NoopTagWindowService.Instance);

        private static PlcEditorViewModel CreatePlcEditor()
            => new(NoopTagWindowService.Instance);

        private static PlcTreeNodeViewModel NavigatorProgram(MainViewModel main, string programName)
            => Flatten(main.PlcProjectViewModel.RootNodes)
                .Single(n => n.Kind == PlcNodeKind.ProgramFbd && n.ProgramName == programName);

        private static IEnumerable<PlcTreeNodeViewModel> Flatten(IEnumerable<PlcTreeNodeViewModel> nodes)
        {
            foreach (var node in nodes)
            {
                yield return node;
                foreach (var child in Flatten(node.Children))
                {
                    yield return child;
                }
            }
        }

        private static IEnumerable<string> ProgramNames(ProgramFolder folder)
        {
            foreach (var program in folder.Programs)
            {
                yield return program.Name;
            }

            foreach (var name in folder.Folders.SelectMany(ProgramNames))
            {
                yield return name;
            }
        }

        private sealed class NoopTagWindowService : ITagWindowService
        {
            public static readonly NoopTagWindowService Instance = new();
            public void ShowTagBrowser() { }
            public void ShowWatchWindow() { }
        }

        private sealed class FixedPathFileDialogService : IFileDialogService
        {
            private readonly string _path;

            public FixedPathFileDialogService(string path) => _path = path;

            public string? ShowSaveFileDialog(string title, string filter, string defaultFileName) => null;
            public string? ShowOpenFileDialog(string title, string filter) => _path;
            public Task<string?> ShowSaveFileDialogAsync(string title, string filter, string defaultFileName) => Task.FromResult<string?>(null);
            public Task<string?> ShowOpenFileDialogAsync(string title, string filter) => Task.FromResult<string?>(_path);
        }

        private sealed class NullConnectionManager : IConnectionManager
        {
            public ObservableCollection<ConnectionProfile> Profiles { get; } = new();
            public ConnectionProfile? ActiveProfile => null;
            public IModbusService? ActiveService => null;

            public event EventHandler<ConnectionProfile?>? ActiveProfileChanged { add { } remove { } }
            public event EventHandler<ConnectionProfile>? ProfileConnected { add { } remove { } }
            public event EventHandler<ConnectionProfile>? ProfileDisconnected { add { } remove { } }

            public void AddProfile(ConnectionProfile profile) => Profiles.Add(profile);
            public void RemoveProfile(ConnectionProfile profile) => Profiles.Remove(profile);
            public void SetActiveProfile(ConnectionProfile profile) { }
            public Task<bool> ConnectProfileAsync(ConnectionProfile profile) => Task.FromResult(false);
            public Task DisconnectProfileAsync(ConnectionProfile profile) => Task.CompletedTask;
            public Task DisconnectAllAsync() => Task.CompletedTask;
            public IModbusService? GetServiceForProfile(ConnectionProfile profile) => null;
            public void SaveProfiles() { }
            public void LoadProfiles() { }
        }
    }
}
