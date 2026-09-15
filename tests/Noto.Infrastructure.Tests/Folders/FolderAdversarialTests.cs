using Noto.Core.Commands;
using Noto.Core.Folders;
using Noto.Core.Notes;
using Noto.UseCases.Folders;
using Noto.UseCases.Notes;
using Xunit;

namespace Noto.Infrastructure.Tests.Folders;

/// <summary>
/// Sensitivity tests for the Slice 4 mistakes that are easy to make and hard to
/// see.
/// </summary>
/// <remarks>
/// <para>
/// Each test here targets a <b>nearest plausible wrong implementation</b> — one
/// that looks right, passes the obvious tests, and differs from the correct
/// version by a single clause. A test that only fails against an obviously
/// broken implementation proves very little.
/// </para>
/// <para>
/// Where a test corresponds to a specific wrong version, the comment names it,
/// so a later reader can reintroduce that mistake and check the test still
/// fails.
/// </para>
/// </remarks>
public sealed class FolderAdversarialTests : IDisposable
{
    private readonly FolderTestContext _context = new();

    public void Dispose() => _context.Dispose();

    private DeleteFolderHandler Delete() => new(_context.Folders, _context.Clock);

    private RestoreFolderHandler Restore() => new(_context.Folders, _context.Clock);

    private ReorderFolderHandler Reorder() => new(_context.Folders, _context.Clock);

    private CreateFolderHandler Create() => new(_context.Folders, _context.Clock);

    // ------------------------------------------------------------------
    // 1. Delete cascade without `AND DeletedAt IS NULL`
    // ------------------------------------------------------------------

    [Fact]
    public void Delete_cascade_must_not_overwrite_an_earlier_deletion()
    {
        // Wrong version:
        //   UPDATE Notes SET DeletedAt = $at WHERE FolderId = $folderId;
        //
        // It deletes the right rows and passes Case A. What it also does is
        // rewrite the DeletedAt of a note the user binned last week to today,
        // destroying the only record of when that happened.
        var folder = _context.SeedFolder();
        DateTimeOffset longAgo = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var old = _context.SeedNote(folder, deletedAt: longAgo);

        _context.Clock.Advance(TimeSpan.FromDays(30));

        Delete().Handle(new DeleteFolder(folder));

        Assert.Equal(longAgo.ToString("O"), _context.DeletedAtOf(old));
    }

    // ------------------------------------------------------------------
    // 2. Restore that only restores what the folder's deletion cascaded
    // ------------------------------------------------------------------

    [Fact]
    public void Restore_must_not_require_provenance_to_decide_what_comes_back()
    {
        // Wrong version: any scheme that distinguishes "deleted with the
        // folder" from "deleted on its own" — a DeletedWithFolderId column, or
        // matching DeletedAt against the folder's own DeletedAt:
        //
        //   UPDATE Notes SET DeletedAt = NULL
        //   WHERE FolderId = $f AND DeletedAt = (SELECT DeletedAt FROM ...);
        //
        // That passes the simple round-trip test, because a cascade-deleted
        // note shares the folder's timestamp. It fails here, where the note was
        // deleted an hour earlier and carries a different one.
        var folder = _context.SeedFolder();
        var independent = _context.SeedNote(folder);

        _context.Notes.SoftDelete(independent, _context.Clock.UtcNow);
        _context.Clock.Advance(TimeSpan.FromHours(1));

        var cascaded = _context.SeedNote(folder);
        Delete().Handle(new DeleteFolder(folder));

        Assert.NotEqual(_context.DeletedAtOf(independent), _context.DeletedAtOf(cascaded));

        _context.Clock.Advance(TimeSpan.FromHours(1));
        Restore().Handle(new RestoreFolder(folder));

        Assert.Null(_context.DeletedAtOf(independent));
        Assert.Null(_context.DeletedAtOf(cascaded));
    }

    [Fact]
    public void No_provenance_column_exists_on_notes()
    {
        // deletion-semantics §5 forbids DeletedWithFolderId or any equivalent.
        // Asserted against the schema so that adding one is a test failure
        // rather than a code-review catch.
        using var connection = _context.Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM pragma_table_info('Notes');";

        var columns = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            columns.Add(reader.GetString(0));
        }

        Assert.DoesNotContain("DeletedWithFolderId", columns);
        Assert.DoesNotContain(columns, c => c.Contains("DeletedWith", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(columns, c => c.Contains("DeletedBy", StringComparison.OrdinalIgnoreCase));
    }

    // ------------------------------------------------------------------
    // 3. Clock called once per row instead of once per transaction
    // ------------------------------------------------------------------

    [Fact]
    public void Delete_cascade_reads_the_clock_exactly_once()
    {
        // Wrong version: the repository calls _clock.UtcNow inside the loop,
        // or the handler passes the clock rather than an instant. With a real
        // clock the stamps differ by microseconds and every equality assertion
        // still passes by luck of resolution — so the clock here CHANGES on
        // every read, which makes the mistake deterministic instead of flaky.
        var counting = new CountingClock(_context.Clock.UtcNow);
        var handler = new DeleteFolderHandler(_context.Folders, counting);

        var folder = _context.SeedFolder();
        var notes = Enumerable.Range(0, 4).Select(_ => _context.SeedNote(folder)).ToList();

        handler.Handle(new DeleteFolder(folder));

        Assert.Equal(1, counting.Reads);

        string stamp = _context.DeletedAtOf(folder)!;
        Assert.All(notes, note => Assert.Equal(stamp, _context.DeletedAtOf(note)));
    }

    [Fact]
    public void Restore_cascade_reads_the_clock_exactly_once()
    {
        var folder = _context.SeedFolder();
        var notes = Enumerable.Range(0, 4).Select(_ => _context.SeedNote(folder)).ToList();

        Delete().Handle(new DeleteFolder(folder));

        var counting = new CountingClock(_context.Clock.UtcNow.AddHours(1));
        var handler = new RestoreFolderHandler(_context.Folders, counting);

        handler.Handle(new RestoreFolder(folder));

        Assert.Equal(1, counting.Reads);

        string stamp = (string)_context.ReadFolderColumn(folder, "UpdatedAt")!;
        Assert.All(
            notes,
            note => Assert.Equal(stamp, (string)_context.ReadNoteColumn(note, "UpdatedAt")!));
    }

    // ------------------------------------------------------------------
    // 4. Pin/Unpin changing SortOrder — covered in PinFolderTests; the
    //    schema-level guard lives here.
    // ------------------------------------------------------------------

    [Fact]
    public void The_pin_statement_cannot_touch_sort_order()
    {
        // Stronger than asserting one folder's value: every folder in the
        // collection keeps its exact SortOrder across a pin and an unpin.
        var ids = Enumerable.Range(0, 5)
            .Select(i => _context.SeedFolder($"f{i}", sortOrder: i * 10d))
            .ToList();

        var before = ids.ToDictionary(id => id, _context.SortOrderOf);

        var pin = new PinFolderHandler(_context.Folders, _context.Clock);
        var unpin = new UnpinFolderHandler(_context.Folders, _context.Clock);

        foreach (FolderId id in ids)
        {
            pin.Handle(new PinFolder(id));
            unpin.Handle(new UnpinFolder(id));
        }

        foreach (FolderId id in ids)
        {
            Assert.Equal(before[id], _context.SortOrderOf(id));
        }
    }

    // ------------------------------------------------------------------
    // 4b. Pin/Unpin that writes when already in the requested state
    //
    // This is the mistake the original mutation suite MISSED. It shipped, and
    // an independent review caught it: the handlers had no already-in-state
    // check at all, so every redundant pin restamped UpdatedAt. The tests
    // covering that path asserted only success and IsPinned, so they passed.
    // ------------------------------------------------------------------

    [Fact]
    public void A_redundant_pin_must_not_write()
    {
        // Wrong version — the one that actually shipped:
        //
        //   if (!Guard.TryEnsureActive(...)) return failure;
        //   _folders.SetPinned(id, isPinned: true, _clock.UtcNow);   // always
        //
        // It is a plausible implementation: it pins, it guards the lifecycle,
        // and pinning twice still succeeds. What it breaks is §4's rule that a
        // command changing no row stamps nothing — observable through C10's
        // "sort by modified".
        var id = _context.SeedFolder(pinned: true);
        string before = _context.UpdatedAtOf(id);

        _context.Clock.Advance(TimeSpan.FromDays(1));

        Assert.True(new PinFolderHandler(_context.Folders, _context.Clock)
            .Handle(new PinFolder(id)).IsSuccess);

        Assert.Equal(before, _context.UpdatedAtOf(id));
    }

    [Fact]
    public void A_redundant_unpin_must_not_write()
    {
        var id = _context.SeedFolder(pinned: false);
        string before = _context.UpdatedAtOf(id);

        _context.Clock.Advance(TimeSpan.FromDays(1));

        Assert.True(new UnpinFolderHandler(_context.Folders, _context.Clock)
            .Handle(new UnpinFolder(id)).IsSuccess);

        Assert.Equal(before, _context.UpdatedAtOf(id));
    }

    [Fact]
    public void The_no_op_check_must_not_come_before_the_lifecycle_check()
    {
        // The other way to get this wrong: short-circuit on the flag first, so
        // pinning an already-pinned DELETED folder returns success instead of
        // InvalidState. §7 fixes the order — I5 is evaluated before
        // idempotency — and a deleted entity is inert either way.
        var deletedPinned = _context.SeedFolder(pinned: true, deletedAt: _context.Clock.UtcNow);
        var deletedUnpinned = _context.SeedFolder(pinned: false, deletedAt: _context.Clock.UtcNow);

        var pin = new PinFolderHandler(_context.Folders, _context.Clock);
        var unpin = new UnpinFolderHandler(_context.Folders, _context.Clock);

        Assert.Equal(
            CommandFailureReason.InvalidState,
            pin.Handle(new PinFolder(deletedPinned)).Failure!.Reason);

        Assert.Equal(
            CommandFailureReason.InvalidState,
            unpin.Handle(new UnpinFolder(deletedUnpinned)).Failure!.Reason);
    }

    [Fact]
    public void A_real_pin_still_stamps()
    {
        // The guard against "fixing" the no-op by never stamping at all.
        var id = _context.SeedFolder(pinned: false);

        DateTimeOffset pinnedAt = _context.Clock.Advance(TimeSpan.FromHours(1));

        new PinFolderHandler(_context.Folders, _context.Clock).Handle(new PinFolder(id));

        Assert.True(_context.IsPinnedOf(id));
        Assert.Equal(pinnedAt.ToString("O"), _context.UpdatedAtOf(id));
    }

    // ------------------------------------------------------------------
    // 7. Accidental folder-name uniqueness
    // ------------------------------------------------------------------

    [Fact]
    public void The_schema_has_no_unique_index_on_folder_name()
    {
        // Case G depends on this. A UNIQUE index would turn a supported state
        // into a StorageException at the worst possible moment — during a
        // restore, which the user cannot then complete.
        using var connection = _context.Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(*) FROM sqlite_master
            WHERE type = 'index' AND tbl_name = 'Folders'
              AND sql IS NOT NULL AND UPPER(sql) LIKE '%UNIQUE%';
            """;

        Assert.Equal(0L, (long)command.ExecuteScalar()!);
    }

    [Fact]
    public void Many_folders_may_share_one_name()
    {
        for (int i = 0; i < 5; i++)
        {
            Assert.True(Create().Handle(new CreateFolder("Same")).IsSuccess);
        }

        Assert.Equal(5, _context.ActiveFolderOrder().Count);
    }

    [Fact]
    public void Duplicate_name_is_never_returned_by_any_folder_command()
    {
        // DuplicateName exists in the failure model for entities that require
        // uniqueness. No folder command may produce it.
        var a = _context.SeedFolder("Same");
        var b = _context.SeedFolder("Same");

        var rename = new RenameFolderHandler(_context.Folders, _context.Clock);
        var results = new List<CommandResult>
        {
            rename.Handle(new RenameFolder(a, "Same")),
            rename.Handle(new RenameFolder(b, "Same")),
            Reorder().Handle(new ReorderFolder(a, b)),
            Delete().Handle(new DeleteFolder(a)),
            Restore().Handle(new RestoreFolder(a)),
        };

        Assert.DoesNotContain(
            results,
            r => r.Failure?.Reason == CommandFailureReason.DuplicateName);
    }

    // ------------------------------------------------------------------
    // 8. Folder work changing note ordering behaviour
    // ------------------------------------------------------------------

    [Fact]
    public void Note_ordering_is_unaffected_by_folder_ordering()
    {
        // The two share one engine, so a folder-shaped assumption leaking into
        // it would show up here: notes are scoped per folder (O1), folders are
        // a single scope. Interleaving both must not disturb either.
        var folderA = _context.SeedFolder("A", sortOrder: 1);
        var folderB = _context.SeedFolder("B", sortOrder: 2);

        var createNote = new CreateNoteHandler(_context.Notes, _context.Clock);

        var inA = Enumerable.Range(0, 3)
            .Select(i => createNote.Handle(new CreateNote(folderA, $"a{i}")).Value)
            .ToList();
        var inB = Enumerable.Range(0, 3)
            .Select(i => createNote.Handle(new CreateNote(folderB, $"b{i}")).Value)
            .ToList();
        var atRoot = Enumerable.Range(0, 3)
            .Select(i => createNote.Handle(new CreateNote(null, $"r{i}")).Value)
            .ToList();

        // Reorder the folders around the notes.
        Reorder().Handle(new ReorderFolder(folderB, null));
        Reorder().Handle(new ReorderFolder(folderA, folderB));

        // Each note scope is still independent and still ordered.
        Assert.Equal(inA, NoteOrderIn(folderA));
        Assert.Equal(inB, NoteOrderIn(folderB));
        Assert.Equal(atRoot, NoteOrderIn(null));
    }

    [Fact]
    public void Folder_ordering_does_not_renumber_notes()
    {
        // Renormalisation rewrites a SCOPE. If the folder scope were expressed
        // loosely enough to match note rows, an O6 rewrite would silently
        // renumber every note in the database.
        var folder = _context.SeedFolder("A", sortOrder: 1);
        var createNote = new CreateNoteHandler(_context.Notes, _context.Clock);

        var notes = Enumerable.Range(0, 4)
            .Select(i => createNote.Handle(new CreateNote(folder, $"n{i}")).Value)
            .ToList();

        var before = notes.ToDictionary(
            id => id,
            id => Convert.ToDouble(
                _context.ReadNoteColumn(id, "SortOrder"),
                System.Globalization.CultureInfo.InvariantCulture));

        // Force folder renormalisation by exhausting the gap.
        var first = _context.SeedFolder("first", sortOrder: 1);
        for (int i = 0; i < 60; i++)
        {
            var mover = _context.SeedFolder($"m{i}", sortOrder: 500 + i);
            Reorder().Handle(new ReorderFolder(mover, first));
        }

        foreach (NoteId id in notes)
        {
            Assert.Equal(
                before[id],
                Convert.ToDouble(
                    _context.ReadNoteColumn(id, "SortOrder"),
                    System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    // ------------------------------------------------------------------
    // 10. Same-position reorder writing something
    // ------------------------------------------------------------------

    [Fact]
    public void A_repeated_no_op_reorder_never_writes()
    {
        // Run the no-op many times with the clock moving. An implementation
        // that writes would leave a trail of UpdatedAt values.
        var a = _context.SeedFolder("A", sortOrder: 1);
        var b = _context.SeedFolder("B", sortOrder: 2);

        string beforeA = _context.UpdatedAtOf(a);
        string beforeB = _context.UpdatedAtOf(b);

        for (int i = 0; i < 10; i++)
        {
            _context.Clock.Advance(TimeSpan.FromMinutes(1));
            Assert.True(Reorder().Handle(new ReorderFolder(b, a)).IsSuccess);
        }

        Assert.Equal(beforeA, _context.UpdatedAtOf(a));
        Assert.Equal(beforeB, _context.UpdatedAtOf(b));
    }

    [Fact]
    public void The_repository_reports_a_no_op_as_false()
    {
        // The signal the handler relies on, asserted directly.
        var a = _context.SeedFolder("A", sortOrder: 1);
        var b = _context.SeedFolder("B", sortOrder: 2);

        Assert.False(_context.Folders.Reorder(b, FolderPlacement.After(a), _context.Clock.UtcNow));
        Assert.True(_context.Folders.Reorder(b, FolderPlacement.First, _context.Clock.UtcNow));
    }

    private List<NoteId> NoteOrderIn(FolderId? folderId)
    {
        using var connection = _context.Database.OpenConnection();
        using var command = connection.CreateCommand();

        command.CommandText = folderId is null
            ? """
              SELECT Id FROM Notes
              WHERE FolderId IS NULL AND DeletedAt IS NULL
              ORDER BY SortOrder ASC, Id ASC;
              """
            : """
              SELECT Id FROM Notes
              WHERE FolderId = $folderId AND DeletedAt IS NULL
              ORDER BY SortOrder ASC, Id ASC;
              """;

        if (folderId is { } id)
        {
            command.Parameters.AddWithValue("$folderId", id.Value);
        }

        var ids = new List<NoteId>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            ids.Add(NoteId.From(reader.GetString(0)));
        }

        return ids;
    }

    /// <summary>
    /// A clock that counts reads and returns a different instant each time.
    /// </summary>
    /// <remarks>
    /// Moving on every read is what makes "called once per row" fail
    /// deterministically. A clock that returned a constant would let the
    /// mistake pass.
    /// </remarks>
    private sealed class CountingClock(DateTimeOffset start) : Noto.Core.IClock
    {
        private DateTimeOffset _now = start;

        public int Reads { get; private set; }

        public DateTimeOffset UtcNow
        {
            get
            {
                Reads++;
                _now = _now.AddSeconds(1);
                return _now;
            }
        }
    }
}
