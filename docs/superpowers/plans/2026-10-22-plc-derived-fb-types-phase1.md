# PLC Derived FB Types (phase 1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The PLC tab's navigator lists every DFB type of an imported XEF under "Derived FB Types", opens their sections like program sections, and marks protected types as empty shells in the navigator, on the canvas, in the Console and in the status text.

**Architecture:** The runtime already knows which DFB types are protected (`<crypted>`); task 1 makes that a flag on the type and the block. The importer (`PlcXmlImporter`) gains a display model of each DFB type (interface groups and sections, FBD sections parsed into canvases with their own node ids), mirroring how it already keeps `PlcXmlVariable` apart from the runtime's `PlcVariable`. The navigator (`PlcProjectViewModel`) builds the new tree from that model; FBD sections are loaded into the PLC editor as programs named `Type/Section`, so selecting one reuses the program canvas path.

**Tech Stack:** C# / .NET 8, Avalonia 11, CommunityToolkit.Mvvm, xUnit.

**Spec:** `docs/superpowers/specs/2026-10-22-plc-derived-fb-types-design.md` (phase 1 only).

## Global Constraints

- A DFB is a shell only when its FBSource has a `<crypted>` child. Every other DFB keeps its code.
- Exact UI copy: protected section note `Empty: the code is protected in the export`; canvas note for protected DFB instances `empty (protected DFB)`; unsupported types keep `not simulated`.
- No client DFB behaviour is built into ModbusForge; nothing is sent off the machine.
- DFB sections are display only in this phase: they are never added to the runtime project (`CompileProject`), and their canvas nodes never map to runtime blocks.
- No em or en dashes in new UI strings, comments or docs.
- Code style (AGENTS.md): `ILogger` for logging, constants for magic numbers, XML doc comments on public members as the surrounding code has them.
- Build and test with `-c Release`: the user's running app locks `bin/Debug`.
- One commit per task, message ending with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Do not push.

## Review Focus

- A DFB section named like a program section (both "Body"): each opens its own canvas. Test in Task 5.
- A DFB type with an interface but no code and no `<crypted>`: listed, not marked protected. Test in Task 4.
- An LD section in a DFB: listed as `Rungs [LD]`, selecting it shows the placeholder, nothing crashes. Test in Task 5.
- Running the PLC while a DFB section is on screen: its nodes get no live values. Test in Task 4.
- Importing a second XEF: the old DFB types and their editor programs are gone. Test in Task 5.

---

### Task 1: Protected DFB flags in the runtime model

**Files:**
- Modify: `ModbusForge.Core/Plc/PlcType.cs` (next to `Dfb`, line ~231)
- Modify: `ModbusForge.Core/Plc/PlcTypeRegistry.cs` (constructor's `FBSource` case; the DFB build that calls `ReadCode`)
- Modify: `ModbusForge.Core/Plc/PlcProject.cs` (`PlcBlock`, next to `IsSimulated`)
- Test: `ModbusForge.Tests/Plc/PlcDfbTests.cs`

**Interfaces:**
- Produces: `PlcType.IsProtected` (`bool`, public get, internal set); `PlcTypeRegistry.ProtectedDfbTypes` (`IReadOnlyList<string>`, FBSource names with `<crypted>`, in XEF order); `PlcBlock.IsProtectedDfb` (`bool`: a user FB block whose instance type `IsProtected`).

- [ ] **Step 1: Write the failing test**

Move the inline FBSource of `EncryptedDfb_StaysInert` into a `SecretDfb` constant (same text) and use it there too. Add:

```csharp
[Fact]
public void EncryptedDfb_IsAProtectedShell()
{
    var plc = new PlcHarness(new XefBuilder().Raw(SecretDfb).Raw(AndDfb)
        .Variable("S1", "Secret").Variable("A1", "MyAnd").Variable("Out", "BOOL").Variable("Q", "BOOL")
        .Section("S", Block("S1", "Secret", 2, 2, "|Q=Out") + Block("A1", "MyAnd", 20, 2, "A=TRUE,B=TRUE|Q=Q"))
        .Project());

    Assert.True(plc.BlockNamed("S1").IsProtectedDfb);
    Assert.False(plc.BlockNamed("A1").IsProtectedDfb);
    Assert.Equal(new[] { "Secret" }, plc.Runtime.Project.Types!.ProtectedDfbTypes);
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test ModbusForge.Tests/ModbusForge.Tests.csproj -c Release --filter "FullyQualifiedName~PlcDfbTests"`
Expected: build error, `IsProtectedDfb` and `ProtectedDfbTypes` do not exist.

- [ ] **Step 3: Implement**

`PlcType.IsProtected { get; internal set; }`, set where the registry builds a DFB type (`if (dfb) ...`) from `source.Element("crypted") != null`. `ProtectedDfbTypes` is filled in the constructor's `FBSource` case. `PlcBlock.IsProtectedDfb => Kind == PlcBlockKind.UserFunctionBlock && InstanceRoot?.Type is { IsProtected: true };`

- [ ] **Step 4: Run the tests**

Run: same command. Expected: all `PlcDfbTests` pass.

- [ ] **Step 5: Commit** (`PLC runtime: mark DFB types whose code is protected`)

---

### Task 2: Live state and Console name protected DFBs

**Files:**
- Modify: `ModbusForge.Core/Models/PlcBlockModels.cs` (`PlcLiveState`, line ~70)
- Modify: `ModbusForge.Core/Services/PlcRuntimeService.cs` (`LiveState`, `Start`)
- Test: `ModbusForge.Tests/Plc/PlcRuntimeServiceTests.cs`

**Interfaces:**
- Consumes: `PlcBlock.IsProtectedDfb`, `PlcTypeRegistry.ProtectedDfbTypes` (Task 1).
- Produces: `PlcLiveState(Inputs, Outputs, Executed, Simulated, bool ProtectedDfb = false)`; `SameAs` also compares `ProtectedDfb`. `Start` raises `ProblemReported` once with the text below when the project has protected types.

- [ ] **Step 1: Write the failing tests**

Add a `SecretDfb` constant (same as Task 1) and a helper that subscribes to `ProblemReported` before `Start`:

```csharp
private static (PlcRuntimeService Service, ObservableCollection<VisualNode> Nodes) Started(XefBuilder xef, List<string> reports)
// import xef.Build(), nodes = the single section's nodes, service.ProblemReported += reports.Add,
// Load, SetScanIntervalMs(10_000), Start(new VisualNodeEditorConfig { Nodes = nodes, ScanIntervalMs = 10_000 })

[Fact]
public void ProtectedDfbInstance_IsTaggedAsAnEmptyProtectedDfb()
{
    var reports = new List<string>();
    var (service, nodes) = Started(new XefBuilder().Raw(SecretDfb).Variable("S1", "Secret").Variable("Out", "BOOL")
        .Section("S", Block("S1", "Secret", 2, 2, "|Q=Out")), reports);
    using var _ = service;
    service.Tick(Scan);

    var live = Node(nodes, "Secret").PlcLive!;
    Assert.False(live.Simulated);
    Assert.True(live.ProtectedDfb);
}

[Fact]
public void Start_NamesTheProtectedDfbTypes()
{
    // same project as above
    Assert.Single(reports, "protected DFB types run empty (their code is protected in the export): Secret");
}

[Fact]
public void Start_WithoutProtectedDfbs_SaysNothingAboutThem()
{
    // Block(".1", "MOVE", 2, 2, "IN=A|OUT=B") with A, B INT
    Assert.DoesNotContain(reports, r => r.Contains("protected DFB"));
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test ModbusForge.Tests/ModbusForge.Tests.csproj -c Release --filter "FullyQualifiedName~PlcRuntimeServiceTests"`
Expected: build error on `ProtectedDfb`.

- [ ] **Step 3: Implement**

`LiveState` passes `block.IsProtectedDfb`. In `Start`, take the names from `_runtime?.Project.Types?.ProtectedDfbTypes` inside the lock and call `Report($"protected DFB types run empty (their code is protected in the export): {string.Join(", ", names)}")` after the lock is released, only when there are names.

- [ ] **Step 4: Run the tests**

Run: same command. Expected: all pass.

- [ ] **Step 5: Commit** (`PLC runtime: tell protected DFBs apart in live values and the Console`)

---

### Task 3: Block counts, canvas note and status texts

**Files:**
- Create: `ModbusForge.Core/Plc/PlcBlockCounts.cs`
- Modify: `ModbusForge/Views/PlcBlockView.cs` (the "not simulated" draw, line ~144)
- Modify: `ModbusForge/ViewModels/PlcEditorViewModel.cs` (`RunningStatusText`)
- Modify: `ModbusForge/ViewModels/MainViewModel.cs` (import `StatusMessage` in `LoadPlcXmlAsync`, line ~4042)
- Test: `ModbusForge.Tests/Plc/PlcDfbTests.cs`, create `ModbusForge.Avalonia.Tests/Views/PlcBlockViewNoteTests.cs`

**Interfaces:**
- Consumes: `PlcBlock.IsProtectedDfb` (Task 1), `PlcLiveState.ProtectedDfb` (Task 2).
- Produces: `public readonly record struct PlcBlockCounts(int Total, int Computed, int EmptyProtectedDfb, int Unsupported)` with `public static PlcBlockCounts Of(IEnumerable<PlcBlock> blocks)` and `public string Describe()`; `internal static string? PlcBlockView.IdleNote(PlcLiveState? live)`.

- [ ] **Step 1: Write the failing tests**

```csharp
// PlcDfbTests
[Fact]
public void BlockCounts_TellEmptyProtectedDfbsFromUnsupportedTypes()
{
    var project = new XefBuilder().Raw(SecretDfb).Raw(AndDfb)
        .Variable("S1", "Secret").Variable("A1", "MyAnd").Variable("Out", "BOOL").Variable("Q", "BOOL")
        .Section("S", Block("S1", "Secret", 2, 2, "|Q=Out") + Block("A1", "MyAnd", 20, 2, "A=TRUE,B=TRUE|Q=Q")
            + Block(".3", "NO_SUCH_BLOCK", 40, 2, "IN=Q|OUT=Out"))
        .Project();

    var counts = PlcBlockCounts.Of(project.Blocks);

    Assert.Equal(new PlcBlockCounts(3, 1, 1, 1), counts);
    Assert.Equal("1 of 3 blocks computed, 1 empty (protected DFB), 1 not simulated (unsupported type)", counts.Describe());
}

// PlcBlockViewNoteTests
[Fact]
public void IdleNote_SaysWhyABlockIsNotComputed()
{
    var none = new Dictionary<string, string>();
    Assert.Equal("empty (protected DFB)", PlcBlockView.IdleNote(new PlcLiveState(none, none, true, false, ProtectedDfb: true)));
    Assert.Equal("not simulated", PlcBlockView.IdleNote(new PlcLiveState(none, none, true, false)));
    Assert.Null(PlcBlockView.IdleNote(new PlcLiveState(none, none, true, true)));
    Assert.Null(PlcBlockView.IdleNote(null));
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test ModbusForge.Tests/ModbusForge.Tests.csproj -c Release --filter "FullyQualifiedName~PlcDfbTests"` and `dotnet test ModbusForge.Avalonia.Tests/ModbusForge.Avalonia.Tests.csproj -c Release --filter "FullyQualifiedName~PlcBlockViewNoteTests"`
Expected: build errors on `PlcBlockCounts` and `IdleNote`.

- [ ] **Step 3: Implement**

`Describe()` returns `"{Computed} of {Total} blocks computed"` followed by `", {n} empty (protected DFB)"` and `", {n} not simulated (unsupported type)"`, each only when its count is above 0. `Unsupported` counts blocks that are neither simulated nor protected DFBs. `PlcBlockView` draws `IdleNote(live)` where it drew "not simulated". `RunningStatusText` becomes `$"Running {sections} sections: {PlcBlockCounts.Of(project.Blocks).Describe()}, {storeMode}"`; the import status replaces its "Run computes ... keep their outputs (...)" part with `$"Run: {counts.Describe()}"`.

- [ ] **Step 4: Run the tests**

Run: both commands from Step 2. Expected: pass.

- [ ] **Step 5: Commit** (`PLC tab: say "empty (protected DFB)" apart from unsupported types`)

---

### Task 4: DFB types in the import result

**Files:**
- Modify: `ModbusForge.Core/Services/PlcXmlImporter.cs` (`ImportFromXml`, `ParseSections`, `ParseFbdSource` lines ~602-720, models near `PlcXmlSection` line ~1334, `PlcXmlImportResult`)
- Test: create `ModbusForge.Tests/Services/PlcXmlDfbTypeImportTests.cs`

**Interfaces:**
- Produces:

```csharp
public sealed class PlcXmlDfbType
{
    public string Name { get; init; } = "";
    public string Version { get; init; } = "";
    public bool IsProtected { get; init; }             // FBSource has <crypted>
    public List<PlcXmlVariable> Inputs { get; } = new();
    public List<PlcXmlVariable> Outputs { get; } = new();
    public List<PlcXmlVariable> InOuts { get; } = new();
    public List<PlcXmlVariable> PublicVariables { get; } = new();
    public List<PlcXmlVariable> PrivateVariables { get; } = new();
    public List<PlcXmlDfbSection> Sections { get; } = new();
}

public sealed class PlcXmlDfbSection
{
    public string Name { get; init; } = "";
    public string Language { get; init; } = "";        // "FBD", "ST", "LD", "IL", "SFC"
    public string EditorKey { get; init; } = "";       // "<type>/<section>", the PLC editor program name
    public PlcXmlSection? Canvas { get; init; }        // FBD only
    public string? SourceText { get; init; }           // ST and IL only
}
```

and `PlcXmlImportResult.DfbTypes` (`List<PlcXmlDfbType>`, root FBSource elements in XEF order).

- [ ] **Step 1: Write the failing tests**

Fixtures as constants in the test class: `AndDfb` (FBD section `Body` with one AND `.1`), `CounterDfb` (with the TON EFBSource; ST section `Body` starting `IF RE(Pulse) THEN`; private `Step` INT init `1`, `Hold` TON), `SecretDfb` (as Task 1), `EmptyDfb` (`<FBSource nameOfFBType="Blank">` with one input, no FBProgram, no crypted). Copy the first three from `ModbusForge.Tests/Plc/PlcDfbTests.cs`. Tests:

```csharp
[Fact] public void ExportedDfbTypes_AreListedWithTheirInterface()
// DfbTypes names == ["MyAnd", "Counter"]; MyAnd: Inputs A,B; Outputs Q; InOuts empty; !IsProtected
// Counter: PrivateVariables names Step, Hold; Step.InitialValue == "1"; Hold.TypeName == "TON"

[Fact] public void AnFbdSection_GetsACanvasKeyedByTypeAndSection()
// MyAnd.Sections.Single(): Name "Body", Language "FBD", EditorKey "MyAnd/Body",
// Canvas!.Nodes contains one node with Plc?.TypeName == "AND", SourceText null

[Fact] public void AnStSection_KeepsItsText()
// Counter.Sections.Single(): Language "ST", Canvas null, SourceText starts with "IF RE(Pulse) THEN"

[Fact] public void AProtectedType_IsAShellWithItsInterface()
// Secret: IsProtected, Outputs names [Q], Sections empty, PrivateVariables empty

[Fact] public void ATypeWithoutCode_IsNotProtected()
// Blank: !IsProtected, Sections empty, Inputs count 1

[Fact] public void DfbCanvases_NeverShareNodeIdsWithPrograms_AndAreNotRun()
// AndDfb + program "S" with Block(".1", "AND", 2, 2, "IN1=X,IN2=Y|OUT=Z"):
// DFB canvas ids and program node ids do not intersect; every DFB canvas id starts with "D";
// result.Project!.Sections names == ["S"]

[Fact] public void ADfbCanvasWithRepeatedInstanceNames_GetsDistinctIds()
// an FBD section with two networkFBD elements, each holding an AND ".1": the canvas node ids are distinct

[Fact] public void DfbCanvasNodes_GetNoLiveValues_WhileRunning()
// Load result.Project into a PlcRuntimeService, Start with Nodes = MyAnd/Body canvas nodes, Tick:
// every node's PlcLive is null
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test ModbusForge.Tests/ModbusForge.Tests.csproj -c Release --filter "FullyQualifiedName~PlcXmlDfbTypeImportTests"`
Expected: build error, `DfbTypes` does not exist.

- [ ] **Step 3: Implement `ParseDfbTypes(XDocument xef, IReadOnlyDictionary<string, BlockPinPositions> pinPositions, PlcXmlImportResult result)`**

Called in `ImportFromXml` after `ParseSections`. Per root `FBSource` (type index `t`):
- Variables per group from the FBSource and its `ExternalToolsOnly`, first name wins, built like `ParseTagTable` builds `PlcXmlVariable` (no address, `variableInit/@value`, `CleanText(comment)`).
- Per `FBProgram` (section index `i`): the language is the first child element named `*Source` (skip `comment`). FBD: `ParseFbdSource(fbd, localTags, pinPositions, name, "", 0, $"D{t}.{i}", scratch)`, then clear the canvas's `RuntimeBlocks` and `RuntimeLinks` and copy the scratch warnings into `result` prefixed `"DFB {type}: "`. `localTags` maps each of the type's variable names to `new PlcAddressReference { SymbolicName = name }`, so a local name never picks up a global's address. ST and IL: `NormalizeSourceText`. Others: listed only.
- `ParseFbdSource` gets a `string idPrefix` parameter after `sectionOrdinal`; node ids become `$"{idPrefix}_{uniqueInstance}"` and text boxes `$"{idPrefix}_#text{n}"`; `ParseSections` passes `$"S{sectionOrdinal}"`, so program ids do not change.
- `EnsureGlobalNodeIdUniqueness` takes the sections to check (`IEnumerable<PlcXmlSection>`) and runs once over the program sections and the DFB canvases together: Control Expert reuses instance names across networks, and duplicate ids crash the canvas.

- [ ] **Step 4: Run the tests, then the importer and PLC suites**

Run: Step 2's command, then `dotnet test ModbusForge.Tests/ModbusForge.Tests.csproj -c Release --filter "FullyQualifiedName!~UITests & FullyQualifiedName!~SmokeTests"`
Expected: all pass.

- [ ] **Step 5: Commit** (`PLC import: DFB types with their interface and sections`)

---

### Task 5: Derived FB Types in the navigator

**Files:**
- Modify: `ModbusForge/ViewModels/PlcProjectViewModel.cs` (`PlcNodeKind`, `PlcTreeNodeViewModel`, `LoadProject`, `BuildApplicationNode`, `SelectedNode`, `FilteredVariables`)
- Modify: `ModbusForge/ViewModels/MainViewModel.cs` (`LoadPlcXmlAsync` program list; `PlcProjectSelectionChanged` and `OpenPlcProgram` kind checks)
- Modify: `ModbusForge/Views/MainView.axaml` (navigator item template line ~266; canvas header line ~280)
- Test: create `ModbusForge.Avalonia.Tests/ViewModels/PlcDerivedFbTypesTests.cs`

**Interfaces:**
- Consumes: `PlcXmlImportResult.DfbTypes`, `PlcXmlDfbType`, `PlcXmlDfbSection` (Task 4).
- Produces: `PlcNodeKind` members `DfbTypesRoot, DfbType, DfbVariables, DfbSectionsRoot, DfbSectionFbd, DfbSectionText, DfbSectionListed, DfbProtectedCode`; on `PlcTreeNodeViewModel`: `IReadOnlyList<PlcXmlVariable>? Variables { get; init; }`, `bool IsLocked { get; init; }`, `bool IsDimmed { get; init; }`, `double TitleOpacity` (0.55 when dimmed, as a named constant); `PlcProjectViewModel.CanvasCaption` (`"FBD program: {name}"` or `"DFB section: {key}"`).

Tree (titles exact), after "Data Management" under Application, always present:
- `Derived FB Types`, then per type in XEF order: `{Name}`, or `{Name} (protected, empty)` when protected.
- Per type: `Inputs ({n})`, `Outputs ({n})`, `Inputs/Outputs ({n})`, `Public variables ({n})`, `Private variables ({n})` (protected: `Private variables (not in the export)`), each `DfbVariables` with its list; then `Sections ({n})`.
- Sections: `{Name} [{Language}]` as `DfbSectionFbd` (`ProgramName` = `EditorKey`), `DfbSectionText` (`SourceText`) or `DfbSectionListed`; a protected type has one `Protected code (empty)` (`DfbProtectedCode`, `IsLocked`, `IsDimmed`, `Detail` = the spec's note).

Selection: `DfbSectionFbd` behaves as `ProgramFbd`, `DfbSectionText` as `ProgramText`; `DfbVariables` shows its own list in the variables grid (Data Management shows the project's again). `SectionsByName` also holds the DFB canvases by `EditorKey`. `MainViewModel` appends the DFB canvases to `LoadImportedPrograms` as `(EditorKey, Canvas.Nodes, Canvas.Connections)` and accepts `DfbSectionFbd` wherever it accepts `ProgramFbd`. The item template shows a small lock `PathIcon` when `IsLocked` and binds the text's `Opacity` to `TitleOpacity`; the canvas header binds `CanvasCaption`.

- [ ] **Step 1: Write the failing tests**

Set up like `PlcSimulationIsolationTests` (temp `.xef`, `FixedPathFileDialogService`, `SyncDispatcher`, `main.LoadPlcXmlCommand`). The fixture XEF: dataBlock variables; program `S` with instances of `MyAnd` and `Secret`; program `Body` (FBD, one AND); FBSources `MyAnd` (FBD `Body`), the TON EFBSource, `Counter` (ST `Body`, private `Step`, `Hold`), `Secret` (crypted), `Ladder` (`<FBProgram name="Rungs"><LDSource/></FBProgram>`). A helper `Node(main, params string[] titles)` walks titles from the root.

```csharp
[Fact] public async Task DerivedFbTypes_FollowsDataManagement_AndListsEveryType()
// Application children titles start with ["Section / Programs", "Data Management", "Derived FB Types"]
// Derived FB Types children == ["MyAnd", "Counter", "Secret (protected, empty)", "Ladder"]

[Fact] public async Task AnExportedType_ShowsItsInterfaceGroupsThenItsSections()
// MyAnd children == ["Inputs (2)", "Outputs (1)", "Inputs/Outputs (0)", "Public variables (0)",
//                    "Private variables (0)", "Sections (1)"]; Sections child "Body [FBD]"

[Fact] public async Task AProtectedType_ShowsAnEmptyLockedSection()
// Secret children end with "Private variables (not in the export)", "Sections (1)";
// its section "Protected code (empty)": IsLocked, IsDimmed, Detail "Empty: the code is protected in the export"

[Fact] public async Task SelectingAVariableGroup_ListsThatGroupsVariables()
// Counter > "Private variables (2)": HasVariablesSelected, FilteredVariables names ["Step", "Hold"];
// then "Data Management": FilteredVariables names == the dataBlock's

[Fact] public async Task SelectingAnFbdSection_OpensItsCanvas()
// MyAnd > Sections (1) > "Body [FBD]": plc.SelectedProgram!.Name == "MyAnd/Body", HasFbdSelected,
// CanvasCaption == "DFB section: MyAnd/Body"

[Fact] public async Task SelectingAnStSection_ShowsItsText()
// Counter > Sections (1) > "Body [ST]": HasSourceSelected, SelectedSourceText starts with "IF RE(Pulse) THEN"

[Fact] public async Task AProgramAndADfbSectionWithTheSameName_OpenTheirOwnCanvases()
// program "Body" opens plc program "Body"; MyAnd's "Body [FBD]" opens "MyAnd/Body"

[Fact] public async Task AnLdSection_IsListedOnly()
// Ladder > Sections (1) > "Rungs [LD]" is DfbSectionListed; selecting it: ShowsPlaceholder

[Fact] public async Task ImportingAnotherXef_ReplacesTheDfbTypes()
// overwrite the temp file with PlcSimulationIsolationTests' SampleXef-like content (no FBSource), import again:
// Derived FB Types has no children; the PLC editor has no "MyAnd/Body" program
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test ModbusForge.Avalonia.Tests/ModbusForge.Avalonia.Tests.csproj -c Release --filter "FullyQualifiedName~PlcDerivedFbTypesTests"`
Expected: build errors on the new `PlcNodeKind` members.

- [ ] **Step 3: Implement as described above**

`BuildDfbTypesNode(PlcXmlImportResult result)` sits beside `BuildApplicationNode`; the `Derived FB Types` node's `Detail` is `"{n} DFB types, {p} with protected code"`.

- [ ] **Step 4: Run the tests, then the whole Avalonia suite**

Run: Step 2's command, then `dotnet test ModbusForge.Avalonia.Tests/ModbusForge.Avalonia.Tests.csproj -c Release`
Expected: all pass, including `PlcSimulationIsolationTests`.

- [ ] **Step 5: Commit** (`PLC navigator: Derived FB Types like Control Expert`)

---

### Task 6: Acceptance on the corpus

**Files:**
- Modify: `ModbusForge.Tests/SmokeTests/PlcXefCorpusSmokeTests.cs`

**Interfaces:**
- Consumes: `PlcXmlImportResult.DfbTypes` (Task 4), `PlcBlock.IsProtectedDfb` (Task 1).

- [ ] **Step 1: Write the tests**

```csharp
[Theory, MemberData(nameof(CorpusFiles))]
public void CorpusFile_ListsEveryDfbType(string fileName)
// skip as the other theories do when the corpus is missing.
// DfbTypes names == root FBSource nameOfFBType values in order; IsProtected == has <crypted>;
// Inputs/Outputs/InOuts counts == variables in input/output/inOutParameters (FBSource or ExternalToolsOnly);
// each FBD section's Canvas has as many non-comment nodes as its FBDSource has FFBBlock elements.

[Fact] public void Lcplc001_ListsItsFourProtectedTypes()
// protected names == {zPLCInfo, xANL101, xANL102, zScale}; xANL101 Inputs 19, Outputs 14, InOuts 5;
// both xANL101 blocks in Analogs_IN are IsProtectedDfb

[Fact] public void Sn6550_OpensLcnDig101AndLcnXiLockSections()
// LCN_DIG101 section Command_FBD has a Canvas with nodes; LCN_xILock section Status has SourceText
```

- [ ] **Step 2: Run them**

Run: `dotnet test ModbusForge.Tests/ModbusForge.Tests.csproj -c Release --filter "FullyQualifiedName~PlcXefCorpusSmokeTests"`
Expected: all pass (46 existing plus the new ones).

- [ ] **Step 3: Full verification**

Run: `dotnet build ModbusForge.sln -c Release` (0 errors, 0 warnings), the core unit suite (`FullyQualifiedName!~UITests & FullyQualifiedName!~SmokeTests`), the Avalonia suite, and the corpus runtime smoke tests (`PlcRuntimeCorpusSmokeTests`). Then open the app, import LCPLC001 and SN6550, and check the acceptance items of the spec by eye.

- [ ] **Step 4: Commit** (`PLC tests: Derived FB Types across the corpus`)
