# Part Feature Workbench checkpoints

Issue #3 and the user's complete workbench specification remain the scope.
No sheet-metal, assembly, profile or drawing work is authorized at this stage.

| Area | Current status |
|---|---|
| Windows Ctrl+Z focus gate | Activation sequencing, foreground grant, bounded retry, HWND diagnostics committed; PENDING WINDOWS |
| Feature graph/transactions | Core engine implemented; 42 managed contract tests PASS; graph UI connected, PENDING WINDOWS |
| Graph persistence | Schema 2, schema 1 migration, real file roundtrip; graph UI saves/opens schema 2; legacy path retained |
| Extruded Cut | OCCT backend, shared PropertyManager, second datum sketch, tree editing and persistence connected; PENDING WINDOWS |
| Stable references | Provenance data and dependency validation; native face/edge resolution NOT IMPLEMENTED |
| 3D Fillet, Chamfer, Basic Hole | NOT IMPLEMENTED |
| Revolve Boss/Cut, Shell, Draft, Rib | NOT IMPLEMENTED |
| Linear/Circular Pattern, Mirror Feature | NOT IMPLEMENTED |
| Reference Plane/Axis/Point/Coordinate System tools | NOT IMPLEMENTED |
| Sweep/Loft Boss/Cut, Hole Wizard | NOT IMPLEMENTED |
| Multibody workbench/Boolean/Move-Copy/Delete-Keep/Split | NOT IMPLEMENTED |
| Arc/Radius/Sketch Fillet/Chamfer/Slot/Polygon/Ellipse and remaining sketch breadth | Still required; no new completion claim in this phase |
| Full FeatureManager edit/delete/suppress/reorder | Core graph and tree edit/delete/suppress connected; PENDING WINDOWS |

Next work: validate the 127 Windows checks, strengthen semantic support/topology
provenance before 3D edge fillet/chamfer and face-attached sketches. Preserve legacy
extrusion/sketch workflows. No placeholder commands. Windows pending checks do not
stop independent architecture work, per explicit user instruction.

Evidence: `docs/validation/interaction/09-focus.md`, `10-feature-graph.md`,
`11-occt-feature-graph.md`. Feature completeness and Windows acceptance remain
separate from managed contract test counts.

Latest integration: `docs/validation/interaction/12-workbench-ui.md`.
