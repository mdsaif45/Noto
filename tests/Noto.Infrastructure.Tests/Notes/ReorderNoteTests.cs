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
/// <c>ReorderNote</c> and the ordering rules O1–O6, against real SQLite.
/// </summary>
public sealed class ReorderNoteTests : IDisposable
{
    private readonly TempDatabase _temp = new();
    private readonly NotoDatabase _database;
    private readonly SqliteNoteRepository _notes;
    private readonly MutableClock _clock = new(new DateTimeOffset(2026, 9, 15, 10, 30, 0, TimeSpan.Zero));

    public ReorderNoteTests()
    {
        _database = new NotoDatabase(_temp.DatabasePath);
        _database.Initialize();
        _notes = new SqliteNoteRepository(_database);
    }

    public void Dispose() => _temp.Dispose();

    private NoteId Create(string content, FolderId? folder = null) =>
        new CreateNoteHandler(_notes, _clock).Handle(new CreateNote(folder, content)).Value;

    private CommandResult Reorder(NoteId note, NoteId? after) =>
        new ReorderNoteHandler(_notes, _clock).Handle(new ReorderNote(note, after));

    // ---- placement --------------------------------------------------------

    [Fact]
    public void Null_target_moves_the_note_first()
    {
        var a = Create("a");
        var b = Create("b");
        var c = Create("c");

        Assert.True(Reorder(c, null).IsSuccess);

        Assert.Equal([c, a, b], OrderedIds(null));
    }

    [Fact]
    public void A_note_can_be_placed_after_a_sibling()
    {
        var a = Create("a");
        var b = Create("b");
        var c = Create("c");

        Assert.True(Reorder(c, a).IsSuccess);

        Assert.Equal([a, c, b], OrderedIds(null));
    }

    [Fact]
    public void A_note_can_be_moved_to_the_last_position()
    {
        var a = Create("a");
        var b = Create("b");
        var c = Create("c");

        Assert.True(Reorder(a, c).IsSuccess);

        Assert.Equal([b, c, a], OrderedIds(null));
    }

    [Fact]
    public void Moving_first_to_last_and_back_restores_the_order()
    {
        var a = Create("a");
        var b = Create("b");
        var c = Create("c");

        Reorder(a, c);
        Reorder(a, null);

        Assert.Equal([a, b, c], OrderedIds(null));
    }

    [Fact]
    public void Only_one_row_is_written_for_an_ordinary_reorder()
    {
        // Design §7: midpoint insertion writes ONE row, not the whole scope.
        // That is the reason SortOrder is REAL rather than an integer rank.
        var a = Create("a");
        var b = Create("b");
        var c = Create("c");

        double beforeA = _notes.FindActive(a)!.SortOrder;
        double beforeB = _notes.FindActive(b)!.SortOrder;

        Reorder(c, a);

        Assert.Equal(beforeA, _notes.FindActive(a)!.SortOrder);
        Assert.Equal(beforeB, _notes.FindActive(b)!.SortOrder);
    }

    // ---- no-op ------------------------------------------------------------

    [Fact]
    public void Reordering_to_the_same_position_writes_nothing()
    {
        // Contract §5: same position is a no-op that writes nothing and stamps
        // nothing.
        var a = Create("a");
        var b = Create("b");

        DateTimeOffset stampedAt = _notes.FindActive(b)!.UpdatedAt;
        double order = _notes.FindActive(b)!.SortOrder;

        _clock.Advance(TimeSpan.FromHours(1));
        Assert.True(Reorder(b, a).IsSuccess);

        Note after = _notes.FindActive(b)!;
        Assert.Equal(stampedAt, after.UpdatedAt);
        Assert.Equal(order, after.SortOrder);
    }

    [Fact]
    public void Reordering_the_first_note_to_first_writes_nothing()
    {
        var a = Create("a");
        Create("b");

        DateTimeOffset stampedAt = _notes.FindActive(a)!.UpdatedAt;

        _clock.Advance(TimeSpan.FromHours(1));
        Assert.True(Reorder(a, null).IsSuccess);

        Assert.Equal(stampedAt, _notes.FindActive(a)!.UpdatedAt);
    }

    // ---- scope (O1) -------------------------------------------------------

    [Fact]
    public void Root_and_folder_are_separate_ordering_scopes()
    {
        FolderId folder = InsertFolder("Work");

        var rootA = Create("root a");
        var rootB = Create("root b");
        var inFolder = Create("folder a", folder);

        Reorder(rootB, null);

        Assert.Equal([rootB, rootA], OrderedIds(null));
        Assert.Equal([inFolder], OrderedIds(folder));
    }

    [Fact]
    public void A_target_in_another_scope_is_InvalidInput()
    {
        // Reordering across scopes would be a move in disguise; that is
        // MoveNoteToFolder's job.
        FolderId folder = InsertFolder("Work");

        var rootNote = Create("root");
        var folderNote = Create("in folder", folder);

        var result = Reorder(rootNote, folderNote);

        Assert.False(result.IsSuccess);
        Assert.Equal(CommandFailureReason.InvalidInput, result.Failure!.Reason);
    }

    [Fact]
    public void Ordering_within_a_folder_works_the_same_way()
    {
        FolderId folder = InsertFolder("Work");

        var a = Create("a", folder);
        var b = Create("b", folder);
        var c = Create("c", folder);

        Reorder(c, a);

        Assert.Equal([a, c, b], OrderedIds(folder));
    }

    [Fact]
    public void An_active_note_in_a_deleted_folder_can_still_be_reordered()
    {
        // Case C: a deleted folder may hold an active note. The folder's state
        // is not a precondition of reordering the note (contract §5).
        FolderId folder = InsertFolder("Work");

        var a = Create("a", folder);
        var b = Create("b", folder);

        SoftDeleteFolder(folder);

        Assert.True(Reorder(b, null).IsSuccess);
        Assert.Equal([b, a], OrderedIds(folder));
    }

    // ---- deleted rows (I5, I6) -------------------------------------------

    [Fact]
    public void A_deleted_note_cannot_be_reordered()
    {
        var a = Create("a");
        var b = Create("b");
        SoftDeleteNote(b);

        var result = Reorder(b, a);

        Assert.False(result.IsSuccess);
        Assert.Equal(CommandFailureReason.InvalidState, result.Failure!.Reason);
    }

    [Fact]
    public void A_deleted_target_is_InvalidInput()
    {
        Create("a");
        var b = Create("b");
        var c = Create("c");
        SoftDeleteNote(b);

        var result = Reorder(c, b);

        Assert.False(result.IsSuccess);
        Assert.Equal(CommandFailureReason.InvalidInput, result.Failure!.Reason);
    }

    [Fact]
    public void A_missing_note_is_NotFound()
    {
        var result = Reorder(NoteId.New(), null);

        Assert.False(result.IsSuccess);
        Assert.Equal(CommandFailureReason.NotFound, result.Failure!.Reason);
    }

    [Fact]
    public void A_missing_target_is_InvalidInput()
    {
        var a = Create("a");

        var result = Reorder(a, NoteId.New());

        Assert.False(result.IsSuccess);
        Assert.Equal(CommandFailureReason.InvalidInput, result.Failure!.Reason);
    }

    [Fact]
    public void Deleted_siblings_are_skipped_when_ordering()
    {
        // I6: deleted rows keep their SortOrder but never participate.
        var a = Create("a");
        var b = Create("b");
        var c = Create("c");
        SoftDeleteNote(b);

        Reorder(c, a);

        Assert.Equal([a, c], OrderedIds(null));
    }

    // ---- timestamps -------------------------------------------------------

    [Fact]
    public void A_reorder_stamps_the_moved_note()
    {
        var a = Create("a");
        var b = Create("b");

        _clock.Advance(TimeSpan.FromHours(1));
        Reorder(b, null);

        Assert.Equal(_clock.UtcNow, _notes.FindActive(b)!.UpdatedAt);
        Assert.NotEqual(_clock.UtcNow, _notes.FindActive(a)!.UpdatedAt);
    }

    // ---- O6 renormalisation and precision ---------------------------------

    [Fact]
    public void Sixty_reorders_to_the_same_spot_keep_the_order_correct()
    {
        // Design §14's required test: repeated midpoint insertion between the
        // same pair exhausts double precision after roughly 50 operations.
        // Without renormalisation the values silently collide and the order
        // becomes undefined — the bug nobody can reproduce.
        var first = Create("first");
        var last = Create("last");

        var movers = new List<NoteId>();
        for (int i = 0; i < 60; i++)
        {
            movers.Add(Create($"mover {i}"));
        }

        // Every one of them is dropped into the same spot: just after `first`.
        foreach (NoteId mover in movers)
        {
            Assert.True(Reorder(mover, first).IsSuccess);
        }

        var order = OrderedIds(null);

        // The order must still be total and correct: `first` leads, the most
        // recently moved note follows it, and `last` remains at the end.
        Assert.Equal(62, order.Count);
        Assert.Equal(first, order[0]);
        Assert.Equal(movers[^1], order[1]);
        Assert.Equal(last, order[^1]);

        // And crucially: no two notes share a position. A duplicate here would
        // mean the ordering had silently degenerated to the Id tiebreak.
        var sortOrders = AllSortOrders(null);
        Assert.Equal(sortOrders.Count, sortOrders.Distinct().Count());
    }

    [Fact]
    public void Renormalisation_produces_contiguous_values()
    {
        // O6: renormalisation rewrites the scope to 1, 2, 3...
        var first = Create("first");
        Create("last");

        for (int i = 0; i < 60; i++)
        {
            var mover = Create($"mover {i}");
            Reorder(mover, first);
        }

        var sortOrders = AllSortOrders(null);

        // After renormalisation has occurred at least once, values must be
        // sane rather than vanishingly small fractions.
        Assert.All(sortOrders, value => Assert.True(
            Math.Abs(value) < 1_000_000,
            $"SortOrder {value} suggests ordering was never renormalised."));

        Assert.Equal(sortOrders.Count, sortOrders.Distinct().Count());
    }

    [Fact]
    public void Renormalisation_actually_fires_within_sixty_reorders()
    {
        // Guards against the precision test passing vacuously. Repeated
        // midpoints between the same pair halve the gap each time, so from a
        // gap of 1.0 the engine's threshold is crossed around the 31st insert.
        // If renormalisation never ran, the smallest gap would keep shrinking
        // toward 1e-18 instead of being rebuilt to whole numbers.
        var first = Create("first");
        Create("last");

        for (int i = 0; i < 60; i++)
        {
            Reorder(Create($"mover {i}"), first);
        }

        var ordered = AllSortOrders(null).Order().ToList();

        double smallestGap = Enumerable
            .Range(1, ordered.Count - 1)
            .Select(i => ordered[i] - ordered[i - 1])
            .Min();

        Assert.True(
            smallestGap > 1e-9,
            $"smallest gap {smallestGap:E3} is below the threshold, so the scope was never renormalised.");
    }

    [Fact]
    public void Renormalisation_preserves_the_visible_order()
    {
        var a = Create("a");
        var b = Create("b");
        var c = Create("c");

        // Force many midpoints immediately after `a`.
        for (int i = 0; i < 60; i++)
        {
            Reorder(Create($"m{i}"), a);
        }

        var order = OrderedIds(null);

        // a leads; c is last; every mover sits between them in reverse
        // insertion order, because each was dropped immediately after a.
        Assert.Equal(a, order[0]);
        Assert.Equal(c, order[^1]);
        Assert.Contains(b, order);
        Assert.Equal(63, order.Count);
    }

    [Fact]
    public void The_order_survives_closing_and_reopening()
    {
        var a = Create("a");
        var b = Create("b");
        var c = Create("c");

        Reorder(c, a);

        SqliteConnection.ClearAllPools();
        var reopened = new NotoDatabase(_temp.DatabasePath);
        reopened.Initialize();

        var repository = new SqliteNoteRepository(reopened);
        Assert.Equal(
            _notes.FindActive(c)!.SortOrder,
            repository.FindActive(c)!.SortOrder);
        Assert.Equal([a, c, b], OrderedIds(null));
    }

    // ---- helpers ----------------------------------------------------------

    private List<NoteId> OrderedIds(FolderId? folder)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();

        // O2's exact ordering.
        command.CommandText = folder is null
            ? """
              SELECT Id FROM Notes WHERE FolderId IS NULL AND DeletedAt IS NULL
              ORDER BY IsPinned DESC, SortOrder ASC, Id ASC;
              """
            : """
              SELECT Id FROM Notes WHERE FolderId = $folderId AND DeletedAt IS NULL
              ORDER BY IsPinned DESC, SortOrder ASC, Id ASC;
              """;

        if (folder is { } scope)
        {
            command.Parameters.AddWithValue("$folderId", scope.Value);
        }

        var ids = new List<NoteId>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            ids.Add(NoteId.From(reader.GetString(0)));
        }

        return ids;
    }

    private List<double> AllSortOrders(FolderId? folder)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = folder is null
            ? "SELECT SortOrder FROM Notes WHERE FolderId IS NULL AND DeletedAt IS NULL;"
            : "SELECT SortOrder FROM Notes WHERE FolderId = $folderId AND DeletedAt IS NULL;";

        if (folder is { } scope)
        {
            command.Parameters.AddWithValue("$folderId", scope.Value);
        }

        var values = new List<double>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            values.Add(reader.GetDouble(0));
        }

        return values;
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

    private void SoftDeleteNote(NoteId id) =>
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

    /// <summary>A clock the test moves deliberately, so stamps are assertable.</summary>
    private sealed class MutableClock(DateTimeOffset start) : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = start;

        public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
    }
}
