# PLC tab: DFB types like Control Expert

Date: 2026-10-22. Status: draft for review.

## Why

The PLC tab runs an imported XEF so engineers can test a SCADA/HMI against it and check
the PLC logic. Instances of DFB types whose code is locked (encrypted in the export) do
nothing today: their outputs keep their initial values and the canvas says "not
simulated". In the 23-project corpus that is 9,686 of 45,142 blocks (21%), from 67
locked types. Everything fed by them (HMI values, alarms, interlocks) is dead or wrong.

The user's rules that shape this:

- Client project data stays on the engineer's machine: ModbusForge sends none of it to an
  AI or any other online service.
- Only Schneider library types are built into ModbusForge. Client DFBs come from each
  project's XEF.
- A locked type is converted with its inputs and outputs and a note that it is empty.
  The engineer can write its code themselves, in LD, FBD or ST.
- Mirror how Control Expert structures and shows DFBs.

## Goals

1. The navigator gets a "Derived FB Types" node, as in Control Expert's Project Browser,
   listing every DFB type of the project with its Inputs, Outputs, Inputs/Outputs, Public
   variables, Private variables and Sections (Program Languages and Structure, 35006144.27,
   p. 352).
2. Only a DFB whose code is protected in the export becomes a shell: its interface and
   one empty section, greyed with a lock and the note "Empty: the code is protected in
   the export". Every other DFB is copied with its code. Instances of a shell read "empty
   (protected DFB)" on the canvas instead of "not simulated".
3. The engineer can give a locked type their own code in ST, FBD or LD, the languages a
   DFB section can have. It runs for every instance of the type, as a DFB section does in
   Control Expert.
4. Saving writes a ModbusForge project file (.xef_sf) in ModbusForge's own layout, with
   only what ModbusForge uses; the XEF is only for the first import. The engineer's code
   is saved in that one file and comes back when it is opened again.
5. Types whose code is exported keep running it, as today. Their sections are part of the
   selection on the left: selecting one opens it as a program section opens.

## Non-goals

- Decrypting or working around protected code. The only route to the original code is an
  unprotected export from its owner.
- Generating DFB code automatically.
- Building any client DFB into ModbusForge.
- Editing exported (unlocked) code.
- Sharing the engineer's code between projects: it belongs to the project file it was
  written for.
- Exporting back to XEF for Control Expert. It is planned for a later stage; when it
  comes, the engineer's code must not leave as the protected DFB's real code.

## Anti-goals (a failure even if it works)

- The engineer's code mistaken for the original. Wherever it runs it is marked as the
  engineer's: the type in the navigator, each instance on the canvas, and the Console when
  the PLC starts ("xANL101 runs the engineer's ST, not the original code").
- Code running against an interface it was not written for. If a type's interface no
  longer matches the one its code was written for, the code is kept but not run, and the
  engineer is told.
- Writing the same code once per instance.
- A project that runs differently after saving and opening it than straight after the
  XEF import.

## Constraints

- A DFB type has Inputs, Outputs, Inputs/Outputs, Public variables, Private variables and
  code Sections in LD, IL, SFC, ST or FBD (35006144.27, p. 352). A section has a name, a
  validation condition, a comment and a protection attribute (p. 359).
- A DFB section uses only its type's parameters and variables, globals only through the
  `project.` prefix, and of the I/O only %S, %SW and %SD (p. 360). The engineer's code
  follows the same rules.
- A locked type in the XEF carries its interface (inputs, outputs, in/outs, sometimes
  public variables) and its code inside `<crypted>`. It carries no private variables and
  no section names or languages. In the corpus, protection always covers the whole type:
  no exported type has a section of its own protected.
- The export can leave public variables out: program code reads only exported fields of
  locked-type instances, but an animation table in GGPLC001 reads three fields of an
  xPID101 instance (pPIDInfo, jPVScale, jSPScale) that the export does not list.
- None of the 67 locked types is a Schneider library type. Their names and comments point
  to the client's library and an integrator's (for example `_DNP_IEDStatus`: "based on
  PTQ-DNP module command error responses").
- The runtime already runs exported FBD and ST sections per instance. LD, IL and SFC
  sections are listed but not run. Exported types in the corpus have 111 FBD, 66 ST and
  5 LD sections.

## Approaches considered

- **A. AI drafts each locked type's code** from names and comments, the engineer checks
  it. Rejected: client data must stay local.
- **B. Red, unconnected placeholders**: locked blocks stay inert and the engineer writes
  stand-in logic in program sections. Superseded by the user: convert the type with its
  inputs and outputs instead.
- **C. Mirror Control Expert (chosen, the user's direction).** A locked type becomes an
  empty DFB type with its interface. The engineer adds sections and variables to
  the type, and every instance runs them.

For the saved file:

- **XEF plus the engineer's part.** The file holds the XEF's content as imported plus the
  engineer's code, and opening it reuses the XEF import. A small job; the file is as big
  as the XEF and carries everything ModbusForge does not use.
- **Own layout, only what ModbusForge uses (chosen by the user).** Smaller and cleaner. A
  bigger job: every part of the import needs a matching save and load, and future import
  features need them too.

## Design

### Navigator

"Derived FB Types" sits beside "Data Management" under Application (my call). One row per
type, marked exported, locked (empty) or locked (engineer's code). Under each type:
Inputs, Outputs, Inputs/Outputs, Public variables and Private variables (name, type,
initial value, comment, in the Data Management grid), then Sections (name, language,
protection). A locked type's original code is one greyed, locked section: "Empty: the
code is protected in the export". The engineer's sections are listed beside it, marked as
the engineer's.

Selecting a section opens it as a program section opens: ST as text, FBD drawn on the
canvas, LD listed until there is an LD view. As with program sections today, edits on
the PLC canvas do not change what runs (the runtime runs the imported code); a read-only
canvas is left for later (my call). A section opens without live values, because values
belong to an instance, not to the type; showing them for a chosen instance is left for
later (my call).

### Canvas, Console and status

Instances of a locked type without code read "empty (protected DFB)" and keep their
outputs, as now. With the engineer's code they compute and carry an "engineer's code"
mark. When the PLC starts, the Console lists the locked types: which are empty and which
run the engineer's code. The status text counts empty protected DFBs apart from
unsupported types.

### Writing the code (ST first)

On a locked type, the engineer can add a section (name; language ST, FBD or LD, built in
that order: the ST compiler and the canvas exist, LD needs an engine) and add variables,
private or public (name, type, initial value; FB types such as TON allowed). My call:
private variables give the code somewhere to keep timers and latches, and public ones
cover fields the export left out.

The ST editor shows the type's parameters and variables beside the text; the existing ST
compiler checks it on save and lists errors with line numbers. Sections run in their
order, every scan, for every instance. Changes take effect at the next Run, which is a
cold start (my call). FBD sections follow on the PLC canvas. LD waits for an LD engine,
which would also run the exported LD sections.

### Storage

Saving writes a ModbusForge project file with its own extension, .xef_sf (the user's
name). The XEF is only for the first import and is never changed; from then on the
engineer opens the .xef_sf. The PLC tab gets Open, Save and Save As for it.

The file has ModbusForge's own layout and holds only what ModbusForge uses after an XEF
import: everything the PLC tab shows (programs and their canvas layout, DFB types,
variables, tasks, the hardware) and everything the runtime needs (types, sections, the
I/O map). A protected type is stored as a shell: its interface and a mark that its code
is protected. The encrypted original is not carried, as ModbusForge cannot use it. The
engineer's variables and sections are stored with their type. The file carries a format
version, so later versions of ModbusForge can still open it. It does not need to open in
Control Expert.

## Phases

1. The Derived FB Types node: every type with its interface and sections, exported
   sections opening as program sections do (ST as text, FBD drawn), and locked types marked empty in
   the navigator, on the canvas, in the Console and in the status text.
2. The .xef_sf project file: Open, Save and Save As on the PLC tab.
3. The engineer's ST sections and variables for locked types, saved in the .xef_sf and run
   per instance.
4. The engineer's FBD sections.
5. LD: an engine and a view, for exported LD sections and the engineer's.

## Acceptance for phases 1 to 3

For each of the 23 corpus projects (phase 2):

- Importing the XEF, saving the .xef_sf and opening it gives the same navigator, the same
  canvases and the same runtime results as the import: the same blocks compute, and the
  same scans give the same values.

On SN6550 (exported types):

- Selecting LCN_DIG101's Command_FBD section draws it on the canvas.
- Selecting LCN_xILock's Status section shows its ST.

On LCPLC001 (locked types):

- Derived FB Types lists zPLCInfo, xANL101, xANL102 and zScale as locked (empty), with
  the interface the XEF gives each (xANL101: 19 inputs, 14 outputs, 5 inputs/outputs).
- The two xANL101 instances in Analogs_IN (PT0001, PT0002) read "empty (protected DFB)".
- ST written for xANL101 (for example fRealVal scaled from fAnaIn between nEngZero and
  nEngFull) makes both instances compute after Run, marked as the engineer's code, and
  the Console says so.
- Saving as LCPLC001.xef_sf and opening that file brings the xANL101 code back;
  LCPLC001.XEF is unchanged.
- If xANL101's interface in the file no longer matches the one its code was written for
  (for example after the file was edited outside ModbusForge), the code is kept, not run,
  and the engineer is told.

## Left to the builder

The .xef_sf encoding and layout, how a type's interface and its code are checked against
each other, the exact layout of the type view, how compile errors are shown, and the
tests.
