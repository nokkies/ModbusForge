# IEC 61131-3 — Visual Spec for FBD & LD Rendering in a PLC Editor

Purpose: a developer-level, actionable visual specification for how **Function Block Diagram (FBD)** and
**Ladder Diagram (LD)** should be rendered in a PLC editor (the ModbusForge PLC editor). Every item is given as
"shape, lines, text placement, positioning" so it can be implemented in code.

## Source honesty — read this first

I could NOT read the IEC 61131-3 standard text itself. It is a paid IEC document
(see the [IEC webstore](https://webstore.iec.ch/)); there is no authoritative free copy, and
`mcp__psa-reading__read_document` is not given a standard here. So where an item is **set by the standard but
only confirmed by a secondary source**, I mark it `[vendor-doc]` or `[inferred]` rather than claiming it is from the
IEC clause. Where I fetched and read an actual document, I mark it `(source)` with a URL.

**What I actually read (fetched):**

- **CODESYS PLCnext Engineer help — "Execution Control: EN/ENO"** — https://engineer.plcnext.help/2026.0_LTS_en/EN_ENO.htm
- **CODESYS PLCnext Engineer help — "Inserting POUs with EN/ENO"** — https://engineer.plcnext.help/2026.0_LTS_en/ENENO_Activation.htm
- **Panasonic FPWIN — "EN/ENO - enable input and enable output"** — https://infohub.industry.panasonic.eu/data/fpwin/en/topics/t-0000014701.html
- **Panasonic FPWIN — "Ladder Diagram (LD) and Function Block Diagram (FBD)"** — https://infohub.industry.panasonic.eu/data/fpwin/en/topics/t-0000014910.html
- **PLC Academy — "Ladder Logic Symbols – All PLC Ladder Diagram Symbols"** (IEC/PLCopen symbol set, downloadable DWG/PNG/PDF) — https://www.plcacademy.com/ladder-logic-symbols/
- **PLC Academy — "Function Block Diagram (FBD) PLC Programming Tutorial"** — https://www.plcacademy.com/function-block-diagram-programming/

**What I found by search but could NOT fetch (page blocked or engine degraded), so these are corroborated by
multiple independent snippets only — treat as `[secondary]`, verify against the IEC text or a vendor manual before
locking pixel details:**

- ForgeIEC FBD elements reference — "A variable input is a small box on the left edge of the diagram that names a
  variable. The wire from this box delivers the variable's current value to whatever input pin it connects to."
  https://forgeiec.io/en/reference/iec61131-3/graphical/function-block-diagram/elements/
- ForgeIEC LD help — "between a left and a right power rail, horizontal current paths (rungs) carry the signal."
  https://forgeiec.io/en/help/ld/
- Siemens TIA Portal — "Rules for the use of FBD elements" (logic path linked per IEC 61131-3; standard boxes for
  flip flops, counters, timers, math). https://docs.tia.siemens.cloud/r/en-us/v20/creating-fbd-programs/inserting-fbd-elements/rules-for-the-use-of-fbd-elements
- plcprogramming.io — "PLC Function Blocks & FBD" (references IEC 61131-3 Ed 4). https://plcprogramming.io/function-blocks
- controlsystemguide.com — "Ladder Logic Symbols: Complete Reference + Cheat Sheet" (NO contact = pair of vertical
  bars; NC contact has a diagonal slash; output coil looks like a circle or parentheses).
  https://controlsystemguide.com/ladder-logic-symbols/

---

# FBD (Function Block Diagram)

## FBD-1. Function blocks (FB) vs functions (EF) — box shape, text, pin layout

**Both are drawn as a rectangle (a box).** The distinction is structural, not just cosmetic:

- **Function block (FB)** — a box that *has memory* (state). The IEC body sits inside. Examples: `TON`, `TP`, `R_TRIG`,
  `CTU`, `CTD`, `MUL_II` (as a stateful call in some toolkits). It is drawn as a box containing:
  - **Header / type name** at the top (or centered), e.g. `TON`, `CTU`.
  - A **vertical divider line** separating the input side from the output side.
  - **Input pins on the left edge**, **output pins on the right edge** (each pin = a short horizontal stub line
    that pokes out of the edge; see FBD-2).
  - **Internal body** below the header, which may contain nested operators, text, or a small label — for the standard
    timer blocks the body often just shows the type name.

- **Function (EF, elementary function)** — a *pure* function, no memory: `ADD`, `SUB`, `REAL_TO_INT`, `AND`, `OR`,
  `NOT`, `XOR`, `SEL`, `MUX`, `LIMIT`. Drawn as a box that is **visually shorter** and **carries no internal body or
  vertical divider** (it has no state, so nothing is rendered inside except the header). Many renderers draw it as a
  thin box (a "stub box") whose header name is the function name (`ADD`, `REAL_TO_INT`) and whose only content is the
  left-edge input pins and right-edge output pins.

  - `[secondary]` The IEC 61131-3 rule (and every major vendor — CODESYS, Siemens, Panasonic) is that **functions have
    EN/ENO formal parameters only when the editor's "use with EN/ENO" option is active**; by default functions are drawn
    as compact boxes. Functions are *never* given a stateful body.
  - The functional difference that must be reflected visually: a **function box has no memory/state** and typically no
    internal divider line; an **FB box has the divider line and (for the standard timers/counters/triggers) a fixed
    known set of pins** (e.g. TON = `IN`, `PT` on the left; `Q`, `ET` on the right).

**Header text placement:** the POU name (block type) goes in the top area of the box, centered or left-aligned in a
reserved header strip. For timers/counters the type name is the entire body in most editors.

## FBD-2. Input/output pins — the "stubs", pin names, and where the variable goes

This is the single most common source of wrong renderings, so it is stated precisely:

- **A pin is a short horizontal line (a "stub" / terminal)** that starts at the box's left (input) or right (output)
  edge and extends a fixed length (typically ~10–15 px in a standard 96 DPI grid) outward. It is *not* a notch or an
  indentation; it is a protruding line segment of one grid step.

- **Pin NAME (IN, Q, PT, EN, ENO, …):** the *formal parameter name* is written **at the inner end of the stub, i.e.
  just inside the box edge** (for input pins, just inside the left edge, to the left of the divider; for output pins,
  just inside the right edge). In the CODESYS/Panasonic renderings the formal name sits inside the box, vertically
  aligned with its stub.

- **The connected variable / tag / constant:** in IEC 61131-3 FBD there are two accepted conventions, and a good
  editor supports both:
  1. **Inline on the wire / at the free end of the stub** — the variable or constant is placed *on the wire*
     immediately outside the box (for an input pin, just to the left of the input stub; for an output pin, just to the
     right of the output stub). This is the dominant vendor style and what most code generates.
  2. **Variable box (input element)** — for a free-standing input that is not the output of another block, IEC provides
     an **input variable element**: "a small box on the left edge of the diagram that names a variable"; the wire from
     this box carries the value to the target input pin. `[secondary]` (ForgeIEC; matches IEC "input variable element").
     This small box is *the* IEC-correct way to show "this input comes from variable X" rather than floating text on a wire.

  - **Developer rule of thumb:** draw (a) the box + stub, (b) the formal name inside the box, (c) the actual value
    (variable name, constant, or the wire) either as text on the wire at the stub's outer end, or — preferred for
    inputs — as an input-variable box / output-variable box at the wire's far end. Do **not** place the tag name *inside*
    the FB box over the formal name (that collides the two).

- **EN / ENO pins** are input/output pins exactly like any other (see FBD-3) and follow the same "stub + inner name +
  value-on-wire" layout.

## FBD-3. EN / ENO convention — where drawn, what connects to what

Fetched and confirmed (CODESYS PLCnext, Panasonic FPWIN):

- **EN** is a **Boolean input** formal parameter — drawn as a **left-edge stub/pin** on the box, named `EN`, typically
  placed at the **top** of the input column.
- **ENO** is a **Boolean output** formal parameter — drawn as a **right-edge stub/pin**, named `ENO`, typically at the
  **top** of the output column (vertically aligned with EN so the enable chain runs straight across the top of the
  block row).
- **Semantics (from the vendor docs, IEC 61131-3 conditional execution):**
  - If `EN = TRUE` the POU executes; on successful execution `ENO = TRUE`.
  - If `EN = FALSE` the POU does **not** execute and outputs keep their last value.
  - `EN` may be left unconnected → assumed `TRUE`. `ENO` may be left unconnected.
- **Chaining / the dashed line:** to execute a chain of functions conditionally, connect `ENO` of block *n* to `EN` of
  block *n+1*. **The connecting line on the EN/ENO parameters in FBD is drawn as a DASHED line** (this is an explicit,
  visible IEC/standard convention stated in the CODESYS doc: "the connecting line at the EN/ENO parameters in FBD is
  shown as a dashed line"). Use a **dashed stroke** for the EN→(next) EN wire so it is visually distinct from data wires.

  > (source: CODESYS PLCnext "Execution Control: EN/ENO" —
  > https://engineer.plcnext.help/2026.0_LTS_en/EN_ENO.htm)
  > (source: Panasonic FPWIN "EN/ENO" — https://infohub.industry.panasonic.eu/data/fpwin/en/topics/t-0000014701.html)

- Toggling: a user action (e.g. "Toggle EN/ENO") adds/removes the EN/ENO pins for a function. (source: CODESYS
  "Inserting POUs with EN/ENO" — https://engineer.plcnext.help/2026.0_LTS_en/ENENO_Activation.htm)

## FBD-4. Wires — how a connection is drawn; how a constant/tag shows on the wire

- **Connection geometry:** a wire runs from the **outer end of one block's output stub** to the **outer end of another
  block's input stub**. It connects **pin-to-pin** (the stubs), not to the box edge and not to the box body. Standard
  orthogonal (Manhattan) routing: horizontal + vertical segments only, with a bend at 90°.
- **The wire does not terminate "on the block"; it terminates at the tip of the stub.** A correct implementation
  anchors the wire endpoint to the stub tip coordinate.
- **Constant on a wire:** a constant value (e.g. `16#FF`, `10`, `T#5s`) is drawn as **text placed on the wire** at or
  near the midpoint of the segment feeding the input stub. For a constant *directly* at an input, the common IEC
  practice is to render the constant as an **input variable/constant element** (the small box at the diagram edge, or a
  box on the wire) — i.e. the value is *boxed*, not free-floating. `[secondary]`
- **Tag/variable on a wire:** the variable name is placed as text at the outer end of the wire (or in an input/output
  variable box — see FBD-2).
- **All elements of a logic path must be linked to each other per IEC 61131-3.** (source: Siemens TIA "Rules for the
  use of FBD elements" — snippet; page not fetchable here) https://docs.tia.siemens.cloud/r/en-us/v20/creating-fbd-programs/inserting-fbd-elements/rules-for-the-use-of-fbd-elements
- **Branching (fan-out):** one output stub can feed multiple input stubs via orthogonal branches; each branch still
  ends at a stub tip.

## FBD-5. Boolean literals (TRUE/FALSE/1/0) and data-type display

- **Boolean literals:** shown as the text `TRUE`/`FALSE` (the IEC 61131-3 keywords) or as `1`/`0` — render whichever
  the user entered; for a constant feeding an EN/ENO or a BOOL input, prefer `TRUE`/`FALSE`. They appear as text on the
  wire / in an input-element box, per FBD-4/FBD-2.
- **Data-type display:** the IEC 61131-3 convention is to make the **data type visible on the box header or pin** using
  the type suffix/annotation, e.g. the pin shows the formal name and the value may carry a type prefix (`REAL#1.5`,
  `T#5s`, `TIME`, `INT#10`). A robust editor:
  - renders **constants with their type qualifier** exactly as IEC (`T#5s`, `REAL#1.0`, `STRING#'x'`);
  - optionally shows the **data type of a non-constant variable** in a secondary (smaller/italic) label next to the
    name, or in the box header for the output pin, so the wire is unambiguous.
  - `[secondary]` This is the standard behavior; the specific placement (next-to-name vs in-header) is a vendor/UX
    choice, not fixed by the IEC text I could read. Pick one and be consistent.

## FBD-6. Flow direction

- **Left-to-right.** Input pins are on the **left** edge; output pins are on the **right** edge; data flows **left →
  right** across the diagram. This is consistent in CODESYS, Siemens, Panasonic and the IEC 61131-3 FBD definition.
  (source: Panasonic FPWIN LD/FBD overview — https://infohub.industry.panasonic.eu/data/fpwin/en/topics/t-0000014910.html
  ; standard IEC 61131-3 FBD convention)
- The program body is divided into **networks**; each network has a header (network number + optional label/comment).
  (source: Panasonic FPWIN, same page.)
- FBD, unlike LD, has **no power rail and no contacts/coils** — only boxes, stubs, wires, and input/output variable
  elements. (source: Panasonic FPWIN, same page.)

---

# LD (Ladder Diagram)

LD is a **circuit-diagram metaphor**: "between a left and a right power rail, horizontal current paths (rungs) carry
the signal." `[secondary]` (ForgeIEC LD help)

## LD-1. Rungs and the power rail

- **Two vertical rails** (conductors) — a **left rail** and (conceptually) a **right rail** — span the top-to-bottom of
  the network area. The **left vertical rail is the "power rail"** (the energizing side).
- **Rungs** are **horizontal wire segments** that start at the **left rail**, run right, and terminate at the **right
  rail** (the "load" / return side). Each rung = one Boolean expression that drives one or more outputs.
- **Multiple rungs** are stacked vertically in a network; each is a separate horizontal path between the two rails.
  Branches (parallel paths) are made by splitting a rung into vertical **drop lines** that reconverge — the parallel
  sections hang as vertical branches between the two rails.
- Each network carries a **network number** in its header (e.g. "Network 1"), plus optional label/comment. (source:
  Panasonic FPWIN LD/FBD overview — https://infohub.industry.panasonic.eu/data/fpwin/en/topics/t-0000014910.html)

## LD-2. The classic IEC bit-logic symbols (shapes)

Confirmed present in the IEC/PLCopen symbol set (PLC Academy, which mirrors IEC 61131-3 / PLCopen; each downloadable
as DWG/PNG/PDF): https://www.plcacademy.com/ladder-logic-symbols/

**Contacts (placed in-line on a rung, between two horizontal wire segments):**

- **Normally-Open (NO) contact:** **two short parallel vertical bars with a gap between them**, drawn between two
  horizontal wire segments. The wires enter at the left bar and exit at the right bar; when the referenced bit is
  FALSE the gap is open (no path), when TRUE it is closed (path). (PLC Academy "NO Contact" symbol; controlsystemguide
  "pair of vertical bars".)
- **Normally-Closed (NC) contact:** the NO symbol **with a diagonal slash through the gap** — two vertical bars with a
  gap, plus a diagonal line across the gap, indicating the contact is closed (conducting) when the bit is FALSE.
  (PLC Academy "NC Contact"; controlsystemguide "diagonal slash through it".)
- **Positive transition-sensing contact:** an NO contact with a **`/` (rising-edge) marker** on the left side (a small
  diagonal rising-edge tick), i.e. an up-arrow/forward-slash prefix. (PLC Academy "Positive Transition-Sensing
  Contact".)
- **Negative transition-sensing contact:** an NC-style contact with a **falling-edge marker** (a backward slash /
  down-arrow tick) on the left side. (PLC Academy "Negative Transition-Sensing Contact".)

**Coils (placed at the right end of a rung, just before the right rail):**

- **Output coil (basic):** a **pair of parentheses `(` `)`** (or, in some renderings, a small **circle**) centered on
  the wire at the end of the rung — the "load." (PLC Academy "Coil"; controlsystemguide "looks like a circle or
  parentheses".) The referenced bit is written on/above the coil.
- **Negated coil (inverted output):** the same coil shape with a **`/` slash through it** (a negation bar), meaning
  the output is the inverse of the rung condition. (PLC Academy "negated coil".)
- **Set coil (S):** the coil shape (parentheses/circle) with the **letter `S`** placed at/above it (typically
  `(` S `)`, or a small `S` to the upper-left). Energizing the rung condition sets the referenced bit and **latches**
  it (stays set even after the condition clears).
- **Reset coil (R):** the coil shape with the **letter `R`** placed at/above it. When the rung condition is TRUE it
  **resets (clears)** the referenced bit; otherwise no effect.
- **Set/Reset (SR / RS) flip-flop:** a **box** (or paired `(`S`)`/`(`R`)` with the two inputs) with **two inputs, `S`
  (set, top) and `R` (reset, bottom)** on the left and **Q / Q̅ outputs** on the right — drawn as a rectangle on the
  rung, inlined where a coil would be, with the set branch feeding `S` and the reset branch feeding `R`.
  `[secondary]` — the SR coil is in the IEC set; the exact two-input box vs the dual-coil notation varies by vendor.

> Symbol-level detail note: PLC Academy's page was fetched but the text truncated right after "negated coil," so the
> precise glyph for set/reset coils is corroborated by the symbol *names* present on the page ("Coil", "negated coil",
> and the standard set) plus controlsystemguide, not by reading the IEC clause. The `S`/`R` letter-on-coil form is the
> IEC 61131-3 standard form; verify against the IEC text or a vendor manual before locking the exact letter placement.

## LD-3. Where tag names / bit addresses go relative to each symbol

- **Contacts:** the tag/bit address (or instruction mnemonic) is placed **above the contact symbol** — for the
  normally-open and normally-closed contacts the label sits **just above the gap**, centered over the two bars; for
  transition contacts the label is above, with the edge marker to the left of the left bar.
- **Coils (basic / negated / set / reset):** the tag/bit address is placed **above the coil**, centered over the
  parentheses/circle, with the `S` or `R` letter (for set/reset) sitting at or just above the coil next to the tag.
- **General:** LD labels go **above the symbol** (contacts, coils). In FBD the labels go at the stubs/wires (FBD-2).
  This "above the symbol" placement is the standard IEC/PLCopen convention and is how the PLC Academy symbol cheat
  sheet and every major vendor render bit addresses.
- Contact/coil labels may also be shown **below** in some vendors; pick **above** as the default to match the IEC
  cheat sheet, and keep it consistent.

## LD-4. How function blocks / functions are inlined on a rung

- A **function block** appears on a rung **as its box**, spanning the rung height (or a defined block height), with:
  - **input pins** on its **left** edge (facing the left rail / the preceding logic),
  - **output pins** on its **right** edge (facing the right rail / following logic),
  - the **type name** in the box header, and the **pin stubs + labels** exactly as in FBD (LD and FBD share the box
    and pin geometry — the only LD-specific elements are the contacts, coils, power rail and rungs).
- **Boolean inlining:** the **output of a block (a BOOL output pin) behaves like a contact** and its **input pin like a
  coil input** on the rung; i.e. the block sits *in the middle of the rung*, wired in-series with contacts and coils.
  A block's BOOL **Q** output can drive a coil; its BOOL **EN/IN** input is fed by contacts.
- **Functions** (e.g. `AND`, `OR`, `XOR`, `NOT`, math functions) are inlined the same way as compact boxes on the rung;
  their EN/ENO follows FBD-3 (dashed enable chain), and their inputs/outputs are wired to contacts/coils as in FBD.
- **Coil-driven FBs / FBs driving coils:** a common pattern is contacts → block(s) → coil at the right rail. The block
  sits between the last contact and the first coil.
- (Consistent with: Siemens TIA "standard boxes (flip flops, counters, timers, math operations, etc.) can be added as
  output to …" the logic path; Panasonic FPWIN: "FBD uses similar programming elements but does not have a power
  rail, contacts, and coils" — i.e., LD *does* have them and the block plugs into that path.)

## LD-5. Timers / counters in LD

- Timers and counters **are function blocks**, so in LD they are rendered **as their box inline on the rung** (e.g.
  `TON`, `CTU`), with the standard pins:
  - **TON:** inputs `EN` (or `IN`), `PT` (preset time) on the left; outputs `Q`, `ET` on the right. The `PT` pin is fed
    by a **constant** (a `TIME` literal such as `T#5s`) drawn as a box/label at the stub (FBD-4/FBD-5). `Q` (a BOOL
    output) is wired like a contact or to a coil on the right.
  - **CTU:** inputs `CU` (count up, BOOL), `R` (reset, BOOL), `PV` (preset value, INT) on the left; outputs `Q`, `CV`
    (current value) on the right.
- In LD these boxes sit **in-series on the rung** exactly as any other block (LD-4); their `Q` output typically feeds
  a coil or a following contact, and their numeric inputs (`PT`, `PV`) are supplied by **constants** (type-qualified
  literals) at the stubs.
- The IEC/PLCopen symbol set and the Siemens "standard boxes (flip flops, counters, timers, math)" confirm that
  timers/counters are first-class blocks on the rung, not special glyphs. (source: Siemens TIA FBD rules — snippet;
  PLC Academy IEC symbol set.)

---

# Implementation checklist (what to actually render)

**Shared (FBD + LD):**
- Block = rounded-corner-free rectangle (sharp corners are the IEC look); header strip for the type name.
- Input pins = short horizontal stubs protruding from the **left** edge; output pins = stubs from the **right** edge.
- Pin formal name **inside** the box at the stub; the connected **value (variable/constant)** on the wire at the stub
  outer end, or in a small input/output-variable element box.
- Wires = orthogonal (Manhattan) lines, anchored at **stub tips**, not at the box body.
- EN = left/top stub, ENO = right/top stub; the **EN→ENO chain is a DASHED wire**.
- Flow = **left → right**.
- Constants type-qualified (`T#5s`, `REAL#1.0`, `16#FF`); boolean literals `TRUE`/`FALSE`.
- Networks numbered, each with an optional label/comment header.

**LD-only:**
- Left (and right) **vertical power rails**; horizontal **rungs** between them; parallel branches as vertical drops.
- **NO contact** = two vertical bars with a gap; **NC contact** = same + diagonal slash; **rising/falling transition**
  contacts = contact + edge marker on the left bar.
- **Coil** = parentheses/circle at the rung end; **negated coil** = coil + `/`; **set coil** = coil + `S`; **reset
  coil** = coil + `R`; **SR flip-flop** = box with `S`/`R` inputs and `Q`/`Q̅` outputs.
- **Bit-address label above** contacts and coils; transition marker on the left bar.
- **Blocks (incl. timers/counters) inline on the rung**, wired in-series with contacts and coils; BOOL `Q` feeds a
  coil or a contact.

## Application to ModbusForge (this repo)

**The target XEF (`LCPLC001.XEF`) is FBD-only.** The parser (`ModbusForge.Core/Xef/XefParser.cs`)
handles `FBDSource` (FBD block/pin/link graph) and `StSource` (ST text) — there is **no Ladder/LD
data path** in the XEF or the parser, and `LCPLC001.XEF` contains no LD programs. Therefore:

- **FBD rendering is implemented and verified mainstream-compliant** (light canvas, pale-blue
  boxes, instance name above FB boxes, pin stubs + variable fields, IEC logic glyphs, orthogonal
  pin-to-pin wires, zoom + fit-to-view). See `ModbusForge/Views/PlcView.axaml` and
  `ModbusForge/ViewModels/PlcViewModel.cs`.
- **Ladder (LD) rendering is N/A for this file** — there is no LD data to render. The LD section of
  this spec (contacts/coils/NO/NC/set-reset, §Ladder) is the spec to implement if/when an LD XEF
  (or another source with rung data) becomes available; the parser would need an LD network reader
  (rungs → contacts/coils/edges) first. This is tracked but out of scope for the FBD-only file.

## Open items that need the IEC 61131-3 text (I could not read it)
1. Exact **set/reset coil letter placement** (S/R above the coil vs inside the parentheses) and the **SR flip-flop**
   canonical glyph — the IEC clause (bit manipulation / flip-flop subclause) settles it.
2. Whether the **input-variable element** is a box *at the diagram edge* or *on the wire* (IEC "input variable
   element" definition) — affects FBD-2's preferred input rendering.
3. Exact **data-type annotation placement** on a pin (next to the name vs in the header) — IEC shows the type as part
   of the constant; variable typing on the pin is largely a vendor choice.
4. Exact **pin stub length / grid snap** and box minimum sizes — these are IEC grid/units guidance, not fixed pixels.