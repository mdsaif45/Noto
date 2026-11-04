using System.Globalization;
using System.Text.Json;
using Noto.Core.Images;
using Noto.Core.Portability;
using Noto.Infrastructure.Storage;

namespace Noto.Infrastructure.Portability;

/// <summary>
/// A restore, from a checked backup to the workspace it replaces (ADR-020 §3). The backup is read into a staging
/// directory under the data root; a marker written last says it is complete; at the next start, before the database
/// is opened, the staged database and images replace the current ones. The swap is renames on one volume, and every
/// step that fails puts back what had moved: afterwards the workspace is the old one or the new one, never a mix.
/// </summary>
public static class PendingRestore
{
    /// <summary>Where a backup is read to before it replaces the workspace.</summary>
    public const string StagingDirectoryName = "restore-staging";

    /// <summary>Written last: the staged backup is whole and should replace the workspace at the next start.</summary>
    public const string MarkerName = "restore-pending.json";

    /// <summary>The staging directory for <paramref name="paths"/>.</summary>
    public static string StagingDirectory(NotoStoragePaths paths) => Path.Join(paths.Root, StagingDirectoryName);

    /// <summary>
    /// Reads <paramref name="archive"/> into a fresh staging directory, checking all of it. Nothing is restored yet:
    /// <see cref="Commit"/> asks for that, <see cref="Discard"/> drops it.
    /// </summary>
    public static (BackupManifest? Manifest, BackupProblem? Problem) Stage(
        NotoStoragePaths paths,
        string archive,
        IProgress<PortabilityProgress>? progress = null,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        Discard(paths);
        try
        {
            var read = BackupReader.Read(archive, StagingDirectory(paths), MigrationRunner.LatestVersion, progress, cancellation);
            if (read.Manifest is null)
            {
                Discard(paths);
            }

            return read;
        }
        catch (OperationCanceledException)
        {
            Discard(paths);
            throw;
        }
    }

    /// <summary>Marks the staged backup as the workspace to restore at the next start.</summary>
    public static void Commit(NotoStoragePaths paths, BackupManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(manifest);
        File.WriteAllText(Path.Join(paths.Root, MarkerName), JsonSerializer.Serialize(new { manifest.CreatedAt, manifest.Counts }));
    }

    /// <summary>Drops a staged backup and its marker, if any.</summary>
    public static void Discard(NotoStoragePaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        BackupWriter.TryDelete(Path.Join(paths.Root, MarkerName));
        BackupWriter.TryDeleteDirectory(StagingDirectory(paths));
    }

    /// <summary>
    /// At start, before the database is opened: replaces the workspace with a committed staged backup, if there is
    /// one. A staging directory without its marker is an abandoned restore and is removed.
    /// </summary>
    public static RestoreOutcome ApplyIfPending(NotoStoragePaths paths, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(time);

        string marker = Path.Join(paths.Root, MarkerName);
        string staging = StagingDirectory(paths);
        if (!File.Exists(marker))
        {
            BackupWriter.TryDeleteDirectory(staging);
            return RestoreOutcome.None;
        }

        string stagedDatabase = Path.Join(staging, BackupManifest.DatabaseEntry);
        string stagedImages = Path.Join(staging, ImageReference.Folder);
        string aside = Path.Join(paths.BackupsDirectory, string.Create(CultureInfo.InvariantCulture, $"replaced-{time.GetUtcNow():yyyyMMdd'T'HHmmss'Z'}"));
        var moved = new Stack<(string From, string To)>();

        try
        {
            if (!File.Exists(stagedDatabase))
            {
                throw new IOException("The staged backup has no database.");
            }

            // The current workspace, aside: the database with its WAL and shared memory, and the images.
            Directory.CreateDirectory(aside);
            foreach (string suffix in new[] { string.Empty, "-wal", "-shm" })
            {
                Move(paths.DatabaseFile + suffix, Path.Join(aside, Path.GetFileName(paths.DatabaseFile) + suffix), moved);
            }

            Move(paths.AttachmentsDirectory, Path.Join(aside, ImageReference.Folder), moved);

            // The backup, in place.
            Move(stagedDatabase, paths.DatabaseFile, moved);
            if (Directory.Exists(stagedImages))
            {
                Move(stagedImages, paths.AttachmentsDirectory, moved);
            }
            else
            {
                Directory.CreateDirectory(paths.AttachmentsDirectory);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Everything that moved goes back, newest first: the workspace is as it was.
            bool undone = true;
            while (moved.TryPop(out var step))
            {
                try
                {
                    MovePath(step.To, step.From);
                }
                catch (Exception undo) when (undo is IOException or UnauthorizedAccessException)
                {
                    // Left where it is, in the aside directory, which is then kept: nothing is deleted that holds it.
                    undone = false;
                }
            }

            Directory.CreateDirectory(paths.AttachmentsDirectory);
            BackupWriter.TryDelete(marker);
            BackupWriter.TryDeleteDirectory(staging);
            if (undone)
            {
                BackupWriter.TryDeleteDirectory(aside);
            }

            return RestoreOutcome.RolledBack;
        }

        BackupWriter.TryDelete(marker);
        BackupWriter.TryDeleteDirectory(staging);

        // The replaced workspace was the undo for the swap; the user's undo is the backup taken before the restore.
        BackupWriter.TryDeleteDirectory(aside);
        return RestoreOutcome.Applied;
    }

    /// <summary>Moves a file or a directory that exists, recording the move so it can be undone.</summary>
    private static void Move(string from, string to, Stack<(string From, string To)> moved)
    {
        if (!File.Exists(from) && !Directory.Exists(from))
        {
            return;
        }

        MovePath(from, to);
        moved.Push((from, to));
    }

    private static void MovePath(string from, string to)
    {
        if (Directory.Exists(from))
        {
            Directory.Move(from, to);
        }
        else
        {
            File.Move(from, to);
        }
    }
}

/// <summary>What happened to a pending restore at start.</summary>
public enum RestoreOutcome
{
    /// <summary>There was none.</summary>
    None,

    /// <summary>The workspace was replaced by the backup.</summary>
    Applied,

    /// <summary>The replacement failed part way and the workspace was put back as it was.</summary>
    RolledBack,
}
