# OCCT feature graph / cut backend — 2026-10-11

Status: backend implementation awaiting Windows C++/CLI build and native execution.
This is NOT a completed, user-facing Extruded Cut command.

The shared graph evaluator now has an OCCT adapter producing detached immutable
B-Rep values. Sketch nodes retain full parametric snapshots. Extrude and Cut use
those inputs; the graph owns parameters, dependencies, transactions and history.
No viewport/selection state is used to infer the source solid.

Native operations:
- Extrude polygon profiles with polygon/circle holes or a single circle.
- Actual BRepAlgoAPI_Cut, with Blind, Through All and signed direction.
- Through All recomputes depth from the current base body's bounds projected onto
  the sketch normal; an upstream depth edit therefore changes the cut extent.
- Validate shape, nonzero solid count and finite positive volume.
- Reject cuts with no material removed or an empty result; candidate rebuild
  failure leaves the committed graph and prior B-Rep unchanged.
- Native bodies are detached ASCII B-Rep values. Save files store recipes, not
  body caches; reopen rebuilds through the identical kernel adapter.
- Preview returns isolated candidate body outputs without a document/history
  mutation, allowing later PropertyManager/viewer integration.

81 added native checks (27 on each standard plane): actual cut volume, validity,
solid/topology counts, candidate preview, termination edit, upstream rebuild,
Undo/Redo, invalid reversed cut rollback, successful reversed cut from an upper
DATUM plane, delete/restore, suppress/restore, empty-result rollback, save/reopen,
and editing the reopened parameters. A datum plane here is explicit world-space
input, NOT a claimed persistent face attachment.

Validation in this environment:
- Full managed suite: 1046 PASS / 0 FAIL.
- C# syntax including NATIVE_ACCEPTANCE: 0 errors.
- Windows C++/CLI build and all 81 new native checks: PENDING WINDOWS.
- Existing native baseline: user reported 75 PASS on a3e9d146, not re-executed here.
- Current total native gate: 156 checks, PENDING WINDOWS on this source.
- Windows UI: original 108 controls unchanged; PENDING WINDOWS.

Limitations: no Cut PropertyManager/toolbar command, graph-backed FeatureManager,
interactive second-sketch workflow or UI graph save/open yet. The old UI path is
preserved. Semantic face/edge resolution remains unsupported and fails explicitly;
there is no index-based fallback. No 3D Fillet/Chamfer/Hole was added. These remain
required work, not waived acceptance criteria. No placeholder buttons were added.

Windows next verification:

```powershell
.\tools\Run-SolidAcceptance.ps1 -RunInteraction
```

The script requires the new FEATURE GRAPH and NATIVE FEATURE GRAPH summaries;
it must fail if they were skipped. New native code links TKBO/TKBool in Debug and
Release; existing deployment copies all OCCT runtime DLLs.
