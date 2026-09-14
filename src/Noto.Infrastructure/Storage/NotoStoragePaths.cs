namespace Noto.Infrastructure.Storage;

/// <summary>
/// Where Noto's data lives on disk (data-model.md).
/// </summary>
/// <remarks>
/// <para>
/// <c>%LOCALAPPDATA%</c>, not <c>%APPDATA%</c>: a roaming profile would try to
/// replicate a live SQLite database between machines, which corrupts it.
/// </para>
/// <para>
/// Nothing here is hardcoded to a developer's machine, and tests never touch
/// these paths — they pass an explicit temporary directory instead.
/// </para>
/// </remarks>
public sealed class NotoStoragePaths
{
    private const string FolderName = "Noto";
    private const string DatabaseFileName = "noto.db";

    /// <summary>The real per-user location.</summary>
    public static NotoStoragePaths ForCurrentUser() => new(
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            FolderName));

    /// <summary>An explicit root. Used by tests, and by any future portable mode.</summary>
    public static NotoStoragePaths At(string root) => new(root);

    private NotoStoragePaths(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Root = root;
    }

    /// <summary>e.g. <c>%LOCALAPPDATA%\Noto</c></summary>
    public string Root { get; }

    /// <summary>The SQLite database. Its -wal and -shm siblings live beside it.</summary>
    public string DatabaseFile => Path.Combine(Root, DatabaseFileName);

    /// <summary>
    /// Attachment binaries. Files live on disk, not in the database: blobs
    /// would bloat it, slow backup, and make the store harder to inspect.
    /// </summary>
    public string AttachmentsDirectory => Path.Combine(Root, "attachments");

    /// <summary>
    /// Backup snapshots. Plain .db files produced by SQLite's backup API —
    /// never a file copy, which can catch a half-written WAL.
    /// </summary>
    public string BackupsDirectory => Path.Combine(Root, "backups");

    /// <summary>Logs. Never contain note content or user file paths.</summary>
    public string LogsDirectory => Path.Combine(Root, "logs");

    /// <summary>Creates the directories. Idempotent.</summary>
    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(AttachmentsDirectory);
        Directory.CreateDirectory(BackupsDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }
}
