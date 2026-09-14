namespace Noto.Core.Storage;

/// <summary>
/// Persistence diagnostics.
/// </summary>
/// <remarks>
/// A narrow interface rather than a general logger: it names the events worth
/// recording, which makes it obvious that <b>note content is never among
/// them</b> (principle 10). The full logging infrastructure arrives with #7;
/// this is the seam it will plug into.
/// </remarks>
public interface IStorageLog
{
    /// <summary>
    /// A pre-migration safety copy was written. The path is infrastructure, not
    /// user content, so recording it is safe — and it is what a user needs if a
    /// migration goes wrong.
    /// </summary>
    void SafetyCopyCreated(int fromVersion, string path);

    void MigrationStarting(int fromVersion, int toVersion, int pendingCount);

    void MigrationApplied(int version, string description);

    void MigrationCompleted(int version);

    /// <summary>
    /// A migration failed and was rolled back. The exception is a SQLite error
    /// about schema, never about content.
    /// </summary>
    void MigrationFailed(int version, Exception exception);
}

/// <summary>Discards everything. The default until #7 lands.</summary>
public sealed class NullStorageLog : IStorageLog
{
    public static NullStorageLog Instance { get; } = new();

    private NullStorageLog()
    {
    }

    public void SafetyCopyCreated(int fromVersion, string path)
    {
    }

    public void MigrationStarting(int fromVersion, int toVersion, int pendingCount)
    {
    }

    public void MigrationApplied(int version, string description)
    {
    }

    public void MigrationCompleted(int version)
    {
    }

    public void MigrationFailed(int version, Exception exception)
    {
    }
}
