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
/// <c>UpdateNoteContent</c> against real SQLite (contract §11, command 2).
/// </summary>
public sealed class UpdateNoteContentTests : IDisposable
{
    private readonly TempDatabase _temp = new();
    private readonly NotoDatabase _database;
    private readonly SqliteNoteRepository _notes;
    private readonly MutableClock _clock = new(new DateTimeOffset(2026, 9, 15, 10, 30, 0, TimeSpan.Zero));

    public UpdateNoteContentTests()
    {
        _database = new NotoDatabase(_temp.DatabasePath);
        _database.Initialize();
        _notes = new SqliteNoteRepository(_database);
    }

    public void Dispose() => _temp.Dispose();

    private NoteId Create(string content, FolderId? folder = null) =>
        new CreateNoteHandler(_notes, _clock).Handle(new CreateNote(folder, content)).Value;

    private CommandResult Update(NoteId id, string content) =>
        new UpdateNoteContentHandler(_notes, _clock).Handle(new UpdateNoteContent(id, content));

    [Fact]
    public void Replaces_the_content()
    {
        var id = Create("before");

        Assert.True(Update(id, "after").IsSuccess);

        Assert.Equal("after", _notes.FindActive(id)!.Content);
    }

    [Fact]
    public void The_title_is_re_derived_from_the_new_content()
    {
        // B16: the title is computed, never stored, so it follows content
        // without a second write.
        var id = Create("# Old title\n\nbody");

        Update(id, "# New title\n\nbody");

        Assert.Equal("New title", _notes.FindActive(id)!.Title);
    }

    [Theory]
    [InlineData("Plain first line\nsecond", "Plain first line")]
    [InlineData("# Heading\nbody", "Heading")]
    [InlineData("\r\n\r\n  Indented after blanks\r\nx", "Indented after blanks")]
    [InlineData("first\rsecond", "first")]
    [InlineData("#Not a heading", "#Not a heading")]
    [InlineData("", "")]
    [InlineData("   \t  ", "")]
    public void The_title_follows_the_same_rules_as_creation(string content, string expectedTitle)
    {
        // Slice 1's algorithm is reused, not reimplemented: a second copy would
        // be the divergence B16 exists to prevent.
        var id = Create("initial");

        Update(id, content);

        Assert.Equal(expectedTitle, _notes.FindActive(id)!.Title);
    }

    [Fact]
    public void Content_is_stored_verbatim_including_newlines_and_unicode()
    {
        const string content = "# Heading\r\n\r\n- [ ] task\n\nÜnïcödé 🗒️";
        var id = Create("initial");

        Update(id, content);

        Assert.Equal(content, _notes.FindActive(id)!.Content);
    }

    [Fact]
    public void Emptying_a_note_is_allowed()
    {
        var id = Create("something");

        Assert.True(Update(id, string.Empty).IsSuccess);

        Note note = _notes.FindActive(id)!;
        Assert.Equal(string.Empty, note.Content);
        Assert.Equal(string.Empty, note.Title);
    }

    [Fact]
    public void Updating_stamps_updated_at_but_not_created_at()
    {
        var id = Create("before");
        Note original = _notes.FindActive(id)!;

        _clock.Advance(TimeSpan.FromHours(2));
        Update(id, "after");

        Note updated = _notes.FindActive(id)!;
        Assert.Equal(_clock.UtcNow, updated.UpdatedAt);
        Assert.Equal(original.CreatedAt, updated.CreatedAt);
        Assert.NotEqual(updated.CreatedAt, updated.UpdatedAt);
    }

    [Fact]
    public void Every_other_field_is_preserved()
    {
        FolderId folder = InsertFolder("Work");
        var id = Create("before", folder);
        Note original = _notes.FindActive(id)!;

        Update(id, "after");

        Note updated = _notes.FindActive(id)!;
        Assert.Equal(original.Id, updated.Id);
        Assert.Equal(original.FolderId, updated.FolderId);
        Assert.Equal(original.SortOrder, updated.SortOrder);
        Assert.Equal(original.IsPinned, updated.IsPinned);
        Assert.Equal(original.IsFolded, updated.IsFolded);
        Assert.Equal(original.ColorKey, updated.ColorKey);
        Assert.Equal(original.DeletedAt, updated.DeletedAt);
    }

    [Fact]
    public void A_missing_note_is_NotFound()
    {
        var result = Update(NoteId.New(), "x");

        Assert.False(result.IsSuccess);
        Assert.Equal(CommandFailureReason.NotFound, result.Failure!.Reason);
    }

    [Fact]
    public void A_deleted_note_is_InvalidState()
    {
        // I5: deleted entities are inert. This is the "edited something in the
        // bin, then restored it" bug class.
        var id = Create("in the bin");
        SoftDelete(id);

        var result = Update(id, "edited");

        Assert.False(result.IsSuccess);
        Assert.Equal(CommandFailureReason.InvalidState, result.Failure!.Reason);
    }

    [Fact]
    public void A_rejected_update_changes_nothing()
    {
        var id = Create("original");
        SoftDelete(id);

        Update(id, "edited");

        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Content FROM Notes WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id.Value);

        Assert.Equal("original", (string)command.ExecuteScalar()!);
    }

    [Fact]
    public void An_active_note_in_a_deleted_folder_can_still_be_edited()
    {
        // Case C: the folder's state is not a precondition for editing an
        // active note inside it.
        FolderId folder = InsertFolder("Work");
        var id = Create("before", folder);
        SoftDeleteFolder(folder);

        Assert.True(Update(id, "after").IsSuccess);
        Assert.Equal("after", _notes.FindActive(id)!.Content);
    }

    [Fact]
    public void Updating_one_note_does_not_touch_another()
    {
        var first = Create("first");
        var second = Create("second");
        Note untouched = _notes.FindActive(second)!;

        _clock.Advance(TimeSpan.FromHours(1));
        Update(first, "changed");

        Note after = _notes.FindActive(second)!;
        Assert.Equal(untouched.Content, after.Content);
        Assert.Equal(untouched.UpdatedAt, after.UpdatedAt);
    }

    [Fact]
    public void A_null_command_throws()
    {
        var handler = new UpdateNoteContentHandler(_notes, _clock);

        Assert.Throws<ArgumentNullException>(() => handler.Handle(null!));
    }

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

    private void SoftDelete(NoteId id) =>
        Execute("UPDATE Notes SET DeletedAt = $now WHERE Id = $id;", id.Value);

    private void SoftDeleteFolder(FolderId id) =>
        Execute("UPDATE Folders SET DeletedAt = $now WHERE Id = $id;", id.Value);

    private void Execute(string sql, string id)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$now", _clock.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    private sealed class MutableClock(DateTimeOffset start) : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = start;

        public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
    }
}
