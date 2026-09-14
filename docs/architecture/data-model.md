# Data Model

**Status:** Draft
**Last updated:** 2026-09-14
**Decisions:** [ADR-002](../decisions/ADR-002-local-first-storage.md),
[ADR-003](../decisions/ADR-003-sqlite-data-access.md),
[ADR-004](../decisions/ADR-004-markdown-content.md),
[ADR-006](../decisions/ADR-006-window-binding-identity.md)

---

## Principle

ADR-002 makes the on-disk format a **public contract**. A user's notes must
outlive the application. That imposes three rules:

1. content is markdown source text, never a rendered or proprietary form
2. the schema is documented, and changing it is a breaking change requiring a
   migration
3. **only the minimal model actually required.** The initial project direction
   listed eleven candidate entities; this model has six entity tables plus one
   join table, one FTS5 virtual table, and one settings table.

Entities are added when a feature needs them, not in anticipation.

---

## Entities

```
  Folder ──┐
           │ 0..1
           v
         Note ──< NoteTag >── Tag
           │
           ├──< Attachment
           │
           ├──< Task              (checklist items)
           │
           └──< NotePresentation  (floating now, contextual at M6)

  deferred to M6:
         Note ──< ContextBinding
```

A note owns its content and organization. It does **not** own how it is shown —
`NotePresentation` references it (ADR-009).

Deliberately absent from the MVP model:

| Candidate | Why not |
| --------- | ------- |
| `Workspace` | One implicit workspace. A second organizing level with no demonstrated need (principle 9). |
| `ApplicationProfile` | Collapsed into `ContextBinding`. A separate profile table adds a join and buys nothing until per-application *settings* exist. |
| `Shortcut` | Keybindings are settings, not relational data. |
| `Reminder` | Not in the MVP, and reminders drift toward task management (principle 1). |

---

## Schema

### Notes

```sql
CREATE TABLE Notes (
    Id            INTEGER PRIMARY KEY,
    FolderId      INTEGER     NULL REFERENCES Folders(Id) ON DELETE SET NULL,
    Title         TEXT        NOT NULL DEFAULT '',
    Content       TEXT        NOT NULL DEFAULT '',   -- markdown source
    Color         TEXT        NULL,                  -- palette key, not a hex value
    IsPinned      INTEGER     NOT NULL DEFAULT 0,
    IsArchived    INTEGER     NOT NULL DEFAULT 0,
    IsLocked      INTEGER     NOT NULL DEFAULT 0,
    SortOrder     REAL        NOT NULL DEFAULT 0,
    CreatedAt     TEXT        NOT NULL,              -- ISO-8601 UTC
    UpdatedAt     TEXT        NOT NULL,
    DeletedAt     TEXT        NULL                   -- soft delete; recycle bin
);

CREATE INDEX IX_Notes_Folder    ON Notes(FolderId)   WHERE DeletedAt IS NULL;
CREATE INDEX IX_Notes_Updated   ON Notes(UpdatedAt)  WHERE DeletedAt IS NULL;
CREATE INDEX IX_Notes_Pinned    ON Notes(IsPinned)   WHERE DeletedAt IS NULL AND IsPinned = 1;
```

Notes worth recording:

- **`Color` stores a palette key, not a hex value.** Hex values do not survive a
  theme change and cannot adapt to dark mode.
- **`SortOrder` is `REAL`, not `INTEGER`.** Reordering by drag then only needs
  to rewrite one row — the new value is the midpoint of its neighbours —
  instead of renumbering the list. Renormalise when the gap gets too small.
- **`DeletedAt` is a soft delete.** Every competitor has a recycle bin, and for
  a product whose data cannot be recovered from a server, hard deletion on a
  keystroke is unacceptable.
- Timestamps are **ISO-8601 UTC text**. SQLite has no date type; text sorts
  correctly and stays readable when a user inspects the database directly,
  which ADR-002 invites them to do.

### Folders

```sql
CREATE TABLE Folders (
    Id           INTEGER PRIMARY KEY,
    ParentId     INTEGER NULL REFERENCES Folders(Id) ON DELETE CASCADE,
    Name         TEXT    NOT NULL,
    Color        TEXT    NULL,
    SortOrder    REAL    NOT NULL DEFAULT 0,
    IsCollapsed  INTEGER NOT NULL DEFAULT 0,
    CreatedAt    TEXT    NOT NULL
);
```

`ParentId` supports nesting in the schema, but **the UI presents a flat,
single-level folder list**, because that is what SideNotes does.

The feature inventory found no sub-folder capability documented anywhere across
45 tips, 18 articles and 40+ release notes — INFERRED flat, from documentation
absence. Noto matches it for parity. **Deeper nesting is not a parity
requirement and must not be added speculatively** (principle 9).

The column is kept because it costs nothing and avoids a migration if nesting
is ever justified by evidence. The constraint is enforced in the application,
not the schema.

### Tags

```sql
CREATE TABLE Tags (
    Id        INTEGER PRIMARY KEY,
    Name      TEXT NOT NULL COLLATE NOCASE UNIQUE,
    Color     TEXT NULL,
    CreatedAt TEXT NOT NULL
);

CREATE TABLE NoteTags (
    NoteId INTEGER NOT NULL REFERENCES Notes(Id) ON DELETE CASCADE,
    TagId  INTEGER NOT NULL REFERENCES Tags(Id)  ON DELETE CASCADE,
    PRIMARY KEY (NoteId, TagId)
);

CREATE INDEX IX_NoteTags_Tag ON NoteTags(TagId);
```

`COLLATE NOCASE UNIQUE` prevents `work` and `Work` becoming separate tags,
which is a papercut every tagging system eventually has to fix.

Noto has **both folders and tags**. Research found SideNotes has folders only
and Noticky tags only; neither has both, and the combination is nearly free.

### Attachments

```sql
CREATE TABLE Attachments (
    Id           INTEGER PRIMARY KEY,
    NoteId       INTEGER NOT NULL REFERENCES Notes(Id) ON DELETE CASCADE,
    FileName     TEXT    NOT NULL,   -- display name
    StoredName   TEXT    NOT NULL,   -- GUID on disk; never user-controlled
    MimeType     TEXT    NULL,
    SizeBytes    INTEGER NOT NULL,
    CreatedAt    TEXT    NOT NULL
);

CREATE INDEX IX_Attachments_Note ON Attachments(NoteId);
```

**Files live on disk, not in the database.** A blob column would bloat the
database, slow backup, and make the store harder to inspect.

`StoredName` is a generated GUID, and the separation from `FileName` is a
security boundary: a filename arriving from a drag-and-drop is untrusted input
and must never reach the file system (principle 10). Deleting a note cascades
the rows; orphaned files are removed by a sweep, not synchronously.

### Context bindings — **M6, not in the initial schema**

> This table is **not created by migration 001**. Contextual notes are M6,
> after SideNotes parity and hardening, and it arrives in the migration that
> introduces them.
>
> It is documented here because the shape matters to ADR-009: context binding
> is a *presentation concern that needs queryable fields*, which is why it gets
> real columns rather than living in `NotePresentations.State` JSON. Recording
> that now costs nothing and prevents the wrong choice later.

Design per ADR-005 and ADR-006, both held at **Proposed**:

```sql
CREATE TABLE ContextBindings (
    Id              INTEGER PRIMARY KEY,
    NoteId          INTEGER NOT NULL REFERENCES Notes(Id) ON DELETE CASCADE,

    Kind            TEXT    NOT NULL,   -- 'application' | 'window'
                                        -- 'document' | 'url' reserved

    -- fingerprint (ADR-006), ranked by trustworthiness
    AppIdentity     TEXT    NOT NULL,   -- AUMID if packaged, else exe path
    WindowClass     TEXT    NULL,       -- strong signal
    TitleSignature  TEXT    NULL,       -- weak; normalised; disambiguation only

    -- presentation
    Behavior        TEXT    NOT NULL DEFAULT 'show-hide',
    Position        TEXT    NULL,       -- JSON; relative placement

    LastMatchedAt   TEXT    NULL,       -- drives the detached state
    CreatedAt       TEXT    NOT NULL
);

CREATE INDEX IX_Bindings_App  ON ContextBindings(AppIdentity);
CREATE INDEX IX_Bindings_Note ON ContextBindings(NoteId);
```

The three fingerprint columns exist as **separate columns, not a serialised
blob**, because matching queries filter on `AppIdentity` and that must be
indexed. Their ranking is ADR-006's, and it is the whole design:

```
  AppIdentity     ####################  required, strong
  WindowClass     ##############        strong
  TitleSignature  #####                 weak, never used alone
```

`LastMatchedAt` is what lets the UI distinguish *attached* from *detached*
without probing the OS.

`Kind` is `application` only in the MVP. `window` arrives in v0.6; `document`
and `url` are reserved and unimplemented — the column accepts them so that
adding the finer ladder rungs does not require a schema migration.

### Note presentations

**A note is not a window** (ADR-009). Presentation is a separate concern that
references a note, never a property of it.

```sql
CREATE TABLE NotePresentations (
    Id         INTEGER PRIMARY KEY,
    NoteId     INTEGER NOT NULL REFERENCES Notes(Id) ON DELETE CASCADE,
    Kind       TEXT    NOT NULL,   -- 'floating' | 'contextual' (M6) | ...
    State      TEXT    NOT NULL,   -- JSON; shape defined per kind
    CreatedAt  TEXT    NOT NULL,
    UpdatedAt  TEXT    NOT NULL
);

CREATE INDEX IX_Presentations_Note ON NotePresentations(NoteId);
CREATE INDEX IX_Presentations_Kind ON NotePresentations(Kind);
```

This replaces the earlier `FloatingWindows` table, which was keyed on `NoteId`
and therefore encoded "a note has at most one floating state" — the model
ADR-009 exists to reject.

Three deliberate choices:

- **No `UNIQUE(NoteId, Kind)`.** The same note floating on two monitors is a
  legitimate thing to want.
- **`State` is JSON.** Each kind has a different shape, presentation state is
  read by exactly one component, and it is never queried across kinds. Contrast
  `ContextBindings`, where `AppIdentity` *is* queried and so *is* a column.
- **The sidebar is not stored here.** Every note is in the sidebar; that is not
  a presentation record. Its position is `Notes.SortOrder`, which is
  organization, not presentation.

For `Kind = 'floating'`, `State` carries:

```json
{
  "x": 120, "y": 340, "width": 300, "height": 400,
  "monitorId": "\\.\DISPLAY1",
  "opacity": 1.0,
  "alwaysOnTop": true,
  "locked": false,
  "clickThrough": false
}
```

`monitorId` is a device path so a note returns to the screen it was on. When
that monitor is absent the note is repositioned onto the primary display —
**never restored off-screen**, which is the classic failure of this feature and
is a required test case.

### Full-text search

External-content FTS5, per ADR-003:

```sql
CREATE VIRTUAL TABLE NotesFts USING fts5(
    Title, Content,
    content='Notes',
    content_rowid='Id',
    tokenize='unicode61 remove_diacritics 2'
);
```

Synchronised by triggers so the index cannot drift from the content:

```sql
CREATE TRIGGER Notes_ai AFTER INSERT ON Notes BEGIN
  INSERT INTO NotesFts(rowid, Title, Content)
  VALUES (new.Id, new.Title, new.Content);
END;

CREATE TRIGGER Notes_ad AFTER DELETE ON Notes BEGIN
  INSERT INTO NotesFts(NotesFts, rowid, Title, Content)
  VALUES ('delete', old.Id, old.Title, old.Content);
END;

CREATE TRIGGER Notes_au AFTER UPDATE ON Notes BEGIN
  INSERT INTO NotesFts(NotesFts, rowid, Title, Content)
  VALUES ('delete', old.Id, old.Title, old.Content);
  INSERT INTO NotesFts(rowid, Title, Content)
  VALUES (new.Id, new.Title, new.Content);
END;
```

> **`MATCH` takes a query language, not a literal string.** Unsanitised user
> input crashes on an apostrophe. All search input is escaped before it reaches
> `MATCH`, and this has a dedicated test (ADR-003).

Because content is stored as markdown source (ADR-004), FTS5 indexes it
directly with no extraction step.

### Settings and schema version

```sql
CREATE TABLE Settings (
    Key   TEXT PRIMARY KEY,
    Value TEXT NOT NULL
);
```

Schema version uses `PRAGMA user_version`, not a table — it is the SQLite-native
mechanism and is available before any table is read.

---

## Storage layout

```
  %LOCALAPPDATA%\Noto\
  ├── noto.db                 SQLite: notes, folders, tags, bindings
  ├── noto.db-wal             write-ahead log
  ├── noto.db-shm             shared memory
  ├── attachments\
  │   └── <guid>.<ext>        GUID-named; original name is in the DB
  ├── backups\
  │   └── noto-<timestamp>.db
  └── logs\
      └── noto-<date>.log     never contains note content
```

`%LOCALAPPDATA%` rather than `%APPDATA%`: roaming profiles must not attempt to
replicate a live SQLite database.

> **WAL and file sync do not mix.** If a user places this folder inside a
> OneDrive or Dropbox tree, the sync client can corrupt the database by copying
> `-wal` and `-shm` out from under SQLite. This is a known hazard, it is the
> reason ADR-002 requires testing before any sync feature ships, and it is why
> **backups are plain `.db` snapshots** rather than a live folder copy.

---

## Migrations

Hand-written and versioned (ADR-003):

```
  open database
  read PRAGMA user_version              -> n
  for each migration m where m > n:
      BEGIN TRANSACTION
        apply m
        PRAGMA user_version = m
      COMMIT
```

Rules, binding:

1. **Migrations are append-only.** A shipped migration is never edited.
2. **Every migration is tested as an upgrade**, not only against an empty
   database. A migration that works on a fresh install and corrupts an existing
   one is the worst bug this product can have.
3. **Back up before migrating.** Snapshot to `backups\` first; restore on
   failure.
4. **Never leave a half-migrated database.** Roll back and keep the old one.
5. SQLite cannot drop or alter a column in place — such changes require an
   explicit table rebuild, written out and reviewed.

---

## Export

ADR-002 requires that Noto never be the only thing that can read a user's
notes.

```
  export/
  ├── <folder>/
  │   └── <note-title>.md      YAML front matter + markdown body
  └── attachments/
```

Lossless by construction: content is already markdown, and the metadata that
does not fit the body goes in front matter.

```yaml
---
title: API notes
tags: [work, api]
created: 2026-09-14T10:30:00Z
updated: 2026-09-14T14:22:00Z
pinned: true
bound-to: Code.exe
---
```

---

## Open questions

| Question | Needed by |
| -------- | --------- |
| Attachment size limit, and behavior when exceeded | M1 |
| Whether `Position` JSON should become typed columns once window binding lands | v0.6 |
| Backup retention policy | M1 |
| Whether `Color` palette keys need to be user-extensible | M1 |
