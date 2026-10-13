# Product Slices — M3 and the product phase

**Status:** Proposed — nothing here is implemented
**Last updated:** 2026-10-13
**Starts when:** M0 is closed (see [progress.md](../project/progress.md))

M3 is delivered as **product slices**. A slice is a capability a user can see
and use, not a layer. Each one names the parity rows it moves, what "done"
means for a user, and how it is validated in the running application.

The UI is first-class work in every slice. The target is SideNotes-level
function and polish in Noto's own Windows-native visual language (ADR-011) —
not a copy of macOS.

```
engine command exists  ≠  a user can reach it  ≠  the parity row is met
```

---

## Order

```
  M0 closed ──> Slice 1 ──> Slice 2 ──> Slice 3 ──> later slices
                Workspace,   Note         Editor      search · export/backup
                settings,    management               URI/CLI · multi-monitor
                interaction  and folders
```

| Slice | Why here |
| ----- | -------- |
| **1. Workspace + Settings + interaction foundation** | Every later slice needs the settings surface and the shared interaction vocabulary (states, focus, motion, menus). Today a preference can be changed only in the database, and three surfaces carry tokens but no interaction design |
| **2. Note management and folders** | The engine already does it; only the UI is missing. The most parity rows per unit of work, and it uses slice 1's components |
| **3. The editor** | The largest block (D: 51 rows) and the riskiest: it starts with a spike on how invisible markdown is built (ADR-004), and autosave (B19) is designed with the editor, not before it |

Later slices, ordered when slice 3 ends: search (E, #17), export and backup
(#15, I-rows, J37–J40), URI and CLI (H), multi-monitor and mixed DPI (#16
slices 8a/8b, #30), floating notes.

---

## Slice 1 — Workspace + Settings + premium interaction foundation

**Outcome.** A user changes every workspace preference without touching the
database; the drawer moves like a native Windows surface; every control looks
and behaves consistently in Light, Dark and High Contrast.

**Depends on:** M0 closed. J4 builds on run at login (#94, merged).

### Steps, each one PR

| Step | Delivers | Parity rows |
| ---- | -------- | ----------- |
| 1.1 Interaction foundation | Type ramp and spacing scale beyond body/caption; control states (rest, hover, pressed, focused, disabled, selected) for buttons, list rows and inputs; a visible focus treatment; motion tokens that respect *Show animations* off; context-menu and flyout styling; the window backdrop decided; the Light and High Contrast review the three surfaces still lack | F1 (manual review half) |
| 1.2 Settings surface | A settings page reached from the tray menu and `Ctrl+,`: side, launch at login, close on outside click, single-click opening, Light/Dark/System, the show/hide hotkey on/off and rebinding, Escape behaviour, reveal the data folder | J3/A2, J4/A16, J5/A13, J7/C4, J21/F1, J25 (show/hide only)/G50/A7, J26/G51/A12, J36 |
| 1.3 Motion | Slide in and out from the docked edge, inside the performance budget; none when animations are off | A20 |
| 1.4 Activation surfaces | The Open Bar (an edge handle) and Hot Side (hover), each independently disableable, including all at once | A3, A4, J1, J2 |

### Acceptance criteria

- Every setting above changes behaviour immediately, survives a restart, and
  is reachable by keyboard alone; nothing requires editing the database.
- Each preference is read through the existing typed settings (#9); no second
  settings store.
- Every control has all of its states in Light, Dark and High Contrast,
  reviewed on screenshots kept with the PR.
- Screen reader: every control has a name and role (UI Automation), and focus
  order follows the visual order.
- Hotkey rebinding refuses a chord another process holds and keeps the old
  one, with a message (the slice 4 refusal path).
- Slide animation: no frame of the drawer appears outside its docked
  rectangle; hide still saves first; the sidebar-open budget (< 100 ms to
  interactive) still holds.
- The rows listed move to `[x]` only when every clause of their requirement
  cell is reachable; J25's other five chords stay open until G2–G6 exist.

### Validation

- `Invoke-Performance.ps1` before and after: warm start, hotkey show and idle
  memory must not regress; the result goes in the PR.
- A settings harness in `tools/validation/` (same rules: isolated data root,
  shell-started, negative controls) that changes each setting through the UI
  and checks the behaviour, then restarts and checks it again.
- The existing drawer, show/hide and tray harnesses still pass.

---

## Slice 2 — Note management and folders

**Outcome.** A user organises notes and folders with the keyboard, the mouse
and drag and drop, and can recover anything deleted.

**Depends on:** slice 1 (list-row states, context menus, drag visuals, settings
for placement and colours).

| Area | Parity rows | Engine |
| ---- | ----------- | ------ |
| Recycle bin: open, restore | B7 + B23 (paired), C11, G15 (from the list) | built (`DeleteNote`, `RestoreNote`, `ListDeleted*`, `DeleteFolder`, `RestoreFolder`) |
| Order notes: up/down, top/bottom, drag | B8, B9, B10, G17, G18 | built (`ReorderNote`) |
| Move a note to a folder | B11, G16 | built (`MoveNoteToFolder`) |
| Fold, fold all | B12, B13, G19–G22 | `FoldNote` built; fold-all needs a command |
| Pin notes and folders | B14, C8, G26 | built; G26's chord collision (G35) decided first |
| Note colours and how they show | B15, F10, J23, J24, G23, G24 | built (`SetNoteColor`, palette `note1`–`note6`) |
| Reorder and sort folders | C9, C10 | `ReorderFolder` built; sorting is new |
| New-note placement, ask for folder | B5, B6, J8, J9 | new |
| Empty states | B26, C15 | new |
| `Ctrl+N` in the editor | G14 | new |

**Acceptance criteria.** Every command reached from the UI has exactly one
call site through its handler (ADR-010, enforced by #20). Every destructive
action is recoverable from the recycle bin; no blocking confirmation. Drag and
drop has a keyboard equivalent for every move. Ordering survives a restart.

**Validation.** A harness per area (reorder, move, bin, pin/colour) driving
the real UI; `Invoke-Performance.ps1` with a realistic store (note-list
load at 1,000 notes must stay within the note-switch budget).

---

## Slice 3 — The editor

**Outcome.** Notes are written in rendered markdown that stays plain text
underneath, saved continuously, with the formatting shortcuts a SideNotes user
expects.

**Depends on:** slice 1; a spike first.

| Step | Delivers | Parity rows |
| ---- | -------- | ----------- |
| 3.0 Spike | How invisible markdown is built on WinUI 3 (ADR-004): candidates measured against the 100 KB large-note budget (< 200 ms; today's plain `TextBox` takes 365 ms) | — |
| 3.1 Rendering | Headings, emphasis, lists, checklists, code, links rendered in place; source preserved byte-for-byte | D-rows (markdown and formatting), #14 |
| 3.2 Continuous persistence | Save without a save action; the interim save-on-leave/hide/close is replaced, never stacked | B19 |
| 3.3 Editing chords and text settings | Formatting shortcuts, show/hide markup, font size; font family and size settings | G27–G42, J12, J13 |

**Acceptance criteria.** No content changes on open and save without an edit
(round trip). A crash loses at most the debounce window, and the failure
boundary (#10) still saves the open note. The large-note budget is met or
revised as a recorded decision with its reason.

**Validation.** Round-trip tests over a markdown corpus; the save-on-close and
failure-save harnesses; `Invoke-Performance.ps1` large-note open and save.

---

## Rules for every slice

1. A parity row moves only when a user can do every clause of it in the
   running application, validated on a frozen Release build.
2. The performance baseline is re-run when a slice touches startup, the shell,
   lists or the editor; the numbers go in the PR.
3. No framework or abstraction without a consumer in the same slice.
4. A slice that cannot be validated at runtime is not done.
