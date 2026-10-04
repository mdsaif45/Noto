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
| `Invoke-MinimizedActivation.ps1 -NotoExe <exe> [-Edge Right,Left] [-Trials 3] [-Control]` | Docked on each edge, then minimized, then the hotkey. Noto must be restored, uncloaked, the foreground window, hold keyboard focus, and sit at exactly its docked rectangle. `-Control` runs a build that is expected *not* to restore, which shows the checks can fail. |
| `Invoke-Slice4Foreground.ps1 -NotoExe <exe> [-Trials 3]` | The #16 slice 4 cases: another app in front, already in front, focus taken away, hotkey disabled, chord held by another process, malformed stored binding. It also checks that the chord is held while Noto runs and freed when it exits, and that no settings row changes. |

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

## Known limitations

- **The negative control is not always refused.** A shell-started probe that
  had received no input was sometimes granted `SetForegroundWindow`.
  - *Where:* each time, the probe targeted a window that had never been
    activated, such as a freshly started window shown with `SW_SHOWNOACTIVATE`.
    Each time, it was also the first probe of its run. This happened in 3
    cases.
  - *Where it didn't:* after one real activation, probes at those same windows
    were refused. No probe aimed at an already-activated window has been
    granted.
  - *Likely cause:* a window that has never been activated may keep its
    start-up activation right. This is not confirmed.
  - *Why it doesn't affect the Noto runs:* every window they probe has already
    been activated, Noto by its own launch and the target by a click. The
    self-test activates both of its windows first, for the same reason.
  - *Safeguards:* every press is still bracketed, and one grant invalidates
    the whole run.
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
- `helpers/realdata-probe.ps1`: fingerprints the real data folder from a shell-started process (names, sizes and hashes only).
