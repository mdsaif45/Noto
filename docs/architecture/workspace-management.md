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

### B2 precisely: pin and fold state (2026-11-02)

| | Pin | Fold |
|---|---|---|
| Stored | `IsPinned` (exists) | `IsFolded` (exists): a note stays folded across restarts and in every surface |
| Order | O2 lays the pinned partition over the shared SortOrder: pinned first, each partition in SortOrder order. Pinning never writes SortOrder, so unpinning returns the note to its place. No second order. | Unchanged |
| What changes on screen | The ● marker; the card or folder moves into or out of the pinned group | A folded card shows its title only, with a chevron marker; the note is still in the stack, selectable and focusable |
| Content | Untouched | Untouched: folding never edits the text |
| The editing card | Pinning it keeps it editing with its unsaved text; the stack is re-read and its editor reloads the session's text | Folding it saves and leaves editing first; a failed save folds nothing |
| Editing a folded card | - | Unfolds it first, stored (ADR-016 §2); a failed unfold edits nothing |
| Fold all / unfold all | - | One command, `FoldNotesInFolder(folder, folded)`, one UPDATE in one statement. Only rows that change are stamped. Fold all leaves editing first (saving), because the editing card folds too; unfold all keeps the editing card editing |
| Focus | The item keeps focus at its new position (re-read, focused by id) | Stays on the card: one card is updated in place (no rebuild, no layout jump beyond its own height); fold all re-reads and focuses the same card |
| Keys | None: menu only (decision 3) | Ctrl+Alt+Left folds, Ctrl+Alt+Right unfolds the current card (the editing one, else the focused one); with Shift, every card in the folder. Handled once, on the stack's PreviewKeyDown, so the card and the editor behave the same |
| Menus | Card: Pin / Unpin. Folder: Pin / Unpin | Card: Fold / Unfold |
| Assistive technology | Help text "Pinned" on cards and folders; the menu item names the action that applies | Help text "Folded"; Fold or Unfold in the menu |
| Events (after commit) | `NotePinChanged`, `FolderPinChanged` | `NoteFoldChanged`, `NotesFoldedInFolder` |
| Motion | None added: the list's own layout; nothing to reduce | Same |

### Reorder (B8, B9, C9, G17, G18)
- **Keys:** Ctrl+Alt+Shift+Up/Down move the current card or folder up or down **within its pin group**. Top and bottom are in the menu.
- **Shared SortOrder space:** pinned and unpinned items share it, while display is pinned first. A pure Core rule therefore turns "up within my group" into the engine's `After(x)`:
  - before the previous item of the same group: after that item's SortOrder predecessor, or first;
  - down: after the next item of the same group.

  At an edge it is a no-op that writes nothing.
- **One ordering system:** `SortOrderEngine` decides the numbers.
- **Event:** `NoteReordered` / `FolderReordered`, after commit.

### B3 precisely: reorder (2026-11-02)

One order exists: `SortOrder` within a scope (a folder's notes; the folder list), shared by pinned and unpinned items. Display is O2: pinned first, then SortOrder, then Id. The engine places an item `After(sibling)` or first, and renormalises when a gap runs out (O6).

**The rule** (`Reposition`, Core, pure; one rule for notes and folders). Given the scope in display order, an item and a direction, it returns either *no move* or the engine's placement:

| Direction | Target in the item's own pin group | Placement |
|---|---|---|
| Up | the previous item of the group | before it: `After(its SortOrder predecessor)`, or `First` when it has none |
| Down | the next item of the group | `After(it)` |
| Top | the group's first item | before it, as for Up |
| Bottom | the group's last item | `After(it)` |

- **The pin boundary is never crossed.** The first unpinned item cannot move above a pinned one, and vice versa: pinning is how a note joins the pinned group.
- **At an edge it is no move.** Nothing is dispatched and nothing is written. The engine's own `AlreadyInPosition` check is the second line.
- **"SortOrder predecessor"** means the previous item in SortOrder order (ties broken by Id, as O2 does), whichever group it belongs to. Landing just after it puts the item immediately before the target in SortOrder, with no item of its own group in between.

**The flow.**
- Notes go through `NoteStackSession.Reorder(note, direction)`: the rule, then `ReorderNote`, then a re-read.
- Folders go through the same rule over `ListFoldersQuery`, then `ReorderFolder`.
- Events are published after commit: `NoteReordered`, `FolderReordered`.

**The editing card** keeps editing with its unsaved text when it is reordered. Order is not content.

**Keys** (G17, G18):
- Ctrl+Alt+Shift+Up/Down move the current card (editing, else focused) or the focused folder.
- They are handled on each list's PreviewKeyDown, ahead of:
  - Ctrl+Alt+Up/Down (edit the previous or next card);
  - Ctrl+Down (enter a folder).

**Menus** (B9): Move to top and Move to bottom on cards and folders, each disabled when the rule says no move.

**Focus:** the item keeps focus at its new place; an editing card keeps its caret. Each move is announced ("Moved up", "Moved to top").

**Not in B3:** sorting folders by name (C10, SHOULD); drag and drop (B4).

### Drag and drop (B10, C9): in-app only
- **What can be dragged:** a card within its stack, and a folder within the folder list.
  - The `ListView`'s own reorder supplies the insertion line, hover feedback and Escape to cancel.
  - The drop is translated into the same `ReorderNote` or `ReorderFolder` (`After` = the item now above it), and the list is re-read from the database. The visual order is never trusted.
- **Invalid drops are refused** and the list is re-read:
  - across the pin boundary (pin it instead);
  - from outside the app.
- **Not in Phase B:** dragging a note onto a folder. The two surfaces are never shown together (ADR-016 §1), so there is no visible target. Ctrl+Shift+M and the menu are the way to move.
- **No shell drag and drop.** Keyboard fallback: reorder keys and the menu.

### B4 precisely: drag and drop (2026-11-02)

Drag and drop is **another way to ask for B3's reorder**, nothing more: the same rule, the same command, the same one order.

| Drag | Onto | Result |
|---|---|---|
| a card | a position in its own pin group, in the same stack | a reorder: `ReorderNote` |
| a card | a position in the other pin group | **refused**: nothing is written, the stack is re-read, "Pin it to move it there" is announced |
| a card | where it already is (or the drag is cancelled with Escape) | **no move**: nothing is written |
| a folder | a position in its own pin group | a reorder: `ReorderFolder` |
| a folder | a position in the other pin group, or where it is | refused, or no move, as for cards |
| a card | a folder | **not offered**: the two surfaces are never shown together (ADR-016 §1); Ctrl+Shift+M and the menu move a note |
| anything from outside Noto (files, text) | either list | **refused**: in-app reorder only; importing is Phase F |

**The rule** (`Reposition.Drop`, Core, pure):
- The list's own reorder reports where the item landed: its index in the list after the drop.
- The rule checks that index against the pin boundary. With *P* pinned items, a pinned item must land below *P* and an unpinned one at or above it.
- It turns the landing index into a position *k* in the item's own group, then into the engine's placement (`Reposition.To`):
  - before the group's *k*-th other item, i.e. after that item's SortOrder predecessor, or first;
  - or, at the end of the group, after the group's last item.
- *k* equal to the item's current position is no move.

**The visual order is never trusted.** After every drop (moved, refused or no-op) the list is rebuilt from the session or the database. A refusal therefore snaps back by construction.

**The editing card** can be dragged. It keeps editing with its unsaved text and caret: order is not content, and nothing is left, so nothing is saved.

**Focus:** the dropped item is selected and focused at its new place, and the move or refusal is announced.

**Accessibility:** drag and drop is never the only way. Every move it does has a key (Ctrl+Alt+Shift+Up/Down) and a menu item (Move to top, Move to bottom), and the result is announced.

**No new subsystem:** the `ListView`'s own reorder (`CanDragItems`, `CanReorderItems`, `AllowDrop`) supplies the drag visuals, the insertion point and Escape to cancel. Noto adds one completion handler per list.

### B5 precisely: deleting folders and the Recycle Bin (2026-11-02)

**One deletion model**, the engine's (deletion-semantics.md §13, P1.1). The UI only names actions and asks first where the action cannot be undone.

**Deleting a folder** (C11):
- **Where:** from the folder list only. On a folder row, its menu's *Delete*, or the Delete key.
- **Confirmation:** a flyout at the row, "Delete *X* and its *n* notes? They go to the Recycle Bin." *Delete* or *Cancel*. Cancel is the default, and Escape cancels.
- **The write:** `DeleteFolder`, a soft delete. The folder and its active notes go to the bin together under one timestamp (Cases A and B). Nothing is ever destroyed.
- **Cancel writes nothing.**
- **No unsaved text, by construction.** The folder list is reached only by leaving the stack, and leaving saves first; a failed save never reaches the folder list (Phase A). So a delete cannot discard what was typed, and the delete needs no save of its own.
- **"The active folder"** is the selected row. After the delete, focus goes to the next folder, else the previous, else the new-folder box. The deletion is announced.

**The Recycle Bin** (B7, B23, G15):
- **Opening it:** a third surface, opened from the folder list by its *Recycle Bin* button, as Settings is opened. Built when opened and released when left. No card is ever being edited there, because it is reached from the folder list.
- **Leaving it:** Back, or Escape under the Escape setting: leave in the two "leave" modes, hide in Hide, nothing in None. Focus returns to where it was in the folder list.
- **Its entries:** every folder and note in the bin, most recently deleted first; at equal times folders come before notes.
  - A folder shows how many of its notes are in the bin with it.
  - A note shows its folder, and whether that folder is itself in the bin.
- **Actions, per entry:**

  | Entry | Restore | Restore to... | Delete permanently |
  |---|---|---|---|
  | folder | `RestoreFolder`: the folder and every note in the bin that points at it (Case D) | - | `PurgeFolder`: refused while an active note lives in it (I7) |
  | note, folder live | `RestoreNote`: back to its folder | `RestoreNoteToFolder` | `PurgeNote` |
  | note, folder in the bin | not offered (I7: no active note in a binned folder) | `RestoreNoteToFolder` | `PurgeNote` |

- **Emptying the bin:** `EmptyRecycleBin`. Every note goes, then every folder that no active note lives in; a folder kept is reported.
- **Confirmation:** *Delete permanently* and *Empty Recycle Bin* cannot be undone. Each asks in a dialog that says so, with Cancel as the default button. Restore never asks.
- **After an action:** the bin is re-read. Focus goes to the next entry, else the previous, else the bin's empty state, and the result is announced.
- **Empty state:** "The Recycle Bin is empty. Deleted notes and folders are kept here until you delete them permanently."
- **Keys:** Tab reaches every control, the arrows move between entries, Shift+F10 or the Menu key opens an entry's menu. Enter restores where restore is offered; Delete deletes permanently, after the dialog.
- **Assistive technology:** each entry's name is its title. Its help text is the kind and state: "Folder, 3 notes", "Note in Work", or "Note in Work, which is in the Recycle Bin". Results are announced.
- **High Contrast:** controls and the design system's own templates only, with no new colours.
- **Reduced motion:** the surface enters through the existing `ContentMotion`, which reads Windows' animation setting per transition. The drawer itself slides its full width in from its edge (F2, #145) through the same class.

**Where the code goes:**
- `RecycleBin` (UseCases/Workspace, framework-free, tested against SQLite) lists the entries and dispatches the existing commands.
- The window shows the entries and asks for confirmation.
- `Removal` (Core/Ordering, pure) picks the next item after one leaves a list, for the folder list and the bin alike.
- `FolderDeleted` and `FolderRestored` are published after commit, joining P1.1's purge, restore and empty events.

**Verified (B5):**
- **Real SQLite** (`RecycleBinSurfaceTests`, 13):
  - a folder delete is soft, takes its notes under one timestamp and destroys nothing;
  - a second delete is refused with nothing written;
  - entries keep their order, counts and folder state;
  - Case D restore; restore refused into a binned folder; Restore to;
  - a folder is never restored into another folder;
  - purge of a note, and of a folder with its notes;
  - purging something already restored is refused;
  - empty;
  - events after commit;
  - I7 after every change.
- **Core:** `Removal` (3).
- **Guards:**
  - the bin is built only while open, from the folder list only, and released when left;
  - Escape follows the setting, and Settings returns to the bin;
  - a folder is deleted only by the confirmation's Delete;
  - the dialogs default to Cancel, and purge and empty run only on the primary choice;
  - restore is offered only where the engine allows it;
  - the controls are named, with help text on each entry's container.
- **Mutation:** 14/14 killed.
- **Runtime:** `Invoke-RecycleBin.ps1` 16/16, `Invoke-NoteStack.ps1` 52/52.

### B6 precisely: keyboard and accessibility hardening (2026-11-02)

B6 adds no feature. It audits A–B5 and fixes what the audit found.

**The keyboard map, as built** (one owner per chord per scope; checked by a guard):

| Scope | Key | Action | Precedence |
|---|---|---|---|
| anywhere | Ctrl+W | hide | root accelerator |
| anywhere | Ctrl+, | Settings | root accelerator; the editor hands it up |
| folder list | Ctrl+N | focus the new-folder box | the pane's accelerator |
| folder list | Ctrl+Down; double-click, or one click by the setting | enter the folder | PreviewKeyDown, after reorder (Enter is deliberately unbound here) |
| folder list | F2 / Delete | rename / delete (confirmed) | KeyDown on the list |
| folder list | Ctrl+Alt+Shift+Up/Down | move the folder | PreviewKeyDown, first |
| stack | Ctrl+N / Ctrl+Shift+M | new note / move to folder | the stack's accelerators, the editor included |
| stack | Ctrl+Alt+Shift+Up/Down | move the card | PreviewKeyDown, first |
| stack | Ctrl+Alt+Left/Right (+Shift) | fold / unfold (all) | PreviewKeyDown, second |
| stack | Enter, F2 | edit the card | PreviewKeyDown, only from a card, never from the editor |
| stack | Ctrl+Alt+Backspace | delete the card | the stack's KeyDown |
| editor | Ctrl+Alt+Up/Down | edit the previous or next card | the host's PreviewKeyDown (after the stack's) |
| bin | Enter / Delete | restore / delete permanently (confirmed) | PreviewKeyDown, only from an entry |
| everywhere | Escape | one level back, by the Escape setting | per surface (`WorkspaceEscape`) |
| everywhere | Shift+F10, Menu | the row's menu | `ContextRequested` |

AltGr types nothing with arrows or Backspace, so no Ctrl+Alt chord above takes a character (implementation plan §10.2).

**What the audit found, and the fix for each:**
1. **F1:** WinUI shows an automatic key-tip tooltip for a `KeyboardAccelerator` (the stray "Ctrl+N" popup). Every element that hosts accelerators sets `KeyboardAcceleratorPlacementMode="Hidden"`; the buttons' own tooltips already name their keys.
2. **F2:** pinning a folder now announces "Pinned"/"Unpinned", as pinning a card does.
3. **F3:** Enter on a bin entry that cannot be restored in place announces why, and where Restore to is, instead of doing nothing silently.
4. **F4:** the runtime case now checks the focused, recycled card's help text far down the scrolled stack, not only its stored flag.

**Announcements** (one per action, from the list where it happened; no second announcement for the same change):
- notes: created, deleted, moved, pinned, folded, fold all, reordered, dropped, refused drop;
- folders: deleted, pinned, reordered, dropped, refused drop;
- bin: restored, restored to, deleted permanently, emptied, can't restore here.

Failed saves are the stack's notice, a live region marked assertive. Creating and renaming a folder move focus to the row, which is read, so they add no announcement.

**Editing safety, every path around an active editor:**

| Path | Result |
|---|---|
| another card, Ctrl+N, move, leaving the folder | save first; a failed save stays put |
| fold | save and leave editing first; a failed save folds nothing |
| pin, reorder, drag | keep editing with the unsaved text |
| hide, close | save; a failed save keeps the window or cancels the close |
| Settings | keeps the editor and its text |
| folder delete, Recycle Bin | unreachable while editing: both live on the folder list, reached only by leaving the stack |

**Undo for deletion: deferred, not built.**
- A deleted note or folder is one action away in the Recycle Bin, with its place, content and notes kept.
- A separate Undo would need state parallel to the transaction that already recorded the deletion.
- Nothing in A–B5 shows a release-blocking gap.
- Revisit after the release-candidate usability pass.

**Left to the release-candidate campaign:**
- Narrator speech, because UI Automation notification events are not observable from the harness's client;
- High Contrast and reduced motion at runtime on a machine set up for them;
- non-US layout and AltGr checks (P9.7).

B6 adds no colour, no template and no motion.

**Verified (B6):**
- **Guards (`KeyboardAccessibilityGuardTests`, 16):**
  - every accelerator host hides the automatic key tip;
  - no chord has two owners in one scope;
  - each of the twelve window actions announces its outcome, with no more than two announcements per method;
  - the bin's actions announce, and Enter on an entry it cannot restore is not silent;
  - failed saves go through the assertive live region.
- **Mutation:** 6/6 killed, by these guards (each fix removed in turn). Phase A 19, B1 17, B2 19, B3 17, B4 15 and B5 14 re-run on this tree: all killed.
- **Runtime**, on a Release build frozen from the final source:
  - `Invoke-NoteStack.ps1` 54/54. Case 29 now checks the recycled card's container help text; case 44 checks there is no key tip while Ctrl is held; case 45 checks Tab reaches the Recycle Bin button.
  - `Invoke-RecycleBin.ps1` 16/16. In case 6, Enter on an entry that cannot be restored in place writes nothing.
- **Negative control:** the same harness against the pre-B6 build fails case 44 (a "Ctrl+N" key tip on both lists). The check is real.

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
