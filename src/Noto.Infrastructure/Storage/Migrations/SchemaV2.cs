namespace Noto.Infrastructure.Storage.Migrations;

/// <summary>
/// Migration 002 — align the schema with the SideNotes parity specification.
/// </summary>
/// <remarks>
/// <para>
/// <b>Shipped. Never edit this.</b> Corrections are a new migration.
/// </para>
/// <para>
/// Three changes, each resolving a conflict between migration 001 and the
/// parity specification:
/// </para>
/// <list type="bullet">
///   <item><b>Remove <c>Notes.Title</c></b> — parity B16: the title is the
///         first line of the content, and there is "no separate title field".
///         A stored title is a second source of truth that diverges.</item>
///   <item><b>Add <c>Folders.IsPinned</c></b> — parity C8.</item>
///   <item><b>Add <c>Folders.DeletedAt</c></b> — parity C11.</item>
/// </list>
/// <para>
/// <b>This is the first migration that transforms user data</b>, so it runs
/// behind a pre-migration safety copy and requires foreign key enforcement to
/// be suspended (see <see cref="Migration.RequiresForeignKeysDisabled"/>).
/// </para>
/// </remarks>
internal static class SchemaV2
{
    internal const string Sql = """
        -- ---------------------------------------------------------------
        -- 1. Preserve title data BEFORE the column disappears.
        --
        -- Notes.Title holds user input. Dropping the column without this
        -- destroys whatever the user typed as a title.
        --
        --   empty title                  -> nothing to preserve
        --   title already the first line -> already represented, skip
        --   otherwise                    -> prepend as a level-1 heading
        --
        -- The comparison is deliberately literal, not a markdown parse. A
        -- migration is the wrong place for a parser: it has to be correct once,
        -- on data it cannot inspect, with no way to ask the user.
        -- ---------------------------------------------------------------
        --
        -- NOTE: SQL TRIM() strips SPACES ONLY. A tab-or-newline-only title
        -- would otherwise survive the emptiness check and be prepended as an
        -- empty heading. Stripping the other whitespace explicitly is not
        -- pedantry — a test caught exactly this.
        UPDATE Notes
        SET Content = '# ' || TRIM(Title, ' ' || char(9) || char(10) || char(13))
                      || char(10) || char(10) || Content
        WHERE TRIM(Title, ' ' || char(9) || char(10) || char(13)) <> ''
          -- Skip when the content already opens with the title, with or without
          -- heading markers. Compared against the content's first line only.
          AND TRIM(Title, ' ' || char(9) || char(10) || char(13)) <> TRIM(
                CASE
                    WHEN INSTR(Content, char(10)) > 0
                        THEN SUBSTR(Content, 1, INSTR(Content, char(10)) - 1)
                    ELSE Content
                END)
          AND TRIM(Title, ' ' || char(9) || char(10) || char(13)) <> TRIM(LTRIM(
                CASE
                    WHEN INSTR(Content, char(10)) > 0
                        THEN SUBSTR(Content, 1, INSTR(Content, char(10)) - 1)
                    ELSE Content
                END, '#  '));

        -- ---------------------------------------------------------------
        -- 2. Rebuild Notes without Title.
        --
        -- A table rebuild rather than DROP COLUMN: the column is referenced by
        -- nothing today, but the rebuild is the pattern every later structural
        -- change needs, and proving it once here is free.
        --
        -- DROP TABLE below fires ON DELETE actions. NoteTags cascades from
        -- Notes, so with foreign keys enabled this silently empties NoteTags —
        -- measured, and the reason this migration sets
        -- RequiresForeignKeysDisabled. The runner re-enables enforcement and
        -- runs foreign_key_check before committing.
        -- ---------------------------------------------------------------
        CREATE TABLE Notes_new (
            Id         TEXT    PRIMARY KEY NOT NULL,
            FolderId   TEXT    NULL REFERENCES Folders (Id) ON DELETE SET NULL,
            Content    TEXT    NOT NULL DEFAULT '',     -- markdown source
            ColorKey   TEXT    NULL,
            IsPinned   INTEGER NOT NULL DEFAULT 0 CHECK (IsPinned IN (0, 1)),
            IsFolded   INTEGER NOT NULL DEFAULT 0 CHECK (IsFolded IN (0, 1)),
            SortOrder  REAL    NOT NULL DEFAULT 0,
            CreatedAt  TEXT    NOT NULL,
            UpdatedAt  TEXT    NOT NULL,
            DeletedAt  TEXT    NULL
        ) STRICT;

        INSERT INTO Notes_new (
            Id, FolderId, Content, ColorKey, IsPinned, IsFolded,
            SortOrder, CreatedAt, UpdatedAt, DeletedAt)
        SELECT
            Id, FolderId, Content, ColorKey, IsPinned, IsFolded,
            SortOrder, CreatedAt, UpdatedAt, DeletedAt
        FROM Notes;

        DROP TABLE Notes;

        ALTER TABLE Notes_new RENAME TO Notes;

        CREATE INDEX IX_Notes_Folder
            ON Notes (FolderId, SortOrder) WHERE DeletedAt IS NULL;

        CREATE INDEX IX_Notes_Updated
            ON Notes (UpdatedAt) WHERE DeletedAt IS NULL;

        CREATE INDEX IX_Notes_Deleted
            ON Notes (DeletedAt) WHERE DeletedAt IS NOT NULL;

        -- ---------------------------------------------------------------
        -- 3. Folders gain pinning and soft deletion.
        --
        -- Added with ALTER TABLE rather than a rebuild: no column is being
        -- removed, so there is nothing for a rebuild to protect against, and
        -- existing rows take the defaults. Every existing folder therefore
        -- stays unpinned and active, which is correct.
        -- ---------------------------------------------------------------
        ALTER TABLE Folders
            ADD COLUMN IsPinned INTEGER NOT NULL DEFAULT 0 CHECK (IsPinned IN (0, 1));

        ALTER TABLE Folders ADD COLUMN DeletedAt TEXT NULL;

        CREATE INDEX IX_Folders_Deleted
            ON Folders (DeletedAt) WHERE DeletedAt IS NOT NULL;
        """;
}
