using System.Text.Json;
using Noto.Core.Images;

namespace Noto.Core.Portability;

/// <summary>
/// What a Noto backup holds and how to check it (ADR-020 §2): the format and its version, the database's schema
/// version, what it counts, and every other entry of the archive with its size and SHA-256.
/// </summary>
public sealed record BackupManifest
{
    /// <summary>The value of <see cref="Format"/> in every Noto backup.</summary>
    public const string FormatName = "noto-backup";

    /// <summary>The format this build writes, and the newest it reads.</summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>The manifest's own entry in the archive.</summary>
    public const string ManifestEntry = "manifest.json";

    /// <summary>The database snapshot's entry in the archive.</summary>
    public const string DatabaseEntry = "noto.db";

    /// <summary>The file extension of a backup.</summary>
    public const string Extension = ".notobackup";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public required string Format { get; init; }

    public required int FormatVersion { get; init; }

    /// <summary>The snapshot's <c>user_version</c>: the schema the database was written in.</summary>
    public required int SchemaVersion { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required string AppVersion { get; init; }

    public required BackupCounts Counts { get; init; }

    public required IReadOnlyList<BackupEntry> Entries { get; init; }

    /// <summary>The manifest as written into the archive.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>
    /// The manifest in <paramref name="json"/>, checked for what can be checked without the archive: that it is a
    /// Noto backup, not from a newer Noto, and that its entries are exactly the database and Noto-named images, each
    /// once, with a hash. Whether the archive's content matches it is the backup reader's to check.
    /// </summary>
    public static (BackupManifest? Manifest, BackupProblem? Problem) Read(string json, int latestSchemaVersion)
    {
        BackupManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<BackupManifest>(json, Json);
        }
        catch (JsonException)
        {
            return (null, BackupProblem.NotABackup);
        }

        if (manifest is null || manifest.Format != FormatName || manifest.Entries is null || manifest.Counts is null)
        {
            return (null, BackupProblem.NotABackup);
        }

        if (manifest.FormatVersion > CurrentFormatVersion)
        {
            return (null, BackupProblem.NewerFormat);
        }

        if (manifest.FormatVersion < 1)
        {
            return (null, BackupProblem.NotABackup);
        }

        if (manifest.SchemaVersion > latestSchemaVersion)
        {
            return (null, BackupProblem.NewerSchema);
        }

        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (BackupEntry entry in manifest.Entries)
        {
            if (entry is null || !IsEntryPath(entry.Path) || !paths.Add(entry.Path) || entry.Bytes < 0 || !IsSha256(entry.Sha256))
            {
                return (null, BackupProblem.Corrupted);
            }
        }

        return paths.Contains(DatabaseEntry) ? (manifest, null) : (null, BackupProblem.Incomplete);
    }

    /// <summary>
    /// Whether <paramref name="path"/> is a path a backup may hold: the database, or an image in <c>attachments/</c>
    /// under a name Noto made. Nothing else, so no entry can name a file outside the place it is restored to.
    /// </summary>
    public static bool IsEntryPath(string? path) =>
        path == DatabaseEntry || ImageReference.TryFileName(path, out _);

    private static bool IsSha256(string? hash) =>
        hash is { Length: 64 } && hash.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'));
}

/// <summary>One entry of a backup besides the manifest: its path in the archive, its size and its SHA-256 (lower hex).</summary>
public sealed record BackupEntry(string Path, long Bytes, string Sha256);

/// <summary>What a backup holds, for the user to recognise it by before restoring.</summary>
public sealed record BackupCounts(int Folders, int Notes, int BinnedNotes, int Images);

/// <summary>Why a file cannot be restored. Each says what is wrong with it, and nothing was changed.</summary>
public enum BackupProblem
{
    /// <summary>Not a Noto backup at all (or its manifest cannot be read).</summary>
    NotABackup,

    /// <summary>Its content does not match its manifest: damaged, or changed after it was written.</summary>
    Corrupted,

    /// <summary>Something the manifest lists is missing.</summary>
    Incomplete,

    /// <summary>Written by a newer Noto in a format this one does not know.</summary>
    NewerFormat,

    /// <summary>Its notes are in a database schema newer than this Noto knows.</summary>
    NewerSchema,

    /// <summary>The file could not be read.</summary>
    Unreadable,
}
