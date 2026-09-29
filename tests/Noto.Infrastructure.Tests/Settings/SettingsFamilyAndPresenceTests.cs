using Noto.Core.Settings;
using Noto.Core.Storage;
using Noto.Core.Workspace;
using Noto.Infrastructure.Storage;
using Xunit;

namespace Noto.Infrastructure.Tests.Settings;

/// <summary>
/// Key families, presence-aware reads and the #16 slice 3 keys, against real
/// SQLite.
/// </summary>
public sealed class SettingsFamilyAndPresenceTests : IDisposable
{
    private readonly SettingsTestContext _context = new();

    private static readonly SettingKeyFamily<double> TestWidths = new("test.width", 360, static v => double.IsFinite(v) && v > 0);

    private static readonly IReadOnlyList<SettingKeyFamily> TestFamilies = [.. SettingKeys.Families, TestWidths];

    public void Dispose() => _context.Dispose();

    private SqliteSettingsStore Loaded()
    {
        var store = new SqliteSettingsStore(_context.Database, _context.Log);
        store.Load(TestSettingKeys.AllIncludingProduction, TestFamilies);
        return store;
    }

    // ------------------------------------------------- family loading

    [Fact]
    public void A_stored_family_member_is_materialised_at_load()
    {
        _context.SeedRaw("test.width::display-a", "512.5");
        _context.SeedRaw("test.width::display-b", "300");

        var store = Loaded();

        Assert.True(store.TryRead(TestWidths.For("display-a"), out double a));
        Assert.Equal(512.5, a);
        Assert.Equal(300, store.Read(TestWidths.For("display-b")));
    }

    [Fact]
    public void The_production_family_is_loaded_by_default()
    {
        _context.SeedRaw("workspace.width::monitor-1", "480");

        var store = new SqliteSettingsStore(_context.Database, _context.Log);
        store.Load();

        Assert.True(store.TryRead(SettingKeys.WorkspaceWidthByScope.For("monitor-1"), out double width));
        Assert.Equal(480, width);
    }

    [Fact]
    public void A_family_that_is_not_declared_is_not_materialised()
    {
        // The row has the right shape for TestWidths, but TestWidths is not
        // in the registry the production Load uses.
        _context.SeedRaw("test.width::display-a", "512");

        var store = new SqliteSettingsStore(_context.Database, _context.Log);
        store.Load(TestSettingKeys.AllIncludingProduction);

        Assert.False(store.TryRead(TestWidths.For("display-a"), out _));
        Assert.Empty(_context.Log.Fallbacks);
    }

    [Theory]
    [InlineData("test.width::")]         // no scope
    [InlineData("test.width::   ")]      // blank scope
    [InlineData("test.widths::x")]       // a prefix that merely starts the same
    [InlineData("unknown.key")]
    [InlineData("unknown::x")]
    public void A_row_no_declared_family_claims_is_ignored_and_preserved(string key)
    {
        _context.SeedRaw(key, "not even a number");

        var store = Loaded();
        Assert.True(store.Write(TestWidths.For("other"), 400));

        Assert.Equal("not even a number", _context.RawValueOf(key));
        Assert.Empty(_context.Log.Fallbacks);
    }

    [Fact]
    public void A_corrupt_family_member_falls_back_is_reported_and_left_untouched()
    {
        _context.SeedRaw("test.width::display-a", "wide");

        var store = Loaded();

        Assert.False(store.TryRead(TestWidths.For("display-a"), out double value));
        Assert.Equal(360, value);
        Assert.Equal(("test.width::display-a", SettingFallbackReason.Corrupt, "Real"), Assert.Single(_context.Log.Fallbacks));
        Assert.Equal("wide", _context.RawValueOf("test.width::display-a"));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-40")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void An_invalid_family_member_falls_back_is_reported_and_left_untouched(string stored)
    {
        _context.SeedRaw("test.width::display-a", stored);

        var store = Loaded();

        Assert.False(store.TryRead(TestWidths.For("display-a"), out _));
        Assert.Single(_context.Log.Fallbacks);
        Assert.Equal(stored, _context.RawValueOf("test.width::display-a"));
    }

    [Fact]
    public void A_family_member_round_trips_through_a_reload()
    {
        var store = Loaded();
        Assert.True(store.Write(TestWidths.For("display-a"), 612.25));

        var reloaded = Loaded();

        Assert.True(reloaded.TryRead(TestWidths.For("display-a"), out double width));
        Assert.Equal(612.25, width);
    }

    [Fact]
    public void Reloading_replaces_family_members_rather_than_merging()
    {
        var store = Loaded();
        Assert.True(store.Write(TestWidths.For("display-a"), 612));

        _context.SeedRaw("test.width::display-a", "wide");
        store.Load(TestSettingKeys.AllIncludingProduction, TestFamilies);

        Assert.False(store.TryRead(TestWidths.For("display-a"), out _));
    }

    [Fact]
    public void Static_keys_are_unaffected_by_family_loading()
    {
        _context.SeedRaw("test.count", "42");
        _context.SeedRaw("test.width::display-a", "512");

        var store = Loaded();

        Assert.Equal(42, store.Read(TestSettingKeys.Count));
        Assert.True(store.TryRead(TestSettingKeys.Count, out int count));
        Assert.Equal(42, count);
    }

    // --------------------------------------------------------- TryRead

    [Fact]
    public void TryRead_is_true_only_for_a_valid_stored_value()
    {
        _context.SeedRaw("test.count", "42");

        var store = Loaded();

        Assert.True(store.TryRead(TestSettingKeys.Count, out int value));
        Assert.Equal(42, value);
    }

    [Fact]
    public void TryRead_is_false_with_the_default_for_a_missing_key()
    {
        var store = Loaded();

        Assert.False(store.TryRead(TestSettingKeys.Count, out int value));
        Assert.Equal(7, value);
        Assert.Empty(_context.Log.Fallbacks);
    }

    [Fact]
    public void TryRead_is_false_with_the_default_for_a_corrupt_row()
    {
        _context.SeedRaw("test.count", "many");

        var store = Loaded();

        Assert.False(store.TryRead(TestSettingKeys.Count, out int value));
        Assert.Equal(7, value);
        Assert.Equal("many", _context.RawValueOf("test.count"));
    }

    [Fact]
    public void TryRead_is_false_with_the_default_for_an_invalid_row()
    {
        _context.SeedRaw("test.count", "500");

        var store = Loaded();

        Assert.False(store.TryRead(TestSettingKeys.Count, out int value));
        Assert.Equal(7, value);
        Assert.Equal("500", _context.RawValueOf("test.count"));
    }

    [Fact]
    public void TryRead_sees_a_value_written_this_session()
    {
        var store = Loaded();
        Assert.False(store.TryRead(TestSettingKeys.Count, out _));

        Assert.True(store.Write(TestSettingKeys.Count, 9));

        Assert.True(store.TryRead(TestSettingKeys.Count, out int value));
        Assert.Equal(9, value);
    }

    [Theory]
    [InlineData(null)]      // missing
    [InlineData("42")]      // valid
    [InlineData("many")]    // corrupt
    [InlineData("500")]     // invalid
    public void Read_and_TryRead_always_agree(string? stored)
    {
        if (stored is not null)
        {
            _context.SeedRaw("test.count", stored);
        }

        var store = Loaded();

        bool present = store.TryRead(TestSettingKeys.Count, out int tried);

        Assert.Equal(store.Read(TestSettingKeys.Count), tried);
        Assert.Equal(stored == "42", present);
    }

    // ---------------------------------------------------- SettingChanged

    [Fact]
    public void Writing_the_value_already_stored_still_raises_one_notification()
    {
        // The approved #9 contract: one notification after every successful
        // write. The event means "persisted", not "different".
        var store = Loaded();
        Assert.True(store.Write(TestSettingKeys.Count, 5));

        var seen = new List<SettingKey>();
        store.SettingChanged += (_, e) => seen.Add(e.Key);

        Assert.True(store.Write(TestSettingKeys.Count, 5));

        Assert.Same(TestSettingKeys.Count, Assert.Single(seen));
    }

    [Fact]
    public void A_family_member_write_notifies_with_a_key_the_family_owns()
    {
        var store = Loaded();
        var seen = new List<SettingKey>();
        store.SettingChanged += (_, e) => seen.Add(e.Key);

        Assert.True(store.Write(TestWidths.For("display-a"), 400));

        Assert.True(TestWidths.Owns(Assert.Single(seen)));
    }

    [Fact]
    public void A_failed_write_raises_no_notification_and_throws()
    {
        var store = Loaded();
        var seen = 0;
        store.SettingChanged += (_, _) => seen++;

        _context.DropSettingsTable();

        Assert.Throws<StorageException>(() => store.Write(TestSettingKeys.Count, 5));
        Assert.Equal(0, seen);
    }

    // ------------------------------------------------ workspace.edge

    [Theory]
    [InlineData("Left", WorkspaceEdge.Left)]
    [InlineData("Right", WorkspaceEdge.Right)]
    public void The_edge_round_trips_by_member_name(string stored, WorkspaceEdge expected)
    {
        _context.SeedRaw("workspace.edge", stored);

        var store = Loaded();

        Assert.Equal(expected, store.Read(SettingKeys.WorkspaceEdge));
    }

    [Fact]
    public void A_missing_edge_is_right()
    {
        Assert.Equal(WorkspaceEdge.Right, Loaded().Read(SettingKeys.WorkspaceEdge));
    }

    [Theory]
    [InlineData("left")]     // wrong case
    [InlineData("RIGHT")]
    [InlineData("2")]        // a number with no member
    [InlineData("Top")]
    [InlineData("")]
    public void An_unusable_edge_is_right_reported_and_left_untouched(string stored)
    {
        _context.SeedRaw("workspace.edge", stored);

        var store = Loaded();

        Assert.Equal(WorkspaceEdge.Right, store.Read(SettingKeys.WorkspaceEdge));
        Assert.Equal("workspace.edge", Assert.Single(_context.Log.Fallbacks).Key);
        Assert.Equal(stored, _context.RawValueOf("workspace.edge"));
    }

    [Fact]
    public void The_edge_is_written_by_name()
    {
        Assert.True(Loaded().Write(SettingKeys.WorkspaceEdge, WorkspaceEdge.Left));

        Assert.Equal("Left", _context.RawValueOf("workspace.edge"));
    }

    // ------------------------------------------------ workspace.width

    [Theory]
    [InlineData("100")]     // below the geometry minimum: still valid data
    [InlineData("5000")]    // above any display's maximum: still valid data
    [InlineData("360.5")]
    public void An_out_of_geometry_width_is_valid_data_and_never_rewritten(string stored)
    {
        _context.SeedRaw("workspace.width", stored);

        var store = Loaded();

        Assert.True(store.TryRead(SettingKeys.WorkspaceWidth, out double width));
        Assert.Equal(double.Parse(stored, System.Globalization.CultureInfo.InvariantCulture), width);
        Assert.Equal(stored, _context.RawValueOf("workspace.width"));
        Assert.Empty(_context.Log.Fallbacks);
    }

    [Fact]
    public void A_width_is_written_in_invariant_culture_and_round_trips_exactly()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");

        try
        {
            var store = Loaded();
            Assert.True(store.Write(SettingKeys.WorkspaceWidthByScope.For("m"), 451.2));

            Assert.Equal("451.2", _context.RawValueOf("workspace.width::m"));
            Assert.True(Loaded().TryRead(SettingKeys.WorkspaceWidthByScope.For("m"), out double back));
            Assert.Equal(451.2, back);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(double.NaN)]
    public void An_invalid_width_is_rejected_and_nothing_is_written(double width)
    {
        var store = Loaded();

        Assert.False(store.Write(SettingKeys.WorkspaceWidth, width));
        Assert.Null(_context.RawValueOf("workspace.width"));
    }
}
