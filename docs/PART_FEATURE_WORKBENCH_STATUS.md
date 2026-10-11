# Part Feature Workbench checkpoints

Issue #3 and the user's complete workbench specification remain the scope.
No sheet-metal, assembly, profile or drawing work is authorized at this stage.

| Area | Current status |
|---|---|
| Windows Ctrl+Z focus gate | Activation sequencing, foreground grant, bounded retry, HWND diagnostics committed; PENDING WINDOWS |
| Feature graph/transactions | Core engine implemented; 31 managed contract tests PASS; UI integration incomplete |
| Graph persistence | Schema 2, schema 1 migration, real file roundtrip; current UI still schema 1 |
| Extruded Cut | OCCT backend + graph + 81 native checks added; PENDING WINDOWS; user-facing command NOT IMPLEMENTED |
| Stable references | Provenance data and dependency validation; native face/edge resolution NOT IMPLEMENTED |
| 3D Fillet, Chamfer, Basic Hole | NOT IMPLEMENTED |
| Revolve Boss/Cut, Shell, Draft, Rib | NOT IMPLEMENTED |
| Linear/Circular Pattern, Mirror Feature | NOT IMPLEMENTED |
| Reference Plane/Axis/Point/Coordinate System tools | NOT IMPLEMENTED |
| Sweep/Loft Boss/Cut, Hole Wizard | NOT IMPLEMENTED |
| Multibody workbench/Boolean/Move-Copy/Delete-Keep/Split | NOT IMPLEMENTED |
| Arc/Radius/Sketch Fillet/Chamfer/Slot/Polygon/Ellipse and remaining sketch breadth | Still required; no new completion claim in this phase |
| Full FeatureManager edit/delete/suppress/reorder | Core graph supports edit/delete/suppress; graph UI NOT IMPLEMENTED |

Next work: connect the graph and candidate B-Rep outputs to a shared
PropertyManager/viewer lifecycle, second sketch creation on explicit planes,
Cut editing and graph-aware save/open. Preserve legacy extrusion/sketch editing
through migration tests. Then implement semantic support/topology provenance
before exposing 3D edge fillet/chamfer or face-attached sketches. Do not add
placeholder toolbar commands. Windows pending checks do not stop independent
architecture work, per explicit user instruction.

Evidence: `docs/validation/interaction/09-focus.md`, `10-feature-graph.md`,
`11-occt-feature-graph.md`. Feature completeness and Windows acceptance remain
separate from managed contract test counts.
