# Windows interaction priority — 2026-10-11

The user's manual failures supersede any previous managed PASS as evidence of UI completion.

1. Hover: command mode now runs the same pointer probe before preview, including its first point. Line/circle bodies prehighlight; endpoint/center/Origin/midpoint markers describe the current pick. Midpoint is a coordinate snap, not a persistent midpoint relation on newly drawn geometry yet. Arc is not implemented and is not advertised as supported.
2. Selection/Delete/context/orientation/tools: next steps, not yet accepted.

Baseline and hover rerun: 572 managed checks PASS, 0 FAIL. This does NOT validate native rendering, mouse routing or Windows focus. Windows hover acceptance remains PENDING: start three-point circle, move over a rectangle edge, endpoint, midpoint, circle center and Origin; verify highlight before every pick and no geometry mutation on hover. Move out of viewport; hover marker must disappear.
