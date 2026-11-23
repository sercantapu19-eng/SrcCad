# Cut workbench integration — 2026-10-12

Status: implementation connected to the real kernel; PENDING WINDOWS build,
native execution and manual acceptance. Not a Part Workbench completion claim.

## User workflow now wired

Create an ordinary sketch and extrusion as before. Select Front/Top/Right datum
plane, click **Yeni Çizim**, draw a closed profile, exit sketch, then click
**Ekstrüzyon Kes**. The selected graph sketch is used as the cut profile and the
current last successful body as the base. This is explicit datum-plane sketching;
clicking a face does NOT yet create a persistent face attachment.

The shared feature PropertyManager has depth, Through All, direction reversal,
actual native preview, OK and Cancel/Esc. Wheel updates the parameter by 10 mm;
invalid values invalidate preview and cannot commit. Comma/dot numeric parsing
uses the common parser. Preview changes do not enter document history.

Graph nodes appear in FeatureManager. Double-click or context Edit opens a sketch
or the shared feature editor. Original extrusion depth edits rebuild dependent
cuts. Source sketch exit also rebuilds the graph; failure keeps editing active.
Context Delete uses explicit confirmation for dependent cascade deletion; Suppress
and unsuppress rebuild the graph. Ctrl+Z/Y use graph history outside sketch mode.
Within sketch mode the existing sketch transaction history stays in use.

Graph files use schema 2 Save/Open, preserving node IDs, recipes and dependencies.
Legacy files continue to open through the old path. Entering the workbench from
an existing extrusion migrates BOTH Undo and Redo stacks to stable graph identities;
it does not discard the old extrusion edits. No fallback to lossy legacy saving
is used for graph documents.

## Transaction/render boundary

FeatureSession now allows publication of a successful candidate before committing
its model/history. Rejected publication leaves the model, revision and both stacks
unchanged. Native graph publication creates the replacement presentation before
removing the old one and restores old presentation state on failure. Preview
sessions intentionally have no publication callback.

## Regression evidence

- Managed baseline: 1046 PASS / 0 FAIL.
- Current: 1057 PASS / 0 FAIL, including 42 feature graph checks (11 added).
- New managed checks: rejected publish/Undo, isolated preview, stable-ID legacy
  history migration including existing redo and undo before extrusion creation.
- Deployment filesystem fixtures: 8/8 PASS; not native tests.
- C# syntax and XAML/project XML syntax checked; not Windows compilation.
- Existing 108 UI checks preserved, 19 added: total 127 PENDING WINDOWS.
- Native suite remains 156 checks, PENDING WINDOWS on current source.

New UI checks use real HWND circle picks and Ctrl+Z/Y, WPF command buttons and
FeatureManager context commands. They check actual OCCT volume after Cut, Undo,
Redo, Through All, upstream depth edit and file reopen. They also check preview
isolation, Escape, native wheel and rejected invalid input. Windows script still
fails on the first failed assertion and retains the original Ctrl+Z assertion.

## Remaining limitations

Windows has not yet validated this integration. Persistent face/edge references,
face-attached sketch support and generic multi-body presentation are not complete.
The graph view currently displays the last successful body in dependency order;
this is not multibody support. 3D Fillet/Chamfer/Hole and later feature families
remain NOT IMPLEMENTED. Arc and the remaining sketch tools are still required.

Run:

```powershell
.\tools\Run-SolidAcceptance.ps1 -RunInteraction
```
