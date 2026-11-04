using System.IO.Compression;
using System.Text;
using Noto.Core.Portability;
using Noto.Infrastructure.Portability;
using Noto.Infrastructure.Storage;
using Xunit;

namespace Noto.Infrastructure.Tests.Portability;

/// <summary>
/// Backups and restores on real databases and files (ADR-020 §2, §3): what a backup holds, that a restore brings it all
/// back exactly, and that a backup that is damaged, incomplete, tampered with or from a newer Noto is refused with
/// nothing changed.
/// </summary>
public sealed class BackupRestoreTests : IDisposable
{
    private readonly Workspace _source = new();
    private readonly Workspace _target = new();

    public void Dispose()
    {
        _source.Dispose();
        _target.Dispose();
    }

    [Fact]
    public void An_empty_workspace_backs_up_and_restores()
    {
        string archive = Backup(_source);

        Assert.Equal(RestoreOutcome.Applied, Restore(_target, archive));
        Assert.Equal(_source.Dump(), _target.Dump());
    }

    [Fact]
    public void Everything_comes_back_exactly_folders_notes_order_state_the_bin_settings_and_images()
    {
        string image = Seed(_source);
        string before = _source.Dump();
        string archive = Backup(_source);

        // The target differs in every way first: a restore replaces, it does not merge.
        var other = _target.Folder("Elsewhere");
        _target.Note(other, "a note the restore must remove");
        _target.Image(0x55);

        Assert.Equal(RestoreOutcome.Applied, Restore(_target, archive));
        Assert.Equal(before, _target.Dump());
        Assert.Equal(1, _target.Found("Überraschung"));
        Assert.Equal(0, _target.Found("remove"));
        Assert.NotNull(_target.Images.PathOf(image));
    }

    [Fact]
    public void The_manifest_counts_and_hashes_what_the_archive_holds()
    {
        Seed(_source);
        string archive = Backup(_source);

        using ZipArchive zip = ZipFile.OpenRead(archive);
        using var reader = new StreamReader(zip.GetEntry(BackupManifest.ManifestEntry)!.Open());
        BackupManifest manifest = BackupManifest.Read(reader.ReadToEnd(), MigrationRunner.LatestVersion).Manifest!;

        Assert.Equal(new BackupCounts(3, 6, 1, 1), manifest.Counts);
        Assert.Equal(MigrationRunner.LatestVersion, manifest.SchemaVersion);
        Assert.Equal([BackupManifest.DatabaseEntry, .. Directory.EnumerateFiles(_source.Paths.AttachmentsDirectory).Select(f => "attachments/" + Path.GetFileName(f))], manifest.Entries.Select(e => e.Path));
        foreach (BackupEntry entry in manifest.Entries)
        {
            using var stream = new MemoryStream();
            zip.GetEntry(entry.Path)!.Open().CopyTo(stream);
            Assert.Equal(entry.Bytes, stream.Length);
            Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(stream.ToArray())), entry.Sha256);
        }
    }

    [Fact]
    public void Only_images_Noto_named_are_backed_up()
    {
        _source.Image(0x01);
        File.WriteAllText(Path.Join(_source.Paths.AttachmentsDirectory, "notes.txt"), "not Noto's");

        using ZipArchive zip = ZipFile.OpenRead(Backup(_source));

        Assert.Equal(3, zip.Entries.Count);
        Assert.DoesNotContain(zip.Entries, e => e.FullName.EndsWith("notes.txt", StringComparison.Ordinal));
    }

    [Fact]
    public void A_backup_holds_the_database_and_Noto_named_images_and_nothing_else_in_the_data_folder()
    {
        string image = _source.Image(0x01);
        string root = _source.Root;
        File.WriteAllText(Path.Join(root, "notes.txt"), "a user's own file");
        File.WriteAllText(Path.Join(root, "noto-premigration-v3-20261101T101010.db"), "a migration's copy");
        File.WriteAllText(Path.Join(_source.Paths.BackupsDirectory, "before-restore-2026-11-01 101010.notobackup"), "an earlier backup");
        Directory.CreateDirectory(Path.Join(root, "logs"));
        File.WriteAllText(Path.Join(root, "logs", "noto-1.log"), "a log");
        Directory.CreateDirectory(Path.Join(root, PendingRestore.StagingDirectoryName));
        File.WriteAllText(Path.Join(root, PendingRestore.StagingDirectoryName, "noto.db"), "a staged restore");
        File.WriteAllText(Path.Join(_source.Paths.AttachmentsDirectory, "desktop.ini"), "shell state");
        File.WriteAllText(Path.Join(_source.Paths.AttachmentsDirectory, "01J00000000000000000000A01.png.partial"), "a partial file");

        string archive = Backup(_source);
        using ZipArchive zip = ZipFile.OpenRead(archive);
        using var reader = new StreamReader(zip.GetEntry(BackupManifest.ManifestEntry)!.Open());
        BackupManifest manifest = BackupManifest.Read(reader.ReadToEnd(), MigrationRunner.LatestVersion).Manifest!;

        // Exactly the database and the one image; and the manifest lists exactly what the archive holds.
        Assert.Equal([BackupManifest.DatabaseEntry, image], manifest.Entries.Select(e => e.Path));
        Assert.Equal(
            manifest.Entries.Select(e => e.Path).Append(BackupManifest.ManifestEntry).Order(StringComparer.Ordinal),
            zip.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_restore_that_cannot_move_the_current_database_aside_changes_nothing()
    {
        Seed(_source);
        string archive = Backup(_source);
        _target.Note(_target.Folder("Mine"), "the target's own note");
        _target.Image(0x22);
        string before = _target.Dump();

        (BackupManifest? manifest, _) = PendingRestore.Stage(_target.Paths, archive);
        PendingRestore.Commit(_target.Paths, manifest!);
        _target.Close();

        // The current database held open by something else: the very first move fails.
        RestoreOutcome outcome;
        using (new FileStream(_target.Paths.DatabaseFile, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            outcome = PendingRestore.ApplyIfPending(_target.Paths, Workspace.Time);
        }

        _target.Reopen();
        Assert.Equal(RestoreOutcome.RolledBack, outcome);
        Assert.Equal(before, _target.Dump());
        Assert.False(Directory.Exists(PendingRestore.StagingDirectory(_target.Paths)));
        Assert.Empty(Directory.EnumerateDirectories(_target.Paths.BackupsDirectory, "replaced-*"));
    }

    [Fact]
    public void A_backup_is_taken_while_the_database_is_open_and_holds_what_was_committed()
    {
        var folder = _source.Folder("Open");
        _source.Note(folder, "written before the backup");

        // A connection held open across the backup, as the running app holds its own: the snapshot goes through the
        // engine (WAL included), not around it.
        using var open = _source.Database.OpenConnection();
        string archive = Backup(_source);

        Assert.Equal(RestoreOutcome.Applied, Restore(_target, archive));
        Assert.Equal(1, _target.Found("written"));
    }

    [Fact]
    public void Repeated_backup_and_restore_changes_nothing()
    {
        Seed(_source);
        string before = _source.Dump();

        for (int round = 0; round < 3; round++)
        {
            Assert.Equal(RestoreOutcome.Applied, Restore(_source, Backup(_source)));
        }

        Assert.Equal(before, _source.Dump());
    }

    [Fact]
    public void A_damaged_entry_is_refused_and_nothing_changes()
    {
        Seed(_source);
        string archive = Rewrite(Backup(_source), (name, bytes) => name == BackupManifest.DatabaseEntry ? Flip(bytes, bytes.Length / 2) : bytes);

        AssertRefused(archive, BackupProblem.Corrupted);
    }

    [Fact]
    public void A_changed_image_is_refused()
    {
        Seed(_source);
        string archive = Rewrite(Backup(_source), (name, bytes) => name.StartsWith("attachments/", StringComparison.Ordinal) ? Flip(bytes, bytes.Length - 1) : bytes);

        AssertRefused(archive, BackupProblem.Corrupted);
    }

    [Fact]
    public void A_missing_entry_is_refused_as_incomplete()
    {
        Seed(_source);
        string archive = Rewrite(Backup(_source), (name, bytes) => name.StartsWith("attachments/", StringComparison.Ordinal) ? null : bytes);

        AssertRefused(archive, BackupProblem.Incomplete);
    }

    [Fact]
    public void An_entry_the_manifest_does_not_list_is_refused()
    {
        string archive = Rewrite(Backup(_source), (name, bytes) => bytes, ("attachments/01J00000000000000000000A99.png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]));

        AssertRefused(archive, BackupProblem.Corrupted);
    }

    [Fact]
    public void A_truncated_archive_is_refused()
    {
        Seed(_source);
        string archive = Backup(_source);
        byte[] bytes = File.ReadAllBytes(archive);
        File.WriteAllBytes(archive, bytes[..(bytes.Length / 2)]);

        AssertRefused(archive, BackupProblem.Corrupted);
    }

    [Theory]
    [InlineData("not a zip at all")]
    [InlineData("")]
    public void A_file_that_is_not_a_backup_is_refused(string content)
    {
        string archive = Path.Join(_source.Scratch("other"), "notes.notobackup");
        File.WriteAllText(archive, content);

        AssertRefused(archive, BackupProblem.NotABackup);
    }

    [Fact]
    public void A_zip_without_a_manifest_is_not_a_backup()
    {
        string archive = Rewrite(Backup(_source), (name, bytes) => name == BackupManifest.ManifestEntry ? null : bytes);

        AssertRefused(archive, BackupProblem.NotABackup);
    }

    [Theory]
    [InlineData("\"formatVersion\": 1", "\"formatVersion\": 2", BackupProblem.NewerFormat)]
    [InlineData("\"schemaVersion\": 4", "\"schemaVersion\": 99", BackupProblem.NewerSchema)]
    [InlineData("\"schemaVersion\": 4", "\"schemaVersion\": 3", BackupProblem.Corrupted)]
    [InlineData("\"format\": \"noto-backup\"", "\"format\": \"someone-else\"", BackupProblem.NotABackup)]
    [InlineData("\"path\": \"noto.db\"", "\"path\": \"../noto.db\"", BackupProblem.Corrupted)]
    public void A_manifest_that_does_not_fit_is_refused(string find, string replace, BackupProblem problem)
    {
        Assert.Equal(4, MigrationRunner.LatestVersion);
        string archive = Rewrite(Backup(_source), (name, bytes) =>
            name == BackupManifest.ManifestEntry ? Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace(find, replace, StringComparison.Ordinal)) : bytes);

        AssertRefused(archive, problem);
    }

    [Fact]
    public void A_cancelled_backup_leaves_no_file()
    {
        Seed(_source);
        string destination = Path.Join(_source.Scratch("out"), "cancelled.notobackup");

        Assert.Throws<OperationCanceledException>(() =>
            BackupWriter.Create(_source.Database, _source.Paths.AttachmentsDirectory, destination, "test", Workspace.Time, cancellation: new CancellationToken(true)));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(destination)!));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void A_backup_cancelled_part_way_or_at_its_last_step_leaves_no_file(int cancelAt)
    {
        // Cancelled after the database is in the archive (1), or after the last entry, just before the archive is
        // renamed into place (2 of 2): neither the backup nor its partial file remains.
        _source.Image(0x01);
        string destination = Path.Join(_source.Scratch("cancel-" + cancelAt), "cancelled.notobackup");
        using var cancellation = new CancellationTokenSource();
        var progress = new Progress(p => { if (p.Done == cancelAt) { cancellation.Cancel(); } });

        Assert.Throws<OperationCanceledException>(() =>
            BackupWriter.Create(_source.Database, _source.Paths.AttachmentsDirectory, destination, "test", Workspace.Time, progress, cancellation.Token));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(destination)!));
    }

    [Fact]
    public void An_entry_whose_size_is_not_the_manifests_is_refused_even_with_the_right_hash()
    {
        string archive = Rewrite(Backup(_source), (name, bytes) =>
        {
            if (name != BackupManifest.ManifestEntry)
            {
                return bytes;
            }

            var json = System.Text.Json.Nodes.JsonNode.Parse(bytes)!;
            json["entries"]![0]!["bytes"] = json["entries"]![0]!["bytes"]!.GetValue<long>() + 1;
            return Encoding.UTF8.GetBytes(json.ToJsonString());
        });

        AssertRefused(archive, BackupProblem.Corrupted);
    }

    [Fact]
    public void A_database_that_is_not_intact_is_refused_even_when_its_hashes_match()
    {
        // A damaged database written into a backup with hashes that match it: only the database's own check finds it.
        byte[] damaged = [.. Encoding.ASCII.GetBytes("SQLite format 3\0"), .. Enumerable.Repeat((byte)0x5A, 8192)];
        string archive = Rewrite(Backup(_source), (name, bytes) => name switch
        {
            BackupManifest.DatabaseEntry => damaged,
            BackupManifest.ManifestEntry => Rehash(bytes, damaged),
            _ => bytes,
        });

        AssertRefused(archive, BackupProblem.Corrupted);

        static byte[] Rehash(byte[] manifest, byte[] database)
        {
            var json = System.Text.Json.Nodes.JsonNode.Parse(manifest)!;
            json["entries"]![0]!["bytes"] = database.LongLength;
            json["entries"]![0]!["sha256"] = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(database));
            return Encoding.UTF8.GetBytes(json.ToJsonString());
        }
    }

    [Fact]
    public void A_cancelled_read_stages_nothing()
    {
        Seed(_source);
        string archive = Backup(_source);

        Assert.Throws<OperationCanceledException>(() => PendingRestore.Stage(_target.Paths, archive, cancellation: new CancellationToken(true)));
        Assert.False(Directory.Exists(PendingRestore.StagingDirectory(_target.Paths)));
        Assert.Equal(RestoreOutcome.None, PendingRestore.ApplyIfPending(_target.Paths, Workspace.Time));
    }

    [Fact]
    public void A_restore_interrupted_part_way_puts_the_workspace_back()
    {
        Seed(_source);
        string archive = Backup(_source);
        _target.Note(_target.Folder("Mine"), "the target's own note");
        _target.Image(0x11);
        string before = _target.Dump();

        (BackupManifest? manifest, _) = PendingRestore.Stage(_target.Paths, archive);
        PendingRestore.Commit(_target.Paths, manifest!);
        _target.Close();

        // The staged images held open: the database has already moved aside when their move fails.
        string stagedImage = Directory.EnumerateFiles(Path.Join(PendingRestore.StagingDirectory(_target.Paths), "attachments")).First();
        RestoreOutcome outcome;
        using (new FileStream(stagedImage, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            outcome = PendingRestore.ApplyIfPending(_target.Paths, Workspace.Time);
        }

        _target.Reopen();
        Assert.Equal(RestoreOutcome.RolledBack, outcome);
        Assert.Equal(before, _target.Dump());
        Assert.False(File.Exists(Path.Join(_target.Root, PendingRestore.MarkerName)));
        Assert.Equal(RestoreOutcome.None, PendingRestore.ApplyIfPending(_target.Paths, Workspace.Time));
    }

    [Fact]
    public void A_staged_backup_without_its_marker_is_abandoned_not_applied()
    {
        string archive = Backup(_source);
        _target.Note(_target.Folder("Mine"), "kept");
        string before = _target.Dump();

        PendingRestore.Stage(_target.Paths, archive);
        _target.Close();

        Assert.Equal(RestoreOutcome.None, PendingRestore.ApplyIfPending(_target.Paths, Workspace.Time));
        _target.Reopen();
        Assert.Equal(before, _target.Dump());
        Assert.False(Directory.Exists(PendingRestore.StagingDirectory(_target.Paths)));
    }

    [Fact]
    public void A_discarded_restore_changes_nothing()
    {
        string archive = Backup(_source);
        _target.Note(_target.Folder("Mine"), "kept");
        string before = _target.Dump();

        (BackupManifest? manifest, _) = PendingRestore.Stage(_target.Paths, archive);
        PendingRestore.Commit(_target.Paths, manifest!);
        PendingRestore.Discard(_target.Paths);
        _target.Close();

        Assert.Equal(RestoreOutcome.None, PendingRestore.ApplyIfPending(_target.Paths, Workspace.Time));
        _target.Reopen();
        Assert.Equal(before, _target.Dump());
    }

    /// <summary>A workspace with something of every kind; the reference of its image.</summary>
    internal static string Seed(Workspace workspace)
    {
        string image = workspace.Image(0xAB);
        var work = workspace.Folder("Work", order: 2, pinned: true);
        var home = workspace.Folder("Hôme ✓", order: 1, collapsed: true);
        workspace.Folder("Gone", order: 3, deleted: true);
        workspace.Folder("Empty", order: 4);

        workspace.Note(work, "# Weekly sync\r\n\r\n- [ ] Tom\r\n- [x] room\r\n", order: 2, pinned: true);
        workspace.Note(work, $"Trip\n![harbour]({image})\nÜberraschung 🎉", order: 1, folded: true);
        workspace.Note(work, "binned", order: 3, deleted: true);
        workspace.Note(home, "日本語のノート\nwith a second line");
        workspace.Note(home, new string('x', 2_000_000) + "\nlarge", order: 2);
        workspace.Note(home, string.Empty, order: 3);
        workspace.Note(home, "\n\n  starts after blank lines\nends with spaces and lines   \n\n", order: 4);
        workspace.Setting("appearance.theme", "Dark");
        workspace.Setting("workspace.edge", "Left");
        return image;
    }

    private static string Backup(Workspace workspace)
    {
        string destination = Path.Join(workspace.Scratch("backups"), Guid.NewGuid().ToString("N") + BackupManifest.Extension);
        BackupWriter.Create(workspace.Database, workspace.Paths.AttachmentsDirectory, destination, "test", Workspace.Time);
        return destination;
    }

    /// <summary>Stages, commits and applies at the next start, as the app does across its restart.</summary>
    private static RestoreOutcome Restore(Workspace workspace, string archive)
    {
        (BackupManifest? manifest, BackupProblem? problem) = PendingRestore.Stage(workspace.Paths, archive);
        Assert.True(manifest is not null, $"refused: {problem}");
        PendingRestore.Commit(workspace.Paths, manifest!);
        workspace.Close();
        RestoreOutcome outcome = PendingRestore.ApplyIfPending(workspace.Paths, Workspace.Time);
        workspace.Reopen();
        return outcome;
    }

    private void AssertRefused(string archive, BackupProblem expected)
    {
        _target.Note(_target.Folder("Mine"), "kept");
        string before = _target.Dump();

        (BackupManifest? manifest, BackupProblem? problem) = PendingRestore.Stage(_target.Paths, archive);

        Assert.Null(manifest);
        Assert.Equal(expected, problem);
        Assert.False(Directory.Exists(PendingRestore.StagingDirectory(_target.Paths)));
        Assert.False(File.Exists(Path.Join(_target.Root, PendingRestore.MarkerName)));
        Assert.Equal(before, _target.Dump());
    }

    /// <summary>A copy of <paramref name="archive"/> with each entry as <paramref name="change"/> returns it (null drops it), and <paramref name="extra"/> added.</summary>
    private string Rewrite(string archive, Func<string, byte[], byte[]?> change, params (string Name, byte[] Bytes)[] extra)
    {
        string result = Path.Join(_source.Scratch("tampered"), Guid.NewGuid().ToString("N") + BackupManifest.Extension);
        using (ZipArchive input = ZipFile.OpenRead(archive))
        using (ZipArchive output = ZipFile.Open(result, ZipArchiveMode.Create))
        {
            foreach (ZipArchiveEntry entry in input.Entries)
            {
                using var bytes = new MemoryStream();
                entry.Open().CopyTo(bytes);
                if (change(entry.FullName, bytes.ToArray()) is { } changed)
                {
                    using Stream stream = output.CreateEntry(entry.FullName).Open();
                    stream.Write(changed);
                }
            }

            foreach ((string name, byte[] bytes) in extra)
            {
                using Stream stream = output.CreateEntry(name).Open();
                stream.Write(bytes);
            }
        }

        return result;
    }

    private sealed class Progress(Action<PortabilityProgress> report) : IProgress<PortabilityProgress>
    {
        public void Report(PortabilityProgress value) => report(value);
    }

    private static byte[] Flip(byte[] bytes, int at)
    {
        byte[] copy = [.. bytes];
        copy[at] ^= 0xFF;
        return copy;
    }
}
