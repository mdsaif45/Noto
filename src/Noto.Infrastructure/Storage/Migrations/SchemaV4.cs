namespace Noto.Infrastructure.Storage.Migrations;

/// <summary>
/// Migration 004 — the full-text search index (ADR-018 §1).
/// </summary>
/// <remarks>
/// <para>
/// <b>Shipped. Never edit this.</b> Corrections are a new migration.
/// </para>
/// <para>
/// An external-content FTS5 table over <c>Notes.Content</c>, keyed by the notes' implicit <c>rowid</c> (ADR-012): the
/// index holds tokens only, the text stays in <c>Notes</c>. Three triggers keep it exact; the update trigger fires on a
/// change of <c>Content</c> only, so moving, pinning, folding or reordering a note never touches it. Every note is
/// indexed, deleted or not: whether a result is shown is the query's decision.
/// </para>
/// <para>
/// It changes no user data (the index is derived), so it needs no safety copy. The last statement builds the index
/// from the notes already there.
/// </para>
/// <para>
/// <b>The <c>rowid</c> rule (ADR-012):</b> <c>VACUUM</c> or a rebuild of <c>Notes</c> can renumber its implicit
/// <c>rowid</c>. Whatever does either must rebuild this index in the same transaction:
/// <c>INSERT INTO NotesFts(NotesFts) VALUES ('rebuild');</c>
/// </para>
/// </remarks>
internal static class SchemaV4
{
    internal const string Sql = """
        CREATE VIRTUAL TABLE NotesFts USING fts5(
            Content,
            content = 'Notes',
            content_rowid = 'rowid',
            tokenize = 'unicode61 remove_diacritics 2'
        );

        CREATE TRIGGER Notes_Fts_Insert AFTER INSERT ON Notes
        BEGIN
            INSERT INTO NotesFts (rowid, Content) VALUES (new.rowid, new.Content);
        END;

        CREATE TRIGGER Notes_Fts_Delete AFTER DELETE ON Notes
        BEGIN
            INSERT INTO NotesFts (NotesFts, rowid, Content) VALUES ('delete', old.rowid, old.Content);
        END;

        CREATE TRIGGER Notes_Fts_Update AFTER UPDATE OF Content ON Notes
        BEGIN
            INSERT INTO NotesFts (NotesFts, rowid, Content) VALUES ('delete', old.rowid, old.Content);
            INSERT INTO NotesFts (rowid, Content) VALUES (new.rowid, new.Content);
        END;

        INSERT INTO NotesFts (NotesFts) VALUES ('rebuild');
        """;
}
