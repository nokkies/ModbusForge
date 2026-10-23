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
2. A locked type shows its interface and one empty section, greyed with a lock and the
   note "Empty: the code is protected in the export". Its instances on the canvas read
   "empty (protected DFB)" instead of "not simulated".
3. The engineer can give a locked type their own code in ST, FBD or LD, the languages a
   DFB section can have. It runs for every instance of the type, as a DFB section does in
   Control Expert.
4. The engineer's code is stored on their machine next to the project file and comes back
   when the same file is opened again.
5. Types whose code is exported keep running it, as today. Their sections are part of the
   selection on the left: selecting one opens it read-only, as a program section opens.

## Non-goals

- Decrypting or working around protected code. The only route to the original code is an
  unprotected export from its owner.
- Generating DFB code automatically.
- Building any client DFB into ModbusForge.
- Editing exported (unlocked) code.
- Sharing the engineer's code between projects: it belongs to the project file it was
  written for.

## Anti-goals (a failure even if it works)

- The engineer's code mistaken for the original. Wherever it runs it is marked as the
  engineer's: the type in the navigator, each instance on the canvas, and the Console when
  the PLC starts ("xANL101 runs the engineer's ST, not the original code").
- Code running against an interface it was not written for. If the XEF's interface of a
  type changed since the code was written, the code is kept but not run, and the engineer
  is told.
- Writing the same code once per instance.

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
  sections are listed but not run. Exported types in the corpus have 105 FBD, 66 ST and
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

## Design

### Navigator

"Derived FB Types" sits beside "Data Management" under Application (my call). One row per
type, marked exported, locked (empty) or locked (engineer's code). Under each type:
Inputs, Outputs, Inputs/Outputs, Public variables and Private variables (name, type,
initial value, comment, in the Data Management grid), then Sections (name, language,
protection). A locked type's original code is one greyed, locked section: "Empty: the
code is protected in the export". The engineer's sections are listed beside it, marked as
the engineer's. A section protected on its own in an otherwise exported type (possible in
Control Expert, p. 359, though no project in the corpus has one) shows the same way, and
the type is marked partly locked, so the rest of its code is never taken for the whole
(my call).

Selecting a section opens it as a program section opens: ST as text, FBD drawn on the
canvas, LD listed until there is an LD view. Exported code opens read-only. A section
opens without live values, because values belong to an instance, not to the type;
showing them for a chosen instance is left for later (my call).

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

The code belongs to the project file it was written for. One local, human-readable file
next to that file (today an XEF or ZEF export, named after it with a fixed suffix) holds,
per type: a fingerprint of the type's interface in the export, the engineer's variables,
and the engineer's sections (name, language, code). Opening the project file loads it.
Nothing is written into the export.

## Phases

1. The Derived FB Types node: every type with its interface and sections, exported
   sections opening read-only (ST as text, FBD drawn), and locked types marked empty in
   the navigator, on the canvas, in the Console and in the status text.
2. The engineer's ST sections and variables for locked types, stored next to the project
   file and run per instance.
3. The engineer's FBD sections.
4. LD: an engine and a view, for exported LD sections and the engineer's.

## Acceptance for phases 1 and 2

On SN6550 (exported types):

- Selecting LCN_DIG101's Command_FBD section draws it on the canvas, read-only.
- Selecting LCN_xILock's Status section shows its ST.

On LCPLC001 (locked types):

- Derived FB Types lists zPLCInfo, xANL101, xANL102 and zScale as locked (empty), with
  the interface the XEF gives each (xANL101: 19 inputs, 14 outputs, 5 inputs/outputs).
- The two xANL101 instances in Analogs_IN (PT0001, PT0002) read "empty (protected DFB)".
- ST written for xANL101 (for example fRealVal scaled from fAnaIn between nEngZero and
  nEngFull) makes both instances compute after Run, marked as the engineer's code, and
  the Console says so.
- Closing and importing LCPLC001 again brings the xANL101 code back.
- After xANL101's interface in the XEF changes, the code is kept, not run, and the
  engineer is told.

## Left to the builder

The file format and suffix, the fingerprint method, the exact layout of the type view,
how compile errors are shown, and the tests.
