namespace Noto.Infrastructure.Storage.Migrations;

/// <summary>
/// Migration 001 — the initial schema.
/// </summary>
/// <remarks>
/// <para>
/// <b>Shipped. Never edit this.</b> Changing an applied migration means
/// existing databases and new ones diverge silently. Corrections are a new
/// migration.
/// </para>
/// <para>
/// Scope is deliberately the minimum M1 needs: folders, notes, tags. Not
/// created here, each with the issue that introduces it:
/// </para>
/// <list type="bullet">
///   <item><c>Attachments</c> — with attachment handling</item>
///   <item><c>Tasks</c> — checklists are markdown content, not rows (ADR-004);
///         a table only appears if querying across notes is needed</item>
///   <item><c>NotesFts</c> — with search (#17)</item>
///   <item><c>NotePresentations</c> — with floating notes (ADR-009)</item>
///   <item><c>ContextBindings</c> — M6</item>
/// </list>
/// </remarks>
internal static class SchemaV1
{
    internal const string Sql = """
        -- ---------------------------------------------------------------
        -- Folders
        --
        -- FLAT, one level. SideNotes has no documented sub-folders, and the
        -- parity spec matches it (row C3). There is deliberately no ParentId:
        -- adding one later is a migration, whereas shipping an unused
        -- hierarchy column invites code that half-supports it (principle 9).
        -- ---------------------------------------------------------------
        CREATE TABLE Folders (
            Id          TEXT    PRIMARY KEY NOT NULL,   -- ULID (ADR-012)
            Name        TEXT    NOT NULL,
            ColorKey    TEXT    NULL,                   -- palette key, never a hex value
            SortOrder   REAL    NOT NULL DEFAULT 0,
            IsCollapsed INTEGER NOT NULL DEFAULT 0 CHECK (IsCollapsed IN (0, 1)),
            CreatedAt   TEXT    NOT NULL,               -- ISO-8601 UTC
            UpdatedAt   TEXT    NOT NULL
        ) STRICT;

        CREATE INDEX IX_Folders_SortOrder ON Folders (SortOrder);

        -- ---------------------------------------------------------------
        -- Notes
        --
        -- Content is markdown SOURCE (ADR-004) — never rendered HTML. That is
        -- what makes export lossless and lets FTS5 index the text directly.
        --
        -- There is no column describing how a note is shown. A note is not a
        -- floating note or a sidebar note (ADR-009); presentation references
        -- the note, never the reverse.
        -- ---------------------------------------------------------------
        CREATE TABLE Notes (
            Id         TEXT    PRIMARY KEY NOT NULL,    -- ULID (ADR-012)
            FolderId   TEXT    NULL REFERENCES Folders (Id) ON DELETE SET NULL,
            Title      TEXT    NOT NULL DEFAULT '',
            Content    TEXT    NOT NULL DEFAULT '',     -- markdown source
            ColorKey   TEXT    NULL,
            IsPinned   INTEGER NOT NULL DEFAULT 0 CHECK (IsPinned IN (0, 1)),
            IsFolded   INTEGER NOT NULL DEFAULT 0 CHECK (IsFolded IN (0, 1)),
            SortOrder  REAL    NOT NULL DEFAULT 0,
            CreatedAt  TEXT    NOT NULL,
            UpdatedAt  TEXT    NOT NULL,
            DeletedAt  TEXT    NULL                     -- soft delete; recycle bin
        ) STRICT;

        -- Partial indexes: the recycle bin is small and rarely queried, so
        -- excluding deleted rows keeps the common path's index small.
        CREATE INDEX IX_Notes_Folder
            ON Notes (FolderId, SortOrder) WHERE DeletedAt IS NULL;

        CREATE INDEX IX_Notes_Updated
            ON Notes (UpdatedAt) WHERE DeletedAt IS NULL;

        CREATE INDEX IX_Notes_Deleted
            ON Notes (DeletedAt) WHERE DeletedAt IS NOT NULL;

        -- ---------------------------------------------------------------
        -- Tags
        --
        -- Noto has folders AND tags. Research found SideNotes has folders only
        -- and Noticky tags only; neither has both, and the combination is
        -- nearly free.
        -- ---------------------------------------------------------------
        CREATE TABLE Tags (
            Id        TEXT NOT NULL PRIMARY KEY,        -- ULID (ADR-012)
            Name      TEXT NOT NULL COLLATE NOCASE,
            ColorKey  TEXT NULL,
            CreatedAt TEXT NOT NULL
        ) STRICT;

        -- NOCASE so 'work' and 'Work' cannot become two tags — a papercut every
        -- tagging system eventually has to fix.
        CREATE UNIQUE INDEX UX_Tags_Name ON Tags (Name COLLATE NOCASE);

        -- A pure join table: no identity of its own, so a composite key rather
        -- than a ULID (ADR-012).
        CREATE TABLE NoteTags (
            NoteId TEXT NOT NULL REFERENCES Notes (Id) ON DELETE CASCADE,
            TagId  TEXT NOT NULL REFERENCES Tags  (Id) ON DELETE CASCADE,
            PRIMARY KEY (NoteId, TagId)
        ) STRICT;

        CREATE INDEX IX_NoteTags_Tag ON NoteTags (TagId);

        -- ---------------------------------------------------------------
        -- Settings
        --
        -- Key/value. Schema version is PRAGMA user_version, not a row here.
        -- ---------------------------------------------------------------
        CREATE TABLE Settings (
            Key   TEXT NOT NULL PRIMARY KEY,
            Value TEXT NOT NULL
        ) STRICT;
        """;
}
