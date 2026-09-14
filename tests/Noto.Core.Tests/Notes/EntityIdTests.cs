using Noto.Core.Folders;
using Noto.Core.Identifiers;
using Noto.Core.Notes;
using Noto.Core.Tags;
using Xunit;

namespace Noto.Core.Tests.Notes;

/// <summary>
/// Strongly typed ids (ADR-012, design §5).
/// </summary>
/// <remarks>
/// The point of these types is that <c>MoveNoteToFolder(noteId, folderId)</c>
/// cannot be called with its arguments swapped. That guarantee is worth a test,
/// because it is the whole reason for not using <c>string</c>.
/// </remarks>
public sealed class EntityIdTests
{
    [Fact]
    public void New_ids_are_valid_ulids()
    {
        Assert.True(Ulid.IsValid(NoteId.New().Value));
        Assert.True(Ulid.IsValid(FolderId.New().Value));
        Assert.True(Ulid.IsValid(TagId.New().Value));
    }

    [Fact]
    public void New_ids_are_unique()
    {
        var ids = Enumerable.Range(0, 1_000).Select(_ => NoteId.New()).ToHashSet();

        Assert.Equal(1_000, ids.Count);
    }

    [Fact]
    public void Ids_are_creation_ordered()
    {
        // ULIDs sort by creation time (ADR-012), which is what makes the
        // ordering tiebreak in design §7 rule 2 deterministic rather than
        // arbitrary.
        var first = NoteId.New();
        var second = NoteId.New();

        Assert.True(string.CompareOrdinal(first.Value, second.Value) < 0);
    }

    [Fact]
    public void Ids_round_trip_through_their_string_form()
    {
        var id = NoteId.New();

        Assert.Equal(id, NoteId.From(id.Value));
    }

    [Fact]
    public void Equality_is_by_value()
    {
        var id = NoteId.New();

        Assert.Equal(NoteId.From(id.Value), id);
        Assert.Equal(NoteId.From(id.Value).GetHashCode(), id.GetHashCode());
        Assert.NotEqual(NoteId.New(), id);
    }

    [Fact]
    public void ToString_is_the_bare_ulid()
    {
        // Persistence and diagnostics both rely on this: a wrapper that printed
        // "NoteId { Value = ... }" would land in a SQL parameter.
        var id = NoteId.New();

        Assert.Equal(id.Value, id.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("too-short")]
    [InlineData("NOT_A_VALID_ULID_0000000000")]
    [InlineData("0123456789012345678901234567890")]
    public void A_malformed_id_throws_rather_than_becoming_a_business_failure(string value)
    {
        // A malformed id is a programming error: no user action produces one.
        // Returning NotFound instead would let a typo masquerade as "no such
        // note" (contract §6).
        Assert.ThrowsAny<ArgumentException>(() => NoteId.From(value));
    }

    [Fact]
    public void A_null_id_throws()
    {
        Assert.ThrowsAny<ArgumentException>(() => NoteId.From(null!));
    }

    [Fact]
    public void The_three_id_types_are_not_interchangeable()
    {
        // The compile-time guarantee, asserted at runtime: the types are
        // distinct, so no implicit conversion exists between them.
        Assert.NotEqual(typeof(NoteId), typeof(FolderId));
        Assert.NotEqual(typeof(NoteId), typeof(TagId));
        Assert.NotEqual(typeof(FolderId), typeof(TagId));

        // Same underlying ULID, different types — and therefore not equal.
        string raw = Ulid.NewId();
        Assert.False(NoteId.From(raw).Equals((object)FolderId.From(raw)));
    }

    [Fact]
    public void IsValid_agrees_with_From()
    {
        string good = Ulid.NewId();

        Assert.True(NoteId.IsValid(good));
        Assert.False(NoteId.IsValid("nope"));
        Assert.False(NoteId.IsValid(null));
    }
}
