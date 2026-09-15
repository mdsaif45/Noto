using Noto.Core.Commands;
using Noto.Core.Notes;
using Noto.Core.Tags;
using Noto.UseCases.Tags;
using Xunit;

namespace Noto.Infrastructure.Tests.Tags;

/// <summary>
/// Sensitivity tests for the Slice 5 mistakes that are easy to make and hard to
/// see.
/// </summary>
/// <remarks>
/// <para>
/// Each targets a <b>nearest plausible wrong implementation</b> — one that
/// looks right and passes the obvious tests. Slice 4 shipped a §4 violation
/// past 428 green tests because the one test on that path asserted the wrong
/// thing and no mutant probed it; these exist so the same class of defect
/// cannot survive here.
/// </para>
/// <para>
/// The normalisation cases matter most. <c>UX_Tags_Name COLLATE NOCASE</c>
/// catches a case duplicate whatever the handler does, so a suite that only
/// tested case would pass against an implementation with no <c>Trim()</c> at
/// all. Only whitespace proves the application-layer rule.
/// </para>
/// </remarks>
public sealed class TagAdversarialTests : IDisposable
{
    private readonly TagTestContext _context = new();

    private static readonly string[] ExpectedNoteTagColumns = ["NoteId", "TagId"];

    public void Dispose() => _context.Dispose();

    private CreateTagHandler Create() => new(_context.Tags, _context.Clock);

    private RenameTagHandler Rename() => new(_context.Tags);

    private DeleteTagHandler Delete() => new(_context.Tags);

    private AssignTagToNoteHandler Assign() => new(_context.Notes, _context.Tags);

    private RemoveTagFromNoteHandler Remove() => new(_context.Notes, _context.Tags);

    // ---- 1, 3: CreateTag without Trim / persisting the raw name ----------

    [Fact]
    public void CreateTag_must_persist_the_trimmed_name()
    {
        // Wrong version: `Name = command.Name` with no normalisation.
        // The database accepts " Work " happily — nothing but this assertion
        // notices.
        var id = Create().Handle(new CreateTag("  Work  ")).Value;

        Assert.Equal("Work", _context.NameOf(id));
        Assert.NotEqual("  Work  ", _context.NameOf(id));
    }

    // ---- 6: duplicate detection before normalisation ---------------------

    [Fact]
    public void CreateTag_must_compare_the_normalised_name()
    {
        // Wrong version: FindIdByName(command.Name) using the RAW value, then
        // storing the trimmed one. It stores correctly and still admits the
        // duplicate, so only a collision test with whitespace catches it.
        Assert.True(Create().Handle(new CreateTag("Work")).IsSuccess);

        var result = Create().Handle(new CreateTag("  Work  "));

        Assert.Equal(CommandFailureReason.DuplicateName, result.Failure!.Reason);
        Assert.Equal(1, _context.TagCount());
    }

    // ---- 2: RenameTag without Trim ---------------------------------------

    [Fact]
    public void RenameTag_must_persist_the_trimmed_name()
    {
        var id = _context.SeedTag("Old");

        Rename().Handle(new RenameTag(id, "  New  "));

        Assert.Equal("New", _context.NameOf(id));
    }

    [Fact]
    public void RenameTag_must_compare_the_normalised_name()
    {
        _context.SeedTag("Taken");
        var id = _context.SeedTag("Mine");

        var result = Rename().Handle(new RenameTag(id, "  Taken  "));

        Assert.Equal(CommandFailureReason.DuplicateName, result.Failure!.Reason);
        Assert.Equal("Mine", _context.NameOf(id));
    }

    // ---- 4: whitespace-only accepted -------------------------------------

    [Fact]
    public void A_whitespace_only_name_must_never_reach_the_database()
    {
        // Wrong version: checking `string.IsNullOrEmpty` instead of
        // `IsNullOrWhiteSpace`, which lets "   " through and stores "" after
        // trimming — a nameless tag the user cannot identify or select.
        foreach (string name in new[] { " ", "\t", "\r\n", "    " })
        {
            Assert.Equal(
                CommandFailureReason.InvalidInput,
                Create().Handle(new CreateTag(name)).Failure!.Reason);
        }

        Assert.Equal(0, _context.TagCount());
    }

    // ---- 5: internal whitespace collapsed --------------------------------

    [Fact]
    public void Internal_whitespace_must_survive_normalisation()
    {
        // Wrong version: a "tidy" normaliser that also collapses runs of
        // spaces. §7a excludes that explicitly — it would change a name the
        // user chose, and make "Work Item" and "Work  Item" collide.
        var single = Create().Handle(new CreateTag("Work Item")).Value;
        var doubled = Create().Handle(new CreateTag("Work  Item")).Value;

        Assert.Equal("Work Item", _context.NameOf(single));
        Assert.Equal("Work  Item", _context.NameOf(doubled));
        Assert.Equal(2, _context.TagCount());
    }

    // ---- 7: uniqueness made case-sensitive -------------------------------

    [Fact]
    public void Uniqueness_must_stay_case_insensitive()
    {
        // Wrong version: comparing with BINARY collation, or in C# with an
        // ordinal comparison, instead of relying on the column's NOCASE.
        Assert.True(Create().Handle(new CreateTag("Work")).IsSuccess);

        Assert.Equal(
            CommandFailureReason.DuplicateName,
            Create().Handle(new CreateTag("work")).Failure!.Reason);
        Assert.Equal(
            CommandFailureReason.DuplicateName,
            Create().Handle(new CreateTag("WORK")).Failure!.Reason);

        Assert.Equal(1, _context.TagCount());
    }

    // ---- 8: RenameTag rejects its own name -------------------------------

    [Fact]
    public void RenameTag_must_not_treat_a_tag_as_colliding_with_itself()
    {
        // Wrong version: `if (FindIdByName(name) is not null) return
        // DuplicateName;` without excluding this tag. It looks right and
        // breaks a case-only rename — "work" to "Work" — which is a real edit.
        var id = _context.SeedTag("work");

        var result = Rename().Handle(new RenameTag(id, "Work"));

        Assert.True(result.IsSuccess);
        Assert.Equal("Work", _context.NameOf(id));
    }

    // ---- 9, 10, 11: DeleteTag ---------------------------------------------

    [Fact]
    public void DeleteTag_must_remove_the_row_not_flag_it()
    {
        var id = _context.SeedTag("Work");

        Delete().Handle(new DeleteTag(id));

        Assert.False(_context.TagExists(id));
        Assert.Null(_context.Tags.Find(id));
    }

    [Fact]
    public void DeleteTag_must_not_leave_relationships_behind()
    {
        // This asserts the OUTCOME the contract specifies (§11 row 21: "removes
        // NoteTags, leaves notes intact"), not the mechanism.
        //
        // Known limit, recorded rather than worked around: it cannot tell an
        // explicit `DELETE FROM NoteTags` from a repository that deletes only
        // the tag and lets the foreign key cascade. Measured — a tag-only
        // delete leaves 0 join rows with foreign_keys=ON and 1 with OFF, and
        // NotoDatabase.ApplyPragmas always enables them, so both
        // implementations satisfy the contract identically here.
        //
        // The repository does the deletion explicitly anyway, so the rule does
        // not rest on a pragma. That is an implementation preference, not a
        // contract requirement, and is deliberately not pinned by a test: an
        // earlier version asserted the exact SQL string and would have failed a
        // correct implementation over a renamed parameter.
        var tag = _context.SeedTag("Work");
        var note = _context.SeedNote();
        _context.SeedRelationship(note, tag);

        Delete().Handle(new DeleteTag(tag));

        Assert.Equal(0, _context.RelationshipCount());
    }

    [Fact]
    public void DeleteTag_must_not_delete_notes()
    {
        // Wrong version: cascading the wrong direction, or deleting notes
        // joined to the tag. The notes are content; the tag is a label.
        var tag = _context.SeedTag("Work");
        var notes = Enumerable.Range(0, 3).Select(_ => _context.SeedNote()).ToList();

        foreach (NoteId note in notes)
        {
            _context.SeedRelationship(note, tag);
        }

        Delete().Handle(new DeleteTag(tag));

        Assert.All(notes, note => Assert.True(_context.NoteExists(note)));
        Assert.All(notes, note => Assert.NotNull(_context.Notes.FindActive(note)));
    }

    // ---- 12, 13: AssignTagToNote -----------------------------------------

    [Fact]
    public void Assign_must_not_insert_when_the_relationship_exists()
    {
        // Wrong version: an unconditional INSERT OR IGNORE. It never throws and
        // the row count stays at one, so the ONLY observable difference is that
        // a write happened — which this catches by pinning the note's stamp,
        // the same shape as the Slice 4 pin defect.
        var note = _context.SeedNote();
        var tag = _context.SeedTag("Work");
        _context.SeedRelationship(note, tag);

        string before = _context.UpdatedAtOf(note);
        _context.Clock.Advance(TimeSpan.FromDays(1));

        Assert.True(Assign().Handle(new AssignTagToNote(note, tag)).IsSuccess);

        Assert.Equal(1, _context.RelationshipCount());
        Assert.Equal(before, _context.UpdatedAtOf(note));
    }

    [Fact]
    public void Assign_must_never_touch_note_updated_at()
    {
        // Wrong version: "tagging modifies the note, so stamp it" — plausible,
        // and wrong. §4 says the note row is not changed. This would surface
        // in C10's "sort by modified" as notes jumping on every tag edit.
        var note = _context.SeedNote();
        var first = _context.SeedTag("A");
        var second = _context.SeedTag("B");

        string before = _context.UpdatedAtOf(note);

        for (int i = 0; i < 3; i++)
        {
            _context.Clock.Advance(TimeSpan.FromHours(1));
            Assign().Handle(new AssignTagToNote(note, i % 2 == 0 ? first : second));
        }

        Assert.Equal(before, _context.UpdatedAtOf(note));
    }

    // ---- 14, 15: Assign skipping validation ------------------------------

    [Fact]
    public void Assign_must_not_skip_the_deleted_note_check()
    {
        var note = _context.SeedNote(deletedAt: _context.Clock.UtcNow);
        var tag = _context.SeedTag("Work");

        var result = Assign().Handle(new AssignTagToNote(note, tag));

        Assert.Equal(CommandFailureReason.InvalidState, result.Failure!.Reason);
        Assert.Equal(0, _context.RelationshipCount());
    }

    [Fact]
    public void Assign_must_not_skip_the_tag_existence_check()
    {
        // Without it the INSERT hits the foreign key and surfaces as a
        // StorageException — an infrastructure fault for what the contract
        // says is a business failure (§6 keeps the two channels apart).
        var note = _context.SeedNote();

        var result = Assign().Handle(new AssignTagToNote(note, TagId.New()));

        Assert.False(result.IsSuccess);
        Assert.Equal(CommandFailureReason.NotFound, result.Failure!.Reason);
    }

    // ---- 16, 17: RemoveTagFromNote ---------------------------------------

    [Fact]
    public void Remove_must_not_validate_the_tag()
    {
        // U12b. Wrong version: copying Assign's ladder wholesale, which adds a
        // tag check Remove must not have. Symmetry is the intuitive choice and
        // the contract explicitly rejects it.
        var note = _context.SeedNote();

        var result = Remove().Handle(new RemoveTagFromNote(note, TagId.New()));

        Assert.True(result.IsSuccess);
        Assert.Null(result.Failure);
    }

    [Fact]
    public void Remove_must_not_report_not_found_for_an_absent_relationship()
    {
        // Wrong version: treating "nothing to delete" as a failure. It makes
        // the command unsafe to retry and reports failure for the state the
        // caller asked for.
        var note = _context.SeedNote();
        var tag = _context.SeedTag("Work");

        var result = Remove().Handle(new RemoveTagFromNote(note, tag));

        Assert.True(result.IsSuccess);
        Assert.Null(result.Failure);
    }

    // ---- 18, 19: Remove stamping / skipping validation --------------------

    [Fact]
    public void Remove_must_never_touch_note_updated_at()
    {
        var note = _context.SeedNote();
        var tag = _context.SeedTag("Work");
        _context.SeedRelationship(note, tag);

        string before = _context.UpdatedAtOf(note);
        _context.Clock.Advance(TimeSpan.FromDays(1));

        Remove().Handle(new RemoveTagFromNote(note, tag));

        Assert.Equal(before, _context.UpdatedAtOf(note));
    }

    [Fact]
    public void Remove_must_not_skip_the_deleted_note_check()
    {
        var note = _context.SeedNote();
        var tag = _context.SeedTag("Work");
        _context.SeedRelationship(note, tag);

        _context.Notes.SoftDelete(note, _context.Clock.UtcNow);

        var result = Remove().Handle(new RemoveTagFromNote(note, tag));

        Assert.Equal(CommandFailureReason.InvalidState, result.Failure!.Reason);
        Assert.Equal(1, _context.RelationshipCount());
    }

    // ---- 20: DeleteTag atomicity ------------------------------------------

    [Fact]
    public void DeleteTag_leaves_no_half_state()
    {
        // Both halves land together. A non-transactional version could be
        // interrupted between them, leaving join rows pointing at a tag that
        // no longer exists — unreachable and invisible.
        var tag = _context.SeedTag("Work");
        var notes = Enumerable.Range(0, 4).Select(_ => _context.SeedNote()).ToList();

        foreach (NoteId note in notes)
        {
            _context.SeedRelationship(note, tag);
        }

        Delete().Handle(new DeleteTag(tag));

        bool tagGone = !_context.TagExists(tag);
        bool joinsGone = _context.RelationshipCount() == 0;

        Assert.True(tagGone && joinsGone, "the tag and its relationships must go together");
        Assert.All(notes, note => Assert.True(_context.NoteExists(note)));
    }

    // ---- the no-timestamp rule, asserted structurally ---------------------

    [Fact]
    public void NoteTags_has_no_timestamp_columns()
    {
        // The reason relationship commands cannot stamp: there is nowhere to
        // stamp. Asserted so that adding a column is a test failure rather
        // than a review catch.
        using var connection = _context.Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM pragma_table_info('NoteTags');";

        var columns = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            columns.Add(reader.GetString(0));
        }

        Assert.Equal(ExpectedNoteTagColumns, columns);
    }
}
