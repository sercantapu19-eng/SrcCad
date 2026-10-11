# Foreground/focus gate — 2026-10-11

User Windows result for a3e9d146: managed 1015 PASS, native 75 PASS; UI 10 PASS,
1 FAIL before keyboard injection: `Keyboard test requires foreground SrcCad and
target focus`. UndoDepth=1, RedoDepth=0, HistoryCommand=None. This confirms a
Delete snapshot exists; it does not confirm Ctrl+Z routing or restore on Windows.

Previously Activate/SetForegroundWindow/SetFocus ran back-to-back, followed by a
single 50ms check. WPF activation can restore focus after SetFocus, and Windows
foreground permission was not granted by the launcher. The report did not say
which HWND failed the condition.

The launcher now explicitly grants its child foreground activation permission
and logs whether Windows accepted the grant. The harness waits for WPF activation
work, then focuses the target, drains deferred focus work and checks the actual
HWND. At most 12 attempts, with 125ms of asynchronous waits per attempt; the outer
60s process watchdog remains. No AttachThreadInput, simulated Alt, direct Undo,
fake modifier, assertion removal or focus condition relaxation.

Progress and failure JSON includes main/target/foreground/focus HWND, process and
thread IDs, target enabled/visible, WPF focused-element type, activation return
values and attempt count. PropertyManager keyboard checks use the same activation
protocol with verified WPF list focus. All 108 assertions remain mandatory.

Local rerun: 1015 managed PASS / 0 FAIL. C# syntax passes. Current Windows UI:
PENDING WINDOWS. Native 75 PASS is user-reported evidence for the parent commit,
not a native run in this Linux environment. No native/model change in this patch.

New workbench phases may proceed independently while Windows validation is
pending, as explicitly authorized in the user's latest workbench specification.
