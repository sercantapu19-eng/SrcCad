# Feature graph foundation — 2026-10-11

Scope: core document/rebuild/transaction/persistence foundation. Not a completed
Windows Part Feature Workbench and not a new UI feature acceptance claim.

The old schema represents one sketch plus one optional extrusion. Schema 2 stores
stable GUID feature identities, typed parameters, input dependencies, semantic
reference provenance and suppression state. Schema 1 can be explicitly migrated
without losing sketch IDs, constraints or extrusion parameters. The existing UI
still uses schema 1; no automatic UI migration or lossy save of a graph is enabled.

The graph detects missing parents, duplicate IDs and cycles, exposes children and
transitive dependents, and evaluates in deterministic dependency order. Changed
features invalidate descendants; unrelated outputs are reused. Outputs are
immutable owned values, separated from transient viewer objects. Failed rebuilds
retain the previous document and last-good outputs, with failed/blocked nodes in
the attempt report. Suppressed parents block children without exposing stale body
outputs as successful. Fixing parameters can rebuild normally again.

A session owns atomic create/edit/delete/suppress transactions and Undo/Redo;
preview uses an isolated candidate. No-op commits do not create history. Explicit
cascade deletion is required if dependents exist. Documents are deep-copied at
transaction/read boundaries so mutable sketch collections cannot bypass history.
Save uses temporary-file replacement and validates schema, units, geometry and
references; reopening reconstructs editable parameters, not a mesh-only solid.

31 new managed tests verify scheduling, rejection rollback, no-op and failed Undo,
suppression, deletion/restore, independent preview, real file roundtrip, legacy
migration, semantic provenance serialization and a 52-node dependency chain.
The evaluator spy verifies graph contracts only; it is NOT a native geometry test.
Managed full regression: 1046 PASS / 0 FAIL.

Remaining: native body evaluator and cut integration, user-facing graph editing,
semantic face/edge resolution, linked support planes, native topological naming.
The production evaluator fails closed for unresolved semantic references; a stored
semantic label alone is not reported as resolved topology. No placeholder buttons.
