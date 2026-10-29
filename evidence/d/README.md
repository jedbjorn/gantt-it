# Lane D evidence: final VM UI pass

Seat: W10C-Testing, 1920x1080 at 100 % scaling (96 DPI). Revit 2026.4 and 2027.3.

Binaries: the `GanttIt-Debug-R26` / `GanttIt-Debug-R27` artifacts of CI run 40794005484 (PR #6 head
`bd0f873`; the add-in reports `0.1.0-alpha+d44123a…`, the PR merge ref CI builds). Every file below
comes from that build, except `captures/bug1-delete-active-view-fails.png`, which shows the bug on
the first build of this branch (`a3777fd`, merge ref `e67e882`).

Captures are `sc vm capture` screenshots. Most are cropped to the Gantt-It window; Revit views are full screen.

## A8: Delete confirms, lists both sheets, removes the view, viewports and unused fill types

| File | Shows |
|---|---|
| `reports/selftest-2026-metric-legend-26-place-sheets.json` | Debug helper put "Project Schedule" on A101 and A102 (2 viewports, 4 fill types in use) |
| `captures/a8-1-delete-confirm-two-sheets.png` | Delete from the Home card, with the chart as Revit's **active view**: the dialog lists `A101 - GI A8 Sheet 1` and `A102 - GI A8 Sheet 2` |
| `captures/a8-2-after-delete.png` | After Delete: "Deleted", no cards, Revit switched to Legend 1 |
| `reports/selftest-2026-metric-legend-26-a8-check.json` | Read-only check: view gone, 0 of 2 viewports left, 0 Gantt-It fill types left (4 before), both sheets now empty |
| `captures/bug1-delete-active-view-fails.png` | The bug fixed in `a58b259`: on `a3777fd`, Delete of the active view failed ("ElementId cannot be deleted") |

## A10: No legend in the model (no-legend.rvt)

| File | Shows |
|---|---|
| `captures/a10-1-editor-before-generate.png` | The chart before Generate: "Phase Plan", 3 sections, one link, Schematic design 21 days |
| `captures/a10-2-no-legend-modal.png` | Generate opens the no-legend modal |
| `captures/a10-3-new-legend-by-hand.png` | After OK and closing Gantt-It with ×: View › Legends › Legend in Revit |
| `captures/a10-4-restored-draft.png` | Gantt-It reopened: the restored notice, and the same name, sections, dates, link and colours |
| `captures/a10-5-generated.png` | Generate succeeds: "Generated "Phase Plan"", the card on Home |

## A12: Lengths in project units, round-tripped through close and reopen

| Model | Field | Typed | Shown after Enter | Shown after reopening | Files |
|---|---|---|---|---|---|
| metric | Cell padding | `2.5` | `2.5` | `2.5` | `captures/a12-metric-settings-{typed,reopened}.png` |
| metric | Timeline width | `200.5` | `200.5` | `200.5` | `captures/a12-metric-editor-{typed,reopened}.png` |
| metric | Name column max | `45` | `45` | `45` | same |
| imperial | Cell padding | `1/16"` | `0' - 0 1/16"` | `0' - 0 1/16"` | `captures/a12-imperial-settings-{default,typed,reopened}.png` (default shows `0' - 0 15/256"`) |
| imperial | Timeline width | `7 1/2"` | `0' - 7 1/2"` | `0' - 7 1/2"` | `captures/a12-imperial-editor-{typed,reopened}.png` |
| imperial | Name column max | `2"` | `0' - 2"` | `0' - 2"` | same |

The metric model shows millimetres without a symbol, as DefaultMetric's own format does. Display
resolution is 0.1 mm (PR #4's `LengthFormat`), so a finer value such as `2.25` is shown as `2.3`
(seen on `a3777fd`, not captured).

## A16: Captures for the FnB's visual sign-off

| What | File |
|---|---|
| Self-Test fixture charts at 1:1 (`Document.ExportImage`, 2026 metric) | `charts/2026-metric-GI-{Days,Duration-only,Months,Multi-year,Wrapped}.png`, `charts/2026-metric-GI-Large-top.png` (first rows at full size), `charts/2026-metric-GI-Large-full-50pct.png` |
| A fixture chart on a sheet | `charts/2026-metric-sheet-A101-GI-Months.png` (export), `captures/2026-sheet-A101-view.png` (Revit view) |
| A fixture chart at 1:1 in Revit | `captures/2026-GI-Months-1to1-view.png` |
| Home with several cards | `captures/2026-home-several-cards.png` |
| Home, first run | `captures/2026-home-first-run.png` |
| Editor with the live preview | `captures/2026-editor-preview.png` |
| Settings | `captures/2026-settings-first-run.png` |
| Colour picker | `captures/2026-color-picker.png` |
| No-legend modal | `captures/a10-2-no-legend-modal.png` |
| Delete confirmation | `captures/a8-1-delete-confirm-two-sheets.png` |

Self-Test report for that run: `reports/selftest-2026-selftest-metric-26-1.json` (`overall: PASS`, 78 checks).

## End-to-end flow on metric-legend.rvt (Revit 2026)

`2026-home-first-run` → `2026-settings-first-run` → `2026-editor-preview` / `2026-color-picker` →
`2026-home-after-generate` → `a12-metric-editor-typed` → `2026-home-after-update` → `2026-open-view-ui-chart`.

§5.2 propagation, with Permitting (row 2) linked to Construction (row 3) by ticking row 2's "following" (row 3's "previous" ticked itself):

| File | Edit | Result |
|---|---|---|
| `captures/s52-1-duration-row2-10.png` | Row 2 Days 7 → 10 | Row 2 ends 11/14; row 3 moves to 11/15–11/21; row 4 unlinked, unchanged |
| `captures/s52-2-start-row3-shifts-chain.png` | Row 3 Start → 11/18 | The whole chain moves +3: row 2 11/8–11/17, row 3 11/18–11/24 |
| `captures/s52-3-end-row2-shifts-later.png` | Row 2 End → 11/19 | Row 2 is 12 days, start kept; row 3 moves to 11/20–11/26 |

Moving Construction below Handover removed its link and showed the inline notice (`2026-editor-preview.png`).

## Revit 2027 smoke (metric-legend.rvt, upgraded in memory, not saved)

`captures/2027-editor.png` (New, after the first-run Settings), `captures/2027-open-view-chart.png`
(Generate, then Edit with row 2 set to 14 days, then Update, then Open view), and
`captures/2027-after-delete.png` (Delete with the chart as the active view).
