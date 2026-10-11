# Windows interaction priority — 2026-10-11

The user's manual failures supersede any previous managed PASS as evidence of UI completion.

1. Hover: command mode now runs the same pointer probe before preview, including its first point. Line/circle bodies prehighlight; endpoint/center/Origin/midpoint markers describe the current pick. Midpoint is a coordinate snap, not a persistent midpoint relation on newly drawn geometry yet. Arc is not implemented and is not advertised as supported.
2. Selection/Delete/context/orientation/tools: next steps, not yet accepted.

Baseline and hover rerun: 572 managed checks PASS, 0 FAIL. This does NOT validate native rendering, mouse routing or Windows focus. Windows hover acceptance remains PENDING: start three-point circle, move over a rectangle edge, endpoint, midpoint, circle center and Origin; verify highlight before every pick and no geometry mutation on hover. Move out of viewport; hover marker must disappear.

## Selection input repair

A selectable WinForms viewport panel now owns focus; Delete/Esc/Space are input keys. Ctrl state is read from both WPF and WinForms. An explicit Seç (Esc) command leaves drawing mode without losing selected geometry. Ctrl selection no longer starts point dragging. Windows acceptance is PENDING: click the canvas, Esc, hover/click a line or center, Ctrl-click a second, click blank space; persistent selection and clearing must match the visible geometry.

## Delete and Windows event regression

Delete now routes list focus to the selected relation/dimension, and canvas focus to sketch geometry. Existing snapshot Undo/Redo and feature-tree deletion remain in use. `tools/Run-WindowsInteraction.ps1 -AppPath <built SrcCad.App.exe>` starts an isolated opt-in app process on an interactive Windows desktop. It sends HWND mouse/key messages, exercises native screen projection and checks the live application model for hover, selection, Delete and Ctrl+Z/Y. It uses actual Ctrl key injection briefly: do not type into other applications during this run. It writes JSON, fails on exceptions/timeouts and never converts pending tests into PASS. Current status: NOT RUN (Linux environment). Manual highlight rendering and usability still require Windows observation.

## Ctrl / Selection Context

Ctrl multi-selection now offers a filtered native popup on Ctrl release. The Relations button opens the same action list; the right-click menu uses it too. Point-like pairs get H/V/Coincident; lines get Parallel/Perpendicular/H/V/Equal/Collinear; line-circle gets Tangent. Smart Dimension consumes the same selected references. H/V on two lines uses the existing solver through one atomic snapshot operation. No relation is offered solely to populate the menu. A geometrically applicable relation can still fail due to conflicting dimensions; rollback is preserved. Windows harness now includes actual Ctrl accumulation/release, popup options, center movement after selecting Horizontal, Relations button and dimension preselection (18 checks, NOT RUN here).

## Space / View Orientation

Space opens an owned view chooser with Front/Back/Top/Bottom/Left/Right/Isometric/Normal To. Buttons call the actual OCCT camera operations; current sketch normal and V axis determine Normal To. Value editors keep ownership of typed spaces. The Windows harness adds 4 checks, including sign change in native ScreenToPlane projection for Back and restoration by Normal To (22 total PENDING).

## Trim — line scope only

Çizgi Kırp displays the segment to be removed in orange before click, bounded by finite line/circle intersections. Clicking removes that segment and keeps up to two real line pieces; construction state is retained. Removed-entity dimensions/relations are cleaned; Undo restores them. Original cutter geometry is unchanged. Preview and commit use the same intersection plan; raw pointer positions avoid snapping to the wrong segment. No boundary means the full line is removed. Ambiguous exact intersection and unsupported circle/arc targets do nothing.

36 command-geometry regressions PASS; total managed 608 PASS / 0 FAIL. Windows harness now includes trim hover, click and Undo: 25 checks PENDING. This is not full Trim completion: circle-to-arc trimming, persistent intersection constraints on retained endpoints and general topology naming remain open. Fillet/Chamfer/Arc/Slot/Polygon/Extruded Cut are still unimplemented, not enabled as fake commands.

Follow-up interaction audit: selected/hovered construction geometry takes precedence over its gray construction color. Native viewport focus takes precedence over a stale WPF text-editor focus record, so Delete/Space are not discarded after leaving an editor. Smart Dimension placement now updates hover feedback as well.

## Full Windows gate

Run `powershell -ExecutionPolicy Bypass -File .\tools\Run-SolidAcceptance.ps1 -RunInteraction` from the repo (add `-OcctRoot` / `-Configuration Release` as appropriate). This builds the solution, requires all managed/native summaries, deploys and validates the exact bridge/ijwhost/OCCT DLLs for the built app, then runs the isolated UI process. It writes `logs/windows-interaction.json`. An unlocked interactive desktop is required. Without `-RunInteraction`, the runner explicitly reports that UI interaction was NOT RUN. A test-run PASS still does not certify all visual/manual requirements.

## Manual FAIL follow-up: Esc, harness timeout, window selection, angle editing

User reported the previous Windows run FAIL: Esc RoutedEvent exception, 60-second harness timeout, missing drag selection, and angle double-click not opening the editor. These reports remain acceptance inputs; no Windows PASS is claimed by the following Linux checks.

* Native key forwarding now sets `Keyboard.PreviewKeyDownEvent` before the shared handler writes Handled. The selectable panel enables standard click/double-click processing for every dimension type.
* The opt-in Windows runner writes checkpoints from startup onward, handles WPF/WinForms exceptions without modal error boxes, and reports its last checkpoint on timeout. Missing/incomplete reports cannot pass.
* Left-to-right window selection requires full geometry inclusion; right-to-left crossing selects intersected geometry. The rectangle is projected to sketch space, not approximated by a world-axis box. Ctrl adds to selection; Delete/Undo uses existing snapshots.
* All eight implemented dimension types use the same screen text hit-test, double-click dispatch, DimensionEditDialog, DimensionEditSession, parser and candidate solver. The dialog selects the current value (without rounding away precision), commits on Enter/OK and cancels on Esc. Invalid input displays an inline error and keeps the dialog open.
* Decimal comma and decimal dot are both accepted; mixed separators/thousands grouping, blank, NaN, Infinity, negatives and out-of-range angles are rejected. Angles require 0 < value < 180 degrees.
* Candidate solve keeps rejected edits out of live geometry/history. Accepted dimension edits and sketch Undo/Redo advance the session revision. This is not a persisted, general document revision system.
* Editing an existing extrusion source validates the candidate through the production OCCT profile builder (or circle preview), marks the source/Extrude dirty and retains the existing transactional rebuild on sketch exit. General multi-feature/Cut dirty propagation is still absent.

### Dimension edit audit

| Type | Shared edit path | Windows double-click regression |
|---|---|---|
| Line length | Implemented, managed verified | Added, PENDING |
| Horizontal / Vertical | Implemented, managed verified | Added, PENDING |
| Aligned point distance | Implemented, managed verified | Added, PENDING |
| Point-line distance | Implemented, managed verified | Added, PENDING |
| Parallel-line distance | Implemented, managed verified | Added, PENDING |
| Angle | Implemented, managed verified; near 0/180, repeat, conflict/invalid rollback | Added, PENDING |
| Diameter | Implemented, managed verified; actual radius changes | Added, PENDING |
| Radius / arc radius | Not implemented | Not marked PASS |

Managed baseline rerun: **706 PASS / 0 FAIL** (includes 18 window-selection and 80 dimension-edit assertions). Deployment fixtures remain separate. Windows harness: **78 planned assertions, NOT RUN here**, including 45→60 geometry, Enter/Esc, invalid inputs, Undo/Redo and all eight current dimension kinds. Native suite adds **12 angle/triangle volume tests**, bringing current source coverage to **66 native tests, NOT RUN here**. Existing historical Windows baseline is not reused as proof of these changes. Raw managed evidence: `07-dimension-edit-managed.txt`.

Next Windows gate: `.\tools\Run-SolidAcceptance.ps1 -RunInteraction`. If it fails, provide `logs/windows-interaction.json` and the error; it now contains the last completed/input step rather than only a 60-second timeout. Manual angle edit and drag selection acceptance remain PENDING. No Fillet/Chamfer/Cut completion claim.

## 2026-10-11 Delete / Ctrl+Z checkpoint

User confirmed first 10 HWND checks PASS, then `HWND Ctrl Z restores deletion`
FAIL on `be76ed4`. See [findings and coverage](08-delete-history.md).
Managed: 1015 PASS / 0 FAIL. Windows: 108 checks PENDING (original 78 retained).
Native: 75 checks PENDING (9 new deletion/restore volume checks).
Do not count this correction as Windows PASS before rerunning the script.

## 2026-10-11 Foreground gate follow-up

User Windows validation of a3e9d146: managed 1015 PASS; native 75 PASS; UI 10 PASS,
1 FAIL at foreground/focus gate before Ctrl+Z injection. [Focus correction](09-focus.md)
adds deferred activation/focus sequencing, launcher permission, bounded retry and
HWND/thread diagnostics. UI remains PENDING WINDOWS; all 108 checks retained.

## Feature graph and OCCT backend checkpoints

[Graph foundation](10-feature-graph.md): 31 added managed contract tests; full
managed 1046 PASS / 0 FAIL. [OCCT graph/cut backend](11-occt-feature-graph.md):
81 new native checks PENDING WINDOWS. Native total 156; user-reported baseline
75 applies to a3e9d146. Current UI 108 PENDING WINDOWS. Cut UI is not implemented.
