using System.IO.Compression;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Noto.Core.Portability;
using Noto.Infrastructure.Storage;

namespace Noto.Infrastructure.Portability;

/// <summary>
/// Reads a Noto backup back (ADR-020 §3, step 1): checks all of it — the manifest, every entry's presence, size and
/// SHA-256, that nothing else is in it, and that the database is intact and of a schema this build can open —
/// extracting it into a directory of the caller's as it goes. Nothing outside that directory is touched.
/// </summary>
public static class BackupReader
{
    /// <summary>The largest manifest read: a backup's manifest is a few kilobytes per thousand images.</summary>
    private const int ManifestLimit = 16 * 1024 * 1024;

    /// <summary>
    /// Checks <paramref name="archive"/> and extracts it into <paramref name="into"/>, which must be empty or absent.
    /// </summary>
    /// <returns>The manifest, or why the file is not a backup that can be restored.</returns>
    /// <exception cref="OperationCanceledException">Cancelled: <paramref name="into"/> may hold part of the backup.</exception>
    public static (BackupManifest? Manifest, BackupProblem? Problem) Read(
        string archive,
        string into,
        int latestSchemaVersion,
        IProgress<PortabilityProgress>? progress = null,
        CancellationToken cancellation = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archive);
        ArgumentException.ThrowIfNullOrWhiteSpace(into);

        try
        {
            using ZipArchive zip = ZipFile.OpenRead(archive);
            ZipArchiveEntry? manifestEntry = zip.GetEntry(BackupManifest.ManifestEntry);
            if (manifestEntry is null || manifestEntry.Length > ManifestLimit)
            {
                return (null, BackupProblem.NotABackup);
            }

            string json;
            using (var reader = new StreamReader(manifestEntry.Open()))
            {
                json = reader.ReadToEnd();
            }

            (BackupManifest? manifest, BackupProblem? problem) = BackupManifest.Read(json, latestSchemaVersion);
            if (manifest is null)
            {
                return (null, problem);
            }

            // Exactly the listed entries: anything else was not written by Noto.
            var listed = manifest.Entries.ToDictionary(e => e.Path, StringComparer.Ordinal);
            if (zip.Entries.Any(e => e.FullName != BackupManifest.ManifestEntry && !listed.ContainsKey(e.FullName)))
            {
                return (null, BackupProblem.Corrupted);
            }

            if (manifest.Entries.Any(e => zip.GetEntry(e.Path) is null))
            {
                return (null, BackupProblem.Incomplete);
            }

            Directory.CreateDirectory(into);
            int done = 0;
            foreach (BackupEntry entry in manifest.Entries)
            {
                cancellation.ThrowIfCancellationRequested();
                if (!Extract(zip.GetEntry(entry.Path)!, entry, Path.Join(into, entry.Path.Replace('/', Path.DirectorySeparatorChar)), cancellation))
                {
                    return (null, BackupProblem.Corrupted);
                }

                progress?.Report(new PortabilityProgress(++done, manifest.Entries.Count));
            }

            string database = Path.Join(into, BackupManifest.DatabaseEntry);
            if (!Snapshots.IsIntact(database))
            {
                return (null, BackupProblem.Corrupted);
            }

            int schema = Snapshots.SchemaVersion(database);
            return schema == manifest.SchemaVersion ? (manifest, null) : (null, BackupProblem.Corrupted);
        }
        catch (InvalidDataException)
        {
            // Not a ZIP, or one whose data does not match its own checksums.
            return (null, LooksLikeZip(archive) ? BackupProblem.Corrupted : BackupProblem.NotABackup);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException)
        {
            return (null, BackupProblem.Unreadable);
        }
    }

    /// <summary>Writes one entry where it belongs, checking its size and SHA-256 against the manifest.</summary>
    private static bool Extract(ZipArchiveEntry entry, BackupEntry expected, string path, CancellationToken cancellation)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using Stream input = entry.Open();
        using FileStream output = File.Create(path);

        var buffer = new byte[81920];
        long bytes = 0;
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellation.ThrowIfCancellationRequested();
            bytes += read;
            if (bytes > expected.Bytes)
            {
                return false;
            }

            hash.AppendData(buffer, 0, read);
            output.Write(buffer, 0, read);
        }

        return bytes == expected.Bytes && Convert.ToHexStringLower(hash.GetHashAndReset()) == expected.Sha256;
    }

    private static bool LooksLikeZip(string path)
    {
        try
        {
            using FileStream file = File.OpenRead(path);
            Span<byte> head = stackalloc byte[2];
            return file.Read(head) == 2 && head[0] == (byte)'P' && head[1] == (byte)'K';
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
