# ADR-016 — The card-stack workspace

**Status:** Accepted
**Date:** 2026-10-20
**Related:**
- ADR-004 (markdown content), ADR-007 (window presentation), ADR-009 (note / presentation separation), ADR-010 (commands and events), ADR-011 (design system)
- `docs/roadmap/implementation-plan.md` §9 decision 1, §10 (shortcut map)
- `docs/architecture/deletion-semantics.md` §13
- Parity: B1, B5, B7, B10–B18, B20, B23, B25, C5–C15, C19, G9–G26, J7, J8, J10

---

## Context

Noto's workspace today uses *replacing navigation* across three surfaces:

```
folders  ──enter──▶  note list (titles)  ──open──▶  editor (one note, raw source)
```

SideNotes, the functional benchmark, shows a folder's notes as a **stack of cards**:
- each card shows the note's content;
- a card is edited in place;
- a card can be folded to its first line;
- the active card has a bottom bar of actions.

Fold (B12, B13), the note bottom bar (B17), notes filling the panel's width (B25) and fold-all only mean something in that model.

The roadmap locks the card stack (decision 1), on the unchanged, normalised Folder → Note domain. The presentation must be fixed **before** P1 builds on it, and fixed so that the P2 editor engine replaces the editor **inside** a card, not the stack around it.

## Decision

### 1. Two surfaces, not three

```
folders  ──enter──▶  note stack (cards, edited in place)
   ▲                        │
   └────── Back / Esc ──────┘
```

- The folder surface is unchanged in kind: a list of folders, pinned first, with the Recycle Bin and Settings beside it.
- The **note stack** replaces both the note list and the editor surface; `EditorRoot` is retired in P1.2.
- The domain does not change. A card is a *presentation* of a `Note` (ADR-009): no presentation state is stored on the note except state the domain already owns (`IsFolded`, `IsPinned`, `ColorKey`, `SortOrder`).

### 2. Card anatomy and states

```
┌─ card ─────────────────────────────────────┐
│ ▌ colour (P7)                    📌 pinned  │
│ body:  read view   (Inactive, Focused)      │
│        editor host (Editing)                │
│ ─ bottom bar (Editing only): pin · fold ·   │
│   move · delete · more ▾        (B17)       │
└─────────────────────────────────────────────┘
list bottom bar:  "12 notes"  ·  +  ·  list menu ▾   (B18, C19)
```

| State | Body | How it is entered |
|---|---|---|
| Inactive | read view: the whole note; first line only when folded | default |
| Focused | read view + focus ring; the card is the list's focused item | Tab into the stack, Up/Down, Esc from Editing |
| Editing | the editor host, caret placed | click, Enter or F2 on a focused card, Ctrl+N (new card) |

- **One card is Editing at a time.** Entering Editing on another card leaves the current one, which saves first: the existing save-on-leave rule, unchanged until P2.1.
- Editing a folded card unfolds it, and the unfold is persisted: one rule, no hidden "temporarily unfolded" state. This is provisional until SideNotes' behaviour is observed; it can change without touching the domain.

### 3. The editor-host seam (the P2 boundary)

`NoteEditorHost` (Noto.Windows) is the only thing that edits text. Everything outside it sees this surface and nothing else:

| Member | Meaning |
|---|---|
| `Load(NoteId, string content)` | show this note's markdown source |
| `string Content` | the current markdown source (ADR-004: markdown is the truth) |
| `event ContentChanged` | the source changed (P2.1 continuous persistence subscribes here) |
| `event LeaveRequested(EditorLeave)` | the user asked to leave: `Escape`, `Next`, `Previous` |
| `void PlaceCaret(CaretPlacement)` | `Start`, `End`, or a point (P2: a hit-tested position) |

- In P1 the host contains a `TextBox`: the plain source, exactly as today.
- In P2 its *inside* becomes the rich editor, driven by Core's markdown model and edit transforms. Callers do not change.
- A guard test holds the seam: no file but the host's own references its inner control.

The **read view** has the same kind of seam: one method builds a card's body from its content and fold state. P1 shows the source as text; P2 renders the same Core span model, read-only.

### 4. List mechanics

- The stack is a WinUI `ListView`, reusing `ListRow`'s states from Slice 1, with variable-height containers. `ListView` is kept, not `ItemsRepeater`, because it brings:
  - virtualisation;
  - single selection;
  - keyboard focus movement;
  - reorder (P1.9);
  - UI Automation list semantics (position in set, selection);
  - theme transitions.
- Only realised cards exist. Only the Editing card holds an editor.
- Selection = the Focused or Editing card. It is remembered per folder for the session, and across restarts from P1.11.

### 5. Keyboard (complete map: implementation plan §10)

| Where | Key | Effect |
|---|---|---|
| stack | Tab | into the stack, onto the selected card |
| card focused | Up / Down | previous / next card |
| card focused | Enter, F2 | edit, caret at end |
| card focused | Esc | the Escape setting (leave folder or hide), as today |
| card focused | Shift+F10, Menu key | the card's context menu |
| editing | Esc | leave editing (save); the card stays focused |
| editing | Ctrl+Alt+Up / Down | edit the previous / next card (G11, G12) |
| editing | Tab | leaves the editor (no tab character in P1; list indentation arrives in P2) |
| anywhere in the stack | Ctrl+N | new note at the configured placement (B5), editing |
| anywhere in the stack | Ctrl+Alt+Backspace | delete the focused / editing note (soft) |

### 6. Pointer

- Clicking a card edits it. Single-click versus double-click (J7) applies to **folders** only: a note does not "open", it is edited in place. The Settings description changes from "Folders and notes" to "Folders" in P1.2.
- Right-click opens the context menu.
- Drag reorders (P1.9).

### 7. Deletion, confirmation and feedback

| Action | Confirmation | Feedback |
|---|---|---|
| Delete note (soft) | none (B7 BETTER) | "Note moved to the Recycle Bin", with **Undo**, announced (live region) |
| Delete folder (soft, notes included) | flyout: "Delete *X* and its *n* notes? They go to the Recycle Bin." | as above |
| Restore | none | "Restored", announced |
| Delete permanently / Empty Recycle Bin | dialog; Cancel is the default; the wording says it cannot be undone | announced |

The Recycle Bin is its own surface, opened from the folder surface (P1.3). Its rules are deletion-semantics §13.

### 8. Accessibility contract (every P1 PR; decision 10)

- A card's UI Automation name is its title (first line, B16), with its states in the help text: pinned, folded, colour name. The editor host is named "Note content".
- Every action is reachable from the keyboard and from the context menu. No pointer-only path.
- Every command result is announced through a live region.
- High Contrast:
  - card colour is shown as a stripe or border in system colours, never as a fill;
  - the focus ring is Slice 1's two-tone ring;
  - no backplate.
- Theme transitions follow Windows animation effects (verified at P1.2).

### 9. Where the code goes

| Concern | Layer |
|---|---|
| Ordering, placement, pin, fold, lifecycle rules | Core / UseCases (they exist; P1 adds the missing commands) |
| Loading a folder's cards, dispatching commands, refreshing on events | `NoteStackPresenter` (Noto.Windows, plain class, no rules) |
| Card visuals, states, bars | Noto.UI styles and templates (tokens only) |
| `NoteCard`, `NoteEditorHost` | Noto.Windows user controls |

`MainWindow` keeps window orchestration and stops growing: each P1 PR moves the code it touches into its presenter.

## Consequences

- P2 changes only the inside of `NoteEditorHost` and the read view.
- The note list's "open a note" path, `EditorRoot` and their tests are retired in P1.2, which is behaviour-preserving for create, edit, save-on-leave, delete, Escape and the drawer.
- Variable-height virtualised cards with an inline editor are the riskiest UI mechanics in P1. P1.2 includes a focused runtime check (focus, arrow keys, transitions under Animation effects off), which the roadmap allows.

## Alternatives considered

| Alternative | Why not |
|---|---|
| Keep drill-in (list → editor) | Fold, the note bottom bar and per-note width have no meaning in it; rejected by decision 1 |
| `ItemsRepeater` stack | Selection, keyboard navigation, reorder and list semantics would all be hand-built; more code, weaker accessibility |
| An editor in every card | Memory and focus cost per card; contradicts "one Editing card" |
| A single editor re-parented between containers | Fragile under virtualisation (containers are recycled) |
