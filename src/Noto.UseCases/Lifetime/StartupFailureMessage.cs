using Noto.Core.Storage;

namespace Noto.UseCases.Lifetime;

/// <summary>
/// What the user is told when Noto cannot start because of its database (#10).
/// </summary>
/// <remarks>
/// The message names the database file, because that is what the user must find, move or restore. It is shown on
/// screen only: the log records the reason, never the path (principle 10).
/// </remarks>
public static class StartupFailureMessage
{
    public static string For(StorageFailure reason, string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        string what = reason switch
        {
            StorageFailure.CannotOpen =>
                "Noto could not open its database. Check that no other program has it open and that you can write to its folder.",
            StorageFailure.Corrupt =>
                "Noto's database is damaged, or is not a Noto database. Noto has not changed it. Restore it from a backup, or move it aside so Noto starts with a new one.",
            StorageFailure.MigrationFailed =>
                "Noto could not update its database to this version. The update was undone: the database is as it was.",
            StorageFailure.SchemaTooNew =>
                "This database was written by a newer version of Noto. Install that version to open it; this one will not change it.",
            _ =>
                "Noto could not start because its database failed.",
        };

        return $"{what}\n\nDatabase:\n{databasePath}";
    }
}
