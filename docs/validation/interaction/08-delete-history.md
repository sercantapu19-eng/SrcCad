# Delete / Ctrl+Z checkpoint — 2026-10-11

## Incoming Windows evidence

User ran published `be76ed4f819ebf1bca4baceaed16f15deab1d472` with
`tools/Run-SolidAcceptance.ps1 -RunInteraction`: Completed=true, 10 PASS,
1 FAIL at `HWND Ctrl Z restores deletion`. Later controls did not execute.
Escape and Delete reaching the native HWND are confirmed by that run.

## Findings and correction

The failed JSON alone cannot distinguish a missing keyboard command from a
history restore failure. Code inspection found that Delete already captured a
full snapshot before mutation. Native KeyDown then manufactured a WPF event,
discarding `KeyEventArgs.Control`; the common handler re-read global WPF/Forms
modifier state. Also there was no native command-key preprocessing adapter.
The harness mixed queued global Ctrl injection with synchronous SendMessage(Z),
without asserting foreground activation. That bypasses normal keyboard
preprocessing and does not establish a reliable modifier/routing contract.

Both WPF and native input now call the same handler with their own modifier
snapshot. Native ProcessCmdKey consumes handled commands once, with KeyDown as
the input-key fallback; no fabricated WPF KeyEventArgs are needed. Text editors
retain local text Undo while native viewport input is independent of stale WPF
text focus. Native focus also takes precedence over stale relation/dimension
list focus for Delete.

The harness now sends the entire Ctrl+Z/Y chord through the OS keyboard queue,
checks foreground/viewport focus, and reports KeyRoute, HistoryCommand and
UndoDepth/RedoDepth in progress and final JSON. The original failing assertion
is retained verbatim. Windows confirmation is required to establish which
routing defect caused the observed failure and to close it.

Delete selection/relation/dimension now uses a shared candidate-model transaction,
commits only after solving, advances revision once and creates one history entry.
Undo/Redo share the same tested full-snapshot restore routine. Deleted geometry,
IDs/counters, references, constraints, dimensions and construction flags return;
UI selection and pending dimension references are cleared safely. All deletion
paths mark an edited extrusion source dirty. Extrusion rebuild remains on exiting
the source sketch, consistent with the current application lifecycle.

## Verification

- Before changes: 706 managed PASS, 0 FAIL.
- After changes: 1015 managed PASS, 0 FAIL (309 new deletion checks).
- Deployment infrastructure fixtures: 8/8 PASS (not B-Rep tests).
- C# syntax, including NATIVE_ACCEPTANCE sources: 0 errors; not a Windows build.
- Existing 78 Windows assertions retained; 30 added = 108 required. PENDING.
- Existing 66 native checks plus 9 real OCCT deletion/restore volume checks = 75. PENDING.
- Windows WPF/C++CLI build, keyboard/UI execution and visual acceptance: PENDING.

New managed scenarios on all three standard planes: line, circle, multi-delete,
window, crossing, relation, dimension, dimension-dependent entity, construction,
closed contour. Each checks real mutation, single history entry, exact restore,
DOF, monotonic revision, redo, repeated undo and no-op behavior. Native additions
rebuild the restored rectangle extrusion and check actual 24000 mm³ volume.
They are not a test of a complete downstream feature graph.

Windows additions exercise circle, Ctrl multi-selection, window, crossing,
construction, dimension and relation deletion; assert complete snapshots and DOF;
relation Undo/Redo uses PropertyManager focus. Original line delete test still
runs first. The new suite must not hide/skip the first failure.

Remaining requested cases: Arc deletion is unsupported (no Arc model entity),
therefore NOT IMPLEMENTED, not PASS. Live application downstream B-Rep rebuild
acceptance remains PENDING. Generic multi-feature/Cut dependencies are not yet
implemented. The attached PART FEATURE WORKBENCH scope is the next architectural
roadmap after this Windows gate; no new feature/placeholder was added here.

Run on Windows:

```powershell
.\tools\Run-SolidAcceptance.ps1 -RunInteraction
```
