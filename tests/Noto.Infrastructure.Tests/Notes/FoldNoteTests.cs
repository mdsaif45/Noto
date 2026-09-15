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
/// <c>FoldNote</c> / <c>UnfoldNote</c> against real SQLite (contract §11, rows 10–11).
/// </summary>
public sealed class FoldNoteTests : IDisposable
{
    private readonly TempDatabase _temp = new();
    private readonly NotoDatabase _database;
    private readonly SqliteNoteRepository _notes;
    private readonly MutableClock _clock = new(new DateTimeOffset(2026, 9, 15, 10, 30, 0, TimeSpan.Zero));

    public FoldNoteTests()
    {
        _database = new NotoDatabase(_temp.DatabasePath);
        _database.Initialize();
        _notes = new SqliteNoteRepository(_database);
    }

    public void Dispose() => _temp.Dispose();

    private NoteId Create(string content, FolderId? folder = null) =>
        new CreateNoteHandler(_notes, _clock).Handle(new CreateNote(folder, content)).Value;

    private CommandResult Fold(NoteId id) =>
        new FoldNoteHandler(_notes, _clock).Handle(new FoldNote(id));

    private CommandResult Unfold(NoteId id) =>
        new UnfoldNoteHandler(_notes, _clock).Handle(new UnfoldNote(id));

    // ---- transitions --------------------------------------------------------

    [Fact]
    public void Folding_sets_the_flag()
    {
        var id = Create("# Title\n\nbody");
        Assert.False(_notes.FindActive(id)!.IsFolded);

        Assert.True(Fold(id).IsSuccess);

        Assert.True(_notes.FindActive(id)!.IsFolded);
    }

    [Fact]
    public void Unfolding_clears_the_flag()
    {
        var id = Create("note");
        Fold(id);

        Assert.True(Unfold(id).IsSuccess);

        Assert.False(_notes.FindActive(id)!.IsFolded);
    }

    [Fact]
    public void Fold_and_unfold_are_independently_invocable()
    {
        // Contract §11 row 11: they are two commands, not one toggle — which is
        // why parity B12 gives them separate chords (Ctrl+Alt+Left / Right).
        var id = Create("note");

        Fold(id);
        Fold(id);
        Assert.True(_notes.FindActive(id)!.IsFolded);

        Unfold(id);
        Unfold(id);
        Assert.False(_notes.FindActive(id)!.IsFolded);
    }

    [Fact]
    public void The_folded_state_survives_closing_and_reopening()
    {
        // Parity B12: folding is user-authored note state, not a rendering
        // detail, so it must persist.
        var id = Create("note");
        Fold(id);

        SqliteConnection.ClearAllPools();
        var reopened = new NotoDatabase(_temp.DatabasePath);
        reopened.Initialize();

        Assert.True(new SqliteNoteRepository(reopened).FindActive(id)!.IsFolded);
    }

    [Fact]
    public void A_folded_note_keeps_its_title_and_content()
    {
        // B12 folds a note "to its first line" — a display concern. The content
        // is not truncated.
        var id = Create("# Visible when folded\n\nhidden body");
        Fold(id);

        Note folded = _notes.FindActive(id)!;
        Assert.Equal("Visible when folded", folded.Title);
        Assert.Equal("# Visible when folded\n\nhidden body", folded.Content);
    }

    // ---- no-op --------------------------------------------------------------

    [Fact]
    public void Folding_an_already_folded_note_writes_nothing()
    {
        var id = Create("note");
        Fold(id);
        DateTimeOffset stamped = _notes.FindActive(id)!.UpdatedAt;

        _clock.Advance(TimeSpan.FromHours(1));
        Assert.True(Fold(id).IsSuccess);

        Note after = _notes.FindActive(id)!;
        Assert.True(after.IsFolded);
        Assert.Equal(stamped, after.UpdatedAt);
    }

    [Fact]
    public void Unfolding_an_already_unfolded_note_writes_nothing()
    {
        var id = Create("note");
        DateTimeOffset stamped = _notes.FindActive(id)!.UpdatedAt;

        _clock.Advance(TimeSpan.FromHours(1));
        Assert.True(Unfold(id).IsSuccess);

        Note after = _notes.FindActive(id)!;
        Assert.False(after.IsFolded);
        Assert.Equal(stamped, after.UpdatedAt);
    }

    // ---- timestamps ---------------------------------------------------------

    [Fact]
    public void A_real_fold_stamps_updated_at()
    {
        var id = Create("note");
        DateTimeOffset created = _notes.FindActive(id)!.CreatedAt;

        _clock.Advance(TimeSpan.FromHours(2));
        Fold(id);

        Note after = _notes.FindActive(id)!;
        Assert.Equal(_clock.UtcNow, after.UpdatedAt);
        Assert.Equal(created, after.CreatedAt);
    }

    [Fact]
    public void A_real_unfold_stamps_updated_at()
    {
        var id = Create("note");
        Fold(id);

        _clock.Advance(TimeSpan.FromHours(2));
        Unfold(id);

        Assert.Equal(_clock.UtcNow, _notes.FindActive(id)!.UpdatedAt);
    }

    // ---- unrelated state ----------------------------------------------------

    [Fact]
    public void Folding_leaves_every_other_field_alone()
    {
        FolderId folder = InsertFolder("Work");
        var id = Create("body", folder);
        new PinNoteHandler(_notes, _clock).Handle(new PinNote(id));
        Note original = _notes.FindActive(id)!;

        _clock.Advance(TimeSpan.FromHours(1));
        Fold(id);

        Note after = _notes.FindActive(id)!;
        Assert.Equal(original.Content, after.Content);
        Assert.Equal(original.FolderId, after.FolderId);
        Assert.Equal(original.SortOrder, after.SortOrder);
        Assert.Equal(original.IsPinned, after.IsPinned);
        Assert.Equal(original.ColorKey, after.ColorKey);
        Assert.Equal(original.CreatedAt, after.CreatedAt);
        Assert.Null(after.DeletedAt);
    }

    // ---- lifecycle ----------------------------------------------------------

    [Fact]
    public void Folding_a_missing_note_is_NotFound()
    {
        var result = Fold(NoteId.New());

        Assert.False(result.IsSuccess);
        Assert.Equal(CommandFailureReason.NotFound, result.Failure!.Reason);
    }

    [Fact]
    public void Unfolding_a_missing_note_is_NotFound()
    {
        var result = Unfold(NoteId.New());

        Assert.False(result.IsSuccess);
        Assert.Equal(CommandFailureReason.NotFound, result.Failure!.Reason);
    }

    [Fact]
    public void Folding_a_deleted_note_is_InvalidState()
    {
        var id = Create("note");
        SoftDelete(id);

        var result = Fold(id);

        Assert.False(result.IsSuccess);
        Assert.Equal(CommandFailureReason.InvalidState, result.Failure!.Reason);
    }

    [Fact]
    public void Unfolding_a_deleted_note_is_InvalidState()
    {
        var id = Create("note");
        Fold(id);
        SoftDelete(id);

        var result = Unfold(id);

        Assert.False(result.IsSuccess);
        Assert.Equal(CommandFailureReason.InvalidState, result.Failure!.Reason);
    }

    [Fact]
    public void A_rejected_fold_writes_nothing()
    {
        var id = Create("note");
        SoftDelete(id);

        Fold(id);

        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT IsFolded FROM Notes WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id.Value);

        Assert.Equal(0L, (long)command.ExecuteScalar()!);
    }

    [Fact]
    public void A_null_command_throws()
    {
        Assert.Throws<ArgumentNullException>(() => new FoldNoteHandler(_notes, _clock).Handle(null!));
        Assert.Throws<ArgumentNullException>(() => new UnfoldNoteHandler(_notes, _clock).Handle(null!));
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

    private void SoftDelete(NoteId id)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Notes SET DeletedAt = $now WHERE Id = $id;";
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
