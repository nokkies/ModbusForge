# XEF-Based "Full PLC" Simulation for ModbusForge — Research & Integration Proposal

**Companion documents:**
- [`schneider-xef-file-format.md`](./schneider-xef-file-format.md) — what a .XEF is, with
  evidence tags.
- [`../../research/st-compiler-research.md`](../../research/st-compiler-research.md) — the
  landscape of open-source ST compilers/interpreters available to a .NET 8 host (MatIEC,
  IronPLC, OpenPLC, PLCnext, the absence of any embeddable C# ST engine).

This document answers the design question: **how do we turn a Schneider XEF into something
ModbusForge can (a) display as PLC code in a new window and (b) execute so the existing
simulation and register watch see a full, program-driven PLC — not just hand-placed
valve/motor/level blocks?**

Method note: this was established with `web_search` snippets, a local repo read
(`ModbusForge.Core/Simulation/**`, `DataStore.cs`, the node editor view model), and
inference. Claims from the web are tagged as in the companion docs; claims about this repo
are measurements (file + line).

---

## 0. What the user asked, restated

1. The **normal simulation** (the existing visual node editor) stays as-is: level tanks,
   valve/motor/VSD feedback, hand-wired logic. That already works.
2. Add a **new "PLC" window** that:
   - accepts a Schneider **XEF** backup file,
   - **shows the PLC program code** the way it is described in the XEF (ST text, and the
     structure of LD/FBD/SFC networks), and
   - **simulates the full PLC** — the program actually runs each scan, its I/O mapped onto
     the same Modbus register space the rest of the app already reads/writes, so the
     registers grid, watch, and trends see program-driven values, and the visual node
     editor can drive/observe the PLC's I/O registers from the other side.

---

## 1. What the current codebase already gives us (measured)

The simulation stack is already shaped like the thing we need to extend — this is the
single most important design fact.

| Piece | Where | What it means for XEF integration |
|---|---|---|
| `DataStore` — the shared memory bus | `ModbusForge.Core/DataStore.cs` (L14) | One 1-based `ushort`/`bool` collection per Modbus area (Holding, Input, Coils, Discretes). **This is exactly the surface an M580 PLC exposes over Modbus.** A "PLC" is just another party reading/writing this store. |
| `ExecutionEngine` | `ModbusForge.Core/Simulation/Engine/ExecutionEngine.cs` (L16) | Two-phase scan: **evaluate all nodes, then write outputs** (L73-124) — already the correct IEC-style scan discipline. Topologically ordered, per-node error isolation (a throwing node freezes, others run). |
| `IFunctionBlock` / `SimulationNode` / catalog | `ModbusForge.Core/Simulation/Core/` (L1-40 of `IFunctionBlock.cs`) | Blocks are **self-describing** (`TypeId`, `Ports`, `Parameters` — L9-40). A new block type is added by implementing one interface and registering it in `FunctionBlockCatalog` — see the 30-block catalog in `VisualSimulationServiceBase.CreateCatalog()` (L111-174). **This is the seam where an ST-interpreter block plugs in.** |
| Address binding | `ExecutionEngine.ReadDataStore/WriteDataStore` (L284-367) | Every port can be bound to a `PlcArea`/`Address` (+ optional NOT). The binding mechanism is already generic — nothing here needs to change for a PLC block. |
| Store mode "device" vs "local" | `VisualSimulationServiceBase.GetEffectiveDataStore` (L495-503) | When a Modbus connection is active the sim reads/writes the **connected device's** store; otherwise a private offline store. **The XEF-PLC plugs into the exact same store, so the registers grid, watch and trends already see its I/O with zero new UI.** |
| Node editor = the model for the new window | `ModbusForge/Views/VisualNodeEditorView.axaml(.cs)`, `VisualNodeEditorViewModel` (partial classes incl. `.Programs.cs`, `.Tags.cs`) | The new "PLC window" is a sibling: another `UserControl` + `ObservableObject` view model registered in `App.axaml.cs` (see L212 for the node editor's registration) and hosted in `MainView.axaml` (L242 pattern). It does **not** need to be a separate OS window; a tab/pane in the main window is the existing convention. |

**Inferred:** the architecture deliberately separates "what is simulated" (blocks) from
"how it is wired to Modbus" (address bindings) from "where the values live" (`DataStore`).
A full-PLC simulation is therefore *not* a parallel engine — it is **one more set of blocks
in the same engine, whose I/O lands in the same store.**

---

## 2. What the XEF actually contains that we can use (from the companion research)

**[documented]** Content scope (eplan `PlcDCXmlExchangerSchneider`): XEF = *complete
information — hardware configuration + variables*; ZEF = same, compressed. The file is a
**ZIP whose payload is plain, unencrypted XML** (Schneider's own FAQ documents
rename-`.zef`-to-`.zip`; Rickard shows the `PK` magic unzipping to an XML named `file.XEF`).

**[community-reported]** The ST program text is stored as **readable text inside the XML**;
LD/FBD/SFC are stored as **structured network/section definitions**, not text. A
purpose-built extractor exists: [`RomanVerzun/xef_extractor`](https://github.com/RomanVerzun/xef_extractor)
("extract code from Unity Pro/Control Expert XEF files for version control in Git"). A
**schema artifact is shipped in Control Expert's install directory** (Fulminis blog) — the
most concrete lead for building our own parser.

**[RESOLVED by UnityParseEngine]** `nokkies/UnityParseEngine` (Python, v1.1.0,
"Production") is a **working XEF parser** — an FBD→LAD migration tool that reads a
Schneider `.XEF` and rebuilds a Rockwell `.L5X`. Its `builder/unity.py` gives us the
**literal XEF XML grammar** the earlier research could not establish. The element names are
real, confirmed by code that parses them (see §2a). What remains open: the **ST-language
program representation** (the parser models FBD blocks/pins/links, not ST text — so a
project written in ST is not yet confirmed to carry readable ST source in the same XML),
and whether scan time is in the file.

### 2a. The confirmed XEF XML grammar (from UnityParseEngine `builder/unity.py`)

This is the single most valuable thing the repo gives us. The XEF root and its children:

```
<root>                                   # (root element name not asserted in code)
  <contentHeader name="PLCNAME"/>        # PLC/controller name  (L247)
  <sectionDesc name=.. FMName=.. FMId=.. FMOrder=.. SectionOrder=../>   # tasks/sections (L250,271-282)
  <dataBlock>
    <variables name=.. typeName=.. topologicalAddress=..>
      <variableInit value=../>
      <instanceElementDesc name=..><value>..</value></instanceElementDesc>
      <comment>..</comment>
    </variables>
  </dataBlock>                           # the variable/IO table  (L303-327)
  <DDTSource DDTName=..>                 # user-defined data types (L463)
    <structure><variables name=.. typeName=../></structure>
  </DDTSource>
  <EFSource nameOf..Type=../>            # function definitions   (L425)
  <FBSource nameOf..Type=../>            # function blocks        (L426)
  <EFBSource nameOf..Type=../>           (L427)
    <inputParameters|outputParameters|inOutParameters>
      <variables name=.. typeName=../>
    </inputParameters|...>
  </FBSource>
  <program>                              # one per program  (L260)
    <identProgram name=../>
    <FBDSource>
      <networkFBD>
        <FFBBlock instanceName=.. typeName=.. enEnO=..>
          <objPosition posX=.. posY=../>
          <inputVariable formalParameter=.. effectiveParameter=.. invertedPin=../>
          <outputVariable formalParameter=.. effectiveParameter=../>
          <descriptionFFB execAfter=../>
        </FFBBlock>
        <linkFB>
          <linkSource parentObjectName=.. pinName=../>
          <linkDestination parentObjectName=.. pinName=../>
        </linkFB>
        <textBox width=.. height=..><objPosition/><text>..</text></textBox>
      </networkFBD>
    </FBDSource>
  </program>
</root>
```

Key facts this establishes:
- **Variables carry `topologicalAddress`** (L324) — the IO address *is* in the file. This
  is the symbol→address table we need for the Modbus mapping, and it's machine-readable.
- **The logic is stored as FBD blocks + pins + links** (FFBBlock / inputVariable /
  outputVariable / linkFB), *not* as text. The block's `typeName` (L52) is the function
  block (TON, AND, OR, MOVE, …) and `effectiveParameter` (L102-110) is the wired value or
  a tag reference (or an inline equation — `has_equation`, L111-124).
- **Block types are declared** in `EFSource`/`FBSource`/`EFBSource` with their full
  input/output/in-out parameter lists (L364-410) — so we know each FB's signature.
- **Tasks/sections** are `sectionDesc` with `FMOrder`/`SectionOrder` (L271-282) — the
  program execution order *is* in the file.
- **User data types** (DDTSource) are structs with named members (L443-450).

**Implication for the design:** the XEF is **FBD-native**. A program is a graph of
function blocks with typed pins and explicit links — which is *exactly* the shape of
ModbusForge's existing `SimulationNode`/`SimulationConnection` dataflow graph. This
changes the recommendation: **the primary path is not "interpret ST" but "translate the
XEF's FBD block/pin/link graph into our existing function-block graph."** ST programs (if
present in the same XML as a different source element) would be the secondary, interpreter
path. See the revised Option A/B in §3.

---

## 3. The candidate architectures (revised after reviewing UnityParseEngine)

**The revision:** UnityParseEngine proves the XEF stores logic as an **FBD block/pin/link
graph** with `topologicalAddress` on every variable and `typeName` on every block. That is
structurally identical to ModbusForge's `SimulationNode`/`SimulationConnection` graph. So
the lowest-risk, highest-reuse path is **direct FBD→graph translation**, with the ST
interpreter demoted to a secondary path for programs that are ST-written.

### Option A (revised) — Translate the XEF FBD graph into the existing function-block graph (recommended for Phase 1)

Parse the XEF (reusing/adapting UnityParseEngine's `builder/unity.py` XPaths) into
`VisualNode`/`NodeConnection` objects:
- Each `FFBBlock` → a `VisualNode` whose `PlcElementType` is mapped from `typeName`
  (TON/TOF/TP/CTU/CTD/AND/OR/NOT/MOVE/compare → our existing catalog; see the
  `PlcElementType` enum in `PlcSimulationModels.cs` — it already has most of these).
- Each `inputVariable`/`outputVariable` with an `effectiveParameter` that is a tag → an
  address binding on the corresponding port (`PlcArea` + `topologicalAddress`).
- Each `linkFB` → a `NodeConnection` between the source block's output pin and the
  destination block's input pin.
- `topologicalAddress` on `dataBlock/variables` → the `XefSymbolTable` (symbol →
  area:address) that drives the live IO table in the PLC window.

**Reuse:** the existing `ExecutionEngine`, `DataStore`, `FunctionBlockCatalog`, and the
node editor's live-value plumbing all work unchanged — we are just *populating* the graph
from the XEF instead of by hand. **This is the path that delivers "full PLC simulation"
with the least new code**, because the XEF's FBD blocks map onto blocks we already execute.

**Gaps to close (bounded):**
- FBs in the XEF that have no ModbusForge equivalent (e.g. Schneider-specific FBs, complex
  math, `ATAN2`, string ops) → shown in the graph but **frozen** with a "no block
  equivalent" marker (same treatment as LD/SFC in v1). The catalog has ~30 blocks; the XEF
  `FBSource` signatures let us know exactly which ones are missing *per project* at load.
- Multi-bit / struct-typed pins → map to the existing `Int32`/`Real` values for v1.
- Execution order: `sectionDesc.FMOrder`/`SectionOrder` gives the program order; within a
  program the FBD links give the topological order the engine already computes.

### Option A2 — ST interpreter (secondary path, for ST-written programs)

**Only needed if a project's programs are ST-written** (the XEF may carry an ST source
element alongside FBD; UnityParseEngine only models the FBD side, so this is the part to
verify against a real ST-heavy XEF). If required: extract the ST source, wrap it as one or
more `IFunctionBlock` instances (one per POU) executing against the same `XefSymbolTable`
each scan, registered in the `FunctionBlockCatalog`. (The original Option A text follows —
the interpreter design is unchanged, it is now the *secondary* path.)

- The engine's existing two-phase execute/write (L73-124) runs them in topological order
  with everything else. If the PLC's outputs are wired to the visual node editor's inputs
  (e.g. PLC coil `M1` → MotorDol `Run` port), **the full loop is simulated in one scan**:
  PLC logic → register write → valve/motor physics → feedback register → PLC logic next
  scan.
- The "PLC window" shows: program list (tree) → per-program ST source (read-only,
  syntax-highlighted text) → live symbol table with current values. Editing the ST and
  re-loading is the workflow; no ST editor is needed in v1.
- **Reuse:** zero new engine code. The risk is isolated to one block type + the extractor.

**Effort driver:** the ST interpreter itself. The realistic subset for industrial programs
is: `IF/THEN/ELSIF/ELSE`, `CASE`, `FOR`/`WHILE`, comparisons, arithmetic, `AND/OR/XOR/NOT`,
assignments, function blocks as calls (`TON`, `CTU` — we already have these as C# blocks),
`TIME`/`REAL`/`INT`/`BOOL`/`WORD`, and symbol read/write of the I/O table. That is a few
thousand lines of C# (hand-rolled recursive-descent parser + tree-walking interpreter is
the honest scope; **Roslyn is the wrong tool** — ST is not C#-compatible enough to be
shoehorned in, and I found no credible open-source ST→C# generator in the searches —
see gaps in §7).

**What the compiler landscape research found (see
[st-compiler-research.md](../../research/st-compiler-research.md)):**
- **No production-grade open C#/.NET-native ST interpreter exists** — the conclusion of an
  8+-query sweep. The production-grade open engines are both the wrong host language to
  embed: **MatIEC** (C/C++, ST→ANSI C, the reference open ST transpiler, BSD-2 family) and
  **IronPLC** (Rust, ST/PLCopen-XML/TwinCAT → bytecode + Rust VM; the most complete modern
  open toolchain). **OpenPLC** is a standalone C/C++ runtime built on MatIEC (wrong shape:
  it wants to *be* the PLC with its own I/O model). **PLCnext** and **CODESYS** are
  closed/vendor ecosystems.
- **No off-the-shelf ST→C# or ST→IL generator exists.** So "generate C# at load time and
  compile with Roslyn" (option c below) requires building the ST→C# transpiler ourselves —
  at which point a tree-walking C# interpreter (option a) is less total work for the same
  fidelity on the subset we need.
- **MatIEC remains the best external reference** if we later want full-IEC fidelity:
  shell out to `iec2c`, but its output is ANSI C — hosting that inside .NET needs a C
  compiler (MinGW/clang) + an I/O bridge, which is exactly the "worst fit" path and is
  held as a fallback, not a plan.

### Option B — LD/FBD → graph translation into existing blocks

Translate the XEF's network definitions into `VisualNode`/`NodeConnection` objects so the
*existing* node editor renders and runs them. Only blocks that map to our catalog
(AND/OR/NOT/R-S/timers/counters/compare/math) can be translated; anything without an
equivalent (SFC, function-block calls to library FBs, ST) is shown but frozen.

- **Cheap for pure LD/FBD programs** and gives a visual, editable result.
- **Inferred** this covers a minority of real M580 programs (most mix ST + LD). It is a
  complement, not a substitute, for Option A.

### Option C — External ST runtime (MatIEC / IronPLC / OpenPLC) via process or native interop

Run a general IEC 61131-3 ST runtime out-of-process; bridge its I/O to our `DataStore`
over a named pipe/file each scan.

- **MatIEC** (C/C++, ST→ANSI C, BSD-2 family) — the strongest open ST transpiler; output
  is ANSI C, so a .NET host needs a C compiler + an I/O bridge in the middle
  [st-compiler-research.md §1a/§3].
- **IronPLC** (Rust, ST/PLCopen XML/TwinCAT → bytecode + `ironplcvm`) — the most complete
  modern open toolchain; reads real vendor exports directly, which is encouraging for the
  XEF-normalisation phase; but it is a Rust VM, interop from .NET is FFI-scale
  [st-compiler-research.md §1c].
- **OpenPLC** — standalone C/C++ runtime on MatIEC; designed to *be* the PLC with its own
  I/O model, so adopting it means surrendering our `DataStore` as the I/O surface
  [st-compiler-research.md §1b].
- **Inferred:** process-per-PLC, cross-language I/O bridge, lifecycle management, and
  licensing to verify per component make this a *fallback*, not the primary path. It is the
  right answer only if we cannot close the ST-subset gap of Option A with real program
  samples.

**Recommendation:** build **Option A**, keep **Option B** as a Phase 2 for LD/FBD-heavy
programs, hold **Option C** as an escape hatch. Option A is the only one where the "PLC
window shows the code *as described in the XEF*" requirement and the "full PLC simulation"
requirement are satisfied by the *same* artifact (the extracted ST text + symbol table).

---

## 4. Proposed design (Option A, concretely)

### 4.1 New assemblies / folders

- `ModbusForge.Core/Xef/` — file format layer, no UI, no engine:
  - `XefPackage` — opens `.xef`/`.zef`: detects ZIP magic, extracts the XML member(s),
    validates the root. Handles both "bare XML" and "zip-wrapped" shapes (§2 open item).
  - `XefModel` — POCOs: `XefApplication` → [`XefProgram` (name, language, text|networks)] +
    `XefHardware` (CPU type, I/O modules) + `XefVariables` (symbol → address + type).
  - `XefSymbolTable` — the **single source of truth** mapping symbolic names
    (`"Motor1.Run"`) to `PlcArea` + address, built from the XEF variables section,
    falling back to a manual mapping dialog when the file's address data is missing.
  - `XefProgramText` — the extracted ST source, per program, with language tags.
- `ModbusForge.Core/Simulation/Blocks/St/` — the interpreter:
  - `StLexer`, `StParser` → `StAst` (recursive descent; the industrial subset of §3A).
  - `StInterpreter` — tree-walking execution against an `StScope` (local variables + the
    `XefSymbolTable` for I/O).
  - `StProgramBlock : IFunctionBlock` — one instance per XEF program. `Execute(context)`
    = run that program's AST once per scan, reading/writing I/O symbols **through
    `IExecutionContext`'s `DataStore`** (the block already gets the store in its context —
    see `ExecutionEngine.EvaluateNode`, L171-193). This keeps I/O access on the engine's
    existing, lock-protected path (the store lock is held for the whole `Execute`, L322-326
    of the base service).

### 4.2 The "PLC window" (UI)

- A new `UserControl` `PlcWindow.axaml` (naming follows the existing window pattern) with:
  1. **Load bar:** `Open XEF…` (file picker for `.xef`/`.zef`), program count + language
     badges, scan-period input (reuses the existing `ScanIntervalMs` conventions,
     min 10 / max 10000 ms — `VisualSimulationServiceBase` L46-52).
  2. **Program tree** (left): one node per XEF program, tagged ST / LD / FBD / SFC.
  3. **Code pane** (center): read-only `TextBox` with monospace font + a small ST syntax
     highlighter (regex-based is fine for v1; Avalonia has no built-in highlighter, so
     this is custom but small). This is the "shows the PLC code as described in the XEF"
     requirement, satisfied directly from `XefProgramText`.
  4. **Symbol/IO table** (right or bottom): DataGrid of symbol → area:address → **live
     value** (bound to the `DataStore` through the existing value-changed plumbing the
     node editor already uses, `VisualNodeEditorViewModel` L881-886 pattern).
- Hosting: register `PlcViewModel` in `App.axaml.cs` next to
  `VisualNodeEditorViewModel` (L212) and add the pane to `MainView.axaml` (L242 pattern).
  **Not a separate OS window** — matches the app's existing single-window/tabs convention.
- **No editing in v1.** "Edit ST → re-run" is a Phase 2 feature (needs an ST editor +
  re-parse-on-save); the v1 loop is *load → watch → drive I/O from the registers grid or
  node editor*.

### 4.3 Execution model (the "full PLC")

- The PLC is **not** a separate timer. Its programs run **inside** the existing
  `ExecutionEngine.Execute` scan, as `StProgramBlock` nodes in the topological order.
- Scan order within one cycle: I/O read → ST programs (in XEF program order — preserve
  the file's program sequence as node ordering; IEC semantics = programs run in
  declaration order, so if the engine's topological sort scrambles them, the PLC programs
  should be a **linear chain** of connections, program N's "done" → program N+1's start,
  to force declaration order). This is the one place we deliberately add engine-level
  structure (a chain), which the engine already supports natively.
- **LD/FBD/SFC programs** that we do not yet translate (Phase 2): their POU instances are
  still *declared* in the symbol table (their variables stay live and visible in the
  symbol table), but their logic is **frozen** with a visible "not yet simulated — LD"
  marker, so a mixed-language project loads and runs partially instead of failing.
- **Hardware simulation (CPU + I/O modules) is out of scope for v1.** The XEF's hardware
  section is *displayed* (module list, I/O map) but we simulate at the **Modbus-register
  level**, which is what M580 exposes to the outside world anyway — the user's own framing
  ("normal simulation will do levels and motor run feedback") is register-level physics,
  and that is exactly what the existing blocks do.

### 4.4 What the user experiences end-to-end

1. Open a project file (`.mfsim`), which as today contains the node graph.
2. Click **PLC** pane → **Open XEF…** → select `MyProject.xef`.
3. The pane shows the program tree + ST source + the I/O symbol table with live values
   (all zero until run).
4. **Run** (the existing sim run button; the PLC blocks join the running graph — no
   separate start/stop in v1, matching the node editor's lifecycle).
5. The registers grid / watch / trends now show program-driven values. Flip an input coil
   from the registers grid → the ST sees it next scan → outputs change → a MotorDol block
   wired to that coil runs up its pickup delay → its feedback register changes → the ST
   reacts next scan. **Full closed loop, one engine, one store.**

---

## 5. Reuse audit — what we deliberately do NOT rewrite

- **No new engine.** `ExecutionEngine` unchanged (Option A needs only the existing
  connection-based ordering).
- **No new store.** `DataStore` unchanged.
- **No new Modbus path.** The XEF-PLC does not talk Modbus directly; it reads/writes the
  same in-process store the Modbus server already exposes, so external Modbus clients and
  the registers grid stay consistent with zero protocol work.
- **No new value types.** `SimulationValue` (Bool/Int32/Real) covers the ST subset v1
  needs; `WORD`/`DWORD` can map onto `Int32` for v1 and be refined later.

---

## 6. Spikes that must happen before full build (de-risk the unknowns)

**Spike 1 — run UnityParseEngine against one real XEF (the gate).**
`nokkies/UnityParseEngine` is a working XEF parser (Python, `lxml`, `pandas`). Instead of
hand-rolling an XPath parser, **run its `EngineRW`/`EngineBuilder` path on one real M580
XEF** and inspect the resulting model + any `build/` output. This confirms: (a) the
`topologicalAddress` values are real IO addresses we can map to Modbus, (b) which FBD
`typeName`s appear in a *real* project (→ the exact set of blocks we need equivalents for),
(c) whether ST-written programs appear as a separate source element the FBD parser skips.
**This is now the cheapest possible spike** — the parser exists; we just point it at a file.
*(Needs one real `.xef` from a Control Expert export; the repo's `files.json` references
`../resources/XML/HGPLC001.XEF` as a sample, so a sample likely exists in the repo or its
history.)*

**Spike 1b — extract the XEF sample.** The repo's `UnityParseEngine/files.json` points at
`resources/XML/HGPLC001.XEF`. Check whether that file (or a fixture) is in the repo or git
history — if so, Spike 1 needs no external file at all.

**Spike 2 — ST subset inventory.** Take 2-3 real ST programs from the extracted file and
list every language construct used. Compare against the §3A subset. Any gap (e.g. `ARRAY`,
`STRUCT`, function-block instances with internal state, `SHL/SHR`, `MOVE`, multi-byte
`TIME`) is a scoped line item, not a discovery mid-build.

**Spike 3 — one program end-to-end.** Implement the smallest possible `StProgramBlock`
that runs one real program (e.g. a startup routine that sets a coil) against a test
`DataStore`, and assert on register values after N scans. This proves the
interpreter↔store seam before the UI exists.

**Spike 4 (parallel, cheap)** — check `RomanVerzun/xef_extractor` and the Control Expert
install-dir schema folder: if either gives us a parser for the variables section, `XefModel`
shrinks to "extract ST + glue."

**Spike 5 (parallel, cheap)** — normalize the XEF to **PLCopen XML** (the open
interoperability format for IEC 61131-3) as a *portability* goal, not a dependency: if our
extractor emits PLCopen XML, then IronPLC / MatIEC / PLCOpener all become usable
second-opinion back-ends for verification ("does our interpreter agree with a
reference engine?"), and the XEF-reading work amortizes across tools. The `plcopener`
Python package already does ST↔PLCopen-XML extraction — a useful reference
[st-compiler-research.md §3].

---

## 7. Open questions / risks (do not pretend these are solved)

1. **XEF element names — now KNOWN** (UnityParseEngine `builder/unity.py`, §2a). The
   remaining unknowns are narrower: the **root element name**, the **ST-program
   representation** (FBD is confirmed; ST is not yet), and whether **scan time** is in the
   file. *(Would close them: run Spike 1 on a real XEF.)*
2. **Scan time in the file — unverified.** If it's not in the XEF, the scan period stays
   a user input (default 100 ms, matching the existing sim default).
3. **ST subset coverage.** The industrial subset is generous but not universal;
   `ARRAY`/`STRUCT`/`STRING` and FB instances with internal state are the likely first
   gaps. Each is a bounded feature; the interpreter's design (AST + scope tree) absorbs
   them without restructuring.
4. **LD/FBD/SFC are Phase 2.** v1 loads and displays them, runs ST, freezes the rest with
   a visible marker. A user whose program is 90% LD will see limited value until Phase 2 —
   worth stating up front.
5. **Licensing.** XEF is Schneider-proprietary with no published spec
   [inferred from absence]; parsing a file the customer legitimately owns for their own
   simulation is low-risk, but the **EULA text has not been read** (location: Control
   Expert License Manager → Help, `C:\Program Files (x86)\Schneider Electric\License
   Manager\EULA`). *Recommendation: one read of the EULA before shipping, because the
   "shows the PLC code" pane displays Schneider-programmed source inside a third-party
   tool.*
6. **No open-source XEF→executable path exists** [community-reported absence]: we are
   building the ST side from scratch. The compiler-landscape research
   [st-compiler-research.md] confirmed **no embeddable C#/.NET ST interpreter exists at
   all** — the production-grade open engines are MatIEC (C/C++→ANSI C) and IronPLC (Rust→
   bytecode), both wrong host languages to embed, so "build our own C# tree-walking
   interpreter" (Option A) is the only path with a realistic in-process footprint;
   external-runtime options (Option C) remain the fallback.
7. **Mid-merge working tree (see git status below).** This repo is currently mid-merge on
   `integration-app-trends` with deleted-then-modified partial-class files (`MainViewModel.*.cs`
   showing as both "new file" staged and "deleted" unstaged). Build work on the simulation
   core is unaffected, but **the merge should be concluded (or reverted) before starting
   XEF work** so commits have a clean parent.

---

## 8. Git status (the "are we up to date" part, measured)

- `master` **is current** with `origin/master`: `rev-list --left-right --count` = `0 0`
  (HEAD `bddae19` "fix(ui): limit active profile server IP display with dropdown" is the
  tip of both).
- **However, the working tree is in an unfinished merge** on branch
  `integration-app-trends`: MERGE_HEAD is `4b4d614` ("v2026.8.28: Reliability, validation
  & housekeeping release") from `origin/app-improvement-suggestions-2cf19`, with
  "All conflicts fixed but you are still merging." Several `MainViewModel.*.cs` partial
  files appear staged as new and simultaneously deleted in the working tree — this looks
  like a conflicted partial-class split that was only half-resolved.
- **Action needed:** `git commit` to conclude the merge (after verifying the
  MainViewModel partials are the intended final state), or `git merge --abort`. Nothing
  here blocks the XEF research, but it blocks a clean build/test and any new branch.

## 9. Suggested sequencing (revised)

1. **Close the merge** (§8). *(you, 10 min)*
2. **Spike 1** — locate/run a real XEF through **UnityParseEngine** (check the repo's
   `resources/XML/HGPLC001.XEF` fixture first); record the `topologicalAddress` values,
   the set of FBD `typeName`s, and whether ST programs appear. *(1-3 days; parser exists)*
3. **Spike 2** — from that real project, build the **FB mapping table**: XEF `typeName` →
   ModbusForge `PlcElementType` (or "no equivalent → frozen"). This is now a *lookup
   table*, not a research project. *(~2-3 days)*
4. **Phase 1 build** — `XefPackage`/`XefModel` (port the XPaths from `unity.py`, or shell
   out to it), **FBD→`VisualNode`/`NodeConnection` translator** (Option A), `XefSymbolTable`
   from `topologicalAddress`, `PlcWindow` with program tree + code/IO pane, registration in
   `App.axaml.cs`/`MainView.axaml`. *(several weeks)*
5. **Phase 2** — ST interpreter path (Option A2) for ST-written programs, LD/FBD-for-LAD
   projects, ST editing, SFC, multi-PLC.
