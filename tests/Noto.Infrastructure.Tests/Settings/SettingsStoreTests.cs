using System.Globalization;
using Noto.Core.Settings;
using Noto.Core.Storage;
using Noto.Infrastructure.Storage;
using Xunit;

namespace Noto.Infrastructure.Tests.Settings;

/// <summary>
/// <c>SqliteSettingsStore</c> against real SQLite (issue #9).
/// </summary>
public sealed class SettingsStoreTests : IDisposable
{
    private readonly SettingsTestContext _context = new();

    public void Dispose() => _context.Dispose();

    // ------------------------------------------------------------ defaults

    [Fact]
    public void A_missing_key_returns_its_default()
    {
        // The normal state of a setting nobody has changed. Not an error, and
        // deliberately not logged — otherwise a clean install logs on every
        // key at every start.
        var store = _context.LoadedStore();

        Assert.Equal(7, store.Read(TestSettingKeys.Count));
        Assert.Empty(_context.Log.Fallbacks);
    }

    [Fact]
    public void Reading_a_missing_key_does_not_write_it()
    {
        // Persisting a default would destroy a value a newer Noto wrote, and
        // would turn a read into a write on the startup path.
        var store = _context.LoadedStore();

        _ = store.Read(TestSettingKeys.Count);
        _ = store.Read(SettingKeys.HotkeyBinding);

        Assert.Equal(0, _context.RowCount());
    }

    [Fact]
    public void The_two_settings_issue_16_needs_have_their_documented_defaults()
    {
        // activation.hotkey.binding is parity G1's mapping of SideNotes'
        // Ctrl+Opt+Cmd+Space; enabled is on because A7 and G1 are MUST rows
        // describing a working shortcut and principle 7 requires a keyboard
        // path. Asserted so neither can drift silently.
        var store = _context.LoadedStore();

        Assert.True(store.Read(SettingKeys.HotkeyEnabled));
        Assert.Equal("Ctrl+Alt+Win+Space", store.Read(SettingKeys.HotkeyBinding));
    }

    // -------------------------------------------------------- round trips

    [Fact]
    public void A_bool_round_trips()
    {
        var store = _context.LoadedStore();

        Assert.True(store.Write(TestSettingKeys.Flag, true));

        Assert.True(store.Read(TestSettingKeys.Flag));
        Assert.True(_context.LoadedStore().Read(TestSettingKeys.Flag));
    }

    [Fact]
    public void An_int_round_trips()
    {
        var store = _context.LoadedStore();

        Assert.True(store.Write(TestSettingKeys.Count, 42));

        Assert.Equal(42, store.Read(TestSettingKeys.Count));
        Assert.Equal(42, _context.LoadedStore().Read(TestSettingKeys.Count));
    }

    [Fact]
    public void A_double_round_trips_exactly_and_in_invariant_culture()
    {
        // 0.1 + 0.2 is the classic value whose shortest representation is not
        // its exact one: written with the default format it reads back
        // different. "R" is what makes the round trip exact.
        const double Awkward = 0.1 + 0.2;

        var store = _context.LoadedStore();

        Assert.True(store.Write(TestSettingKeys.Ratio, Awkward));

        Assert.Equal(Awkward, _context.LoadedStore().Read(TestSettingKeys.Ratio));

        // A decimal POINT regardless of the machine's locale. A comma here
        // would be unreadable by any other machine.
        string raw = _context.RawValueOf("test.ratio")!;
        Assert.Contains(".", raw, StringComparison.Ordinal);
        Assert.DoesNotContain(",", raw, StringComparison.Ordinal);
    }

    [Fact]
    public void A_double_written_under_a_comma_locale_still_reads_back()
    {
        // The defect this guards is silent: a value written in Berlin and read
        // in London. The store pins the culture on both sides, so switching
        // the thread's culture must change nothing.
        CultureInfo original = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            Assert.True(_context.LoadedStore().Write(TestSettingKeys.Ratio, 3.25));

            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            Assert.Equal(3.25, _context.LoadedStore().Read(TestSettingKeys.Ratio));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void A_string_round_trips()
    {
        var store = _context.LoadedStore();

        Assert.True(store.Write(TestSettingKeys.Label, "Ctrl+Alt+Win+Space"));

        Assert.Equal("Ctrl+Alt+Win+Space", _context.LoadedStore().Read(TestSettingKeys.Label));
    }

    [Fact]
    public void An_enum_round_trips_by_name()
    {
        var store = _context.LoadedStore();

        Assert.True(store.Write(TestSettingKeys.Edge, TestEdge.Left));

        Assert.Equal(TestEdge.Left, _context.LoadedStore().Read(TestSettingKeys.Edge));

        // The NAME, not the number: a member inserted mid-enum later would
        // renumber the rest and silently change what a stored "1" means.
        Assert.Equal("Left", _context.RawValueOf("test.edge"));
    }

    // ------------------------------------------------- invalid and corrupt

    [Fact]
    public void A_corrupt_value_falls_back_and_is_reported_as_corrupt()
    {
        _context.SeedRaw("test.count", "not-a-number");

        var store = _context.LoadedStore();

        Assert.Equal(7, store.Read(TestSettingKeys.Count));

        (string key, SettingFallbackReason reason, string expected) = Assert.Single(_context.Log.Fallbacks);
        Assert.Equal("test.count", key);
        Assert.Equal(SettingFallbackReason.Corrupt, reason);
        Assert.Equal("Whole", expected);
    }

    [Fact]
    public void An_invalid_value_falls_back_and_is_reported_as_invalid()
    {
        // Parses cleanly as an int, but the key's rule is 0..100. Separated
        // from Corrupt because the cause is different: a range this build
        // narrowed, not text of the wrong type.
        _context.SeedRaw("test.count", "5000");

        var store = _context.LoadedStore();

        Assert.Equal(7, store.Read(TestSettingKeys.Count));

        (string key, SettingFallbackReason reason, _) = Assert.Single(_context.Log.Fallbacks);
        Assert.Equal("test.count", key);
        Assert.Equal(SettingFallbackReason.Invalid, reason);
    }

    [Theory]
    [InlineData("test.count", "not-a-number")]
    [InlineData("test.count", "5000")]
    [InlineData("test.flag", "perhaps")]
    [InlineData("test.edge", "Sideways")]
    [InlineData("test.edge", "7")]
    [InlineData("test.ratio", "NaN")]
    public void A_bad_row_is_left_exactly_as_it_was(string key, string stored)
    {
        // The store reads around a bad value; it never repairs one. Rewriting
        // it would discard what a newer Noto — or the user — actually put
        // there, and a fallback is a read-time decision, not a migration.
        _context.SeedRaw(key, stored);

        _ = _context.LoadedStore();

        Assert.Equal(stored, _context.RawValueOf(key));
    }

    [Theory]
    [InlineData("7")]
    [InlineData("-1")]
    public void An_enum_number_with_no_member_is_rejected(string stored)
    {
        // Measured, not assumed: Enum.TryParse(typeof(E), "7", false, out v)
        // returns TRUE with v == 7 for a two-member enum. Without the
        // Enum.IsDefined guard that 7 reaches the cache and no switch handles
        // it. IsDefined is what rejects it.
        _context.SeedRaw("test.edge", stored);

        Assert.Equal(TestEdge.Right, _context.LoadedStore().Read(TestSettingKeys.Edge));

        (_, SettingFallbackReason reason, _) = Assert.Single(_context.Log.Fallbacks);
        Assert.Equal(SettingFallbackReason.Corrupt, reason);
    }

    [Fact]
    public void An_enum_stored_in_the_wrong_case_is_rejected()
    {
        // ignoreCase: false, so "left" does not parse. Deliberate: the store
        // writes the exact member name, so anything else was not written by
        // Noto and guessing at its intent is how a typo becomes a silent
        // setting change.
        _context.SeedRaw("test.edge", "left");

        Assert.Equal(TestEdge.Right, _context.LoadedStore().Read(TestSettingKeys.Edge));
        Assert.Single(_context.Log.Fallbacks);
    }

    // ------------------------------------------------------- unknown keys

    [Fact]
    public void An_unknown_key_is_preserved_and_never_deleted()
    {
        // A key a newer Noto wrote. An older build must leave it alone, or
        // downgrading once silently discards the newer build's settings.
        _context.SeedRaw("workspace.width", "320");
        _context.SeedRaw("some.future.setting", "whatever");

        var store = _context.LoadedStore();
        _ = store.Write(SettingKeys.HotkeyEnabled, false);

        Assert.Equal("320", _context.RawValueOf("workspace.width"));
        Assert.Equal("whatever", _context.RawValueOf("some.future.setting"));
    }

    [Fact]
    public void An_unknown_key_is_not_reported_as_a_fallback()
    {
        // It is not a failure of any declared setting; it is simply not ours.
        _context.SeedRaw("some.future.setting", "whatever");

        _ = _context.LoadedStore();

        Assert.Empty(_context.Log.Fallbacks);
    }

    // -------------------------------------------------------------- writes

    [Fact]
    public void Write_validates_before_persisting()
    {
        var store = _context.LoadedStore();

        Assert.False(store.Write(TestSettingKeys.Count, 5000));

        // Rejected means nothing was stored and nothing was cached — not
        // stored-then-rolled-back.
        Assert.Null(_context.RawValueOf("test.count"));
        Assert.Equal(7, store.Read(TestSettingKeys.Count));
    }

    [Fact]
    public void A_rejected_write_leaves_an_existing_value_alone()
    {
        var store = _context.LoadedStore();
        Assert.True(store.Write(TestSettingKeys.Count, 42));

        Assert.False(store.Write(TestSettingKeys.Count, -1));

        Assert.Equal(42, store.Read(TestSettingKeys.Count));
        Assert.Equal("42", _context.RawValueOf("test.count"));
    }

    [Fact]
    public void A_successful_write_updates_the_cache_without_reloading()
    {
        var store = _context.LoadedStore();

        Assert.True(store.Write(TestSettingKeys.Label, "changed"));

        // Same instance, no Load() in between.
        Assert.Equal("changed", store.Read(TestSettingKeys.Label));
    }

    [Fact]
    public void Writing_twice_updates_rather_than_duplicating()
    {
        var store = _context.LoadedStore();

        Assert.True(store.Write(TestSettingKeys.Count, 10));
        Assert.True(store.Write(TestSettingKeys.Count, 20));

        Assert.Equal(1, _context.RowCount());
        Assert.Equal("20", _context.RawValueOf("test.count"));
    }

    [Fact]
    public void Settings_do_not_interfere_with_each_other()
    {
        var store = _context.LoadedStore();

        Assert.True(store.Write(TestSettingKeys.Count, 42));
        Assert.True(store.Write(TestSettingKeys.Label, "kept"));
        Assert.True(store.Write(TestSettingKeys.Edge, TestEdge.Left));
        Assert.False(store.Write(TestSettingKeys.Ratio, -5));

        var reloaded = _context.LoadedStore();

        Assert.Equal(42, reloaded.Read(TestSettingKeys.Count));
        Assert.Equal("kept", reloaded.Read(TestSettingKeys.Label));
        Assert.Equal(TestEdge.Left, reloaded.Read(TestSettingKeys.Edge));
        Assert.Equal(1.5, reloaded.Read(TestSettingKeys.Ratio));
    }

    // ------------------------------------------------------- notification

    [Fact]
    public void A_successful_write_raises_exactly_one_notification()
    {
        var store = _context.LoadedStore();
        var seen = new List<SettingKey>();

        store.SettingChanged += (_, e) => seen.Add(e.Key);

        Assert.True(store.Write(TestSettingKeys.Count, 42));

        Assert.Same(TestSettingKeys.Count, Assert.Single(seen));
    }

    [Fact]
    public void A_rejected_write_raises_nothing()
    {
        var store = _context.LoadedStore();
        var raised = 0;

        store.SettingChanged += (_, _) => raised++;

        Assert.False(store.Write(TestSettingKeys.Count, 5000));

        Assert.Equal(0, raised);
    }

    [Fact]
    public void A_subscriber_reading_back_sees_the_new_value()
    {
        // Persist, then cache, then notify. A subscriber that re-reads inside
        // its handler must not see the old value.
        var store = _context.LoadedStore();
        string? observed = null;

        store.SettingChanged += (_, _) => observed = store.Read(TestSettingKeys.Label);

        Assert.True(store.Write(TestSettingKeys.Label, "new"));

        Assert.Equal("new", observed);
    }

    [Fact]
    public void A_throwing_subscriber_does_not_fail_the_write()
    {
        // The value is already committed by the time subscribers run, so a
        // subscriber's failure is not the writer's (ADR-010).
        var store = _context.LoadedStore();

        store.SettingChanged += (_, _) => throw new InvalidOperationException("subscriber blew up");

        Assert.True(store.Write(TestSettingKeys.Count, 42));

        Assert.Equal(42, store.Read(TestSettingKeys.Count));
        Assert.Equal("42", _context.RawValueOf("test.count"));
    }

    [Fact]
    public void A_throwing_subscriber_is_reported_rather_than_discarded()
    {
        // The exception cannot reach the writer without failing a committed
        // write, so it has to be recorded instead. A subscriber that silently
        // stops reacting is otherwise invisible.
        var store = _context.LoadedStore();

        store.SettingChanged += (_, _) => throw new InvalidOperationException("subscriber blew up");

        Assert.True(store.Write(TestSettingKeys.Count, 42));

        (string key, Exception ex) = Assert.Single(_context.Log.SubscriberFailures);
        Assert.Equal("test.count", key);
        Assert.IsType<InvalidOperationException>(ex);
    }

    [Fact]
    public void A_throwing_subscriber_does_not_stop_the_others()
    {
        var store = _context.LoadedStore();
        var reached = false;

        store.SettingChanged += (_, _) => throw new InvalidOperationException("first");
        store.SettingChanged += (_, _) => reached = true;

        Assert.True(store.Write(TestSettingKeys.Count, 42));

        Assert.True(reached);
    }

    [Fact]
    public void An_unsubscribed_handler_stops_being_called()
    {
        var store = _context.LoadedStore();
        var raised = 0;

        void Handler(object? sender, SettingChangedEventArgs e) => raised++;

        store.SettingChanged += Handler;
        Assert.True(store.Write(TestSettingKeys.Count, 1));

        store.SettingChanged -= Handler;
        Assert.True(store.Write(TestSettingKeys.Count, 2));

        Assert.Equal(1, raised);
    }

    // ------------------------------------------------------------ startup

    [Fact]
    public void Load_reads_every_stored_setting_in_one_pass()
    {
        _context.SeedRaw("test.count", "42");
        _context.SeedRaw("test.label", "seeded");
        _context.SeedRaw("test.edge", "Left");
        _context.SeedRaw("activation.hotkey.enabled", "False");

        var store = _context.LoadedStore();

        Assert.Equal(42, store.Read(TestSettingKeys.Count));
        Assert.Equal("seeded", store.Read(TestSettingKeys.Label));
        Assert.Equal(TestEdge.Left, store.Read(TestSettingKeys.Edge));
        Assert.False(store.Read(SettingKeys.HotkeyEnabled));
    }

    [Fact]
    public void A_settings_read_failure_falls_back_to_defaults_without_throwing()
    {
        // "Settings cannot prevent the application from starting" (#9). This
        // is NOT the unopenable-database case, which still refuses to start —
        // that decision is made earlier, by NotoDatabase.Initialize.
        _context.SeedRaw("test.count", "42");
        _context.DropSettingsTable();

        var store = new SqliteSettingsStore(_context.Database, _context.Log);

        store.Load(TestSettingKeys.AllIncludingProduction);

        Assert.Equal(7, store.Read(TestSettingKeys.Count));
        Assert.Equal("Ctrl+Alt+Win+Space", store.Read(SettingKeys.HotkeyBinding));
        Assert.Single(_context.Log.Unreadable);
    }

    [Fact]
    public void A_settings_read_failure_reports_a_storage_exception()
    {
        _context.DropSettingsTable();

        var store = new SqliteSettingsStore(_context.Database, _context.Log);
        store.Load(TestSettingKeys.AllIncludingProduction);

        Assert.IsType<StorageException>(Assert.Single(_context.Log.Unreadable));
    }

    [Fact]
    public void Reloading_replaces_the_cache_rather_than_merging_into_it()
    {
        var store = new SqliteSettingsStore(_context.Database, _context.Log);
        store.Load(TestSettingKeys.AllIncludingProduction);
        Assert.True(store.Write(TestSettingKeys.Count, 42));

        // Another process removing the row is not a scenario Noto supports,
        // but Load must still describe the table as it now is.
        _context.SeedRaw("test.count", "not-a-number");
        store.Load(TestSettingKeys.AllIncludingProduction);

        Assert.Equal(7, store.Read(TestSettingKeys.Count));
    }

    // ------------------------------------------------------------ the keys

    [Fact]
    public void A_key_cannot_be_declared_with_a_default_that_breaks_its_own_rule()
    {
        // Otherwise every fallback for that key produces an illegal value,
        // and the failure surfaces far from its cause.
        Assert.Throws<ArgumentException>(
            () => new SettingKey<int>("test.bad", 500, static v => v is >= 0 and <= 100));
    }

    [Fact]
    public void An_unsupported_value_type_is_rejected_at_declaration()
    {
        Assert.Throws<NotSupportedException>(
            () => new SettingKey<DateTimeOffset>("test.when", DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void These_tests_never_touch_the_developers_real_database()
    {
        // Not ceremony. A settings test that resolved the production path
        // would write into the real Noto folder and sit alongside a
        // developer's own notes. Asserted rather than assumed, because the
        // fixture is the only thing preventing it.
        string real = NotoStoragePaths.ForCurrentUser().DatabaseFile;

        Assert.NotEqual(real, _context.DatabasePath);
        Assert.StartsWith(Path.GetTempPath(), _context.DatabasePath, StringComparison.OrdinalIgnoreCase);

        // And the write path really did land in the temp file.
        Assert.True(_context.LoadedStore().Write(TestSettingKeys.Count, 42));
        Assert.True(File.Exists(_context.DatabasePath));
    }

    [Fact]
    public void The_registry_lists_exactly_the_settings_issue_9_declares()
    {
        // Guards the scope boundary: workspace.width, the edge side, hover and
        // tray belong to #16 and must not appear here by accident.
        Assert.Equal(
            ["activation.hotkey.enabled", "activation.hotkey.binding"],
            SettingKeys.All.Select(k => k.Name));
    }
}
