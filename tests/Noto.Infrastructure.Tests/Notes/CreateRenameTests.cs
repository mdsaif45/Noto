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
using Noto.UseCases.Workspace;
using Xunit;

namespace Noto.Infrastructure.Tests.Notes;

/// <summary>
/// P1.2 against real SQLite (create-and-rename design gate): folder names normalised on the way in, duplicates and
/// legacy names left alone, new-note placement, notes renamed by their first line, and events only after commit.
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

    // ---- notes: placement (B5, J8) -------------------------------------------------

    [Fact]
    public void Bottom_is_the_default_and_keeps_the_old_positions_exactly()
    {
        FolderId folder = CreateFolder("F").Value;

        NoteId a = CreateNote(folder, "a").Value;
        NoteId b = CreateNote(folder, "b", NewNotePlacement.Bottom).Value;

        Assert.Equal(0d, _notes.FindActive(a)!.SortOrder);
        Assert.Equal(1d, _notes.FindActive(b)!.SortOrder);
        Assert.Equal([a, b], Listed(folder));
        Assert.Equal(NewNotePlacement.Bottom, Preferences().NewNotePlacement);
    }

    [Fact]
    public void Top_places_the_note_first_among_the_unpinned()
    {
        FolderId folder = CreateFolder("F").Value;
        NoteId pinned = CreateNote(folder, "pinned").Value;
        Assert.True(new PinNoteHandler(_notes, _clock).Handle(new PinNote(pinned)).IsSuccess);
        NoteId a = CreateNote(folder, "a").Value;

        NoteId top = CreateNote(folder, "top", NewNotePlacement.Top).Value;

        Assert.Equal([pinned, top, a], Listed(folder));
    }

    [Fact]
    public void Above_and_below_the_selected_note()
    {
        FolderId folder = CreateFolder("F").Value;
        NoteId a = CreateNote(folder, "a").Value;
        NoteId b = CreateNote(folder, "b").Value;
        NoteId c = CreateNote(folder, "c").Value;

        NoteId below = CreateNote(folder, "below b", NewNotePlacement.BelowSelected, b).Value;
        NoteId above = CreateNote(folder, "above b", NewNotePlacement.AboveSelected, b).Value;

        Assert.Equal([a, above, b, below, c], Listed(folder));
    }

    [Fact]
    public void A_selection_that_is_gone_or_elsewhere_places_the_note_at_the_end()
    {
        FolderId folder = CreateFolder("F").Value;
        FolderId other = CreateFolder("Other").Value;
        NoteId a = CreateNote(folder, "a").Value;
        NoteId gone = CreateNote(folder, "gone").Value;
        Assert.True(new DeleteNoteHandler(_notes, _clock).Handle(new DeleteNote(gone)).IsSuccess);
        NoteId elsewhere = CreateNote(other, "elsewhere").Value;

        NoteId afterGone = CreateNote(folder, "x", NewNotePlacement.BelowSelected, gone).Value;
        NoteId afterElsewhere = CreateNote(folder, "y", NewNotePlacement.AboveSelected, elsewhere).Value;

        Assert.Equal([a, afterGone, afterElsewhere], Listed(folder));
    }

    [Fact]
    public void The_repository_itself_never_anchors_a_note_on_a_sibling_from_another_folder()
    {
        FolderId folder = CreateFolder("F").Value;
        FolderId other = CreateFolder("Other").Value;
        NoteId a = CreateNote(folder, "a").Value;
        NoteId b = CreateNote(folder, "b").Value;
        NoteId foreign = CreateNote(other, "foreign").Value;
        var note = NewNote(folder);

        // The foreign note sits at the same SortOrder as "a" in its own folder, so anchoring on it would land the new
        // note between "a" and "b"; refusing it lands the note at the end.
        Assert.Equal(_notes.FindActive(a)!.SortOrder, _notes.FindActive(foreign)!.SortOrder);

        _notes.AddAt(note, NotePlacement.After(foreign));

        Assert.Equal([a, b, note.Id], Listed(folder));
    }

    [Fact]
    public void Placing_many_notes_at_the_same_spot_keeps_every_one_in_order()
    {
        FolderId folder = CreateFolder("F").Value;
        NoteId first = CreateNote(folder, "first").Value;
        NoteId last = CreateNote(folder, "last").Value;

        var inserted = new List<NoteId>();
        for (int i = 0; i < 70; i++)
        {
            // Each new note goes just below "first", so each lands above the previous one (O6 renormalises on the way).
            inserted.Insert(0, CreateNote(folder, $"n{i}", NewNotePlacement.BelowSelected, first).Value);
        }

        Assert.Equal([first, .. inserted, last], Listed(folder));
    }

    [Fact]
    public void A_placed_create_that_the_database_refuses_writes_nothing()
    {
        FolderId folder = CreateFolder("F").Value;
        NoteId a = CreateNote(folder, "a").Value;
        string before = Snapshot();
        Sql("CREATE TRIGGER inject BEFORE INSERT ON Notes BEGIN SELECT RAISE(ABORT, 'injected'); END;");

        Assert.Throws<StorageException>(() => CreateNote(folder, "x", NewNotePlacement.BelowSelected, a));

        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void A_stored_placement_that_is_not_a_value_reads_as_Bottom()
    {
        Sql("INSERT INTO Settings (Key, Value) VALUES ('notes.new-placement', 'Sideways');");

        Assert.Equal(NewNotePlacement.Bottom, Preferences().NewNotePlacement);
    }

    [Fact]
    public void Every_placement_keeps_every_active_note_in_a_live_folder()
    {
        FolderId folder = CreateFolder("F").Value;
        NoteId a = CreateNote(folder, "a").Value;
        foreach (NewNotePlacement placement in Enum.GetValues<NewNotePlacement>())
        {
            Assert.True(CreateNote(folder, placement.ToString(), placement, a).IsSuccess);
        }

        Assert.Equal(0L, Count("SELECT COUNT(*) FROM Notes n WHERE n.DeletedAt IS NULL AND NOT EXISTS (SELECT 1 FROM Folders f WHERE f.Id = n.FolderId AND f.DeletedAt IS NULL);"));
    }

    // ---- notes: renamed by their first line (B16) --------------------------------------

    [Fact]
    public void A_note_is_renamed_by_editing_its_first_line_and_nothing_else_moves()
    {
        FolderId folder = CreateFolder("F").Value;
        NoteId id = CreateNote(folder, "# Old title\n\nbody stays").Value;
        double sortOrder = _notes.FindActive(id)!.SortOrder;

        Assert.True(new UpdateNoteContentHandler(_notes, _clock).Handle(new UpdateNoteContent(id, "# New title\n\nbody stays")).IsSuccess);

        Note renamed = _notes.FindActive(id)!;
        Assert.Equal("New title", renamed.Title);
        Assert.Equal("# New title\n\nbody stays", renamed.Content);
        Assert.Equal(sortOrder, renamed.SortOrder);
        Assert.Equal(folder, renamed.FolderId);
        Assert.DoesNotContain("Title", Columns("Notes"));
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
                    static (command, _) => new FolderRenamed(command.FolderId))
                .Register<CreateNote, CommandResult<NoteId>, NoteCreated>(
                    new CreateNoteHandler(_notes, _clock).Handle,
                    static (command, result) => new NoteCreated(result.Value, command.FolderId)),
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
        using var c = events.Subscribe<NoteCreated>(e =>
        {
            Assert.NotNull(_notes.FindActive(e.NoteId));
            told.Add(e);
        });

        FolderId folder = dispatcher.Dispatch(new CreateFolder("  Inbox ")).Value;
        Assert.True(dispatcher.Dispatch(new RenameFolder(folder, "Archive")).IsSuccess);
        NoteId first = dispatcher.Dispatch(new CreateNote(folder, "a")).Value;
        NoteId top = dispatcher.Dispatch(new CreateNote(folder, "t", NewNotePlacement.Top)).Value;
        Assert.False(dispatcher.Dispatch(new CreateFolder(" ")).IsSuccess);
        Assert.False(dispatcher.Dispatch(new RenameFolder(folder, "\t")).IsSuccess);
        Assert.False(dispatcher.Dispatch(new RenameFolder(FolderId.New(), "x")).IsSuccess);

        Assert.Equal(
            [new FolderCreated(folder), new FolderRenamed(folder), new NoteCreated(first, folder), new NoteCreated(top, folder)],
            told);
        Assert.Empty(failures);
    }

    // ---- helpers ---------------------------------------------------------------------------

    private CommandResult<FolderId> CreateFolder(string name) =>
        new CreateFolderHandler(_folders, _clock).Handle(new CreateFolder(name));

    private CommandResult Rename(FolderId id, string name) =>
        new RenameFolderHandler(_folders, _clock).Handle(new RenameFolder(id, name));

    private CommandResult<NoteId> CreateNote(FolderId folder, string content, NewNotePlacement placement = NewNotePlacement.Bottom, NoteId? selected = null)
    {
        _clock.Advance(TimeSpan.FromSeconds(1));
        return new CreateNoteHandler(_notes, _clock).Handle(new CreateNote(folder, content, placement, selected));
    }

    private Note NewNote(FolderId folder) => new()
    {
        Id = NoteId.New(),
        Content = "direct",
        FolderId = folder,
        CreatedAt = _clock.UtcNow,
        UpdatedAt = _clock.UtcNow,
    };

    private WorkspacePreferences Preferences()
    {
        var settings = new SqliteSettingsStore(_database, NullStorageLog.Instance);
        settings.Load();
        return new WorkspacePreferences(settings);
    }

    private List<NoteId> Listed(FolderId folder) => [.. _notes.ListInFolder(folder).Select(n => n.Id)];

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

    private List<string> Columns(string table)
    {
        using SqliteConnection connection = _database.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT name FROM pragma_table_info('{table}');";
        using SqliteDataReader reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
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

    private long Count(string sql) => (long)Scalar(sql)!;

    private sealed class Clock(DateTimeOffset start) : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = start;

        public void Advance(TimeSpan by) => UtcNow += by;
    }
}
