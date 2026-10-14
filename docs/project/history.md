# Noto Delivery History

Moved verbatim from `progress.md` on 2026-10-13, when that document became a
current-state summary. **Historical record, not maintained:** it describes each
piece of work as it was when it merged, and its "NOW" and "Outstanding
validation" headings mean *at that time*. The current state is
[progress.md](progress.md).

---

## Last UI Surface Completed — M2-3, the note editor

**M2-3 — Note Editor** merged as PR #63 → `23ee2a5`. The third production
surface, and the first time the UI writes a note.

```
folder list  ⇄  note list  ⇄  note editor      exactly one visible at a time
```

| Delivered | Detail |
| --------- | ------ |
| Plain-source editor | A multiline `TextBox` over the note's raw markdown. No parsing, no rendering |
| Open a note | `Enter` or double-click from the note list; the editor takes focus |
| Leave | `Esc` or the Back control, which is labelled with the note's current title |
| Create | `Ctrl+N` in the **note list**, opening the created note. Unbound in the editor |
| Delete | `Alt+Ctrl+Backspace` — soft delete, no confirmation dialog (parity B7) |
| Save | **On leave**, when the buffer differs from what was opened. A failed save keeps the user in the editor with the text intact. **Also when the window closes**, added after M2-3: a failed save cancels the close, and a second close with the same text discards it |
| States | Loading / Loaded / Error + Retry. **No Empty state** — an empty note is valid, and its row reads `Untitled note` |
| Title | Recomputed through `NoteTitle.From`, never stored (parity B16) |

**Dirtiness is an exact content comparison**, not a flag: the buffer is compared
against what the note was opened with using `StringComparison.Ordinal`. Typing
something and undoing it correctly reads as clean, so leaving performs no write.

**Three defects were found by running the application and fixed inside the PR.**
`Enter` did nothing because a `ListView` treats it as item activation and marks
it handled before `KeyDown` bubbles — moved to `PreviewKeyDown` (`528d6bd`).
The note list kept a stale title after a save, because the leave path restored
selection against a collection built before the write (`ef25382`). Note failures
reported folder wording (`d7442b0`). Only the last was found by static review.

**What M2-3 deliberately did not do.** No save command or Save button, and no
autosave infrastructure — no debounce, no per-keystroke write, no timer —
because parity B19's continuous-persistence design is M3's to make and a
half-built version would prejudge it. `Ctrl+N` stays unbound in the editor
because nothing documents what it should do with a note open.

---

## Engine's Last Slice — Slice 6, queries

**Slice 6 — Queries** merged as PR #52 → `4e9e09e`. The four queries the §15
slice plan never assigned, completing the engine's public surface.

```
Q2 ListNotesInFolder   Q3 ListFolders   Q4 ListTags   Q5 ListNotesForTag
```

**All 23 commands and all 7 queries of contract §1 now exist.** The contract
records Slice 6 as *"the last slice of the Core Note Engine"*.

| Query | Ordering | Source |
| --- | --- | --- |
| Q2 `ListNotesInFolder` | `IsPinned DESC, SortOrder ASC, Id ASC` | O2 in full |
| Q3 `ListFolders` | `IsPinned DESC, SortOrder ASC, Id ASC` | O2 in full |
| Q4 `ListTags` | `Name COLLATE NOCASE ASC` | **U13a** |
| Q5 `ListNotesForTag` | `UpdatedAt DESC` | **U13b** |

**Two contract amendments were gated before implementation.** §11's Q4 row
carried an *empty* obligation column and Q5's specified no order, so neither
could be written without choosing one — a product-visible decision the contract
had not made. Q2 needed no amendment: its obligation already cited O1/O2.

**Q5 deliberately has no secondary tie-break.** Two notes can share an
`UpdatedAt` (§4 gives one transaction's rows one timestamp), and the contract
does not say how they order. Recorded as open in §5a rather than resolved by
invention, so the tests assert membership for ties and not a relative order.

**No migration.** Every table and index the four queries need already existed.

> **A real test weakness was found by mutation before merge.** The ordering
> tie-break tests seeded fresh ULIDs, which are creation-ordered — so insertion
> order always equalled id order, and the tests passed whether or not the query
> ordered by `Id`. Measured: with `Id ASC` SQLite returns AAA, MMM, ZZZ;
> without it, insertion order. Ties are now seeded in *descending* id order.
> 19 mutants killed, 1 equivalent — the equivalent one drops an explicit
> `COLLATE NOCASE` that the column declaration already supplies, verified
> against SQLite rather than assumed, and no test was manufactured to kill it.

---

## Completed Foundation

| Item | Status | Evidence |
| ---- | ------ | -------- |
| Solution structure — Core / UseCases / Infrastructure / Platform / Windows | DONE | 10 projects in `Noto.sln` (6 source, 4 test); dependencies point inward |
| ADRs 001–013 | ACCEPTED (005, 006 Proposed, deferred to M6) | `docs/decisions/` |
| WinUI 3 + Windows App SDK validated | DONE | ADR-001 "validation gate passed" |
| SQLite foundation, WAL, pragmas | DONE | `NotoDatabase`; ADR-003 |
| Schema v1 | DONE | `SchemaV1.cs` |
| **Schema v2 / Migration 002** | DONE | PR #38 → `8b86a5a` — drops `Notes.Title`, adds `Folders.IsPinned` + `DeletedAt` |
| Migration safety copy | DONE | `MigrationSafetyCopy` — `BackupDatabase`, not a file copy |
| Deletion semantics, Cases A–G | ACCEPTED | PR #37 → `a6c3358`; Case D corrected in PR #39 → `4cc669b` |
| **Frozen Core Note Engine contract** | FROZEN | PR #40 → `61bfdfb` — 23 commands, 7 queries, I1–I6, O1–O6 |
| Note engine Slice 1 — types, title, failure model, `CreateNote`, `GetNote` | DONE | PR #41 → `c258c79` |
| Slice 2a — content, move, reorder, ordering, renormalisation | DONE | PR #42 → `77a4f43` |
| Note palette keys `note1`–`note6` | DONE | PR #43 → `a14e2a8` (ADR-011) |
| Slice 2b — pin, fold, colour | DONE | PR #44 → `63efc6d` |
| Slice 3 — delete, restore, recycle bin | DONE | PR #45 → `9d81c21` |
| **ULID C′ contract + implementation** | DONE | PR #46 → `fa0996d` — O(1) state, 80-bit CSPRNG kept |
| **Test-infrastructure pool isolation** | DONE | PR #47 → `4f49003` — replaced process-global `ClearAllPools` in fixture teardown; PR #85 → `bbde12e` replaced the 25 calls that remained in test bodies |
| Generic ordering engine | DONE | PR #48 → `7b13269` — domain-neutral, shared by notes and folders |
| Slice 4 — the seven folder commands | DONE | PR #48 → `7b13269` — Cases A, B, D, G; two atomic cascades |
| Progress document | DONE | PR #49 → `a5c3b1f` |
| **U12a / U12b contract amendments** | ACCEPTED | PR #50 → `66845c4` — §7a tag-name normalisation; §7 `RemoveTagFromNote` |
| **Slice 5 — the five tag commands** | DONE | PR #50 → `66845c4` — hard delete, relationship idempotency, no migration |
| Progress document | DONE | PR #51 → `f86afac` |
| **U13a / U13b contract amendments** | ACCEPTED | PR #52 → `4e9e09e` — §5a query ordering |
| **Slice 6 — the four remaining queries** | DONE | PR #52 → `4e9e09e` — Q2–Q5; no migration; engine surface complete |
| CI — build/test, CodeQL, C# analysis, docs governance | DONE | `.github/workflows/ci.yml`; branch protection on `main` |

---

## Recently Completed

```
2026-09-14  PR #38  Schema v2 / migration 002
2026-09-14  PR #39  Case D correction — RestoreFolder semantics
2026-09-14  PR #40  Core Note Engine contract FROZEN
2026-09-14  PR #41  Slice 1 — domain types, title, CreateNote, GetNote
2026-09-14  PR #42  Slice 2a — ordering engine, renormalisation
2026-09-14  PR #43  ADR-011 — note palette keys note1–note6
2026-09-14  PR #44  Slice 2b — pin, fold, colour
2026-09-14  PR #45  Slice 3 — delete, restore, recycle bin
2026-09-15  PR #46  ULID C′ — O(1) state, ordering semantics settled
2026-09-15  PR #47  Test-infrastructure pool isolation
2026-09-15  PR #48  Slice 4 — ordering engine generalised; the seven folder commands
2026-09-15  PR #50  Slice 5 — the five tag commands; U12a/U12b amendments
2026-09-15  PR #52  Slice 6 — the four remaining queries; engine surface complete
2026-09-15  PR #54  M2-0 workspace integration spike
2026-09-16  PR #56  #22 design system — Noto.UI, tokens, Light/Dark/HighContrast
2026-09-21  PR #57  M2-1 folder pane — the first production surface
2026-09-22  PR #60  M2-2 note list — enter a folder, list its notes
2026-09-22  PR #61  M2 status and parity reconciliation
2026-09-22  PR #62  M2-3 save model and Ctrl+N scope recorded
2026-09-22  PR #63  M2-3 note editor — plain-source editing, note create and delete
2026-09-28  PR #74  #16 slice 1 — window platform primitives
2026-09-28  PR #75  ADR-007 — workspace width limits recorded
2026-09-29  PR #76  #16 slice 2 — basic edge docking
2026-09-29  PR #77  #16 slices 1–2 status reconciliation
2026-09-29  PR #78  #16 slice 3 — edge setting, resize and width persistence
2026-09-29  PR #79  #16 slice 3 status reconciliation
2026-09-29  PR #80  #16 slice 4 — global activation hotkey
2026-09-29  PR #81  #16 slice 4 status reconciliation
2026-10-04  PR #83  Corrected runtime-validation harness (tools/validation/)
2026-10-04  PR #82  #16 restore re-docks at the remembered width; minimized activation restores
2026-10-05  PR #72  Windows SDK BuildTools 10.0.26100.1742 → 10.0.28000.2705
2026-10-05  PR #73  coverlet.collector 10.0.1 → 10.1.0
2026-10-05  PR #84  #16 slice 4 foreground evidence from the corrected harness
2026-10-05  PR #85  Test SQLite pool isolation; remaining layer-direction guards
2026-10-05  PR #86  Unsaved editor text is saved before the window closes
2026-10-05  PR #87  Ctrl+N creates the first note in an empty folder
2026-10-05  PR #88  Repository status truth before the Slice 5 gate
2026-10-05  PR #89  #16 slice 5 — show and hide through the global hotkey
2026-10-06  PR #90  A17 — single-instance mechanism: mutex ownership, activation pipe
2026-10-06  PR #91  A17 — one Noto per data root: startup wiring, harness, ADR-013
2026-10-07  PR #92  #16 slice 6 — the drawer: topmost, put away, pin
2026-10-12  PR #93  #16 slice 7 — the tray and the application's lifetime
```

---

## Work as it was merged (the former NOW list)

- **Run at login (#94) is on `feat/94-run-at-login`** (ADR-015). With
  `--login` Noto starts hidden: it takes the data root only if it is free —
  never a handoff, never a message box — and never activates its window, so
  nothing is shown until the hotkey, the tray or a second launch asks
  (measured in a Phase 0: no frame, never in front). The tray's Start Noto
  with Windows registers it under the user's `Run` key. Windows'
  registration is the only truth: off, on, or turned off in Windows' Startup
  apps (read, never written; unreadable counts as off). Tests and mutations
  pass, and the final campaign on the frozen `7205ef2` build passed through
  the real registry. See *Outstanding validation — run at login*.
- **#16 slice 7, the tray and the application's lifetime, is merged** (PR #93
  → `acb9e75`). A tray icon whose selection is a launch, its menu, the
  session's end saving unsaved text, one shutdown transition. See
  *Outstanding validation — #16 slice 7*.
- **#16 slice 6, the drawer, is merged** (PR #92 → `dc27173`). Topmost while
  shown; put away by `Ctrl+W`, by Escape or by losing activation; a
  session-only pin. See *Outstanding validation — #16 slice 6*.
- **A17 single instance is merged: PR #90 (platform,
  `6e56213`) and PR #91 (integration).** A second launch hands its request to the
  running Noto over a named pipe and exits; ownership of a data root is a
  named mutex, decided before the database opens (ADR-013). See
  *Outstanding validation — A17*.
- **Windows App SDK is 2.5.1** — merged as PR #68 (`4d100f2`), a major-version
  change under ADR-008's unpackaged self-contained configuration. Validated by
  a controlled A/B runtime campaign against the real application on both 1.6
  and 2.5.1: 25 scenarios, no regression observed. **PR #58 carried the same
  bump and was closed as superseded**, not merged — it was based before M2-1,
  M2-2, M2-3, #9 and #67 existed.
- **`Noto.Windows.Tests` exists** — PR #67. The narrow platform test project
  `quality-gates.md` specifies, referencing `Noto.Platform.Windows` and nothing
  above it.
- **#16 slices 1 and 2 are merged.** Slice 1 (PR #74 → `c2473d3`): display
  enumeration, work areas, monitor identity, per-display DPI, frame-inset
  measurement and dock geometry. Slice 2 (PR #76 → `126f3f0`): at launch the
  real window docks flush against the right edge of its display's work area
  (left via the `--dock-left` validation switch), at the 360 DIP default width
  fitted by the ADR-007 §4 width contract. **#16 remains OPEN.**
- **#16 slice 3 is merged** (PR #78 → `b17a1ae`). The edge comes from the
  `workspace.edge` setting (`--dock-left` is removed); the width from
  `workspace.width::<display>`, then `workspace.width`, then 360, clamped where
  it is shown and never rewritten. The window resizes from its inner edge only,
  through a native window subclass, and a finished resize saves the visible
  width. Settings gained declared key families and `TryRead`.
- **#16 slice 4 is merged** (PR #80 → `3d2d23f`). A global hotkey,
  `Ctrl+Alt+Win+Space` by default, brings the docked window to the foreground
  with keyboard focus; it never hides it (that is slice 5). As merged, it
  left a minimized window minimized; #82 fixed that (below). It is registered
  after settings load and before the window, on a message-only window, from
  `activation.hotkey.enabled` and `activation.hotkey.binding`, read at startup
  only. A malformed binding falls back to the default with its row untouched; a
  refused chord registers nothing else and is reported through `Debug` only.
- **The corrected runtime-validation harness is merged** (PR #83 → `8f6d762`):
  `tools/validation/`. It drives the real `Noto.exe` with foreground rights
  controlled. Noto is started by the shell, the foreground is set by real
  input, and a negative control brackets every press. It replaces the slice 4
  campaign, which was contaminated by inherited foreground rights.
- **The minimized restore and activation defect is fixed** (PR #82 →
  `63014bf`). Before it, a restore rebuilt the dock from minimized-state
  geometry: a shrunken width, a display chosen by the parking position, and a
  frame inset measured while minimized. The hotkey also left a minimized
  window minimized. Now a restore re-docks at the remembered requested width,
  on the window's own edge and display, and activation restores before taking
  the foreground. See *Outstanding validation — minimized restore (#82)*.
- **#16 slice 5, show/hide, is merged** (PR #89 → `582ae91`). The hotkey
  toggles the window: hidden → show, minimized → restore, behind → bring
  forward, in front → hide. Runtime-validated on one display (see
  *Outstanding validation — #16 slice 5*).
- **The pre-Slice 5 small-work campaign is merged** (PRs #72, #73, #84–#87).
  - Closing the window no longer loses unsaved editor text (PR #86). One
    explicit save runs at close; a failure cancels the close and keeps the
    text; a second close with the same text discards it with no further
    attempt. Not autosave: B19 stays M3's.
  - `Ctrl+N` creates the first note in an empty folder (PR #87), as a
    keyboard accelerator on the note surface. #69 is still open on GitHub;
    closing it is the owner's decision.
  - Test bodies no longer reset the process-wide SQLite pool, and the
    remaining layer directions are guarded (PR #85).
  - Dependency updates #72 and #73. The BuildTools update produced
    byte-identical Release output.

---

## Slice 4 Scope (as delivered)

Verified against the frozen contract §11 and §15. Everything in the IN column
is merged; nothing in the OUT column was introduced.

```
IN (all merged)                 OUT (none introduced)
CreateFolder                    MoveFolder          folders are flat; stale ADR-010 wording
RenameFolder                    ListFolders / Q3    no slice assigns it
DeleteFolder     atomic         Nested folders      parity C3 MUST: flat
RestoreFolder    atomic         Folder colours      parity C17 DROP
PinFolder                       IsCollapsed command no command exists
UnpinFolder                     Duplicate-name prevention  names are not unique (Case G)
ReorderFolder    atomic         Purge · Tags · UI · Sync · Migration 003
```

---

## Validation records

### Outstanding validation — run at login (#94)

**Phase 0, before any production code** (a disposable probe, never
committed):
- **P1, a `--login` start:** five starts, watched from before each began by a
  desktop-wide window-show hook and a foreground sampler. **No frame was ever
  presented, and Noto was never in front.** The first hotkey showed the
  workspace docked, in front and focused, with its folders loaded.
- **P2:** a tray click and a second launch after a real desktop click showed
  it as a launch. `--login` against a running owner exited silently, with no
  handoff and the owner untouched.
- **P3:** a restore after minimizing re-docked exactly.
- **P4:** one write to `Run\Noto`, read back exactly and restored.
  `StartupApproved` was only read. **Correction (2026-10-12):** the probe ran
  from a packaged (MSIX) host. That host virtualizes HKCU writes, so P4's write
  and restore reached a private copy, not your hive. The real round trip is the
  final campaign's below.

The undocumented `StartupApproved` form, as found: 12 bytes, first byte `0x02`
enabled or `0x03` disabled. Anything else is treated as disabled (the owner's
decision).

**Mutation:** 18 mutants, all killed by deterministic tests or source guards.
L2, L3 and L14 were also killed at runtime. Mutants that could write the real
registry were never given a runtime run.

**Runtime: the final campaign on the frozen `7205ef2` build (#94 with #95 on
top) passed.** Every stage, with the real registry last:

```
Tray 58 · Drawer 87 · ShowHide 35 · SingleInstance 61 · MinimizedActivation 36
Slice4Foreground 36 · EditorSaveOnClose 19 · LoginStart -RealRegistry 31
-> 363 checks, 0 failed, 0 invalid; 80/80 negative controls refused
```

- **The real registration.** The harness first proved it was on the user's real
  hive: a shell-started process saw its scratch value, then saw it gone.
  - Choosing the row wrote `Run\Noto` = `"<exe>" --login` (`before=Off
    after=On`). That value was read back both by the harness and by a
    shell-started process, and the row read checked.
  - Choosing it again removed the value (`before=On after=Off`).
  - `Run` (11 values) and `StartupApproved` (20 values) were identical to the
    snapshot, also as a shell-started process saw them. `StartupApproved` was
    never written.

- **What it took to get a valid campaign.** The fixes were all to the harness;
  there was no product defect.
  - Menu Hide left the overflow open, so the overflow is now closed after it.
  - The registry was read before Noto had written it, so the read now waits
    for Noto's trace.
  - A packaged host virtualizes HKCU. `-RealRegistry` now runs through
    `Invoke-InShell.ps1` and refuses a virtualized view.
  - Probes started by the shell could inherit a foreground right the shell had
    just used. A probe host now starts every probe, and each control records
    its ancestry.
  - Windows reuses pids. A process is now identified by its pid and its start
    time, and a launch is judged from the moment it began.
- **The environment it ran in.** The live foreground-lock timeout was
  2147483647 ms, not the stored 200000, through two clean restarts. The setter
  was not found: it was not cua-driver and not ChatGPT/Codex. It is stricter,
  not looser, so it was accepted as the baseline. A computer-use daemon that
  starts at logon was disabled for the campaign.

| Item | Status |
| ---- | ------ |
| **Final frozen campaign** | **PASSED** — frozen `7205ef2`, 363/363, real registry round trip included |
| **A real sign-in** | **NOT VALIDATED** — cannot be driven from inside a session; Noto is started from the shell with `--login`, as Explorer starts a `Run` entry |
| **Turned off in Windows Startup apps, on the real key** | **NOT VALIDATED on the real key** — that would mean writing `StartupApproved`, which Noto never does; validated on a scratch key |
| **Windows 10** | **NOT VALIDATED** (#32) |

### Outstanding validation — #16 slice 7

**Phase 0, before any production code** (a disposable probe, never
committed): what Windows 11 does with a notification-area icon. Measured:
- a new icon sits in the overflow;
- opening the overflow takes activation from Noto before the click arrives;
- `SetForegroundWindow` succeeds inside the click callback, with negative
  controls refused before and after;
- the callbacks are sent, so `GetMessageTime` is stale;
- `TaskbarCreated` reaches a hidden top-level window but not a message-only
  one, and adding an existing icon fails rather than duplicating it;
- WinUI takes no action of its own on the session's end;
- `WM_CLOSE` raises Closing and Closed synchronously, and a cancel is visible
  at once.

The first result replaced the approved toggle with a launch (the owner's
decision D9).

**Implementation found and fixed one lifetime defect** before the frozen
campaign. Under Restart Manager, `AppWindow.Closing` was raised twice, once
for Windows' own close request and once for the one Noto posts. The shutdown
transition is now entered once, and the tray's own window ignores `WM_CLOSE`.

**Mutation:** 30 mutants, all accounted for.
- **Killed by deterministic tests or source guards:** 28.
- **Killed only at runtime:** 2 (a cancelled Quit not shown; no foreground
  clean-up after the menu).
- **Killed by a guard, equivalent at runtime:** 1, the click as a toggle.
  Through the overflow it behaves as a launch, which is why D9 was made.

**Runtime evidence comes from one frozen Release build**, used for every run:
- commit `6373c48`;
- `Noto.exe` sha256 `9d29fe1b…`, `Noto.dll` `e4737bdb…`;
- manifest `02a4da25…`, 356 files, verified before the campaign.

Every run was on an idle desktop, with no other automation running. Each run
started Noto from the shell, used an isolated data root, and bracketed every
foreground claim with negative controls. Every run checked afterwards that the
real data was unchanged.

| Harness | Run | Checks | Failed | Invalid | Controls refused |
| ------- | --- | -----: | -----: | ------: | ---------------: |
| `Invoke-Tray.ps1` | `tray-20261012-100221-daf4a3` | 58 | 0 | 0 | 10/10 |
| `Invoke-Drawer.ps1` | `drawer-20261012-100732-3e6081` | 86 | 0 | 0 | 24/24 |
| `Invoke-ShowHide.ps1` | `show-hide-20261012-101257-7bb5ae` | 35 | 0 | 0 | 7/7 |
| `Invoke-SingleInstance.ps1` | `single-instance-20261012-101529-4f647a` | 61 | 0 | 0 | 6/6 |
| `Invoke-MinimizedActivation.ps1` | `minimized-20261012-101733-37a463` | 36 | 0 | 0 | 12/12 |
| `Invoke-Slice4Foreground.ps1` | `slice4-20261012-101836-fef8f3` | 36 | 0 | 0 | 15/15 |
| `Invoke-EditorSaveOnClose.ps1` | `save-on-close-20261012-101938-06dd38` | 19 | 0 | 0 | 0/0 (none needed) |
| `Invoke-Tray.ps1 -ExplorerRestart`, last | `tray-20261012-102100-ad7d64` | 7 | 0 | 0 | 0/0 (none needed) |

The Explorer restart was real: Explorer came back as a new process, the icon
was re-added exactly once, and Noto kept its process, stayed hidden and
unpinned, and received no workspace request.

Two earlier attempts on other builds are not evidence. One on `ae9499d` was
stopped as INVALID by a system overlay (`PickerHost`'s `Shell_SystemDim`). A
development run on an unfrozen build passed 56/56.

| Item | Status |
| ---- | ------ |
| **A real logoff, shutdown or restart** | **NOT VALIDATED** — cannot be driven from inside the session; Restart Manager stands in |
| **A critical shutdown** | **NOT VALIDATED** — Restart Manager cannot request one |
| **An icon on the taskbar itself** | **NOT MEASURED** — putting it there is a setting change; the overflow is the default |
| **Windows 10, multi-display** | **NOT VALIDATED** (#32, #30) |

### Outstanding validation — #16 slice 6

All runtime evidence comes from one frozen Release build (`ac5ccd2`, `Noto.exe` sha256 `3a9afa79…`), on an
idle desktop. `tools/validation/Invoke-Drawer.ps1` uses an isolated data root,
starts Noto and every other window from the shell, sends keys to Noto only
while it is in front, and brackets every foreground claim with a negative
control. **85/85 checks (run `drawer-20261007-022230-0a8825`), 0 invalid, negative controls refused 24/24.** Noto's `Noto.Workspace` trace shows each activation
change and what the coordinator decided.

Passed:
- topmost while shown: the window style, and `WindowFromPoint` over a
  maximized window and over borderless full-screen windows (one itself topmost)
- losing activation hides it: a click on another app, a newly launched
  application (5 rounds), Alt+Tab, a click on the desktop, a virtual-desktop
  switch. Unsaved text is saved first; a failed save keeps it shown, with the
  notice, and focus is not taken back
- pinned, or with the setting off, losing activation leaves it shown, above
  the other window; the hotkey still hides a pinned, focused Noto
- never hidden in the foreground: when an activation lands on a hidden or
  minimized Noto (Alt+Tab completing to it, a taskbar click), it is shown or
  restored. A refused foreign foreground request (6 rounds, hidden and
  minimized) leaves it hidden or minimized, not in front and not focused
- `Ctrl+W` on each surface hides it, saving first; a failed save keeps it shown
- Escape: all four behaviours on the editor, note list and folder list (12
  checks); Escape in the new-folder box cancels the input and never hides
- not deactivations: Noto's own context menu, an inner-edge drag across
  another window, a minimize
- the pin works by keyboard (focus, Space) and is never stored
- after an automatic hide, the hotkey and a second launch show it again
- virtual desktops: unpinned, a switch hides it and the hotkey shows it on the
  new desktop; pinned, it stays on its own desktop and the hotkey switches back

Regression on the same frozen build: `Invoke-ShowHide.ps1` 35/35, `Invoke-SingleInstance.ps1` 61/61, `Invoke-MinimizedActivation.ps1` 36/36, `Invoke-Slice4Foreground.ps1` 36/36, `Invoke-EditorSaveOnClose.ps1` 19/19, each with 0 invalid and every negative control refused. The ShowHide, SingleInstance and
EditorSaveOnClose harnesses turn hiding on deactivation off in their roots, so
they keep validating the toggle, launch and close contracts unchanged; the
default is `Invoke-Drawer.ps1`'s.

Mutation: every mutant of the final source has a disposition, recorded in PR
#92.
- **Equivalent, with proof:** one (App's own closing check, which the
  coordinator's shutdown drop duplicates exactly).
- **Retired:** two, whose code the A13 fix deleted.
- **Killed:** every other mutant, by tests, source guards or runtime.

| Item | Status |
| ---- | ------ |
| **Noto's own taskbar button** | **Deviation from the design gate, accepted by the owner.** The gate predicted "hidden". Measured: the press does not take activation from Noto, and Windows does not minimize a window that is not minimizable, so nothing happens. No workaround |
| **Exclusive full-screen** | Out of reach (summoning Noto ends exclusive mode); **not validated** |
| **Startup** | A deactivation during launch never hides; unit-tested only, because the harness cannot force its timing |
| **The "landed" rule** | Measured on Windows 11 only: 47 of 47 genuine activations found Noto already in the foreground, 14 of 14 refused requests did not. **Windows 10 not validated** (#32) |
| **Windows 10, multi-display** | **NOT VALIDATED** (#32; one display connected) |

### Outstanding validation — A17 single instance

`tools/validation/Invoke-SingleInstance.ps1` ran on a Release build of the
PR #91 branch: isolated data roots, Noto and every second launch started by
the shell, each launch preceded by a real desktop click (the user's act), and
every foreground claim bracketed by a negative control (6/6 refused).
**61/61 checks** (run `single-instance-20261005-222315-0c7806`). A second
launch was caught the instant it appeared and watched to its exit; Noto's own `Noto.Instance` diagnostics were read from the
debug-output channel.

Passed:
- one owner: mutex held, pipe served by the owner's pid, one process, one window
- a second launch exits 0 in about 200 ms, never shows a window or a message
  box, and never opens the database (new opens denied on the file meanwhile)
- the owner, behind another app, minimized or hidden, comes forward docked
  with foreground and keyboard focus; already in front, it is left as it is —
  a launch never hides Noto
- shown on another virtual desktop: Windows switches to it. Hidden: shown on
  the current desktop, no switch (ADR-007 §4, unchanged)
- during startup (0, 150, 400 ms apart): one process survives, shown
- five simultaneous launches: one owner, four handed off, one window
- during shutdown (the owner's UI thread frozen the instant shutdown began):
  the new launch waits, then takes over once the old one exits (`tookOver=True`)
- a killed owner leaves no mutex or pipe; the next launch owns at once
- an unreachable owner (process suspended): message box at about 5.3 s, exit 2
- malformed, oversized, wrong-version, unknown and reserved kinds: `Rejected`;
  the owner is not activated and stays healthy
- an unrelated process cannot take the mutex or add a pipe instance; clients
  with the user SID disabled, at low integrity, or anonymous are refused by the
  pipe's ACL; a pipe served by another user (Windows' `epmapper`) is refused by
  Noto's client before anything is sent
- pipe squatting is detected and recorded (`NameTaken`); a second launch still
  never becomes a second workspace (exit 2)
- the elevated launch (`Invoke-SingleInstanceElevated.ps1`, one UAC approval,
  13/13): with no owner it is refused — message box, exit 3, never holds the
  mutex; with a normal owner it hands off (exit 0) and the owner stays the owner

Regression on the same build: `Invoke-ShowHide.ps1` 35/35 (7/7 controls
refused), `Invoke-MinimizedActivation.ps1` 36/36 (12/12),
`Invoke-Slice4Foreground.ps1` 36/36 (15/15), `Invoke-EditorSaveOnClose.ps1`
19/19. Mutation: 29/29 platform mutants killed by tests; of 10 integration
mutants, 7 killed at runtime and 8 by the source guards — two (a second
coordinator, the pipe after the window) are equivalent at runtime and caught
only by the guards, as is the mutex released before the pipe closes.

| Item | Status |
| ---- | ------ |
| **Another Windows session, another user's Noto** | **NOT VALIDATED end to end** — needs a second account. The client's checks are tested against Windows' own system pipes and by pure tests |
| **Residual shutdown race** | A launch accepted a moment before a close goes ahead is dropped; documented in ADR-013, not mitigated |
| **Packaged build (ADR-008)** | Not built yet; re-run the harness when the sparse package lands |
| **Windows 10** | **NOT VALIDATED** (#32) |

### Outstanding validation — #16 slice 5

`tools/validation/Invoke-ShowHide.ps1` ran on a Release build of PR #89: an
isolated data root, Noto started by the shell, every foreground claim bracketed
by a negative control (7/7 refused). 35/35 checks.

Passed:
- launch visible and docked; hide; show at the exact docked rectangle with
  foreground and keyboard focus
- restore from minimized; show from hidden-while-minimized
- unsaved text saved once before hiding; a failed save keeps the window shown
  with the text and the notice
- a tight burst of chords; a chord during a resize drag (ignored); a chord
  during shutdown (dropped, exit code 0); chords during startup (coalesced,
  never hidden)
- the taskbar button gone while hidden and back when shown
- virtual desktops, as decided (ADR-007 §4): a hidden Noto summoned from
  another desktop is shown on the current desktop with no switch; a shown
  Noto summoned from another desktop makes Windows switch back to it
- Alt+Tab through the real switcher, not a proxy: Noto shown is selectable;
  Noto hidden is never selected across a full cycle. The walk runs last,
  because an injected Alt press can unlock the foreground for the next
  `SetForegroundWindow` and would contaminate any later negative control

Closing ran under `Invoke-EditorSaveOnClose.ps1` (19/19). Regression:
`Invoke-MinimizedActivation.ps1` 36/36, `Invoke-Slice4Foreground.ps1` 36/36 —
its case B now expects the hide.

| Item | Status |
| ---- | ------ |
| **Windows 10** | **NOT VALIDATED** — virtual-desktop and Alt+Tab behaviour measured on Windows 11 only (#32) |
| **Multi-display, mixed DPI, display disconnected while hidden** | **NOT VALIDATED.** One display at 96 DPI; mixed DPI blocked by #30 |
| **Physical keyboard, IME** | **NOT VALIDATED** |
| **Second launch (A17)** | Covered by A17 (ADR-013) — see *Outstanding validation — A17* |
| **Session end or a kill while editing** | Unsaved text was lost then. Since #16 slice 7 the session's end saves it (ADR-014); a kill or crash still loses it (#10; B19 is M3's) |

### Outstanding validation — minimized restore (#82)

**Validated.** The guard's decision is a pure function, covered by
deterministic tests that construct the displays. These include a display left
of the primary, which is the layout behind the off-screen restore. The
runtime tests use real Win32 windows on both edges and cover:
- `SW_RESTORE` and `SC_RESTORE` return to the exact docked rectangle
- a 480 DIP resize survives minimize and restore
- a clamped width never replaces the remembered one
- minimized activation restores and re-docks

Eight scripted mutants were all killed.

The corrected harness (`Invoke-MinimizedActivation.ps1`) ran on both edges, 3
trials each: minimized, then the hotkey with another app in front. Each trial
checked that Noto was restored (`WS_VISIBLE`, not minimized, not cloaked), was
the foreground window, held keyboard focus, and sat at exactly its docked
rectangle. Result: **36/36**, with every negative control refused. That ran on
a build whose product code is byte-identical to `63014bf`. A build without the
fix reproduced the defect in 4/4 trials.

| Item | Status |
| ---- | ------ |
| **Second display at runtime** | **NOT VALIDATED.** One display only. The off-screen restore with a display left of the primary is covered by deterministic tests, not by a real two-display run |
| **Display disconnected while minimized** | **NOT VALIDATED.** The window lands on the display nearest the saved placement (ADR-007 §4) |
| **Mixed DPI** | **NOT VALIDATED — blocked by #30** |
| **Shell gestures** | The docked window has no minimize button, and taskbar click, `Win+M` and `Win+D` did not minimize it when measured. Minimizing was driven by `SC_MINIMIZE` |
| **Hidden window** | **Out of scope.** Hide and show are slice 5; minimized and hidden stay distinct states |

### Outstanding validation — #16 slice 4

**Foreground evidence comes from the corrected harness**, `tools/validation/`
(PR #83 → `8f6d762`). Its `README.md` sets out the method:
- Noto and every helper are started by the running shell, not by the test.
- The other app is brought to the foreground by a real click and typing, and
  the typed text must arrive.
- Before and after every press, an unrelated shell-started process asks for
  the foreground and must be refused; any grant invalidates the run.
- Each run uses an isolated data root and a temporary virtual desktop, and
  cleans up every process it started.

The `Invoke-Slice4Foreground.ps1` campaign was run from a clean checkout. On
the slice 4 product code (`main` before #82), it gave **36/36**, with all 16
negative controls refused:
- another app in front → Noto is the foreground window with keyboard focus (3/3)
- already in front and focused → it stays, visible, with the same rectangle and
  no hide
- focus moved away → it is regained
- disabled → nothing is registered, and the chord leaves the other app in front
- chord held by another process before launch → Noto starts and docks, the
  holder keeps the chord and receives the press
- malformed binding `Ctrl+Hyper+Q` → the default registers and works, and the
  row is unchanged
- the chord is held while Noto runs and freed on exit
- no settings row changes, and every launch has a fresh, shell-started pid

On #82's product code, byte-identical to what `main` now has, it gave
**36/36** again, with all 16 controls refused: no regression. Both views of
the real data folders were unchanged. Visibility, minimized state, cloaking,
the foreground window and keyboard focus are each recorded separately.

The six real-app mutants were killed under the slice's original method: wiring,
the foreground call, registration order ×2, the wrong key, and WinUI
`Activate()`. That method could only make a mutant *survive*, through inherited
foreground rights, so those kills still stand. The slice 3 campaign also re-ran
on both edges under that method without regression. That is geometry evidence,
which foreground rights do not affect.

What the campaign did **not** establish:

| Item | Status |
| ---- | ------ |
| **Minimized window** | **NOT MET by slice 4; fixed by #82.** On the slice 4 code, pressing the chord made a minimized Noto the foreground window but left it minimized, with no keyboard focus; the corrected harness reproduced this in 4/4 trials. See *Outstanding validation — minimized restore (#82)* |
| **Negative-control caveat** | Windows occasionally grants the control probe the foreground. It was first seen on never-activated windows; on `6917692` it also happened right after a session unlock and on the first probe at a just-launched Noto. The cause is not established. Any grant makes the run INVALID, and only runs with every control refused count. See `tools/validation/README.md` |
| **Physical keyboard** | **NOT VALIDATED.** Every press was injected with `SendInput`; no key was pressed on a physical keyboard |
| **IME / AltGr layouts** | **NOT VALIDATED.** Only US keyboard layouts are installed on the validation machine |
| **User-visible conflict reporting** | **NOT IMPLEMENTED — deferred.** #16 requires it; a refused chord is reported through `Debug` only, until a surface (tray or settings) exists to report it on |
| **CodeQL** | **#193 open by design; #194 open, analysed.** #193 is the same `DefSubclassProc` call as #189, moved into the shared `WindowSubclass.CallDefault`; #189 is fixed on `main` as a result. #194 (`cs/missed-using-statement`, a note) is the ownership transfer in `StartHotkey`, which CA2000 requires and a `using` cannot express. Both review threads were resolved with that rationale; neither alert was dismissed or suppressed, and the CodeQL configuration is unchanged |

### Outstanding validation — #16 slice 3

The real-application campaign ran both edges on the real `Noto.exe` against
isolated data folders, with real mouse drags, Win+Arrow keys and an external
`SetWindowPos`: inner-edge resize, the 240 / 900 DIP clamps, the docked edge
and full height held, snaps and foreign moves refused, the visible width saved
to both keys at drag end, and edge and width restored after a restart. The
real data folders were unchanged. What it did **not** establish:

| Item | Status |
| ---- | ------ |
| **Second display at runtime** | **NOT VALIDATED.** One display only. The per-display width chain across two displays, the position guard across monitors, and the 7 px outer overhang onto a neighbouring display — which could be grabbable there — are covered by deterministic tests at most |
| **Mixed DPI** | **NOT VALIDATED — blocked by #30.** One display at 96 DPI; resize limits at other scales are covered by deterministic tests only |
| **Human visual review** | **OUTSTANDING.** The borderless appearance, resize-cursor feel, and closing without a caption button (Alt+F4 or the taskbar menu until the tray and hide slices) have not been looked at by a person |
| **CodeQL `cs/call-to-unmanaged-code` #189** | **Superseded by #193 (#16 slice 4).** The single `DefSubclassProc` call the subclass requires; the review thread was resolved with the rationale in ADR-007 §4, and the alert was not dismissed or suppressed. Slice 4 moved the call into `WindowSubclass.CallDefault`, so #189 is fixed on `main` and the same finding continues as #193 |


### Outstanding validation — #16 slice 2

The real-application campaign measured both edges on the real `Noto.exe`: the
visible frame flush with the work-area edge (0 px gap), the taskbar excluded,
and the outer rectangle overhanging by exactly the measured 7/0/7/7 invisible
border. What it did **not** establish:

| Item | Status |
| ---- | ------ |
| **Mixed DPI** | **NOT VALIDATED — blocked by #30.** One display at 96 DPI; no scale other than 100% was exercised |
| **Multi-monitor at runtime** | **NOT VALIDATED.** No second display was connected during the run. Negative and non-zero monitor origins are covered by deterministic tests only |
| **Mutations W5, N1, N10** | **RUNTIME-UNVERIFIED.** Each picks a display other than the window's at the native boundary; with one display they cannot fail. The runtime tests that would kill them iterate every connected display, but have not run on two |
| **Mutation A7 — docking before `Activate`** | **SURVIVED, retained by decision.** It produced identical bounds; docking stays after `Activate`, because reading the frame inset from a window not yet shown is not relied on |

### Outstanding validation — M2-3

Three items were disclosed in PR #63 and remain open. None blocks the merge;
none may be recorded as passed.

| Item | Status |
| ---- | ------ |
| **Human Light / High Contrast visual review** | **OUTSTANDING.** Theme resources were verified structurally — 13 keys across Light/Dark/HighContrast, identical key sets, HighContrast entirely system-driven, no hardcoded values — but resource inspection is not the same as looking at the result. The PR's screenshots are **Dark only**. The UI quality gate is not closed |
| **Delete-failure path (scenario 27)** | **NOT EXERCISED.** A `DeleteNote` failure requires the note to be already gone, which is the same setup as note-missing-on-open; it could not be constructed as a distinct delete failure |
| **Editor Retry after a read failure (scenario 33)** | **NOT EXERCISED.** Needs a `StorageException` during `GetNote`, not inducible without corrupting the database mid-session. The control exists and is wired as the note list's is |

**One performance observation, not a defect.** Opening a 100 KB note measured
~2360 ms against ADR-004's **provisional** `< 200 ms` budget. The engine read is
~23.8 ms; the remainder is WinUI laying out a single 102 KB `TextBox`. Typing
stayed responsive (231 ms for 10 characters), save latency is flat (85–117 ms
regardless of note size), and list virtualisation is intact (21 of 2000 rows
realised). Deliberately not optimised in M2-3; it belongs with M4.

### Outstanding validation — Windows App SDK 2.5.1 (#68)

The A/B campaign validated 25 scenarios on both 1.6 and 2.5.1 with no
regression observed. Three things it did **not** establish, recorded so the
coverage is not read as wider than it was:

| Item | Status |
| ---- | ------ |
| **High Contrast** | **NOT VALIDATED.** Applying a contrast theme needs the interactive Settings UI; the registry and `.theme` routes did not activate it, confirmed by `SystemInformation.HighContrast` reading `False`. Blocked identically on both SDK versions, so it is evidence neither for nor against a 2.5.1 regression |
| **Note-list virtualization / scrolling** | **NOT EXERCISED.** The fixture's 7 rows occupy roughly 245 px in a ~625 px list, so the list never scrolls and no row is virtualized away. The *editor* was scrolled over an 11,898-character note on both versions; that is not the same thing |
| **Mixed-DPI and multi-monitor** | **NOT VALIDATED.** The campaign ran the application on one 1920×1080 display at 96 DPI and exercised no multi-monitor or mixed-DPI behaviour. Current status is under #16 slices 8a/8b |

**A pre-existing UI gap was found, and is not an SDK regression.** With a
folder open that contains no notes, `Ctrl+N` cannot create the first note: the
handler is attached to the note `ListView`, and an empty `ListView` has no item
to take focus. Reproduced identically on both SDK versions, so it predates the
upgrade and belongs to M2-3. Tracked as **#69**; no fix was included in #68.
**Fixed by PR #87** (runtime-validated, including a pre-fix control that
reproduced it); #69 is still open on GitHub pending the owner's decision.

**Known issues** (open):
- unsaved editor text is lost if the process is killed or crashes while
  editing (#10; B19 continuous persistence is M3's). The session's end saves
  it (#16 slice 7, ADR-014), except a critical end whose save fails;
- #69 is fixed (PR #87) but still open on GitHub, pending the owner's decision;
- the harness's negative control is occasionally granted for an unknown cause
  (see `tools/validation/README.md`); such runs are INVALID and repeated.

Four defects found during M1 were all real and are fixed — a ULID monotonicity
bug (PR #46), a process-global SQLite pool race (PR #47), and in PR #48 the
folder pin/unpin `UpdatedAt` no-op violation plus a missing `DeletedAt` guard on
two repository updates.

**One recorded follow-up.** A `UNIQUE` violation on `Tags.Name` surfaces as
`StorageException`, not `DuplicateName`. Unreachable under §10's frozen
"single user, single process" model — the check-then-insert window needs
concurrent writers — so it is deferred rather than fixed.

**Worth remembering:** PR #48 reached 428 passing tests, clean CI and clean
CodeQL while still violating contract §4, and PR #50 passed 110 tests with a
redundant write that state could not observe. Both were caught by challenging
the contract and mutating the implementation, not by reading test results.
