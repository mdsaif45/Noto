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
/// <c>PinNote</c> / <c>UnpinNote</c> against real SQLite (contract §11, rows 7–8).
/// </summary>
/// <remarks>
/// The invariant these tests exist for: pinning changes <c>IsPinned</c> and
/// <b>never</b> <c>SortOrder</c>. O2 lifts pinned notes in the rendered list;
/// the underlying sequence is untouched, which is what lets row 8 promise that
/// unpinning "returns to `SortOrder` position".
/// </remarks>
public sealed class PinNoteTests : IDisposable
{
    private readonly TempDatabase _temp = new();
    private readonly NotoDatabase _database;
    private readonly SqliteNoteRepository _notes;
    private readonly MutableClock _clock = new(new DateTimeOffset(2026, 9, 15, 10, 30, 0, TimeSpan.Zero));

    public PinNoteTests()
    {
        _database = new NotoDatabase(_temp.DatabasePath);
        _database.Initialize();
        _notes = new SqliteNoteRepository(_database);
    }

    public void Dispose() => _temp.Dispose();

    private NoteId Create(string content, FolderId? folder = null) =>
        new CreateNoteHandler(_notes, _clock).Handle(new CreateNote(folder, content)).Value;

    private CommandResult Pin(NoteId id) =>
        new PinNoteHandler(_notes, _clock).Handle(new PinNote(id));

    private CommandResult Unpin(NoteId id) =>
        new UnpinNoteHandler(_notes, _clock).Handle(new UnpinNote(id));

    // ---- THE critical invariant -------------------------------------------

    [Fact]
    public void Pinning_and_unpinning_never_change_sort_order()
    {
        // The nearest plausible wrong implementation pins by moving the note to
        // the top of the scope — superficially reasonable, and it would destroy
        // the position unpinning is contractually required to return to.
        var a = Create("a");
        var b = Create("b");
        var c = Create("c");

        double originalA = _notes.FindActive(a)!.SortOrder;
        double originalB = _notes.FindActive(b)!.SortOrder;
        double originalC = _notes.FindActive(c)!.SortOrder;

        Assert.True(Pin(c).IsSuccess);

        Note pinned = _notes.FindActive(c)!;
        Assert.True(pinned.IsPinned);
        Assert.Equal(originalC, pinned.SortOrder);

        Assert.True(Unpin(c).IsSuccess);

        Note unpinned = _notes.FindActive(c)!;
        Assert.False(unpinned.IsPinned);
        Assert.Equal(originalC, unpinned.SortOrder);

        // Siblings are untouched too — a "move to top" implementation would
        // likely renumber them.
        Assert.Equal(originalA, _notes.FindActive(a)!.SortOrder);
        Assert.Equal(originalB, _notes.FindActive(b)!.SortOrder);
    }

    [Fact]
    public void Pinning_changes_the_displayed_position_without_changing_the_sequence()
    {
        // Both halves matter: the view moves, the data does not.
        var a = Create("a");
        var b = Create("b");
        var c = Create("c");

        Assert.Equal([a, b, c], OrderedIds());

        double originalC = _notes.FindActive(c)!.SortOrder;
        Pin(c);

        // Display: c is lifted to the front by O2's IsPinned DESC.
        Assert.Equal([c, a, b], OrderedIds());

        // Sequence: c is still last, and its stored value is byte-identical.
        Assert.Equal(originalC, _notes.FindActive(c)!.SortOrder);
        Assert.True(originalC > _notes.FindActive(b)!.SortOrder);
    }

    [Fact]
    public void Unpinning_returns_the_note_to_its_sort_order_position()
    {
        // Contract §11 row 8, stated as a round trip through the rendered list.
        var a = Create("a");
        var b = Create("b");
        var c = Create("c");

        Pin(b);
        Assert.Equal([b, a, c], OrderedIds());

        Unpin(b);
        Assert.Equal([a, b, c], OrderedIds());
    }

    [Fact]
    public void A_pinned_note_keeps_its_place_among_other_pinned_notes()
    {
        // Within the pinned partition, SortOrder still decides — further proof
        // that pinning is a partition rather than a reordering.
        var a = Create("a");
        var b = Create("b");
        var c = Create("c");

        Pin(c);
        Pin(a);

        Assert.Equal([a, c, b], OrderedIds());
    }

    // ---- state transitions -------------------------------------------------

    [Fact]
    public void Pinning_sets_the_flag()
    {
        var id = Create("note");
        Assert.False(_notes.FindActive(id)!.IsPinned);

        Assert.True(Pin(id).IsSuccess);

        Assert.True(_notes.FindActive(id)!.IsPinned);
    }

    [Fact]
    public void Unpinning_clears_the_flag()
    {
        var id = Create("note");
        Pin(id);

        Assert.True(Unpin(id).IsSuccess);

        Assert.False(_notes.FindActive(id)!.IsPinned);
    }

    [Fact]
    public void The_pinned_state_survives_closing_and_reopening()
    {
        var id = Create("note");
        Pin(id);

        SqliteConnection.ClearAllPools();
        var reopened = new NotoDatabase(_temp.DatabasePath);
        reopened.Initialize();

        Assert.True(new SqliteNoteRepository(reopened).FindActive(id)!.IsPinned);
    }

    // ---- no-op --------------------------------------------------------------

    [Fact]
    public void Pinning_an_already_pinned_note_writes_nothing()
    {
        var id = Create("note");
        Pin(id);
        DateTimeOffset stamped = _notes.FindActive(id)!.UpdatedAt;

        _clock.Advance(TimeSpan.FromHours(1));
        Assert.True(Pin(id).IsSuccess);

        Note after = _notes.FindActive(id)!;
        Assert.True(after.IsPinned);
        Assert.Equal(stamped, after.UpdatedAt);
    }

    [Fact]
    public void Unpinning_an_already_unpinned_note_writes_nothing()
    {
        var id = Create("note");
        DateTimeOffset stamped = _notes.FindActive(id)!.UpdatedAt;

        _clock.Advance(TimeSpan.FromHours(1));
        Assert.True(Unpin(id).IsSuccess);

        Note after = _notes.FindActive(id)!;
        Assert.False(after.IsPinned);
        Assert.Equal(stamped, after.UpdatedAt);
    }

    // ---- timestamps ---------------------------------------------------------

    [Fact]
    public void A_real_pin_stamps_updated_at()
    {
        var id = Create("note");
        DateTimeOffset created = _notes.FindActive(id)!.CreatedAt;

        _clock.Advance(TimeSpan.FromHours(4));
        Pin(id);

        Note after = _notes.FindActive(id)!;
        Assert.Equal(_clock.UtcNow, after.UpdatedAt);
        Assert.Equal(created, after.CreatedAt);
    }

    [Fact]
    public void A_real_unpin_stamps_updated_at()
    {
        var id = Create("note");
        Pin(id);

        _clock.Advance(TimeSpan.FromHours(4));
        Unpin(id);

        Assert.Equal(_clock.UtcNow, _notes.FindActive(id)!.UpdatedAt);
    }

    // ---- unrelated state ----------------------------------------------------

    [Fact]
    public void Pinning_leaves_every_other_field_alone()
    {
        FolderId folder = InsertFolder("Work");
        var id = Create("# Title\n\nbody", folder);
        Note original = _notes.FindActive(id)!;

        _clock.Advance(TimeSpan.FromHours(1));
        Pin(id);

        Note after = _notes.FindActive(id)!;
        Assert.Equal(original.Content, after.Content);
        Assert.Equal(original.Title, after.Title);
        Assert.Equal(original.FolderId, after.FolderId);
        Assert.Equal(original.SortOrder, after.SortOrder);
        Assert.Equal(original.IsFolded, after.IsFolded);
        Assert.Equal(original.ColorKey, after.ColorKey);
        Assert.Equal(original.CreatedAt, after.CreatedAt);
        Assert.Null(after.DeletedAt);
    }

    // ---- lifecycle ----------------------------------------------------------

    [Fact]
    public void Pinning_a_missing_note_is_NotFound()
    {
        var result = Pin(NoteId.New());

        Assert.False(result.IsSuccess);
        Assert.Equal(CommandFailureReason.NotFound, result.Failure!.Reason);
    }

    [Fact]
    public void Unpinning_a_missing_note_is_NotFound()
    {
        var result = Unpin(NoteId.New());

        Assert.False(result.IsSuccess);
        Assert.Equal(CommandFailureReason.NotFound, result.Failure!.Reason);
    }

    [Fact]
    public void Pinning_a_deleted_note_is_InvalidState()
    {
        // I5: deleted entities are inert.
        var id = Create("note");
        SoftDelete(id);

        var result = Pin(id);

        Assert.False(result.IsSuccess);
        Assert.Equal(CommandFailureReason.InvalidState, result.Failure!.Reason);
    }

    [Fact]
    public void Unpinning_a_deleted_note_is_InvalidState()
    {
        var id = Create("note");
        Pin(id);
        SoftDelete(id);

        var result = Unpin(id);

        Assert.False(result.IsSuccess);
        Assert.Equal(CommandFailureReason.InvalidState, result.Failure!.Reason);
    }

    [Fact]
    public void A_rejected_pin_writes_nothing()
    {
        var id = Create("note");
        SoftDelete(id);

        Pin(id);

        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT IsPinned FROM Notes WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id.Value);

        Assert.Equal(0L, (long)command.ExecuteScalar()!);
    }

    [Fact]
    public void An_active_note_in_a_deleted_folder_can_still_be_pinned()
    {
        // Case C: the folder's state is not a precondition.
        FolderId folder = InsertFolder("Work");
        var id = Create("note", folder);
        SoftDeleteFolder(folder);

        Assert.True(Pin(id).IsSuccess);
        Assert.True(_notes.FindActive(id)!.IsPinned);
    }

    [Fact]
    public void A_null_command_throws()
    {
        Assert.Throws<ArgumentNullException>(() => new PinNoteHandler(_notes, _clock).Handle(null!));
        Assert.Throws<ArgumentNullException>(() => new UnpinNoteHandler(_notes, _clock).Handle(null!));
    }

    // ---- helpers ------------------------------------------------------------

    private List<NoteId> OrderedIds()
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id FROM Notes WHERE FolderId IS NULL AND DeletedAt IS NULL
            ORDER BY IsPinned DESC, SortOrder ASC, Id ASC;
            """;

        var ids = new List<NoteId>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            ids.Add(NoteId.From(reader.GetString(0)));
        }

        return ids;
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
