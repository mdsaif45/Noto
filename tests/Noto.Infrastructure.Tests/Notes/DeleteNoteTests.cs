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
/// <c>DeleteNote</c> and <c>RestoreNote</c> against real SQLite
/// (contract §11 rows 3–4).
/// </summary>
/// <remarks>
/// The bin is a lifecycle flag, not a removal: the row survives, keeps every
/// field including its <c>SortOrder</c>, and simply stops appearing in ordinary
/// queries (I1, I6).
/// </remarks>
public sealed class DeleteNoteTests : IDisposable
{
    private readonly TempDatabase _temp = new();
    private readonly NotoDatabase _database;
    private readonly SqliteNoteRepository _notes;
    private readonly MutableClock _clock = new(new DateTimeOffset(2026, 9, 15, 10, 30, 0, TimeSpan.Zero));

    public DeleteNoteTests()
    {
        _database = new NotoDatabase(_temp.DatabasePath);
        _database.Initialize();
        _notes = new SqliteNoteRepository(_database);
    }

    public void Dispose() => _temp.Dispose();

    private NoteId Create(string content, FolderId? folder = null) =>
        new CreateNoteHandler(_notes, _clock).Handle(new CreateNote(folder, content)).Value;

    private CommandResult Delete(NoteId id) =>
        new DeleteNoteHandler(_notes, _clock).Handle(new DeleteNote(id));

    private CommandResult Restore(NoteId id) =>
        new RestoreNoteHandler(_notes, _clock).Handle(new RestoreNote(id));

    // ---- ATTACK 1: the row must survive ------------------------------------

    [Fact]
    public void Deleting_marks_the_row_rather_than_removing_it()
    {
        // The nearest plausible wrong implementation is DELETE FROM Notes.
        // Everything downstream — the bin, restore, ADR-002's "data cannot be
        // recovered from a server" — depends on the row still being there.
        var id = Create("keep me");

        Assert.True(Delete(id).IsSuccess);

        Assert.Equal(1L, ScalarLong("SELECT COUNT(*) FROM Notes WHERE Id = $id;", id.Value));
        Assert.Equal(1L, ScalarLong("SELECT COUNT(*) FROM Notes WHERE Id = $id AND DeletedAt IS NOT NULL;", id.Value));
    }

    [Fact]
    public void A_deleted_note_keeps_every_field()
    {
        // Restoration restores the existing note; it does not reconstruct one.
        FolderId folder = InsertFolder("Work");
        var id = Create("# Title\n\nbody", folder);
        new PinNoteHandler(_notes, _clock).Handle(new PinNote(id));
        new FoldNoteHandler(_notes, _clock).Handle(new FoldNote(id));
        new SetNoteColorHandler(_notes, _clock).Handle(new SetNoteColor(id, "note4"));

        Note before = _notes.FindActive(id)!;
        Delete(id);

        Note binned = Single(new ListDeletedNotesQuery(_notes).Execute());
        Assert.Equal(before.Content, binned.Content);
        Assert.Equal(before.Title, binned.Title);
        Assert.Equal(before.FolderId, binned.FolderId);
        Assert.Equal(before.SortOrder, binned.SortOrder);
        Assert.Equal(before.IsPinned, binned.IsPinned);
        Assert.Equal(before.IsFolded, binned.IsFolded);
        Assert.Equal(before.ColorKey, binned.ColorKey);
        Assert.Equal(before.CreatedAt, binned.CreatedAt);
    }

    // ---- ATTACK 2: siblings must not be renumbered -------------------------

    [Fact]
    public void Deleting_leaves_the_siblings_sort_order_untouched()
    {
        // O5: "deleting leaves gaps. Gaps are harmless." The plausible wrong
        // implementation tidies up by renumbering, which would silently move
        // every other note.
        var a = Create("a");
        var b = Create("b");
        var c = Create("c");
        var d = Create("d");

        double orderA = _notes.FindActive(a)!.SortOrder;
        double orderC = _notes.FindActive(c)!.SortOrder;
        double orderD = _notes.FindActive(d)!.SortOrder;

        Delete(b);

        Assert.Equal(orderA, _notes.FindActive(a)!.SortOrder);
        Assert.Equal(orderC, _notes.FindActive(c)!.SortOrder);
        Assert.Equal(orderD, _notes.FindActive(d)!.SortOrder);

        // The gap is real: c did not slide down into b's place.
        Assert.True(orderC - orderA > 1.5, $"expected a gap, got {orderA} then {orderC}");
    }

    [Fact]
    public void A_deleted_note_keeps_its_own_sort_order()
    {
        // I6: "deleted rows keep their value — so restore returns a note
        // roughly where it was". Zeroing it would make restore arbitrary.
        var a = Create("a");
        var b = Create("b");
        Create("c");

        double original = _notes.FindActive(b)!.SortOrder;
        Delete(b);

        Assert.Equal(original, Single(new ListDeletedNotesQuery(_notes).Execute()).SortOrder);
        Assert.True(original > _notes.FindActive(a)!.SortOrder);
    }

    // ---- ATTACK 3: ordinary queries must not expose the bin ----------------

    [Fact]
    public void GetNote_reports_a_deleted_note_as_NotFound()
    {
        // I1. The plausible wrong implementation looks the note up by id
        // without the DeletedAt filter.
        var id = Create("note");
        Delete(id);

        var result = new GetNoteQuery(_notes).Execute(id);

        Assert.False(result.IsSuccess);
        Assert.Equal(CommandFailureReason.NotFound, result.Failure!.Reason);
    }

    [Fact]
    public void A_deleted_note_is_indistinguishable_from_a_missing_one()
    {
        var id = Create("note");
        Delete(id);

        var deleted = new GetNoteQuery(_notes).Execute(id);
        var missing = new GetNoteQuery(_notes).Execute(NoteId.New());

        Assert.Equal(missing.Failure!.Reason, deleted.Failure!.Reason);
    }

    [Fact]
    public void FindActive_excludes_a_deleted_note()
    {
        var id = Create("note");
        Delete(id);

        Assert.Null(_notes.FindActive(id));
    }

    // ---- lifecycle ----------------------------------------------------------

    [Fact]
    public void Deleting_a_missing_note_is_NotFound()
    {
        var result = Delete(NoteId.New());

        Assert.False(result.IsSuccess);
        Assert.Equal(CommandFailureReason.NotFound, result.Failure!.Reason);
    }

    [Fact]
    public void Deleting_an_already_deleted_note_is_InvalidState()
    {
        // Contract §11 row 3 lists InvalidState, and I5 makes a deleted entity
        // inert: restore and purge are its only legal transitions. This is
        // deliberately NOT idempotent — unlike pin/fold/colour, where the
        // contract does specify a no-op.
        var id = Create("note");
        Delete(id);

        var result = Delete(id);

        Assert.False(result.IsSuccess);
        Assert.Equal(CommandFailureReason.InvalidState, result.Failure!.Reason);
    }

    [Fact]
    public void A_second_delete_does_not_move_the_original_deleted_at()
    {
        var id = Create("note");
        Delete(id);
        DateTimeOffset original = Single(new ListDeletedNotesQuery(_notes).Execute()).DeletedAt!.Value;

        _clock.Advance(TimeSpan.FromHours(3));
        Delete(id);

        Assert.Equal(original, Single(new ListDeletedNotesQuery(_notes).Execute()).DeletedAt);
    }

    // ---- timestamps ---------------------------------------------------------

    [Fact]
    public void Deleting_stamps_deleted_at_and_updated_at_with_one_instant()
    {
        var id = Create("note");
        DateTimeOffset created = _notes.FindActive(id)!.CreatedAt;

        _clock.Advance(TimeSpan.FromHours(5));
        Delete(id);

        Note binned = Single(new ListDeletedNotesQuery(_notes).Execute());
        Assert.Equal(_clock.UtcNow, binned.DeletedAt);
        Assert.Equal(_clock.UtcNow, binned.UpdatedAt);
        Assert.Equal(binned.DeletedAt, binned.UpdatedAt);
        Assert.Equal(created, binned.CreatedAt);
    }

    [Fact]
    public void A_rejected_delete_writes_nothing()
    {
        var id = Create("note");
        DateTimeOffset stamped = _notes.FindActive(id)!.UpdatedAt;

        _clock.Advance(TimeSpan.FromHours(1));
        Delete(NoteId.New());

        Assert.Equal(stamped, _notes.FindActive(id)!.UpdatedAt);
        Assert.Equal(0L, ScalarLong("SELECT COUNT(*) FROM Notes WHERE DeletedAt IS NOT NULL AND Id = $id;", id.Value));
    }

    // ---- RESTORE -------------------------------------------------------------

    [Fact]
    public void Restoring_brings_the_note_back()
    {
        var id = Create("note");
        Delete(id);

        Assert.True(Restore(id).IsSuccess);

        Note active = _notes.FindActive(id)!;
        Assert.Null(active.DeletedAt);
        Assert.False(active.IsDeleted);
    }

    // ---- ATTACK 5: restore must not recompute state ------------------------

    [Fact]
    public void Restoring_preserves_every_field()
    {
        // The plausible wrong implementation "resets" the note on the way back:
        // moving it to the end of the scope, clearing its colour, unpinning it.
        FolderId folder = InsertFolder("Work");
        var id = Create("# Title\n\nbody", folder);
        new PinNoteHandler(_notes, _clock).Handle(new PinNote(id));
        new FoldNoteHandler(_notes, _clock).Handle(new FoldNote(id));
        new SetNoteColorHandler(_notes, _clock).Handle(new SetNoteColor(id, "note2"));

        Note before = _notes.FindActive(id)!;
        Delete(id);
        _clock.Advance(TimeSpan.FromHours(2));
        Restore(id);

        Note after = _notes.FindActive(id)!;
        Assert.Equal(before.Content, after.Content);
        Assert.Equal(before.Title, after.Title);
        Assert.Equal(before.FolderId, after.FolderId);
        Assert.Equal(before.SortOrder, after.SortOrder);
        Assert.Equal(before.IsPinned, after.IsPinned);
        Assert.Equal(before.IsFolded, after.IsFolded);
        Assert.Equal(before.ColorKey, after.ColorKey);
        Assert.Equal(before.CreatedAt, after.CreatedAt);
    }

    [Fact]
    public void A_restored_note_returns_to_its_original_position()
    {
        // I6's promise, observed: "restore returns a note roughly where it
        // was". No placement logic is needed because SortOrder was kept.
        var a = Create("a");
        var b = Create("b");
        var c = Create("c");

        Assert.Equal([a, b, c], ActiveIds());

        Delete(b);
        Assert.Equal([a, c], ActiveIds());

        Restore(b);
        Assert.Equal([a, b, c], ActiveIds());
    }

    [Fact]
    public void Restoring_stamps_updated_at_and_clears_deleted_at()
    {
        var id = Create("note");
        Delete(id);

        _clock.Advance(TimeSpan.FromHours(6));
        Restore(id);

        Note after = _notes.FindActive(id)!;
        Assert.Null(after.DeletedAt);
        Assert.Equal(_clock.UtcNow, after.UpdatedAt);
    }

    [Fact]
    public void Restoring_a_missing_note_is_NotFound()
    {
        var result = Restore(NoteId.New());

        Assert.False(result.IsSuccess);
        Assert.Equal(CommandFailureReason.NotFound, result.Failure!.Reason);
    }

    [Fact]
    public void Restoring_an_active_note_is_InvalidState()
    {
        // Contract §6: InvalidState covers "RestoreNote on a non-deleted one".
        var id = Create("note");

        var result = Restore(id);

        Assert.False(result.IsSuccess);
        Assert.Equal(CommandFailureReason.InvalidState, result.Failure!.Reason);
    }

    [Fact]
    public void A_rejected_restore_writes_nothing()
    {
        var id = Create("note");
        DateTimeOffset stamped = _notes.FindActive(id)!.UpdatedAt;

        _clock.Advance(TimeSpan.FromHours(1));
        Restore(id);

        Assert.Equal(stamped, _notes.FindActive(id)!.UpdatedAt);
    }

    [Fact]
    public void An_active_note_in_a_deleted_folder_survives_the_round_trip()
    {
        // Case C: a restored note keeps a FolderId that still points at a
        // deleted folder. That state is legal and is how a user rescues one
        // note from a binned folder.
        FolderId folder = InsertFolder("Archive");
        var id = Create("rescue me", folder);
        SoftDeleteFolder(folder);

        Delete(id);
        Assert.True(Restore(id).IsSuccess);

        Assert.Equal(folder, _notes.FindActive(id)!.FolderId);
    }

    [Fact]
    public void The_lifecycle_survives_closing_and_reopening()
    {
        var id = Create("note");
        Delete(id);

        SqliteConnection.ClearAllPools();
        var reopened = new NotoDatabase(_temp.DatabasePath);
        reopened.Initialize();
        var repository = new SqliteNoteRepository(reopened);

        Assert.Null(repository.FindActive(id));
        Assert.Single(repository.ListDeleted());
    }

    [Fact]
    public void A_null_command_throws()
    {
        Assert.Throws<ArgumentNullException>(() => new DeleteNoteHandler(_notes, _clock).Handle(null!));
        Assert.Throws<ArgumentNullException>(() => new RestoreNoteHandler(_notes, _clock).Handle(null!));
    }

    // ---- helpers ------------------------------------------------------------

    private static Note Single(IReadOnlyList<Note> notes)
    {
        Assert.Single(notes);
        return notes[0];
    }

    private List<NoteId> ActiveIds()
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

    private long ScalarLong(string sql, string id)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", id);
        return (long)command.ExecuteScalar()!;
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
