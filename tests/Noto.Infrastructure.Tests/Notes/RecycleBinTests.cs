using Noto.Core;
using Noto.Core.Commands;
using Noto.Core.Folders;
using Noto.Core.Notes;
using Noto.Infrastructure.Storage;
using Noto.Infrastructure.Tests.Storage;
using Noto.UseCases.Notes;
using Xunit;

namespace Noto.Infrastructure.Tests.Notes;

/// <summary>
/// The explicit recycle-bin queries (contract §11 Q6–Q7, invariants I1/I2).
/// </summary>
/// <remarks>
/// I2's rule in one sentence: deleted entities are returned <b>only</b> by a
/// method whose name says so. These tests exist to keep that true in both
/// directions — the bin shows nothing active, and the active queries show
/// nothing binned.
/// </remarks>
public sealed class RecycleBinTests : IDisposable
{
    private readonly TempDatabase _temp = new();
    private readonly NotoDatabase _database;
    private readonly SqliteNoteRepository _notes;
    private readonly SqliteFolderRepository _folders;
    private readonly MutableClock _clock = new(new DateTimeOffset(2026, 9, 15, 10, 30, 0, TimeSpan.Zero));

    public RecycleBinTests()
    {
        _database = new NotoDatabase(_temp.DatabasePath);
        _database.Initialize();
        _notes = new SqliteNoteRepository(_database);
        _folders = new SqliteFolderRepository(_database);
    }

    public void Dispose() => _temp.Dispose();

    private NoteId Create(string content, FolderId? folder = null) =>
        new CreateNoteHandler(_notes, _clock).Handle(new CreateNote(folder, content)).Value;

    private void Delete(NoteId id) =>
        new DeleteNoteHandler(_notes, _clock).Handle(new DeleteNote(id));

    private IReadOnlyList<Note> Bin() => new ListDeletedNotesQuery(_notes).Execute();

    private IReadOnlyList<Folder> FolderBin() => new ListDeletedFoldersQuery(_folders).Execute();

    // ---- ATTACK 4: the bin must contain ONLY deleted notes -----------------

    [Fact]
    public void The_bin_contains_exactly_the_deleted_notes()
    {
        // The plausible wrong implementation lists every note. A mixed dataset
        // is the only way to catch it — a bin test over deleted-only data
        // would pass either way.
        var a = Create("active a");
        var b = Create("deleted b");
        var c = Create("active c");
        var d = Create("deleted d");

        Delete(b);
        Delete(d);

        var binned = Bin().Select(n => n.Id).ToHashSet();

        Assert.Equal(2, binned.Count);
        Assert.Contains(b, binned);
        Assert.Contains(d, binned);
        Assert.DoesNotContain(a, binned);
        Assert.DoesNotContain(c, binned);
    }

    [Fact]
    public void An_empty_bin_is_an_empty_list_not_a_failure()
    {
        Create("active");

        Assert.Empty(Bin());
    }

    [Fact]
    public void Every_note_in_the_bin_is_marked_deleted()
    {
        Create("active");
        Delete(Create("gone"));

        Assert.All(Bin(), note =>
        {
            Assert.True(note.IsDeleted);
            Assert.NotNull(note.DeletedAt);
        });
    }

    [Fact]
    public void Restoring_removes_a_note_from_the_bin()
    {
        var id = Create("note");
        Delete(id);
        Assert.Single(Bin());

        new RestoreNoteHandler(_notes, _clock).Handle(new RestoreNote(id));

        Assert.Empty(Bin());
    }

    [Fact]
    public void The_bin_spans_every_scope()
    {
        // The bin is not scoped by folder: a user looking for a deleted note
        // should not have to remember which folder it was in.
        FolderId folder = InsertFolder("Work");
        var atRoot = Create("root");
        var inFolder = Create("filed", folder);

        Delete(atRoot);
        Delete(inFolder);

        var binned = Bin().Select(n => n.Id).ToHashSet();
        Assert.Contains(atRoot, binned);
        Assert.Contains(inFolder, binned);
    }

    [Fact]
    public void The_bin_returns_a_deterministic_order()
    {
        // The contract does not specify an order for the bin, so this asserts
        // only that the result is stable across calls — not a particular
        // sequence, which would invent an API guarantee.
        for (int i = 0; i < 5; i++)
        {
            var id = Create($"note {i}");
            _clock.Advance(TimeSpan.FromMinutes(1));
            Delete(id);
        }

        Assert.Equal(Bin().Select(n => n.Id), Bin().Select(n => n.Id));
    }

    // ---- I1: ordinary queries must exclude the bin -------------------------

    [Fact]
    public void Deleted_notes_are_absent_from_every_ordinary_read()
    {
        // I1's enforcement note asks for exactly this: "a test that asserts a
        // deleted note is absent from EVERY list method, so a new method that
        // forgets the filter fails".
        FolderId folder = InsertFolder("Work");
        var id = Create("gone", folder);
        Delete(id);

        Assert.Null(_notes.FindActive(id));
        Assert.False(new GetNoteQuery(_notes).Execute(id).IsSuccess);
        Assert.False(_notes.IsActiveSiblingIn(id, folder));
        Assert.Equal(NoteLifecycle.Deleted, _notes.GetLifecycle(id));
    }

    [Fact]
    public void A_deleted_note_does_not_distort_the_ordering_scope()
    {
        // I6: a deleted row keeps its SortOrder but must never participate in
        // ordering. If MaxSortOrder counted it, the next created note would be
        // pushed past a note nobody can see.
        var a = Create("a");
        var b = Create("b");
        Delete(b);

        double? max = _notes.MaxSortOrder(null);

        Assert.Equal(_notes.FindActive(a)!.SortOrder, max);

        // And the next note lands immediately after the last ACTIVE sibling.
        var c = Create("c");
        Assert.Equal(_notes.FindActive(a)!.SortOrder + 1, _notes.FindActive(c)!.SortOrder);
    }

    [Fact]
    public void A_deleted_note_cannot_be_a_reorder_target()
    {
        var a = Create("a");
        var b = Create("b");
        var c = Create("c");
        Delete(b);

        var result = new ReorderNoteHandler(_notes, _clock).Handle(new ReorderNote(c, b));

        Assert.False(result.IsSuccess);
        Assert.Equal(CommandFailureReason.InvalidInput, result.Failure!.Reason);
        Assert.NotNull(_notes.FindActive(a));
    }

    // ---- I5: deleted notes are inert to every Slice 2 mutation -------------

    [Fact]
    public void Every_mutating_command_rejects_a_deleted_note_with_InvalidState()
    {
        // I5 names the whole class: a deleted note "cannot be edited, moved,
        // reordered, pinned or tagged". Checked across the commands that exist
        // so far, so a future command that forgets the guard stands out.
        FolderId folder = InsertFolder("Work");
        var id = Create("note");
        var sibling = Create("sibling");
        Delete(id);

        var results = new (string Name, CommandResult Result)[]
        {
            ("UpdateNoteContent", new UpdateNoteContentHandler(_notes, _clock).Handle(new UpdateNoteContent(id, "edited"))),
            ("MoveNoteToFolder", new MoveNoteToFolderHandler(_notes, _clock).Handle(new MoveNoteToFolder(id, folder))),
            ("ReorderNote", new ReorderNoteHandler(_notes, _clock).Handle(new ReorderNote(id, sibling))),
            ("PinNote", new PinNoteHandler(_notes, _clock).Handle(new PinNote(id))),
            ("UnpinNote", new UnpinNoteHandler(_notes, _clock).Handle(new UnpinNote(id))),
            ("FoldNote", new FoldNoteHandler(_notes, _clock).Handle(new FoldNote(id))),
            ("UnfoldNote", new UnfoldNoteHandler(_notes, _clock).Handle(new UnfoldNote(id))),
            ("SetNoteColor", new SetNoteColorHandler(_notes, _clock).Handle(new SetNoteColor(id, "note1"))),
            ("DeleteNote", new DeleteNoteHandler(_notes, _clock).Handle(new DeleteNote(id))),
        };

        Assert.All(results, entry =>
        {
            Assert.False(entry.Result.IsSuccess, $"{entry.Name} accepted a deleted note");
            Assert.Equal(CommandFailureReason.InvalidState, entry.Result.Failure!.Reason);
        });
    }

    [Fact]
    public void A_rejected_mutation_leaves_the_binned_note_untouched()
    {
        var id = Create("original");
        Delete(id);
        Note before = Bin()[0];

        _clock.Advance(TimeSpan.FromHours(1));
        new UpdateNoteContentHandler(_notes, _clock).Handle(new UpdateNoteContent(id, "edited"));
        new PinNoteHandler(_notes, _clock).Handle(new PinNote(id));

        Note after = Bin()[0];
        Assert.Equal(before.Content, after.Content);
        Assert.Equal(before.IsPinned, after.IsPinned);
        Assert.Equal(before.UpdatedAt, after.UpdatedAt);
        Assert.Equal(before.DeletedAt, after.DeletedAt);
    }

    // ---- folders (Q7) --------------------------------------------------------

    [Fact]
    public void The_folder_bin_contains_exactly_the_deleted_folders()
    {
        // Folder deletion is Slice 4, so the deleted state is established
        // directly in the database rather than by broadening the domain
        // surface to make a query testable.
        FolderId active = InsertFolder("Active");
        FolderId binnedFolder = InsertFolder("Binned");
        SoftDeleteFolder(binnedFolder);

        var ids = FolderBin().Select(f => f.Id).ToHashSet();

        Assert.Single(ids);
        Assert.Contains(binnedFolder, ids);
        Assert.DoesNotContain(active, ids);
    }

    [Fact]
    public void An_empty_folder_bin_is_an_empty_list()
    {
        InsertFolder("Active");

        Assert.Empty(FolderBin());
    }

    [Fact]
    public void A_binned_folder_returns_its_full_state()
    {
        FolderId id = InsertFolder("Archive");
        SoftDeleteFolder(id);

        Folder folder = Assert.Single(FolderBin());
        Assert.Equal(id, folder.Id);
        Assert.Equal("Archive", folder.Name);
        Assert.True(folder.IsDeleted);
        Assert.NotNull(folder.DeletedAt);
    }

    [Fact]
    public void The_note_bin_and_the_folder_bin_are_separate()
    {
        // Contract §11 Q7: "notes and folders separate". A union query would
        // have to return one of them badly.
        FolderId folder = InsertFolder("Archive");
        SoftDeleteFolder(folder);
        Delete(Create("a note"));

        Assert.Single(Bin());
        Assert.Single(FolderBin());
    }

    // ---- helpers ------------------------------------------------------------

    private FolderId InsertFolder(string name)
    {
        var id = FolderId.New();

        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Folders (Id, Name, SortOrder, CreatedAt, UpdatedAt)
            VALUES ($id, $name, 0, $now, $now);
            """;
        command.Parameters.AddWithValue("$id", id.Value);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$now", _clock.UtcNow.ToString("O"));
        command.ExecuteNonQuery();

        return id;
    }

    private void SoftDeleteFolder(FolderId id)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Folders SET DeletedAt = $now WHERE Id = $id;";
        command.Parameters.AddWithValue("$now", _clock.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$id", id.Value);
        command.ExecuteNonQuery();
    }

    private sealed class MutableClock(DateTimeOffset start) : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = start;

        public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
    }
}
