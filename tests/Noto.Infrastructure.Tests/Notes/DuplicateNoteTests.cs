using Noto.Core.Commands;
using Noto.Core.Folders;
using Noto.Core.Notes;
using Noto.Core.Storage;
using Noto.Infrastructure.Storage;
using Noto.Infrastructure.Tests.Folders;
using Noto.Infrastructure.Tests.Storage;
using Noto.UseCases.Folders;
using Noto.UseCases.Notes;
using Xunit;

namespace Noto.Infrastructure.Tests.Notes;

/// <summary>
/// <c>DuplicateNote</c> against real SQLite (#232; parity B22): the copy keeps the text, colour and mode, is neither
/// pinned nor folded, and sits directly below the original, or at the top of the unpinned notes when the original is
/// pinned.
/// </summary>
public sealed class DuplicateNoteTests : IDisposable
{
    private readonly TempDatabase _temp = new();
    private readonly NotoDatabase _database;
    private readonly SqliteNoteRepository _notes;
    private readonly MutableClock _clock = new(new DateTimeOffset(2026, 11, 17, 9, 0, 0, TimeSpan.Zero));
    private readonly FolderId _home;

    public DuplicateNoteTests()
    {
        _database = new NotoDatabase(_temp.DatabasePath);
        _database.Initialize();
        _notes = new SqliteNoteRepository(_database);
        _home = new CreateFolderHandler(new SqliteFolderRepository(_database), _clock).Handle(new CreateFolder("Home")).Value;
    }

    public void Dispose() => _temp.Dispose();

    private NoteId Create(string content, NoteMode mode = NoteMode.Markdown) =>
        new CreateNoteHandler(_notes, _clock).Handle(new CreateNote(_home, content, Mode: mode)).Value;

    private CommandResult<NoteId> Duplicate(NoteId id) =>
        new DuplicateNoteHandler(_notes, _clock).Handle(new DuplicateNote(id));

    private IEnumerable<NoteId> Order() => _notes.ListInFolder(_home).Select(n => n.Id);

    [Fact]
    public void The_copy_keeps_text_colour_and_mode_and_is_a_new_unpinned_unfolded_note()
    {
        const string text = "SELECT 1;\n![shot](attachments/01JABCDEFGHJKMNPQRSTVWXYZ0.png)";
        NoteId original = Create(text, NoteMode.Code);
        Assert.True(new SetNoteColorHandler(_notes, _clock).Handle(new SetNoteColor(original, "note3")).IsSuccess);
        Assert.True(new FoldNoteHandler(_notes, _clock).Handle(new FoldNote(original)).IsSuccess);
        _clock.Advance(TimeSpan.FromHours(1));

        CommandResult<NoteId> result = Duplicate(original);

        Assert.True(result.IsSuccess);
        Note copy = _notes.FindActive(result.Value)!;
        Assert.NotEqual(original, copy.Id);
        Assert.Equal((text, "note3", NoteMode.Code, _home), (copy.Content, copy.ColorKey, copy.Mode, copy.FolderId!.Value));
        Assert.False(copy.IsPinned);
        Assert.False(copy.IsFolded);
        Assert.Equal((_clock.UtcNow, _clock.UtcNow), (copy.CreatedAt, copy.UpdatedAt));

        // The original is untouched.
        Note after = _notes.FindActive(original)!;
        Assert.Equal((text, "note3", true), (after.Content, after.ColorKey, after.IsFolded));
    }

    [Fact]
    public void The_copy_sits_directly_below_the_original()
    {
        NoteId a = Create("a");
        NoteId b = Create("b");
        NoteId c = Create("c");

        NoteId copy = Duplicate(a).Value;
        NoteId again = Duplicate(c).Value;

        Assert.Equal([a, copy, b, c, again], Order());
    }

    [Fact]
    public void A_pinned_notes_copy_goes_to_the_top_of_the_unpinned_notes()
    {
        NoteId a = Create("a");
        NoteId b = Create("b");
        NoteId c = Create("c");
        Assert.True(new PinNoteHandler(_notes, _clock).Handle(new PinNote(b)).IsSuccess);

        NoteId copy = Duplicate(b).Value;

        Assert.Equal([b, copy, a, c], Order());
        Assert.False(_notes.FindActive(copy)!.IsPinned);
    }

    [Fact]
    public void A_missing_note_is_NotFound_and_a_binned_one_InvalidState_and_nothing_is_written()
    {
        NoteId binned = Create("binned");
        Assert.True(new DeleteNoteHandler(_notes, _clock).Handle(new DeleteNote(binned)).IsSuccess);

        Assert.Equal(CommandFailureReason.NotFound, Duplicate(NoteId.New()).Failure!.Reason);
        Assert.Equal(CommandFailureReason.InvalidState, Duplicate(binned).Failure!.Reason);
        Assert.Empty(_notes.ListInFolder(_home));
    }

    [Fact]
    public void A_failed_insert_writes_nothing()
    {
        NoteId a = Create("a");
        Sql("CREATE TRIGGER inject BEFORE INSERT ON Notes BEGIN SELECT RAISE(ABORT, 'injected'); END;");

        Assert.Throws<StorageException>(() => Duplicate(a));

        Assert.Equal([a], Order());
    }

    private void Sql(string sql)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
