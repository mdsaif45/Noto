# SideNotes Parity Specification

**Status:** Draft
**Last updated:** 2026-11-13 — every row re-scored against `main` for v0.2 (#162; see *Current status*)
**Milestone:** [M3 — SideNotes Parity](../roadmap/roadmap.md#m3--sidenotes-parity--the-first-product)
**Source:** [sidenotes-feature-inventory.md](../research/_raw/sidenotes-feature-inventory.md) — SideNotes 1.6.5, macOS

---

## What this document is

This is the **objective definition of done** for M3, and therefore for Noto's
first product.

Every row traces to a numbered item in the feature inventory, which was built
from Apptorium's own documentation. Nothing here was invented. If a capability
is not in the inventory, it is not a parity requirement — it is a feature
request, and it belongs in
[competitor-enhancements.md](competitor-enhancements.md) or a later milestone.

> **The rule:** **M3 is complete when every `MUST` row is `[x]`.**
>
> `SHOULD` rows that are still `[ ]` at the end of M3 are explicitly listed as
> known gaps at the M4 gate and re-decided there — they do not silently slip.
> `DROP` rows are never checked; they are decisions, not work.

Two constraints inherited from the roadmap, restated because they are the ones
most likely to be violated in practice:

1. **Parity is functional, not visual.** Noto reproduces interaction concepts
   in its own visual language (ADR-011) and replaces macOS mechanisms with
   Windows ones. Nothing in this document requires visual cloning, and no row
   may be read as requiring it.
2. **Every parity item is a tested requirement** with acceptance criteria, not
   a checkbox someone ticks. Rows become GitHub issues under the M3 milestone;
   the `Issue` column is populated when they do.

---

## How to read it

### Columns

```
  Status  ID  Feature  SideNotes behavior  Noto requirement  Level  Issue  Notes
    │     │                                                    │      │
    │     └── inventory ID — A1, D24, J7 … traceable to source │      └── created from this doc
    │                                                          └── the decision vocabulary
    └── [ ] not started · [~] in progress · [x] done
```

Cells are deliberately short. **Reasoning lives in the prose paragraph under
each table**, not crammed into a cell.

**A row is scored against its own `Noto requirement` cell, and nothing else.**
`[~]` means some clause of that cell is delivered and some is not; `[x]` means
every clause is. A row does not become `[~]` because a neighbouring row moved,
and it does not stay `[ ]` because unrelated work is outstanding.

**Delivered means a user can do it in the running application.** A command
handler with tests, or a setting that exists only in the database, delivers
nothing on its own:

```
engine command exists  ≠  a user can reach it  ≠  the parity row is met
```

A provisional row whose open question could still change its requirement is
not scored, even when the application happens to meet it today (B25, C3).

### Current status

Counted from the rows below on 2026-11-13, re-scored against `main` for v0.2
(#162: v0.1 and the v0.2 items, F1–F22):

| Level | Rows | `[x]` | `[~]` | `[ ]` |
| ----- | ---: | ----: | ----: | ----: |
| MUST | 147 | 103 | 24 | 20 |
| SHOULD | 66 | 11 | 3 | 52 |
| ADAPT | 23 | 4 | 5 | 14 |
| BETTER | 5 | 4 | 0 | 1 |
| DROP | 23 | — | — | 23 (never checked) |
| **Total** | **264** | **122** | **32** | **110** |

```
MUST [x]   A1 A2 A7 A9 A10 A12 A13 A14 A16 A17 A19 · B1 B2 B8 B10 B14 B16 B19 B20
           C1 C4 C8 C9 C11 C13 · D1 D2 D3 D4 D5 D8 D9 D10 D11 D12 D13 D14 D17 D20
           D22 D24 D30 D36 D47 D48 D51 · E1 E2 E3 E8 · F1 · G1 G2 G3 G4 G7 G8 G9
           G11 G12 G14–G24 G27–G34 G38–G42 G45 G46 G47 G51 · I1 I2 I9 I10
           J3 J4 J5 J7 J12 J13 J21 J26 J36 J37 J40
SHOULD [x] A20 B9 B17 C15 C19 D6 D7 D15 D26 D40 G6
ADAPT [x]  A6 H1 I12 I13
BETTER [x] B7 B23 I7 I15
```

**The 2026-11-13 re-score (#162).** Every row was read against its own
requirement cell and the code on `main`, with the guard tests and the
validation harnesses as evidence; each changed row says what delivers it in
its Notes (**v0.2 score**). Before it: MUST 13 / 13 / 121, SHOULD 0 / 1 / 65,
ADAPT 2 / 2 / 19, BETTER 0 / 0 / 5.

- **Chords from the implementation plan's final map (§10).** Where the plan
  moved a chord off `Ctrl+Alt+letter` (the AltGr rule), the row is scored
  against the plan's final chord and says so: G2–G4 (`Ctrl+Shift+Win+N`, `F`,
  `V`), G6 (`Ctrl+D` inside Noto, the owner's #146), G10, G34
  (`Ctrl+Shift+T`).
- **Scored from code and harness; the owner to confirm:** A18 (multi-monitor,
  #30), B4 and H14 (drags from apps other than Explorer, #31), D1 (heading
  levels 4–5, typed).
- **Not scored, provisional:** B25 and C3, as before.
- **Every MUST still open has an issue:** #30, #31 and #185–#199, each in its
  capability milestone, not v0.2. The owner decides what joins v0.2.

The 2026-10-13 re-score, kept as history: two rows went down, because what
they credited was a stored option no user could choose (A12, G51); eleven went
up (A19, C13, G1, G9, I13 to `[x]`; A7, C2, C15, F1, G14, G15 to `[~]`).

### Traceability

Every row traces to the inventory. These are **Noto enhancements**, not
SideNotes behaviour, and are labelled so they are never mistaken for parity
debt:

| Rows | Why they are Noto's |
| ---- | ------------------- |
| B7, B23, I7, I15, J2 | BETTER — §12. The inventory records no trash, no export and no keyboard-only activation in SideNotes |
| G26 | SideNotes documents no pin-note chord; `Ctrl+Shift+P` is Noto's proposal |
| B22, E8, A18, A19, G52 | The inventory marks the SideNotes behaviour UNKNOWN; each is kept on Noto's own merits (principle 3 for E8) |
| D51 | A SideNotes defect turned into a Noto requirement |
| Tags | No row at all: the engine has tags (contract §7); they are not parity |

**Where two rows are one decision, the pair is scored together.** B7 and B23
are the case that exists today: B7's chord performs a soft delete, but the
recycle bin and restore that make a soft delete recoverable are B23's
requirement and have no UI. Scoring B7 alone would report the safety property
as underway when the half that delivers the safety does not exist. Both stay
`[ ]` until the pair is whole.

**On IDs.** Categories A–F, H and I reuse the inventory's own IDs verbatim, so
every row is traceable to a numbered source item — all 172 are present here,
none invented. The inventory's **G (shortcuts)** and **J (preferences)**
sections are unnumbered tables, so this document assigns `G1`–`G52` and
`J1`–`J40` and each row carries the SideNotes chord or setting it derives from
in the `SideNotes` column. Those IDs originate here and are stable from now on.

### Decision vocabulary

| Level | Meaning |
| ----- | ------- |
| **MUST** | Parity is not achieved without it. A Windows SideNotes missing this is not a Windows SideNotes. |
| **SHOULD** | Parity is materially weaker without it. Not disqualifying alone; several missing is. |
| **ADAPT** | SideNotes has it; Noto achieves the **same user outcome** by a different Windows mechanism. The outcome is the requirement, the mechanism is not. |
| **DROP** | macOS-specific with no sensible Windows equivalent. A reason is always given. |
| **BETTER** | Noto deliberately exceeds SideNotes. Justified individually, and kept small. |

**`DROP` rows are deliberate non-goals with reasons, not omissions.** They are
listed precisely so that nobody re-discovers them in six months and files them
as missing work. A `DROP` with no reason is a bug in this document.

### Provisional rows

The inventory labels every claim **CONFIRMED**, **INFERRED** or **UNKNOWN**.
That labelling is preserved here.

- Rows sourced from **INFERRED** or **UNKNOWN** evidence are marked
  **`⚠ provisional`** in Notes.
- A provisional row's *requirement* may be correct, but its *fidelity to
  SideNotes* is unverified. See [Open questions](#13-open-questions).
- **A provisional row may not be the sole justification for a MUST.** Where a
  provisional item is nonetheless MUST, it is because Noto needs the capability
  on its own merits, not because SideNotes is proven to have it.

---

## 1. A — Sidebar / Workspace

| Status | ID | Feature | SideNotes behavior | Noto requirement | Level | Issue | Notes |
| :----: | -- | ------- | ------------------ | ---------------- | :---: | :---: | ----- |
| `[x]` | A1 | Screen edge window | Occupies one vertical screen edge as an overlay panel | Edge-docked topmost panel (ADR-007) | MUST | — | **Done** — #16 slices 2–3 (#76, #78): docked to either edge; slice 6: topmost while shown |
| `[x]` | A2 | Side changing | Left / right edge; default right | Left / right, switchable; default right | MUST | — | M2 **v0.2 score (2026-11-13):** Left or right in Settings › Side, default right, re-docked at once (`Invoke-Settings` case 4). |
| `[ ]` | A3 | Open Bar | Semi-transparent edge strip; click to reveal. Hide: never / mouse inactive / always | Edge handle with the same three hide modes | MUST | #185 | `always` must not require another surface **v0.2 score (2026-11-13):** Not built. |
| `[ ]` | A4 | Hot Side mode | Hover the screen edge to reveal | Hover activation, independently disableable | MUST | #185 | Principle 7 **v0.2 score (2026-11-13):** Not built. |
| `[~]` | A5 | Menubar Icon mode | Menu-bar icon toggles visibility | **System tray** icon toggles visibility | ADAPT | — | macOS menu bar → Windows tray. **Partial** — #16 slice 7 (ADR-014): selecting the tray icon shows, restores or brings Noto forward, but does not hide it. On Windows 11 a new icon sits in the overflow, and opening the overflow takes activation from Noto before the click arrives (measured), so an in-front Noto is never in front when the click lands: selection is a launch, not a toggle. Hiding from the tray is its menu's Hide |
| `[x]` | A6 | Icon context menu | Right-click menu-bar icon → actions | Tray icon right-click context menu | ADAPT | — | **Done** — #16 slice 7 (ADR-014): Show, Hide, Keep Noto open, Quit; by right-click, Shift+F10 or the menu key |
| `[x]` | A7 | Global show/hide shortcut | `⌃⌥⌘␣`, configurable | Global hotkey, configurable, works when unfocused | MUST | — | **Partial** — `Ctrl+Alt+Win+Space` shows and hides Noto when it is not focused, on by default (#16 slices 4–5). Configurable only by editing the stored setting: no settings UI (G50). See G1 **v0.2 score (2026-11-13):** Rebindable in Settings › Shortcuts with a recorder (G50, Slice 1.2b; `Invoke-Settings` case 14). |
| `[~]` | A8 | Always on top | Over other apps incl. full-screen; Stage Manager compatible | Topmost over normal and full-screen windows | MUST | #186 | Stage Manager has no analogue; that half is dropped. **Partial** — #16 slice 6: topmost over normal and borderless full-screen windows (runtime-validated). Exclusive full-screen is out of reach (summoning Noto ends exclusive mode) and not validated **v0.2 score (2026-11-13):** Unchanged: exclusive full-screen not validated. |
| `[x]` | A9 | Pin window open | Configurable shortcut pins the panel visible | Pin panel open, keyboard + tray reachable | MUST | — | **Done** — #16 slice 6: a header pin toggle, reachable by Tab and Space, session-only; #16 slice 7: the tray's Keep Noto open, through the same toggle. A configurable chord is G6 |
| `[x]` | A10 | Close window | `⌘W` hides the panel | `Ctrl+W` hides the panel | MUST | — | **Done** — #16 slice 6: `Ctrl+W` hides the workspace from any surface, saving unsaved text first |
| `[ ]` | A11 | Hide with modifier+Enter | Optional `⌘↩` binding to hide | Optional `Ctrl+Enter` binding to hide | SHOULD | — | Optional, off by default |
| `[x]` | A12 | Escape behavior | 4 modes: leave-folder-or-hide / leave-folder / hide / none | The same 4 modes, same default | MUST | — | **Partial** — #16 slice 6 built the four behaviours and stores the choice in `workspace.escape`; a user reaches only the default, leave-folder-or-hide. The other three need the settings UI (G51/J26). Re-scored 2026-10-13: a stored option no user can choose is not delivered **v0.2 score (2026-11-13):** All four modes in Settings › Escape (`Invoke-Settings` case 5). |
| `[x]` | A13 | Close on click outside | Panel hides on outside click | Same, as a setting | MUST | — | **Partial** — #16 slice 6: hides when activation moves to another application (the mechanism behind "outside click"), on by default, suppressed by pin; stored in `workspace.hide-on-deactivation`. No settings UI yet (J5) **v0.2 score (2026-11-13):** Settings › Hide when I click elsewhere (`Invoke-Settings` case 6). |
| `[x]` | A14 | Width resize | Free drag on the window edge; no presets | Free drag resize, width persisted | MUST | — | **Done** — #16 slice 3 (#78): free inner-edge drag, width remembered per display |
| `[ ]` | A15 | Drag-to-reveal at edge | Dragging content to the edge reveals the panel; disableable | Same; two independent toggles as in SideNotes | MUST | #185 | Disableable per principle 7 **v0.2 score (2026-11-13):** Not built. |
| `[x]` | A16 | Launch on startup | Auto-launch at login | Run at login, off by default | MUST | #94 | **Done** — run at login (ADR-015): the tray's Start Noto with Windows registers a hidden start (`--login`) under the user's `Run` key, off by default; Windows' Startup apps setting is honoured and never overridden. A real sign-in is not validated end to end (not drivable from inside a session); `StartupTask` waits for the sparse package (ADR-008) |
| `[x]` | A17 | Second launch focuses | Relaunch brings existing instance forward | Single-instance; second launch shows the panel | MUST | — | **Done** — A17 (#90, #91; ADR-013): a second launch hands off and exits; the running Noto is shown, restored or brought forward, never hidden |
| `[~]` | A18 | Multi-display behavior | Undocumented | Deterministic monitor choice + follow-focus option | MUST | #30 | ⚠ provisional — UNKNOWN in every source **v0.2 score (2026-11-13):** The monitor is chosen deterministically (ADR-007: the display under the window, else the saved placement). No follow-focus option; mixed-DPI multi-monitor is #30. Scored from code and harness; owner to confirm. |
| `[x]` | A19 | Virtual desktop behavior | Undocumented (Spaces) | Defined, documented Virtual Desktop behavior | MUST | — | **Done** — defined and documented in ADR-007 §4 (*Virtual desktops*): the hotkey never moves the window between desktops, and a desktop switch is a deactivation. Measured by `Invoke-ShowHide.ps1` and `Invoke-Drawer.ps1`. Q2 (what SideNotes does) stays open; per Q2 it cannot change this requirement |
| `[x]` | A20 | Slide animation | Panel slides out from the edge | Slide-in/out within the perf budget | SHOULD | — | ⚠ provisional — INFERRED from review **v0.2 score (2026-11-13):** F2 (#145): slides in over 167 ms and out over 83 ms, at once with animations off (`Invoke-Motion`). |
| `[ ]` | A21 | Two-finger edge gesture | Trackpad edge gesture; known to conflict | Precision-touchpad gesture **if** available, **off by default** | ADAPT | — | ⚠ provisional — INFERRED. Keyboard path mandatory |

**A1–A4, A14, A16** were planned as M2 work — they are listed here because
parity is defined by the whole set, and M3's exit criterion reads this table.
A1, A14 and A16 are checked; A2 (switching sides), A3 (Open Bar) and A4 (Hot
Side) are not built, and move to the M3 product slices.

**A3 and A4 are the product's loudest research finding.** SideNotes users have
complained for years that they must choose between a visible Open Bar that
opens accidentally and an extra menu-bar icon, with no keyboard-only mode.
Principle 7 makes every activation surface independently disableable, which
turns SideNotes' `Hide Open Bar = always` restriction (only available in Hot
Side and Menubar modes) into an unconditional capability. That is recorded as a
BETTER row in section 12 rather than bloating this table.

**A8 splits.** "Always on top, including over full-screen apps" is a MUST and
is the harder half. "Stage Manager compatible" is meaningless on Windows and is
silently dropped — Windows Virtual Desktops are covered by A19 instead.

**A18 and A19 are the largest provisional rows in the document.** Multi-display
behavior is undocumented on every Apptorium page, and the inventory says so
explicitly. Noto cannot copy an undocumented behavior, so these rows specify
*that a deterministic behavior exists and is documented*, not *which* behavior.
They are MUST because a multi-monitor Windows user hits this on day one — not
because SideNotes is known to do it well. The actual behavior is decided in M2
and recorded, not inherited.

**A21 is the one gesture row.** SideNotes' two-finger edge gesture is a
documented source of conflict. Noto may implement a precision-touchpad
equivalent where the hardware supports it, but it ships **off by default** and
never as the only path to anything (principle 7).

---

## 2. B — Note management

| Status | ID | Feature | SideNotes behavior | Noto requirement | Level | Issue | Notes |
| :----: | -- | ------- | ------------------ | ---------------- | :---: | :---: | ----- |
| `[x]` | B1 | Create note (in app) | `⌘N`; creates a folder in folder list | `Ctrl+N`, context-dependent | MUST | — | **Partial** — M2-3 (#63) binds it in the note list; unbound in the editor. B2–B5 creation surfaces remain — see the note below **v0.2 score (2026-11-13):** Ctrl+N makes a folder on the folder list and a note in a folder, also while editing (`Invoke-NoteStack` case 7, #69). |
| `[x]` | B2 | Create note (global) | `⌃⌥⌘N`; panel opens with note active | Global hotkey, note focused and ready to type | MUST | — | **v0.2 score (2026-11-13):** F17 (#156): a global shortcut shows Noto with a new note being edited; off by default, Ctrl+Shift+Win+N (implementation plan §10.1), rebindable (`Invoke-Settings` case 17). |
| `[~]` | B3 | Note from clipboard | New note from clipboard; long-press **+** or global shortcut | Same, via global hotkey and **+** menu | MUST | #187 | **v0.2 score (2026-11-13):** F18 (#157): from a global shortcut and the tray menu. No **+** menu offers it. |
| `[~]` | B4 | Create by drag & drop | Drop text / files / images to create a note | Same, Explorer and app drag sources | MUST | #31 | **v0.2 score (2026-11-13):** F9 (#152): files dropped from an OLE file list (as Explorer gives) become notes (`Invoke-DropImport`). Drags from other apps, a browser included, are the owner's check (#31). Scored from code and harness; owner to confirm. |
| `[~]` | B5 | New note placement | top / bottom / over current / under current | The same four options | MUST | #188 | **v0.2 score (2026-11-13):** The four placements are built and stored (`notes.new-placement`) but Settings has no row: a user reaches only Bottom. |
| `[ ]` | B6 | Ask for folder on global create | Setting: prompt for destination folder | Same setting | SHOULD | — | |
| `[x]` | B7 | Delete note | `⌥⌘⌫`; confirmation popover since 1.6 | `Alt+Ctrl+Backspace`; **soft delete**, no blocking confirm | BETTER | — | M2-3 (#63) wires the chord to a soft delete with no confirmation. **Not `[~]`** — B7 and B23 are one decision, and the recycle bin and restore B23 requires have no UI — see B23 / section 12 **v0.2 score (2026-11-13):** Ctrl+Alt+Backspace moves the note to the Recycle Bin, no confirm; with B23 the pair is whole (`Invoke-NoteStack` case 11, `Invoke-RecycleBin`). |
| `[x]` | B8 | Move note up / down | `⇧⌥⌘↑` / `⇧⌥⌘↓` | Same action, Windows chord | MUST | — | **v0.2 score (2026-11-13):** Ctrl+Alt+Shift+Up/Down within the pin group (G17, G18; `Invoke-NoteStack`). |
| `[x]` | B9 | Move note to top / bottom | Exists; chord unpublished | Move to top / bottom, keyboard reachable | SHOULD | — | ⚠ provisional — UNKNOWN chord **v0.2 score (2026-11-13):** Move to top / Move to bottom in the card menu (`Invoke-NoteStack` cases 31–35). |
| `[x]` | B10 | Drag & drop reordering | Reorder notes and folders; note-onto-note disabled | Same, incl. the note-onto-note prohibition | MUST | — | **v0.2 score (2026-11-13):** Cards and folders drag to reorder; a drop across the pin groups is refused (`Invoke-NoteStack` cases 37–42). |
| `[~]` | B11 | Move note to folder | `⇧⌘M`; recent folders; create folder inline | Same, incl. recent folders and inline create | MUST | #189 | **v0.2 score (2026-11-13):** Ctrl+Shift+M and the card menu move a note, recent folders first. No inline folder create. |
| `[~]` | B12 | Fold / unfold note | Collapse to first line; gear, double-click, pinch, keyboard | Collapse to first line; menu, double-click, keyboard | MUST | #190 | Pinch → touchpad only, optional **v0.2 score (2026-11-13):** The card menu, the card bar's Fold button and Ctrl+Alt+Left/Right; "+ N lines" unfolds (F6, #149). No double-click. |
| `[~]` | B13 | Fold all / unfold all | `⇧⌥⌘←` / `⇧⌥⌘→`; also the **…** menu | Same, keyboard and list menu | MUST | #190 | **v0.2 score (2026-11-13):** Ctrl+Alt+Shift+Left/Right (`Invoke-NoteStack` case 27). No list menu. |
| `[x]` | B14 | Pin notes | Pinned notes stay at list top | Same | MUST | — | **v0.2 score (2026-11-13):** Pin and Unpin from the card menu; pinned cards first (`Invoke-NoteStack` case 22). |
| `[~]` | B15 | Note colors | Six colors + empty; `⌘0`–`⌘6`; Background or Bar style | Six colors + none; `Ctrl+0`–`Ctrl+6`; both styles | MUST | #191 | Six, not "some" **v0.2 score (2026-11-13):** F7 (#150): six colours and none, Ctrl+0–Ctrl+6, the menu and the bar. One style (tint and edge marker together), not the two. |
| `[x]` | B16 | First line is the title | Title = line 1; survives folding; excluded by quick-copy; in note URL | Same rule, everywhere the title is used | MUST | — | **Partial** — M2-2 (#60) and M2-3 (#63) derive every displayed title through `NoteTitle.From`; none is stored. Folding, quick-copy and note URLs do not exist yet **v0.2 score (2026-11-13):** Every title is `NoteTitle.From`; a folded card shows it; Search's Ctrl+Shift+C copies without it. Note URLs do not exist (H5). |
| `[x]` | B17 | Note bottom bar | Per-note action bar (format / share / move) | Per-note action affordance; Noto's own layout | SHOULD | — | Functional, not visual **v0.2 score (2026-11-13):** The card being edited has a bar: colour, format, fold, recycle (B17, F6–F8). |
| `[~]` | B18 | List bottom bar | List-level **…** menu + note count | List-level menu + note count | SHOULD | #190 | **v0.2 score (2026-11-13):** The list's bar shows the note count and a New note button. No list-level menu. |
| `[x]` | B19 | Auto-save | Continuous persistence; no save command | Continuous persistence; no save command | MUST | — | ⚠ provisional — INFERRED (no save cmd documented). **NOT satisfied by M2-3 (#63, merged)** — save-on-leave is an interim model; deferred to M3, see the note below **v0.2 score (2026-11-13):** Autosave a second after typing stops; every leave, hide and close saves (P2.1; `Invoke-Editor` case 10). |
| `[x]` | B20 | Switch between notes | `⌘⌥↓` / `⌘⌥↑` | Same action, Windows chord | MUST | — | **v0.2 score (2026-11-13):** Ctrl+Alt+Up/Down edit the previous or next card (G11, G12; `Invoke-Editor` case 18). |
| `[ ]` | B21 | Print a note | `⌘P` | `Ctrl+P` | SHOULD | — | Top historic complaint; fixed in 1.6.3 |
| `[ ]` | B22 | Duplicate note | Not documented (folder duplication is) | Duplicate note | SHOULD | — | ⚠ provisional — UNKNOWN in SideNotes |
| `[x]` | B23 | Trash / restore | **None.** Recovery is backups only | Soft delete + recycle bin + restore | BETTER | — | ADR-002; M1 **v0.2 score (2026-11-13):** The Recycle Bin restores and permanently deletes notes and folders (`Invoke-RecycleBin`); B7 makes the pair whole. |
| `[ ]` | B24 | Note templates | Not documented | **Not a parity requirement** | DROP | — | No evidence it exists. Do not build speculatively |
| `[ ]` | B25 | Per-note width | Notes fill panel width; no per-note size | Notes fill panel width | MUST | — | ⚠ provisional — INFERRED. Met by construction today (the editor fills the panel); scored when the row stops being provisional |
| `[ ]` | B26 | Empty note placeholder | Placeholder UI for empty notes | Placeholder text in an empty note | SHOULD | — | |

**B7 and B23 are one decision, not two.** SideNotes deletes immediately behind
a confirmation popover, and has no trash — the inventory is explicit that
recovery is via backups only. Noto inverts this: soft delete into a recycle
bin, which removes the need for a modal confirmation on every delete. This is a
BETTER row and is justified in section 12. The engine half is M1 work and is
built (`DeleteNote`, `RestoreNote`, `ListDeletedNotes`); the recycle bin a user
can open is not.

**Which is why B7 is still `[ ]` after M2-3.** The chord is wired, the delete is
soft, and there is no blocking confirmation — every clause of B7's own
requirement cell. But the recycle bin it deletes into has no user surface, so a
user who presses it cannot yet get the note back. The inversion this BETTER row
claims over SideNotes is recoverability, and recoverability is the part that is
missing. Marking B7 `[~]` would report the trade as partly honoured when the
side Noto offers in exchange for dropping the confirmation dialog is absent.

**B16 is load-bearing and easy to get wrong.** SideNotes has no title field.
Line 1 *is* the title: it is what remains when a note is folded, what
`⌘⌥C` excludes from quick-copy, and what appears in the note URL. Noto must
adopt the same rule rather than adding a title column, because adding one would
change quick-copy, folding and URL semantics all at once.

**B1's context, and where M2-3 stopped.** The inventory is explicit and
CONFIRMED: the same key creates a note in the current folder and a folder in
the folder list. M2-1 binds `Ctrl+N` in the folder list; M2-3 (#63, merged)
binds it in the note list, which completes the documented pair.

Inside the **editor** it stays unbound. Nothing documents what `⌘N` does while
a note is open, and the plausible answers — create and switch, create in the
background, open a second editor — are all multi-document behaviour that no
source describes and that the single-surface navigation model does not
support. Leaving it unbound is the reading that invents nothing; B1 stays open
until the remaining creation surfaces (B2 global, B3 clipboard, B4 drag-drop,
B5 placement) land.

**B19 is deliberately NOT satisfied by M2-3. It is a deferred parity
requirement, accepted as a recorded exception.**

M2-3's editor, merged as #63, edits an in-memory buffer and persists **on
leave**: `Esc` or Back runs `UpdateNoteContent` when the buffer is dirty, stays
in the editor if that fails, and never discards the edit. Closing the window
runs the same save once more, as one explicit save rather than autosave: a
failed save cancels the close, and only a second close with the same text,
which the failure notice offers, discards it. This row requires
*continuous* persistence. Those are **materially different user-facing behaviours** — under
save-on-leave a crash or a kill mid-edit loses the buffer, and under continuous
persistence it does not — so **B19 remains open and M2-3 must not claim it.**

**The engine does not satisfy this row.** `UpdateNoteContent` has existed since
M1 (contract row 2, which cites B19), and that is a capability, not the
behaviour B19 specifies. B19 is about *when* Noto writes, and the write trigger
is part of the product behaviour a user experiences — not an implementation
detail behind it. Anyone reading "the command exists" as "B19 is met" is
reading it wrong.

**Resolution is M3 work.** Continuous persistence must be designed, implemented
and validated during parity completion, whose exit is this specification being
complete. Until then B19 stays `[ ]` and this exception stands.

Two things followed that M2-3 did not do, and neither widens its scope into
autosave. **No save command or Save button** was introduced, because B19 says
there is none and adding one would have to be taken back. **No autosave
infrastructure** — no debounce, no per-keystroke write, no background timer —
was built speculatively, because the continuous-persistence design is M3's to
make and a half-built version would prejudge it. Both constraints were verified
against the merged implementation and continue to bind later work.

**B22 is a SHOULD on Noto's merits, not SideNotes'.** Folder duplication is
CONFIRMED in SideNotes 1.6.3; note duplication is not documented anywhere.
Noto does it because it is trivially cheap once folder duplication exists, not
because parity demands it. If it slips, parity is not affected.

**B24 is a genuine non-goal.** No source mentions templates. Building them
would be inventing a SideNotes feature, which this document forbids.

---

## 3. C — Folders / organization

| Status | ID | Feature | SideNotes behavior | Noto requirement | Level | Issue | Notes |
| :----: | -- | ------- | ------------------ | ---------------- | :---: | :---: | ----- |
| `[x]` | C1 | Folders | Folder list is top level; entering shows notes | Same two-level navigation model | MUST | — | **Done** — M2-1 (#57) + M2-2 (#60): replacing navigation, folder list ⇄ note list |
| `[~]` | C2 | Create folder | `⌘N` in folder list; also via automation | `Ctrl+N` in folder list; also via URI / CLI | MUST | #192 | **Partial** — M2-1 (#57): `Ctrl+N` in the folder list focuses the name box, and Create adds the folder. No URI or CLI (H-rows) **v0.2 score (2026-11-13):** Unchanged: Ctrl+N, the **+** button (#148) and the folder menu; no URI or CLI. |
| `[ ]` | C3 | Nesting depth | Flat, one level (no sub-folders documented) | **Flat, one level** | MUST | — | ⚠ provisional — INFERRED from absence. Met by construction (no `ParentId`; the UI is flat); scored when Q6 is resolved. Do not add nesting |
| `[x]` | C4 | Enter folder | `⌘↓`; double-click, or single-click if enabled | `Ctrl+Down`; same single-click setting | MUST | — | **Partial** — M2-2 (#60): `Ctrl+Down` and double-click. The single-click **setting** is deferred with #9 settings **v0.2 score (2026-11-13):** Ctrl+Down, and Settings › Open with a single click (`Invoke-Settings` case 7). |
| `[~]` | C5 | Leave folder / back | `⎋`; long-press back → recent folders | `Esc`; back control exposes recent folders | MUST | #193 | **Partial** — M2-2 (#60): `Esc` (A12 mode 1) and a visible Back control. Recent folders deferred — long-press is macOS-specific and no Windows equivalent is decided. A12's four behaviours landed with #16 slice 6 **v0.2 score (2026-11-13):** Unchanged: Back does not list recent folders. |
| `[ ]` | C6 | Switch to last folder | `⌘⌥O` | Same action, Windows chord | MUST | #193 | **v0.2 score (2026-11-13):** Not built. |
| `[~]` | C7 | Show all folders | Command to return to the folder list | Same, incl. from URI / CLI | MUST | #192 | **Partial** — the folder list is reachable via `Esc`/Back (M2-2). No explicit command, and no URI/CLI surface yet **v0.2 score (2026-11-13):** Unchanged: no URI or CLI. |
| `[x]` | C8 | Pin folders | Pinned folders stay at list top | Same | MUST | — | **v0.2 score (2026-11-13):** Pin and Unpin in the folder menu; pinned folders first (`Invoke-NoteStack` case 30). |
| `[x]` | C9 | Folder reordering | Drag & drop up / down | Same | MUST | — | **v0.2 score (2026-11-13):** Ctrl+Alt+Shift+Up/Down, Move to top/bottom and dragging (`Invoke-NoteStack` cases 36, 42). |
| `[ ]` | C10 | Folder sorting | Exists since 1.3; keys not enumerated | Sort by name / created / modified / manual | SHOULD | — | ⚠ provisional — UNKNOWN options |
| `[x]` | C11 | Delete folder | Confirmation popover | Soft delete to recycle bin, notes included | MUST | — | Consistent with B23 **v0.2 score (2026-11-13):** Delete moves the folder and its notes to the Recycle Bin; restore brings them back (`Invoke-RecycleBin`). |
| `[ ]` | C12 | Duplicate folder | Copy a folder with contents | Same | SHOULD | — | |
| `[x]` | C13 | Rename folder | Inline rename; Esc cancels | Inline rename; Esc cancels | MUST | — | **Done** — M2-1 (#57): `F2` starts an inline rename, `Enter` commits, `Esc` and focus loss cancel. See *C2 / C13* below |
| `[ ]` | C14 | Open last folder on restart | Setting | Same setting | SHOULD | — | |
| `[x]` | C15 | Empty folder guidance | Helper text in an empty folder | Same | SHOULD | — | **Partial** — an empty folder reads "This folder has no notes yet." It does not say how to add one. Principle 2: shown only in the empty state **v0.2 score (2026-11-13):** An empty folder reads "This folder has no notes yet. Press Ctrl+N or select New note to write one." |
| `[ ]` | C16 | Folder URL | `Copy URL` from context menu | `noto://` folder URI, copyable | ADAPT | — | See H5 |
| `[ ]` | C17 | Folder colors | Not documented | **Not a parity requirement** | DROP | — | Theme has folder *styling*, not per-folder color |
| `[ ]` | C18 | All-notes / favorites / archive | None documented | **Not a parity requirement** | DROP | — | Pinning + search are the documented access paths |
| `[x]` | C19 | Note count | Shown in the list bottom bar | Same | SHOULD | — | **v0.2 score (2026-11-13):** Each folder row shows its note count (#147); the list's bar shows the folder's. |

**C3 is the most important restraint in this document.** Every source describes
a flat folder → notes hierarchy; no sub-folder feature is documented anywhere.
The inventory labels this INFERRED-from-absence and instructs treating it as
flat unless disproven. Noto matches it. **Deeper nesting is not a parity
requirement and must not be added speculatively** — it is a second
organizational paradigm, which principle 9 defaults to no, and it would change
navigation, move, search scoping and URI shape simultaneously. If nesting is
ever wanted, it is an M7 candidate with its own justification.

**C17 and C18 are dropped for the same reason as B24:** absence of evidence in
the source. Note colors are CONFIRMED; folder colors are not. Pinning (B14,
C8) is the documented analogue of favorites, and cross-folder search (E1) is
the documented analogue of an all-notes view. Building either would be
inventing scope.

**C11 deviates deliberately.** SideNotes confirms-then-deletes; Noto soft
deletes. Folder deletion taking its notes with it into the recycle bin is the
only coherent behavior once B23 exists.

### C2 / C13 — the M2-1 Folder Pane interpretation

The M2-1 Folder Pane design gate had to decide how C2 and C13 are actually
driven. The distinction matters, so it is stated explicitly:

| | Source |
| --- | --- |
| `⌘N` creates a folder *in the folder list*; Noto's equivalent is `Ctrl+N` | **SideNotes documented capability** (C2) |
| Rename is inline, and `Esc` cancels it | **SideNotes documented capability** (C13) |
| `Ctrl+N` is scoped to the folder pane, not global | **Noto interpretation** — the global hotkey is a separate M2 surface, and principle 7 requires each activation surface to be independently disableable |
| `F2` also starts a rename | **Noto interpretation** — the Windows convention for inline rename; no macOS analogue exists to copy |
| Double-click does *not* start a rename | **Noto interpretation** — C4 reserves double-click for *enter folder* |
| Focus loss cancels a rename rather than committing it | **Noto interpretation** — C13 documents only Esc-cancel; committing on blur would persist a value the user may have abandoned |

**Folder names are deliberately not unique, and this is an engine rule rather
than a parity one.** The frozen engine contract makes duplicate folder names
legal (Case G), so `CreateFolder` and `RenameFolder` never return
`DuplicateName`. The Folder Pane therefore has **no duplicate-name error
state** — showing one would report a supported outcome as a failure. Tag names
are the opposite case and *are* unique; the two must not be generalised to each
other.

---

## 4. D — Editor & content

### 4.1 D.1 — Markdown & formatting

| Status | ID | Feature | SideNotes behavior | Noto requirement | Level | Issue | Notes |
| :----: | -- | ------- | ------------------ | ---------------- | :---: | :---: | ----- |
| `[x]` | D1 | Headers | `#`…`#####`, 5 levels; `⇧⌘H` | 5 levels; `Ctrl+Shift+H` | MUST | — | ADR-004 **v0.2 score (2026-11-13):** `#`–`######` render as headings; Ctrl+Shift+H cycles levels 1–3, deeper levels are typed. Scored from code and harness; owner to confirm. |
| `[x]` | D2 | Bold | `**text**`; `⌘B` | Same; `Ctrl+B` | MUST | — | **v0.2 score (2026-11-13):** `**text**`, Ctrl+B (`Invoke-Editor` case 5). |
| `[x]` | D3 | Italic | `*text*`; `⌘I` | Same; `Ctrl+I` | MUST | — | **v0.2 score (2026-11-13):** `*text*`, Ctrl+I. |
| `[x]` | D4 | Bold italic | `***text***` | Same | MUST | — | **v0.2 score (2026-11-13):** `***text***` is both (markdown-model.md). |
| `[x]` | D5 | Strikethrough | `~~text~~` | Same | MUST | — | **v0.2 score (2026-11-13):** `~~text~~`, Ctrl+Shift+E. |
| `[x]` | D6 | Mark / highlight | `::text::` (non-standard) | Same syntax, same rendering | SHOULD | — | Non-standard; stored verbatim, export stays lossless **v0.2 score (2026-11-13):** F8 (#151): `::text::`, drawn in the editor and on the card (ADR-017 amendment; `Invoke-Editor` case 21). |
| `[x]` | D7 | Underline | Added 1.6; syntax undocumented | Underline via shortcut + toolbar | SHOULD | — | ⚠ provisional — UNKNOWN syntax. Needs a syntax decision **v0.2 score (2026-11-13):** `==text==`, Ctrl+U, and the formatting toolbar (F8, #151). |
| `[x]` | D8 | Quote | `> text` | Same | MUST | — | **v0.2 score (2026-11-13):** `> quote`, styled in the editor and on the card. |
| `[x]` | D9 | Bullet list | `* item`; `⌘L` | `-` / `*`; `Ctrl+L` | MUST | — | **v0.2 score (2026-11-13):** `-` / `*` lists, Ctrl+L; Enter continues them (`Invoke-Editor` case 7). |
| `[x]` | D10 | Ordered list | `1. item`; `⇧⌘L` | Same; `Ctrl+Shift+L` | MUST | — | **v0.2 score (2026-11-13):** Ordered lists, Ctrl+Shift+L. |
| `[x]` | D11 | Task (unchecked) | `[ ]`; `⌘T` toggles line to task | `- [ ]`; `Ctrl+T` | MUST | — | ADR-004 uses GFM task lists **v0.2 score (2026-11-13):** `- [ ]`, Ctrl+T (`Invoke-Editor` case 8). |
| `[x]` | D12 | Task (checked) | `[x]`; `⌘.` toggles state | `- [x]`; `Ctrl+.` | MUST | — | **v0.2 score (2026-11-13):** `- [x]`, Ctrl+.; a click on the drawn box (`Invoke-Editor` case 16). |
| `[x]` | D13 | Inline code | `` `code` `` | Same | MUST | — | **v0.2 score (2026-11-13):** `` `code` ``, Ctrl+E. |
| `[x]` | D14 | Code block | ` ``` ` fenced | Fenced, with language | MUST | — | **v0.2 score (2026-11-13):** Fenced code with an info string (markdown-model.md); Code block in the Format menu. |
| `[x]` | D15 | Color swatch preview | `#rrggbb` renders a swatch | Same | SHOULD | — | **v0.2 score (2026-11-13):** F14 (#155): a swatch after each `#rrggbb` code on the card (`Invoke-Visual` case 16). |
| `[~]` | D16 | Horizontal separator | `---` or Aa menu | `---` + formatting menu | MUST | #194 | **v0.2 score (2026-11-13):** `---` renders as a separator; the Format menu has no separator command. |
| `[x]` | D17 | Inline links | `[text](url)` | Same; scheme validated before open | MUST | — | ADR-004 security: http/https/mailto/file only **v0.2 score (2026-11-13):** Links are clickable on the card and with Ctrl+Click in the editor, scheme-checked before opening (ADR-019; `Invoke-Links`). |
| `[ ]` | D18 | Tables | Absent from all documentation | **Not a parity requirement** | DROP | — | ⚠ provisional — UNKNOWN. ADR-004 defers tables |
| `[~]` | D19 | Indent / outdent | `⌘]` / `⌘[` | `Ctrl+]` / `Ctrl+[` | MUST | #194 | **v0.2 score (2026-11-13):** Tab and Shift+Tab indent list items (ADR-017 §10). Ctrl+] and Ctrl+[ are not bound. |
| `[x]` | D20 | Move line up / down | `⌘⌥[` / `⌘⌥]` | `Alt+Up` / `Alt+Down` | MUST | — | Windows editor convention; see G **v0.2 score (2026-11-13):** Alt+Up/Down move the lines (`Invoke-Editor` case 15). |
| `[ ]` | D21 | Clear formatting | Exists; chord undocumented | Strip formatting from selection | SHOULD | — | ⚠ provisional — UNKNOWN chord |
| `[x]` | D22 | Clear completed tasks | Deletes checked tasks; `⇧⌥⌘T` | Same; deleted text is undoable | MUST | — | **v0.2 score (2026-11-13):** F19 (#158): Ctrl+Shift+T (implementation plan §10.2) and the Format menu; one undo step (`Invoke-Editor` case 22). |
| `[ ]` | D23 | Completed tasks to bottom | Checked tasks auto-move to list bottom | Same, as a setting | SHOULD | — | |
| `[x]` | D24 | Show / hide markup | **Invisible markdown**, markup hidden by default; `⇧⌘R` | **Live-styled editing, markup hidden by default**; `Ctrl+Shift+R` toggles | MUST | — | **ADR-004 (revised): a parity MUST, not deferred** **v0.2 score (2026-11-13):** Markup hidden while editing; Ctrl+Shift+R shows it (ADR-017 §2; `Invoke-Editor` case 3). |
| `[~]` | D25 | Quick formatting toolbar | Floating toolbar on selection; above / below / disabled | Same three options | SHOULD | — | Mouse convenience; keyboard path exists already **v0.2 score (2026-11-13):** F8 (#151): a toolbar above the selection (below it with no room), on or off in Settings. Not the three options above / below / disabled. |
| `[x]` | D26 | Formatting menu (Aa) | Per-note menu of formatting commands | Equivalent menu; Noto's own layout | SHOULD | — | **v0.2 score (2026-11-13):** The card bar's Format menu lists every command with its key. |

**D24 is the single highest-risk row in this document and it is non-negotiable.**
ADR-004 was revised on 2026-09-14 specifically to move live-styled editing —
"invisible markdown" — out of M8 and into M3. SideNotes rebuilt its entire
editor in 1.5 to ship it, which is a realistic signal of the cost. A Windows
SideNotes that shows raw `**asterisks**` by default is not at parity however
good the rest is. Plain-source editing is a valid *intermediate* state during
M2; it is not an acceptable end state.

**The supported syntax is a deliberate subset, and matching it is the point.**
ADR-004's in-scope list and the inventory's D1–D17 agree almost exactly. Tables
(D18) are absent from SideNotes' documentation and deferred by ADR-004 — they
are therefore **not a parity gap**, and adding them during M3 would be scope
creep dressed as completeness.

**D6 (`::mark::`) is non-standard markdown** and will not render in other
tools. It is a SHOULD rather than MUST for that reason, and because it is
stored verbatim in the markdown source, export remains lossless regardless
(ADR-002, ADR-004).

**D7 needs a decision before it can be built.** SideNotes added underline in
1.6 but published neither syntax nor shortcut. Noto must pick a representation
that survives round-tripping to plain markdown. Listed in section 13.

**D20 deviates from a literal mapping on purpose.** `⌘⌥[` / `⌘⌥]` translates
to `Ctrl+Alt+[` / `Ctrl+Alt+]`, which no Windows user would guess. `Alt+Up` /
`Alt+Down` is the Visual Studio / VS Code convention for exactly this action
and is what the target audience already has in their fingers.

### 4.2 D.2 — Note modes

| Status | ID | Feature | SideNotes behavior | Noto requirement | Level | Issue | Notes |
| :----: | -- | ------- | ------------------ | ---------------- | :---: | :---: | ----- |
| `[ ]` | D27 | Code mode | Per-note monospace mode; spell check off | Per-note code mode; monospace; spell check off | MUST | #195 | **v0.2 score (2026-11-13):** Not built. |
| `[ ]` | D28 | Three note modes | code / plain text / markdown; global default | Same three modes + a global default | SHOULD | — | ⚠ provisional — INFERRED naming (Setapp listing) |

**D27 is CONFIRMED and D28 is not.** Code mode is documented directly by
Apptorium; the *three-way* mode set appears only in a Setapp listing. Noto
implements code mode as a MUST and the three-way default as a SHOULD. All three
modes share one storage format (markdown source, ADR-004) — the mode affects
rendering and spell check, never what is written to disk, so this costs nothing
architecturally.

### 4.3 D.3 — Content types

| Status | ID | Feature | SideNotes behavior | Noto requirement | Level | Issue | Notes |
| :----: | -- | ------- | ------------------ | ---------------- | :---: | :---: | ----- |
| `[~]` | D29 | Images | Embed inline; `⇧⌘P`, drag, paste | Same; `Ctrl+Shift+P`, drag, paste | MUST | #194 | Attachment store, M1 **v0.2 score (2026-11-13):** Pictures are added by drop and paste and drawn in the note (ADR-019; `Invoke-Images`). No Ctrl+Shift+P. |
| `[x]` | D30 | Screenshots | Screenshots as note content via drop | Same via drop and paste | MUST | — | Capture API is M5, not parity **v0.2 score (2026-11-13):** A screenshot on the clipboard is pasted as a picture; a dropped picture file is kept (`Invoke-Images` cases 9–12). |
| `[ ]` | D31 | Image from iPhone/iPad | Continuity Camera | **No equivalent** | DROP | — | Apple Continuity. No Windows analogue |
| `[ ]` | D32 | Copy image quickly | Copy an embedded image to clipboard | Same | SHOULD | — | |
| `[ ]` | D33 | Save image to file | Export an embedded image | Same | SHOULD | — | |
| `[~]` | D34 | File & folder shortcuts | Drop a file → shortcut with preview; standard / compact | Same; both preview styles; double-click opens | MUST | #196 | Path validation, principle 10 **v0.2 score (2026-11-13):** A dropped or pasted file becomes a link to its path, opened with Ctrl+Click (ADR-019). No preview styles, no double-click. |
| `[ ]` | D35 | Quick Look preview | Space previews an image or file | **In-app preview** on Space for images; Explorer preview otherwise | ADAPT | — | Quick Look is a macOS system service |
| `[x]` | D36 | Text snippets | Notes as reusable snippets via search + quick copy | Same pattern; no separate snippet feature | MUST | — | Emergent from E3, not new machinery **v0.2 score (2026-11-13):** Search plus Ctrl+C / Ctrl+Shift+C (E3) makes any note a snippet. |
| `[~]` | D37 | URL handling | URLs clickable; `⌘R` launches from search | Clickable, scheme-validated; `Ctrl+R` from search | MUST | #197 | ADR-004 security rules **v0.2 score (2026-11-13):** Clickable and scheme-validated. No Ctrl+R from Search. |
| `[~]` | D38 | Save web address quickly | Via the system Share extension | Clipboard capture + URI protocol | ADAPT | — | No Windows share sheet equivalent **v0.2 score (2026-11-13):** A note from the clipboard (F18, #157). No URI protocol. |
| `[ ]` | D39 | Code detection on paste | Detects pasted code → code block or code note | Same, with the same setting | SHOULD | — | |
| `[x]` | D40 | Better pasting from ChatGPT | Cleanup of pasted LLM output | Sane paste cleanup for HTML/rich sources | SHOULD | — | Generalized; no per-product special case **v0.2 score (2026-11-13):** Ctrl+V pastes a page's HTML as Markdown, scripts and styles dropped (ADR-017 §10; `Invoke-Editor` case 17). |
| `[ ]` | D41 | No auto-spaces on paste | Setting | Same setting | SHOULD | — | |
| `[ ]` | D42 | Smarter pasted links | Resolves link titles when accessible | Same, **off by default** | SHOULD | — | Title resolution is a network call — principle 4 and 10 |
| `[~]` | D43 | Paste clipboard as note | Note from clipboard contents | Same (= B3) | MUST | #187 | Duplicate of B3 **v0.2 score (2026-11-13):** As B3: a global shortcut and the tray menu, no **+** menu. |
| `[ ]` | D44 | Text replacement | macOS system Text Replacement works in notes | **No equivalent; do not build one** | DROP | — | Windows has no system-wide equivalent service |
| `[~]` | D45 | Spell check | Active in notes; off in code mode | Windows spell check; off in code mode | ADAPT | — | Windows spell-check API, not NSSpellChecker **v0.2 score (2026-11-13):** Windows spell check is on in the editor. There is no code mode to turn it off in (D27). |
| `[ ]` | D46 | Word count | Not documented | **Not a parity requirement** | DROP | — | Note count (C19) exists; word count does not |

**D34 is a security surface as well as a feature.** File and folder shortcuts
store user paths and open them on double-click. Principle 10 requires every
path validated, and the logging rule forbids recording user file paths. This
row does not ship without both.

**D35 and D38 are the two ADAPTs in this group and they resolve differently.**
Quick Look has a clean Windows outcome — press Space, see the thing — reachable
by an in-app image preview plus Explorer's own preview for other file types.
The Share extension does not: Windows has no equivalent system share sheet, so
the *outcome* (capture the current web address into a note fast) is delivered
by clipboard capture and the `noto://` protocol instead. Neither adapts the
mechanism; both preserve the outcome.

**D42 is the one row where Noto is deliberately more conservative than
SideNotes.** Resolving a link's title means fetching the URL. Principle 4 says
Noto works completely offline, and principle 10 says notes are sensitive by
default — a paste that silently phones out to a host named in the clipboard is
not acceptable as a default. The feature ships; the default is off.

**D44 is dropped rather than adapted** because the outcome it delivers is a
system-wide OS service, not an app feature. Building a snippet-expansion engine
inside Noto to replace it would be a new subsystem to maintain for years
(principle 9) that no source asks for.

### 4.4 D.4 — Typography

| Status | ID | Feature | SideNotes behavior | Noto requirement | Level | Issue | Notes |
| :----: | -- | ------- | ------------------ | ---------------- | :---: | :---: | ----- |
| `[x]` | D47 | Font family | Any installed font, across UI elements | Font family selection; token-driven (ADR-011) | MUST | — | **v0.2 score (2026-11-13):** F13 (#154): Settings › Font, from a closed list of Windows fonts, token-driven. |
| `[x]` | D48 | Font size | `⌘+` / `⌘−` | `Ctrl+=` / `Ctrl+-`, persisted | MUST | — | **v0.2 score (2026-11-13):** F13 (#154): Settings › Text size, Ctrl+= and Ctrl+-, stored (`Invoke-Settings` case 16). |
| `[ ]` | D49 | Line height | Tight / Normal / Relaxed | The same three presets | SHOULD | — | |
| `[ ]` | D50 | Paragraph spacing | Tight / Normal / Relaxed | The same three presets | SHOULD | — | |
| `[x]` | D51 | Missing-font fallback | Crash fixed in 1.6.1 | Font fallback; never fails on a missing font | MUST | — | A bug in SideNotes is a requirement here **v0.2 score (2026-11-13):** F13 (#154): a chosen family is always followed by the design system's, so a missing font falls back. |

**D51 is included because the inventory recorded the crash.** A user-selected
font can be uninstalled between sessions. SideNotes crashed on it once; Noto
must fall back deterministically and say nothing (principle 6).

---

## 5. E — Search

| Status | ID | Feature | SideNotes behavior | Noto requirement | Level | Issue | Notes |
| :----: | -- | ------- | ------------------ | ---------------- | :---: | :---: | ----- |
| `[x]` | E1 | Search window | Searches notes **and** folders, cross-folder; `⌘F` | Same scope; `Ctrl+F` | MUST | — | FTS5, ADR-003 **v0.2 score (2026-11-13):** Ctrl+F searches notes and folders across folders (ADR-018; `Invoke-Search`). |
| `[x]` | E2 | Global search shortcut | `⌃⌥⌘F` recommended, configurable; works over full-screen apps | Configurable global hotkey; works when unfocused | MUST | — | **v0.2 score (2026-11-13):** F21 (#159): a global shortcut shows Noto with Search open; off by default, Ctrl+Shift+Win+F (implementation plan §10.1), rebindable (`Invoke-Settings` case 19). |
| `[x]` | E3 | Quick copy from results | `⌘C` copies all; `⌘⌥C` excludes line 1; window closes, focus returns | `Ctrl+C` / `Ctrl+Shift+C`; same close-and-return-focus | MUST | — | Focus return is the whole point **v0.2 score (2026-11-13):** In Search, Ctrl+C copies a result and Ctrl+Shift+C copies it without its title; Search closes and Noto hides, focus back to the app (ADR-018). |
| `[ ]` | E4 | Launch URL from result | Title on line 1, URL on line 2 → `⌘R` opens it | `Ctrl+R`, scheme-validated | SHOULD | — | |
| `[ ]` | E5 | Open containing folder | Modifier+click on a result | `Ctrl+Click` on a result | SHOULD | — | ⚠ provisional — UNKNOWN modifier (`⌃` vs `⌘`) |
| `[ ]` | E6 | Search via automation | AppleScript `search`/`search notes`/`search folders` | Search via `noto://` URI and CLI | ADAPT | — | See H3 |
| `[ ]` | E7 | Search via Shortcuts | Find Notes / Find Folders actions | Covered by the CLI | ADAPT | — | See H4 |
| `[x]` | E8 | Live search / filters / in-note find | Not documented | Incremental results as you type | MUST | — | ⚠ provisional — UNKNOWN in SideNotes; required by principle 3 **v0.2 score (2026-11-13):** Results update as you type (ADR-018; `Invoke-Search`). |

**E3 is not a copy shortcut, it is the snippet workflow.** SideNotes' documented
use as a snippet store (D36) depends entirely on this behavior: search, copy,
window closes, the *previously frontmost app regains focus* so the paste lands
where the user was working. Implementing the copy without the focus return
delivers none of the value. This is one of the few rows where the exact
interaction sequence, not just the capability, is the requirement.

**E3's `⌘⌥C` maps to `Ctrl+Shift+C`, not `Ctrl+Alt+C`.** See section 7 — the
literal `Alt` mapping collides with nothing standard but is awkward; `Ctrl+Shift+C`
is the conventional "copy variant" chord on Windows.

**E8 is MUST despite being UNKNOWN in SideNotes.** The inventory could not
confirm whether SideNotes' search is incremental. Noto requires it regardless,
because principle 3 states search results must be "as fast as typing" — a
search that requires pressing Enter fails that budget by construction. Filters
and in-note find are *not* pulled in with it; neither is documented, and
neither is required by a principle.

---

## 6. F — Appearance & themes

| Status | ID | Feature | SideNotes behavior | Noto requirement | Level | Issue | Notes |
| :----: | -- | ------- | ------------------ | ---------------- | :---: | :---: | ----- |
| `[x]` | F1 | Light / dark / system | Follow system, or set manually | Same three states; follows Windows theme | MUST | — | **Partial** — Light, Dark and High Contrast follow the Windows theme (`Noto.UI` ThemeDictionaries, #22). No manual choice (needs the settings UI); Light and High Contrast not yet visually reviewed **v0.2 score (2026-11-13):** Settings › Theme: System, Light, Dark; System follows Windows live (F2, #145; `Invoke-Settings` case 3). |
| `[ ]` | F2 | UI themes | Installable color themes | Installable color themes | SHOULD | — | Built on ADR-011 tokens |
| `[ ]` | F3 | Custom theme files | `.sntheme` = JSON, schema-documented, editable in any editor | JSON theme file + published schema | SHOULD | — | Format is Noto's own |
| `[ ]` | F4 | Theme structure | `light` / `dark` / `any`; sections incl. notes.clean + 6 colors | Equivalent structure; must cover the six note colors | SHOULD | — | Structure follows ADR-011, not SideNotes' |
| `[ ]` | F5 | Theme color properties | background, text ×3, link, button, quote, mark, code, checkmark, preview | Token set covering the same surfaces | SHOULD | — | |
| `[ ]` | F6 | Theme color formats | `#rgb`, `#rrggbb`, `#rrggbbaa` | Same three | SHOULD | — | |
| `[ ]` | F7 | Theme installation | Double-click a theme file to install; manage + reveal in Finder | Double-click to install; manage + reveal in Explorer | SHOULD | — | File association needs the package (ADR-008) |
| `[ ]` | F8 | Theme gallery | Vendor-hosted downloadable gallery | **Not in M3** | DROP | — | A website, not an app feature |
| `[ ]` | F9 | Theme via automation | AppleScript / Shortcuts set theme | Set theme via CLI | ADAPT | — | |
| `[ ]` | F10 | Note color display style | Background (default) / Bar | Same two styles | MUST | #191 | **v0.2 score (2026-11-13):** Not built: one style only (#150). |
| `[ ]` | F11 | Transparency | Open Bar semi-transparent; theme alpha supported | Alpha in themes; edge handle translucent | SHOULD | — | ⚠ provisional — UNKNOWN whether a window opacity setting exists |
| `[ ]` | F12 | Density | No global density setting; line height + compact file links only | Same — no global density setting | DROP | — | ⚠ provisional — INFERRED. Do not invent one |
| `[ ]` | F13 | Icon refresh | Icons refreshed across releases | **Not applicable** | DROP | — | Noto has its own icon set (ADR-011) |
| `[ ]` | F14 | Disable color swatches | Setting to hide `#rrggbb` swatches | Same setting | SHOULD | — | Pairs with D15 |

**F1 and F10 are the only MUSTs here, and that is intentional.** Light/dark
following the Windows theme is a principle-8 requirement, and the six note
colors (B15) are meaningless without their two display styles. Everything else
in this category is theming *depth* — genuinely present in SideNotes, genuinely
not what makes a notes panel a notes panel.

**F2–F7 are one piece of work, not six.** They describe a single capability:
a documented JSON theme format with an installer. Noto's theme file is built on
ADR-011's design tokens, so its structure is Noto's own — copying SideNotes'
section names would couple Noto's design system to a competitor's internals for
no user benefit. Parity here is "users can install and author themes", not
"the file looks the same".

**F8, F12, F13 are dropped as non-features.** A hosted gallery is a website.
Density is not a documented SideNotes setting at all (the inventory found only
line-height presets and compact file links, and labels the density row
INFERRED). Icon refreshes are release notes, not capabilities.

---

## 7. G — Keyboard shortcuts

47 documented SideNotes chords. Mapping rule: **⌘→Ctrl, ⌥→Alt, ⇧→Shift,
⌃→(nothing; folded into the global chord)**. Literal mapping is the default;
deviations are marked **↯** and justified below the table.

Global chords use `Ctrl+Alt+Win+<key>` where SideNotes uses `⌃⌥⌘<key>` —
Windows has no fourth modifier, and `Ctrl+Alt+Shift` collides with too much.

### 7.1 Global (work when Noto is hidden) — all configurable

| Status | ID | Action | SideNotes | Noto (proposed) | Level | Issue | Notes |
| :----: | -- | ------ | --------- | --------------- | :---: | :---: | ----- |
| `[x]` | G1 | Show / hide app | `⌃⌥⌘␣` | `Ctrl+Alt+Win+Space` | MUST | — | **Done** — #16 slices 4–5: shows and hides from any app, on by default. Rebinding is G50. ⚠ may collide with IME switchers |
| `[x]` | G2 | New note in current folder | `⌃⌥⌘N` | `Ctrl+Alt+Win+N` | MUST | — | **v0.2 score (2026-11-13):** As B2; the chord is Ctrl+Shift+Win+N, the implementation plan's §10.1 final default. |
| `[x]` | G3 | Search | `⌃⌥⌘F` | `Ctrl+Alt+Win+F` | MUST | — | **v0.2 score (2026-11-13):** As E2; the chord is Ctrl+Shift+Win+F (§10.1). |
| `[x]` | G4 | Note from clipboard | configurable, no default | `Ctrl+Alt+Win+V` | MUST | — | Noto proposes a default **v0.2 score (2026-11-13):** F18 (#157); the chord is Ctrl+Shift+Win+V (§10.1). |
| `[ ]` | G5 | Change side | configurable | unbound by default | SHOULD | — | |
| `[x]` | G6 | Pin window | configurable | unbound by default | SHOULD | — | **v0.2 score (2026-11-13):** Owner decision (#146): Ctrl+D inside Noto, no global chord (§10.1). |

### 7.2 Window / navigation

| Status | ID | Action | SideNotes | Noto (proposed) | Level | Issue | Notes |
| :----: | -- | ------ | --------- | --------------- | :---: | :---: | ----- |
| `[x]` | G7 | Close panel | `⌘W` | `Ctrl+W` | MUST | — | **Done** — = A10 |
| `[x]` | G8 | Back / hide (4 modes) | `⎋` | `Esc` | MUST | — | **Done** — = A12 |
| `[x]` | G9 | Enter folder | `⌘↓` | `Ctrl+Down` | MUST | — | **Done** — M2-2 (#60). Same code as C4, whose `[~]` is the single-click setting |
| `[ ]` | G10 | Last-open folder | `⌘⌥O` | `Ctrl+Alt+O` | MUST | #193 | **v0.2 score (2026-11-13):** Not built (§10.2 final chord Ctrl+Shift+O). |
| `[x]` | G11 | Next note | `⌘⌥↓` | `Ctrl+Alt+Down` | MUST | — | **v0.2 score (2026-11-13):** Ctrl+Alt+Down. |
| `[x]` | G12 | Previous note | `⌘⌥↑` | `Ctrl+Alt+Up` | MUST | — | **v0.2 score (2026-11-13):** Ctrl+Alt+Up. |
| `[ ]` | G13 | Hide (optional binding) | `⌘↩` | `Ctrl+Enter` | SHOULD | — | Off by default; see A11 |

### 7.3 Note management

| Status | ID | Action | SideNotes | Noto (proposed) | Level | Issue | Notes |
| :----: | -- | ------ | --------- | --------------- | :---: | :---: | ----- |
| `[x]` | G14 | New note / new folder | `⌘N` | `Ctrl+N` | MUST | — | **Partial** — context-dependent: the folder list focuses the folder name box, the note list creates and opens a note (#63, #87). Unbound in the editor **v0.2 score (2026-11-13):** Ctrl+N, also while editing a card (`Invoke-NoteStack` case 7). |
| `[x]` | G15 | Delete note | `⌥⌘⌫` | `Ctrl+Alt+Backspace` ↯ | MUST | — | **Partial** — works in the editor (#63), a soft delete; not from the note list. Order swapped for Windows convention. B7 stays `[ ]` until B23's recycle bin has a UI **v0.2 score (2026-11-13):** Ctrl+Alt+Backspace (`Invoke-NoteStack` case 11). |
| `[x]` | G16 | Move note to folder | `⇧⌘M` | `Ctrl+Shift+M` | MUST | — | **v0.2 score (2026-11-13):** Ctrl+Shift+M (`Invoke-NoteStack` cases 19–21). |
| `[x]` | G17 | Move note up | `⇧⌥⌘↑` | `Ctrl+Alt+Shift+Up` | MUST | — | **v0.2 score (2026-11-13):** Ctrl+Alt+Shift+Up. |
| `[x]` | G18 | Move note down | `⇧⌥⌘↓` | `Ctrl+Alt+Shift+Down` | MUST | — | **v0.2 score (2026-11-13):** Ctrl+Alt+Shift+Down. |
| `[x]` | G19 | Fold note | `⌥⌘←` | `Ctrl+Alt+Left` | MUST | — | **v0.2 score (2026-11-13):** Ctrl+Alt+Left (`Invoke-NoteStack` case 23). |
| `[x]` | G20 | Unfold note | `⌥⌘→` | `Ctrl+Alt+Right` | MUST | — | **v0.2 score (2026-11-13):** Ctrl+Alt+Right. |
| `[x]` | G21 | Fold all | `⇧⌥⌘←` | `Ctrl+Alt+Shift+Left` | MUST | — | **v0.2 score (2026-11-13):** Ctrl+Alt+Shift+Left (`Invoke-NoteStack` case 27). |
| `[x]` | G22 | Unfold all | `⇧⌥⌘→` | `Ctrl+Alt+Shift+Right` | MUST | — | **v0.2 score (2026-11-13):** Ctrl+Alt+Shift+Right. |
| `[x]` | G23 | Set note color 1–6 | `⌘1`…`⌘6` | `Ctrl+1`…`Ctrl+6` | MUST | — | **v0.2 score (2026-11-13):** F7 (#150): Ctrl+1–Ctrl+6 (`Invoke-NoteStack` case 46). |
| `[x]` | G24 | Clear note color | `⌘0` | `Ctrl+0` | MUST | — | ⚠ collides with "reset zoom" convention **v0.2 score (2026-11-13):** F7 (#150): Ctrl+0. |
| `[ ]` | G25 | Print note | `⌘P` | `Ctrl+P` | SHOULD | — | |
| `[ ]` | G26 | Pin note | none documented | `Ctrl+Shift+P` ↯ | SHOULD | — | Collides with D29 insert-picture — unresolved |

### 7.4 Editing

| Status | ID | Action | SideNotes | Noto (proposed) | Level | Issue | Notes |
| :----: | -- | ------ | --------- | --------------- | :---: | :---: | ----- |
| `[x]` | G27 | Bold | `⌘B` | `Ctrl+B` | MUST | — | **v0.2 score (2026-11-13):** Ctrl+B. |
| `[x]` | G28 | Italic | `⌘I` | `Ctrl+I` | MUST | — | **v0.2 score (2026-11-13):** Ctrl+I. |
| `[x]` | G29 | Header | `⇧⌘H` | `Ctrl+Shift+H` | MUST | — | **v0.2 score (2026-11-13):** Ctrl+Shift+H. |
| `[x]` | G30 | Bullet list | `⌘L` | `Ctrl+L` | MUST | — | **v0.2 score (2026-11-13):** Ctrl+L. |
| `[x]` | G31 | Ordered list | `⇧⌘L` | `Ctrl+Shift+L` | MUST | — | **v0.2 score (2026-11-13):** Ctrl+Shift+L. |
| `[x]` | G32 | Create / remove task | `⌘T` | `Ctrl+T` | MUST | — | **v0.2 score (2026-11-13):** Ctrl+T. |
| `[x]` | G33 | Toggle task state | `⌘.` | `Ctrl+.` | MUST | — | **v0.2 score (2026-11-13):** Ctrl+. |
| `[x]` | G34 | Clear completed tasks | `⇧⌥⌘T` | `Ctrl+Alt+Shift+T` | MUST | — | **v0.2 score (2026-11-13):** F19 (#158): Ctrl+Shift+T, the §10.2 final chord. |
| `[ ]` | G35 | Insert picture | `⇧⌘P` | `Ctrl+Shift+P` | MUST | #194 | ⚠ collides with G26 and with command-palette convention **v0.2 score (2026-11-13):** Not built. |
| `[ ]` | G36 | Indent right | `⌘]` | `Ctrl+]` | MUST | #194 | Tab also indents in lists **v0.2 score (2026-11-13):** Not bound; Tab indents list items. |
| `[ ]` | G37 | Indent left | `⌘[` | `Ctrl+[` | MUST | #194 | Shift+Tab also outdents **v0.2 score (2026-11-13):** Not bound; Shift+Tab outdents list items. |
| `[x]` | G38 | Move line up | `⌘⌥[` | `Alt+Up` ↯ | MUST | — | VS/VS Code convention **v0.2 score (2026-11-13):** Alt+Up. |
| `[x]` | G39 | Move line down | `⌘⌥]` | `Alt+Down` ↯ | MUST | — | VS/VS Code convention **v0.2 score (2026-11-13):** Alt+Down. |
| `[x]` | G40 | Show / hide markup | `⇧⌘R` | `Ctrl+Shift+R` | MUST | — | ⚠ browser hard-reload muscle memory; in-app only **v0.2 score (2026-11-13):** Ctrl+Shift+R. |
| `[x]` | G41 | Increase font size | `⌘+` | `Ctrl+=` / `Ctrl++` | MUST | — | **v0.2 score (2026-11-13):** F13 (#154): Ctrl+= and Ctrl++, keypad + too. |
| `[x]` | G42 | Decrease font size | `⌘−` | `Ctrl+-` | MUST | — | **v0.2 score (2026-11-13):** F13 (#154): Ctrl+-, keypad - too. |
| `[ ]` | G43 | Share menu | `⌘↩` | tray / note menu, no chord ↯ | ADAPT | — | No Windows share sheet; see H2 |
| `[ ]` | G44 | Preview image / file | `Space` | `Space` | ADAPT | — | In-app preview; see D35 |

### 7.5 Search window

| Status | ID | Action | SideNotes | Noto (proposed) | Level | Issue | Notes |
| :----: | -- | ------ | --------- | --------------- | :---: | :---: | ----- |
| `[x]` | G45 | Open search | `⌘F` | `Ctrl+F` | MUST | — | **v0.2 score (2026-11-13):** Ctrl+F, and the header's Search button (#148). |
| `[x]` | G46 | Copy note (incl. line 1) | `⌘C` | `Ctrl+C` | MUST | — | **v0.2 score (2026-11-13):** Ctrl+C on a search result (E3). |
| `[x]` | G47 | Copy note (excl. line 1) | `⌘⌥C` | `Ctrl+Shift+C` ↯ | MUST | — | Conventional copy-variant chord **v0.2 score (2026-11-13):** Ctrl+Shift+C on a search result (E3). |
| `[ ]` | G48 | Launch URL from line 2 | `⌘R` | `Ctrl+R` | SHOULD | — | |
| `[ ]` | G49 | Open containing folder | `⌃`/`⌘ + Click` | `Ctrl+Click` | SHOULD | — | ⚠ provisional — sources differ |

### 7.6 Customization

| Status | ID | Feature | SideNotes behavior | Noto requirement | Level | Issue | Notes |
| :----: | -- | ------- | ------------------ | ---------------- | :---: | :---: | ----- |
| `[~]` | G50 | Rebind global shortcuts | Record-shortcut UI for the global set | Same, for the global set | MUST | #198 | **v0.2 score (2026-11-13):** Four global shortcuts are rebindable in Settings: show and hide, new note, note from clipboard, search. No global change-side shortcut. |
| `[x]` | G51 | Escape behavior setting | 4 options | Same 4 options | MUST | — | The four options exist and are stored (A12); no settings UI to choose one. Re-scored 2026-10-13 from `[~]`: this row is the setting, and no user can reach it **v0.2 score (2026-11-13):** Settings › Escape, four options (`Invoke-Settings` case 5). |
| `[ ]` | G52 | Rebind in-app shortcuts | Undocumented whether possible | Rebindable, with conflict detection | SHOULD | — | ⚠ provisional — UNKNOWN in SideNotes |

### Collisions with standard Windows shortcuts

```
  CHORD                  COLLIDES WITH                          RESOLUTION
  ─────────────────────  ─────────────────────────────────────  ──────────────────────
  Ctrl+Alt+Win+Space     IME / input-method switchers (varies)  configurable; test first
  Ctrl+0          (G24)  "reset zoom" in almost every app       keep; Noto has no zoom
  Ctrl+Shift+P    (G35)  command palette (VS Code, Explorer-ish) keep; in-app only
  Ctrl+Shift+P    (G26)  ALSO G35 insert-picture — INTERNAL      UNRESOLVED — see below
  Ctrl+Shift+R    (G40)  browser hard reload (muscle memory)     keep; in-app only
  Ctrl+[ / Ctrl+] (G36/7) Esc-equivalent in some terminals       keep; in-app only
  Ctrl+Alt+<key>         AltGr on many non-US layouts            MUST verify on DE/FR/PL
  Ctrl+W          (G7)   "close document" everywhere             correct — it closes the panel
```

**One internal collision is unresolved and must be decided before G26/G35 are
built.** Pin-note and insert-picture cannot both be `Ctrl+Shift+P`. Insert
picture (G35) has the stronger claim — it maps literally from `⇧⌘P` and is
CONFIRMED in SideNotes; pin-note has no documented chord at all. Provisional
resolution: G35 keeps `Ctrl+Shift+P`, G26 takes an unbound-by-default slot.
Recorded in section 13.

**`Ctrl+Alt+<key>` is the systemic risk, not any individual chord.** On German,
French, Polish and several other keyboard layouts, `AltGr` is delivered to
applications as `Ctrl+Alt`, so every `Ctrl+Alt+<letter>` chord can swallow a
character the user was trying to type. Eight rows use it (G10, G11, G12, G15,
G17–G22, G34). This must be tested on non-US layouts during M3, and G52's
rebinding is the mitigation of last resort. SideNotes does not have this
problem because macOS has no AltGr.

**The deviations (↯) are five, and each has one reason.** G15 reorders modifiers
to Windows convention. G38/G39 adopt the editor convention the target audience
already knows. G47 uses the conventional copy-variant chord. G43 has no chord
because it has no Windows equivalent to bind one to.

---

## 8. H — Integration & automation

| Status | ID | Feature | SideNotes behavior | Noto requirement | Level | Issue | Notes |
| :----: | -- | ------- | ------------------ | ---------------- | :---: | :---: | ----- |
| `[x]` | H1 | Menu bar item | Menu-bar icon + right-click menu | **System tray** icon + context menu | ADAPT | — | = A5, A6; scored with them: partial while A5 is (#16 slice 7) **v0.2 score (2026-11-13):** The tray icon and its menu: Show, Hide, Keep Noto open, Start with Windows, New note from clipboard, Settings, Quit (`Invoke-Tray`). |
| `[~]` | H2 | Share extension | System share sheet; also a web-address share extension | **Copy / save-as / open-with**, no share sheet | ADAPT | — | Windows has no NSSharingService analogue **v0.2 score (2026-11-13):** Copy (Search's Ctrl+C) and save-as (Export as Markdown… on a note or folder). No open-with. |
| `[ ]` | H3 | AppleScript API | Full scripting API over folders and notes | **`noto://` URI + CLI (+ PowerShell module)** | ADAPT | — | ADR-010 makes this cheap |
| `[ ]` | H4 | Apple Shortcuts actions | 12+ automation actions | **CLI is the automation surface**; no GUI-flow integration | ADAPT | — | No Windows equivalent worth targeting |
| `[ ]` | H5 | URL scheme | `sidenotes://open/<uuid>`, `add-note-with-text/<text>` | `noto://` with the same two capabilities | MUST | #192 | Registered protocol handler; ADR-008 **v0.2 score (2026-11-13):** Not built. |
| `[ ]` | H6 | Alfred workflow | Launcher workflow | **Not in M3** — CLI enables it | DROP | — | Third party writes the plugin, not Noto |
| `[ ]` | H7 | Hookmark | macOS-only linking app | **No equivalent** | DROP | — | Mac-only product |
| `[ ]` | H8 | PopClip | macOS-only | **No equivalent** | DROP | — | Mac-only product |
| `[ ]` | H9 | Dropzone | macOS-only | **No equivalent** | DROP | — | Mac-only product |
| `[ ]` | H10 | Raycast | Primarily macOS | **Not in M3** | DROP | — | Windows version emerging; third-party plugin if ever |
| `[ ]` | H11 | Workspaces | Apptorium's own Mac app | **Not applicable** | DROP | — | Vendor's own product |
| `[ ]` | H12 | MindNode | Mind maps from notes | **No equivalent** | DROP | — | Mac/iOS-only product |
| `[ ]` | H13 | Things | Tasks from notes via Shortcuts | **No equivalent** | DROP | — | Apple-platform-only product |
| `[~]` | H14 | Drag from other apps | Text, files, folders, images from any app | Same, via Windows OLE drag & drop | MUST | #31 | Explorer is the main source **v0.2 score (2026-11-13):** As B4: OLE file drops are verified; other apps are the owner's check. Scored from code and harness; owner to confirm. |
| `[~]` | H15 | Clipboard behaviors | Note-from-clipboard, quick copy, copy image, code detect, link titles, no auto-space | All six (= B3, E3, D32, D39, D42, D41) | MUST | #187 | Aggregate row **v0.2 score (2026-11-13):** Of the six: note from clipboard (partly, B3) and quick copy (E3). |
| `[ ]` | H16 | Dropshare target | Mac share destination | **No equivalent** | DROP | — | Mac-only product |
| `[ ]` | H17 | Handoff / Continuity | Not documented | **Not applicable** | DROP | — | Apple-only concept |
| `[ ]` | H18 | Services menu | Not documented | **Not applicable** | DROP | — | macOS-only concept |

**Eleven of eighteen rows here are DROP, and that is the correct answer.**
SideNotes' integration surface is largely a list of Mac software — Hookmark,
PopClip, Dropzone, MindNode, Things, Dropshare, Workspaces, Alfred. These are
not features SideNotes built; they are products that exist on macOS and do not
exist on Windows. "Noto lacks Hookmark integration" is not a parity gap, it is
a statement about Hookmark. See [section 11](#11-where-noto-deliberately-differs).

**H3, H4 and H5 collapse into one piece of work.** ADR-010 requires every
mutation to go through a command, so a URI handler, a CLI and a future
automation surface all dispatch the same commands rather than reimplementing
logic per entry point. That is why the automation ADAPT is cheap: the command
layer already has to exist, and the CLI is a thin shell over it. H5 is the MUST
here because it is the only genuinely portable concept of the three; H3 and H4
are ADAPT because the *outcome* (script Noto from outside) is preserved while
the mechanism is entirely different.

**H2 needs honesty about what is lost.** SideNotes' share sheet reaches Messages,
Notes, Reminders and dozens of installed apps with zero per-app work. Windows'
share contract requires packaging and reaches far less. Noto's answer is the
primitives — copy, save as image, save as markdown, open with — which covers
the documented use cases (D38, I4) without pretending to be a share sheet.

---

## 9. I — Import / export / backup / sync

| Status | ID | Feature | SideNotes behavior | Noto requirement | Level | Issue | Notes |
| :----: | -- | ------- | ------------------ | ---------------- | :---: | :---: | ----- |
| `[x]` | I1 | Import text files | Drop text files, or whole folders, to create notes | Same, incl. bulk folder import | MUST | — | **v0.2 score (2026-11-13):** Settings › Import brings in a folder of Markdown; dropped `.md` / `.txt` files become notes (ADR-020; F9, #152). |
| `[x]` | I2 | Import images | Dropped images embedded; behavior configurable | Same | MUST | — | **v0.2 score (2026-11-13):** A dropped picture becomes a note holding it; an imported folder brings the pictures its notes use (F9, #152; ADR-020). |
| `[~]` | I3 | Import as shortcuts | Files become previewed shortcuts, not content | Same; standard / compact styles | MUST | #196 | = D34 **v0.2 score (2026-11-13):** A file dropped into a note becomes a link to it. No preview styles. |
| `[ ]` | I4 | Export note to image | Share → save/copy image; background, margins, ratio, bottom bar | Same, with the same four export settings | SHOULD | — | |
| `[ ]` | I5 | Export via Shortcuts | Shortcuts action wrapping image export | Image export via CLI | ADAPT | — | |
| `[ ]` | I6 | Print | `⌘P` | `Ctrl+P` | SHOULD | — | = B21 |
| `[x]` | I7 | Text / markdown export | **None.** Image export and print only | **Bulk export to markdown files** | BETTER | — | ADR-002; ships in M1 **v0.2 score (2026-11-13):** Export as Markdown: everything, a folder or a note, with pictures and an index (ADR-020; `Invoke-Portability`). |
| `[~]` | I8 | Automatic backups | Auto backup to the data folder; 15 / 30 / 45 min; auto-cleanup | Same, incl. the three intervals and cleanup | MUST | #199 | **v0.2 score (2026-11-13):** F15 (#15): once a day, the last seven kept. Not the three intervals. |
| `[x]` | I9 | Manual backup | Browse Backups → Make backup; manual backups locked from cleanup | Same, incl. the lock | MUST | — | **v0.2 score (2026-11-13):** Settings › Back up writes a whole backup where the user chooses; automatic cleanup only removes Noto's own `auto-` backups, so a manual one is never pruned (F15). |
| `[x]` | I10 | Restore a backup | Restore from a chosen backup | Same, verified in M4 | MUST | — | **v0.2 score (2026-11-13):** Settings › Restore, with a backup taken first (ADR-020; `Invoke-Portability`). |
| `[~]` | I11 | Backups in cloud drive | Backup folder may be iCloud Drive | **User-chosen backup folder** (OneDrive etc.) | ADAPT | — | Noto writes to a folder; the user picks it **v0.2 score (2026-11-13):** A manual backup goes to any folder the user picks, OneDrive included. Automatic backups go to the data folder only. |
| `[x]` | I12 | iCloud sync | Automatic cross-device sync; last-write-wins; iCloud canonical | **No sync in v1** — local-first only | ADAPT | — | ADR-002. BYO-folder sync is post-v1 **v0.2 score (2026-11-13):** Local-first, no sync (ADR-001). |
| `[x]` | I13 | Local storage location | SQLite in Application Support; paths vary by channel | SQLite under `%LOCALAPPDATA%`; one location | ADAPT | — | **Done** — ADR-003; `NotoStoragePaths`: `%LOCALAPPDATA%\Noto\noto.db`, one channel, one path |
| `[ ]` | I14 | Legacy storage format | Pre-1.3 JSON format | **Not applicable** | DROP | — | Noto has no legacy format |
| `[x]` | I15 | Data portability | Effectively via SQLite + backups | Documented schema + markdown export + open backups | BETTER | — | ADR-002 makes this a contract **v0.2 score (2026-11-13):** The schema is documented (data-model.md), notes export to Markdown, and a backup is an open archive (ADR-020). |

**I12 is the biggest single deviation in this document, and it is decided, not
open.** SideNotes' iCloud sync is a headline feature: automatic, cross-device,
with a documented last-write-wins conflict rule. Noto ships **no sync** in v1.
ADR-002 makes local-first non-negotiable and principle 4 forbids core
functionality requiring an account or network. The roadmap places BYO-folder
sync explicitly post-v1, gated on solving the WAL-under-file-sync hazard first.
This is ADAPT rather than DROP because the underlying user need — "my notes are
safe and I can get at them elsewhere" — is met by I7, I8–I11 and I15: real
export, real backups, a documented schema, and a backup folder the user can
point at OneDrive. It is not the same capability and this document does not
pretend otherwise.

**I7 is the clearest genuine gap in SideNotes and the cheapest to beat.** The
inventory found no documented `.md`, `.txt` or PDF export and no bulk export at
all — data portability is "the SQLite file and the backups." ADR-002 promises
the opposite, and since note content is already stored as markdown source
(ADR-004), export is a file copy. It lands in M1, so it costs M3 nothing.

**I8–I10 are MUST and are tested in M4, not merely implemented in M3.** A
backup system that has never been restored from is not a backup system. The
roadmap's M4 already lists "backup and restore verified" as a hardening item.

---

## 10. J — Preferences

40 documented settings across 10 panes. Principle 9 says preferences are not
free — each is a branch in behavior and a test-matrix entry — so this table is
also the list of every configuration surface M3 is allowed to ship.

| Status | ID | Pane → Setting | SideNotes options | Noto requirement | Level | Issue | Notes |
| :----: | -- | -------------- | ----------------- | ---------------- | :---: | :---: | ----- |
| `[ ]` | J1 | General → Show or Hide Notes | Open Bar / Hot Side / Menubar Icon | Edge handle / Hover / Tray — **independently toggled** | ADAPT | — | Not exclusive modes; see A3/A4 and §12 |
| `[ ]` | J2 | General → Hide Open Bar | never / mouse inactive / always | Same three, **unconditionally available** | BETTER | — | SideNotes gates `always` on other modes |
| `[x]` | J3 | General → Side | Left / Right (default Right) | Same | MUST | — | = A2 **v0.2 score (2026-11-13):** Settings › Side. |
| `[x]` | J4 | General → Launch on Startup | on / off | Same, off by default | MUST | #94 | = A16. The setting exists in Windows (ADR-015) and in the tray; the settings surface does not **v0.2 score (2026-11-13):** Settings › Start with Windows, off by default, read from Windows (#94). |
| `[x]` | J5 | General → Close on outside click | on / off | Same | MUST | — | = A13 **v0.2 score (2026-11-13):** Settings › Hide when I click elsewhere. |
| `[~]` | J6 | Notes → Quick Formatting Toolbar | above / below / disabled | Same three | SHOULD | — | = D25 **v0.2 score (2026-11-13):** Settings › Formatting toolbar, on or off (F8, #151). Not the three options. |
| `[x]` | J7 | Folders → Single-click opening | on / off | Same | MUST | — | = C4 **v0.2 score (2026-11-13):** Settings › Open with a single click. |
| `[ ]` | J8 | Creating Notes → Placement | top / bottom / over / under current | Same four | MUST | #188 | = B5 **v0.2 score (2026-11-13):** No Settings row; the value is stored only. |
| `[ ]` | J9 | Creating Notes → Ask for folder | on / off | Same | SHOULD | — | = B6 |
| `[ ]` | J10 | Creating Notes → Open last folder on restart | on / off | Same | SHOULD | — | = C14 |
| `[ ]` | J11 | Text → Default text formatting | Markdown / Code (/ plain) | Same | SHOULD | — | = D28 |
| `[x]` | J12 | Text → Font family | any installed font | Same | MUST | — | = D47 **v0.2 score (2026-11-13):** Settings › Font (F13, #154). |
| `[x]` | J13 | Text → Font size | adjustable | Same | MUST | — | = D48 **v0.2 score (2026-11-13):** Settings › Text size (F13, #154). |
| `[ ]` | J14 | Text → Line height | Tight / Normal / Relaxed | Same | SHOULD | — | = D49 |
| `[ ]` | J15 | Text → Paragraph spacing | Tight / Normal / Relaxed | Same | SHOULD | — | = D50 |
| `[ ]` | J16 | Text → File links | standard / compact | Same | MUST | #196 | = D34 **v0.2 score (2026-11-13):** Not built. |
| `[ ]` | J17 | Text → Color copy format | output format for `#rrggbb` | Same | SHOULD | — | |
| `[ ]` | J18 | Text → Disable color display | on / off | Same | SHOULD | — | = F14 |
| `[ ]` | J19 | Text → No auto-spaces on paste | on / off | Same | SHOULD | — | = D41 |
| `[ ]` | J20 | Text → Code detection on paste | code block / code note | Same | SHOULD | — | = D39 |
| `[x]` | J21 | Appearance → Light / Dark / System | manual or follow system | Same | MUST | — | = F1 **v0.2 score (2026-11-13):** Settings › Theme. |
| `[ ]` | J22 | Appearance → Themes → Manage | install, list, reveal | Install, list, reveal in Explorer | SHOULD | — | = F2, F7 |
| `[ ]` | J23 | Appearance → Note colors | Background / Bar | Same | MUST | #191 | = F10 **v0.2 score (2026-11-13):** Not built. |
| `[ ]` | J24 | Appearance → Default new-note color | one of six, or empty | Same | SHOULD | — | = B15 |
| `[~]` | J25 | Shortcuts → Global shortcuts | record 6 global chords | Same 6, rebindable | MUST | #198 | = G50 **v0.2 score (2026-11-13):** As G50: four of the six are recorded in Settings. |
| `[x]` | J26 | Shortcuts → Escape behavior | 4 options | Same 4 | MUST | — | = A12 **v0.2 score (2026-11-13):** Settings › Escape. |
| `[ ]` | J27 | Importing → DnD on screen edge | enable / disable | Same | MUST | #185 | = A15 **v0.2 score (2026-11-13):** Not built. |
| `[ ]` | J28 | Importing → Show while dragging | enable / disable | Same | MUST | #185 | = A15 **v0.2 score (2026-11-13):** Not built. |
| `[ ]` | J29 | Importing → Move imported files to Trash | on / off | Move to **Recycle Bin**, off by default | ADAPT | — | Destructive — never default on |
| `[ ]` | J30 | Importing → Import behavior | images and text files | Same | MUST | #196 | = I1, I2 **v0.2 score (2026-11-13):** Not built. |
| `[ ]` | J31 | Exporting → Image background | color or transparent | Same | SHOULD | — | = I4 |
| `[ ]` | J32 | Exporting → Image margins | adjustable | Same | SHOULD | — | = I4 |
| `[ ]` | J33 | Exporting → Image ratio | selectable | Same | SHOULD | — | = I4 |
| `[ ]` | J34 | Exporting → Bottom bar in image | on / off | Same | SHOULD | — | = I4 |
| `[ ]` | J35 | Data → iCloud sync | enable / disable | **Absent** — no sync in v1 | DROP | — | = I12, ADR-002 |
| `[x]` | J36 | Data → Data locations | reveal data folders | Reveal data folder in Explorer | MUST | — | One location, not two **v0.2 score (2026-11-13):** Settings › Open data folder in Explorer (`Invoke-Settings` case 10). |
| `[x]` | J37 | Backups → Automatic backups | enable / disable | Same, **on by default** | MUST | — | = I8 **v0.2 score (2026-11-13):** Settings › Back up automatically, on by default (F15, #15). |
| `[ ]` | J38 | Backups → Frequency | 15 / 30 / 45 min | Same three | MUST | #199 | = I8 **v0.2 score (2026-11-13):** Not built: once a day. |
| `[ ]` | J39 | Backups → Backup folder | custom, incl. iCloud Drive | Custom folder, any path incl. OneDrive | ADAPT | — | = I11 |
| `[x]` | J40 | Backups → Browse backups | make / restore / lock | Same | MUST | — | = I9, I10 **v0.2 score (2026-11-13):** Settings › Back up and Restore; manual backups are never pruned (I9). |

**Most J rows are the settings-surface half of a row elsewhere** — the `=` in
Notes says which. They are enumerated separately because the settings window is
itself a deliverable with a structure, and because this table is the complete
list of permitted preferences. **A preference not in this table does not ship in
M3 without amending this document.**

**J1 is an ADAPT with real product consequence.** SideNotes treats activation as
three mutually exclusive modes, which is exactly why its `Hide Open Bar =
always` option is only available in two of them, and exactly why users have
asked for years for keyboard-only activation. Noto makes the three surfaces —
edge handle, hover, tray — independently toggleable, so "all off, global hotkey
only" is reachable. That also makes J2's third option unconditional, which is
recorded as BETTER.

**J29 flips a default deliberately.** Moving imported source files to the
Recycle Bin is destructive and touches files Noto does not own. It ships off.

**J35 is absent rather than disabled.** A greyed-out sync toggle would advertise
a capability that does not exist and invite the question every release
(principle 2, principle 6).

---

## 11. Where Noto deliberately differs

Every ADAPT and DROP in one place, with its reason. **None of these is an
omission.**

```
  macOS mechanism            Noto mechanism              Level   Why
  ─────────────────────────  ──────────────────────────  ──────  ────────────────────────────
  menu bar icon              system tray icon            ADAPT   Windows' equivalent surface
  iCloud sync                local-first, no sync in v1  ADAPT   ADR-002, principle 4
  iCloud Drive backups       user-chosen backup folder   ADAPT   OneDrive etc. is just a path
  AppleScript API            noto:// URI + CLI + PS      ADAPT   ADR-010 commands, one impl
  Apple Shortcuts            CLI (evaluate only)         ADAPT   no equivalent worth targeting
  Share Extension            copy / save-as / open-with  ADAPT   no NSSharingService on Windows
  Quick Look (Space)         in-app preview             ADAPT   OS service -> app feature
  macOS spell check          Windows spell-check API     ADAPT   different API, same outcome
  Stage Manager compat       (nothing)                   DROP    no analogue; A19 covers desktops
  Continuity Camera          (nothing)                   DROP    Apple-only device pairing
  macOS Text Replacement     (nothing)                   DROP    OS service, not an app feature
  Alfred workflow            CLI enables third parties   DROP    Noto ships no launcher plugin
  Hookmark / PopClip /       (nothing)                   DROP    Mac-only PRODUCTS — their
  Dropzone / MindNode /                                          absence is a fact about them,
  Things / Dropshare /                                           not a gap in Noto
  Workspaces / Raycast
  Handoff, Services menu     (nothing)                   DROP    Apple-only OS concepts
  trackpad gestures          precision touchpad, opt-in  ADAPT   keyboard path MANDATORY (P7)
  theme gallery website      (nothing in M3)             DROP    a website, not an app feature
```

**On the Mac-only third-party integrations (H7–H13, H16).** Eight of SideNotes'
listed integrations are with products that only exist on macOS. Noto cannot
integrate with software that does not run on Windows, and building Windows
lookalikes of them is not parity — it is eight new products. The CLI (H3/H4)
is the general answer: anyone who wants Noto in PowerToys Run, Flow Launcher or
their own script can build it against a stable command surface. Noto does not
ship those plugins in M3.

**On gestures (A21, B12 pinch).** SideNotes' trackpad gestures are a documented
source of conflict, and the inability to disable the edge trigger is the app's
longest-running complaint. Noto may implement precision-touchpad equivalents
where hardware supports them, but principle 7 is absolute: **every gesture has a
keyboard equivalent, and every gesture is disableable.** No gesture is the only
path to anything.

**On sync (I12).** This is the one ADAPT where the user outcome is genuinely
reduced, and it is stated plainly rather than softened. SideNotes users get
automatic cross-device sync; Noto v1 users get their notes on one machine, with
real export, real backups, and a backup folder they may point at any sync
service. That is a deliberate trade made by ADR-002, not an implementation
shortfall.

---

## 12. Where Noto deliberately exceeds SideNotes

Five BETTER rows, grouped into three themes below. Kept small on purpose —
scope discipline is what makes M3 finishable, and "better than SideNotes" is an
infinite well.

| ID | Gap in SideNotes | Noto | Why it is justified |
| -- | ---------------- | ---- | ------------------- |
| I7, I15 | **No bulk / text / markdown export.** Image export and print only; portability is the SQLite file and backups | Bulk export to markdown files; documented schema; open backup format | **ADR-002 promises the data outlives the app.** Content is already markdown source (ADR-004), so export is a copy. Planned for M1 (#15); not started |
| B7, B23 | **No trash or undo for deleted notes.** Recovery is backups only; a confirmation popover is the only protection | Soft delete + recycle bin + restore | Same promise. A confirmation dialog is not data safety, it is a speed bump. Engine built in M1; no recycle-bin UI yet |
| J2 (with A3, A4, J1) | **No keyboard-only activation.** Users must accept a visible edge bar or a menu-bar icon; `Hide Open Bar = always` is gated on being in another mode | Every activation surface independently disableable — including all of them at once | **The most-requested missing SideNotes feature in reviews**, and principle 7 states it directly. Costs one settings decision over the parity implementation |

A3, A4 and J1 stay MUST/ADAPT rather than BETTER because the activation
*surfaces* are plain parity. Only J2's unconditional `always`, and the
independence between the surfaces, exceed SideNotes.

**Flat folders are explicitly not a BETTER row.** SideNotes appears to be flat,
one level — INFERRED from documentation absence, not confirmed. Noto matches it
for parity (C3). **Deeper nesting is not a parity requirement and must not be
added speculatively.** It would be a second organizational paradigm (principle 9
defaults to no) and it would change navigation, move, search scoping and URI
shape at once. If it is ever wanted, it is an M7 candidate with its own case.

**The boundary this table defends.** Noto has a long list of things it could do
better than SideNotes; the roadmap puts them in M5–M8 and
`competitor-enhancements.md`, deliberately separate from this document so that
"we promised SideNotes parity" never blurs into "we are making Noto better."
The rows above earn their place because two of the three themes are M1 work
mandated by an ADR, and the third is a settings decision rather than a
feature. None of the five is delivered yet.

---

## 12a. Keyboard constraint: `Ctrl+Alt` and AltGr

**This is a real defect risk, not a preference, and it has no macOS analogue.**

On German, French, Polish and several other layouts, **AltGr is delivered to
applications as `Ctrl+Alt`**. A global or in-app shortcut bound to
`Ctrl+Alt+<key>` therefore intercepts characters the user is actively typing:

```
  German layout     AltGr+Q  ->  @        <- swallowed by Ctrl+Alt+Q
  Polish layout     AltGr+A  ->  a-ogonek <- swallowed by Ctrl+Alt+A
  French layout     AltGr+E  ->  euro     <- swallowed by Ctrl+Alt+E
```

SideNotes never faced this: macOS uses Option for the same characters, and its
shortcuts use Command.

**Eight rows in section G map a SideNotes chord onto `Ctrl+Alt+<key>`.** Each
must be re-mapped before implementation.

### Rules

1. **No default shortcut uses `Ctrl+Alt+<letter>`.** Prefer `Ctrl+Shift+<key>`,
   `Alt+<key>` or a chord.
2. **Every shortcut is rebindable** — the mitigation of last resort, and
   required anyway by principle 7.
3. **Test on a non-US layout** before shipping the shortcut set. This belongs in
   the M4 gate.

One internal collision is also unresolved: **pin-note and insert-picture both
map to `Ctrl+Shift+P`.** Resolve when G rows become issues.

---

## 13. Open questions

The inventory lists 17 items that no source documents and that must be resolved
by **hands-on testing of SideNotes** before the corresponding rows are final.
Until then those rows are **provisional**: the requirement may be right, but its
fidelity to SideNotes is unverified.

| # | Question | Rows | Effect if the answer differs |
| - | -------- | ---- | ---------------------------- |
| Q1 | **Multi-display: which monitor, does it follow focus?** | A18 | Highest impact. Undocumented in *every* source. Noto must define its own behavior in M2 regardless |
| Q2 | Virtual desktop / Spaces behavior | A19 | Windows Virtual Desktop behavior must be defined either way |
| Q3 | Exact chord for move-note-to-top/bottom | B9 | Chord only; capability is confirmed |
| Q4 | Does note-level duplication exist? | B22 | If no, B22 drops from SHOULD to a Noto extra |
| Q5 | Is there any trash / undo for deletion? | B23 | If yes, the BETTER row in §12 becomes a MUST |
| Q6 | Confirm folders are flat, one level | C3 | If nesting exists, C3 flips from MUST-flat to MUST-nested — a large change |
| Q7 | Folder sorting options | C10 | Which sort keys are parity vs. extra |
| Q8 | Underline syntax and shortcut | D7 | Noto must choose a markdown-safe representation regardless |
| Q9 | Confirm tables are unsupported | D18 | If supported, D18 flips DROP → MUST and contradicts ADR-004's deferral |
| Q10 | Clear-formatting shortcut | D21 | Chord only |
| Q11 | Search: incremental? filters? in-note find? | E8 | Noto requires incremental regardless (principle 3); filters/in-note find stay out unless confirmed |
| Q12 | Open-containing-folder modifier (`⌃` vs `⌘`) | E5, G49 | Chord only |
| Q13 | Window transparency / opacity setting | F11 | If a real setting exists, F11 gains a preference row |
| Q14 | Confirm no text / markdown / PDF export | I7 | If it exists, the §12 BETTER row becomes plain parity |
| Q15 | Are non-global shortcuts rebindable? | G52 | Determines whether G52 is MUST or SHOULD |
| Q16 | macOS Services integration — confirm absent | H18 | Already DROP; confirmation only |
| Q17 | Handoff support — confirm absent | H17 | Already DROP; confirmation only |

**Q1 is the one that actually matters.** Multi-display behavior is undocumented
on every Apptorium page, was noted as absent by the reviewer the inventory
cites, and is the first thing a Windows user with two monitors encounters. No
amount of reading resolves it. Noto's behavior will be decided on its own merits
in M2 and written down — but until SideNotes is tested by hand, A18 cannot claim
to be a parity row at all, only a Noto requirement.

**Q6 and Q9 are the two that could move real scope.** If SideNotes turns out to
support sub-folders or tables, C3 and D18 invert, and both are substantial. Both
are currently INFERRED-from-absence, which is the weakest evidence class in the
inventory. Neither should be built speculatively before testing.

**Rows depending on Q3, Q10 and Q12 are chord-only questions** and do not block
implementation — the capability is confirmed in each case, only the exact
SideNotes keystroke is unknown, and Noto is remapping chords anyway (section 7).

---

## 14. Coverage summary

### Rows by level

```
  MUST     ██████████████████████████████████████████████████████  147
  SHOULD   ████████████████████████                                 66
  ADAPT    ████████                                                 23
  DROP     ████████                                                 23
  BETTER   ██                                                        5
                                                                  ─────
  TOTAL                                                             264
```

| Level | Count | Meaning for M3 |
| ----- | ----: | -------------- |
| **MUST** | 147 | M3 is not complete until all are `[x]` |
| **SHOULD** | 66 | Gaps listed and re-decided at the M4 gate |
| **ADAPT** | 23 | Windows mechanism, same outcome; counted as work |
| **DROP** | 23 | Decisions, never checked, never re-litigated without a reason |
| **BETTER** | 5 | Deliberate excess; 4 of 5 planned for M1, 0 of 5 delivered |
| **Total rows** | **264** | Traced to ~273 inventory items (K. pricing / version history / privacy are not requirements) |

### Rows by category

| Cat | Category | Rows | MUST | SHOULD | ADAPT | DROP | BETTER | Primarily lands in |
| --- | -------- | ---: | ---: | -----: | ----: | ---: | -----: | ------------------ |
| A | Sidebar / Workspace | 21 | 16 | 2 | 3 | 0 | 0 | **M2**, verified in M3 |
| B | Note management | 26 | 16 | 7 | 0 | 1 | 2 | M1 domain + **M3** UI |
| C | Folders / organization | 19 | 11 | 5 | 1 | 2 | 0 | M1 domain + **M3** UI |
| D | Editor & content | 51 | 28 | 16 | 3 | 4 | 0 | **M3** — the largest block |
| E | Search | 8 | 4 | 2 | 2 | 0 | 0 | **M3** |
| F | Appearance & themes | 14 | 2 | 8 | 1 | 3 | 0 | **M3**, on M0 tokens |
| G | Keyboard shortcuts | 52 | 42 | 8 | 2 | 0 | 0 | **M3** |
| H | Integration & automation | 18 | 3 | 0 | 4 | 11 | 0 | **M3** (URI/CLI) |
| I | Import / export / backup | 15 | 6 | 2 | 4 | 1 | 2 | M1 export + **M3** rest |
| J | Preferences | 40 | 19 | 16 | 3 | 1 | 1 | **M3** |

### Where the work actually is

```
  M1   domain: soft delete, markdown storage, export, attachments   ── B, C, I
  M2   the panel itself: edge dock, activation, DPI, tray           ── A
  M3   ████████████████████████████████████████  everything else
       editor (D, 51 rows)  ·  shortcuts (G, 52)  ·  prefs (J, 40)
       search (E)  ·  themes (F)  ·  URI + CLI (H)  ·  floating notes
  M4   verification of the above under load, at scale, accessibly
```

Three categories — **D (editor), G (shortcuts), J (preferences)** — are 143 of
264 rows, 54% of the document, and they hold 89 of the 147 MUSTs. That is where
M3 is won or lost. Category A (M2) and the M1 slices of B, C and I were
expected to arrive checked. They did not: M2 delivered most of A, and the M1
engine is built, but most B, C and I rows still need a surface a user can reach
— see *Current status*. They are listed because M3's exit criterion reads the
whole table.

**Floating notes** appear in the roadmap's M3 list but have no inventory row —
SideNotes has them, the inventory did not enumerate them as a numbered feature.
Under ADR-009 they are a presentation kind, not a domain change. They are M3
work governed by the roadmap, not by a row here; if hands-on testing produces
SideNotes-specific floating-note behaviors, they get rows in a revision of this
document.

---

## Revision rules

1. **Adding a row requires an inventory reference.** If it is not in the
   inventory, it is not parity — file it against M5–M8 instead.
2. **Changing a `MUST` to a `SHOULD` requires a reason recorded here**, not a
   quiet edit.
3. **Resolving an open question updates the affected rows and removes the
   `⚠ provisional` mark** in the same change.
4. **A `DROP` is re-opened only with new evidence**, not with a preference.
5. This document and [ADR-004](../decisions/ADR-004-markdown-content.md) must
   stay consistent on invisible markdown; if they diverge, the ADR is reviewed
   first (principle 11).
