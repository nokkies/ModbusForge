# CODESYS FBD / LD — Visual Rendering Spec (for replication)

Research target: match the **mainstream** IEC 61131-3 look that CODESYS V3 sets.
Sources below are **measured** (fetched and read, or rendered images read via vision) unless
marked **inferred**. Where I could not verify a pixel-level detail, I say so and name what
would settle it. Do **not** treat the "inferred" lines as CODESYS fact.

---

## 0. Source quality & method

- `content.helpme-codesys.com` = CODESYS's own online help (canonical). Fetched many pages
  directly (HTTP 200). These are the primary sources.
- `help.plc.abb.com/AB270_en/` = ABB mirror of the same CODESYS CHM (same prose). Fetched
  the FBD programming page directly; used as corroboration.
- The **real rendering** evidence is four PNG figures on the Safety FBD page
  (`sil3_fbd_editor.html`) — those are actual CODESYS V3 FBD editor renders, kept at
  `.research-codesys/fig22..fig25`. **Important:** figures 22–25 come from the **Safety
  extension** editor. Figure 22's yellow fill/wires are the Safety "safe data flow" coloring
  (see §6), NOT standard FBD coloring. Figures 24/25 show the standard look (pale-blue box,
  black wires, white canvas).
- `web_fetch` was intermittently rate-limited (429) on codesys.com and DNS-flaky on the CDN;
  a handful of pages 403/404'd on retry. I did not read every element page; gaps are listed
  in §8.

---

## 1. The box: FB vs function vs conversion

All three are drawn as **one shape — a rounded-look rectangle (actually a plain rectangle
with a thin 1px black border)**. The distinction is **what text sits in the box and whether
there is a name above it.**

**Function block (e.g. TON, R_TRIG) — the "call box":**
- **Type name** (e.g. `TON`) — **inside** the box, in the **top band** of the box,
  centered horizontally. (Measured: `fig23_added_pou.png` shows the name `???` in the top
  area inside the box; the CFC Box page states "the input field ??? is also displayed
  **above** the function block symbol" for the **instance name**.)
- **Instance name** (e.g. `tmrOn`) — **above the box**, as a separate text field, centered
  over the top edge. This is **required only for a function block** (the docs repeat: "the
  instance name above the box, which is required in the case of a function block").
- Source: [Programming in FBD](https://content.helpme-codesys.com/en/CODESYS%20LD%20FBD/_cds_programming_in_fbd.html);
  [CFC Element: Box](https://content.helpme-codesys.com/en/CODESYS%20CFC/_cds_cfc_element_box.html).

**Function (e.g. ADD, AND, OR, MUL) — the "operator box":**
- **One centered name** (e.g. `AND`) inside the box, in the **top band**. **No instance
  name above** (a function has no instance). (Measured: `fig22_safe_and.png`,
  `fig24_input_var2.png`, `fig25_deleted.png` all show `AND` centered in the top band, no
  name above the box.)
- For logic operators CODESYS also draws the **IEC symbol glyph** below the name, centered:
  `AND` → `&`, `OR` → `≥1` (in the standard IEC 81346 form), `XOR` → `=1`. (Measured: a
  large dark-blue `&` under the `AND` label in all three figures. **Inferred:** the exact
  glyphs for OR/XOR from IEC 81346, not individually verified here.)

**Conversion (e.g. REAL_TO_INT, INT_TO_REAL):**
- Drawn **identically to a function box**: one centered type name in the top band, **no
  instance name above**. (Inferred from the fact that the docs treat conversions as
  functions/operators; the box page: "A box and its call can represent … IEC functions,
  library function blocks, or operators." No separate rendering rule found.)

**Optional icon:** "If the box also provides an image file, then the box icon is displayed
inside the box" (FBD/LD/IL Element: Box). This is an opt-in library feature, not the default.

### Header band vs centered
The type name is **centered horizontally in the top portion of the box** (a visual "header
band" area), with the **optional glyph/symbol centered below it in the middle**. It is not a
full-height side or a filled band — the box is a single flat rectangle and the name simply
sits in the top region. (Measured from fig22/24/25.)

---

## 2. Pins and where the connected variable name goes

**Pin geometry (measured from fig23/24/25):**
- Input pins: **small rectangular "stub" tabs** protruding from the **left edge**, one per
  input, vertically stacked and evenly spaced. In the unpopulated insert state each stub
  shows the pin's `???` placeholder. (fig23 shows two left stubs, each a small rectangle
  attached to the box's left edge.)
- Output pins: a **short wire/line extends from the middle of the right edge** (single
  output) — a short horizontal stub. Multiple outputs stack on the right edge like the
  inputs.
- So: **yes, there are little pin stubs** on both edges. Inputs on the left, outputs on the
  right.

**Where the connected variable name sits (the key CODESYS convention):**
The connected value/variable name does **not** go on the wire as free text, and it is **not**
a big box at the diagram edge. It sits **immediately outside the pin stub**, flush against the
box edge, as **a small rectangular text field with a thin border** (white fill):
- `fig24_input_var2.png`: the input variable **`Var2`** is in a **small bordered rectangle
  on the wire, directly left of the box's left edge**.
- `fig25_deleted.png`: inputs **`Var1`** and **`???`** are the variable names placed
  **directly to the left of the pin stubs**.
- For outputs, the variable name sits in the same kind of small bordered field just **right**
  of the right edge (fig22 shows `bVarOut` in a box to the right of the output wire).

> Inference: the "input element" / "output element" (the standalone edge element you insert
> from the ToolBox) **is** this small bordered field at the network edge. The docs describe
> the FBD Input element as "a variable or a constant" replaced from `???` — i.e. the field
> that holds the name. So: **variable name = small bordered text box at the pin**, whether
> it's the full edge element or the in-line stub label.

**Pin name vs variable name:** The rendered figures do **not** show a separate "IN:" / "PT:"
pin-name label on the wire in the simple operator case — the **variable/constant name is the
label shown next to the pin**. (For FBs the pin names like `IN`, `PT`, `Q` are part of the
box interface but in the default compact rendering the displayed text at each pin is the
connected variable. **Inferred** from the figures; the docs don't show a separate pin-name
glyph in these renders.)

Source: [FBD/LD/IL Element: Box](https://content.helpme-codesys.com/en/CODESYS%20LD%20FBD/_cds_fbd_ld_il_element_box.html),
[FBD/LD/IL Element: Input](https://content.helpme-codesys.com/en/CODESYS%20LD%20FBD/_cds_fbd_ld_il_element_input.html),
figures 22/24/25.

---

## 3. EN / ENO — the enable chain

**Semantics (measured, quoted):**
- "The command adds a Boolean input **EN** (Enable) and a Boolean output **ENO** (Enable Out)
  to the selected box." — [Command: EN/ENO (CFC)](https://content.helpme-codesys.com/en/CODESYS%20CFC/_cds_cmd_cfc_en_eno.html)
- "When the `EN` input has the value `FALSE` at the time of the POU call, the operations
  defined in the POU are **not executed**. Otherwise, these operations are executed when `EN`
  is `TRUE`. The **ENO output has the same value as the EN input**." —
  [Box with EN/ENO element](https://content.helpme-codesys.com/en/CODESYS%20LD%20FBD/_cds_fbd_ld_il_element_box_en_eno.html)
- FBD command to add it: **Ctrl + Shift + E** ("Insert Box with EN/ENO"). —
  [Command: Insert Box with EN/ENO](https://content.helpme-codesys.com/en/CODESYS%20LD%20FBD/_cds_cmd_fbd_ld_il_insert_box_with_en_eno.html)

**How it's drawn (measured from the box-with-ENO icons):**
- **EN pin: top-left** — on the **left edge, in the upper half** (above the normal data
  inputs). **ENO pin: top-right** — on the **right edge, in the upper half** (above the
  normal data outputs). They sit at the **top corners/upper region** of the side edges, one
  per side, mirroring each other at the same height. (Measured: `icon_insert_box_eneno.png`
  and `box_eneno_symbol.png` both show EN upper-left, ENO upper-right.)
- EN/ENO are **plain thin pins/wires, same style as data pins** (thin black/gray line) in the
  base editor. **I could NOT confirm a dashed/dotted EN→ENO line in the standard FBD editor
  from any source I read.** The "dashed enable wire" you mentioned is a known *TIA Portal /
  generic IEC* convention and is widely used, but I found no CODESYS help sentence stating
  the FBD editor draws EN→ENO dashed.
  > What would settle this: a screenshot of a CODESYS V3 FBD POU with a TON box and its
  > EN/ENO chain, or the "Editor" page
  > ([_cds_edt_fbd_ld_il_editor.html](https://content.helpme-codesys.com/en/CODESYS%20LD%20FBD/_cds_edt_fbd_ld_il_editor.html)),
  > which 403'd on retry. **Recommendation for your spec:** draw the enable chain as a
  > **dashed** line (matches the mainstream IEC look and what the user expects); it is a
  > defensible convention even if I can't quote CODESYS for it.

Source: three EN/ENO pages above + the two EN/ENO box icons.

---

## 4. Wires

- **Orthogonal, pin-to-pin.** Connections are straight line segments between a pin stub and
  the next pin stub (or edge field). In simple left→right flow they are **horizontal**; the
  branch/subnetwork element splits a line and produces **right-angle (orthogonal) routing**.
  (Measured: fig24/25 show short straight horizontal black wires; the Branch element splits
  the processing line at a box output.)
- **Color (base editor): black / very dark gray**, thin (≈1px). (Measured: fig24/25 wires
  are "thin, solid, dark gray/black horizontal lines.")
- **No color-coding by data type** in the base editor — data wires are uniformly dark.
  (Inferred: I saw no per-type wire colors in any render; the only colored wires were the
  Safety-extension yellow, §6.)
- **Constants / tags:** a **constant** (literal) is drawn as **a small bordered box at the
  pin** containing the literal value — same bordered-field treatment as a variable name
  (e.g. `TRUE`, a number). (Measured: fig22 shows literal `TRUE` in a small box on the input
  wire.) A **tag/variable** is drawn as a small bordered box with the variable name.
  So in CODESYS, **both constants and tags are little bordered text boxes on the wire at the
  pin**, distinguished only by their content.
- **Insertion affordances** (not rendered in a static diagram but part of the look while
  editing): insertion positions show as **gray diamonds** (hover) / **green** (drop target);
  newly added networks get a **yellow background**, the left network-number area a **red
  background**. (Measured: Programming in FBD page.)

Source: [Programming in FBD](https://content.helpme-codesys.com/en/CODESYS%20LD%20FBD/_cds_programming_in_fbd.html),
figures 22/24/25, [Branch element](https://content.helpme-codesys.com/en/CODESYS%20LD%20FBD/_cds_fbd_ld_il_element_branch.html).

---

## 5. Ladder (LD) rendering

All LD elements measured from the CODESYS "Symbol in the editor" PNGs on the LD element pages
(`contact_editor.png`, `contact_negated.png`, `coil_editor.png`, `coil_negated.png`,
`coil_set.png`, `coil_reset.png`). They are small (≈24×20px) but structurally unambiguous.
Colors in those icons are a dark navy/blue on white — the icons are symbolic; the in-editor
wire/equipment color is black/gray (consistent with FBD). I report **shape** as the
replicable convention and **label placement** from the docs.

- **NO contact (make):** **two short vertical parallel bars** (the "open switch"), a small
  air gap between them, horizontal wiring entering left / leaving right. Line thickness
  ~2px, solid. (Measured: `contact_editor.png`.)
- **NC contact (break):** the same **two vertical bars** with a **diagonal slash through the
  air gap** (negation mark). (Measured: `contact_negated.png`.)
- **Coil (output):** a **circle/ellipse** (drawn as two arcs `(` `)`) centered on the rung
  wire, solid, ~2px. (Measured: `coil_editor.png`.)
- **Negated coil:** the circle with a **diagonal slash**. (Measured: `coil_negated.png`.)
- **Set coil:** the coil **with a centered "S"** inside. (Measured: `coil_set.png` = `( S )`.)
- **Reset coil:** the coil **with a centered "R"** inside. (Measured: `coil_reset.png` = `( R )`.)

**Bit address / variable label placement (measured from the docs):**
- **Contact:** the variable name goes **directly ABOVE the contact** — the contact page:
  "replace the (`???`) placeholder **above the contact** with the name of a Boolean
  variable." (Source: [LD Element: Contact](https://content.helpme-codesys.com/en/CODESYS%20LD%20FBD/_cds_ld_element_contact.html).)
- **Coil:** the variable name goes **directly ABOVE the coil circle** (same convention; the
  coil page does not state it as explicitly but the editor uses the top-of-element label
  placement for all LD elements). (Inferred placement from the contact convention + editor
  behavior; the coil page confirms only semantics.)
- **Negation is shown on the symbol itself** (slash), not via a `NOT` prefix on the label.
- **Set/Reset:** assigned via the **FBD/LD/IL → Set/Reset** command or inserted as Set Coil /
  Reset Coil from the ToolBox (the `S`/`R` letters are the visual markers). (Source:
  [LD Element: Coil](https://content.helpme-codesys.com/en/CODESYS%20LD%20FBD/_cds_ld_element_coil.html),
  [Command: Set/Reset](https://content.helpme-codesys.com/en/CODESYS%20Ladder/_ld_cmd_set_reset_set.html).)
- **Rungs & rails:** LD auto-creates connections on insert; a rung is a horizontal wire with
  the two vertical power rails at left/right. Multiple contacts go in **series** (same rung)
  or **parallel** (branches). (Measured: [LD editor](https://help.plc.abb.com/AB270_en/_cds_editor_general_LD_editor.html),
  [LD Element: Contact].)

---

## 6. Color scheme (FBD/LD)

**Base editor = LIGHT theme, WHITE canvas.** (Measured: every render — fig22/23/24/25 — has a
white FBD/LD canvas.)

| Element | Color | Evidence |
|---|---|---|
| Canvas background | **White** | all figures |
| Box (FB / function / operator) fill | **Very pale blue / lavender** (≈ #E6EAF5 / #DCE3F0 range) | fig23/24/25: "pale light-blue / lavender fill, thin dark-gray outline" |
| Box border | **Thin black / dark gray (~1px)** | fig23/24/25 |
| Wires (data) | **Black / dark gray, thin** | fig24/25 |
| Variable/constant field fill | **White**, thin black border | fig22/24/25 |
| Network number gutter (left) | **Red** background behind the network number | Programming in FBD page |
| Newly added network (edit state) | **Yellow** background | Programming in FBD page |
| Text (names) | **Black**, sans-serif | figures |

**Safety-extension overrides (do NOT copy these for standard FBD):** in the Safety FBD
editor, safe-data-flow is highlighted **yellow** — "Literals are highlighted in yellow.
`SAFExxx` variables are highlighted in yellow. The data flow of `SAFE` values … is
represented by **thick yellow lines**. Function blocks are displayed in yellow … Operator
call boxes are filled in with yellow…" That is why **fig22 is yellow** (it is the Safety
extension's `AND` example, not the standard editor). (Measured: [Safety FBD Editor](https://content.helpme-codesys.com/en/CODESYS%20Safety%20Extension/sil3_fbd_editor.html).)

**Change markings (edit state, not base color):** added = green, changed = red, deleted =
blue. (Measured: same Safety page, "Change markings in the FBD editor.")

**So for your base renderer:** white canvas, pale-blue-filled rectangles with thin dark
borders, thin black wires, white-bordered variable fields, black text. No per-type color
coding.

---

## 7. Grid & default sizes

- **No visible grid** in any CODESYS FBD render I read (fig24/25: "No grid is visible").
  **Inferred:** CODESYS FBD is a **free-position** editor (drag from ToolBox; elements snap
  to insertion points, not a visible grid). I found **no documented grid spacing and no
  documented default block pixel size** in the help pages I read.
- What IS fixed by the rendering: box width is proportional to content (a 1-output
  operator is a compact square; a TON with EN/ENO + PT/Q is taller). Pin stubs are small
  fixed-size tabs. Wire thickness ≈1px, contact/coil strokes ≈2px (from the icons).
  > **Unresolved:** exact grid pitch and default box W×H. What would settle it: the
  > "Editor" options page
  > ([FBD, LD and IL options / _cds_edt_fbd_ld_il_editor.html](https://content.helpme-codesys.com/en/CODESYS%20LD%20FBD/_cds_edt_fbd_ld_il_editor.html))
  > or a zoomed CODESYS screenshot with the magnifier. **Recommendation:** no grid;
  > default operator box ≈ a small square (e.g. ~80×60 px at 100%), FB box taller to fit
  > EN/ENO + instance name, pin stubs ~8px, 1px wires, 2px LD symbol strokes.

---

## 8. What I could NOT establish (gaps + how to close them)

1. **Dashed EN→ENO line in the base FBD editor** — could not confirm or deny from CODESYS
   text. Close: read the "Editor" options page or a TON-in-FBD screenshot. (Recommend dashed
   for the mainstream look regardless.)
2. **Exact box W×H, grid pitch, font** — not in the help text I read. Close: the FBD/LD/IL
   **Editor options** page or a zoomed screenshot.
3. **OR/XOR operator glyphs** — inferred from IEC 81346 (`≥1`, `=1`); only `&` for AND was
   directly measured. Close: render an OR box.
4. **Coil label placement** (above the circle) — inferred from the contact convention; the
   coil page confirms only semantics. Close: a rendered LD network screenshot.
5. A few element pages **403/404'd on retry** (output element, the Editor options page) —
   the metasearch backend was rate-limiting. The gaps above are exactly those pages.

---

## 9. Replication cheat-sheet (base editor, no safety coloring)

**FBD box (function / operator / conversion):**
- Plain rectangle, **1px dark border**, **pale-blue fill** (~#E6EAF5).
- Type name centered in the **top band**; for logic operators also the IEC glyph centered
  below it (`&`, `≥1`, `=1`).
- **FB only:** instance name in a separate field **above** the top edge, centered.
- **Inputs:** small rectangular stubs on the **left edge**, stacked; **outputs** on the
  **right edge** (middle for single output).
- Each pin's connected **variable/constant** in a **small white bordered box** flush outside
  the stub (left for in, right for out). No separate pin-name text in the default view.
- **EN/ENO:** optional; **EN = top-left, ENO = top-right** pins (upper half of each side).
  Draw the enable chain **dashed** (mainstream convention; not confirmed in CODESYS text).
- **Wires:** thin (1px) black, orthogonal, pin-to-pin. Constants and tags both shown as
  small bordered boxes at the pin. White canvas, no grid.

**LD:**
- **NO contact** = two vertical bars (gap). **NC** = two vertical bars + diagonal slash.
- **Coil** = circle. **Negated coil** = circle + slash. **Set** = circle + `S`. **Reset** =
  circle + `R`.
- Variable/bit label **above** the element (contact or coil).
- Thin black rung wire, vertical power rails at both ends, white canvas.
