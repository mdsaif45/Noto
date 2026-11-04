using Microsoft.Data.Sqlite;
using Noto.Core.Portability;
using Noto.Core.Workspace;
using Noto.Infrastructure.Portability;
using Noto.Infrastructure.Storage;
using Noto.Infrastructure.Tests.Portability;
using Noto.UseCases.Workspace;
using Xunit;

using PortabilityWorkspace = Noto.Infrastructure.Tests.Portability.Workspace;

namespace Noto.Infrastructure.Tests.Workspace;

/// <summary>
/// The welcome's first-start rule across real starts of a real data root (ADR-021): each start applies a pending
/// restore, opens and initializes the database, loads settings and asks <see cref="Onboarding"/>, in the order the
/// application does.
/// </summary>
public sealed class WelcomeLifecycleTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("noto-welcome-").FullName;
    private readonly NotoStoragePaths _paths;
    private readonly List<IDisposable> _others = [];
    private NotoDatabase? _database;

    public WelcomeLifecycleTests()
    {
        _paths = NotoStoragePaths.At(_root);
        _paths.EnsureCreated();
    }

    [Fact]
    public void Initialize_says_the_database_is_new_only_when_it_created_it()
    {
        var database = new NotoDatabase(_paths.DatabaseFile);

        Assert.True(database.Initialize());
        Assert.False(database.Initialize());
        Assert.False(new NotoDatabase(_paths.DatabaseFile).Initialize());
        database.Release();
    }

    [Fact]
    public void A_fresh_workspace_opens_with_the_welcome()
    {
        (bool show, Onboarding onboarding) = Start();

        Assert.True(show);
        Assert.Equal(WelcomeState.Due, onboarding.State);
    }

    [Fact]
    public void A_completed_welcome_does_not_open_again()
    {
        (_, Onboarding onboarding) = Start();
        Assert.True(onboarding.Complete());
        Stop();

        (bool show, onboarding) = Start();
        Assert.False(show);
        Assert.Equal(WelcomeState.Completed, onboarding.State);
    }

    [Fact]
    public void A_skipped_welcome_does_not_interrupt_later_starts()
    {
        (_, Onboarding onboarding) = Start();
        Assert.True(onboarding.Skip());
        Stop();

        for (int start = 0; start < 3; start++)
        {
            Assert.False(Start().Show);
            Stop();
        }
    }

    [Fact]
    public void A_start_that_stopped_with_the_welcome_open_shows_it_again()
    {
        // Ended, or crashed, before Finish or Skip: nothing recorded an end.
        Assert.True(Start().Show);
        Stop();

        (bool show, Onboarding onboarding) = Start();
        Assert.True(show);
        Assert.Equal(WelcomeState.Due, onboarding.State);
    }

    [Fact]
    public void A_workspace_upgraded_from_a_noto_without_the_welcome_never_shows_it_even_when_empty()
    {
        // A database an earlier Noto made: an older schema, no welcome row, and nothing in it.
        using (var connection = new SqliteConnection($"Data Source={_paths.DatabaseFile};Pooling=False"))
        {
            connection.Open();
            _ = new MigrationRunner(MigrationRunner.Migrations.Where(m => m.Version <= 2).ToArray()).Run(connection);
        }

        (bool show, Onboarding onboarding) = Start();

        Assert.False(show);
        Assert.Equal(WelcomeState.None, onboarding.State);
    }

    [Fact]
    public void An_existing_workspace_with_notes_never_shows_it()
    {
        Assert.True(Start().Show);
        Stop();
        using (var connection = new SqliteConnection($"Data Source={_paths.DatabaseFile};Pooling=False"))
        {
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "INSERT INTO Folders (Id, Name, SortOrder, IsPinned, IsCollapsed, CreatedAt, UpdatedAt) VALUES ('01J00000000000000000000F01', 'Mine', 1, 0, 0, '2026-10-01T08:00:00.0000000+00:00', '2026-10-01T08:00:00.0000000+00:00');";
            _ = command.ExecuteNonQuery();
        }

        // Owed, but no longer empty (Import from Settings while the welcome was open, then a crash): not new any more.
        (bool show, Onboarding onboarding) = Start();
        Assert.False(show);
        Assert.Equal(WelcomeState.None, onboarding.State);
        Stop();

        // Emptied, it still never shows it.
        using (var connection = new SqliteConnection($"Data Source={_paths.DatabaseFile};Pooling=False"))
        {
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "DELETE FROM Folders;";
            _ = command.ExecuteNonQuery();
        }

        Assert.False(Start().Show);
    }

    [Theory]
    [InlineData("INSERT INTO Folders (Id, Name, SortOrder, IsPinned, IsCollapsed, CreatedAt, UpdatedAt, DeletedAt) VALUES ('01J00000000000000000000F02', 'Binned', 1, 0, 0, '2026-10-01T08:00:00.0000000+00:00', '2026-10-01T08:00:00.0000000+00:00', '2026-10-02T08:00:00.0000000+00:00');")]
    [InlineData("INSERT INTO Notes (Id, FolderId, Content, SortOrder, IsPinned, IsFolded, CreatedAt, UpdatedAt, DeletedAt) VALUES ('01J00000000000000000000N02', NULL, 'an unfiled note, binned', 1, 0, 0, '2026-10-01T08:00:00.0000000+00:00', '2026-10-01T08:00:00.0000000+00:00', '2026-10-02T08:00:00.0000000+00:00');")]
    public void What_is_only_in_the_Recycle_Bin_is_the_users_too(string row)
    {
        Assert.True(Start().Show);
        Stop();
        using (var connection = new SqliteConnection($"Data Source={_paths.DatabaseFile};Pooling=False"))
        {
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = row;
            _ = command.ExecuteNonQuery();
        }

        // Owed, but holding a binned folder, or a binned note no folder holds: nothing else would say so.
        Assert.False(Start().Show);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_restored_workspace_never_shows_it(bool sourceHoldsNotes)
    {
        // The backup: a workspace of notes, or an empty one whose own welcome was still open when it was taken.
        var source = new PortabilityWorkspace();
        _others.Add(source);
        if (sourceHoldsNotes)
        {
            _ = source.Note(source.Folder("Kept"), "a note");
        }
        else
        {
            source.Setting("onboarding.welcome", "Due");
        }

        string archive = Path.Join(source.Scratch("backups"), "source" + BackupManifest.Extension);
        BackupWriter.Create(source.Database, source.Paths.AttachmentsDirectory, archive, "test", PortabilityWorkspace.Time);

        // A new workspace, its welcome open, restores from Settings: staged, committed, applied at the next start.
        Assert.True(Start().Show);
        (BackupManifest? manifest, BackupProblem? problem) = PendingRestore.Stage(_paths, archive);
        Assert.True(manifest is not null, $"refused: {problem}");
        PendingRestore.Commit(_paths, manifest!);
        Stop();

        Assert.False(Start().Show);
        Stop();
        (bool show, Onboarding onboarding) = Start();
        Assert.False(show);
        Assert.NotEqual(WelcomeState.Due, onboarding.State);
    }

    public void Dispose()
    {
        Stop();
        foreach (IDisposable other in _others)
        {
            other.Dispose();
        }

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A file still held by the system (an indexer, a scanner) is left in the temporary folder Windows clears;
            // the test's result does not depend on it.
        }
    }

    /// <summary>One start, as App.OnLaunched orders it.</summary>
    private (bool Show, Onboarding Onboarding) Start()
    {
        RestoreOutcome restored = PendingRestore.ApplyIfPending(_paths, PortabilityWorkspace.Time);
        _database = new NotoDatabase(_paths.DatabaseFile);
        bool created = _database.Initialize();
        var settings = new SqliteSettingsStore(_database);
        settings.Load();

        var onboarding = new Onboarding(settings);
        var folders = new SqliteFolderRepository(_database);
        var notes = new SqliteNoteRepository(_database);
        bool show = onboarding.ShowAtStart(created, restored == RestoreOutcome.Applied, () => Onboarding.HoldsAnything(folders, notes));
        return (show, onboarding);
    }

    /// <summary>The end of a start: the database released, so its files can move (a restore applies at the next).</summary>
    private void Stop()
    {
        _database?.Release();
        _database = null;
        SqliteConnection.ClearAllPools();
    }
}
