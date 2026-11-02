# Workspace management: Phase B design gate

**Status:** Accepted, 2026-11-02
**Scope:** Basic Release Phase B ([basic-release.md](../roadmap/basic-release.md)): move, recent folders, pin, fold, reorder, drag and drop, menus, confirmations, keys.
**Related:** ADR-016 (card stack), ADR-010 (commands and events), [deletion-semantics.md](deletion-semantics.md) §13, [core-note-engine-contract.md](core-note-engine-contract.md), [implementation-plan.md](../roadmap/implementation-plan.md) §10 (shortcut map).

## 1. What exists

The engine already does every Phase B mutation, transactionally and with its ordering rules:

| Command | Engine rule |
|---|---|
| `MoveNoteToFolder` | Destination validated; the note lands at the end of the target (O4); I7 |
| `ReorderNote`, `ReorderFolder` | `After(sibling)` or first, computed by `SortOrderEngine`, with renormalisation (O6) |
| `PinNote`, `UnpinNote`, `PinFolder`, `UnpinFolder` | One field; idempotent |
| `FoldNote`, `UnfoldNote` | One field; idempotent |
| `DeleteFolder`, `RestoreFolder`, `RestoreNote`, the purges | P1.1 |

**What is missing is consumption.** Each command needs:
- registering in the composition root;
- an event published after commit (ADR-010);
- a keyboard path and a menu entry;
- focus rules.

No new mutation, ordering system or pin model is introduced.

## 2. The flow, for every action

```
key / menu item / button / drop
        -> NoteStackSession or a UseCases service      (which command, which arguments: pure rules)
        -> CommandDispatcher -> handler -> repository  (one transaction)
        -> commit -> typed event (registry, after success only)
        -> UI: re-read, restore focus, announce
```

`MainWindow` and XAML decide nothing. They name the action and show the result. Any rule ("which sibling is above me in my group", "which folders are recent") is a pure function in Core, or a method on a framework-free UseCases type, and is unit-tested and mutation-tested there.

## 3. Decisions

### Move (B11, G16)
- **Destinations:** every active folder except the note's own. A move into its own folder would move the note to the end, which is a reorder, not a move. It is not offered, and the session treats it as a no-op.
- **Ways to move:** Ctrl+Shift+M on a card or in its editor, and the card's menu (*Move to*). Both open the same destination menu:
  - the recent folders first;
  - then *All folders*, in folder-list order.
- **The editing card is saved first.** A failed save moves nothing.
- **After a move:**
  - the card leaves the stack;
  - focus goes to the next card, else the previous, else the stack;
  - the result is announced ("Moved to *Work*").
- **Nothing follows the note** to its new folder: the user is filing it, not navigating.
- **Event:** `NoteMoved(NoteId, FolderId target)`, after commit.

### Recent folders (B11, SideNotes 1.6 "recent-folder moves")
| Question | Answer |
|---|---|
| What counts | A folder that **received a moved note**. Opening or creating a folder does not count. |
| Order | Most recent first |
| Duplicates | Moving to a folder already listed brings it to the front |
| Size | 5 |
| A deleted or purged folder | Skipped when the list is shown, and dropped the next time the list is written. Nothing eager. |
| After restart | Kept: a typed setting, `notes.recent-move-folders` (ids in order; anything unparseable reads as empty) |
| Recorded by | A `NoteMoved` subscriber: the move's own transaction is never extended by a settings write, and a failed settings write never fails a move |

### Pin (B14, C8)
- **Ways to pin:** a card's or a folder's menu toggles it (*Pin* or *Unpin*). There is no default chord (implementation plan decision 3).
- **Order:** the item is re-sorted by O2 (pinned first) and keeps focus where it lands.
- **Markers:** the existing ● marker. In High Contrast it is a text glyph in the row's own colour, never a fill.
- **New notes are never pinned;** P1.2's placement rule already places them among the unpinned.

### Fold (B12, B13, G19–G22)
- **Keys:** Ctrl+Alt+Left folds the current card and Ctrl+Alt+Right unfolds it. The menu toggles it.
- **Fold all and unfold all:** Ctrl+Alt+Shift+Left/Right, through one command for the open folder.
- **Folded state is stored:** `IsFolded` is the domain's.
- **Editing a folded card unfolds it, and the unfold is stored** (ADR-016 §2). A folded card shows its title only; its content is untouched.

### Reorder (B8, B9, C9, G17, G18)
- **Keys:** Ctrl+Alt+Shift+Up/Down move the current card or folder up or down **within its pin group**. Top and bottom are in the menu.
- **Shared SortOrder space:** pinned and unpinned items share it, while display is pinned first. A pure Core rule therefore turns "up within my group" into the engine's `After(x)`:
  - before the previous item of the same group: after that item's SortOrder predecessor, or first;
  - down: after the next item of the same group.

  At an edge it is a no-op that writes nothing.
- **One ordering system:** `SortOrderEngine` decides the numbers.
- **Event:** `NoteReordered` / `FolderReordered`, after commit.

### Drag and drop (B10, C9): in-app only
- **What can be dragged:** a card within its stack, and a folder within the folder list.
  - The `ListView`'s own reorder supplies the insertion line, hover feedback and Escape to cancel.
  - The drop is translated into the same `ReorderNote` or `ReorderFolder` (`After` = the item now above it), and the list is re-read from the database. The visual order is never trusted.
- **Invalid drops are refused** and the list is re-read:
  - across the pin boundary (pin it instead);
  - from outside the app.
- **Not in Phase B:** dragging a note onto a folder. The two surfaces are never shown together (ADR-016 §1), so there is no visible target. Ctrl+Shift+M and the menu are the way to move.
- **No shell drag and drop.** Keyboard fallback: reorder keys and the menu.

### Menus and confirmations
- **Menus:** short, and each item shows its key.
  - Card: Edit, New note, Move to ▸, Pin/Unpin, Fold/Unfold, Move to top, Move to bottom, Move to the Recycle Bin.
  - Folder: Open, Rename, New folder, Pin/Unpin, Move to top, Move to bottom, Delete.
- **Confirmations:**
  - Deleting a folder (soft, with its notes): a flyout, "Delete *X* and its *n* notes? They go to the Recycle Bin." (ADR-016 §7).
  - Permanent deletes: the Recycle Bin's own dialogs, P1.1 semantics, Cancel as the default.
  - Pin, fold, move, reorder and deleting a note never ask: each is reversible.

### Focus and selection (every action)
- **After an action**, the acted-on item keeps focus where it now sits.
- **When the item has left the list**, focus goes to the next item, else the previous, else the list or Back. Never nowhere.
- **Selection** is by id, never by index (ids survive the re-read).

## 4. PRs

| PR | Contents |
|---|---|
| B1 | Move to folder (Ctrl+Shift+M, menu), recent folders, `NoteMoved`, this gate |
| B2 | Pin and fold, notes and folders; fold all; their events |
| B3 | Reorder by keyboard and menu (top, bottom, up, down), notes and folders |
| B4 | Drag and drop reorder, in-app |
| B5 | Folder delete and restore UI with its confirmation; the Recycle Bin surface (P1.1's engine) |
| B6 | Keys, menus and accessibility completion: a pass over B1–B5 |

**Every PR includes:**
- Core tests for its pure rules;
- real-SQLite tests for the session and the events;
- guards for wiring and menus;
- mutation testing of the rules and the session;
- the cheap gates.

A focused runtime case is added only where Windows interaction is uncertain: flyouts and submenus, drag and drop.
