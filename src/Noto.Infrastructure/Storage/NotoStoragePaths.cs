using System.Diagnostics;

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
    public static NotoStoragePaths ForCurrentUser()
    {
        string localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);

        if (string.IsNullOrWhiteSpace(localAppData))
        {
            // Should not happen on Windows, but a silent empty string here
            // would resolve the database to the working directory.
            throw new InvalidOperationException(
                "The local application data folder could not be determined.");
        }

        // Path.Join rather than Path.Combine: Combine reinterprets a rooted
        // second argument and silently discards the first, which is a sharp
        // edge with no upside here.
        return new NotoStoragePaths(Path.Join(localAppData, FolderName));
    }

    /// <summary>An explicit root. Used by tests, and by any future portable mode.</summary>
    public static NotoStoragePaths At(string root) => new(root);

    private NotoStoragePaths(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        // Normalise once, here, so every derived path below is provably safe.
        //
        // Path.Combine silently discards earlier arguments when a later one is
        // rooted, so combining an unvalidated root with a literal segment can
        // produce a path somewhere entirely unexpected. Resolving to a full
        // path up front means Root is always absolute and the fixed literal
        // segments can never be rooted.
        Root = Path.GetFullPath(root);
    }

    /// <summary>
    /// Joins a fixed, known-relative segment onto <see cref="Root"/>.
    /// </summary>
    /// <remarks>
    /// Every caller passes a compile-time literal, never user input, so this
    /// cannot be used to escape the root. The assertion documents and enforces
    /// that invariant rather than leaving it to convention.
    /// </remarks>
    private string Resolve(string relativeSegment)
    {
        Debug.Assert(
            !Path.IsPathRooted(relativeSegment),
            $"'{relativeSegment}' must be relative; a rooted segment would discard Root.");

        // Path.Join concatenates without reinterpreting a rooted segment.
        return Path.Join(Root, relativeSegment);
    }

    /// <summary>e.g. <c>%LOCALAPPDATA%\Noto</c></summary>
    public string Root { get; }

    /// <summary>The SQLite database. Its -wal and -shm siblings live beside it.</summary>
    public string DatabaseFile => Resolve(DatabaseFileName);

    /// <summary>
    /// Attachment binaries. Files live on disk, not in the database: blobs
    /// would bloat it, slow backup, and make the store harder to inspect.
    /// </summary>
    public string AttachmentsDirectory => Resolve("attachments");

    /// <summary>
    /// Backup snapshots. Plain .db files produced by SQLite's backup API —
    /// never a file copy, which can catch a half-written WAL.
    /// </summary>
    public string BackupsDirectory => Resolve("backups");

    /// <summary>Logs. Never contain note content or user file paths.</summary>
    public string LogsDirectory => Resolve("logs");

    /// <summary>Creates the directories. Idempotent.</summary>
    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(AttachmentsDirectory);
        Directory.CreateDirectory(BackupsDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }
}
