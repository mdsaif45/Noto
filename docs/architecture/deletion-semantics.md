# Deletion Semantics — Design Gate

**Related:** [#13 design gate](core-note-engine-design.md), migration 002
**Status:** Proposal — **not implemented**
**Date:** 2026-09-14
**Verdict:** **ACCEPTED** — `DeletedWithFolderId` rejected (§5); the revised migration shipped as 002

---

## 1. Why this document exists

The #13 design gate proposed a `Notes.DeletedWithFolderId` column to make folder
restore correct. That was challenged on the grounds that it solves today's case
without the lifecycle being established first.

The challenge was right, and working through the cases changes the answer.

---

## 2. The finding that reframes everything

**The recycle bin is not a parity requirement.**

```
  C11  Delete folder ............ MUST    "notes included"
  B7   Delete note .............. BETTER  soft delete
  B23  Trash / restore .......... BETTER  "SideNotes has None.
                                           Recovery is backups only"
```

Only the *"notes included"* half of C11 is a MUST. The recycle bin itself is a
**`BETTER` row** — something Noto adds because ADR-002 means data cannot be
recovered from a server.

Two consequences:

1. **There is no external authority to copy.** SideNotes has no trash, so
   "what does SideNotes do?" has no answer. We are designing this ourselves.
2. **The simplest model that is correct wins**, because every behaviour here is
   a cost we are choosing to take on, not one parity forces.

This is also why the architecture hierarchy matters:

```
  product requirement   "deleting a folder must not scatter its notes"   (C11)
        ↓
  domain behaviour      what deleted/restored actually mean
        ↓
  persistence           whichever columns represent that
```

C11 does **not** say "therefore a `DeletedWithFolderId` column exists". It
constrains behaviour; the schema is our choice.

---

## 3. The lifecycle

Two states per entity. Not four.

```
  ACTIVE  ──delete──>  DELETED  ──restore──>  ACTIVE
                          │
                          └──purge──>  (gone)
```

"Restored" is not a state — it is a transition back to `ACTIVE`. "Permanently
deleted" is not a state — it is the absence of a row. Modelling either as a
state adds a column that is always derivable.

**A folder and its notes are independent entities that happen to be deleted
together.** That is the whole insight, and it is what removes the need for a
tracking column.

---

## 4. The seven cases

| | Scenario | Behaviour | Rationale |
| - | -------- | --------- | --------- |
| **A** | Folder with active notes is deleted | Folder → `DELETED`. Its **active** notes → `DELETED`. One transaction. | C11: "notes included" |
| **B** | A note was already deleted, *then* its folder is deleted | The already-deleted note is **untouched** — its `DeletedAt` keeps its original value | It was already in the bin. Re-stamping it would lose when the user actually deleted it. |
| **C** | Folder deleted; one of its notes is restored individually | **Refused since P1.1 (§13, I7).** Was: allowed. The note returns to `ACTIVE` with `FolderId` still pointing at the deleted folder. | See §6 — this is the case that kills the tracking column |
| **D** | Deleted folder is restored | Folder → `ACTIVE`. Notes that are still `DELETED` **and** point at this folder → `ACTIVE`. | See §5 for why this is right without a marker |
| **E** | Folder deleted; one of its notes is purged; folder restored | The purged note is gone. Everything else restores. | Purge is irreversible by definition |
| **F** | Folder is purged | Its **still-deleted** notes are purged with it. Notes restored out of it beforehand (case C) survive, moved to root. | A purged folder cannot be referenced |
| **G** | Restored folder's name collides with an existing folder | **Allowed.** Names are not unique. | Only `Tags.Name` is unique. Two folders called "Work" is a display question, not a data one. |

Cases **E, F and G** are only reachable through purge and rename flows that #13
does not build. They are recorded so the model is complete, and marked
**deferred** in §8.

---

## 5. Why `DeletedWithFolderId` is rejected

The column was proposed to answer: *on restore, which notes came with the
folder?*

Work case D through without it:

```sql
-- restore folder F
UPDATE Folders SET DeletedAt = NULL WHERE Id = @F;

UPDATE Notes SET DeletedAt = NULL
WHERE FolderId = @F AND DeletedAt IS NOT NULL;
```

Does that wrongly resurrect a note the user deleted *individually* before the
folder? **It restores it too** — and on inspection that is the better outcome,
not a defect.

The user's model is *"I deleted Project X. Give me Project X back."* They do not
remember that three weeks ago they deleted one note inside it. Restoring the
folder as it last existed is the least surprising result, and the note is one
keystroke from being deleted again.

The alternative — a folder that restores with a hole in it, and a note left
stranded in the bin pointing at a now-active folder — is harder to explain and
harder to recover from.

### What the column would actually cost

| Cost | Detail |
| ---- | ------ |
| Correctness | Case C breaks it. Restore a note individually, then restore the folder: the marker is stale and must be cleared, or the note is double-counted. The column needs its own invariant. |
| Schema | A nullable FK that is meaningful only while a row is deleted — a column whose validity depends on another column's value |
| Purge | Case F has to null it out, or it dangles |
| Queries | Every restore path must decide whether to trust it |

It adds a second, weaker source of truth for a relationship `FolderId` already
expresses.

### Options compared

| | Correctness | Restore | Schema | Stale refs | Verdict |
| --- | --- | --- | --- | --- | --- |
| **A. `DeletedWithFolderId`** | Fails case C without extra rules | Exact, when maintained | +1 nullable FK | Yes — cases C and F | **Rejected** |
| **B. Deletion batch/group table** | Correct | Exact | +1 table, +1 FK | Yes | **Rejected** — a table to record something `FolderId` already records |
| **C. `FolderId` + `DeletedAt` only** | Correct for every case | Restores the folder as it last was | **No change** | None | **Recommended** |

**Option C. No new column.** `DeletedAt` says *whether*; `FolderId` says *where*.
Together they answer every case.

> This is the smallest model that correctly represents the behaviour — which was
> the instruction. The earlier proposal added a column because it was solving
> the restore query in isolation rather than the lifecycle.

---

## 6. Query invariants

The point of stating these once: **no repository method should independently
remember how deletion works.**

### I1 — Active by default

Every ordinary query filters `DeletedAt IS NULL`. Listing notes, listing
folders, counting, searching, exporting.

### I2 — The bin is explicit

Deleted entities are returned **only** by a method whose name says so
(`ListDeletedNotes`, `ListDeletedFolders`). There is no flag parameter on the
normal methods — a `bool includeDeleted` defaulting to `false` is the shape that
eventually gets passed `true` by accident.

### I3 — Restore is scoped

Restoring a folder touches that folder and notes whose `FolderId` matches it.
It never touches notes in other folders or at root.

### I4 — Purge leaves nothing dangling

Purging a note deletes its `NoteTags` rows (already cascading). Purging a folder
purges its still-deleted notes and nulls `FolderId` on any active note still
pointing at it.

### I5 — Deleted entities are inert

A deleted note cannot be edited, moved, reordered, pinned or tagged. The only
legal transitions are **restore** and **purge**. This prevents the "edited
something in the bin, then restored it" class of bug.

### I6 — Ordering ignores the deleted

`SortOrder` is only meaningful among active siblings. Deleted rows keep their
value — so restore returns a note roughly where it was — but never participate
in ordering queries.

**Enforcement:** these live in the repository implementations, each with a test.
`I1` additionally gets a test that asserts a deleted note is absent from *every*
list method, so a new method that forgets the filter fails.

---

## 7. Revised migration 002 specification

**Not implemented in this task.** Specification only.

```
Migration 002 — align schema with parity

REMOVE
  Notes.Title                 parity B16: "No separate title field"

ADD
  Folders.IsPinned  INTEGER NOT NULL DEFAULT 0 CHECK (IsPinned IN (0,1))
                              parity C8
  Folders.DeletedAt TEXT NULL
                              parity C11
  INDEX IX_Folders_Deleted ON Folders (DeletedAt) WHERE DeletedAt IS NOT NULL
  (and make IX_Folders_SortOrder partial: WHERE DeletedAt IS NULL)

CHANGE
  nothing else

NOT ADDED
  Notes.DeletedWithFolderId   rejected — see §5

DATA TRANSFORMATION
  see §8 — Title is preserved into Content, never discarded

ROLLBACK
  the whole migration runs in one transaction; failure leaves a v1 database
  byte-for-byte unchanged. There is no down-migration — see §9.
```

Three changes, not four. The rejected column is the difference.

---

## 8. Existing data: v1 → v2

**The critical part. `Notes.Title` holds user data and must not be discarded.**

A v1 database can contain notes where `Title` and `Content` disagree. Dropping
the column silently would destroy whatever the user typed as a title.

### The rule

```
  Title is empty                      -> drop it, nothing lost
  Content's first line == Title       -> drop it, already represented
  otherwise                           -> prepend Title to Content as a heading
```

```sql
-- only where the title carries information the content does not
UPDATE Notes
SET Content = '# ' || Title || char(10) || char(10) || Content
WHERE Title <> ''
  AND Content NOT LIKE Title || '%'
  AND Content NOT LIKE '# ' || Title || '%';
```

Then rebuild the table without the column.

### Why a table rebuild rather than `DROP COLUMN`

SQLite gained `DROP COLUMN` in 3.35, and the shipped `Microsoft.Data.Sqlite` has
it. The rebuild is used anyway because:

- it is the pattern every later structural change needs, and proving it once
  while the database is empty is free
- it lets the new columns, the new `CHECK`, and the partial indexes be created
  in their final form rather than patched on
- `DROP COLUMN` silently fails on columns referenced by an index or a partial
  index predicate — a trap worth not walking into

```
  PRAGMA foreign_keys = OFF          (inside the transaction)
  CREATE TABLE Notes_new (...)       final shape, no Title
  INSERT INTO Notes_new SELECT ...   from Notes, after the UPDATE above
  DROP TABLE Notes
  ALTER TABLE Notes_new RENAME TO Notes
  recreate indexes
  PRAGMA foreign_key_check           must return no rows
  PRAGMA foreign_keys = ON
```

> `foreign_keys` must be toggled **outside** any active transaction to take
> effect — a documented SQLite behaviour that silently no-ops otherwise. The
> migration runner currently sets pragmas per connection and wraps each
> migration in a transaction, so **this migration needs the runner to support a
> pre/post pragma step, or must use `legacy_alter_table` semantics carefully.**
> That is an implementation detail to resolve when writing it, and it is flagged
> here because it is exactly the kind of thing that is discovered at the wrong
> moment.

### Upgrade-path tests required

Not just fresh-database tests:

- v1 database with notes whose `Title` is empty → content unchanged
- v1 with `Title` matching line 1 → content unchanged, no duplicate heading
- v1 with `Title` differing from content → title preserved as a heading
- v1 with folders, notes, tags and `NoteTags` → **all rows survive, all
  relationships intact** (`PRAGMA foreign_key_check` returns nothing)
- v1 with a note referencing a folder → FK still valid after the rebuild
- migration failure mid-rebuild → v1 database unchanged
- v1 → v2 → reopen → still v2, no re-run

---

## 9. Rollback

**There is no down-migration, deliberately.**

Migrations are append-only (ADR-003). A v2 database opened by a v1 build is
refused with `SchemaTooNew` — already implemented and tested.

Recovery from a bad migration is the **pre-migration backup**, not a reverse
script. `data-model.md` already specifies backups to `backups\` as plain `.db`
snapshots; taking one before migrating is a runner responsibility that does not
exist yet and should be added **with** migration 002, because this is the first
migration that transforms user data.

> That is a genuine gap: migration 001 creates tables on an empty file, so a
> backup was never needed. Migration 002 rewrites a table containing user notes.

---

## 10. Remaining decisions, unchanged from the #13 gate

Confirmed by this analysis, not re-opened:

- **Title derived from line 1**, no cached column. If list rendering later proves
  slow at scale, that is a measured problem with a measured fix — not a reason
  to ship two sources of truth now.
- **`Folders.IsPinned`** — default 0; pinned folders sort first; pinning is
  independent of `Note.IsPinned`; a pinned folder can be deleted and retains its
  pin state through restore (it is folder state, not bin state).
- **Ordering** — the *requirement* is deterministic, persistent, per-scope
  ordering with a total tiebreak. `SortOrder REAL` with midpoint insertion is
  the proposed mechanism and can change without changing the requirement.
- **Dapper** — remove. 5 tables, 26 flat columns, one join.

---

## 11. Deferred, explicitly

| Deferred | Why |
| -------- | --- |
| Purge / empty bin (cases E, F) | Needs a retention policy; the bin has no UI yet |
| Bin UI and restore affordance | M2 workspace |
| Retention limits (age or count) | Product decision, no data yet |
| Restoring into a purged folder | Unreachable until purge exists |

**#13 implements:** delete and restore for notes and folders, with the
invariants in §6. The bin is reachable through repository queries; its surfacing
is M2.

---

## 12. Verdict

# ACCEPTED

This document opened as **REQUEST CHANGES** against the earlier #13 proposal,
not against the repository. Those changes were made and have shipped.

| Earlier proposal | Resolution | Status |
| ---------------- | ---------- | ------ |
| `DeletedWithFolderId` column | **Rejected** — `FolderId` + `DeletedAt` answer every lifecycle case (§5) | ✅ never added |
| Four schema changes | Reduced to **three** | ✅ migration 002 |
| Title data handling unspecified | Specified (§8) — preserved into content, never discarded | ✅ implemented and tested |
| Backup before migration unaddressed | Flagged as a runner gap (§9) | ✅ `MigrationSafetyCopy`, scoped to migration safety only |

Migration 002 additionally established a capability this document did not
anticipate: **per-migration control of foreign key enforcement.** It is not
optional. A probe showed `PRAGMA foreign_keys` is a no-op inside a transaction,
and that rebuilding `Notes` with enforcement on silently empties `NoteTags`
while `foreign_key_check` still reports clean.

**Nothing in §3–§6 changed.** The lifecycle, the seven cases, the rejection of
the tracking column and the six query invariants are exactly as reviewed, and
they remain the specification for #13.

### Still deferred, unchanged

Purge / empty bin, bin UI, retention limits, and restoring into a purged
folder. **#13 implements delete and restore for notes and folders**, with the
§6 invariants. The bin is reachable through repository queries; surfacing it is
M2.

---

## 13. P1 amendment: purge, empty, and nothing unreachable (2026-10-20)

The deferred rows in §11 are now decided, for P1.1 (engine) and P1.3 (Recycle Bin UI). §3–§6 are unchanged; this section only adds.

### Retention

**None automatic in v1.** Nothing leaves the bin unless the user purges it. A silent age or count limit would be data loss the user did not ask for. The bin's size is a P10 scale case, not a reason to delete.

### New commands

| Command | Precondition | Effect | Failure |
|---|---|---|---|
| `PurgeNote(NoteId)` | the note is deleted | the row is removed; `NoteTags` cascade (I4) | `NotFound`; `InvalidState` if active |
| `PurgeFolder(FolderId)` | the folder is deleted, **and no active note references it** | the folder and its still-deleted notes are removed in one transaction (case F) | `NotFound`; `InvalidState` if active, or if an active note still references it |
| `EmptyRecycleBin` | - | every deleted note, and every deleted folder that no active note references, removed in one transaction; returns what was removed and what was kept | none (an empty bin succeeds) |
| `RestoreNoteToFolder(NoteId, FolderId)` | the note is deleted; the target folder is active | restore and move, **one transaction** | `NotFound`; `InvalidState` |

### I7: no active note is left where no surface reaches it

C1 is a two-level model (folders → notes). There is no surface for notes at the root, nor for active notes inside a deleted folder. Two rules follow:

1. **Case F changes.** A deleted folder that an active note still references is **not purged**. Purging it would move that note to the root (I4), where no surface shows it.
   - `PurgeFolder` refuses (`InvalidState`).
   - `EmptyRecycleBin` keeps that folder and reports it.
   - The user restores the folder, or moves the note.
2. **Restoring into a folder in the bin is refused, by the engine** (P1.1). This closes Case C's restore path; the original text below said Case C stays legal in the engine.
   - `RestoreNote` of a note with no live folder to return to (its folder is in the bin, or it has none) fails with `InvalidState` and changes nothing.
   - `RestoreNoteToFolder` refuses a target in the bin.
   - The bin therefore offers two routes. **Restore folder** (case D) brings back the folder with its notes. **Restore into…** (`RestoreNoteToFolder`) brings the note into a live folder.
   - An active note inside a binned folder now exists only in data written before P1.1. The engine still lets commands act on such a note (move, pin, edit, reorder), so it can be moved out. `PurgeFolder` and `EmptyRecycleBin` never strand it.

### I7 is executable

- `RecycleBinRules` (Core) holds the decisions:
  - `Reaches` states I7;
  - the `Refuse…` methods return each refusal.
- The repositories re-decide the same conditions inside the transaction that writes, so a check and its write cannot be split.
- `RecycleBinPurgeTests` ends every scenario by asserting I7 on the whole database: no active note in a binned or missing folder.

### Results and idempotence

| Command | Repeated | Result | Effect |
|---|---|---|---|
| `PurgeNote` | on a note already purged | `NotFound` (contract §6) | none; no event |
| `RestoreNoteToFolder` | on a note already restored | `InvalidState` | none; no event |
| `EmptyRecycleBin` | on an empty bin | success, with zero counts | none |

Events are published only after the transaction commits, by the registry, and never on a refusal or an exception:
- `NotePurged`, `NoteRestored`, `FolderPurged`, `RecycleBinEmptied`;
- they carry identities and counts, never content.

### No active note without a folder (closed in P1.1)

C1 is a two-level model (folders → notes) and SideNotes has no notes outside a folder: its global create asks for one. No Noto surface ever created a note at the root. So the root is not a place an active note can be.

- **Commands cannot name it.** `CreateNote` and `MoveNoteToFolder` take a `FolderId`, not a nullable one: no entry point (window, tray, a future CLI) can express "no folder".
- **The database refuses it.** Migration 3 adds two triggers that abort any insert or update leaving an active note without a folder. That includes the foreign key's `ON DELETE SET NULL` when a folder row is removed under an active note; the persistence test proves the action is refused.
- **Existing ones are repaired explicitly.** Migration 3 moves any active note that already has no folder to the recycle bin, stamped at the upgrade, behind the pre-migration safety copy. Nothing is lost, nothing is hidden, and no folder is invented. It comes back only through `RestoreNoteToFolder`, into a folder the user chooses; a plain `RestoreNote` refuses it, having no live folder to return to.
- **A deleted note may still have no folder.** That is how the repaired notes wait in the bin.

I7's test oracle is now strict: every active note lives in a live folder.

### Follow-up: the refusal message under a race (correctness hardening, not a contract change)

`RestoreNote` checks the note's lifecycle, then writes. If another writer restored the note between the two, the repository refuses the write and the command reports "no live folder to return to" rather than "not deleted". The outcome is right: a refusal, nothing changed, no event. Only the wording is wrong.

It is not observable through any current entry point:
- every command is dispatched on the UI thread;
- one process owns a data root (ADR-013);
- the planned CLI routes through that owner (implementation plan R12).

When a second writer becomes possible, the repository should report why it refused, and the handler should map that.

### Purge and later content

Attachments (P3) are removed by the same purge transactions: the P3.0 design extends this table, never a second purge path.
