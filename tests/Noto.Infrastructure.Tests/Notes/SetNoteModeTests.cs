using Noto.Core.Commands;
using Noto.Core.Folders;
using Noto.Core.Notes;
using Noto.Infrastructure.Storage;
using Noto.Infrastructure.Tests.Folders;
using Noto.Infrastructure.Tests.Storage;
using Noto.UseCases.Folders;
using Noto.UseCases.Notes;
using Xunit;

namespace Noto.Infrastructure.Tests.Notes;

/// <summary>
/// <c>SetNoteMode</c> and a new note's mode against real SQLite (#195; parity D27, D28): stored by name, read back,
/// and never a change to the text.
/// </summary>
public sealed class SetNoteModeTests : IDisposable
{
    private readonly TempDatabase _temp = new();
    private readonly NotoDatabase _database;
    private readonly SqliteNoteRepository _notes;
    private readonly MutableClock _clock = new(new DateTimeOffset(2026, 11, 16, 10, 0, 0, TimeSpan.Zero));
    private readonly FolderId _home;

    public SetNoteModeTests()
    {
        _database = new NotoDatabase(_temp.DatabasePath);
        _database.Initialize();
        _notes = new SqliteNoteRepository(_database);
        _home = new CreateFolderHandler(new SqliteFolderRepository(_database), _clock).Handle(new CreateFolder("Home")).Value;
    }

    public void Dispose() => _temp.Dispose();

    private NoteId Create(string content, NoteMode mode = NoteMode.Markdown) =>
        new CreateNoteHandler(_notes, _clock).Handle(new CreateNote(_home, content, Mode: mode)).Value;

    private CommandResult SetMode(NoteId id, NoteMode mode) =>
        new SetNoteModeHandler(_notes, _clock).Handle(new SetNoteMode(id, mode));

    [Theory]
    [InlineData(NoteMode.Code, "Code")]
    [InlineData(NoteMode.PlainText, "PlainText")]
    [InlineData(NoteMode.Markdown, "Markdown")]
    public void Each_mode_is_stored_by_name_and_read_back(NoteMode mode, string stored)
    {
        var id = Create("note", mode == NoteMode.Markdown ? NoteMode.Code : NoteMode.Markdown);

        Assert.True(SetMode(id, mode).IsSuccess);

        Assert.Equal(mode, _notes.FindActive(id)!.Mode);
        Assert.Equal(stored, Persisted(id));
    }

    [Fact]
    public void A_new_note_is_markdown_unless_created_in_another_mode()
    {
        Assert.Equal(NoteMode.Markdown, _notes.FindActive(Create("a"))!.Mode);
        Assert.Equal(NoteMode.Code, _notes.FindActive(Create("b", NoteMode.Code))!.Mode);
        Assert.Equal(NoteMode.PlainText, _notes.FindActive(Create("c", NoteMode.PlainText))!.Mode);

        // Listed with its mode, as the stack reads it.
        Assert.Equal([NoteMode.Markdown, NoteMode.Code, NoteMode.PlainText], _notes.ListInFolder(_home).Select(n => n.Mode));
    }

    [Fact]
    public void The_text_is_never_changed_and_switching_back_restores_the_markdown_reading()
    {
        const string text = "# Heading\r\n**bold**\n\t- [ ] task  ";
        var id = Create(text);
        Note original = _notes.FindActive(id)!;

        _clock.Advance(TimeSpan.FromHours(1));
        Assert.True(SetMode(id, NoteMode.Code).IsSuccess);
        Note code = _notes.FindActive(id)!;
        Assert.Equal(text, code.Content);
        Assert.Equal("# Heading", code.Title);
        Assert.Equal(_clock.UtcNow, code.UpdatedAt);
        Assert.Equal((original.FolderId, original.SortOrder, original.IsPinned, original.IsFolded, original.ColorKey, original.CreatedAt), (code.FolderId, code.SortOrder, code.IsPinned, code.IsFolded, code.ColorKey, code.CreatedAt));

        Assert.True(SetMode(id, NoteMode.Markdown).IsSuccess);
        Note back = _notes.FindActive(id)!;
        Assert.Equal(text, back.Content);
        Assert.Equal(original.Title, back.Title);
        Assert.Equal("Heading", back.Title);
    }

    [Fact]
    public void The_same_mode_again_writes_nothing()
    {
        var id = Create("note", NoteMode.Code);
        DateTimeOffset stamped = _notes.FindActive(id)!.UpdatedAt;

        _clock.Advance(TimeSpan.FromHours(1));
        Assert.True(SetMode(id, NoteMode.Code).IsSuccess);

        Assert.Equal(stamped, _notes.FindActive(id)!.UpdatedAt);
    }

    [Fact]
    public void A_value_that_is_not_a_mode_is_refused_and_nothing_is_written()
    {
        var id = Create("note");

        CommandResult result = SetMode(id, (NoteMode)3);

        Assert.Equal(CommandFailureReason.InvalidInput, result.Failure!.Reason);
        Assert.Equal("Markdown", Persisted(id));
    }

    [Fact]
    public void A_missing_note_is_NotFound_and_a_deleted_one_InvalidState()
    {
        Assert.Equal(CommandFailureReason.NotFound, SetMode(NoteId.New(), NoteMode.Code).Failure!.Reason);

        var id = Create("note");
        Assert.True(new DeleteNoteHandler(_notes, _clock).Handle(new DeleteNote(id)).IsSuccess);

        Assert.Equal(CommandFailureReason.InvalidState, SetMode(id, NoteMode.Code).Failure!.Reason);
    }

    [Fact]
    public void Search_finds_a_code_note_by_its_text_as_before()
    {
        var id = Create("SELECT harbour FROM boats", NoteMode.Code);

        Assert.Contains(_notes.FindMatching(Noto.Core.Search.SearchQuery.Parse("harbour"), null, 10), n => n.Id == id && n.Mode == NoteMode.Code);
    }

    private string Persisted(NoteId id)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Mode FROM Notes WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id.Value);
        return (string)command.ExecuteScalar()!;
    }
}
