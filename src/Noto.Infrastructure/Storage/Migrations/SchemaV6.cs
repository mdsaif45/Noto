namespace Noto.Infrastructure.Storage.Migrations;

/// <summary>
/// Migration 006 — a folder's note sort (#242; parity C10): Manual, Name, Created or Modified.
/// </summary>
/// <remarks>
/// <para>
/// <b>Shipped. Never edit this.</b> Corrections are a new migration.
/// </para>
/// <para>
/// One added column, stored by the member's name (<c>NoteSorts.Parse</c>), and <c>Manual</c> for every folder already
/// there, which is how they were all ordered before. <c>ALTER TABLE … ADD COLUMN</c> changes only the table's
/// definition: no row is rewritten, so no folder's name, order or timestamps move. No <c>CHECK</c> on the value, for
/// migration 5's reason: the application writes only a member's name and reads anything else as Manual.
/// </para>
/// <para>
/// No pre-migration safety copy, unlike migration 5: it rewrites no row and does not touch <c>Notes</c>, the table
/// whose first change since a release was migration 5's reason for one. It extends a structure, as migration 4 did.
/// </para>
/// </remarks>
internal static class SchemaV6
{
    internal const string Sql = """
        ALTER TABLE Folders ADD COLUMN NoteSort TEXT NOT NULL DEFAULT 'Manual';
        """;
}
