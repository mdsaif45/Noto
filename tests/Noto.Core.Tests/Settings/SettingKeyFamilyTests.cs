using Noto.Core.Settings;
using Noto.Core.Workspace;
using Xunit;

namespace Noto.Core.Tests.Settings;

/// <summary>
/// Declared key families and the registry rules that keep them from becoming
/// an arbitrary-key escape hatch (#16 slice 3).
/// </summary>
public sealed class SettingKeyFamilyTests
{
    private static readonly SettingKeyFamily<double> Widths = new("test.width", 360, static v => double.IsFinite(v) && v > 0);

    // ------------------------------------------------------------ naming

    [Fact]
    public void A_member_is_named_prefix_separator_scope()
    {
        Assert.Equal("test.width::display-7", Widths.For("display-7").Name);
    }

    [Fact]
    public void The_same_scope_always_names_the_same_setting()
    {
        SettingKey<double> first = Widths.For(@"\\?\DISPLAY#ABC#{guid}");
        SettingKey<double> second = Widths.For(@"\\?\DISPLAY#ABC#{guid}");

        Assert.Equal(first.Name, second.Name);
    }

    [Fact]
    public void A_scope_is_stored_exactly_as_given()
    {
        // Opaque: not trimmed, not case-folded. Two displays whose identities
        // differ only in case are two displays.
        Assert.NotEqual(Widths.For("Display").Name, Widths.For("display").Name);
        Assert.Equal("test.width:: padded ", Widths.For(" padded ").Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void A_blank_scope_is_rejected(string scope)
    {
        Assert.Throws<ArgumentException>(() => Widths.For(scope));
    }

    [Fact]
    public void A_member_has_the_family_default_and_validity()
    {
        SettingKey<double> member = Widths.For("x");

        Assert.Equal(360, member.Default);
        Assert.True(member.IsValid(100));
        Assert.False(member.IsValid(0));
        Assert.False(member.IsValid(double.NaN));
    }

    // ------------------------------------------------------ declaration

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void A_blank_prefix_is_rejected(string prefix)
    {
        Assert.Throws<ArgumentException>(() => new SettingKeyFamily<double>(prefix, 1));
    }

    [Theory]
    [InlineData("test::width")]
    [InlineData("::")]
    [InlineData("test.width::")]
    public void A_prefix_containing_the_separator_is_rejected(string prefix)
    {
        Assert.Throws<ArgumentException>(() => new SettingKeyFamily<double>(prefix, 1));
    }

    [Fact]
    public void A_family_cannot_be_declared_with_a_default_that_breaks_its_own_rule()
    {
        Assert.Throws<ArgumentException>(() => new SettingKeyFamily<double>("test.bad", -1, static v => v > 0));
    }

    [Fact]
    public void A_family_of_an_unsupported_type_is_rejected_at_declaration()
    {
        Assert.Throws<NotSupportedException>(() => new SettingKeyFamily<DateTimeOffset>("test.when", DateTimeOffset.UnixEpoch));
    }

    // ---------------------------------------------------- membership

    [Fact]
    public void A_stored_name_with_the_prefix_and_a_scope_is_a_member()
    {
        SettingKey? member = Widths.MemberNamed("test.width::display-7");

        Assert.NotNull(member);
        Assert.Equal("test.width::display-7", member.Name);
        Assert.Equal(typeof(double), member.ValueType);
    }

    [Theory]
    [InlineData("test.width")]            // the prefix alone: a static key's name, not a member
    [InlineData("test.width::")]          // no scope
    [InlineData("test.width::   ")]       // blank scope
    [InlineData("test.widths::display")]  // a different prefix that merely starts the same
    [InlineData("other::display")]
    [InlineData("TEST.WIDTH::display")]   // prefixes are case-sensitive, as key names are
    [InlineData("")]
    public void A_name_the_family_does_not_declare_is_not_a_member(string name)
    {
        Assert.Null(Widths.MemberNamed(name));
    }

    [Fact]
    public void Owns_recognises_its_members_and_nothing_else()
    {
        Assert.True(Widths.Owns(Widths.For("a")));
        Assert.True(Widths.Owns(new SettingKey<double>("test.width::b", 1)));

        Assert.False(Widths.Owns(new SettingKey<double>("test.width", 1)));
        Assert.False(Widths.Owns(new SettingKey<double>("test.other::a", 1)));

        // Right name, wrong type: not a member.
        Assert.False(Widths.Owns(new SettingKey<int>("test.width::a", 1)));
    }

    // ------------------------------------------------------- registry

    [Fact]
    public void No_static_key_name_contains_the_separator()
    {
        // Guarantees a static key can never be read as a family member.
        Assert.All(SettingKeys.All, k => Assert.DoesNotContain(SettingKeyFamily.Separator, k.Name, StringComparison.Ordinal));
    }

    [Fact]
    public void Family_prefixes_are_distinct_and_free_of_the_separator()
    {
        string[] prefixes = [.. SettingKeys.Families.Select(f => f.Prefix)];

        Assert.Equal(prefixes.Length, prefixes.Distinct(StringComparer.Ordinal).Count());
        Assert.All(prefixes, p => Assert.DoesNotContain(SettingKeyFamily.Separator, p, StringComparison.Ordinal));
    }

    [Fact]
    public void No_static_key_is_a_member_of_any_family()
    {
        Assert.All(SettingKeys.All, k => Assert.DoesNotContain(SettingKeys.Families, f => f.Owns(k)));
    }

    [Fact]
    public void The_registered_families_are_exactly_the_workspace_width()
    {
        Assert.Equal(["workspace.width"], SettingKeys.Families.Select(f => f.Prefix));
        Assert.Same(SettingKeys.WorkspaceWidthByScope, Assert.Single(SettingKeys.Families));
    }

    // -------------------------------------------- the slice 3 keys

    [Fact]
    public void The_edge_defaults_to_right()
    {
        // Parity A2 and J3: "default Right".
        Assert.Equal("workspace.edge", SettingKeys.WorkspaceEdge.Name);
        Assert.Equal(WorkspaceEdge.Right, SettingKeys.WorkspaceEdge.Default);
        Assert.Equal(SettingValueKind.Enumeration, SettingKeys.WorkspaceEdge.Kind);
    }

    [Fact]
    public void The_global_and_per_scope_widths_default_to_360_dip()
    {
        // ADR-007 §4's nominal default.
        Assert.Equal("workspace.width", SettingKeys.WorkspaceWidth.Name);
        Assert.Equal(360, SettingKeys.WorkspaceWidth.Default);
        Assert.Equal(360, SettingKeys.WorkspaceWidthByScope.For("x").Default);
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(100)]   // below the 240 DIP minimum: valid data, clamped only when shown
    [InlineData(360)]
    [InlineData(5000)]  // above any display's maximum: valid data, clamped only when shown
    [InlineData(double.MaxValue)]
    public void Any_finite_positive_width_is_valid_data(double width)
    {
        Assert.True(SettingKeys.WorkspaceWidth.IsValid(width));
        Assert.True(SettingKeys.WorkspaceWidthByScope.For("x").IsValid(width));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-0.0001)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void A_width_that_is_not_finite_and_positive_is_invalid(double width)
    {
        Assert.False(SettingKeys.WorkspaceWidth.IsValid(width));
        Assert.False(SettingKeys.WorkspaceWidthByScope.For("x").IsValid(width));
    }
}
