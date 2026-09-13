# XEF → PLC simulation — shared brief for subagents

Goal: make the **PLC screen** run a *true* simulation of a loaded Schneider XEF: execute the
program's FBD logic cycle-by-cycle (reusing the existing simulation engine), and reflect the
live results in the on-screen blocks and the I/O table. The normal Simulation screen (motors,
levels, valves) stays untouched.

Repo: `C:\Users\rvn\source\repos\ModbusForge` (.NET 8, Avalonia). Branch is green:
`dotnet build ModbusForge.sln` = 0 errors. Tests live in `ModbusForge.Tests` (Core) and
`ModbusForge.Avalonia.Tests` (UI). Build with `dotnet build ModbusForge.sln`; run a test project
with `dotnet test <csproj> --filter "FullyQualifiedName~Xef" --no-build`.

## What already exists (do NOT rewrite; build on these)

### 1. XEF model + parser — `ModbusForge.Core/Xef/`
- `XefModels.cs`: `XefProject` (Name, Tasks, Variables, DataTypes, BlockTypes, Programs,
  HasStPrograms, Warnings, FindVariable(name), FindProgram(name)); `XefVariable` (Name,
  DataType, Address = the `topologicalAddress`, Value, Comment, Members, `TryParseAddress()`
  → `(PlcArea, int)?`); `XefBlockType` (Name, SourceKind, Inputs/Outputs/InOuts of
  `XefBlockTypePin` Name/Type); `XefProgram` (Name, Language "FBD"/"ST", Blocks, Links,
  Comments, StSource, FindBlock(instanceName)); `XefBlock` (InstanceName, TypeName, EnEno,
  PosX, PosY, Inputs/Outputs of `XefPin`, FindInput/FindOutput(formalParameter));
  `XefPin` (FormalParameter, EffectiveParameter (string? — a constant like "5"/"T#5s", a tag
  name like "Motor.Run", or an equation), Inverted, HasEquation); `XefLink`
  (SourceBlock, SourcePin, DestBlock, DestPin); `XefComment`.
- `XefParser.cs`: `XefParser.ParseFile(path, logger?)` and `XefParser.Parse(XElement, logger?)`.
  8 passing unit tests in `ModbusForge.Tests/Services/XefParserTests.cs` (FBD, ST, UDT, links,
  ZIP-wrapped, bad input). **Read this file to see the exact shapes.**

### 2. Existing simulation engine (REUSE, do not fork)
- `ExecutionEngine` (`ModbusForge.Core/Simulation/Engine/ExecutionEngine.cs`):
  `LoadGraph(nodes, connections)`, `Execute(DataStore?)` (two-phase evaluate-then-write,
  topological order, per-node error isolation). Reads/writes the `DataStore`.
- `FunctionBlockCatalog` (`.../Simulation/Core/FunctionBlockCatalog.cs`):
  `Create(string typeId)` → `IFunctionBlock`. TypeId == `PlcElementType.ToString()`.
- `IFunctionBlock`: `TypeId`, `Ports` (list of `{Name, Direction, Type}`), `Parameters`,
  `Execute(...)`. `BlockPorts.Inputs(ports)` / `BlockPorts.PrimaryOutput(ports)` helpers exist.
- `DataStore` (`ModbusForge.Core/DataStore.cs`): 1-based `ModbusDataCollection<T>` for
  HoldingRegister/InputRegister/Coil/DiscreteInput — **the shared Modbus memory bus**.
- `VisualSimulationServiceBase` (`ModbusForge.Core/Services/VisualSimulationServiceBase.cs`)
  already does the full job of running a `VisualNodeEditorConfig`: it converts
  `VisualNode`/`NodeConnection` → `SimulationNode`/`SimulationConnection` (see
  `GetOrCreateSimNode`, `ApplyNodeBindings`, `MapToSimulationConnection` around L574–775),
  rebuilds the graph when the config hash changes, ticks the engine on a timer, and pushes
  live values back. The concrete `AvaloniaVisualSimulationService` (registered as
  `IVisualSimulationService` in `App.axaml.cs`) is what `VisualNodeEditorViewModel` uses.

### 3. The visual node graph types the translator emits
- `VisualNode` (`ModbusForge.Core/Models/VisualNodeModels.cs` L14): `[ObservableProperty]`
  `Id`, `Name`, `ElementType` (`PlcElementType`), `X`, `Y`, `Width`, `Height`, `IsEnabled`,
  `Input1Address` / `Input2Address` / `OutputAddress` (`PlcAddressReference`),
  `OutputPortBindings` (`Dictionary<string, PlcAddressReference>`),
  `OutputPortNames` (`ObservableCollection<string>`, default `["Output"]`),
  `TimerPresetMs`, `CounterPreset`, `CompareValue`, `CompareValueReal`, etc.
- `NodeConnection` (L490): `SourceNodeId`, `TargetNodeId`, `SourceConnector` (default
  "Output"), `TargetConnector` (default "Input1", or "Input2"); ctor
  `NodeConnection(sourceNodeId, targetNodeId, targetConnector = "Input1")` (set
  `SourceConnector` explicitly if not "Output").
- `VisualNodeEditorConfig` (L562): `Nodes`, `Connections`, `ConnectorConfigs`
  (`ObservableCollection`s), `CanvasWidth/Height`, `ShowLiveValues`, `ScanIntervalMs`.
- `PlcAddressReference` (`Models/PlcSimulationModels.cs` L82): `Area` (`PlcArea`),
  `Address` (int), `Not` (bool), `SymbolicName` (string?). `DisplayAddress`, `IsSymbolic`,
  `Clone()`.
- `PlcElementType` enum (L11–66): Input, Output, InputBool, InputInt, OutputBool, OutputInt,
  NOT, AND, OR, RS, TON, TOF, TP, CTU, CTD, CTC, COMPARE_EQ/NE/GT/LT/GE/LE (+_REAL),
  MATH_ADD/SUB/MUL/DIV (+_REAL), SignalGenerator(+Real), Valve, MotorDol, Vsd, Scale,
  EdgeDetect, MovingAverage. **This enum is appended-only and persisted as a numeric — never
  reorder or insert.** A new XEF-only element type must be APPENDED at the end.

### 4. The PLC screen (already built last round)
- `ModbusForge/ViewModels/PlcViewModel.cs`: opens XEF (file dialog), holds the parsed
  `XefProject`, a program tree, the FBD block view (blocks+links positioned from PosX/PosY),
  the ST source pane, and a live I/O monitor (`RefreshLiveValuesAsync` reads holding registers
  via `IConnectionManager.ActiveService`). `LoadProject(XefProject)` populates it.
- `ModbusForge/Views/PlcView.axaml(.cs)`: the UI. Toolbar (Open XEF, Monitor I/O, Unit, ms),
  program tree (left), canvas/ST (centre), block detail (right), I/O table (bottom).
- Registered in DI (`App.axaml.cs`), exposed via `MainViewModel.PlcViewModel`, nav item "PLC"
  (nav index 16 → tab index 16, see `NavigationIndexConverter`).

## The gap to close (this round)
The screen **shows** the logic and monitors IO, but does **not execute** the FBD program.
Build the "true simulation":

1. **Translator** — `XefToSimulationConfig` (in `ModbusForge.Core/Xef/`): for a selected
   `XefProgram` (FBD), emit a `VisualNodeEditorConfig` (VisualNode per `XefBlock`, NodeConnection
   per `XefLink`, address bindings from `topologicalAddress` / pin `effectiveParameter` tag
   names). Map supported XEF FB type names → `PlcElementType`; for unsupported types, either
   map to a generic passthrough or (preferred) append a new `PlcElementType` marker so the
   block is visible but flagged as "no simulation equivalent". Return a result type that also
   reports which blocks were mapped vs. unsupported (for the UI to badge).
2. **Runner** — `PlcSimulationRunner` (in `ModbusForge.Core/Xef/` or `.../Simulation/`): given
   the translated `VisualNodeEditorConfig`, drive the existing engine. PREFER reusing
   `IVisualSimulationService` if its public surface allows feeding an arbitrary
   `VisualNodeEditorConfig` and a shared `DataStore`; otherwise construct
   `ExecutionEngine` + `FunctionBlockCatalog` directly and tick on a `PeriodicTimer` at the
   configured scan interval. Must read inputs from / write outputs to the `DataStore`, and be
   start/stop-able and thread-safe.
3. **Integration** — wire `PlcViewModel`: "Run/Stop simulation" command; on load, run the
   translator for the selected program; while running, pull each node's live value into the
   on-screen `PlcBlockItem` (add a `CurrentValue` to it) and refresh the I/O table from the
   `DataStore` (in simulation mode) or the live Modbus connection (in monitor-only mode).
   Keep monitor-only mode working when no engine is running.

## Conventions
- Namespace `ModbusForge.Core.Xef` for the Core types; `ModbusForge.Avalonia.ViewModels` for
  the VM. Use `ILogger` (never `Debug.WriteLine`). Constants for magic numbers. Null-annotate.
- `InternalsVisibleTo` already covers `ModbusForge.Tests`, `ModbusForge`,
  `ModbusForge.Avalonia.Tests`, `ModbusForge.Headless` — Core tests can use `internal`.
- PowerShell uses `;` not `&&`. Build: `dotnet build ModbusForge.sln`. The built app is
  `ModbusForge\bin\Debug\net8.0\ModbusForge.exe` (may need `taskkill /f /im ModbusForge.exe`
  first if the exe is locked).
- **Do not** touch the pre-existing in-flight core edits (`ModbusSerialService.cs`,
  `TagService.cs`, `ModbusFrameLogger.cs`, etc.) or the `docs/research`/`research` notes —
  they are the user's WIP.
- **Do not commit.** Leave changes in the working tree.
- Verify: `dotnet build ModbusForge.sln` green + `dotnet test` on your new tests green.

## Fixture for end-to-end tests
There is no real `.xef` on disk. Hand-build XEF XML matching the confirmed grammar (see
`XefParserTests.FbdXml`) and assert the translator + runner produce correct values: e.g. a
program where an input tag (a coil/holding register the DataStore sets) drives an AND → an
output tag; assert the output DataStore cell flips when the input does.
