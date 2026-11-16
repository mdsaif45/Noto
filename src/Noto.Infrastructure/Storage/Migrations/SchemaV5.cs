namespace Noto.Infrastructure.Storage.Migrations;

/// <summary>
/// Migration 005 — a note's mode (#195; parity D27, D28): Markdown, Code or PlainText.
/// </summary>
/// <remarks>
/// <para>
/// <b>Shipped. Never edit this.</b> Corrections are a new migration.
/// </para>
/// <para>
/// One added column, stored by the member's name (<c>NoteModes.Parse</c>), and <c>Markdown</c> for every note already
/// there, which is how they were all read before. <c>ALTER TABLE … ADD COLUMN</c> changes only the table's definition:
/// no row is rewritten, so no note's content, timestamps or <c>rowid</c> move, and the search index (which follows
/// <c>Content</c> and the <c>rowid</c>, ADR-012) stays exact without a rebuild.
/// </para>
/// <para>
/// No <c>CHECK</c> on the value: changing one later would need a rebuild of <c>Notes</c> (migration 2's pattern, with
/// foreign keys off), while the application already writes only a member's name and reads anything else as Markdown.
/// </para>
/// <para>
/// It runs behind the pre-migration safety copy although it rewrites no row: it is the first change to the table that
/// holds every note since a release shipped, and the copy costs one file beside the database.
/// </para>
/// </remarks>
internal static class SchemaV5
{
    internal const string Sql = """
        ALTER TABLE Notes ADD COLUMN Mode TEXT NOT NULL DEFAULT 'Markdown';
        """;
}
