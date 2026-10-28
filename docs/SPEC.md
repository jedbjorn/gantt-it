# Gantt-It — Revit Add-in Specification

Status: **Draft v0.2** · 2026-10-28 · Owner: jedbjorn · Vendor: Designs/OS

Gantt-It creates and edits Gantt charts inside a Revit model. Each chart is a
native Revit **Legend view** built from the project's own text styles, line
styles and filled regions. Because it is a legend, it can be placed on any
number of sheets. The chart's definition is stored in the model, so it can be
reopened, edited, regenerated or deleted from the add-in at any time.

---

## 1. Decisions (locked)

| # | Topic | Decision |
|---|-------|----------|
| D1 | Language / UI | C# + WPF, a standard Revit add-in (`IExternalApplication` + `IExternalCommand`, `.addin` manifest). No web views, no Dynamo/pyRevit. |
| D2 | Revit versions | **Revit 2025, 2026 and 2027.** One codebase using rst-c's build pattern: configurations `Debug/Release R25/R26/R27`, `net8.0-windows` for 2025/2026 and `net10.0-windows` for 2027. One build per Revit year. |
| D3 | Ribbon | One panel, **"Gantt-It"**, on the built-in **Add-Ins** tab, with one button that opens the add-in. |
| D4 | Output | One chart = one **Legend view**, made of `TextNote`s, detail lines (`DetailCurve`) and `FilledRegion`s. |
| D5 | Section links | **One shared link** between neighbours. Section N's "relates to following" and section N+1's "relates to previous" are the same link, so ticking either ticks both. See §5. |
| D6 | Time | **Calendar days.** End dates are inclusive. The header timescale is chosen automatically from the chart's span and can be overridden per chart. |
| D7 | Window model | **Modal** WPF window owned by the Revit main window. Generate, edit and delete run directly from button handlers inside the command's API context, so v1 needs no `ExternalEvent`. |
| D8 | Regeneration | Regenerating **keeps the legend view**, so sheet placements survive. It deletes only the elements Gantt-It created and redraws them. |
| D9 | Legend source | **Duplicate an existing legend, or ask for one.** No seed files, no copying between models, no downloads. If the project has no legend, a modal asks the user to create one (§7.1). |
| D10 | Vendor ID | **`DSOS`** (Designs/OS), the standard for all Designs/OS add-ins. Used in the `.addin` manifest and every Extensible Storage schema. (rst-c currently uses `RSTC` and can move over later; it stores no data under that ID.) |
| D11 | Units | Every length the user enters or sees (chart width, padding) is shown in the **project's length units**, formatted with `UnitFormatUtils`. |
| D12 | Cell sizing | Cells are sized from the **measured** size of the real Revit text, never from estimates. Lines are drawn on one shared grid, so they never cross text and always meet cleanly (§6). |
| D13 | Icons | Match rst-c's icon style: flat glyph on a light-grey tile (§8.2). |

---

## 2. User experience

### 2.1 Home (splash) screen
Opens when the user clicks **Gantt-It** on the Add-Ins tab.

- **New chart** button, always the first card.
- One **card per existing chart** in the active document, showing:
  - chart name (the legend view name)
  - overall date range and number of sections
  - a thumbnail drawn by the same renderer as the editor preview
  - "On N sheets", or "Not placed"
  - actions: **Edit**, **Open view** (closes the dialog and activates the legend), **Delete**
- **Settings** button (gear) that opens §2.3.
- If no project settings exist yet, the first **New** or **Generate** opens Settings before continuing.

### 2.2 Chart editor
Top to bottom:

1. **Chart properties**: name (must be unique among views), title text (defaults to the name), timescale (`Auto` / `Days` / `Weeks` / `Months` / `Quarters`), visible table columns (Start, End, Duration; the name column is always shown), timeline width on paper, and the maximum width of the name column. Lengths are in project units.
2. **Sections grid**. Each row has:
   - drag handle to reorder
   - **Name**
   - **Start** date (date picker), **End** date (date picker), **Duration** (days). Editing any one recalculates the others (§5.2).
   - **☐ Relates to previous** (disabled on the first row)
   - **☐ Relates to following** (disabled on the last row)
   - **Color** swatch, which opens the color picker (§2.4)
   - delete row
   - **Add section** appends a row. Its start is the day after the previous row's end, it lasts 7 days, and it gets the next palette color.
3. **Live preview** pinned to the bottom of the window. It redraws on every change, using the same layout engine as the Revit generator (§6), so it closely matches the output.
4. Buttons: **Generate** (or **Update** for an existing chart), **Cancel**, and **Delete** (existing charts only).

Validation: no blank names, no end before start, a chart name that collides with any view name is rejected. Generate is disabled while there are errors.

### 2.3 Settings (per project)
These are read live from the open model:

| Setting | Source in Revit | Used for |
|---|---|---|
| Title text style | `TextNoteType` | Chart title |
| Header text style | `TextNoteType` | Column headers and timescale labels |
| Body text style | `TextNoteType` | Section names and dates |
| Chart line style | Line styles (subcategories of `OST_Lines`, as `GraphicsStyle`) | Row separators and grid lines |
| Header line style | same | Header rules and the outer border |
| Date format | `yyyy-MM-dd`, `MM/dd/yyyy`, `dd/MM/yyyy`, `dd.MM.yyyy`, or a custom pattern | Date columns and labels |
| Cell padding | Length (project units), default ≈ 1.5 mm paper | Space between text and cell lines |

- Settings are stored **in the model** (§7.2). They reference elements by UniqueId and also store the name as a fallback.
- **"Use as my default for new projects"** saves the setting *names* to `%AppData%\GanttIt\defaults.json`. In a new model, Gantt-It matches styles by those names first.
- If a stored style no longer exists, it is flagged in red in Settings and Generate is blocked until a replacement is chosen.
- Changing settings does not alter existing charts on its own. Settings offers **"Regenerate all charts now"**.

### 2.4 Color picker
A custom WPF control. Nothing third-party is used, which avoids licensing and dependency conflicts.
- a default palette of 12 swatches, also used to auto-assign colors to new sections
- a recent-colors row
- hex input (`#RRGGBB`)
- a **More…** button that opens the Windows color dialog

---

## 3. Data model

```text
Chart
  Id            Guid       stable identity, also stored on the legend view
  Name          string     = legend view name
  Title         string
  Timescale     Auto|Days|Weeks|Months|Quarters
  Columns       flags      Start | End | Duration
  TimelineWidth double     paper length, stored in Revit internal units (ft); default ≈ 180 mm
  NameMaxWidth  double     paper length (ft); longer section names wrap; default ≈ 60 mm
  Sections      List<Section>   ordered
  Links         List<Link>
  SchemaVersion int

Section
  Id     Guid
  Name   string
  Start  DateOnly
  End    DateOnly          inclusive; Duration = End - Start + 1 (>= 1)
  Color  RGB

Link   (only between neighbouring sections)
  FromSectionId  Guid      the earlier section
  ToSectionId    Guid      the next section
  LagDays        int       To.Start = From.End + 1 + LagDays  (may be negative = overlap)
```

The UI shows each link as two checkboxes, "following" on the earlier row and
"previous" on the later row. Both are bound to the same `Link`.

The chart is serialized to JSON with `System.Text.Json`. `SchemaVersion`
allows migrating stored charts forward.

---

## 4. Revit output — anatomy of a generated chart

```
┌───────────────────────────────────────────────────────────────────────┐
│ TITLE (Title style)                                                    │
├──────────────┬──────────┬──────────┬─────┬────────────────────────────┤ ← header line style
│ Section      │ Start    │ End      │ Dur │  2026         │  2027       │ ← timescale tier 1
│              │          │          │     │ Oct │ Nov │ Dec │ Jan │ ...  │ ← timescale tier 2
├──────────────┼──────────┼──────────┼─────┼─────┼─────┼─────┼─────┼─────┤ ← header line style
│ Design       │ 26-10-01 │ 26-11-15 │ 46  │ ██████████        :     :   │
├──────────────┼──────────┼──────────┼─────┼─────┼─────┼─────┼─────┼─────┤ ← chart line style
│ Permitting   │ 26-11-16 │ 27-01-10 │ 56  │     :    ████████████████   │
└──────────────┴──────────┴──────────┴─────┴─────┴─────┴─────┴─────┴─────┘ ← header line style (border)
```

| Part | Revit element | Style |
|---|---|---|
| Title | `TextNote` | Title text style |
| Column and timescale headers | `TextNote` | Header text style |
| Section names, dates, durations | `TextNote` | Body text style |
| Header rules, outer border | `DetailCurve` (line) | Header line style |
| Row separators, vertical grid at timescale ticks | `DetailCurve` (line) | Chart line style |
| Section bars | `FilledRegion`, a solid-fill type per color, boundary set to `<Invisible lines>` | Gantt-It fill type (§7.3) |

---

## 5. Scheduling rules

### 5.1 Links
- Links exist only between **neighbouring** sections in list order. Neighbours that are linked form a **chain**.
- Ticking a link records the **current gap** as `LagDays`. Unticking it changes no dates.
- If a section is reordered, its links on both sides are removed. The user is told with an inline notice, never a silent change.

### 5.2 Edits and propagation (calendar days, end inclusive)
| User edits | Effect on the section | Effect on its chain |
|---|---|---|
| **Start** | Shifts the section; duration is kept | The **whole chain** shifts by the same number of days, both earlier and later sections |
| **End** | Duration changes | Later linked sections shift (each keeps its duration); earlier ones don't move |
| **Duration** | End changes | Same as End |
| Toggle link on | Records the current lag | No change |

Propagation enforces `To.Start = From.End + 1 + LagDays` across each chain.
It is deterministic, has no cycles (links follow list order), and is tested in
`GanttIt.Core` (§8).

### 5.3 Timescale (Auto)
The span is from the earliest start to the latest end.

| Span | Tier 1 / Tier 2 |
|---|---|
| ≤ 6 weeks | Weeks / Days |
| ≤ 9 months | Months / Weeks |
| ≤ 4 years | Years / Months |
| > 4 years | Years / Quarters |

The axis is padded out to whole tier-2 units. These thresholds are starting
values, to be tuned on real charts.

---

## 6. Layout engine

The goal is a chart whose lines stay clean: every cell is exactly as big as
its text needs, plus padding, and no line ever crosses text or overshoots
another line.

### 6.1 Measure, then lay out
Layout runs in two steps, and both the preview and Revit use the same code:

```
cells   = BuildCells(chart, settings)                // every text string + its style
sizes   = measurer.Measure(cells)                    // real width/height of each string
grid    = SolveGrid(cells, sizes, chart, settings)   // column x-positions, row y-positions
prims   = Emit(grid, chart)                          // Text | Line | FilledRect primitives
```

`ITextMeasurer` has two implementations:

| Measurer | Used by | How |
|---|---|---|
| `RevitTextMeasurer` | Generate / Update | Inside the generate transaction: create every `TextNote` at the origin, call `doc.Regenerate()`, and read each note's real extents from its bounding box in the legend view. The notes are then **moved into place**, not recreated. Revit's own text engine is the source of truth. |
| `WpfTextMeasurer` | Live preview | `FormattedText` with the style's font, size, bold/italic and width factor. Close to Revit, and calibrated against it in Phase 0 (V9). |

The output is therefore always measured. The preview may differ from Revit
by a fraction of a millimetre, but the generated chart never relies on an
estimate.

### 6.2 Grid rules
- **Columns** (Name, Start, End, Duration): width = widest measured text in
  that column, header included, + 2 × padding.
- **Name column**: capped at `NameMaxWidth`. A longer name gets
  `TextNote.Width = NameMaxWidth − 2 × padding`, so Revit wraps it, and the
  wrapped note is measured again.
- **Rows**: height = tallest measured cell in the row + 2 × padding. Rows with
  wrapped names grow, and everything below shifts down.
- **Header tiers**: each tier's height comes from its tallest label + 2 × padding.
- **Text anchoring**: every note uses horizontal alignment *Left* (*Right* for
  Duration) and is placed at `cell.left + padding` / `cell.top + padding`. Its
  measured box then fits inside the cell.
- **Timescale labels**: if a label doesn't fit its tick cell, try shorter
  formats in turn (`January` → `Jan` → `J`, `2026` → `'26`). If even the
  shortest doesn't fit, label every 2nd, then 4th, tick. Labels never overlap
  and never touch a line.
- **Timeline**: day width = `TimelineWidth / days in axis`. A bar runs from
  its start date's left edge to the right edge of its end date, inset
  vertically by padding inside its row.

### 6.3 Clean lines
- One **continuous line per grid line**: one curve for each row separator
  across the full width, one for each column divider and each timescale tick
  over its full height. No per-cell segments, so nothing doubles up.
- Every line endpoint is **snapped** to the grid's exact x/y values, so
  corners meet exactly with no gaps or overshoot.
- Hierarchy: the outer border and header rules use the **header line style**.
  Row separators and grid ticks use the **chart line style**. Where two
  styles would overlap the same segment, only the heavier header line is drawn.
- Lines shorter than Revit's short-curve tolerance (~0.8 mm at 1:1) can't
  occur, because cells are always at least text height + 2 × padding.
  Day-level ticks are dropped when a day is narrower than 1 mm.

### 6.4 Scale and units
- The generator sets the legend view to **1:1**, so 1 model unit equals 1
  paper unit and measured text sizes are paper sizes.
- Geometry is computed in Revit internal units (feet). The UI shows and
  accepts lengths in the **project's length units**
  (`doc.GetUnits()` + `UnitFormatUtils.Format` / `TryParse`).

---

## 7. Revit integration

### 7.1 Creating the legend view (key constraint)
The Revit API (2025/2026 included) **has no method to create a legend view
from scratch.** The standard workaround:

Gantt-It keeps this simple:

1. **A legend exists** (`View.ViewType == ViewType.Legend` and not a template):
   call `View.Duplicate(ViewDuplicateOption.Duplicate)` on it. That gives an
   empty legend; Gantt-It renames it, sets Scale = 1 and attaches the chart
   entity. Gantt-It's own chart legends can also serve as the source.
2. **No legend exists**: show a **modal** explaining that Revit's API can't
   create a legend from scratch, with the steps to make one (*View ›
   Legends › Legend*, any name and scale), and an **OK** button. The chart
   being edited is kept in the open editor, so after creating a legend the
   user just presses Generate again. Nothing is downloaded, opened or copied
   from another model.

### 7.2 Persistence (Extensible Storage)
| Schema | Attached to | Fields |
|---|---|---|
| `GanttIt.Chart` v1 (fixed GUID) | the chart's legend `View` | `ChartJson` (string), `GeneratedIds` (list of UniqueIds of the elements Gantt-It created), `AppVersion` |
| `GanttIt.Settings` v1 (fixed GUID) | one `DataStorage` element | style UniqueIds and names, date format, default palette |

- The schema read/write access level is `Vendor`, using vendor id **`DSOS`** (D10). It must match `<VendorId>` in `GanttIt.addin`.
- Finding charts: collect every `View` of type Legend that has a valid
  `GanttIt.Chart` entity.
- **Copies made in Revit**: if the user duplicates a chart legend in Revit and
  the entity comes along, two views share one `Chart.Id`. On load, the later
  one is treated as a copy and gets a new Id. Its stored `GeneratedIds` won't
  match its own elements, so it is regenerated on its first edit.

### 7.3 Filled region types
- There is one `FilledRegionType` per distinct color, named
  `Gantt-It Fill #RRGGBB`. It is duplicated from any existing type, then given
  the `<Solid fill>` drafting pattern as foreground, the color as foreground
  color, no background pattern, and `IsMasking = false`.
- Existing types are reused across charts. After a delete or regenerate,
  Gantt-It types that no element uses are deleted.

### 7.4 Generate / Update / Delete
Each action runs in one `Transaction` named `Gantt-It: <action> <name>`, so it is one Undo step.
- **Generate (new)**: create the legend (§7.1), draw the primitives, store the entity.
- **Update**: delete the elements listed in `GeneratedIds`, then redraw. Any
  annotation the **user added by hand stays**. Gantt-It elements the user
  edited by hand are **overwritten**, and the editor warns about this once
  per chart.
- **Delete**: confirm first. If the legend is placed on sheets, the dialog
  lists them, because deleting the view removes those viewports. Then delete
  the legend view and clean up unused fill types.
- **Failure handling**: catch and report the error, roll back the transaction,
  and never leave a half-drawn chart. Revit failure messages are pre-processed
  with an `IFailuresPreprocessor` so warnings don't appear as modal popups.

### 7.5 Worksharing
In workshared models, editing requires the legend view and the settings
`DataStorage` to be editable. If they are owned by someone else, the add-in
reports who owns them (`WorksharingUtils.GetCheckoutStatus`) and aborts
cleanly. v1 does no automatic borrowing beyond what Revit does implicitly.

---

## 8. Solution architecture

```
gantt-it/
  src/
    GanttIt.Core/            net8.0;net10.0  model, scheduling, grid solver, JSON. NO Revit, NO WPF.
    GanttIt.Revit/           $(RevitTargetFramework) (UseWPF)
      App.cs                 IExternalApplication: ribbon panel on Add-Ins tab
      Commands/OpenGanttIt.cs IExternalCommand: shows the modal window
      Revit/                 StyleReader, ChartStore (ES), LegendFactory, RevitTextMeasurer, ChartGenerator
      UI/                    WPF views + view-models (MVVM, plain INotifyPropertyChanged), WpfTextMeasurer
      Resources/icons/       gantt_32.png, gantt_16.png (§8.2)
      GanttIt.addin
  tests/
    GanttIt.Core.Tests/      xUnit. Scheduling and grid tests (fake measurer), run on any OS.
  Directory.Build.props      copied from rst-c's pattern (Configurations, RevitVersion, RevitTargetFramework)
  Directory.Packages.props   central package versions
  global.json                SDK pin, as in rst-c
  docs/SPEC.md
```

- **Build pattern**: same as rst-c. Configurations `Debug R25 … Release R27`
  set `RevitVersion` and `RevitTargetFramework`: net8 for 2025/2026, net10
  for 2027. The R27 build needs the .NET 10 SDK.
- **Revit API references**: the `Nice3point.Revit.Api.RevitAPI` /
  `RevitAPIUI` NuGet packages (reference-only, `ExcludeAssets=runtime`), with
  the version taken from `$(RevitVersion)` as in rst-c. The project builds
  without Revit installed, including on the Linux host with
  `EnableWindowsTargeting`.
- **Assembly metadata**: `Company = Designs/OS`, `Product = Gantt-It`, as in
  rst-c's `Directory.Build.props`. `GanttIt.addin` gets a fixed `ClientId`
  GUID (never changed once shipped), `VendorId = DSOS`, and
  `VendorDescription = Designs/OS`.
- **Dependencies**: as few as possible. The base class library and WPF only,
  so Revit 2026 dependency-isolation issues can't arise.
- **Deployment (dev)**: copy the build output to
  `%AppData%\Autodesk\Revit\Addins\<year>\GanttIt\` with `GanttIt.addin` next
  to the folder. Revit locks loaded DLLs, so redeploying means closing Revit.
- **Deployment (release)**: a per-user MSI like rst-c's (WiX), later. Not v1.

### 8.2 Icons
They match rst-c (`rst-c/src/RST.Engine/Assets/icons/`):
- PNG, **32×32** (large button) and **16×16** (small and tooltip), RGBA
- light-grey tile `#D9D9D9` with transparent corners
- a flat single-color glyph in rst-c blue `#4F8EF7`
- no outlines, gradients or text

The Gantt-It glyph is three horizontal bars, staggered left to right like a
Gantt schedule. Generate it with the same tile geometry as rst-c's pack
icons, so it sits naturally next to the RST panel on the Add-Ins tab.

### 8.1 Dev loop with W10C-Testing
1. Build on the Linux host (needs a .NET 8 SDK installed there) or on the VM.
2. Push the build output to the VM.
3. Launch Revit on the VM with a test model.
4. Drive and verify through the VM's MCP: screenshots, plus a debug-only
   **"Gantt-It Self-Test"** command. It generates a set of fixture charts into
   the open model and writes a JSON report (element counts, styles, bounding
   boxes) to `%TEMP%\GanttIt\selftest.json`, which can be pulled back and
   diffed.

---

## 9. Out of scope for v1
- Working days, holidays, multiple calendars
- Dependencies other than finish-to-start between neighbours (no arbitrary
  predecessors, no start-to-start)
- Milestones (zero-length diamonds), progress/percent-complete fill, a
  "today" line
- Nested sections or groups
- Importing from or exporting to MS Project, P6 or Excel
- Dragging bars in the preview (the data model supports it later)
- A modeless or dockable window
- Localization (English only, though dates follow the chosen format)

---

## 10. Phase 0 — verification spike (before feature work)

Run on W10C-Testing. Each item is a tiny throwaway command in a spike add-in.

| # | Verify | Pass criterion |
|---|---|---|
| V1 | Which Revit years (2025/2026/2027) are installed on the VM and licensed (not an expiring trial) | Versions recorded; the ones that are missing are noted |
| V2 | Build toolchain: .NET 8 (and 10 for R27) SDK on the VM, or a host build that loads in Revit | Hello-world command shows a `TaskDialog` from a "Gantt-It" panel on the Add-Ins tab |
| V3 | `View.Duplicate(Duplicate)` on a legend gives an empty legend, and `Scale = 1` is accepted | New, empty legend at 1:1 |
| V4 | `FilledRegion.Create` in a legend, with a duplicated solid-fill type in a custom color and an invisible-line boundary | Colored bar, no visible outline |
| V5 | `TextNote.Create` and `NewDetailCurve` in a legend using the chosen `TextNoteType` / `GraphicsStyle` | Correct styles on the elements |
| V6 | Extensible Storage (`DSOS` vendor) on a legend `View`: survives save, close and reopen; check what `Duplicate` does to the entity | Data reads back; duplication behavior recorded |
| V7 | Modal WPF window owned by Revit; a transaction run from a button handler | Element created while the dialog is open; Undo works |
| V8 | Delete a legend placed on 2 sheets | Viewports removed, no errors |
| V9 | **Text measurement**: create notes, `Regenerate`, read bounding boxes, then move the notes (`TextNote.Coord` or `MoveElement`). Compare with `WpfTextMeasurer` for 3 common fonts at 2–3 sizes, including width factor ≠ 1 and wrapped text | Revit box is tight and reliable (no hidden margin, or a known constant one); WPF is within ~0.5 mm |

---

## 11. Open questions

Resolved 2026-10-28: vendor ID (`DSOS`, D10), units (project units, D11),
legend source (duplicate or ask, D9), icons (rst-c style, D13), Revit
2025–2027 (D2). Worksharing is deferred, so v1 is tested on standalone
models only.

| # | Question | Default if unanswered |
|---|---|---|
| Q1 | Should settings live only in the model, or also have per-user defaults? | Both, as in §2.3 |
| Q2 | Should rst-c move from `RSTC` to `DSOS`? This is a separate change in the rst-c repo. | Leave rst-c as-is for now |
