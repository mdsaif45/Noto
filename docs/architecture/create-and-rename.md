# Create and rename: design gate (P1.2)

**Status:** Accepted 2026-10-21. Step 1 (engine) built on `p1/2-create-rename-tmp`; step 2 (UI) waits for the card-stack surface.
**Related:**
- ADR-010 (commands and events), ADR-016 (card-stack workspace)
- deletion-semantics §13 (I7), core-note-engine-contract §11
- Parity: B1, B5, B16, C2, C13, C15, G14, J8
- Implementation-plan item P1.4. Your sequence names it P1.2; the numbering is reconciled when the plan is next amended.

Step 1 is built as specified, with two refinements:
- **Runs of breaks.** A run of line breaks or tabs becomes one space, so CRLF does not become two. Ordinary spaces typed inside a name are kept as typed.
- **Pinned selections.** A pinned selection places the new note at the top of the unpinned notes. A new note cannot sit among the pinned.

---

## 1. What exists

| Capability | Engine | UI today | Gap |
|---|---|---|---|
| Create folder | `CreateFolder(Name)`; empty or whitespace → `InvalidInput`; names not unique (Case G); placed last (O3) | folder pane: name box + Create button, Enter / Esc | name stored **untrimmed**; no event (the window refreshes by hand) |
| Rename folder | `RenameFolder(FolderId, Name)`; lifecycle checked before input; same validation | F2 → inline editor, Enter commits, Esc / focus loss cancels | untrimmed; no event; no context-menu entry |
| Create note | `CreateNote(FolderId, Content)`; always a folder (P1.1, I7); placed last (O3); `NoteCreated` | Ctrl+N on the note surface (#69), at the end | no placement choice (B5 / J8); no entry while editing (ADR-016 §5) |
| Rename note | none, by design: the title is the first line (B16, `NoteTitle`) | edit the first line | P2 (editor); no separate command |

**Duplicated logic.**
- `MainWindow` validates nothing itself; it relies on the commands, which is right.
- But it has two hand-written create flows (`SubmitCreate`, `OnCreateNoteAccelerator`) and one rename flow (`CommitRename`), each refreshing its list by hand and each announcing failures differently.
- P1.2 replaces the hand refreshes with events and keeps one flow per action in the presenters (ADR-016 §9).

## 2. Decisions

### 2.1 Names (folders)

- **Normalised once, in Core:** `FolderName.Normalise`, the same pattern as `TagName.Normalise`.
  - Leading and trailing whitespace is removed.
  - Every control character inside (line breaks, tabs) becomes one space; a pasted multi-line name stays one line.
  - Empty after that → `InvalidInput`.
  - `CreateFolder` and `RenameFolder` store the normalised name.
- **No length cap** in the engine. Nothing in the parity spec or SideNotes sets one, and a cap is a choice nobody asked for. Long names are a display concern: the row truncates with an ellipsis and the full name stays in UI Automation.
- **Duplicates stay allowed** (Case G). No warning dialog: the user may want two "Work" folders.
- **Existing untrimmed names are left as they are.** No migration rewrites user data for cosmetics. A rename normalises.

### 2.2 Note "rename"

There is no `RenameNote`. A note's title is its first line (B16), so renaming is editing, in the card (P1.2 surface, then the P2 editor). A second command would create a second source of truth.

### 2.3 New-note placement (B5, J8)

- One setting, `notes.new-placement`, with four values: `Top`, `Bottom`, `AboveSelected`, `BelowSelected`.
- **Default `Bottom`**, Noto's behaviour today.
- `CreateNote` gains `NotePlacement` (it already has `First`, `Last`, `After(sibling)`). The use case resolves the setting:

  | Setting | Resolves to |
  |---|---|
  | `Top` | `First` |
  | `Bottom` | `Last` |
  | `BelowSelected` | `After(selected)` |
  | `AboveSelected` | `After(previous active sibling in O2 order)`, or `First` when the selected note is first |

- No selection, or a selection that is no longer an active sibling: fall back to `Bottom`.
- Pinned notes sort first (O2). A new note is never pinned, so "Top" means the top of the unpinned notes. That is the same rule `ReorderNote` already follows.
- Placement and insert are **one transaction**, with renormalisation if needed (O6), as `MoveNoteToFolder` already does.

### 2.4 Events (ADR-010)

| Command | Event (after commit only) |
|---|---|
| `CreateFolder` | `FolderCreated(FolderId)` (new) |
| `RenameFolder` | `FolderRenamed(FolderId)` (new) |
| `CreateNote` | `NoteCreated(NoteId, FolderId)` (exists) |

The presenters subscribe and refresh. No surface refreshes by hand after a command.

### 2.5 Selection and focus

| After | Selection | Focus | Announcement |
|---|---|---|---|
| create folder | the new folder | stays in the name box, which is cleared, so several folders can be typed in a row | "Folder *name* created" |
| rename folder | unchanged | back on the folder's row | "Renamed to *name*" |
| create note | the new card | its editor, caret at the start (B26 placeholder shown) | "New note" |
| any refusal | unchanged | stays where it was; the input keeps its text | the failure's message (live region, assertive) |

### 2.6 Entry points (keyboard first; ADR-016 §5, plan §10)

| Action | Keyboard | Pointer | Context menu |
|---|---|---|---|
| New folder | Ctrl+N on the folder surface | Create button | folder list → "New folder" |
| Rename folder | F2 on a folder row; Enter commits; Esc cancels | double-click the name (when single-click opening is off) | folder row → "Rename" |
| New note | Ctrl+N anywhere in the stack, **including while editing** | list bottom bar + (B18) | card → "New note below"; list menu → "New note" |

### 2.7 Accessibility (cross-cutting, decision 10)

- The name box and the rename editor have UI Automation names ("New folder name", "Rename folder *name*").
- Every result above is announced through the existing live regions.
- The rename editor is reachable and dismissible by keyboard alone.
- High Contrast: the rename editor uses the text-input brushes Slice 1 already maps; no new colours.

### 2.8 Transactions

- Folder create and rename are single statements.
- Note create with placement uses one transaction for the position and the insert (§2.3).
- The I7 triggers (migration 3) stay the last line of defence for a note's folder.

## 3. Tests and mutation targets

- **Core (`FolderName.Normalise`):** trimming, control characters to spaces, emptiness, idempotence.
- **SQLite:** stored normalised; untrimmed legacy names untouched; each placement, including fallbacks and pinned siblings; renormalisation inside the create's transaction; rollback on an injected failure.
- **Dispatch:** each event after commit only; none on refusal.
- **Mutants:**
  - normalisation skipped;
  - control characters kept;
  - placement ignored;
  - the AboveSelected / BelowSelected inversion;
  - fallback missing;
  - the event published on failure.

## 4. Order of implementation (each step gated)

1. **Engine:** `FolderName`, events, `CreateNote` placement, the setting key and its use case. No UI.
2. **UI, on the card-stack surface (plan P1.2):**
   - the presenters subscribe to the events;
   - the entry points of §2.6;
   - the announcements and focus of §2.5.

Step 1 does not depend on the card stack. Step 2 does.

## 5. Open questions (none blocks step 1)

- SideNotes' own default placement is unknown (parity J8 says "the same four"). `Bottom` keeps Noto's current behaviour until observed.
