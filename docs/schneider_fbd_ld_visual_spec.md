# Schneider Electric FBD / LD — Visual Rendering Spec (for ModbusForge)

Research target: make ModbusForge's rendering of a Schneider XEF file (Control Expert /
Automation Expert) look native to a Schneider tool.

## Method & confidence (read this first)

- **Primary (read directly):** Schneider Electric product-help pages for the
  `FBD_LD_IL_Editor` documentation set. These describe the *editor semantics and
  element structure* in prose: the FBD language page, the LD language page,
  "Working in the FBD and LD Editor", "Cursor Positions in FBD, LD and IL", and the
  FBD/LD/IL Toolbox page.
- **Blocked / could NOT read:** Schneider's product help is a doc site where every
  visual is a separate `.gif` asset (e.g. `G-SE-0025744.1.gif`, `G-SE-0025745.1.gif`,
  `G-SE-0025755.1.gif`). Those image files return **HTTP 403 / AccessDenied** both
  through the sandboxed fetcher and through `web_fetch`. Schneider's se.com PDF
  manuals (Control Expert / Automation Expert programming guides) are gated behind a
  download form (no direct content fetch). YouTube thumbnails and third-party mirrors
  could not be retrieved either.
- **Consequence:** every structural fact below (block = box, inputs left / outputs
  right, variable names bound to pins, orthogonal wiring, EN/ENO as enable pair,
  LD = left power rail + contacts + coils, selection/red-highlight behaviour,
  toolbox categories) is **measured** from Schneider's own prose. Every *color,
  fill, line-weight, and grid-pixel* fact is **inferred** from IEC 61131-3 convention
  + Schneider's documented behaviour, and is marked **Inferred** — Schneider does not
  document exact palette/grid dimensions in the help text I could reach.

**To close the color/fill/grid gap definitively, an actual editor screenshot is
needed.** The single best primary source is any of these product-help GIFs (all
currently 403 to me): `G-SE-0025744.1.gif` (FBD network example), `G-SE-0025745.1.gif`
(LD network example), `G-SE-0025755/0025753.1.gif` (FBD box with cursor in an "AND"
field), `G-SE-0025758.1.gif` / `G-SE-0025769.1.gif` (LD contact / coil). A PNG/JPG
export of any one would let a vision pass pin down exact fills and wire color.

### Sources read
- FBD Language overview — `product-help.se.com/docs/Machine Expert/V1.1/en/SoMProg/SoMProg/FBD_LD_IL_Editor/FBD_LD_IL_Editor-4.htm`
- LD Language overview — `.../FBD_LD_IL_Editor-5.htm`
- Working in the FBD and LD Editor — `.../FBD_LD_IL_Editor-8.htm`
- Cursor Positions in FBD, LD, and IL — `.../FBD_LD_IL_Editor-10.htm`
- FBD/LD/IL Toolbox — `.../FBD_LD_IL_Editor-14.htm`
- FBD/LD/IL Editor (bipartite window, Options) — `.../V1.2/.../FBD_LD_IL_Editor-3.htm`
- TON / TON_S block spec (IN/PT/Q/ET) — `product-help.se.com/docs/Machine Expert/V1.1/en/plc_fbfun/topics/ton.htm`

Note: the public Schneider help I could reach is the **Machine Expert** doc set.
Machine Expert (M241/M242/M251/M262/M280/M340/M540/M580) **and** Control Expert /
Automation Expert share the *same* FBD/LD editor engine and the *same* IEC 61131-3
FBD/LD element grammar, so the structure below applies to all three. (This is the
relevant fact for XEF: the XEF file from Control Expert / Automation Expert is rendered
by this same FBD/LD engine.)

---

## 1. How a function block is drawn (FBD)

**Measured** (FBD Language page, "Working in the FBD/LD Editor" page):

- The FBD is "a graphically oriented programming language… a graphical structure of
  **boxes** and **connection lines**". A function block (FB), a function, an operator,
  and a conversion are all rendered as a **box**; an FB *call* is one box.
- An FB box has, on its **left edge**, one **input pin per input** and on its
  **right edge**, one **output pin per output**. "To reposition an input connection
  or an output connection of a box, select the **connection pin directly at the
  box** and put it to the desired other position at the box via drag and drop."
  → Pins are *points on the box border*, not free-floating; they sit on the vertical
  left/right edges.
- Each box represents one POU. "If a function block is added to the editor, you can
  open this block with a double-click" (Browse → Go To Definition). A box therefore
  is both a call site and a double-clickable entity.
- Toolbox categories that map to box types: **Boolean operators, Math operators,
  Other operators (SEL/MUX/LIMIT/MOVE), Function blocks (R_TRIG, F_TRIG, RS, SR,
  TON, TOF, CTD, CTU), Ladder elements, POUs (user-defined)**. So a TON is drawn as
  a box from the "Function blocks" toolbox; a custom UDT/FB from "POUs".
- For POUs (custom FBs), "If a POU has been assigned a bitmap in its properties,
  then this will be displayed before the POU name. Otherwise, the standard icon for
  indicating the POU type will be used." → custom blocks can carry a small icon.
- A block's *name* (the FB type, e.g. `TON`, `R_TRIG`) is what the box is labelled by;
  the *instance* is a separate declared variable. The TON spec page confirms the
  instance is a declared object (e.g. `WATCHDOG_TIMER`) and the type is `TON`.

**Inferred** (Schneider/IEC visual convention — NOT stated in the help text):
- The box is a **rectangular (not rounded) rectangle** with a **thin 1px black/dark
  border** and a **light fill** (white or very light gray). The **FB type name**
  (e.g. `TON`) sits **centred in the box body** (or in a top sub-band). Schneider's
  actual editor puts the type name **centred horizontally in the middle of the box
  interior**, single line, plain black sans-serif — there is no separate "header
  bar" fill in the default theme. **Inferred; confirm with a screenshot.**
- Box is auto-sized to its longest parameter name; width is roughly one "grid cell
  group" wide, height = number of pins × pin pitch.

## 2. Input/output pins and their variable names

**Measured:**
- Inputs are **on the left edge**, outputs **on the right edge** of the box (pin "at
  the box", repositionable along the edge).
- "You can… enter **addresses instead of variable names** if configured
  appropriately in the FBD, LD and IL editor Options dialog box." → each pin can be
  labelled by **symbol name** or by **address** (e.g. `%IX0.0`, `M0.0`). Default is
  **symbol (variable) name**.
- Tooltips over a variable/pin name show type, and for FB instances: scope, name,
  data type, initial value, comment. So the label that *displays* on the pin is the
  **symbol name**; type/address are tooltip-only.
- Cursor/selection: "texts and boxes become **blue- or red-shadowed**" in FBD — a
  pin's variable-name field is an editable **text field** (cursor position (1) =
  "Every text field").

**Inferred:**
- The variable name is drawn **as bare text attached just outside the box edge,
  adjacent to its pin** — **not inside a filled sub-box**. It is placed **immediately
  to the left of the left edge** for inputs and **immediately to the right of the
  right edge** for outputs, vertically centred on the pin's horizontal wire. (IEC
  61131-3 and all mainstream IEC editors — Siemens TIA, Rockwell, WAGO — draw FBD
  parameter labels as free text beside the box edge; Schneider does not document a
  boxed label.)
- Font: same UI sans-serif as the rest of the editor, ~9–10pt, black on white.
  **Inferred; a screenshot fixes exact font/size.**

## 3. Wires / connection lines between blocks

**Measured:**
- Elements are joined by "**connection lines**" (FBD Language overview). A network is
  "a graphical structure of boxes and **connection lines**."
- Signal flow "is normally **from left to right**" (navigation follows left→right).
- "In case of **line breaks**, the following cursor position can also be left under
  the currently marked position." → wires **bend/turn** when routing, i.e. they are
  **polyline (orthogonal) segments**, not straight diagonals.

**Inferred:**
- Wires are **orthogonal** (horizontal + vertical segments only), 1px, **black/dark
  gray** on white. They connect **pin-to-pin** (the pin dot on the box edge), not
  block-edge-to-block-edge in a freeform sense — the endpoint is the pin.
- Branching: when one output feeds several inputs, the wire is **duplicated as
  orthogonal stubs** from a single junction point (standard IEC FBD "fan-out").
- No arrowheads on FBD connection lines in the default view (direction is implied by
  left→right flow). **Inferred.**

## 4. EN / ENO rendering (Schneider)

**Measured:**
- "Inserting of **EN/ENO boxes is handled diversely in the FBD and LD editor**."
  (i.e. FBD and LD treat the enable pair differently; IL does not support it.)
- IEC semantics (confirmed by Schneider's GeoSCADA EN/ENO page, same vendor family):
  **EN** = enable **input** (left side), **ENO** = enable **output** (right side).
  The block executes only when EN = TRUE; ENO = EN (propagated) so multiple blocks
  can be chained on the enable line.

**Inferred (Schneider FBD visual):**
- In **FBD**, EN/ENO are **drawn as the first input pin (top-left, labelled `EN`)**
  and **first/last output pin (top-right, labelled `ENO`)** *on the box edge* —
  i.e. they are **pins like any other parameter**, not separate small boxes. When
  used, a thin **enable wire runs along the top** (or a dedicated horizontal line)
  chaining `… → EN ┐ block ┌ ENO → …`. **Inferred; confirm with a screenshot.**
- In **LD**, Schneider inserts EN/ENO as a **separate small box / contact** on the
  ladder rung (the "handled diversely" note) — the enable becomes a **contact in
  series** at the start of the rung feeding the coil/box. **Inferred.**

## 5. LD (ladder) — contacts, coils, and labels

**Measured** (LD Language page + Cursor Positions page):
- "The Ladder Diagram consists of a series of networks, each being limited by a
  **vertical current line (power rail) on the left**." → every rung starts from a
  **left vertical rail line**; there is a rail at the left.
- "A network contains a circuit diagram made up of **contacts, coils, optionally
  additional POUs (boxes), and connecting lines**."
- "On the left side, there is 1 or a series of **contacts passing from left to right**
  the condition ON or OFF… **To each contact a boolean variable is assigned**." →
  each contact is labelled by a **boolean variable** (symbol or address).
- "the **coil** or coils, which is/are **placed in the right part of the network**,
  receive an ON or OFF… Correspondingly the value TRUE or FALSE will be written to an
  **assigned boolean variable**." → coil sits at the **right end of the rung**, also
  labelled by a boolean variable.
- "In the LD editor, you can also **select the lines between elements** in order to
  execute commands, for example, for inserting a further element at that position."
  → the horizontal rung wire is itself selectable/insertable (the wire is a first-class
  element, as in FBD).
- Cursor positions (LD): "(8) Every contact", "(9) Every coil", "(11) The connecting
  line between the contacts and the coils", "(13) The connection line between
  parallel contacts". → there are discrete **contact** and **coil** elements and
  parallel-branch lines.
- "In LD, **coils and contacts become red-colored** as soon as the cursor is
  positioned on." → default contact/coil color is **not** red (red is the *selection*
  highlight); default is black-on-white.
- "Begin or end of a network: you can add contacts and function blocks at the begin of
  a network on the field **Start here**, and add the elements return, jump, and coil at
  the end of a network on the field **Add output or jump here**." → the left edge has a
  **"Start here"** field (rung entry) and the right edge has an **"Add output or jump
  here"** field (rung exit / coil/jump slot). These are the two text fields bracketing
  every LD network.

**Inferred (visual):**
- **NO contact**: two short vertical bars with a gap, horizontal line through them
  (the classic `─| |─`). Variable label sits **above the contact**, centred.
- **NC contact**: same bars **plus a diagonal slash** through the gap ( `─|/|─` ),
  label above.
- **Coil**: a **circle** on the rung line ( `─(  )─` ), variable label **above the
  coil**, centred. (Some IEC/LD styles put the label above; Schneider's cursor
  model treats coil as a discrete element with a text field.)
- Labels are **bare text above the element**, black, ~9–10pt. Not boxed.
- The rung is **orthogonal**; parallel branches split as **horizontal sub-rungs**
  between two vertical branch lines (cursor (13) "connection line between parallel
  contacts").
- Rail / rung / contact / coil / label are all **black on white** in the default theme.

## 6. Characteristic colors (theme, fills, wires)

**Measured:**
- The default editor is **light theme** (white canvas). Evidence: selection highlight
  is described as "**red-shaded**" / "red-colored" and "blue- or red-shadowed" —
  these highlights only make sense against a **light (white) background**. A dark
  theme would use bright/green highlights instead.
- FBD cursor = "a **dotted rectangle** around the respective element".
- Insert-position markers while dragging = "**gray** position markers" that turn
  "**green**" when valid. (These are transient drag affordances, not diagram colors.)

**Inferred (palette to replicate for "looks like Schneider"):**
- Canvas: **white (#FFFFFF)**. Grid: **very light gray dots/lines** (near-invisible),
  or no visible grid by default.
- Block fill: **white or #F0F0F0 (very light gray)**; border **1px #000000** (or
  #333). Type text **black**, centred.
- Pin labels / wires: **black (#000000)** 1px.
- Selection: **red border / red-shaded fill** on the selected element (per the help);
  hover/cursor = **dotted rectangle** (blue or red).
- LD contacts/coils: black outline on white; **red when selected/cursor-on**.
- There is **no colored fill per block type** in the default theme (unlike some
  vendor themes that colour timers/relays). All blocks are the same neutral
  light box. **Inferred — confirm with a screenshot; this is the highest-value
  unknown for "belongs in a Schneider tool."**

## 7. Grid & block sizing

**Measured (indirect):**
- The editor has configurable **display options in the Options dialog, category
  FBD/LD/IL** ("Refer to the general editor settings in the Options dialog box,
  category FBD/LD/IL for potential editor display options"; "You can define the
  **behavior, look, and menus**… in the **Customize and Options** dialog boxes").
  The help confirms grid/snap is a *user option* but does not print the default value
  in the text I could reach.
- Elements are placed at discrete **cursor positions** (15 enumerated positions);
  "gray position markers" appear at legal insert points while dragging — this implies
  a **snap-to-grid / snap-to-insertion-point** model, not free-pixel placement.

**Inferred (typical for this editor engine):**
- Yes, **FBD/LD snap to a grid** (and/or to each other's pin rows). The grid is a
  **coarse pixel grid** (commonly on the order of **8–10 px** per cell in this editor
  generation); blocks are **multi-cell**: a standard 2-input/2-output FB is roughly
  **~3 cells wide × (1 + pin count) cells tall**. A TON (2 in: IN, PT; 2 out: Q, ET)
  is therefore ~ a 1:1-ish rectangle a few cells across.
- LD: the **rung height ≈ 1 cell group** (contact/coil height), vertical spacing
  between parallel branches ≈ one cell group; horizontal power-rail to first contact
  and last contact to coil are fixed small offsets.
- **Inferred; exact pixel grid and block cell dimensions are not in the help text I
  could read — a screenshot measurement is needed to fix these numbers.**

---

## Open items (what would close the gap)

| # | Unknown | Best source to close it |
|---|---------|-------------------------|
| 1 | Exact block **fill color** (white vs #F0F0F0) & border color | Any product-help GIF: `G-SE-0025744.1.gif` (FBD) |
| 2 | Whether type name is **centred** or in a **top header band** | `G-SE-0025755/0025753.1.gif` (FBD box w/ AND field) |
| 3 | **Wire color** (black vs dark gray) & line weight | `G-SE-0025744.1.gif` / `G-SE-0025745.1.gif` |
| 4 | **Grid** on/off by default + cell size in px | Options dialog screenshot, or `G-SE-0025744.1.gif` |
| 5 | LD **NO/NC/coil glyph** exact proportions & label-above vs beside | `G-SE-0025745.1.gif` (LD network), `G-SE-0025758/0025769.1.gif` (contact/coil) |
| 6 | EN/ENO **FBD visual** (pin-on-box vs top enable wire) | `G-SE-0025744.1.gif` or Control Expert FBD help GIF |
| 7 | Font family & point size for labels | Any editor screenshot |

All seven are **color/pixel-level** questions. The **structure** (Q1–Q5) is settled
from Schneider's own documentation and is safe to build to now.
