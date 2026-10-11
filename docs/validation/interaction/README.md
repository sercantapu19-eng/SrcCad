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
