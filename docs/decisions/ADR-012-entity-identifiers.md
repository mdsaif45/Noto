# ADR-012 — Entity identifiers: ULIDs stored as TEXT

**Status:** Accepted
**Date:** 2026-09-14
**Related:** ADR-002 (local-first), ADR-003 (SQLite)

---

## Context

`data-model.md` sketched every table with `INTEGER PRIMARY KEY` without
examining the choice. That is the SQLite default and it is the right default for
many applications — but Noto has four requirements that make it worth checking
before a schema ships, because changing primary keys afterwards means rewriting
every table and every foreign key.

The requirements that bear on it:

| Requirement | Source |
| ----------- | ------ |
| Export must be lossless and round-trippable | ADR-002 — the data outlives the app |
| Notes get stable URLs (`noto://note/<id>`) | SideNotes parity, row H2 |
| Sync is possible later, bring-your-own-storage | ADR-002 |
| The user can inspect the database directly | ADR-002 |

---

## Problem

What identifies a note, a folder, a tag or an attachment?

---

## Options considered

### Option A — `INTEGER PRIMARY KEY` (rowid alias)

The SQLite default. Smallest, fastest, and the natural choice for a
single-machine store.

Three problems, all downstream:

- **Identifiers collide across machines.** Two machines both create note `7`.
  Any future merge — sync, or importing a colleague's export — has to rewrite
  every id and every foreign key referencing it.
- **A URL is not stable across a restore.** `noto://note/7` means a different
  note after an import, which is worse than it failing.
- **`rowid` can be renumbered by `VACUUM`** when it is not declared
  `INTEGER PRIMARY KEY` — a subtle footgun near a feature Noto will want.

### Option B — GUID / UUIDv4 as TEXT

Globally unique, no coordination. Merges and URLs both work.

But UUIDv4 is **random**, so inserts scatter across the index rather than
appending. On a local store of a few thousand notes this is not a real
performance problem — the honest objection is different: a v4 GUID carries no
ordering, so "the notes in creation order" needs a separate timestamp column and
an index, and two notes created in the same millisecond have no defined order.

### Option C — ULID as TEXT

26 characters, Crockford base32, **lexicographically sortable by creation
time**: a 48-bit timestamp followed by 80 bits of randomness.

Globally unique like a GUID, but monotonic, so it indexes well and sorts
meaningfully. Readable in a database browser.

### Option D — Integer surrogate key plus a public GUID

Both. Integer for joins, GUID for export and URLs.

Correct in a system with heavy relational joins — and disproportionate here.
Two identifiers per row means two indexes, two things to keep in sync, and a
standing question of which one a given piece of code should use.

---

## Decision

**ULIDs, stored as `TEXT` in a `CHAR(26)`-shaped column, as the primary key of
every user-facing entity.**

```sql
Id  TEXT PRIMARY KEY NOT NULL   -- ULID, 26 chars, Crockford base32
```

Applies to: notes, folders, tags, attachments, and any future user-facing
entity.

**Not** applied to:

- pure join tables (`NoteTags`), which use a composite primary key of the two
  foreign keys — they have no independent identity
- FTS5 external-content tables, which require an integer `content_rowid` and
  therefore keep SQLite's own `rowid`

### Consequence for FTS5

This is the one real complication, and it is worth stating plainly rather than
discovering at M3.

FTS5 external-content tables index by `rowid`, an integer. A `TEXT` primary key
is not a `rowid` alias, so notes keep an implicit `rowid` alongside their ULID,
and the FTS table joins on that.

```
  Notes:  rowid (implicit, integer)  +  Id (ULID, the real identity)
                  |
                  +-- NotesFts.content_rowid
```

The triggers therefore reference `new.rowid`, not `new.Id`. **`VACUUM` can
renumber an implicit `rowid`**, which would silently desynchronise the search
index — so the rebuild-after-vacuum step is mandatory and belongs in the same
issue as FTS5 (#17).

An `INTEGER PRIMARY KEY` would have avoided this. The trade is accepted: search
index maintenance is a solvable, local, testable problem, whereas identifier
collisions on import are a data-corruption problem with no clean fix.

---

## Rationale

**Export and import are a contract, not a feature.** ADR-002 promises the user's
notes outlive the application. An export whose identifiers cannot be re-imported
without rewriting every reference does not honour that promise, and the SideNotes
research found *no bulk export at all* in the competitor — so this is a
differentiator Noto should not weaken at the schema level.

**Stable URLs need stable identifiers.** `noto://note/<id>` is a parity
requirement. An identifier that changes on restore makes every saved link wrong,
which is worse than not having links.

**Sync stays possible without a rewrite.** ADR-002 defers sync, and this ADR is
what keeps that deferral cheap. Merging two machines' notes with integer keys
means renumbering; with ULIDs it means a union.

**ULID over GUID because ordering is free.** Creation-ordered identifiers make
`ORDER BY Id` meaningful, keep index inserts appending rather than scattering,
and give a deterministic tiebreak for two notes created in the same instant.

**Why not the integer+GUID pair.** Noto's data model is small and its joins are
shallow. Carrying two identities per row to save a few bytes per index entry is
machinery without a demonstrated problem (principle 9).

### The honest cost

- 26 bytes per key instead of up to 8
- foreign keys are `TEXT`, so indexes are larger
- the FTS5 `rowid` complication above

At Noto's scale — thousands of notes, not millions — the size cost is
irrelevant. It is recorded so that nobody believes this choice was free.

---

## Implementation

ULID generation lives in `Noto.Core` as a small self-contained helper. It is
domain vocabulary, not infrastructure, and it must be usable when constructing
an entity rather than only when persisting one.

No dependency is taken for this. A ULID is a timestamp, 80 random bits, and a
base32 encoding — roughly forty lines, fully testable, versus a package to audit
and update forever (principle 9).

Requirements:

- monotonic within the same millisecond, so ordering is total
- generated from a cryptographically strong source for the random component
- canonical 26-character Crockford base32, matching the published spec, so an
  external tool can read them

---

## Consequences

### Positive

- export and import round-trip without rewriting references
- stable, permanent note URLs
- sync remains additive rather than a migration
- creation-ordered by construction
- readable in any database browser
- no coordination needed to mint an id, including offline

### Negative

- larger keys and indexes
- **FTS5 must join on `rowid`, and a `VACUUM` requires an index rebuild**
- one more piece of code to own (the generator), though a small and tested one
- `ORDER BY Id` sorts by *creation*, which is not the same as user ordering —
  user ordering stays `SortOrder`, and conflating the two would be a bug

### Testing requirements

- [ ] Generated ids are 26 characters, valid Crockford base32
- [ ] Ids are monotonically increasing, including within one millisecond
- [ ] Ids sort lexicographically in creation order
- [ ] A large batch generated in a tight loop produces no duplicates
- [ ] The timestamp component decodes to the creation time

---

## Future reconsideration criteria

Revisit if:

- the FTS5 `rowid` indirection proves genuinely unmanageable in practice — the
  fallback is an integer primary key plus a public ULID (Option D), which is a
  migration but a mechanical one
- a measured performance problem traces to key size, at a note count real users
  actually reach

Do **not** revisit to "simplify" to integer keys. That simplification is exactly
what breaks export, URLs and any future sync, and it would be discovered only
once users have data to lose.
