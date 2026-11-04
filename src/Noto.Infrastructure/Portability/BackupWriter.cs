using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Noto.Core.Images;
using Noto.Core.Portability;
using Noto.Infrastructure.Storage;

namespace Noto.Infrastructure.Portability;

/// <summary>
/// Writes a Noto backup (ADR-020 §2): a snapshot of the database taken through SQLite's online backup API, every image
/// Noto named, and a manifest hashing each. The archive is built beside its destination and renamed into place only
/// when whole, so a cancelled or failed backup leaves nothing behind.
/// </summary>
public static class BackupWriter
{
    /// <summary>Writes a backup of <paramref name="database"/> and <paramref name="attachmentsDirectory"/> to <paramref name="destination"/>.</summary>
    /// <returns>What the backup holds.</returns>
    /// <exception cref="OperationCanceledException">Cancelled: nothing was written.</exception>
    /// <exception cref="PortabilityException">The backup could not be taken or written: nothing was written.</exception>
    public static BackupCounts Create(
        NotoDatabase database,
        string attachmentsDirectory,
        string destination,
        string appVersion,
        TimeProvider time,
        IProgress<PortabilityProgress>? progress = null,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(attachmentsDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentNullException.ThrowIfNull(time);

        string work = Directory.CreateTempSubdirectory("noto-backup-").FullName;
        string partial = destination + ".partial";
        try
        {
            long started = Stopwatch.GetTimestamp();
            string snapshot = Path.Join(work, BackupManifest.DatabaseEntry);
            Snapshot(database, snapshot);
            cancellation.ThrowIfCancellationRequested();
            double snapshotMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

            (int schema, BackupCounts counts) = Describe(snapshot, attachmentsDirectory);
            var images = Images(attachmentsDirectory);
            int total = images.Count + 1;
            progress?.Report(new PortabilityProgress(0, total));

            var entries = new List<BackupEntry>(total);
            using (var archive = ZipFile.Open(partial, ZipArchiveMode.Create))
            {
                entries.Add(Add(archive, BackupManifest.DatabaseEntry, snapshot, CompressionLevel.Optimal, cancellation));
                progress?.Report(new PortabilityProgress(1, total));

                for (int i = 0; i < images.Count; i++)
                {
                    cancellation.ThrowIfCancellationRequested();

                    // Pictures are already compressed; storing them is as small and much faster.
                    entries.Add(Add(archive, $"{ImageReference.Folder}/{Path.GetFileName(images[i])}", images[i], CompressionLevel.NoCompression, cancellation));
                    progress?.Report(new PortabilityProgress(i + 2, total));
                }

                var manifest = new BackupManifest
                {
                    Format = BackupManifest.FormatName,
                    FormatVersion = BackupManifest.CurrentFormatVersion,
                    SchemaVersion = schema,
                    CreatedAt = time.GetUtcNow(),
                    AppVersion = appVersion,
                    Counts = counts with { Images = images.Count },
                    Entries = entries,
                };

                using var writer = new StreamWriter(archive.CreateEntry(BackupManifest.ManifestEntry, CompressionLevel.Optimal).Open());
                writer.Write(manifest.ToJson());
            }

            cancellation.ThrowIfCancellationRequested();
            double archiveMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds - snapshotMs;
            File.Move(partial, destination, overwrite: true);

            // Timing only, on the validation channel: counts and milliseconds, never a name or a path.
            Trace.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"Noto.Portability backup images={images.Count} snapshot-ms={snapshotMs:F0} archive-ms={archiveMs:F0} total-ms={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F0}"));
            return counts with { Images = images.Count };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException or InvalidDataException)
        {
            throw new PortabilityException("The backup could not be written.", ex);
        }
        finally
        {
            TryDelete(partial);
            TryDeleteDirectory(work);
        }
    }

    /// <summary>
    /// A consistent copy of the live database in one file, through the engine (committed WAL content included,
    /// <c>rowid</c>s kept), checked before it is used. Never a file copy, never <c>VACUUM INTO</c> (ADR-012, ADR-018).
    /// </summary>
    internal static void Snapshot(NotoDatabase database, string path)
    {
        using (SqliteConnection source = database.OpenConnection())
        using (SqliteConnection target = Snapshots.Open(path, SqliteOpenMode.ReadWriteCreate))
        {
            source.BackupDatabase(target);
        }

        if (!Snapshots.IsIntact(path))
        {
            throw new InvalidDataException("The database snapshot did not pass its integrity check.");
        }
    }

    /// <summary>The snapshot's schema version and what it holds.</summary>
    private static (int Schema, BackupCounts Counts) Describe(string snapshot, string attachmentsDirectory)
    {
        using SqliteConnection connection = Snapshots.Open(snapshot, SqliteOpenMode.ReadOnly);
        int Count(string sql)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        return (
            Count("PRAGMA user_version;"),
            new BackupCounts(
                Count("SELECT COUNT(*) FROM Folders WHERE DeletedAt IS NULL;"),
                Count("SELECT COUNT(*) FROM Notes WHERE DeletedAt IS NULL;"),
                Count("SELECT COUNT(*) FROM Notes WHERE DeletedAt IS NOT NULL;"),
                0));
    }

    /// <summary>Every image file Noto named, in name order; anything else in the folder is not Noto's to back up.</summary>
    private static List<string> Images(string attachmentsDirectory) =>
        Directory.Exists(attachmentsDirectory)
            ? [.. Directory.EnumerateFiles(attachmentsDirectory)
                .Where(f => ImageReference.TryFileName($"{ImageReference.Folder}/{Path.GetFileName(f)}", out _))
                .Order(StringComparer.Ordinal)]
            : [];

    /// <summary>Copies <paramref name="file"/> into the archive, hashing exactly the bytes written.</summary>
    private static BackupEntry Add(ZipArchive archive, string path, string file, CompressionLevel level, CancellationToken cancellation)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using FileStream input = File.OpenRead(file);
        using Stream output = archive.CreateEntry(path, level).Open();

        var buffer = new byte[81920];
        long bytes = 0;
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellation.ThrowIfCancellationRequested();
            hash.AppendData(buffer, 0, read);
            output.Write(buffer, 0, read);
            bytes += read;
        }

        return new BackupEntry(path, bytes, Convert.ToHexStringLower(hash.GetHashAndReset()));
    }

    internal static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover partial file is named as one and is replaced by the next backup to the same place.
        }
    }

    internal static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Temporary files only; Windows clears the temporary folder.
        }
    }
}

/// <summary>A backup, restore, export or import could not be done; nothing was changed.</summary>
public sealed class PortabilityException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Opening a database file that is not the live one: unpooled, so no handle outlives its use.</summary>
internal static class Snapshots
{
    public static SqliteConnection Open(string path, SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = mode, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }

    /// <summary>Whether SQLite's own integrity check passes: the file is a database, and an undamaged one.</summary>
    public static bool IsIntact(string path)
    {
        try
        {
            using SqliteConnection connection = Open(path, SqliteOpenMode.ReadOnly);
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA integrity_check;";
            return command.ExecuteScalar() as string == "ok";
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    /// <summary>The file's <c>user_version</c>: its schema version.</summary>
    public static int SchemaVersion(string path)
    {
        using SqliteConnection connection = Open(path, SqliteOpenMode.ReadOnly);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }
}
