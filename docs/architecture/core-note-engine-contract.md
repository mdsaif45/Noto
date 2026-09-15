# Core Note Engine — Implementation Contract

**Issue:** [#13](https://github.com/mdsaif45/Noto/issues/13)
**Status:** **FROZEN** — complete; implementation may proceed against this document
**Gate:** documentation / contract only — **no production code, no schema
change, no migration, no implementation PR**
**Date:** 2026-09-15
**Baseline:** `main` @ `4cc669b` (PR #39, Case D correction)

**Amendments since the freeze** — each decided at a gate, each additive, none
altering a rule that was already stated:

| # | Date | Change | Sections |
|---|---|---|---|
| U12a | 2026-09-15 | Tag names are trimmed before validation, comparison and persistence | new §7a; §6 `InvalidInput` row; §11 rows 19, 20 |
| U12b | 2026-09-15 | `RemoveTagFromNote` does not validate the tag's existence | §7 precondition ladder; §11 row 23 |

Both resolve silences found at the Slice 5 design gate, not contradictions:
nothing in the contract previously said whether a tag name was trimmed, and the
`RemoveTagFromNote` reading was already implied by §11 row 23's failure set but
was not stated outright.

**Authoritative sources:**

| Document | Status |
| -------- | ------ |
| [core-note-engine-design.md](core-note-engine-design.md) | ACCEPTED |
| [deletion-semantics.md](deletion-semantics.md) | ACCEPTED — supersedes the design in part |
| [ADR-004](../decisions/ADR-004-markdown-content.md) markdown content | Accepted |
| [ADR-010](../decisions/ADR-010-commands-and-events.md) commands and events | Accepted |
| [ADR-012](../decisions/ADR-012-entity-identifiers.md) identifiers | Accepted |
| [sidenotes-parity.md](../product/sidenotes-parity.md) | parity baseline |

---

## 0. Classification

```
  SPECIFIED   stated in an accepted document; citation given
  DERIVED     follows necessarily from a SPECIFIED rule; the rule is named
  APPROVED    decided at a decision gate (2026-09-14 / 2026-09-15); binding
```

**No UNKNOWN remains.** Every rule below is SPECIFIED, DERIVED or APPROVED, and
no unapproved proposal appears in any normative section.

---

## 1. Public surface — 23 commands, 7 queries

**The "21 operations" terminology is retired.** It appeared in no accepted
document; it was a miscount in an early revision of this contract. Design §6
prints three slash-pairs, giving 20 typographic tokens and 23 distinct names.

**A slash-pair is two independent public commands** (APPROVED, U7). Evidence:
parity B12's inventory row gives fold and unfold **separate keyboard chords**
(`⌥⌘←` / `⌥⌘→`), and principle 7 requires every primary action to have a
keyboard path. ADR-010 also lists `PinNote` and `UnpinNote` separately.

### The 23 commands

| # | Command | Source |
|---|---------|--------|
| 1 | `CreateNote` | design §6, ADR-010 |
| 2 | `UpdateNoteContent` | design §6, ADR-010 |
| 3 | `DeleteNote` | design §6, ADR-010 |
| 4 | `RestoreNote` | design §6, ADR-010 |
| 5 | `MoveNoteToFolder` | design §6 (ADR-010: `MoveNote`) |
| 6 | `ReorderNote` | design §6, ADR-010 |
| 7 | `PinNote` | design §6, ADR-010 |
| 8 | `UnpinNote` | design §6, ADR-010 |
| 9 | `SetNoteColor` | design §6, ADR-010 |
| 10 | `FoldNote` | design §6, ADR-010 |
| 11 | `UnfoldNote` | design §6 |
| 12 | `CreateFolder` | design §6, ADR-010 |
| 13 | `RenameFolder` | design §6, ADR-010 |
| 14 | `DeleteFolder` | design §6, ADR-010 |
| 15 | `RestoreFolder` | design §6 |
| 16 | `PinFolder` | design §6 |
| 17 | `UnpinFolder` | design §6 |
| 18 | `ReorderFolder` | design §6 |
| 19 | `CreateTag` | design §6 |
| 20 | `RenameTag` | design §6 |
| 21 | `DeleteTag` | design §6 |
| 22 | `AssignTagToNote` | design §6 |
| 23 | `RemoveTagFromNote` | design §6 |

### The 7 queries

| # | Query | Source |
|---|-------|--------|
| 1 | `GetNote` | design §6 |
| 2 | `ListNotesInFolder` | design §6 |
| 3 | `ListFolders` | design §6 |
| 4 | `ListTags` | design §6 |
| 5 | `ListNotesForTag` | design §6 |
| 6 | `ListDeletedNotes` | deletion-semantics I2 |
| 7 | `ListDeletedFolders` | deletion-semantics I2 |

**U6 resolved (APPROVED).** The generic `ListDeleted` was stale wording. The
design's own header declares *"Superseded in part by: deletion-semantics.md"*,
and I2 is the specific statement in the superseding document. Notes and folders
also come from different repositories (design §11), so one method would need a
union return. **No generic union query. No `bool includeDeleted`** (I2).

> ADR-010's command list additionally contains `ArchiveNote` and `MoveFolder`.
> Both are **stale**: design §5 rejects `IsArchived`, and folders are flat.
> ADR-010 predates the parity work, which is also why it omits `UnfoldNote`,
> `PinFolder` and `UnpinFolder` — B14 and C8 are both *"Added in 1.6"* and were
> found by the inventory afterwards.

---

## 2. Domain model (design §5)

```
  Note                                          Folder
  ├── Id          NoteId    ULID                ├── Id          FolderId  ULID
  ├── Content     string    markdown SOURCE     ├── Name        string    non-empty
  ├── Title       COMPUTED  §3                  ├── ColorKey    ColorKey?
  ├── FolderId    FolderId? null = root         ├── IsPinned    bool
  ├── ColorKey    ColorKey?                     ├── IsCollapsed bool
  ├── IsPinned    bool                          ├── SortOrder   double
  ├── IsFolded    bool                          ├── CreatedAt   DateTimeOffset
  ├── SortOrder   double                        ├── UpdatedAt   DateTimeOffset
  ├── CreatedAt   DateTimeOffset UTC            └── DeletedAt   DateTimeOffset?
  ├── UpdatedAt   DateTimeOffset UTC
  └── DeletedAt   DateTimeOffset?               Tag
                                                ├── Id        TagId  ULID
  NoteTag  (NoteId, TagId)                      ├── Name      case-insensitively unique
  join only, no identity                        ├── ColorKey  ColorKey?
                                                └── CreatedAt DateTimeOffset
```

`Title` computed, never stored (C1, B16) · strongly-typed ids (ADR-012) ·
folders flat, no `ParentId` · no `IsArchived` · **`Tag` has no `UpdatedAt`** ·
**`NoteTag` has no timestamps at all**.

---

## 3. Title algorithm (APPROVED, U2)

Deterministic, so it cannot become a source of test disagreement:

```
  1. Split Content into lines. A line terminates at LF, CRLF or CR.
  2. Find the first line that is non-empty after trimming whitespace.
     None -> title is the EMPTY STRING. Never null.
  3. If that line is a CommonMark ATX heading:
        - opening sequence of 1-6 '#' followed by a space/tab or end of line
        - up to 3 leading spaces allowed; 4+ is an indented code block
        -> remove the opening sequence and the optional closing '#' sequence
  4. Trim the result.
  5. No length cap in the domain. Display surfaces truncate.
```

Setext headings (`Title` / `=====`) are **not** recognised — the title is
line-based, so the underline is a separate line.

| Input | Title | Class |
|---|---|---|
| `""` / whitespace only | `""` | SPECIFIED — design §14 |
| `Plain text` | `Plain text` | SPECIFIED — B16 |
| `\n\n  Real title` | `Real title` | SPECIFIED — "first non-empty" |
| `# Title` / `### Title` | `Title` | SPECIFIED — C1 |
| `#Title` | `#Title` | DERIVED — CommonMark needs a space |
| `   # Title` (≤3 spaces) | `Title` | DERIVED — CommonMark §4.2 |
| `    # Title` (4 spaces) | `# Title` | DERIVED — indented code |
| `# Title #` | `Title` | DERIVED — closing sequence |
| `#` alone | `""` | SPECIFIED — §14 + CommonMark empty heading |
| `####### x` (7) | `####### x` | DERIVED — caps at 6 |
| `Title\n=====` | `Title` | APPROVED — Setext not recognised |
| 10,000-char line | full string | APPROVED — no domain cap |

CommonMark is normative through ADR-004 (*"CommonMark plus GitHub-flavoured
task lists"*).

> Title length is **not** externally observable through note addressing: H5
> specifies `noto://open/<uuid>` — by id. SideNotes includes the title in note
> URLs as a descriptive slug only. The observable surface is B12 (a folded note
> shows its title), which is presentation and may truncate.

---

## 4. `UpdatedAt` rule (APPROVED, U1)

> **Every mutating command stamps `UpdatedAt` on each entity row it changes.
> Within a transaction, all affected rows receive one timestamp captured once
> for that transaction. Entities without an `UpdatedAt` field are not stamped.**

This is **timestamp semantics**, distinct from ADR-010's **dispatch
architecture**. ADR-010 establishes one interception point; it does not by
itself enumerate which commands stamp. Both statements hold without conflict,
and ADR-010 needs no amendment.

| Command | Stamps | Entity |
|---|---|---|
| CreateNote | yes (`= CreatedAt`) | note |
| UpdateNoteContent | yes | note |
| MoveNoteToFolder | yes | note |
| ReorderNote | yes — **unless the no-op case (§5)** | note |
| PinNote / UnpinNote | yes | note |
| SetNoteColor | yes | note |
| FoldNote / UnfoldNote | yes | note |
| DeleteNote / RestoreNote | yes | note |
| AssignTagToNote / RemoveTagFromNote | **no** | `NoteTags` has no timestamps, and the **note row is not changed** |
| CreateFolder | yes (`= CreatedAt`) | folder |
| RenameFolder / PinFolder / UnpinFolder / ReorderFolder | yes | folder |
| DeleteFolder | yes | folder **and** every cascaded note — one timestamp |
| RestoreFolder | yes | folder **and** every restored note — one timestamp |
| CreateTag / RenameTag / DeleteTag | **no** | `Tag` has no `UpdatedAt` (design §5) |
| **All 7 queries** | **no** | queries are not commands (ADR-010) |

**The rule is self-consistent for every no-op in this contract.** "Each entity
row it changes" means a command that changes no row stamps nothing — this holds
for same-position reorder (§5) and for both tag no-ops (§7) without either
being an exception to the rule.

**Consumer:** parity **C10** — *"Sort by name / created / modified / manual"*
(SHOULD, provisional). **U10 resolved (APPROVED):** the engine follows the
uniform rule regardless of C10's status; it is an internal consistency rule
first.

---

## 5. Ordering (design §7)

| # | Rule |
|---|---|
| **O1** | Scoped per folder; root (`FolderId IS NULL`) is its own scope |
| **O2** | `ORDER BY IsPinned DESC, SortOrder ASC, Id ASC` — total, ULIDs are creation-ordered (ADR-012) |
| **O3** | Insert at an end uses min−1 / max+1. No renumbering |
| **O4** | Moving between folders assigns a new `SortOrder` in the target scope, one transaction |
| **O5** | Deleting leaves gaps. Gaps are harmless |
| **O6** | Renormalise when the gap between neighbours is exhausted; rewrite that folder's `SortOrder` to 1, 2, 3… in one transaction |

### Reorder signature (APPROVED, U3 + U9)

```
  ReorderNote(noteId, afterNoteId?)
  ReorderFolder(folderId, afterFolderId?)

    null          -> place first
    afterXId      -> place immediately after that active sibling
```

**Scope is inferred** from the note's current `FolderId`; the folder collection
is its own scope. **No explicit `FolderId` argument** (U9). `SortOrder` is
computed by the engine and **never supplied by the caller** — that keeps O6
inside the engine, where §7 and §9 put it.

| Case | Behaviour |
|---|---|
| Same position | **no-op, writes nothing, stamps nothing** (§4) |
| First / last | `afterId = null` → min−1; last sibling → max+1 |
| Target not an active sibling in scope | `InvalidInput` (§6) |
| Target is deleted | rejected — I5, I6 |
| Note is deleted | rejected — I5 |
| Note is active inside a **deleted folder** | **allowed** — Case C; the folder's state is not a precondition |
| Note-onto-note drag | **not a domain rule** — B10 is a drag-source constraint, unrepresentable in this signature |

### Renormalisation (U5 — RESOLVED, no specification change)

Observable contract only: ordering stays correct, the operation is atomic with
its trigger (§9), and renormalisation yields contiguous 1, 2, 3…

**The numeric threshold is implementation-defined** and is deliberately *not*
part of this contract. Design §7 gives the reason (~52 mantissa bits, ~50
same-spot operations) without a number; §14 requires a ~60-insert test, which
is the real guarantee.

---

## 6. Failure model (APPROVED, U4 + U8)

```
  BUSINESS / APPLICATION      -> RETURNED result
  INFRASTRUCTURE / STORAGE    -> StorageException  (exists, unchanged)
  PROGRAMMING / INVARIANT     -> ordinary argument exception; fail loudly
```

A single result type carrying a reason, mirroring `StorageException`'s own
documented shape (*"a single type with a `Reason` rather than a hierarchy …
a class per failure mode would be machinery without a second implementation"* —
principle 9). Callers distinguish business from infrastructure by **return
value versus exception**.

### Reasons — each traced to a specified requirement

| Reason | Required by | Operations |
|---|---|---|
| `NotFound` | I3/I5 presuppose identity; U8 | every command except the 4 creates; `GetNote` |
| `DuplicateName` | `Tag.Name` case-insensitively unique (design §5); `UX_Tags_Name` in SchemaV1 | `CreateTag`, `RenameTag` |
| `InvalidInput` | design §14 — *"empty folder name rejected; unknown colour key rejected"*; §7a — a tag name empty after trimming | `CreateFolder`, `RenameFolder`, `SetNoteColor`, `ReorderNote`, `ReorderFolder`, `CreateTag`, `RenameTag` |
| `InvalidState` | I5 — deleted entities are inert; restore/purge are the only legal transitions | every mutation on a deleted entity; `RestoreNote`/`RestoreFolder` on a non-deleted one |

**Exactly four reasons. No reason was invented, and none was added for U11** —
an existing or absent relationship is the desired state, not a failure.

**`DuplicateName` is reserved for entity-name uniqueness**, where uniqueness is
a business constraint on `Tags.Name`. It is **never** used for an existing
`(NoteId, TagId)` relationship (§7).

**Infrastructure stays as-is:** `StorageException` + `StorageFailure`
(`Unknown, CannotOpen, Corrupt, MigrationFailed, SchemaTooNew, WriteFailed`).
`WriteFailed` is declared and currently unused — it is the value the #13 write
path will use. **`StorageFailure` gains no domain values.**

**Error messages never contain note content** (principle 10, and
`StorageException`'s own doc comment).

### Query failure contract (APPROVED, U8)

| Query | Missing / empty |
|---|---|
| `GetNote` on an unknown id | business `NotFound` result |
| Any `List*` with no matches | **success, empty collection** |

**`null` is never an overloaded failure channel.**

---

## 7. Tag relationship semantics (APPROVED, U11)

Both relationship commands are **idempotent**.

```
  AssignTagToNote(noteId, tagId)
      relationship already exists  ->  SUCCESS
                                       no duplicate row
                                       no state change
                                       no persistence write
                                       no UpdatedAt change

  RemoveTagFromNote(noteId, tagId)
      relationship does not exist  ->  SUCCESS
                                       no state change
                                       no persistence write
                                       no UpdatedAt change
```

**Rationale (APPROVED):** relationship commands are state-transition
declarations — `Assign(A,B)` twice reaches the same final state as once. This
also keeps SQLite's `PRIMARY KEY (NoteId, TagId)` from surfacing as a
user-facing business error, and gives the engine idempotent semantics that stay
correct under command retry.

**`DuplicateName` is NOT used for a repeated assignment.** It applies to
creating or renaming an entity whose *name* must be unique; an existing
relationship is already the requested outcome.

### Precondition order — I5 is evaluated first

Idempotency does **not** weaken invariant I5 (*"a deleted note cannot be
edited, moved, reordered, pinned or tagged"*):

```
  1. note exists?          no  -> NotFound
  2. note ACTIVE?          no  -> InvalidState      <-- I5, checked BEFORE idempotency
  3. tag exists?           no  -> NotFound          (AssignTagToNote ONLY)
  4. relationship state    ->  present/absent as required  -> write
                           ->  already as requested        -> SUCCESS, no write
```

A deleted note plus an already-assigned tag is **`InvalidState`, not success.**

### `RemoveTagFromNote` does not validate the tag (APPROVED, U12b)

Step 3 applies to **`AssignTagToNote` only**. `RemoveTagFromNote` checks the
note and then attempts the removal:

```
  RemoveTagFromNote(noteId, tagId)
      note missing      ->  NotFound
      note deleted      ->  InvalidState            <-- I5, still first
      tag does not exist ->  SUCCESS, no write      <-- not NotFound
      relationship absent ->  SUCCESS, no write
```

**Rationale (APPROVED):** a tag that does not exist cannot be related to the
note, so the requested end state — *this note does not carry that tag* — already
holds. Returning `NotFound` would make the command report failure for a
condition it was asked to bring about, and would break the symmetry that makes
`Remove(A,B)` safe to retry.

This was already the contract's position rather than a change to it: §11 row 23
lists the failure set as `NotFound` (**note**) and `InvalidState`, naming no tag
failure, and step 3 above was annotated for `AssignTagToNote`. The rule is
stated explicitly here because a reader could otherwise take row 23's bare
`NotFound` to cover both arguments.

**Asymmetry with `AssignTagToNote` is deliberate.** Assign must reach an end
state that *requires* the tag to exist, so a missing tag is a genuine
`NotFound`. Remove must reach an end state that a missing tag already satisfies.

### What the sources do and do not say

| Statement | Source |
|---|---|
| `AssignTagToNote`, `RemoveTagFromNote` are MUST commands | design §6 |
| `Tag.Name` case-insensitively unique | design §5 |
| `DeleteTag` is hard; `NoteTags` cascades | design §8, §14 |
| Duplicate relationships are unstorable | `PRIMARY KEY (NoteId, TagId)`, SchemaV1 |
| **Whether a repeated command succeeds or fails** | **not stated anywhere** — decided at the gate |
| **Whether a tag name is trimmed** | **not stated anywhere** — decided at the gate (U12a below) |

> Tags appear in **no** parity row and in **no** feature-inventory row. They are
> a Noto addition, so — as with the recycle bin (deletion-semantics §2) — there
> is no external product to copy and the behaviour had to be chosen explicitly
> rather than derived.

---

## 7a. Tag name normalisation (APPROVED, U12a)

> **A tag name is trimmed of leading and trailing whitespace before validation,
> before uniqueness comparison, before persistence, and before the rename
> comparison. The trimmed value is what is stored.**

```
  " Work "    ->  "Work"
  "Work "     ->  "Work"
  " Work"     ->  "Work"

  "Work Item" ->  "Work Item"        internal whitespace is NEVER touched
```

| Input | Outcome |
|---|---|
| `""`, `"   "`, `"\t"`, any whitespace-only | empty after trimming → **`InvalidInput`** |
| `" Work "` when `"Work"` exists | collides → **`DuplicateName`** |
| `"work "` when `"Work"` exists | collides → **`DuplicateName`** (trim *and* case-insensitivity) |
| `"Work Item"` | stored verbatim as `"Work Item"` |

**Why this was a gap.** `UX_Tags_Name ... COLLATE NOCASE` (SchemaV1) makes
`'work'` and `'Work'` collide, but collation says nothing about whitespace: the
index treats `" Work"`, `"Work "` and `"Work"` as three distinct names. Without
this rule the engine would admit tags that are indistinguishable in any list the
user sees — the invisible-duplicate problem — while correctly rejecting the
visible duplicate `'work'`.

**Why trimming rather than rejecting untrimmed input.** Leading and trailing
whitespace in a name is almost always an artefact of how the text arrived, not
an intention. The engine already takes this position elsewhere: §3 finds the
first line *"non-empty after trimming"* and maps whitespace-only content to an
empty title, and `CreateFolder`/`RenameFolder` reject whitespace-only names as
empty. Tags now agree with both.

**Scope of the rule.** It governs `CreateTag` and `RenameTag` — the only
commands that accept a name. It is an **application-layer** rule: the database
enforces case-insensitive uniqueness (`UX_Tags_Name`), and normalisation happens
before the value reaches it. No schema change and no migration follow from this
decision.

**Not in scope.** Internal whitespace is not collapsed (`"Work  Item"` with two
spaces stays as typed), no Unicode normalisation form is applied, and no other
entity's name is affected — folder names keep their own rule, where duplicates
are legal (Case G).

---

## 8. Deletion semantics (design §8, deletion-semantics §4)

| Case | Scenario | Behaviour | Command |
|---|---|---|---|
| **A** | Folder with active notes deleted | folder + its **active** notes → DELETED, one transaction | `DeleteFolder` |
| **B** | Note already deleted, then folder deleted | already-deleted note **untouched**, keeps its original `DeletedAt` | `DeleteFolder` |
| **C** | Folder deleted, one note restored individually | **allowed**; note → ACTIVE with `FolderId` still pointing at the deleted folder | `RestoreNote` |
| **D** | Deleted folder restored | folder → ACTIVE **and every still-deleted note pointing at it** → ACTIVE | `RestoreFolder` |
| **E** | Note purged, then folder restored | — | **deferred** |
| **F** | Folder purged | — | **deferred** |
| **G** | Restored folder name collides | **allowed** — folder names are not unique | `RestoreFolder` |

```sql
-- RestoreFolder, design §8 — no causal column
UPDATE Folders SET DeletedAt = NULL WHERE Id = @F;
UPDATE Notes   SET DeletedAt = NULL WHERE FolderId = @F AND DeletedAt IS NOT NULL;
```

**No `DeletedWithFolderId` or equivalent.** `DeletedAt` says *whether*,
`FolderId` says *where*; nothing records *why*, and nothing needs to
(deletion-semantics §5).

`DeleteTag` is the **only hard delete** — *"a tag is a label, not content"*.

---

## 9. Query invariants (deletion-semantics §6)

| | Invariant | Obligation |
|---|---|---|
| **I1** | Active by default | every ordinary query filters `DeletedAt IS NULL`; a test asserts a deleted note is absent from **every** list method |
| **I2** | The bin is explicit | only `ListDeletedNotes` / `ListDeletedFolders`; **no `bool includeDeleted`** |
| **I3** | Restore is scoped | the folder + notes whose `FolderId` matches; never other folders or root |
| **I4** | Purge leaves nothing dangling | **deferred** — purge is not in #13 |
| **I5** | Deleted entities are inert | every mutation precondition requires an active entity → `InvalidState`, checked before idempotency (§7) |
| **I6** | Ordering ignores the deleted | deleted rows keep `SortOrder` but never participate in ordering queries |

Enforcement lives in the repository implementations, each with a test.

---

## 10. Transactions (design §9)

```
  DeleteFolder     folder + its active notes
  RestoreFolder    folder + its still-deleted notes
  MoveNoteToFolder folder change + new SortOrder
  ReorderNote      the write + any renormalisation
  DeleteTag        tag + NoteTags cascade
```

`SqliteTransaction` directly; no framework. Single-row updates need none.
Each atomic unit takes **one** `UpdatedAt` timestamp for all rows it touches (§4).

**Concurrency:** single user, single process. WAL + 5s busy timeout.
**Last-write-wins** — no version columns, no optimistic concurrency.

---

## 11. Requirement matrix

Every public operation. **Failure** lists business reasons only —
`StorageException` applies to all, and is omitted per row.

### Commands — notes

| # | Operation | Source | Obligation | Result | Failure | Atomic | Tests |
|---|---|---|---|---|---|---|---|
| 1 | `CreateNote` | §6, B1 | insert; ULID; `CreatedAt`=`UpdatedAt`; `SortOrder` per O3 | `NoteId` | `NotFound` (folder), `InvalidState` (folder deleted) | no | round-trip; placement at end |
| 2 | `UpdateNoteContent` | §6, B19 | replace `Content`; stamp | — | `NotFound`, `InvalidState` | no | content verbatim incl. newlines/unicode; title re-derives |
| 3 | `DeleteNote` | §6, B23 | `DeletedAt` = now; stamp | — | `NotFound`, `InvalidState` | no | excluded from all lists (I1); present in bin (I2) |
| 4 | `RestoreNote` | §6, B23 | `DeletedAt` = null; stamp | — | `NotFound`, `InvalidState` (not deleted) | no | Case C — restores into a deleted folder |
| 5 | `MoveNoteToFolder` | §6, B11 | `FolderId` + new `SortOrder` in target scope | — | `NotFound`, `InvalidState` | **yes** | scope change + order together; rollback on failure |
| 6 | `ReorderNote` | §6, B8/B10 | `SortOrder` per O3/O6; scope from `FolderId` | — | `NotFound`, `InvalidInput`, `InvalidState` | **yes** | one row written; ~60 same-spot inserts; same-position no-op writes nothing |
| 7 | `PinNote` | §6, B14 | `IsPinned` = true; stamp | — | `NotFound`, `InvalidState` | no | pinned sort above unpinned (O2) |
| 8 | `UnpinNote` | §6, B14 | `IsPinned` = false; stamp | — | `NotFound`, `InvalidState` | no | returns to `SortOrder` position |
| 9 | `SetNoteColor` | §6, B15 | `ColorKey` (null clears); stamp | — | `NotFound`, `InvalidInput` (unknown key), `InvalidState` | no | **unknown colour key rejected** (§14) |
| 10 | `FoldNote` | §6, B12 | `IsFolded` = true; stamp | — | `NotFound`, `InvalidState` | no | survives reopen |
| 11 | `UnfoldNote` | §6, B12 | `IsFolded` = false; stamp | — | `NotFound`, `InvalidState` | no | independently invocable |

### Commands — folders

| # | Operation | Source | Obligation | Result | Failure | Atomic | Tests |
|---|---|---|---|---|---|---|---|
| 12 | `CreateFolder` | §6, C2 | insert; ULID; timestamps | `FolderId` | `InvalidInput` (empty name) | no | **empty folder name rejected** (§14) |
| 13 | `RenameFolder` | §6, C13 | replace `Name`; stamp | — | `NotFound`, `InvalidInput`, `InvalidState` | no | duplicate names allowed (Case G) |
| 14 | `DeleteFolder` | §6, C11 | folder + its **active** notes → deleted; one timestamp | — | `NotFound`, `InvalidState` | **yes** | Case A; **Case B** — already-deleted untouched; rollback |
| 15 | `RestoreFolder` | §6, **Case D** | folder + **every still-deleted note with matching `FolderId`** → active | — | `NotFound`, `InvalidState` (not deleted) | **yes** | **Case D incl. a note deleted individually beforehand**; Case G |
| 16 | `PinFolder` | §6, C8 | `IsPinned` = true; stamp | — | `NotFound`, `InvalidState` | no | pinned folders sort first |
| 17 | `UnpinFolder` | §6, C8 | `IsPinned` = false; stamp | — | `NotFound`, `InvalidState` | no | — |
| 18 | `ReorderFolder` | §6, C9 | `SortOrder` per O3/O6; folder collection is the scope | — | `NotFound`, `InvalidInput`, `InvalidState` | **yes** | same-position no-op; renormalisation |

### Commands — tags

| # | Operation | Source | Obligation | Result | Failure | Atomic | Tests |
|---|---|---|---|---|---|---|---|
| 19 | `CreateTag` | §6, §5, **§7a** | **trim name (§7a)**; insert; ULID; `CreatedAt`. **No `UpdatedAt`** | `TagId` | `InvalidInput`, `DuplicateName` | no | `'work'` vs `'Work'` rejected (`UX_Tags_Name`); `' Work '` vs `'Work'` rejected; whitespace-only → `InvalidInput` |
| 20 | `RenameTag` | §6, §5, **§7a** | **trim name (§7a)**; replace `Name`. **Stamps nothing** | — | `NotFound`, `InvalidInput`, `DuplicateName` | no | CI uniqueness on rename; trimmed collision rejected; internal whitespace preserved |
| 21 | `DeleteTag` | §6, §8 | **HARD delete**; `NoteTags` cascades | — | `NotFound` | **yes** | **removes `NoteTags`, leaves notes intact** (§14) |
| 22 | `AssignTagToNote` | §6, **U11a** | insert relationship; **existing → success, no write, no stamp** | — | `NotFound` (note/tag), `InvalidState` (note deleted) | no | idempotent: twice = one row; deleted note still `InvalidState` |
| 23 | `RemoveTagFromNote` | §6, **U11b**, **U12b** | delete relationship; **absent → success, no write, no stamp**; **tag not validated (§7)** | — | `NotFound` (**note only**), `InvalidState` | no | idempotent: twice succeeds; note unaffected; **missing tag → success**, not `NotFound` |

### Queries

| # | Query | Source | Obligation | Result | Failure | Tests |
|---|---|---|---|---|---|---|
| Q1 | `GetNote` | §6, U8 | by id; I1 | the note | `NotFound` | never returns null as a failure channel |
| Q2 | `ListNotesInFolder` | §6 | scope + O1/O2; I1, I6 | ordered list; **empty if none** | — | root is its own scope; deleted excluded |
| Q3 | `ListFolders` | §6 | O2; I1 | ordered list | — | pinned first |
| Q4 | `ListTags` | §6 | — | list | — | — |
| Q5 | `ListNotesForTag` | §6 | join via `NoteTags`; I1 | list | — | deleted notes excluded |
| Q6 | `ListDeletedNotes` | I2 | **only** deleted | list | — | the I2 explicit-bin method |
| Q7 | `ListDeletedFolders` | I2 | **only** deleted | list | — | notes and folders separate |

**No PENDING rows.** Every operation above is fully specified by this contract.

---

## 12. Persistence

**SchemaV2 is sufficient. No migration 003.** Every §2 field maps to an
existing column:

```
  Notes     Id FolderId Content ColorKey IsPinned IsFolded
            SortOrder CreatedAt UpdatedAt DeletedAt          all present
  Folders   Id Name ColorKey SortOrder IsCollapsed
            CreatedAt UpdatedAt                              SchemaV1
            IsPinned DeletedAt                               added by SchemaV2
  Tags      Id Name ColorKey CreatedAt        + UX_Tags_Name (NOCASE)
  NoteTags  NoteId TagId  PRIMARY KEY (NoteId, TagId), both FKs ON DELETE CASCADE
```

Repository interfaces in `Noto.Core`, implementations in `Noto.Infrastructure`
(design §11). Three repositories — `INoteRepository`, `IFolderRepository`,
`ITagRepository` — **not** a generic `IRepository<T>`. No separate persistence
models. `Title` is not persisted.

---

## 13. Resolution status

| ID | Gap | Status |
|---|---|---|
| **U1** | `UpdatedAt` semantics | **RESOLVED** — §4 |
| **U2** | Title algorithm | **RESOLVED** — §3 |
| **U3** | `ReorderNote` signature | **RESOLVED** — §5 |
| **U4** | Domain error model | **RESOLVED** — §6 |
| **U5** | Renormalisation threshold | **RESOLVED** — implementation-defined |
| **U6** | Deleted-list query surface | **RESOLVED** — §1 |
| **U7** | Command count | **RESOLVED** — 23 + 7, §1 |
| **U8** | Query failure model | **RESOLVED** — §6 |
| **U9** | Reorder scope | **RESOLVED** — §5 |
| **U10** | C10 dependency | **RESOLVED** — §4 |
| **U11** | Tag relationship idempotency | **RESOLVED** — §7 |
| **U12a** | Tag name whitespace normalisation | **RESOLVED** — §7a |
| **U12b** | Whether `RemoveTagFromNote` validates the tag | **RESOLVED** — §7 |

**No unresolved gap remains.**

U12a and U12b were found at the Slice 5 design gate, after the freeze. Both were
silences rather than contradictions — see the amendment table in the header.

---

## 14. Deferred — not in #13

```
  EmptyRecycleBin / purge        needs a retention policy  (I4, Cases E and F)
  Retention limits               product decision
  Bin UI, restore affordance     M2 workspace
  DuplicateNote (B22)            SHOULD, not MUST
  Search / FTS5                  #17
  Export                         #15
  Markdown rendering             #14
  Attachments, presentations,
  context bindings               later milestones
```

---

## 15. Implementation plan

Five vertical slices; each ends with passing tests. Domain rules in
`Noto.Core.Tests` (no database); everything touching persistence in
`Noto.Infrastructure.Tests` against **real SQLite**.

```
  Slice 1   domain types, title algorithm (§3), id structs,
            repository interfaces, failure model (§6),
            CreateNote, GetNote

  Slice 2   UpdateNoteContent, MoveNoteToFolder, ReorderNote,
            SortOrder + renormalisation, pin/fold/colour
            (~60-insert precision test)

  Slice 3   DeleteNote, RestoreNote, ListDeletedNotes/Folders
            (I1, I2, I5, I6)

  Slice 4   folder commands incl. DeleteFolder / RestoreFolder
            (Cases A, B, C, D, G + rollback)
            HIGHEST RISK: Case D + two atomic operations

  Slice 5   tags: create, rename, delete, assign, remove
            (idempotency §7, cascade, CI uniqueness)
```

The architecture guard must still pass with domain types present: `Noto.Core`
gains no platform or storage reference.

---

## 16. Gate compliance

| Rule | Status |
|---|---|
| No production code | ✅ |
| No schema change | ✅ — SchemaV2 sufficient; no migration 003 |
| No tests added or changed | ✅ |
| No implementation PR | ✅ |
| Accepted documents modified | **1 line total** — design §6 `ListDeleted` → `ListDeletedNotes`, `ListDeletedFolders` (U6). CRLF preserved. U11 required no design change |
| Unapproved proposals in normative sections | ✅ none |
