# Runtime validation harness

Scripts that drive the real `Noto.exe` on a real desktop and check what only
a running app can show: which window is in front, which one has keyboard
focus, and where the window sits on screen. They complement the unit and
runtime tests. They do not replace them.

## Why the method matters

Windows lets a process call `SetForegroundWindow` only in certain cases. A
script that launches Noto as its own child, or that has just injected input,
often holds that right itself. Noto then "comes to the front" whether or not
its own code earned it. An earlier slice 4 campaign was contaminated this
way. This harness rules it out:

| Rule | How |
|---|---|
| Noto is not the harness's child | Every process starts through the running shell (desktop view `ShellExecute`), so its parent is `explorer.exe`. The parent is checked on every start. |
| The foreground comes from real input to another app | The target app is brought forward by a real click, then real typing. The typed token must appear in that app's text box. |
| Negative control | Immediately before and after every hotkey press, an unrelated shell-started process that has received no input calls `SetForegroundWindow` for whichever window is not in front. Windows must refuse it. If Windows grants it even once, the run is **INVALID**: exit code 2, whatever the checks say. Each press is bracketed because a refusal is not guaranteed (see *Known limitations*). |
| The harness needs no foreground rights | The harness never calls `SetForegroundWindow`. |
| Injection is fenced | Typing happens only while the target owns the foreground. It stops the moment the foreground changes. The activation chord is sent only while one of the run's own windows is in front. Clicks are refused unless the window under the point is the target. The only other keys sent are the shell's Win+Ctrl virtual-desktop hotkeys, which Windows consumes before any app sees them. |
| Isolated data | Every run gets a new root, `%TEMP%\noto-validation\<run>`. Noto always starts with `--data-root=` under it. Settings are written only to databases under it, and that path is asserted before every write. |
| Real data untouched | Before and after each run, the harness hashes the default `%LOCALAPPDATA%\Noto` folder in two views: its own, and a shell-started process's. On this machine those are different folders, because of package redirection. Any difference fails the run. |
| Fresh processes | Each Noto start must have a new pid, never seen earlier in the run, and must have been started by the shell. |
| Own virtual desktop | Each run works on a temporary virtual desktop. Afterwards it switches back to the original desktop and removes the temporary one. |
| Cleanup | Every process the run started is stopped, and the run checks that none are left. |

## Evidence kept per check

`Get-WindowEvidence` records each of these separately. A minimized or cloaked
window still reports `IsWindowVisible` = true, and a minimized window can be
the foreground window.

- `WsVisible`: the `WS_VISIBLE` style.
- `Minimized`: `IsIconic`.
- `Cloaked`: `DWMWA_CLOAKED`.
- `IsForeground`: the foreground window is Noto's main window.
- `KeyboardFocus`: the foreground thread's focus window has Noto's main window as its root.
- `Outer` and `Frame`: the window rectangle and the DWM visible frame, against the monitor's work area.

## Scripts

| Script | What it checks |
|---|---|
| `Invoke-HarnessSelfTest.ps1` | The method itself, with no Noto build. It checks shell launch and parent, fresh pids, the keyboard-established foreground (the typed token reaches only the intended window), that the negative control is refused, a positive control (real input *can* move the foreground), cleanup, desktop restore, and that real data is unchanged. Run it first. |
| `Invoke-CtrlNEmptyFolder.ps1 -NotoExe <exe>` | Ctrl+N on the note surface (#69): in an empty folder it creates one note and opens it with focus in the editor; with a row focused it creates exactly one more; inside the editor it creates nothing; on the folder list it still focuses the folder name box. |
| `Invoke-MinimizedActivation.ps1 -NotoExe <exe> [-Edge Right,Left] [-Trials 3] [-Control]` | Docked on each edge, then minimized, then the hotkey. Noto must be restored, uncloaked, the foreground window, hold keyboard focus, and sit at exactly its docked rectangle. `-Control` runs a build that is expected *not* to restore, which shows the checks can fail. |
| `Invoke-Slice4Foreground.ps1 -NotoExe <exe> [-Trials 3]` | The #16 slice 4 cases: another app in front, already in front (since slice 5: the chord hides it), focus taken away, hotkey disabled, chord held by another process, malformed stored binding. It also checks that the chord is held while Noto runs and freed when it exits, and that no settings row changes. |
| `Invoke-ShowHide.ps1 -NotoExe <exe>` | #16 slice 5, the show/hide toggle: hide and show, the docked rectangle, foreground and keyboard focus after show, restore from minimized and from hidden-while-minimized, saving unsaved text before hiding (and staying shown when that fails), a tight burst of chords, a chord during a resize drag, during shutdown and during startup, a chord from another virtual desktop (hidden: shown on the current desktop; shown: Windows switches back), the taskbar button, and Alt+Tab through the real switcher (run last, because an injected Alt press can unlock the foreground). Closing is `Invoke-EditorSaveOnClose.ps1`. |
| `Invoke-Drawer.ps1 -NotoExe <exe>` | #16 slice 6, the drawer (ADR-007 §4, *The drawer*). Topmost while shown, checked by the window's style and by `WindowFromPoint` over a maximized window and over borderless full-screen windows. Putting it away: `Ctrl+W` on each surface; Escape in each of its four behaviours on each surface, and in an inline input; losing activation to another application (a click, the desktop, a virtual-desktop switch, a newly launched application in 5 rounds, Alt+Tab), with the setting on, off, and pinned. Never hidden in the foreground: a hidden Noto that is the foreground window or holds keyboard focus fails wherever it is checked, including 7 Alt+Tab rounds whose landing Windows decides. A refused foreign foreground request at a hidden or minimized Noto must leave it so (6 rounds); a click on a minimized Noto's taskbar button, an activation that lands, restores it (3 rounds). Taskbar clicks and injected Alt release the foreground lock, so those scenarios run after every bracketed claim. Every hide saves first; a failed save keeps Noto shown. Noto's own context menu is not a deactivation, and neither is an inner-edge drag or a minimize. The pin works by keyboard and is never stored. Noto's `Noto.Workspace` diagnostics are read from the debug-output channel. Virtual desktops run last, because the injected Win+Ctrl chords can unlock the foreground. |
| `Invoke-SingleInstance.ps1 -NotoExe <exe>` | A17, single instance (ADR-013). A second launch is caught the instant it appears and watched to the end: exit code, every window it showed, any message box. Noto's own `Noto.Instance` diagnostics are read from the debug-output channel. The key, mutex and pipe are probed by name, from the harness's own computation of the key. Covers: one owner; a handoff that brings the owner forward (hidden, minimized, behind another app, on another desktop) and never hides it; no database access by the second launch; malformed, oversized, wrong-version and unknown-kind requests rejected; clients the pipe's ACL must refuse; a launch during startup; five launches at once; a launch during shutdown, which waits then takes over (the owner's UI thread is frozen the instant shutdown begins); a killed owner; an unreachable owner, which gives up after about 5 s; pipe squatting; and a foreign pipe server refused by Noto's own client. Launches model the user: a real click on the desktop first, so the shell is in front. |
| `Invoke-SingleInstanceElevated.ps1 -NotoExe <exe>` | A17 scenario 23, the elevated launch. **Needs one UAC approval** from the person at the machine. Only `helpers/elevated-launch.ps1` runs elevated. With no owner, the elevated Noto must be refused (message box, exit 3, never owns). With a normal owner, it must hand off (exit 0). A declined prompt makes the run INVALID. |
| `Invoke-EditorSaveOnClose.ps1 -NotoExe <exe>` | Unsaved editor text when the window closes: saved and exits; a clean editor exits untouched; a failed save cancels the close and keeps the text and notice; a second close with the same text discards and exits; text edited after a failure is a new first attempt; closing from the note or folder list is unaffected. The editor text is set through UI Automation, so nothing is typed into the editor. |

## Prerequisites

- Windows 10 or 11 with virtual desktops, in an interactive, unlocked session.
  A lock screen, a minimized RDP window or a UAC prompt blocks input.
- PowerShell 7.4 or later, with `pwsh.exe` on `PATH`, because the shell starts
  it by name. The settings helpers load `Microsoft.Data.Sqlite` (net8.0) from
  the Noto build output.
- The .NET SDK the repository builds with, to produce `Noto.exe`.
- Nothing may hold Ctrl+Alt+Win+Space. Close any running Noto first. The runs
  check this and refuse to start otherwise.
- Hands off mouse and keyboard while a run is in progress (about 1–3 minutes).
  The run switches to a temporary virtual desktop and back.

## Running from a clean checkout

```
dotnet build Noto.sln -c Release
pwsh -NoProfile -File tools/validation/Invoke-HarnessSelfTest.ps1
pwsh -NoProfile -File tools/validation/Invoke-MinimizedActivation.ps1 -NotoExe src/Noto.Windows/bin/Release/net9.0-windows10.0.19041.0/win-x64/Noto.exe
pwsh -NoProfile -File tools/validation/Invoke-Slice4Foreground.ps1 -NotoExe src/Noto.Windows/bin/Release/net9.0-windows10.0.19041.0/win-x64/Noto.exe
```

To compare two builds, for example a fix against `main`, copy each build's
output folder somewhere else and pass each `Noto.exe` in turn. Add `-Control`
for the build that is expected *not* to restore.

Exit codes:

- `0`: all checks pass.
- `1`: a check failed.
- `2`: invalid. A control failed or the run aborted, so it proves nothing.

Each run writes `results.json` to its run root under `%TEMP%`. It includes
every negative-control result. Nothing is written into the repository.

## Hiding on deactivation (#16 slice 6)

Noto now hides when activation moves to another application, by default. The
harnesses that validate other contracts need a Noto left shown behind another
window — the toggle's background case, the Alt+Tab walk, a launch that brings a
background owner forward, the two closes of #86 — so `Invoke-ShowHide.ps1`,
`Invoke-SingleInstance.ps1` and `Invoke-EditorSaveOnClose.ps1` turn
`workspace.hide-on-deactivation` off in their isolated roots. The default, on,
is validated by `Invoke-Drawer.ps1`; `Invoke-MinimizedActivation.ps1` and
`Invoke-Slice4Foreground.ps1` run with it on.

## Known limitations

- **The negative control is not always refused.** A shell-started probe that
  had received no input was sometimes granted `SetForegroundWindow`.
  - *Seen first:* the probe targeted a window that had never been activated,
    such as one shown with `SW_SHOWNOACTIVATE`, and it was the first probe of
    its run. After one real activation, probes at those windows were refused.
  - *Seen since (2026-10-05):* the self-test's probes were granted right after
    a session unlock, although both of its windows had been activated. In two
    of three Slice 4 runs on `6917692`, the first probe, aimed at the
    just-launched Noto, was granted. Both kinds of run were valid when
    repeated.
  - *Cause:* not established. The earlier "never-activated window" explanation
    does not cover the later cases.
  - *What it means for results:* any grant makes the run INVALID, and an
    INVALID run proves nothing. Only runs in which every control was refused
    count, so a run that fails this way is repeated, never averaged in.
- **A launched window does not always take the foreground itself.** With the
  foreground lock enforced, a shell-started GUI process whose console is hidden
  from the start (`SW_HIDE`) was never brought in front. Started normally, the
  new console's host window (Windows Terminal, another process) takes the
  foreground for a moment, which is the route the drawer's scenario 18 uses. It
  records each launch's foreground chain on a background thread.
- Only the displays attached during the run are tested. To test a
  multi-display layout, the displays must be connected.
- Keyboard focus is read from `GetGUIThreadInfo`, which names the window that
  receives keystrokes. The harness does not type into Noto to confirm it:
  editor typing tests are out of scope.

## Files

- `Invoke-HarnessSelfTest.ps1`: the smoke test of the method.
- `NotoValidation.psm1`: the shared module (interop, shell launch, guarded input, evidence, data isolation, desktop, cleanup).
- `helpers/target-app.ps1`: the other app, a window with a text box that reports what is typed into it.
- `helpers/foreground-probe.ps1`: the negative control.
- `helpers/hotkey-holder.ps1`: another process holding Ctrl+Alt+Win+Space.
- `helpers/fullscreen-app.ps1`: a borderless full-screen window, optionally itself topmost (Invoke-Drawer.ps1).
- `helpers/single-instance.cs`: the single-instance probes, shared by both A17 scripts (key, mutex, pipe, raw requests, restricted clients, debug-output reader, squatter).
- `helpers/elevated-launch.ps1`: the only script that runs elevated, through UAC, for scenario 23.
- `helpers/realdata-probe.ps1`: fingerprints the real data folder from a shell-started process (names, sizes and hashes only).
