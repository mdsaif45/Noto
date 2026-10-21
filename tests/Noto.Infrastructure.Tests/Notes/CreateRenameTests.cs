using Microsoft.Data.Sqlite;
using Noto.Core;
using Noto.Core.Commands;
using Noto.Core.Diagnostics;
using Noto.Core.Folders;
using Noto.Core.Notes;
using Noto.Core.Settings;
using Noto.Core.Storage;
using Noto.Infrastructure.Storage;
using Noto.Infrastructure.Tests.Storage;
using Noto.UseCases.Commands;
using Noto.UseCases.Folders;
using Noto.UseCases.Notes;
using Xunit;

namespace Noto.Infrastructure.Tests.Notes;

/// <summary>
/// P1.2 against real SQLite (create-and-rename design gate): folder names normalised on the way in, duplicates and
/// legacy names left alone, and events only after commit.
/// </summary>
public sealed class CreateRenameTests : IDisposable
{
    private readonly TempDatabase _temp = new();
    private readonly NotoDatabase _database;
    private readonly SqliteNoteRepository _notes;
    private readonly SqliteFolderRepository _folders;
    private readonly Clock _clock = new(new DateTimeOffset(2026, 10, 21, 9, 0, 0, TimeSpan.Zero));

    public CreateRenameTests()
    {
        _database = new NotoDatabase(_temp.DatabasePath);
        _database.Initialize();
        _notes = new SqliteNoteRepository(_database);
        _folders = new SqliteFolderRepository(_database);
    }

    public void Dispose() => _temp.Dispose();

    // ---- folder names ---------------------------------------------------------

    [Fact]
    public void A_created_folder_is_stored_under_its_normalised_name()
    {
        FolderId id = CreateFolder("  Work\tlog\r\n2026  ").Value;

        Assert.Equal("Work log 2026", NameOf(id));
    }

    [Fact]
    public void A_renamed_folder_is_stored_under_its_normalised_name()
    {
        FolderId id = CreateFolder("Old").Value;

        Assert.True(Rename(id, "\tNew\nname  ").IsSuccess);

        Assert.Equal("New name", NameOf(id));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public void A_name_with_nothing_left_is_refused_and_nothing_is_written(string name)
    {
        FolderId existing = CreateFolder("Keep").Value;
        string before = Snapshot();

        Assert.Equal(CommandFailureReason.InvalidInput, CreateFolder(name).Failure!.Reason);
        Assert.Equal(CommandFailureReason.InvalidInput, Rename(existing, name).Failure!.Reason);

        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void Duplicate_names_are_allowed_even_after_normalising()
    {
        FolderId a = CreateFolder("Work").Value;
        CommandResult<FolderId> b = CreateFolder("  Work ");
        FolderId c = CreateFolder("Other").Value;

        Assert.True(b.IsSuccess);
        Assert.True(Rename(c, "Work").IsSuccess);
        Assert.Equal(3, _folders.ListActive().Count(f => f.Name == "Work"));
        Assert.NotEqual(a, b.Value);
    }

    [Fact]
    public void A_name_stored_before_normalisation_is_left_exactly_as_it_was_until_renamed()
    {
        // No migration rewrites user data for cosmetics.
        FolderId legacy = FolderId.New();
        Sql("INSERT INTO Folders (Id, Name, SortOrder, CreatedAt, UpdatedAt) VALUES ($p, '  Legacy\tname  ', 0, '2026-01-01T00:00:00.0000000+00:00', '2026-01-01T00:00:00.0000000+00:00');", legacy.Value);
        NoteId note = CreateNote(legacy, "n").Value;
        Assert.True(new PinFolderHandler(_folders, _clock).Handle(new PinFolder(legacy)).IsSuccess);

        Assert.Equal("  Legacy\tname  ", NameOf(legacy));
        Assert.NotNull(_notes.FindActive(note));

        Assert.True(Rename(legacy, "  Legacy\tname  ").IsSuccess);
        Assert.Equal("Legacy name", NameOf(legacy));
    }

    [Fact]
    public void A_rename_changes_the_name_and_UpdatedAt_only()
    {
        FolderId id = CreateFolder("Before").Value;
        Folder before = _folders.FindActive(id)!;
        _clock.Advance(TimeSpan.FromMinutes(5));

        Assert.True(Rename(id, "After").IsSuccess);

        Folder after = _folders.FindActive(id)!;
        Assert.Equal("After", after.Name);
        Assert.Equal(_clock.UtcNow, after.UpdatedAt);
        Assert.Equal(before.CreatedAt, after.CreatedAt);
        Assert.Equal(before.SortOrder, after.SortOrder);
        Assert.Equal(before.IsPinned, after.IsPinned);
    }

    [Fact]
    public void A_rename_that_the_database_refuses_leaves_the_old_name()
    {
        FolderId id = CreateFolder("Before").Value;
        Sql("CREATE TRIGGER inject BEFORE UPDATE OF Name ON Folders BEGIN SELECT RAISE(ABORT, 'injected'); END;");

        Assert.Throws<StorageException>(() => Rename(id, "After"));

        Assert.Equal("Before", NameOf(id));
    }

    // ---- events (ADR-010): after commit only, never on a refusal ------------------------------

    [Fact]
    public void Creating_and_renaming_a_folder_are_announced_after_the_write_and_refusals_are_not()
    {
        var failures = new List<ExceptionSummary>();
        var events = new EventHub(failures.Add);
        var dispatcher = new CommandDispatcher(
            new CommandRegistry()
                .Register<CreateFolder, CommandResult<FolderId>, FolderCreated>(
                    new CreateFolderHandler(_folders, _clock).Handle,
                    static (_, result) => new FolderCreated(result.Value))
                .Register<RenameFolder, CommandResult, FolderRenamed>(
                    new RenameFolderHandler(_folders, _clock).Handle,
                    static (command, _) => new FolderRenamed(command.FolderId)),
            events);
        var told = new List<object>();
        using var a = events.Subscribe<FolderCreated>(e =>
        {
            Assert.Equal("Inbox", _folders.FindActive(e.FolderId)!.Name);
            told.Add(e);
        });
        using var b = events.Subscribe<FolderRenamed>(e =>
        {
            Assert.Equal("Archive", _folders.FindActive(e.FolderId)!.Name);
            told.Add(e);
        });

        FolderId folder = dispatcher.Dispatch(new CreateFolder("  Inbox ")).Value;
        Assert.True(dispatcher.Dispatch(new RenameFolder(folder, "Archive")).IsSuccess);
        Assert.False(dispatcher.Dispatch(new CreateFolder(" ")).IsSuccess);
        Assert.False(dispatcher.Dispatch(new RenameFolder(folder, "\t")).IsSuccess);
        Assert.False(dispatcher.Dispatch(new RenameFolder(FolderId.New(), "x")).IsSuccess);

        Assert.Equal(
            [new FolderCreated(folder), new FolderRenamed(folder)],
            told);
        Assert.Empty(failures);
    }

    // ---- helpers ---------------------------------------------------------------------------

    private CommandResult<FolderId> CreateFolder(string name) =>
        new CreateFolderHandler(_folders, _clock).Handle(new CreateFolder(name));

    private CommandResult Rename(FolderId id, string name) =>
        new RenameFolderHandler(_folders, _clock).Handle(new RenameFolder(id, name));

    private CommandResult<NoteId> CreateNote(FolderId folder, string content)
    {
        _clock.Advance(TimeSpan.FromSeconds(1));
        return new CreateNoteHandler(_notes, _clock).Handle(new CreateNote(folder, content));
    }

    private string? NameOf(FolderId id) => Scalar("SELECT Name FROM Folders WHERE Id = $p;", id.Value) as string;

    private string Snapshot()
    {
        using SqliteConnection connection = _database.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT 'F|' || Id || '|' || Name || '|' || SortOrder || '|' || UpdatedAt FROM Folders
            UNION ALL
            SELECT 'N|' || Id || '|' || FolderId || '|' || SortOrder || '|' || UpdatedAt || '|' || Content FROM Notes
            ORDER BY 1;
            """;
        using SqliteDataReader reader = command.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read())
        {
            rows.Add(reader.GetString(0));
        }

        return string.Join("\n", rows);
    }

    private void Sql(string sql, string? p = null)
    {
        using SqliteConnection connection = _database.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        if (p is not null)
        {
            command.Parameters.AddWithValue("$p", p);
        }

        command.ExecuteNonQuery();
    }

    private object? Scalar(string sql, string? p = null)
    {
        using SqliteConnection connection = _database.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        if (p is not null)
        {
            command.Parameters.AddWithValue("$p", p);
        }

        return command.ExecuteScalar();
    }

    private sealed class Clock(DateTimeOffset start) : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = start;

        public void Advance(TimeSpan by) => UtcNow += by;
    }
}
