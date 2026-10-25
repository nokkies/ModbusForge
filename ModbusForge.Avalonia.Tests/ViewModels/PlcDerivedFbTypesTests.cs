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
    /// The Derived FB Types branch of the PLC navigator: every user function block type of
    /// the imported XEF with its interface and sections, and what selecting each one shows.
    /// </summary>
    public sealed class PlcDerivedFbTypesTests : IDisposable
    {
        // Program S instantiates MyAnd (M1) and Secret (Sec1). Program Body has the same name as
        // MyAnd's FBD section. Types, in XEF order: MyAnd (FBD "Body"), Counter (ST "Body", private
        // Step and Hold), Secret (protected, empty), Ladder (LD "Rungs"). The TON EFBSource is not a
        // user type and is not listed.
        private const string FixtureXef = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<FEFExchangeFile>
  <dataBlock>
    <variables name=""M1"" typeName=""MyAnd""/>
    <variables name=""Sec1"" typeName=""Secret""/>
    <variables name=""X"" typeName=""BOOL""/>
    <variables name=""Y"" typeName=""BOOL""/>
    <variables name=""Z"" typeName=""BOOL""/>
    <variables name=""Out"" typeName=""BOOL""/>
    <variables name=""Alarm"" typeName=""BOOL""/>
  </dataBlock>
  <program>
    <identProgram name=""S"" type=""section"" task=""MAST""/>
    <FBDSource>
      <networkFBD>
        <FFBBlock instanceName=""M1"" typeName=""MyAnd"" enEnO=""false"" width=""7"" height=""6"">
          <objPosition posX=""2"" posY=""2""/>
          <descriptionFFB execAfter="""">
            <inputVariable invertedPin=""false"" formalParameter=""A"" effectiveParameter=""X""/>
            <inputVariable invertedPin=""false"" formalParameter=""B"" effectiveParameter=""Y""/>
            <outputVariable invertedPin=""false"" formalParameter=""Q"" effectiveParameter=""Z""/>
          </descriptionFFB>
        </FFBBlock>
        <FFBBlock instanceName=""Sec1"" typeName=""Secret"" enEnO=""false"" width=""7"" height=""6"">
          <objPosition posX=""2"" posY=""12""/>
          <descriptionFFB execAfter="""">
            <outputVariable invertedPin=""false"" formalParameter=""Q"" effectiveParameter=""Out""/>
          </descriptionFFB>
        </FFBBlock>
      </networkFBD>
    </FBDSource>
  </program>
  <program>
    <identProgram name=""Body"" type=""section"" task=""MAST""/>
    <FBDSource>
      <networkFBD>
        <FFBBlock instanceName=""AND1"" typeName=""AND"" width=""7"" height=""5"">
          <objPosition posX=""10"" posY=""2""/>
          <descriptionFFB>
            <inputVariable formalParameter=""IN1"" effectiveParameter=""X""/>
            <inputVariable formalParameter=""IN2"" effectiveParameter=""Y""/>
            <outputVariable formalParameter=""OUT"" effectiveParameter=""Alarm""/>
          </descriptionFFB>
        </FFBBlock>
      </networkFBD>
    </FBDSource>
  </program>
  <FBSource nameOfFBType=""MyAnd"">
    <inputParameters><variables name=""A"" typeName=""BOOL""/><variables name=""B"" typeName=""BOOL""/></inputParameters>
    <outputParameters><variables name=""Q"" typeName=""BOOL""/></outputParameters>
    <FBProgram name=""Body""><FBDSource><networkFBD>
      <FFBBlock instanceName="".1"" typeName=""AND"" enEnO=""false"" width=""7"" height=""6""><objPosition posX=""2"" posY=""2""/>
        <descriptionFFB execAfter="""">
          <inputVariable invertedPin=""false"" formalParameter=""IN1"" effectiveParameter=""A""/>
          <inputVariable invertedPin=""false"" formalParameter=""IN2"" effectiveParameter=""B""/>
          <outputVariable invertedPin=""false"" formalParameter=""OUT"" effectiveParameter=""Q""/>
        </descriptionFFB></FFBBlock>
    </networkFBD></FBDSource></FBProgram>
  </FBSource>
  <EFBSource nameOfEFBType=""TON""><ExternalToolsOnly>
    <inputParameters><variables name=""IN"" typeName=""BOOL""/><variables name=""PT"" typeName=""TIME""/></inputParameters>
    <outputParameters><variables name=""Q"" typeName=""BOOL""/><variables name=""ET"" typeName=""TIME""/></outputParameters>
  </ExternalToolsOnly></EFBSource>
  <FBSource nameOfFBType=""Counter"">
    <inputParameters><variables name=""Pulse"" typeName=""EBOOL""/></inputParameters>
    <outputParameters><variables name=""Count"" typeName=""INT""/><variables name=""Late"" typeName=""BOOL""/></outputParameters>
    <privateLocalVariables>
      <variables name=""Step"" typeName=""INT""><variableInit value=""1""/></variables>
      <variables name=""Hold"" typeName=""TON""/>
    </privateLocalVariables>
    <FBProgram name=""Body""><STSource>IF RE(Pulse) THEN Count := Count + Step; END_IF;
Hold(IN := Pulse, PT := t#300ms);
Late := Hold.Q;</STSource></FBProgram>
  </FBSource>
  <FBSource nameOfFBType=""Secret""><crypted>00</crypted><ExternalToolsOnly>
    <outputParameters><variables name=""Q"" typeName=""BOOL""/></outputParameters></ExternalToolsOnly></FBSource>
  <FBSource nameOfFBType=""Ladder"">
    <FBProgram name=""Rungs""><LDSource/></FBProgram>
  </FBSource>
</FEFExchangeFile>";

        // A project with no user function block types: the same first program as the isolation tests.
        private const string PlainXef = @"<?xml version=""1.0"" encoding=""UTF-8""?>
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
</FEFExchangeFile>";

        // Secret's code is encrypted in the export. No corpus file carries code under a protected type;
        // this FBD program named Body is a defensive case.
        private const string SecretFbdProgram = @"<FBProgram name=""Body""><FBDSource><networkFBD>
  <FFBBlock instanceName="".1"" typeName=""AND"" enEnO=""false"" width=""7"" height=""6""><objPosition posX=""2"" posY=""2""/>
    <descriptionFFB execAfter="""">
      <inputVariable invertedPin=""false"" formalParameter=""IN1"" effectiveParameter=""X""/>
      <inputVariable invertedPin=""false"" formalParameter=""IN2"" effectiveParameter=""Y""/>
      <outputVariable invertedPin=""false"" formalParameter=""OUT"" effectiveParameter=""Alarm""/>
    </descriptionFFB></FFBBlock>
</networkFBD></FBDSource></FBProgram>";

        // An ST program beside the FBD one, so the text header of a program can be checked.
        private const string TextProgram = @"<program><identProgram name=""Cleanup"" type=""section"" task=""MAST""/><STSource>Alarm := FALSE;</STSource></program>";

        // TabItem index of the PLC tab (see PlcSimulationIsolationTests).
        private const int PlcTabIndex = 7;

        private static readonly string[] DataBlockNames = { "M1", "Sec1", "X", "Y", "Z", "Out", "Alarm" };

        private readonly string _xefPath;

        public PlcDerivedFbTypesTests()
        {
            _xefPath = Path.Combine(Path.GetTempPath(), $"plc-dfb-{Guid.NewGuid():N}.xef");
            File.WriteAllText(_xefPath, FixtureXef);
        }

        public void Dispose()
        {
            try { File.Delete(_xefPath); } catch (IOException) { }
        }

        [Fact]
        public async Task DerivedFbTypes_FollowsDataManagement_AndListsEveryType()
        {
            using var simulation = CreateSimulationEditor();
            using var plc = CreatePlcEditor();
            using var main = CreateMainViewModel(simulation, plc);

            await main.LoadPlcXmlCommand.ExecuteAsync(null);

            Assert.Equal(new[] { "Section / Programs", "Data Management", "Derived FB Types" },
                Titles(Node(main, "Application").Children));
            var types = Node(main, "Application", "Derived FB Types");
            Assert.Equal(new[] { "MyAnd", "Counter", "Secret (protected, empty)", "Ladder" }, Titles(types.Children));
            Assert.Equal("4 DFB types, 1 with protected code", types.Detail);
        }

        [Fact]
        public async Task AnExportedType_ShowsItsInterfaceGroupsThenItsSections()
        {
            using var simulation = CreateSimulationEditor();
            using var plc = CreatePlcEditor();
            using var main = CreateMainViewModel(simulation, plc);
            await main.LoadPlcXmlCommand.ExecuteAsync(null);

            Assert.Equal(
                new[] { "Inputs (2)", "Outputs (1)", "Inputs/Outputs (0)", "Public variables (0)", "Private variables (0)", "Sections (1)" },
                Titles(Node(main, "Application", "Derived FB Types", "MyAnd").Children));
            Assert.Equal(new[] { "A", "B" },
                Node(main, "Application", "Derived FB Types", "MyAnd", "Inputs (2)").Variables!.Select(v => v.Name));
            var section = Node(main, "Application", "Derived FB Types", "MyAnd", "Sections (1)", "Body [FBD]");
            Assert.Equal(PlcNodeKind.DfbSectionFbd, section.Kind);
            Assert.Equal("MyAnd/Body", section.ProgramName);
        }

        [Fact]
        public async Task AProtectedType_ShowsAnEmptyLockedSection()
        {
            using var simulation = CreateSimulationEditor();
            using var plc = CreatePlcEditor();
            using var main = CreateMainViewModel(simulation, plc);
            await main.LoadPlcXmlCommand.ExecuteAsync(null);

            Assert.Equal(
                new[] { "Inputs (0)", "Outputs (1)", "Inputs/Outputs (0)", "Public variables (0, may be incomplete)", "Private variables (not in the export)", "Sections (1)" },
                Titles(Node(main, "Application", "Derived FB Types", "Secret (protected, empty)").Children));
            var code = Node(main, "Application", "Derived FB Types", "Secret (protected, empty)", "Sections (1)", "Protected code (empty)");
            Assert.Equal(PlcNodeKind.DfbProtectedCode, code.Kind);
            Assert.True(code.IsLocked);
            Assert.True(code.IsDimmed);
            Assert.Equal(0.55, code.TitleOpacity);
            Assert.Equal("Empty: the code is protected in the export", code.Detail);
        }

        [Fact]
        public async Task SelectingAVariableGroup_ListsThatGroupsVariables()
        {
            using var simulation = CreateSimulationEditor();
            using var plc = CreatePlcEditor();
            using var main = CreateMainViewModel(simulation, plc);
            await main.LoadPlcXmlCommand.ExecuteAsync(null);
            var project = main.PlcProjectViewModel;

            project.SelectedNode = Node(main, "Application", "Derived FB Types", "Counter", "Private variables (2)");

            Assert.True(project.HasVariablesSelected);
            Assert.Equal(new[] { "Step", "Hold" }, project.FilteredVariables.Select(v => v.Name));

            project.SelectedNode = Node(main, "Application", "Data Management");

            Assert.Equal(DataBlockNames, project.FilteredVariables.Select(v => v.Name));
        }

        [Fact]
        public async Task SelectingAnFbdSection_OpensItsCanvas()
        {
            using var simulation = CreateSimulationEditor();
            using var plc = CreatePlcEditor();
            using var main = CreateMainViewModel(simulation, plc);
            await main.LoadPlcXmlCommand.ExecuteAsync(null);

            main.PlcProjectViewModel.SelectedNode = Node(main, "Application", "Derived FB Types", "MyAnd", "Sections (1)", "Body [FBD]");

            Assert.Equal("MyAnd/Body", plc.SelectedProgram?.Name);
            Assert.True(main.PlcProjectViewModel.HasFbdSelected);
            Assert.Equal("DFB section: MyAnd/Body", main.PlcProjectViewModel.CanvasCaption);
        }

        [Fact]
        public async Task SelectingAnStSection_ShowsItsText()
        {
            using var simulation = CreateSimulationEditor();
            using var plc = CreatePlcEditor();
            using var main = CreateMainViewModel(simulation, plc);
            await main.LoadPlcXmlCommand.ExecuteAsync(null);

            main.PlcProjectViewModel.SelectedNode = Node(main, "Application", "Derived FB Types", "Counter", "Sections (1)", "Body [ST]");

            Assert.True(main.PlcProjectViewModel.HasSourceSelected);
            var text = main.PlcProjectViewModel.SelectedSourceText;
            Assert.NotNull(text);
            Assert.StartsWith("IF RE(Pulse) THEN", text);
        }

        [Fact]
        public async Task TextSectionHeaders_NameTheDfbSectionOrTheProgram()
        {
            File.WriteAllText(_xefPath, FixtureXef.Replace("</FEFExchangeFile>", TextProgram + "</FEFExchangeFile>"));
            using var simulation = CreateSimulationEditor();
            using var plc = CreatePlcEditor();
            using var main = CreateMainViewModel(simulation, plc);
            await main.LoadPlcXmlCommand.ExecuteAsync(null);

            main.PlcProjectViewModel.SelectedNode = Node(main, "Application", "Derived FB Types", "Counter", "Sections (1)", "Body [ST]");
            Assert.Equal("DFB section: Counter/Body", main.PlcProjectViewModel.SourceCaption);

            main.PlcProjectViewModel.SelectedNode = Node(main, "Application", "Section / Programs", "(unassigned)", "Cleanup [ST]");
            Assert.Equal("Program: Cleanup [ST]", main.PlcProjectViewModel.SourceCaption);
        }

        [Fact]
        public async Task AProgramAndADfbSectionWithTheSameName_OpenTheirOwnCanvases()
        {
            using var simulation = CreateSimulationEditor();
            using var plc = CreatePlcEditor();
            using var main = CreateMainViewModel(simulation, plc);
            await main.LoadPlcXmlCommand.ExecuteAsync(null);

            main.PlcProjectViewModel.SelectedNode = NavigatorProgram(main, "Body");
            Assert.Equal("Body", plc.SelectedProgram?.Name);

            main.PlcProjectViewModel.SelectedNode = Node(main, "Application", "Derived FB Types", "MyAnd", "Sections (1)", "Body [FBD]");
            Assert.Equal("MyAnd/Body", plc.SelectedProgram?.Name);
        }

        [Fact]
        public async Task DoubleClickingADfbSection_OpensItsCanvasOnThePlcTab()
        {
            using var simulation = CreateSimulationEditor();
            using var plc = CreatePlcEditor();
            using var main = CreateMainViewModel(simulation, plc);
            await main.LoadPlcXmlCommand.ExecuteAsync(null);

            main.OpenPlcProgram(Node(main, "Application", "Derived FB Types", "MyAnd", "Sections (1)", "Body [FBD]"));

            Assert.Equal("MyAnd/Body", plc.SelectedProgram?.Name);
            Assert.Equal(PlcTabIndex, main.SelectedTabIndex);
        }

        [Fact]
        public async Task AnLdSection_IsListedOnly()
        {
            using var simulation = CreateSimulationEditor();
            using var plc = CreatePlcEditor();
            using var main = CreateMainViewModel(simulation, plc);
            await main.LoadPlcXmlCommand.ExecuteAsync(null);
            var rungs = Node(main, "Application", "Derived FB Types", "Ladder", "Sections (1)", "Rungs [LD]");

            Assert.Equal(PlcNodeKind.DfbSectionListed, rungs.Kind);
            main.PlcProjectViewModel.SelectedNode = rungs;
            Assert.True(main.PlcProjectViewModel.ShowsPlaceholder);
        }

        [Fact]
        public async Task ImportingAnotherXef_ReplacesTheDfbTypes()
        {
            using var simulation = CreateSimulationEditor();
            using var plc = CreatePlcEditor();
            using var main = CreateMainViewModel(simulation, plc);
            await main.LoadPlcXmlCommand.ExecuteAsync(null);
            Assert.NotEmpty(Node(main, "Application", "Derived FB Types").Children);
            Assert.Contains("MyAnd/Body", ProgramNames(plc.ProgramTree));
            File.WriteAllText(_xefPath, PlainXef);

            await main.LoadPlcXmlCommand.ExecuteAsync(null);

            Assert.Empty(Node(main, "Application", "Derived FB Types").Children);
            Assert.DoesNotContain("MyAnd/Body", ProgramNames(plc.ProgramTree));
        }

        [Fact]
        public async Task TheStatusTexts_CountEmptyProtectedDfbsApartFromComputedBlocks()
        {
            using var simulation = CreateSimulationEditor();
            using var plc = CreatePlcEditor();
            using var main = CreateMainViewModel(simulation, plc);

            await main.LoadPlcXmlCommand.ExecuteAsync(null);
            Assert.Contains("empty (protected DFB)", main.StatusMessage);

            plc.RunCommand.Execute(null);
            Assert.Contains("empty (protected DFB)", plc.StatusText);
            plc.StopCommand.Execute(null);
        }

        [Fact]
        public async Task AProjectWithoutProgramSections_DoesNotOpenOnADfbCanvas()
        {
            File.WriteAllText(_xefPath, System.Text.RegularExpressions.Regex.Replace(
                FixtureXef, "<program>.*?</program>", "", System.Text.RegularExpressions.RegexOptions.Singleline));
            using var simulation = CreateSimulationEditor();
            using var plc = CreatePlcEditor();
            using var main = CreateMainViewModel(simulation, plc);

            await main.LoadPlcXmlCommand.ExecuteAsync(null);

            Assert.True(main.PlcProjectViewModel.HasProject);
            Assert.Contains("MyAnd/Body", ProgramNames(plc.ProgramTree));
            Assert.NotEqual("MyAnd/Body", plc.SelectedProgram?.Name);
        }

        [Fact]
        public async Task AProtectedTypeWithCodeInItsXml_AddsNoEditorProgram()
        {
            File.WriteAllText(_xefPath, FixtureXef.Replace("<crypted>00</crypted>", "<crypted>00</crypted>" + SecretFbdProgram));
            using var simulation = CreateSimulationEditor();
            using var plc = CreatePlcEditor();
            using var main = CreateMainViewModel(simulation, plc);

            await main.LoadPlcXmlCommand.ExecuteAsync(null);

            Assert.DoesNotContain("Secret/Body", ProgramNames(plc.ProgramTree));
            Assert.Equal(new[] { "Protected code (empty)" },
                Titles(Node(main, "Application", "Derived FB Types", "Secret (protected, empty)", "Sections (1)").Children));
        }

        private MainViewModel CreateMainViewModel(VisualNodeEditorViewModel simulation, PlcEditorViewModel plc)
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

        /// <summary>Walks the navigator from its root by row titles.</summary>
        private static PlcTreeNodeViewModel Node(MainViewModel main, params string[] titles)
        {
            IEnumerable<PlcTreeNodeViewModel> level = main.PlcProjectViewModel.RootNodes;
            PlcTreeNodeViewModel? node = null;
            foreach (var title in titles)
            {
                node = level.Single(n => n.Title == title);
                level = node.Children;
            }
            return node ?? throw new ArgumentException("At least one title is needed.", nameof(titles));
        }

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

        private static string[] Titles(IEnumerable<PlcTreeNodeViewModel> nodes) => nodes.Select(n => n.Title).ToArray();

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
