# v0.2 release checklist

**Status:** steps 1–4 done; the release workflow and publishing are the owner's. **Prepared:** 2026-11-13.
**Updated:** 2026-11-14, campaign run.
**Related:** [release-v0.2.md](release-v0.2.md) (the items), [release-v0.1.md](release-v0.1.md) (the last release),
[ADR-008](../decisions/ADR-008-packaging-and-identity.md), [CHANGELOG](../../CHANGELOG.md)

This is the release path to a published v0.2. A box is ticked in the PR that does it.

---

## Done

- [x] Every planned item, F1–F22, merged (#144–#160, #10, #15; PRs #169–#184)
- [x] The parity table scored against v0.2, progress updated; open MUST gaps filed as #185–#199 (#162, PR #200)
- [x] The owner's checks run on this machine as far as it allows (below), with the defects they found fixed or filed

## The owner's checks, run 2026-11-13

On the frozen Release build of `main` plus this checklist's PR, on this laptop (Windows 11 Home 10.0.26300, one
display, 1920 × 1080 at 100%), on AC power, on a temporary virtual desktop unless noted.

| Check | Issue | Result |
|---|---|---|
| Drag and drop from Explorer, the desktop and a browser | #31 | **Explorer: pass** (one file; two files, one result). **Edge: pass after two fixes:** a picture from a page is a *virtual file* with no path (Noto imported nothing), and text from a page was not accepted by the editor; both now work. **The desktop: not run**, its icons are hidden on this machine (a user setting, left as it is); it is the same shell view as an Explorer window. `Invoke-ShellDrops.ps1` |
| Accessibility by hand | #161 | **§1 Narrator, run with Narrator itself** (`Invoke-Narrator.ps1`, its "copy last spoken phrase"): the editor's name, a setting's name, state and help, and Search's result line now pass. **Fixed:** no live region was ever announced (WinUI does not raise LiveRegionChanged by itself): notices, statuses and Search's count were silent. **Filed:** a row's state (Pinned, Folded) is never spoken (#201); Narrator takes Ctrl+Alt+arrows, so fold, unfold, previous and next note do nothing with it on (#202). **§6 Focus and restoration:** covered by the ShowHide, Settings, NoteStack and Motion harnesses, all passing. **§2 High Contrast, §3 scaling, §5 IME: not run**, each needs a Windows setting changed or a language installed. **§4: not run**, one display |
| Dock spike | #153 | **One display: Windows gives the space back** however the docked process ends: killed 0.1 s, crashed 12.3 s, exit without removal about 2.4 s ([dock-appbar-spike.md](../research/dock-appbar-spike.md)). **Two monitors: not run.** *Dock* as a mode: #203 |
| Mixed-DPI, two monitors | #30 | **Not run:** one display |
| Windows 10 22H2 | #32 | **Not run:** no Windows 10 machine here |

## Release steps

1. [x] **Merge this checklist's PR** (#204) (the #31 and #161 fixes, the three new harnesses, these notes).
2. [x] **Freeze the release candidate:** the product code on `main` after step 1. From then on, only documentation, CI,
   tests and harnesses change.
3. [x] **Run the campaign** on that build, the extracted release package, on AC power:
   - every runtime harness in `tools/validation` (Baseline, Drawer, ShowHide, Motion, Visual, Settings, NoteStack,
     Editor, Images, Links, Search, RecycleBin, Welcome, Tray, Portability, AutoBackup, CrashLoop, DropImport,
     ShellDrops, Narrator, LoginStart, SingleInstance, Stability, Performance);
   - `Invoke-Install.ps1`: install v0.2 over an installed v0.1, upgrade while running, uninstall; the notes untouched;
   - `Invoke-Performance.ps1 -Runs 5 -IdleSeconds 60 -NoteCount 1000` against [quality-gates.md](../architecture/quality-gates.md).
   Every fail is fixed before step 4, or filed and listed under the CHANGELOG's known limitations by the owner's decision.
4. [x] **Date the CHANGELOG:** `[0.2.0] - <date>`; the known limitations below added.
5. [ ] **Run the release workflow** (owner): Actions › Release › Run, version `0.2.0`. It checks the version and the
   CHANGELOG, builds the installer from `main`, and creates a **draft** GitHub release with `Noto.App-win-Setup.exe`, the
   package and the feed.
   The owner then installs it from that draft on their own PC, over or beside v0.1 (decided 2026-11-14, in place of
   `Invoke-Install.ps1` here).
6. [ ] **Publish** (owner): read the draft and publish it. Publishing tags `v0.2.0`. Noto never looks for updates, so
   a v0.1 user runs the v0.2 installer; the notes are kept.

## The campaign, run 2026-11-14

On the release package (`Noto.App-0.2.0-full.nupkg`, extracted), `0.2.0+f43a030`, on AC power, on temporary virtual
desktops, with the desktop preflight clear.

**Fixed before the freeze held (#205):**
- Ctrl+C in Search hid Noto, which then came back by itself, in 4 of 6 runs on the first candidate (`0.2.0+d20abb5`).
  - Cause: Search's focus restore ran after the hide, and focusing the hidden window activated it again.
  - Fixed, then 5 of 5 runs passed.
- AutoBackup's harness was out of date: it predated the `not-due` and `off` traces added in #160. The product was right.
- The candidate was rebuilt from `main` after #205.

| Result | Harnesses |
|---|---|
| All pass | HarnessSelfTest 10, Baseline 22, Drawer 94, ShowHide 35, Motion 35, Visual 25, Settings 89, NoteStack 60, Editor 30, EditorSaveOnClose 27, Images 19, Links 13, Search 15, RecycleBin 16, Welcome 17, CtrlNEmptyFolder 11, Tray 61, Portability 18, AutoBackup 10, CrashLoop 7, DropImport 8, ShellDrops 8, LoginStart 26, SingleInstance 61, MinimizedActivation 36, Slice4Foreground 36, ExitCycles 25, Stability 14, Performance 11 |
| Known limitations | Narrator N1, N3 (#201) and N4 (#202); its other 7 checks pass |
| Rerun | HarnessSelfTest, Baseline, Drawer and ShowHide were INVALID at the campaign's start (no temporary desktop could be made); all passed on a rerun |
| Not run | `Invoke-Install.ps1` (the owner installs from the draft release instead); `Invoke-SingleInstanceElevated.ps1` (needs UAC); Tray `-ExplorerRestart` |

**Performance** (`-Runs 5 -IdleSeconds 60 -NoteCount 1000`, medians), against [quality-gates.md](../architecture/quality-gates.md):

| Measure | Result |
|---|---|
| Start: window | 652 ms |
| Start: folder list | 715 ms |
| Start: hotkey registered | 321 ms |
| Start: tray icon added | 589 ms |
| Login start: first chord served | 668.5 ms |
| Hotkey: chord to window in front | 15 ms |
| Idle 60 s, hidden | CPU 0.052% of a core, working set 7.2 MB |
| Idle 60 s, shown | CPU 0%, working set 72 MB |
| Open a short note | 60 ms |
| Open a 100,636-character note | 350 ms |
| Save on hide | 0.92 ms (short note), 4.18 ms (large note) |

## Known limitations to state in the CHANGELOG

- Narrator does not say a row's state (Pinned, Folded, a colour) (#201).
- With Narrator on, Ctrl+Alt+arrows are Narrator's: fold and unfold from the card's menu instead (#202).
- High Contrast, display scaling, input methods, two monitors and Windows 10 are not checked by hand for v0.2 (#161,
  #30, #32).
- Still unsigned: SmartScreen warns on the first run, as for v0.1 (ADR-008).

## After the release (owner)

- [ ] #161: §2 High Contrast, §3 scaling (125–200%, text size), §5 a non-US layout and an IME, §7 sign-off
- [ ] #30 and #153's two-monitor half, when a second display is connected
- [ ] #32: Windows 10 22H2, on a clean machine
- [ ] #31: a drag from the desktop, with its icons shown
- [ ] Decide which of #185–#199 (the open MUST gaps), #201–#203 join v0.3

## Decisions for the owner before step 2

| Decision | Recommendation |
|---|---|
| Release v0.2 with #201 and #202 open, as known limitations | Yes: the card menu reaches every command; both are fixes for v0.2.x |
| The checks not runnable here (#30, #32, #161 §2–5) wait until after the release, as for v0.1 | Yes, as decided for v0.1 on 2026-11-06 |
| *Dock* (#203) is v0.3 or later, after #153's two-monitor half | Yes |
