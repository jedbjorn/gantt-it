# v1.1 evidence: VM pass on v1.1.0-rc.1

Seat: W10C-Testing, 1920x1080 at 100 % scaling. Revit 2026.4 and 2027 installed, no Revit 2025.
Snapshot `generic-multi-project`, reset at the start and at the end (the VM is shared with dos-arch).

Binaries:

- Installers: the release assets `GanttIt-1.0.0.msi` (ProductCode `{51947B27-…}`) and
  `GanttIt-1.1.0-rc.1.msi` (ProductCode `{A20D2DF2-4FAF-4593-9F03-A377DA7AFD18}`, ProductVersion 1.1.0,
  UpgradeCode `{644FCAD5-E631-4441-9A11-75F175565136}`), built from main `aa83f35`.
- Self-Test: the `GanttIt-Debug-R26` artifact of CI run 40890022441 (main `aa83f35`; the add-in
  reports `0.0.0-dev+aa83f351…`).

Accounts: the auto-logged-on user `public` is a member of Administrators with UAC on. Every
`msiexec` ran in the console session as `public` through a scheduled task with an Interactive
principal at **Limited** run level (the filtered, non-elevated token, as a double-click would).
The file and registry steps (dummy `Revit 2025\Revit.exe`, renaming the real `Revit.exe` files)
ran over the `sc vm exec` SSH channel, which runs as `public` **elevated** (High integrity).

Captures are `sc vm capture` screenshots (the mouse pointer is never in them). Logs in `logs/` are
`msiexec /l*v` logs trimmed to the relevant lines. The run was stopped early by the owner (see
"Not run" below).

## Summary

| Criterion | Result | Notes |
|---|---|---|
| V1 Detection, upgrade, uninstall (2026 + 2027) | PASS | 2027 panel load not re-checked after the upgrade (not run) |
| A1 Clean state, install v1.0.0 | PASS | |
| A2 Upgrade to rc.1 | PASS | |
| A3 Fallback; Repair adds 2025; `/fa` drops 2025 | PASS with a finding | `msiexec /fa` removes the files but leaves the HKCU marker; the UI's Repair mode (`/fecmus`) removes both. Docs fixed |
| A4 Revit removed, then uninstall | PASS | |
| A5 No Revit at all | PASS | |
| A6 Final install, panel in 2026 and 2027 | 2026 PASS, 2027 not run | |
| A17 Pick from screen | 2026 PASS, 2027 not run | |
| A19 Date fields | PASS (2026) | |
| A20 Delete fallback | PASS (2026) | restore-on-failure stays inferred, as spec #1 says |
| A23 Dark mode | 2026 PASS, 2027 not run | |
| A24 Languages | PASS (2026) | |
| **V4 Purge Self-Test** | **FAIL** | product bug: the purge never deletes a fill type (below) |
| V5 Doc screenshots | PASS | all 8 TODOs replaced; FnB's eye still needed |

## Product bug: the fill-type purge (§7.3) never deletes anything (V4 FAIL)

Debug R26 Self-Test phase 1 on a copy of `metric-legend.rvt`, Revit 2026.4:
`overall=FAIL, passed=78, failed=3`, all three failures are the §7.3 purge checks
(`reports/selftest-2026-selftest-26-1.json`).

Every unused Gantt-It fill type is kept with the reason
`deleting it would also delete 3 elements: <id+1> Element/no category, <id+2> GraphicsStyle/no category, <id+3> Element/no category`,
for example `Gantt-It Fill #0A0B0C: … 366104 Element, 366105 GraphicsStyle, 366106 Element`.
`FillTypes.KeepReason` (`src/GanttIt.Revit/Services/FillTypes.cs`) refuses any type whose
`GetDependentElements(null)` returns anything besides itself, but in Revit 2026.4 every
`FilledRegionType` has three intrinsic dependents of its own (two class-less `Element`s and a
`GraphicsStyle`, with the ids right after the type's). So `purgeDeleted` is always empty:

- "Generate purges the orphan and the unused default type": `orphanGone=false`, `defaultTypeGone=false`,
  `purgeDeleted=[]`.
- "a commit failed by the purge is retried once without it": `simulatedCommitFailureFired=false`,
  because the fault only fires after the purge deleted at least one type.
- "after the probes are removed by hand, deleting the charts purges every probe fill type":
  all six probe types left.

Repro: deploy `GanttIt-Debug-R26`, open a copy of `metric-legend.rvt`, run Self-Test phase 1
(`revit.ps1 -Action launch -Year 2026 -Model <copy> -Phase 1`). Or by hand in the Release build:
generate a chart, change one section's colour, Update; the old colour's `Gantt-It Fill #RRGGBB`
type stays in the model. Effect for users: unused Gantt-It fill types pile up; nothing is deleted
wrongly and no action fails. A likely fix is to ignore the type's intrinsic dependents (filter out
dependents that are not `FilledRegion`s or other real users) rather than refusing on any dependent.
2027 was not run (owner stopped the pass).

The USER-GUIDE text on fill cleanup (correction 1) describes the intended behaviour, which rc.1
does not deliver until this is fixed.

## V1 and section A: installer

| Step | Evidence | Result |
|---|---|---|
| A1 Clean state: no Gantt-It in `Addins\2025/2026/2027` (the snapshot had none; `Addins\2025` holds only another add-in, RST). v1.0.0 installs 16 files each into 2026 and 2027 | `logs/1-install-v1.0.0.txt` | PASS |
| A2 No elevation prompt: the Install button carries no UAC shield, the install runs without a prompt, the log says `MSI_LUA: Package is marked as LUA installation capable with no elevation required` and, for the old version's removal, `Nested installation UAC elevation tracks that of parent (is not elevated)` | `captures/v1-1-upgrade-license-no-shield.png`, `captures/v1-2-upgrade-finished.png`, `logs/2-upgrade-rc1.txt` | PASS |
| A2 One Apps entry: Settings › Apps filtered on "gantt" shows one entry, Gantt-It 1.1.0. The MSI API and the Uninstall keys list only `{A20D2DF2-…}` 1.1.0 | `captures/v1-3-apps-one-entry-1.1.0.png` | PASS |
| A2 16 files in 2026 and in 2027: the rc.1 File table has 48 rows, 16 per year (`GanttIt.addin`, 5 in `GanttIt\` (2 DLLs, 2 PDBs, deps.json), 4 icons, 6 fr/es/de satellites); the guest tree matched it file for file, version `1.1.0-rc.1`. `Addins\2025` got no Gantt-It file | listing in this README; `logs/2-upgrade-rc1.txt` | PASS |
| A2 Detection via the registry: `AppSearch: Property: REVIT2026FOUND, Signature: Revit2026ExeInInstallLocation` followed by `Adding REVIT2026FOUND … C:\Program Files\Autodesk\Revit 2026\Revit.exe` (same for 2027); 2025 not found by either search | `logs/2-upgrade-rc1.txt` | PASS |
| A3 Fallback: dummy `C:\Program Files\Autodesk\Revit 2025\Revit.exe`, no uninstall entry for 2025. Repair from the UI (Change/Repair/Remove page): `REVIT2025FOUND` is set by `Revit2025ExeInDefaultFolder`, 2025 gets 16 files and its HKCU marker | `captures/a3-1-repair-dialog.png`, `logs/3a-repair-add-2025.txt` | PASS |
| A3 Dummy exe deleted, folder kept, `msiexec /fa`: 2025's files and folders are removed (`Folders2025 … Action: Absent`), but `HKCU\Software\Designs/OS\Gantt-It\2025\Installed=1` **stays**: `/fa` (REINSTALLMODE=`a`) does not process per-user registry values, so RemoveRegistryValues does nothing and the value is orphaned (the component is unregistered) | `logs/3b-repair-fa-drop-2025.txt` | finding |
| A3 Same drop with the UI's repair mode (`msiexec /fecmus`, REINSTALLMODE `ecmus` as the Repair button uses): files, folders **and** the 2025 marker are removed (re-added first with the dummy back) | `logs/3e-repair-ecmus-drop-2025.txt` | PASS |
| A4 Dummy back, Repair (`/fecmus`): 2025 installed. Dummy deleted, `msiexec /x`: every year's files, `GanttIt` folders and all HKCU markers gone (the `Designs/OS` key itself is gone); only RST's files remain in `Addins\2025/2026` | `logs/4b-uninstall-2025-revit-gone.txt` | PASS |
| A5 Both real `Revit.exe` renamed, no dummy: a fresh install stops with "Gantt-It needs Revit 2025, 2026 or 2027, and none was found on this computer."; nothing installed | `captures/a5-1-no-revit-message.png`, `logs/5a-install-no-revit.txt` | PASS |
| A5 Install (Revit back), rename both away, uninstall: succeeds (`REMOVE=ALL`, no REVIT*FOUND), all files and markers gone. Both `Revit.exe` renamed back and the dummy folder deleted afterwards | `logs/5c-uninstall-no-revit.txt` | PASS |
| A6 Final rc.1 install (2026 + 2027, 16 files each). Revit 2026: "Always Load" prompt, the Gantt-It panel on Add-Ins, the Gantt-It window opens | `logs/6-final-install.txt`, `captures/a6-1-2026-always-load.png`, `captures/a6-2-2026-panel-and-window.png` | 2026 PASS |
| A6 Revit 2027 | | not run (owner accepted without retest) |

The final state was not kept: the owner stopped the pass, and the VM was reset to its snapshot.
A repair to put the Release files back over the Debug R26 deploy had returned 1603 just before;
it was not investigated, because the reset discards it (and `msiexec /famus` over files of a
different version is not a user path).

## A17: Pick from screen (Revit 2026)

The chart "North wing programme" open in Revit at 1:1. "Planning submission" set to the custom
colour `#1F6E8C` with Hex/Apply and updated; the bar in the Revit view samples exactly
`#1F6E8C` (and the palette bars `#4F8EF7`, `#F28E2B`, `#59A14F`, `#B07AA1`).

| File | Shows | Result |
|---|---|---|
| `captures/a17-0-2026-chart-in-revit-view.png` | the chart in the Revit view | |
| `captures/a17-1-2026-pick-loupe-over-bar.png` | Survey's colour window › eyedropper: Gantt-It steps aside, the loupe over the Planning bar reads `#1F6E8C` | |
| `captures/a17-2-2026-after-pick-hex-and-recent.png` | after the click: Survey's hex is `#1F6E8C` exactly, and it is in Recent | PASS |
| `captures/a17-3…`, `a17-4-2026-after-esc-unchanged.png` | eyedropper, loupe on the green bar, Esc: windows back, colour unchanged | PASS |
| (no capture) early Esc right after clicking the eyedropper | windows back, colour unchanged | PASS |
| `captures/a17-5-2026-after-double-click-pick.png`, `a17-6-2026-recent-after-second-pick.png` | double-click on the Tender bar: one pick, `#B07AA1`, added to Recent; the second click lands harmlessly on the editor | PASS |
| `captures/a17-7-2026-after-altf4-windows-back.png` | Alt+F4 during the pick: the pick ends, windows back, colour unchanged, Revit not closed | PASS |
| 2027 | | not run (owner accepted without retest) |

Mixed-DPI and multi-monitor mapping stay inferred (one display), as spec #1 says.

## A19: Date fields (Revit 2026)

| File | Shows | Result |
|---|---|---|
| `captures/a19-1-2026-ddMMyyyy-typed.png` | Date format `dd.MM.yyyy`; `14.12.2026` typed into row 1 Start; fields and preview in that format | |
| `captures/a19-2-2026-ddMMyyyy-reopened-roundtrip.png` | after Generate and Edit: `14.12.2026` | PASS |
| `captures/a19-3-2026-invalid-error-style-update-disabled.png` | `31.02.2027` typed: red outline, problem list "is not a date in this chart's format. Type it as dd.MM.yyyy…", Update greyed | PASS |
| `captures/a19-4…`, `a19-5-2026-scrolled-back-error-kept.png` | 12-section list scrolled to the bottom and back: the error state is kept | PASS |
| `captures/a19-6…`, `a19-7…`, `a19-8-2026-custom-reopened-roundtrip.png` | custom pattern `d MMM yyyy`: fields show `14 Dec 2026`; `7 Dec 2026` typed, Update, reopen: `7 Dec 2026` | PASS |
| `captures/doc3-2026-MMMyyyy-fields-fall-back-to-iso.png` | custom pattern `MMM yyyy` (no day): the date fields show and accept `yyyy-MM-dd` while the chart columns read `Dec 2026` (docs correction 3) | verified |

## A20: Delete (Revit 2026)

| File | Shows | Result |
|---|---|---|
| `captures/a20-1-2026-chart-only-open-view.png`, `a20-2-2026-after-delete-lands-on-level1-plan.png` | "South wing programme" is the only open view (Close Inactive Views); Delete from Home: "Deleted", Revit now shows Floor Plan: Level 1 | PASS |
| `captures/a20-3-2026-chart-tiled-beside-plan.png`, `a20-4-2026-tiled-after-delete-plan-remains.png` | "East wing programme" tiled beside Level 1; Delete: the chart's tile closes, Level 1 remains | PASS |

Restore-on-failure stays inferred from review and unit tests, as spec #1 says. Side note, not a
Gantt-It defect: Revit's Properties palette kept showing the deleted legend's properties until the
next selection (a modal Gantt-It window blocks its refresh).

## A23: Dark mode (Revit 2026)

Revit Options › Colors › UI active theme = Dark, then Light again; Gantt-It reopened after each
change.

| File | Shows | Result |
|---|---|---|
| `captures/a23-1-2026-dark-home-and-ribbon.png` | dark Home; the ribbon's Gantt-It icon is legible on the dark ribbon | PASS |
| `captures/a23-2-2026-dark-editor.png` | dark editor, preview on white paper (also `docs/images/editor-dark.png`) | PASS |
| `captures/a23-3-2026-dark-textbox-context-menu.png` | TextBox context menu (Cut/Copy/Paste) dark | PASS |
| `captures/a23-4-2026-dark-colour-popup.png`, `a23-5-2026-dark-pick-from-screen.png` | colour window and loupe dark | PASS |
| `captures/a23-6…`, `a23-7…`, `a23-9-2026-dark-first-run-settings-banners.png` | Settings, its drop-down, and the first-run info banners dark | PASS |
| `captures/a23-8-2026-dark-message-dialog.png` | message dialog ("Charts regenerated") dark | PASS |
| `captures/a23-10-2026-dark-no-legend-dialog.png` | no-legend dialog dark (a copy of `no-legend.rvt`) | PASS |
| `captures/a23-11…`, `a23-12-2026-light-textbox-context-menu.png` | back to Light: light editor, no-legend dialog, context menu; light ribbon icon | PASS |
| `captures/a23-13-2026-high-contrast-uses-light-palette.png` | Windows High Contrast Black on: Gantt-It uses its light palette (observed only) | observed |
| 2027 | | not run (owner accepted without retest) |

No light-only fragment was seen in dark.

## A24: Languages (Revit 2026, `Revit.exe /language XXX`)

`tools/vm/revit.ps1` gained `-RevitArgs` for this (`-RevitArgs "/language FRA"`).
Note: Revit remembers the last `/language` as its default, so a plain launch after `ITA` came up
in Italian; the English relaunch used `/language ENU`.

| File | Shows | Result |
|---|---|---|
| `captures/a24-fra-1-home.png`, `a24-fra-2-settings.png`, `a24-fra-3-editor-preview.png` | French Home, Settings (Chart language defaults to Français), editor; preview in French (`novembre 2026`, Début/Fin/Durée) | PASS |
| `captures/a24-deu-1…`, `a24-deu-2…`, `a24-deu-3…`, `a24-deu-4…`, `a24-deu-5-generated-german-chart.png` | German Home, Settings, editor; "Bauzeitenplan Nordflügel" generated with Chart language Deutsch: Abschnitt, Beginn, Ende, Dauer, November … Februar | PASS |
| `captures/a24-esp-1…`, `a24-esp-2…`, `a24-esp-3-settings.png` | Spanish Home, editor and Configuración; the chart stays German (project setting) | PASS |
| `captures/a24-ita-1-editor-english-fallback.png` | Italian Revit: Gantt-It in English | PASS |
| `captures/a24-enu-1…`, `a24-enu-2-chart-still-german-after-regenerate.png` | English Revit, Regenerate all charts now ("Regenerated 1 chart"): the chart is still German | PASS |

No clipping or English leftovers were seen in Gantt-It's own windows. (The Revit-side RST and
Align panels on the ribbon are other add-ins.)

## V4: purge Self-Test

FAIL, see the product bug above. Report: `reports/selftest-2026-selftest-26-1.json`. The other
78 checks of phase 1 pass.

## V5: documentation screenshots

All eight `TODO(screenshot)` markers in `docs/USER-GUIDE.md` are replaced (README had none):
`settings.png` (with Chart language), `editor.png`, `color-picker.png`, `pick-from-screen.png`,
`chart-on-sheet.png` (sheet "A101 - Programme", project "Riverside Library"),
`delete-confirm.png` (A101 - Programme, A102 - Phasing), `chart-german.png`, `editor-dark.png`.
No test names in the images. `pick-from-screen.png` is taken over the chart in Revit rather than
over a plan view (the fixture's plans are empty), and the screen grabber does not show the
crosshair pointer, only the loupe and its hint.

Docs text corrections from the code read (USER-GUIDE and DEVELOPING): fill cleanup wording,
two Undo steps for Regenerate all (seen in Revit's Undo list: `captures/doc2-…`), the
`yyyy-MM-dd` entry fallback (seen: `captures/doc3-…`), detached legends (pasted, or drawn
elements deleted by hand), the `_dark` ribbon icons and an accurate GanttIt.Core description, and
`msiexec /fecmus` instead of `/fa` for a command-line repair.

## Not run (owner accepted without retest)

The owner stopped the VM pass and accepted v1.1 as is. Not run: A6 for Revit 2027 (Always Load,
panel, window from the MSI install), A17 in 2027, A23 in 2027, and the Debug R27 Self-Test.
