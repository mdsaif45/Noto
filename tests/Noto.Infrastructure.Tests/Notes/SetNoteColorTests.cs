using Microsoft.Data.Sqlite;
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
/// <c>SetNoteColor</c> against real SQLite (contract §11, row 9; ADR-011).
/// </summary>
/// <remarks>
/// The palette is a closed set of six persisted keys plus <see langword="null"/>.
/// <c>ColorKey</c> is a <c>TEXT</c> column with no <c>CHECK</c> constraint, so
/// nothing but this validation stops an arbitrary string becoming durable data.
/// </remarks>
public sealed class SetNoteColorTests : IDisposable
{
    private readonly TempDatabase _temp = new();
    private readonly NotoDatabase _database;
    private readonly SqliteNoteRepository _notes;
    private readonly MutableClock _clock = new(new DateTimeOffset(2026, 9, 15, 10, 30, 0, TimeSpan.Zero));

    public SetNoteColorTests()
    {
        _database = new NotoDatabase(_temp.DatabasePath);
        _database.Initialize();
        _notes = new SqliteNoteRepository(_database);
    }

    public void Dispose() => _temp.Dispose();

    private NoteId Create(string content, FolderId? folder = null) =>
        new CreateNoteHandler(_notes, _clock).Handle(new CreateNote(folder, content)).Value;

    private CommandResult SetColor(NoteId id, string? key) =>
        new SetNoteColorHandler(_notes, _clock).Handle(new SetNoteColor(id, key));

    // ---- the six valid keys -------------------------------------------------

    [Theory]
    [InlineData("note1")]
    [InlineData("note2")]
    [InlineData("note3")]
    [InlineData("note4")]
    [InlineData("note5")]
    [InlineData("note6")]
    public void Each_palette_key_is_accepted_and_persisted(string key)
    {
        var id = Create("note");

        Assert.True(SetColor(id, key).IsSuccess);

        Assert.Equal(key, _notes.FindActive(id)!.ColorKey);
        Assert.Equal(key, PersistedColor(id));
    }

    [Fact]
    public void The_palette_is_exactly_six_keys()
    {
        // Parity B15: "Six, not 'some'". A seventh would silently widen a
        // durable data contract.
        Assert.Equal(6, NoteColor.Keys.Count);
        Assert.Equal(["note1", "note2", "note3", "note4", "note5", "note6"], NoteColor.Keys);
    }

    [Fact]
    public void A_colour_can_be_changed_to_another_valid_colour()
    {
        var id = Create("note");
        SetColor(id, "note2");

        Assert.True(SetColor(id, "note5").IsSuccess);

        Assert.Equal("note5", _notes.FindActive(id)!.ColorKey);
    }

    [Fact]
    public void The_colour_survives_closing_and_reopening()
    {
        var id = Create("note");
        SetColor(id, "note4");

        SqliteConnection.ClearAllPools();
        var reopened = new NotoDatabase(_temp.DatabasePath);
        reopened.Initialize();

        Assert.Equal("note4", new SqliteNoteRepository(reopened).FindActive(id)!.ColorKey);
    }

    // ---- clearing (Ctrl+0) --------------------------------------------------

    [Fact]
    public void Null_clears_the_colour_to_sql_null()
    {
        var id = Create("note");
        SetColor(id, "note3");

        _clock.Advance(TimeSpan.FromHours(1));
        Assert.True(SetColor(id, null).IsSuccess);

        Assert.Null(_notes.FindActive(id)!.ColorKey);

        // SQL NULL, not the string "none"/"null"/"" — ADR-011 makes null the
        // no-colour state rather than a seventh key.
        Assert.True(IsSqlNull(id));
        Assert.Equal(_clock.UtcNow, _notes.FindActive(id)!.UpdatedAt);
    }

    [Fact]
    public void Clearing_an_uncoloured_note_writes_nothing()
    {
        var id = Create("note");
        DateTimeOffset stamped = _notes.FindActive(id)!.UpdatedAt;

        _clock.Advance(TimeSpan.FromHours(1));
        Assert.True(SetColor(id, null).IsSuccess);

        Assert.Null(_notes.FindActive(id)!.ColorKey);
        Assert.Equal(stamped, _notes.FindActive(id)!.UpdatedAt);
    }

    // ---- unknown keys are REJECTED and NOT stored ---------------------------

    [Theory]
    [InlineData("unknown")]
    [InlineData("note0")]
    [InlineData("note7")]
    [InlineData("yellow")]
    [InlineData("#ffcc00")]
    [InlineData("NOTE1")]
    [InlineData("note1 ")]
    [InlineData("")]
    [InlineData("none")]
    public void An_unknown_key_is_InvalidInput_and_changes_nothing(string key)
    {
        // Both halves are required. Asserting only the returned failure would
        // pass against an implementation that rejects AND writes.
        var id = Create("note");
        SetColor(id, "note2");
        DateTimeOffset stamped = _notes.FindActive(id)!.UpdatedAt;

        _clock.Advance(TimeSpan.FromHours(1));
        var result = SetColor(id, key);

        Assert.False(result.IsSuccess);
        Assert.Equal(CommandFailureReason.InvalidInput, result.Failure!.Reason);

        Assert.Equal("note2", PersistedColor(id));
        Assert.Equal(stamped, _notes.FindActive(id)!.UpdatedAt);
    }

    [Fact]
    public void An_unknown_key_does_not_reach_an_uncoloured_note()
    {
        var id = Create("note");

        Assert.False(SetColor(id, "chartreuse").IsSuccess);

        Assert.True(IsSqlNull(id));
    }

    [Fact]
    public void The_rejection_message_does_not_echo_the_rejected_value()
    {
        // Principle 10: the value is unbounded caller input, and a caller that
        // passed the wrong variable would put it into a message that may be
        // logged. The valid set is named instead.
        var id = Create("note");

        var result = SetColor(id, "some-secret-looking-value");

        Assert.DoesNotContain("some-secret-looking-value", result.Failure!.Message, StringComparison.Ordinal);
        Assert.Contains("note1", result.Failure.Message, StringComparison.Ordinal);
    }

    // ---- no-op --------------------------------------------------------------

    [Fact]
    public void Setting_the_same_colour_again_writes_nothing()
    {
        var id = Create("note");
        SetColor(id, "note6");
        DateTimeOffset stamped = _notes.FindActive(id)!.UpdatedAt;

        _clock.Advance(TimeSpan.FromHours(1));
        Assert.True(SetColor(id, "note6").IsSuccess);

        Note after = _notes.FindActive(id)!;
        Assert.Equal("note6", after.ColorKey);
        Assert.Equal(stamped, after.UpdatedAt);
    }

    // ---- timestamps and unrelated state -------------------------------------

    [Fact]
    public void A_real_colour_change_stamps_updated_at()
    {
        var id = Create("note");
        DateTimeOffset created = _notes.FindActive(id)!.CreatedAt;

        _clock.Advance(TimeSpan.FromHours(3));
        SetColor(id, "note1");

        Note after = _notes.FindActive(id)!;
        Assert.Equal(_clock.UtcNow, after.UpdatedAt);
        Assert.Equal(created, after.CreatedAt);
    }

    [Fact]
    public void Colouring_leaves_every_other_field_alone()
    {
        FolderId folder = InsertFolder("Work");
        var id = Create("# Title\n\nbody", folder);
        new PinNoteHandler(_notes, _clock).Handle(new PinNote(id));
        Note original = _notes.FindActive(id)!;

        _clock.Advance(TimeSpan.FromHours(1));
        SetColor(id, "note3");

        Note after = _notes.FindActive(id)!;
        Assert.Equal(original.Content, after.Content);
        Assert.Equal(original.Title, after.Title);
        Assert.Equal(original.FolderId, after.FolderId);
        Assert.Equal(original.SortOrder, after.SortOrder);
        Assert.Equal(original.IsPinned, after.IsPinned);
        Assert.Equal(original.IsFolded, after.IsFolded);
        Assert.Equal(original.CreatedAt, after.CreatedAt);
        Assert.Null(after.DeletedAt);
    }

    // ---- lifecycle ----------------------------------------------------------

    [Fact]
    public void A_missing_note_is_NotFound()
    {
        var result = SetColor(NoteId.New(), "note1");

        Assert.False(result.IsSuccess);
        Assert.Equal(CommandFailureReason.NotFound, result.Failure!.Reason);
    }

    [Fact]
    public void A_deleted_note_is_InvalidState()
    {
        var id = Create("note");
        SoftDelete(id);

        var result = SetColor(id, "note1");

        Assert.False(result.IsSuccess);
        Assert.Equal(CommandFailureReason.InvalidState, result.Failure!.Reason);
    }

    [Fact]
    public void A_deleted_note_is_InvalidState_even_with_an_invalid_key()
    {
        // The lifecycle precondition is evaluated before validation, so the
        // caller learns the actionable thing: the note is in the bin.
        var id = Create("note");
        SoftDelete(id);

        var result = SetColor(id, "not-a-key");

        Assert.Equal(CommandFailureReason.InvalidState, result.Failure!.Reason);
    }

    [Fact]
    public void An_active_note_in_a_deleted_folder_can_still_be_coloured()
    {
        FolderId folder = InsertFolder("Work");
        var id = Create("note", folder);
        SoftDeleteFolder(folder);

        Assert.True(SetColor(id, "note2").IsSuccess);
        Assert.Equal("note2", _notes.FindActive(id)!.ColorKey);
    }

    [Fact]
    public void A_null_command_throws()
    {
        Assert.Throws<ArgumentNullException>(() => new SetNoteColorHandler(_notes, _clock).Handle(null!));
    }

    // ---- helpers ------------------------------------------------------------

    private string? PersistedColor(NoteId id)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ColorKey FROM Notes WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id.Value);

        object? value = command.ExecuteScalar();
        return value is null or DBNull ? null : (string)value;
    }

    private bool IsSqlNull(NoteId id)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Notes WHERE Id = $id AND ColorKey IS NULL;";
        command.Parameters.AddWithValue("$id", id.Value);

        return (long)command.ExecuteScalar()! == 1;
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
