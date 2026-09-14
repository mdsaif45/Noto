# Core Note Engine — Design Gate

**Issue:** [#13](https://github.com/mdsaif45/Noto/issues/13)
**Status:** Design proposal — **not implemented**
**Date:** 2026-09-14
**Verdict:** **ACCEPTED** — the three schema prerequisites shipped in migration 002 (§10)
**Superseded in part by:** [deletion-semantics.md](deletion-semantics.md) — `DeletedWithFolderId` is **rejected** there after working the lifecycle cases

---

## 1. What this is

A design checkpoint before writing any Core Note Engine code. It exists because
three of the decisions below were never actually made — they were inherited from
a schema sketch and carried into migration 001 unexamined.

Finding them now costs a migration. Finding them after the workspace is built
costs a migration *plus* the code that assumed them.

---

## 2. Current state

| | |
| --- | --- |
| `Noto.Core` | `Ulid`, `IStorageLog`, `StorageException`. **No domain types yet.** |
| `Noto.Infrastructure` | `NotoDatabase`, `MigrationRunner`, `SchemaV1`, `NotoStoragePaths` |
| `Noto.UseCases` | Empty — marker only |
| Schema | Folders, Notes, Tags, NoteTags, Settings (migration 001, shipped) |
| Tests | 33 passing; boundary guard active |

The domain layer is genuinely empty. Nothing has to be unpicked — only the
schema decisions below.

---

## 3. Conflicts found between documents and implementation

**These are reported, not silently reconciled.**

### C1 — `Notes.Title` contradicts parity row B16

```
  parity B16:  "First line is the title ... No separate title field"
  schema:      Title TEXT NOT NULL DEFAULT ''
```

SideNotes derives the title from line 1 of the content. It has no title field,
and B16 says so explicitly.

A stored `Title` creates a second source of truth that must be kept in sync with
the content on every keystroke, and which can silently diverge. It also breaks
"the title survives folding" and "the title appears in the note URL", both of
which follow naturally from *deriving* it and awkwardly from *storing* it.

**The column was never justified.** Neither ADR-004 nor `data-model.md` explains
it; it appears in the original sketch and was carried forward.

> **Recommendation: remove `Notes.Title`.** Title becomes a computed property on
> the domain type — first non-empty line of `Content`, trimmed of markdown
> heading markers. If a measured search or list-rendering cost later justifies a
> denormalised copy, that is a cache, added deliberately, not the source of
> truth.

### C2 — Folder deletion contradicts parity row C11

```
  parity C11:  "Soft delete to recycle bin, notes included"
  schema:      FolderId ... ON DELETE SET NULL
```

The schema's `SET NULL` means deleting a folder **orphans its notes to the
root**. Parity says the notes go to the recycle bin *with* the folder.

This is precisely the case the brief warned about: a SQLite foreign-key clause
silently decided a product behaviour. It is also the wrong behaviour — a user
deleting "Project X" expects its notes to go too, and expects to get them back
together.

> **Recommendation: folder deletion is a soft delete that cascades in the
> domain**, setting `DeletedAt` on the folder and on its notes in one
> transaction. `ON DELETE SET NULL` stays on the column as a backstop for the
> hard delete that empties the recycle bin, but it is no longer the mechanism
> for user-facing deletion.

### C3 — `Folders` cannot satisfy parity rows C8 and C11

| Parity row | Requires | Schema has |
| ---------- | -------- | ---------- |
| C8 Pin folders | `IsPinned` | **missing** |
| C11 Delete folder (soft) | `DeletedAt` | **missing** |

`Notes` has both. `Folders` has neither, so folders currently cannot be pinned
or soft-deleted at all.

> **Recommendation: add `IsPinned` and `DeletedAt` to `Folders`** in migration
> 002.

### C4 — `data-model.md` is stale

It still shows `INTEGER PRIMARY KEY` and a `Color` column; the shipped schema
uses ULID `TEXT` and `ColorKey`. An implementation note was added at the top in
#8, but the SQL blocks below it were not updated.

> **Recommendation: update the document to match what shipped.** Low risk, but
> a stale schema document is how the next person makes a wrong assumption.

---

## 4. Requirements, scoped

### Must exist in #13

Every item traces to a parity **MUST** row.

| Capability | Parity |
| ---------- | ------ |
| Create, read, update, delete a note | B1, B7 |
| Note content as markdown source | ADR-004 |
| Title derived from line 1 | **B16** |
| Move a note between folders | B11 |
| Reorder notes, precisely (before/after) | B8, B10 |
| Pin / unpin a note | B14 |
| Note colour, from a palette key | B15 |
| Fold / unfold a note | B12 |
| Create, rename, delete a folder | C2, C11, C13 |
| Pin a folder | **C8** |
| Reorder folders | C9 |
| Create, rename, delete a tag; assign and remove | inventory |
| Soft delete and restore, for notes and folders | B23, **C11** |
| Timestamps that mean something | B21, B22 |

### Should exist soon — not #13

Folder sorting options (C10, provisional), note-count queries (B18), recent
folders (C5). All are **query** concerns over the same model; none changes it.

### Deferred — explicitly outside #13

Attachments, FTS5 search (#17), presentations (ADR-009), context bindings (M6),
export (#15), markdown *rendering* (#14).

### Not required — do not introduce

Folder hierarchy (C3 says flat), tag hierarchy or groups, note versioning,
per-note width (B25 says notes fill the panel), collaboration, sync.

---

## 5. Proposed domain model

```
  Note
   ├── Id            NoteId (ULID)      stable identity; survives export,
   │                                    round-trips, addresses noto:// URLs
   ├── Content       string             markdown SOURCE — the canonical form
   ├── Title         (computed)         first non-empty line of Content (B16)
   ├── FolderId      FolderId?          null = root; flat, no hierarchy
   ├── ColorKey      ColorKey?          palette key, never a hex value
   ├── IsPinned      bool               B14
   ├── IsFolded      bool               B12  ← see note below
   ├── SortOrder     double             explicit order (§7)
   ├── CreatedAt     DateTimeOffset     UTC
   ├── UpdatedAt     DateTimeOffset     UTC
   └── DeletedAt     DateTimeOffset?    null = live; set = in recycle bin

  Folder
   ├── Id            FolderId (ULID)
   ├── Name          string             required, non-empty
   ├── ColorKey      ColorKey?
   ├── IsPinned      bool               C8  ← NOT in schema yet
   ├── IsCollapsed   bool               list-state  ← see note below
   ├── SortOrder     double             C9
   ├── CreatedAt     DateTimeOffset
   ├── UpdatedAt     DateTimeOffset
   └── DeletedAt     DateTimeOffset?    C11  ← NOT in schema yet

  Tag
   ├── Id            TagId (ULID)
   ├── Name          string             case-insensitively unique
   ├── ColorKey      ColorKey?
   └── CreatedAt     DateTimeOffset

  NoteTag            (NoteId, TagId)    join only; no identity of its own
```

### Fields that needed an argument

**`Title` is computed, not stored.** See C1. One source of truth.

**`IsFolded` and `IsCollapsed` are the uncomfortable pair.** Both are arguably
*presentation* state, and ADR-009 says presentation does not live on the domain
type.

The distinction that justifies keeping them: ADR-009 forbids the domain knowing
about **windows, coordinates, monitors and z-order** — where a thing is *drawn*.
Folded-ness is not a rendering coordinate; it is a user-authored property of the
note that must persist identically in every surface. SideNotes treats it as note
state (B12: it survives, and the title shows while folded).

The counter-argument is real, so the test is: *would two different presentations
of the same note ever disagree about it?* For folding — no; a folded note is
folded wherever it appears. For window position — yes, which is exactly why that
lives in `NotePresentations`.

**Keeping them, with that reasoning recorded.** If a second presentation ever
needs its own fold state, it moves — and ADR-009's design makes that move cheap.

**Strongly-typed ids** (`NoteId`, `FolderId`, `TagId` as readonly record
structs over a string). Cheap, and it makes `MoveNote(noteId, folderId)`
impossible to call with the arguments swapped — a bug that is otherwise silent
and data-corrupting.

**No `IsArchived`.** The original sketch had one. Archive is not a parity MUST,
and soft delete already covers "out of the way but recoverable". Adding a second
hidden-ness axis invites every query to handle four states instead of two.

---

## 6. Use cases

Commands per ADR-010. **Queries are not commands** — reading goes direct.

### MUST — #13

```
  CreateNote          UpdateNoteContent   DeleteNote        RestoreNote
  MoveNoteToFolder    ReorderNote         PinNote/UnpinNote
  SetNoteColor        FoldNote/UnfoldNote
  CreateFolder        RenameFolder        DeleteFolder      RestoreFolder
  PinFolder/UnpinFolder                   ReorderFolder
  CreateTag           RenameTag           DeleteTag
  AssignTagToNote     RemoveTagFromNote
```

Queries: `GetNote`, `ListNotesInFolder`, `ListFolders`, `ListTags`,
`ListNotesForTag`, `ListDeleted`.

### DEFER

`EmptyRecycleBin` and retention (needs a policy decision), `DuplicateNote`
(B6, SHOULD), search (#17), export (#15).

### No `NoteService`

Each command gets its own handler. A `NoteService` accumulating twelve methods
is the god-object the brief warns about, and it makes the ADR-010 rule
("every mutation goes through a command") unenforceable.

---

## 7. Ordering — explicit, never incidental

**Never** insertion order, `rowid`, or timestamps.

`SortOrder REAL` with midpoint insertion (already in the schema, and the
rationale is already in `data-model.md`):

```
  before:  A=1.0    B=2.0    C=3.0
  drop X between A and B  ->  X = 1.5      one row written, not four
  before:  A=1.0    X=1.5    B=2.0
```

Rules:

1. **Ordering is scoped per folder.** Root (`FolderId IS NULL`) is its own scope.
2. **Pinned first.** `ORDER BY IsPinned DESC, SortOrder ASC, Id ASC` — the `Id`
   tiebreak makes it total, and because ULIDs are creation-ordered (ADR-012),
   equal `SortOrder` falls back to creation order rather than to chance.
3. **Insert at an end** uses min−1 or max+1. No renumbering.
4. **Moving between folders** assigns a new `SortOrder` in the target scope.
   One transaction (§9).
5. **Deleting leaves gaps.** Gaps are harmless.
6. **Renormalise when the gap between neighbours falls below a threshold.**
   `double` has ~52 bits of mantissa, so repeated midpoint insertion between the
   same pair exhausts precision after roughly 50 operations — reachable by a
   user who repeatedly drags one note to the same spot. Renormalisation rewrites
   that folder's `SortOrder` to 1, 2, 3… in one transaction.

> Point 6 is the one that gets skipped and then produces a bug nobody can
> reproduce. It needs a test that performs ~60 midpoint inserts at the same
> position and asserts the order still holds.

---

## 8. Deletion semantics

| Action | Behaviour | Why |
| ------ | --------- | --- |
| Delete note | Soft — set `DeletedAt` | B23; data cannot be recovered from a server (ADR-002) |
| Delete folder | Soft — set `DeletedAt` on folder **and its notes**, one transaction | **C11**, resolving conflict C2 |
| Restore folder | Restore folder and the notes deleted *with* it | Needs the grouping below |
| Delete tag | **Hard** — row removed, `NoteTags` cascades | A tag is a label, not content. Losing one loses nothing recoverable. |
| Empty recycle bin | Hard delete | Deferred — needs a retention policy |

### Restoring a folder — resolved without a new column

An earlier draft of this document proposed a `Notes.DeletedWithFolderId` column
to identify which notes were deleted *with* a folder.

**That proposal is rejected.** Working through the full lifecycle in
[deletion-semantics.md](deletion-semantics.md) showed that `FolderId` plus
`DeletedAt` already answer every case, and that the column breaks on the
"restore one note out of a deleted folder, then restore the folder" case unless
it carries its own invariant.

```sql
-- restore folder F: no marker needed
UPDATE Folders SET DeletedAt = NULL WHERE Id = @F;
UPDATE Notes   SET DeletedAt = NULL WHERE FolderId = @F AND DeletedAt IS NOT NULL;
```

This also restores a note the user had deleted individually before the folder —
and that is the better outcome: the user asked for the folder back as it last
existed, and does not remember a deletion from three weeks ago.

> The lesson is worth recording: the column looked correct because it solved the
> restore *query*. It was wrong because the *lifecycle* had not been worked
> through first.

## 9. Transactions

Atomic units — anything that would leave the store inconsistent if half-applied:

```
  DeleteFolder     folder + its active notes
  RestoreFolder    folder + its still-deleted notes
  MoveNoteToFolder folder change + new SortOrder
  ReorderNote      the write + any renormalisation
  DeleteTag        tag + NoteTags cascade
```

Single-row updates need none. `SqliteTransaction` directly — no framework.

**Concurrency:** single user, single process. WAL plus a 5s busy timeout
(already configured) is sufficient. Last-write-wins on the rare double-write;
no version columns, no optimistic concurrency, no conflict resolution. That
machinery belongs to sync, which does not exist.

---

## 10. Required schema changes — migration 002

**This is what makes the verdict HOLD.** Three changes, all cheap now:

| # | Change | Reason |
| - | ------ | ------ |
| 1 | **Drop `Notes.Title`** | Parity B16 — title is derived (conflict C1) |
| 2 | **Add `Folders.IsPinned`** | Parity C8 (conflict C3) |
| 3 | **Add `Folders.DeletedAt`** + partial index | Parity C11 (conflict C3) |


Nothing has shipped to a user and the domain layer is empty, so this costs one
migration and no code. After #13 it would cost a migration *plus* every query,
handler and test that assumed a stored title.

> SQLite cannot drop a column in place before 3.35, and `Microsoft.Data.Sqlite`
> ships a build where `DROP COLUMN` is available — but the migration should use
> the explicit table-rebuild pattern regardless, because it is the pattern every
> later structural change will need, and proving it once on an empty database is
> free.

---

## 11. Persistence mapping

Direction is **domain → persistence**, never the reverse:

```
  Note.Id          -> Notes.Id           TEXT, ULID
  Note.Content     -> Notes.Content      TEXT, markdown source
  Note.Title       -> (not persisted)    computed from Content
  Note.FolderId    -> Notes.FolderId     TEXT NULL
  Note.ColorKey    -> Notes.ColorKey     TEXT NULL
  Note.IsPinned    -> Notes.IsPinned     INTEGER 0/1
  Note.IsFolded    -> Notes.IsFolded     INTEGER 0/1
  Note.SortOrder   -> Notes.SortOrder    REAL
  Note.CreatedAt   -> Notes.CreatedAt    TEXT, ISO-8601 UTC
  Note.DeletedAt   -> Notes.DeletedAt    TEXT NULL
```

**Repository interfaces live in `Noto.Core`, implementations in
`Noto.Infrastructure`** — the domain declares what it needs, the outer layer
provides it.

Three repositories (`INoteRepository`, `IFolderRepository`, `ITagRepository`),
because there are three aggregates. **Not** a generic `IRepository<T>`: it would
force every query into a lowest common denominator and add nothing.

No separate persistence models. The domain types map directly; introducing DTOs
for five flat tables is ceremony.

---

## 12. Dapper — recommend removing

Evidence from the actual schema and the #13 query list:

```
  tables                 5
  columns               26, all flat scalars
  object graphs          none
  joins                  one, via NoteTags
  queries in #13        ~15, all single-table or one join
```

| | `Microsoft.Data.Sqlite` alone | + Dapper |
| --- | --- | --- |
| Mapping | explicit reader loop per query | one line |
| Dependency | none added | one more to audit and update |
| Visibility | row → domain mapping is on screen | mapping is implicit |
| Startup | none | negligible but non-zero |

Dapper's win is removing reader boilerplate. Its cost is a dependency and an
indirection at exactly the layer where, this early, **seeing the mapping is
valuable** — a wrong column read is a data bug, and explicit code makes it
reviewable.

> **Recommendation: remove the unused Dapper reference.** Revisit if #13's
> mapping code is genuinely repetitive enough to justify it — a decision to make
> with the code in front of us, not in advance. Removing an unused dependency is
> also strictly reversible.

---

## 13. Canonical content — verified

ADR-004's requirement holds in this design:

- `Note.Content` is **markdown source**, the only persisted form
- `Title` is derived from it — not a parallel representation
- the domain references no renderer, editor, HTML, WinUI control or WebView
- the architecture guard already fails the build on such a reference

The editor (#14) consumes and produces markdown source. It is replaceable
without touching the domain, which is the point.

---

## 14. Test plan

Meaningful behaviour, not coverage.

**Domain (`Noto.Core.Tests`) — no database:**
- title derives from line 1; heading markers stripped; empty and whitespace-only
  content; content that is only a heading
- strongly-typed ids do not interchange
- validation: empty folder name rejected; unknown colour key rejected

**Ordering (`Noto.Infrastructure.Tests`):**
- insert between two notes writes **one** row
- pinned sort above unpinned
- equal `SortOrder` falls back to id (total order)
- **~60 repeated midpoint inserts at the same position, then assert order** —
  the precision-exhaustion case
- ordering is per-folder; root is its own scope
- ordering survives reopen

**Deletion:**
- deleting a folder soft-deletes its notes, in one transaction
- restoring a folder restores exactly the notes deleted with it, and **not**
  notes already individually in the bin
- deleted notes are excluded from listings and included in the bin
- deleting a tag removes `NoteTags` and leaves notes intact
- a failed multi-step delete rolls back completely

**Persistence:**
- every entity round-trips
- markdown content survives verbatim, including newlines and unicode
- migration 002 upgrades a **populated** v1 database (ADR-003's rule)

**Architecture:**
- existing guard still passes with domain types present

---

## 15. Risks

| Risk | Impact | Decision | Blocks #13? |
| ---- | ------ | -------- | ----------- |
| `Notes.Title` as a second source of truth | Silent divergence from content; contradicts B16 | Drop the column | **YES** |
| `ON DELETE SET NULL` orphans notes | Wrong product behaviour, contradicts C11 | Domain-level cascading soft delete | **YES** |
| `Folders` cannot pin or soft-delete | Two parity MUSTs unimplementable | Add columns | **YES** |
| Folder restore cannot identify its notes | Users lose notes on restore | **Resolved without a column** — `FolderId` + `DeletedAt` suffice (deletion-semantics.md §5) | No |
| Migration 002 transforms user data (`Title`) | Silent data loss if mishandled | Preserve title into content; **take a backup before migrating** (deletion-semantics.md §8, §9) | **YES** |
| `SortOrder` precision exhaustion | Ordering silently breaks after ~50 same-spot drags | Renormalise, with a test | No — design settles it |
| Fold state on the domain type | Possible ADR-009 tension | Reasoned in §5; move later if a second presentation disagrees | No |
| Unused Dapper dependency | Dead weight | Remove | No |
| Stale `data-model.md` | Next reader makes a wrong assumption | Update | No |
| #30/#31/#32 spikes open | — | UI/platform only; no bearing on the domain | No |

---

## 16. Architecture review

| Area | Rating | Evidence |
| ---- | ------ | -------- |
| Core boundaries | **GOOD** | plain `net9.0`, zero package references, guard enforced and proven to fail |
| Domain model | **ATTENTION** | sound once the four schema conflicts are resolved; unresolved, it encodes a contradiction |
| Use-case boundaries | **GOOD** | command per operation, no god service, queries stay direct |
| Persistence boundary | **GOOD** | interfaces in Core, implementations outside, no generic repository |
| Testability | **GOOD** | domain testable with no database; ordering and deletion testable with one |
| Extensibility | **GOOD** | presentations, attachments, FTS5 and context all attach without changing these types |
| Complexity | **GOOD** | 3 aggregates, 1 join, no framework, one dependency proposed for **removal** |

---

## 17. Verdict

# ACCEPTED

This gate was held at **HOLD** until migration 002 landed. **It has**, on `main`
at `8b86a5a`, and every prerequisite is satisfied:

| Prerequisite | Status |
| ------------ | ------ |
| drop `Notes.Title` (parity B16) | ✅ migration 002, with existing titles preserved into content per [deletion-semantics.md](deletion-semantics.md) §8 |
| add `Folders.IsPinned` (parity C8) | ✅ migration 002 |
| add `Folders.DeletedAt` (parity C11) | ✅ migration 002 |
| remove the unused Dapper reference | ✅ no package reference remains |

Verified against the real database rather than the C# definitions:
`user_version = 2`, `foreign_keys = 1`, `foreign_key_check = 0`, `Notes` has no
`Title`, `Folders` has both new columns, and existing `NoteTags` relationships
survived as a set.

**No substantive design decision changed when this verdict was updated.** The
model in §5, the ordering rules in §7, the deletion semantics in §8 and the
transaction boundaries in §9 are exactly as reviewed. The only edit was removing
two stale words ("deletion grouping") that described the rejected
`DeletedWithFolderId` mechanism.

The remaining non-blocking cleanup is to update the stale SQL blocks in
`data-model.md`; its header already carries an implementation note.

**On approval, the order is:** migration 002 → domain types → repositories →
commands → tests. Implementing the domain against the current schema would
build code on a contradiction with the parity specification.
