# Schneider Electric .XEF File Format — Research Notes

**Scope.** What a `.xef` file is, what's inside it, how program text is represented, what
it contains vs. not, how to extract/inspect it, and how people simulate/replay Schneider
programs outside the real PLC.

**Method / evidence caveat (read first).** This was established via `web_search` **only**.
Per this deployment's research rules, a search result is a *pointer, not a source*: I get a
title, a URL, and a snippet somebody else wrote — I could **not** open any of the pages
(`web_fetch` is not registered here). So every claim below is tagged:

- **[documented]** — supported by a snippet from a named, high-standing source (Schneider
  `se.com` help/FAQ, eplan API docs, an official manual page). I am quoting the *snippet
  that surfaced in search*, not the full page; treat as strong-but-should-verify-when-you-can-open-it.
- **[community-reported]** — a blog, forum, or GitHub issue/repo description.
- **[inferred]** — my own reasoning from the above plus general knowledge of these formats.

Where I could **not** establish something, I say so explicitly rather than fill the gap.

---

## 1. What exactly is a `.xef` file? Is it a ZIP? What's inside?

**Name / role.** `.XEF` = **"Application Exchange File"**; `.ZEF` = **"Full Application
Exchange File."** These are two of the four file types Control Expert (Unity Pro) manages
for user applications/projects: `.STU` (working file, default save), `.STA` (archived
application), `.XEF`, `.ZEF`. [documented] (Schneider FAQ
[FAQ000259996](https://www.se.com/us/en/faqs/FAQ000259996/), mirrored at
[ref_D-SA-0025860](https://filedn.eu/l6oFb0aAwOAyRLLaUMiUmzz/CE_HELP/eng/ref/ref_D-SA-0025860.htm):
"Control Expert manages four types of files ... `*.STU` ... `*.STA`: Archived Application
File, `*.XEF`: Application Exchange File, `*.ZEF`: Full Application Exchange File").

**What's in an exchange file.** The eplan PLC XML-converter API describes the family by
content scope: XEF = *"complete information, i.e. hardware and variables"*; `.XHW` =
*"hardware configuration"*; `.XSY` = *"PLC variables"*; `.ZEF` = *"complete information,
i.e. hardware and variables, **COMPRESSED!**"* [documented] (eplan
[PlcDCXmlExchangerSchneider](https://www.eplan.help/en-us/infoportal/content/api/2022/PlcDCXmlExchangerSchneider.html),
also at
[.../api/2024/...](https://www.eplan.help/en-us/Infoportal/Content/api/2024/PlcDCXmlExchangerSchneider.html)
and
[.../api/2025/...](https://www.eplan.help/en-us/Infoportal/Content/api/2025/PlcDCXmlExchangerSchneider.html)).

**Is it a ZIP?** Yes — at least for the ZEF variant, concretely demonstrated:
- A blog reports the file starts with `0x50 0x4b 0x03 0x04`, "which also happens to be the
  same as a ZIP file. So I renamed it 'file.zip' and unzipped and there was my XML file
  called 'file.XEF'." [community-reported] (Paul Rickard,
  [Schneider: Unity Pro ZEF files](https://pauljohnrickard.com/2020/06/03/schneider-unity-pro-zef-files/)).
- Schneider's own FAQ instructs: export the application to a `.zef`, **rename the `.zef` to
  `.zip`**, open with Winzip/Windows Explorer, then **copy the `.xef` out** to a temp folder
  for Web Designer import. [documented] (Schneider FAQ
  [FA241538](https://www.se.com/us/en/faqs/FA241538/)).

**Is the internal content plain XML or compressed/encrypted?** **Plain XML** inside a ZIP
container, *not* encrypted. The ZEF is the *compressed* (ZIP) wrapper; the `.xef` member
inside it is an XML file. The same blog states the premise "much of the programs are stored
in XML format." [community-reported + documented (ZIP step)].

**Bottom line.** A `.zef`/`.xef` is (or contains) a ZIP archive whose payload is
**plain-text XML** describing the application. It is a container, not an opaque blob, and
nothing I found indicates encryption. [inferred: the "plain XML, no encryption" conclusion
rests on the Rickard post + Schneider's own rename-to-zip procedure; I did not personally
unzip one.]

> **Open item.** I could **not** establish from snippets whether the *bare `.xef`* (as opposed
> to the `.zef` wrapper) is itself a ZIP or a single XML document. The SE FAQ procedure implies
> a `.xef` is *produced by* extracting from a `.zef` (i.e. the `.xef` is the XML member), while
> the eplan API treats `.xef` as a first-class importable format on its own. Reconciling those
> two views would require opening an actual file — not possible here.

---

## 2. Internal structure: the XML files, what they describe, namespace convention

- **High-level content scope.** Per eplan, the "complete information" XEF bundles **hardware
  configuration + variables** (plus, by the file's purpose, the program). [documented]
- **Program language sections exist per language.** The official Control Expert **Reference
  manual** contains a section titled **"Description of SFC sections"** and the manual covers
  "programming languages, project structure, data types, file formats, configuration tools,
  memory organization." [documented] (manualzz mirror of the
  [Control Expert Reference manual](https://manualzz.com/doc/o/2mqm0c/schneider-electric-ecostruxure%E2%84%A2-control-expert-reference-...-description-of-sfc-sections);
  and the
  [Program Languages and Structure Reference Manual](https://www.se.com/us/en/download/document/35006144K01000/)
  whose scope is described as "supported controller platforms, programming languages, project
  structure, data types, **file formats**, configuration tools, memory organization, and
  operating modes").
- **Schema / DTD hint.** A third-party blog reports that "a **hidden folder in Control
  Expert's installation directory**" supplied the material that let an LLM parse/emit the
  XEF, and that community knowledge was "scattered fragments — someone's reverse-engineered
  snippet of the **variable block**, another person's partial description of the **SFC
  format**." [community-reported] (Fulminis,
  [llm-plc-programming-schneider-xef](https://fulminis.ro/en/blog/llm-plc-programming-schneider-xef.html)).
  This strongly implies the XML is schema/DTD-structured (something in the install dir
  defines the elements) — but I could **not** read the blog to learn the actual element/namespace
  names.

**What I could NOT establish (important).** I have **no snippet giving the concrete XML
element names, file names, or the namespace/URI string** (e.g. no `Program`, `Pou`, `Hardware`,
`IO`, `Config` tag list, and no `xmlns="..."` value). Any namespace convention I might guess
is **[inferred]** and should be treated as unverified. The structure is *known to be XML with
language-specific sections and a hardware/variables block*, but the literal element names and
namespace are **not** in my evidence. Do not ship a parser built on guessed tags.

---

## 3. Programming languages, and how program code is represented in the XEF

- **Languages supported.** IEC 61131-3 ST, LD, FBD, SFC (the standard four), plus the
  platform's own blocks; the SE "Program Languages and Structure" manual is the
  authoritative list. [documented — languages exist; the exact per-platform matrix not in my
  snippets.]
- **ST is stored as readable text.** The community report is consistent with this: the whole
  "extract code for Git" use case (see §5, `xef_extractor`) and the "much of the programs are
  stored in XML format" statement imply ST source is human-readable text embedded in the XML
  (typically an element's text node), not bytecode. [community-reported] (Rickard,
  `RomanVerzun/xef_extractor`). I did **not** capture an actual ST-in-XML sample.
- **LD / FBD / SFC.** Stored as **graphical network / section definitions**, not as text:
  the reference manual's "Description of SFC sections" and the Fulminis mention of a
  "partial description of the **SFC format**" indicate SFC (and by analogy LD/FBD) are
  captured as structured section/network definitions. [community-reported + documented
  (manual has the sections)]. Exact representation (e.g. node/edge lists, coordinates) is
  **not** in my evidence.

**Bottom line.** ST = readable text in XML [community-reported]; LD/FBD/SFC = structured
network/section definitions [inferred from the manual's section names + Fulminis]. I could
**not** verify any of this by reading an actual XEF.

---

## 4. What an XEF does and does NOT contain

- **Hardware config (CPU type, I/O modules):** **Yes, included** in the "complete
  information" XEF (and dedicated `.XHW` = hardware configuration). [documented] (eplan API).
- **Variables / IO mapping:** **Yes** — `.XSY` = PLC variables; the XEF bundles variables.
  [documented] (eplan API). The presence of a "variable block" the community reverse-
  engineered is corroborating. [community-reported]
- **Program text:** **Yes** (see §3). [community-reported]
- **Program scan time / scan configuration:** **Could not establish.** The reference-manual
  scope mentions "operating modes" and "memory organization," which *may* include scan
  timing, but no snippet confirms scan time is in the XEF. **Treat as unverified.**
- **Memory layout:** The manual scope explicitly covers "memory organization," so the
  format has a memory-organization concept, but whether the *file* carries a memory-map
  table is **not** in my snippets. **Unverified.**

**Bottom line.** Confirmed present: hardware (CPU + I/O) config, variables/IO mapping,
program text (ST readable; LD/FBD/SFC as network defs). Unconfirmed in my evidence: scan
time and an explicit memory-layout table.

---

## 5. How to extract/inspect an XEF — known tools

**Schneider's own path (documented).**
- Export from Control Expert (right-click project → Export, or File → Export) to produce a
  `.zef`/`.xef`; for Web Designer import, **rename the `.zef` to `.zip`**, open in Winzip/
  Explorer, and copy the `.xef` member out. [documented] (SE FAQ
  [FA241538](https://www.se.com/us/en/faqs/FA241538/),
  [plctalk thread on XEF variable export](https://www.plctalk.net/forums/threads/unity-pro-variables-import.78322/)).
- Import: "Importing from an XEF format involves to re-generate the project." [documented]
  ([filedn.eu CE_HELP install](https://filedn.eu/l6oFb0aAwOAyRLLaUMiUmzz/CE_HELP/eng/install/install_D-SA-0025805.htm)).

**Third-party / open-source (GitHub).**
| Repo | Language | What it does | Evidence |
|---|---|---|---|
| [RomanVerzun/xef_extractor](https://github.com/RomanVerzun/xef_extractor) | **not stated** in snippet (Ukrainian desc) | "XEF Code Extractor — extracts code from Unity Pro/Control Expert XEF files for **version control in Git**." i.e. pulls readable program code out of XEFs. [community-reported] |
| [eplan `PlcDCXmlExchangerSchneider`](https://www.eplan.help/en-us/infoportal/content/api/2022/PlcDCXmlExchangerSchneider.html) | .NET (eplan API) | **Official import/export** for XEF/XHW/XSY/ZEF between eplan and Unity Pro. Not open-source; documents the format family. [documented] |
| [61131/echidna](https://github.com/61131/echidna) | (snippet doesn't state; VM-based) | "Compiler and virtual machine run-time for IEC 61131-3 languages" — a PLC you can run ST/etc. on. Not XEF-specific (see §6). [community-reported] |
| [LaBackDoor/iec_st_compiler](https://github.com/LaBackDoor/iec_st_compiler) | **Python** | "parses IEC 61131-3 Structured Text (ST) source files and outputs a corresponding AST in XML." Useful for *consuming* extracted ST. [community-reported] |
| [amal029/st](https://github.com/amal029/st) | **not stated** in snippet | "IEC-61131-3 Structured text parser." [community-reported] |
| [Isaac-W/KinectXEFTools](https://github.com/Isaac-W/KinectXEFTools) | (C#, from desc) | **NOT Schneider** — parses *KinectStudio* XEF files (a totally different format). Listed here to prevent confusion. [community-reported] |

**Named but I could NOT verify:** The task mentioned a **"UnityProXef" Python library** and
**"xef-tools"** — **neither surfaced in any search result I ran**, so I cannot confirm they
exist or their languages/features. If they exist, they did not rank in this search. (I would
not report them as real.)

**Note on `peteral/softplc`** ([link](https://github.com/peteral/softplc)): "PLC simulation
for load/stress testing of SCADA systems ... originally planned to make a simulation of
Siemens Simatic PLC ... decided to make a generic PLC." That is a **generic** Modbus/protocol
PLC simulator, **not** a Schneider XEF loader. [community-reported]

**Bottom line.** The clearest, purpose-built open tool is `RomanVerzun/xef_extractor`
(extract program code for Git). Schneider's own procedure (export → rename to zip → unzip →
XML) is the documented manual path. eplan's converter is a documented non-open .NET bridge.

---

## 6. Simulating / replaying Schneider programs outside the real PLC

- **Schneider's built-in PLC simulator (the practical answer).** Control Expert ships a
  **PLC simulator** that "can be used to test a Control Expert project during development,"
  with a Control Panel; the SE FAQ specifically covers simulating an **M580** (and "Modicon
  M580 Safety CPU"). This is the intended offline path and it operates on the project/XEF
  data. [documented] (SE FAQ
  [FAQ000219867](https://www.se.com/us/en/faqs/FAQ000219867/);
  [filedn.eu CE_HELP sim](https://filedn.eu/l6oFb0aAwOAyRLLaUMiUmzz/CE_HELP/eng/sim/sim_D-SE-0081442.htm)).
- **No open-source Modicon/M580 simulator** that *loads an XEF and runs its ST* surfaced in
  any search. The closest general-purpose ST runtimes are language-level, not XEF-aware:
  - [61131/echidna](https://github.com/61131/echidna) — compiler + VM for IEC 61131-3
    languages (ST/IL, etc.), aimed at building a soft-PLC for automation/IoT. [community-reported]
  - **Python:** [LaBackDoor/iec_st_compiler](https://github.com/LaBackDoor/iec_st_compiler)
    (ST → XML AST). [community-reported]
  - **ST→XML for codegen:** [fdik.org/iec2xml](http://fdik.org/iec2xml) — "IEC 61131-3
    Structured Text to XML Compiler ... used ... to generate HMI code out of the IEC source."
    [community-reported]
  - The well-known **C# `SoftPLC`/`SoftMotion`** ST toolchain (SoftPLC is a .NET IEC
    61131-3/ST compiler/interpreter) is widely referenced, but I **could not confirm a
    current GitHub URL from my snippets**, so I list it as **[inferred/likely-real, unverified-
    here]** rather than a citation.
- **The "LLM-as-PLC-programmer" approach.** The Fulminis blog describes using a **hidden
  folder in Control Expert's install dir** to give an LLM enough of the XEF schema to parse
  and **generate** Schneider programs — i.e. people are already extracting the XML and
  working with it programmatically (generate → import back through Control Expert / its
  simulator). This is the most relevant "replay outside the PLC" data point I found.
  [community-reported]
- **Academic IEC 61131-3 ST work (for compile/interpret):**
  - [arXiv 2410.22159](https://arxiv.org/html/2410.22159v3) — "Training LLMs for Generating
    IEC 61131-3 Structured Text with Online [RL]." [documented (paper abstract)]
  - [comsis.org 076-0711](https://comsis.org/pdf.php?id=076-0711) — "A compiler of IEC 61131-3
    Structured Text language ... ST programs are translated into ... universal executable
    code" (CPDev system). [documented (paper abstract)]
  - [freshcode.club IEC61131-3 VM](http://freshcode.club/projects/iec61131-3_vm) — "compiler
    for IEC 61131-3 textual languages ... byte code ... small virtual machine ... initially"
    supporting IL/ST. [community-reported]

**Bottom line.** The *reliable, supported* way to run a Schneider program without hardware is
Control Expert's **own PLC simulator** (M580 confirmed). For *open-source, XEF-agnostic* ST
execution, the realistic route is: **extract the ST text from the XEF** (xef_extractor or
unzip-and-parse) → feed to a general IEC 61131-3 ST tool (echidna, iec_st_compiler, SoftPLC).
I found **no** open-source project that loads a raw XEF and executes it end-to-end.

---

## 7. License / practical notes

- **Proprietary, no public spec.** The XEF/ZEF format is **Schneider-proprietary**. There is
  **no public, Schneider-published schema/spec** in my evidence. What exists is (a) Schneider
  help describing the *file roles* (exchange/archive), (b) eplan's API describing *content
  scope*, and (c) community reverse-engineering (Rickard, Fulminis, xef_extractor). So the
  format is effectively **reverse-engineered**, with Schneider's docs only naming the
  containers. [inferred — from the absence of any published spec + the community posts above]
- **A schema artifact does exist, shipped by Schneider.** The Fulminis report that a
  "hidden folder in Control Expert's installation directory" contained the material used to
  parse/emit XEF implies Schneider ships some schema/DTD/resource file in the product. This is
  a practical extraction lead, but its legal status is unknown. [community-reported]
- **EULA.** Control Expert's EULA is found in
  `C:\Program Files (x86)\Schneider Electric\License Manager\EULA` (or via License Manager →
  Help). [documented] (SE FAQ [FAQ000277619](https://www.se.com/au/en/faqs/FAQ000277619/)).
  **I did not read the EULA text**, so I **cannot** state whether it restricts *parsing an
  exported file* or *extracting program code*. That is a real open legal question — the EULA
  (and the fact that program code is likely customer IP) would decide it. **Do not assume
  parsing is permitted without reading the current EULA.** [inferred — flag, don't assert]

**Practical note.** The export→zip→unzip path and eplan's converter are the *sanctioned*
integration routes; the open-source extractors operate on files you legitimately hold.

---

## All URLs found (with one-line notes)

**Schneider / official**
- [se.com FAQ000259996](https://www.se.com/us/en/faqs/FAQ000259996/) — the four file types .STU/.STA/.XEF/.ZEF, what each is. (XEF = Application Exchange File.)
- [se.com FA241538](https://www.se.com/us/en/faqs/FA241538/) — how to get a `.xef` for Web Designer import; **rename `.zef` to `.zip`, unzip, copy `.xef` out**.
- [se.com FAQ000219867](https://www.se.com/us/en/faqs/FAQ000219867/) — **how to use the built-in PLC simulator** (M580) offline.
- [se.com FAQ000277619](https://www.se.com/au/en/faqs/FAQ000277619/) — **where the Control Expert EULA lives** (License Manager dir).
- [se.com 35006144K01000](https://www.se.com/us/en/download/document/35006144K01000/) — **Control Expert "Program Languages and Structure" Reference Manual** (platforms, languages, project structure, data types, file formats, memory organization, operating modes).
- [filedn.eu ref_D-SA-0025860](https://filedn.eu/l6oFb0aAwOAyRLLaUMiUmzz/CE_HELP/eng/ref/ref_D-SA-0025860.htm) — help mirror: the four file types (STU/STA/XEF/ZEF).
- [filedn.eu install_D-SA-0025805](https://filedn.eu/l6oFb0aAwOAyRLLaUMiUmzz/CE_HELP/eng/install/install_D-SA-0025805.htm) — help mirror: restoring/importing `*.XEF` ("re-generate the project").
- [filedn.eu sim_D-SE-0081442](https://filedn.eu/l6oFb0aAwOAyRLLaUMiUmzz/CE_HELP/eng/sim/sim_D-SE-0081442.htm) — help mirror: PLC simulator for **Modicon M580 Safety CPU**.
- [se.com M580 HW User Guide EIO0000001578](https://www.se.com/us/en/download/document/EIO0000001578/) — M580 hardware user guide (platform context, not XEF format).

**eplan (documents the XEF/ZEF/XHW/XSY content scope)**
- [eplan PlcDCXmlExchangerSchneider (2022)](https://www.eplan.help/en-us/infoportal/content/api/2022/PlcDCXmlExchangerSchneider.html) — XEF = "complete info (hardware+variables)", XHW = hardware, XSY = variables, ZEF = same + COMPRESSED.
- [eplan ...2024](https://www.eplan.help/en-us/Infoportal/Content/api/2024/PlcDCXmlExchangerSchneider.html) / [2025](https://www.eplan.help/en-us/Infoportal/Content/api/2025/PlcDCXmlExchangerSchneider.html) — same, other years.
- [github binfen1 Eplan doc mirror](https://github.com/binfen1/Eplan_2026_IA_MCP_scripts/blob/main/Eplan_2026_RAG_mcp/Eplan_DOCS/Api/XML%20Converters/Category%20PLCXmlConverter/PlcDCXmlExchangerSchneider.md) / [iflow-mcp mirror](https://github.com/iflow-mcp/covagashi-eplan_2026_ia_mcp_scripts/blob/main/Eplan_DOCS/API%20Reference/XML%20Converters/Category%20PLCXmlConverter/PlcDCXmlExchangerSchneider.md) — GitHub copies of the eplan doc.

**Community / reverse-engineering**
- [pauljohnrickard.com — Schneider: Unity Pro ZEF files](https://pauljohnrickard.com/2020/06/03/schneider-unity-pro-zef-files/) — **ZEF is a ZIP; unzip → XML file named `file.XEF`; "much of the programs are stored in XML format."**
- [fulminis.ro — llm-plc-programming-schneider-xef](https://fulminis.ro/en/blog/llm-plc-programming-schneider-xef.html) — "How We Decoded Schneider's XEF Format"; hidden install-dir folder as the schema; variable block + SFC format fragments; LLM→PLC.
- [plctalk — Unity Pro variables import](https://www.plctalk.net/forums/threads/unity-pro-variables-import.78322/) — "export the whole project as an XEF file."
- [control.com — Unity Export/Import Database](https://control.com/thread/1026212147) — Unity UDE/`.XSF` "is a standard XML file." (`.XSF` ≠ `.XEF`; adjacent.)
- [blog.ioprogrammo.info — Unity 3.0S→Pro 3.0 XL XEF compatibility](https://blog.ioprogrammo.info/en/case-study-successful-transition-from-unity-3-0s-to-unity-pro-3-0-xl/) — XEF versioning/compat case study.

**GitHub (tools)**
- [RomanVerzun/xef_extractor](https://github.com/RomanVerzun/xef_extractor) — **extract program code from XEFs for Git** (purpose-built; language not in snippet).
- [61131/echidna](https://github.com/61131/echidna) — IEC 61131-3 compiler + VM runtime (soft-PLC).
- [LaBackDoor/iec_st_compiler](https://github.com/LaBackDoor/iec_st_compiler) — Python ST → XML AST.
- [amal029/st](https://github.com/amal029/st) — IEC-61131-3 ST parser.
- [peteral/softplc](https://github.com/peteral/softplc) — **generic** PLC sim for SCADA/protocol testing (NOT Schneider).
- [Isaac-W/KinectXEFTools](https://github.com/Isaac-W/KinectXEFTools) — **NOT Schneider** (KinectStudio XEF, unrelated).
- [github.com/topics/iec61131-3](https://github.com/topics/iec61131-3) — topic index of IEC 61131-3 projects.

**ST compile/interpret / academic**
- [fdik.org/iec2xml](http://fdik.org/iec2xml) — IEC 61131-3 ST → XML compiler (HMI codegen use case).
- [arXiv 2410.22159](https://arxiv.org/html/2410.22159v3) — fine-tuning LLMs to generate IEC 61131-3 ST (online RL).
- [comsis.org 076-0711](https://comsis.org/pdf.php?id=076-0711) — paper: ST compiler in CPDev; ST → universal executable code.
- [freshcode.club IEC61131-3 VM](http://freshcode.club/projects/iec61131-3_vm) — compiler → bytecode → VM for IEC 61131-3 textual languages (IL/ST).
- [manualzz — Control Expert Reference manual (SFC sections)](https://manualzz.com/doc/o/2mqm0c/schneider-electric-ecostruxure%E2%84%A2-control-expert-reference-...-description-of-sfc-sections) — official manual page "Description of SFC sections."
- [manualzz — Control Expert manual collection](https://manualzz.com/manual/Schneider%2520Electric/EcoStruxure%25E2%2584%25A2%2520Control%2520Expert/) — ~100 Control Expert manuals (User/Reference/Installation).
- [manualsnet — EcoStruxure Control Engineering docs](https://manualsnet.com/schneider-electric/ecostruxure-control-engineering-documentation) — manual aggregator.
- [plcopen.org](https://www.plcopen.org/) — open-standards org behind IEC 61131-3.

---

## What I could NOT establish (do not treat as settled)

1. **Exact XML element names, file names, and the namespace/URI** for the XEF XML. No snippet
   gave them. Any tags/namespace I'd name are guesses.
2. **Whether a bare `.xef` (vs `.zef`) is itself a ZIP or a single XML document.** The SE FAQ
   and eplan API describe it two slightly different ways; reconciling needs an actual file.
3. **Whether scan time and an explicit memory-layout table are in the file.** Not in any
   snippet.
4. **The literal representation of LD/FBD/SFC networks** (node/edge lists, coordinates). Only
   the *existence* of SFC sections is documented.
5. **EULA position on parsing/extracting exported files.** The EULA's location is known, but
   its text was not read.
6. **Existence/language of a "UnityProXef" Python lib and "xef-tools."** Neither surfaced in
   search; unconfirmed.
7. **A current, verified GitHub URL for the C# `SoftPLC`/`SoftMotion` ST toolchain.** Likely
   real but not confirmed here.

**How to close these gaps:** open one real `.zef` (rename to `.zip`, unzip) and read the XML —
that alone settles items 1–4; read the EULA text (License Manager → Help) for item 5; and open
the [Program Languages and Structure manual](https://www.se.com/us/en/download/document/35006144K01000/)
for the authoritative language/platform matrix and memory/scan sections.
