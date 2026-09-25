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
    /// Run on the PLC tab executes the imported project like the controller: every
    /// FBD section, whichever one is on screen, reading and writing the variables on
    /// the Modbus registers. The canvas shows the section on screen's values.
    /// </summary>
    public sealed class PlcRuntimeRunTests : IDisposable
    {
        private const string MotorXef = @"<FEFExchangeFile>
  <dataBlock>
    <variables name=""StartPb"" typeName=""EBOOL"" topologicalAddress=""%M1""/>
    <variables name=""StopPb"" typeName=""EBOOL"" topologicalAddress=""%M2""/>
    <variables name=""MotorRun"" typeName=""EBOOL"" topologicalAddress=""%M10""/>
    <variables name=""Setpoint"" typeName=""INT"" topologicalAddress=""%MW5""/>
    <variables name=""Command"" typeName=""INT"" topologicalAddress=""%MW10""/>
    <variables name=""Limit"" typeName=""INT"" topologicalAddress=""%MW11""/>
  </dataBlock>
  <program>
    <identProgram name=""Motor"" type=""section"" task=""MAST""/>
    <FBDSource><networkFBD>
      <FFBBlock instanceName="".1"" typeName=""SET"" additionnalPinNumber=""0"" enEnO=""true"" width=""7"" height=""5"">
        <objPosition posX=""10"" posY=""2""/>
        <descriptionFFB execAfter="""">
          <inputVariable invertedPin=""false"" formalParameter=""EN"" effectiveParameter=""StartPb""/>
          <outputVariable invertedPin=""false"" formalParameter=""ENO""/>
          <outputVariable invertedPin=""false"" formalParameter=""OUT"" effectiveParameter=""MotorRun""/>
        </descriptionFFB>
      </FFBBlock>
      <FFBBlock instanceName="".2"" typeName=""RESET"" additionnalPinNumber=""0"" enEnO=""true"" width=""7"" height=""5"">
        <objPosition posX=""10"" posY=""12""/>
        <descriptionFFB execAfter="""">
          <inputVariable invertedPin=""false"" formalParameter=""EN"" effectiveParameter=""StopPb""/>
          <outputVariable invertedPin=""false"" formalParameter=""ENO""/>
          <outputVariable invertedPin=""false"" formalParameter=""OUT"" effectiveParameter=""MotorRun""/>
        </descriptionFFB>
      </FFBBlock>
    </networkFBD></FBDSource>
  </program>
  <program>
    <identProgram name=""Speed"" type=""section"" task=""MAST""/>
    <FBDSource><networkFBD>
      <FFBBlock instanceName="".1"" typeName=""MOVE"" additionnalPinNumber=""0"" enEnO=""false"" width=""7"" height=""5"">
        <objPosition posX=""10"" posY=""2""/>
        <descriptionFFB execAfter="""">
          <inputVariable invertedPin=""false"" formalParameter=""IN"" effectiveParameter=""Setpoint""/>
          <outputVariable invertedPin=""false"" formalParameter=""OUT"" effectiveParameter=""Command""/>
        </descriptionFFB>
      </FFBBlock>
      <FFBBlock instanceName="".2"" typeName=""MOVE"" additionnalPinNumber=""0"" enEnO=""false"" width=""7"" height=""5"">
        <objPosition posX=""10"" posY=""12""/>
        <descriptionFFB execAfter="""">
          <inputVariable invertedPin=""false"" formalParameter=""IN"" effectiveParameter=""250""/>
          <outputVariable invertedPin=""false"" formalParameter=""OUT"" effectiveParameter=""Limit""/>
        </descriptionFFB>
      </FFBBlock>
    </networkFBD></FBDSource>
  </program>
  <logicConf><resource><taskDesc task=""MAST"" taskType=""cyclic"">
    <sectionDesc name=""Motor""/><sectionDesc name=""Speed""/>
  </taskDesc></resource></logicConf>
</FEFExchangeFile>";

        private static readonly TimeSpan Scan = TimeSpan.FromMilliseconds(100);

        private readonly string _xefPath;

        public PlcRuntimeRunTests()
        {
            _xefPath = Path.Combine(Path.GetTempPath(), $"plc-run-{Guid.NewGuid():N}.xef");
            File.WriteAllText(_xefPath, MotorXef);
        }

        public void Dispose()
        {
            try { File.Delete(_xefPath); } catch (IOException) { }
        }

        [Fact]
        public async Task Run_ExecutesTheImportedLogicAgainstTheRegisters()
        {
            using var plc = await LoadedPlcEditor();
            plc.RunCommand.Execute(null);
            var store = plc.PlcRuntime.CurrentDataStore;

            store.CoilDiscretes[1] = true;
            plc.PlcRuntime.Tick(Scan);
            Assert.True(store.CoilDiscretes[10]);

            store.CoilDiscretes[1] = false;
            plc.PlcRuntime.Tick(Scan);
            Assert.True(store.CoilDiscretes[10]);

            store.CoilDiscretes[2] = true;
            plc.PlcRuntime.Tick(Scan);
            Assert.False(store.CoilDiscretes[10]);
        }

        [Fact]
        public async Task Run_ExecutesSectionsThatAreNotOnScreen()
        {
            using var plc = await LoadedPlcEditor();
            Assert.Equal("Motor", plc.SelectedProgram?.Name);
            plc.RunCommand.Execute(null);
            var store = plc.PlcRuntime.CurrentDataStore;

            store.HoldingRegisters[5] = 321;
            plc.PlcRuntime.Tick(Scan);

            Assert.Equal(321, store.HoldingRegisters[10]);
        }

        [Fact]
        public async Task Run_ShowsTheValuesOfTheSectionOnScreen()
        {
            using var plc = await LoadedPlcEditor();
            plc.RunCommand.Execute(null);
            plc.PlcRuntime.CurrentDataStore.CoilDiscretes[1] = true;

            plc.PlcRuntime.Tick(Scan);

            var set = plc.Nodes.Single(n => n.Plc?.TypeName == "SET");
            Assert.NotNull(set.PlcLive);
            Assert.Equal("TRUE", set.PlcLive!.Inputs["EN"]);
            Assert.Equal("TRUE", set.PlcLive.Outputs["OUT"]);
            Assert.True(set.PlcLive.Executed);

            var reset = plc.Nodes.Single(n => n.Plc?.TypeName == "RESET");
            Assert.False(reset.PlcLive!.Executed);
        }

        [Fact]
        public async Task SwitchingTheSectionOnScreen_KeepsThePlcRunning()
        {
            using var plc = await LoadedPlcEditor();
            plc.RunCommand.Execute(null);
            var store = plc.PlcRuntime.CurrentDataStore;

            plc.SelectedProgram = Programs(plc.ProgramTree).Single(p => p.Name == "Speed");
            store.HoldingRegisters[5] = 42;
            store.CoilDiscretes[1] = true;
            plc.PlcRuntime.Tick(Scan);

            Assert.True(plc.IsRunning);
            Assert.True(store.CoilDiscretes[10]);
            var move = plc.Nodes.First(n => n.Plc?.TypeName == "MOVE");
            Assert.Equal("42", move.PlcLive!.Outputs["OUT"]);
        }

        [Fact]
        public async Task Run_TagsVariablesButNotConstants()
        {
            using var plc = await LoadedPlcEditor();
            plc.SelectedProgram = Programs(plc.ProgramTree).Single(p => p.Name == "Speed");
            plc.RunCommand.Execute(null);

            plc.PlcRuntime.Tick(Scan);

            var constant = plc.Nodes.Single(n => n.Plc?.Pins.Any(p => p.ActualParameter == "250") == true);
            Assert.False(constant.PlcLive!.Inputs.ContainsKey("IN"));
            Assert.Equal("250", constant.PlcLive.Outputs["OUT"]);
            var variable = plc.Nodes.Single(n => n.Plc?.Pins.Any(p => p.ActualParameter == "Setpoint") == true);
            Assert.Equal("0", variable.PlcLive!.Inputs["IN"]);
        }

        [Fact]
        public async Task Run_FeedsTheNodesPrimaryValueToTheControlsPanel()
        {
            using var plc = await LoadedPlcEditor();
            plc.SelectedProgram = Programs(plc.ProgramTree).Single(p => p.Name == "Speed");
            plc.RunCommand.Execute(null);
            plc.PlcRuntime.CurrentDataStore.HoldingRegisters[5] = 42;

            plc.PlcRuntime.Tick(Scan);

            var move = plc.Nodes.Single(n => n.Plc?.Pins.Any(p => p.ActualParameter == "Setpoint") == true);
            Assert.Equal(42, move.CurrentValueDouble);
            Assert.Equal(0, plc.PlcRuntime.CurrentDataStore.HoldingRegisters[4]);
        }

        [Fact]
        public async Task RunStatus_NamesTheStoreOnce()
        {
            using var plc = await LoadedPlcEditor();

            plc.RunCommand.Execute(null);

            Assert.EndsWith(", local store (offline)", plc.StatusText);
        }

        [Fact]
        public async Task Stop_HaltsTheScan_AndClearsTheLiveValues()
        {
            using var plc = await LoadedPlcEditor();
            plc.RunCommand.Execute(null);
            var store = plc.PlcRuntime.CurrentDataStore;
            plc.PlcRuntime.Tick(Scan);

            plc.StopCommand.Execute(null);
            store.HoldingRegisters[5] = 7;
            plc.PlcRuntime.Tick(Scan);

            Assert.False(plc.IsRunning);
            Assert.Equal(0, store.HoldingRegisters[10]);
            Assert.All(plc.Nodes.Where(n => n.Plc != null), n => Assert.Null(n.PlcLive));
        }

        [Fact]
        public async Task RunStatus_SaysHowMuchOfTheProjectRuns()
        {
            using var plc = await LoadedPlcEditor();

            plc.RunCommand.Execute(null);

            Assert.Contains("2 sections", plc.StatusText);
            Assert.Contains("4 of 4 blocks", plc.StatusText);
        }

        private async Task<PlcEditorViewModel> LoadedPlcEditor()
        {
            var plc = new PlcEditorViewModel(NoopTagWindowService.Instance);
            // The timer must not scan behind the test's back: every scan is a Tick.
            plc.ScanIntervalMs = 10_000;
            using var main = new MainViewModel(
                new NullConnectionManager(),
                NullLogger<MainViewModel>.Instance,
                new SyncDispatcher(),
                fileDialogService: new FixedPathFileDialogService(_xefPath),
                plcEditorViewModel: plc);
            await main.LoadPlcXmlCommand.ExecuteAsync(null);
            Assert.True(main.PlcProjectViewModel.HasProject, "the XEF must import");
            return plc;
        }

        private static IEnumerable<ProgramModel> Programs(ProgramFolder folder)
            => folder.Programs.Concat(folder.Folders.SelectMany(Programs));

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
