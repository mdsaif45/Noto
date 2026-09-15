using Noto.Core.Tags;
using Xunit;

namespace Noto.Core.Tests.Tags;

/// <summary>
/// The tag-name normalisation rule frozen in core-note-engine-contract.md §7a
/// (APPROVED, U12a).
/// </summary>
/// <remarks>
/// <para>
/// Tested here, in Core, because the rule is pure: no database, no clock, no
/// repository. The Infrastructure suite proves the <i>handlers</i> apply it;
/// this proves the rule itself, at the layer it lives in — the same split the
/// title algorithm already uses (§3, <c>NoteTitleTests</c>).
/// </para>
/// <para>
/// The rule matters because the database cannot enforce it.
/// <c>UX_Tags_Name ... COLLATE NOCASE</c> makes <c>'work'</c> and <c>'Work'</c>
/// collide, but no collation removes whitespace — so without normalisation the
/// engine would admit tags indistinguishable in any list the user sees.
/// </para>
/// </remarks>
public sealed class TagNameTests
{
    // ---- names that need no change ---------------------------------------

    [Theory]
    [InlineData("Work")]
    [InlineData("work")]
    [InlineData("WORK")]
    [InlineData("w")]
    [InlineData("2026")]
    public void An_already_normalised_name_is_returned_unchanged(string name)
    {
        Assert.Equal(name, TagName.Normalise(name));
    }

    [Fact]
    public void Case_is_never_altered()
    {
        // Normalisation is about whitespace only. Case-insensitivity is a
        // COMPARISON rule enforced by the column's collation, not a
        // transformation — a tag stored as "Work" must not come back "work".
        Assert.Equal("WoRk", TagName.Normalise("WoRk"));
    }

    // ---- trimming ---------------------------------------------------------

    [Theory]
    [InlineData(" Work", "Work")]
    [InlineData("   Work", "Work")]
    [InlineData("\tWork", "Work")]
    [InlineData("\nWork", "Work")]
    public void Leading_whitespace_is_trimmed(string input, string expected)
    {
        Assert.Equal(expected, TagName.Normalise(input));
    }

    [Theory]
    [InlineData("Work ", "Work")]
    [InlineData("Work   ", "Work")]
    [InlineData("Work\t", "Work")]
    [InlineData("Work\r\n", "Work")]
    public void Trailing_whitespace_is_trimmed(string input, string expected)
    {
        Assert.Equal(expected, TagName.Normalise(input));
    }

    [Theory]
    [InlineData(" Work ", "Work")]
    [InlineData("   Work   ", "Work")]
    [InlineData("\t Work \r\n", "Work")]
    public void Whitespace_on_both_sides_is_trimmed(string input, string expected)
    {
        Assert.Equal(expected, TagName.Normalise(input));
    }

    // ---- internal whitespace is NOT touched -------------------------------

    [Theory]
    [InlineData("Work Item")]
    [InlineData("Work  Item")]
    [InlineData("a b c")]
    [InlineData("Work\tItem")]
    public void Internal_whitespace_is_preserved_exactly(string name)
    {
        // §7a, "not in scope": collapsing runs of spaces would change a name
        // the user chose, and would make "Work Item" and "Work  Item" collide.
        Assert.Equal(name, TagName.Normalise(name));
    }

    [Fact]
    public void The_outside_is_trimmed_while_the_inside_is_left_alone()
    {
        // Both halves of the rule in one case, so a normaliser that trims
        // everything cannot pass.
        Assert.Equal("Work  Item", TagName.Normalise("   Work  Item   "));
    }

    // ---- nothing usable ---------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData(" \t \r\n ")]
    public void A_name_that_is_empty_after_trimming_returns_null(string name)
    {
        // null means "not a usable name". Returning it rather than "" is what
        // makes normalisation and validation one step: a caller cannot persist
        // an empty name by forgetting a separate emptiness check.
        Assert.Null(TagName.Normalise(name));
    }

    [Fact]
    public void Null_input_returns_null()
    {
        // The parameter is declared nullable, so null is a legal input rather
        // than a programming error — it lands on the same InvalidInput branch
        // as an empty name.
        Assert.Null(TagName.Normalise(null));
    }

    // ---- the property the rule exists for ---------------------------------

    [Theory]
    [InlineData("Work", " Work")]
    [InlineData("Work", "Work ")]
    [InlineData("Work", "  Work  ")]
    [InlineData("Work Item", " Work Item ")]
    public void Names_differing_only_by_surrounding_whitespace_normalise_alike(
        string first, string second)
    {
        // The invariant the duplicate check depends on: if two inputs normalise
        // to the same string, the uniqueness comparison must see them as the
        // same tag.
        Assert.Equal(TagName.Normalise(first), TagName.Normalise(second));
    }

    [Fact]
    public void Normalisation_is_idempotent()
    {
        // Normalising a stored name must be a no-op, or a rename round-trip
        // could drift.
        string once = TagName.Normalise("  Work  Item  ")!;

        Assert.Equal(once, TagName.Normalise(once));
    }
}
