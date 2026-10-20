namespace Noto.Infrastructure.Storage.Migrations;

/// <summary>
/// Migration 003 — every active note has a folder (I7, deletion-semantics §13).
/// </summary>
/// <remarks>
/// <para>
/// <b>Shipped. Never edit this.</b> Corrections are a new migration.
/// </para>
/// <para>
/// The workspace shows notes only inside a live folder (C1, ADR-016). An active note with no folder is reachable from
/// no surface, so from P1.1 no command can create one, and this migration makes the database refuse one too:
/// </para>
/// <list type="bullet">
///   <item><b>Repair, explicitly.</b> An active note that already has no folder (written by an earlier engine; no
///         Noto surface ever created one) is moved to the recycle bin, stamped now. Nothing is lost and nothing is
///         hidden: the bin lists it, and restoring it into a folder the user chooses (<c>RestoreNoteToFolder</c>)
///         brings it back. No folder is invented for it.</item>
///   <item><b>Refuse.</b> Two triggers abort any insert or update that would leave an active note without a folder,
///         including the foreign key's <c>ON DELETE SET NULL</c> when a folder row is removed under an active note.
///         A deleted note may still have no folder: that is how the repaired ones wait in the bin.</item>
/// </list>
/// <para>
/// It changes user data, so it runs behind the pre-migration safety copy. The timestamp is written in the exact form
/// <c>Timestamps.Format</c> produces (UTC, seven fractional digits, <c>+00:00</c>).
/// </para>
/// </remarks>
internal static class SchemaV3
{
    internal const string Sql = """
        UPDATE Notes
        SET DeletedAt = strftime('%Y-%m-%dT%H:%M:%f', 'now') || '0000+00:00',
            UpdatedAt = strftime('%Y-%m-%dT%H:%M:%f', 'now') || '0000+00:00'
        WHERE FolderId IS NULL AND DeletedAt IS NULL;

        CREATE TRIGGER Notes_ActiveNoteNeedsFolder_Insert
        BEFORE INSERT ON Notes
        WHEN NEW.DeletedAt IS NULL AND NEW.FolderId IS NULL
        BEGIN
            SELECT RAISE(ABORT, 'I7: an active note needs a folder');
        END;

        CREATE TRIGGER Notes_ActiveNoteNeedsFolder_Update
        BEFORE UPDATE OF FolderId, DeletedAt ON Notes
        WHEN NEW.DeletedAt IS NULL AND NEW.FolderId IS NULL
        BEGIN
            SELECT RAISE(ABORT, 'I7: an active note needs a folder');
        END;
        """;
}
