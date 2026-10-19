using Noto.Core.Settings;
using Noto.Core.Storage;
using Noto.Core.Workspace;
using Noto.UseCases.Workspace;
using Xunit;

namespace Noto.UseCases.Tests.Workspace;

/// <summary>
/// The docked workspace's edge and width resolution (#16 slice 3), and the
/// typed reads and writes behind the settings surface (Slice 1).
/// </summary>
/// <remarks>
/// Against an in-memory <see cref="ISettingsStore"/> that honours the store's
/// contract — a value is "present" only when it has the key's type and passes
/// its rule — so the resolution order is tested without SQLite. The SQLite
/// behaviour of the same keys is covered in Noto.Infrastructure.Tests.
/// </remarks>
public sealed class WorkspacePreferencesTests
{
    private const string Here = @"\\?\DISPLAY#HERE#{e6f07b5f}";

    public enum Stored
    {
        Absent,
        Valid,
        Invalid,
        Corrupt,
    }

    // ------------------------------------------------------------ edge

    [Theory]
    [InlineData(WorkspaceEdge.Left)]
    [InlineData(WorkspaceEdge.Right)]
    public void The_edge_is_the_stored_one(WorkspaceEdge edge)
    {
        var store = new FakeSettings();
        store.Seed(SettingKeys.WorkspaceEdge.Name, edge);

        Assert.Equal(edge, new WorkspacePreferences(store).Edge);
    }

    [Fact]
    public void With_no_stored_edge_it_is_right()
    {
        Assert.Equal(WorkspaceEdge.Right, new WorkspacePreferences(new FakeSettings()).Edge);
    }

    // ------------------------------------- Escape and deactivation (slice 6)

    [Theory]
    [InlineData(EscapeBehavior.LeaveFolderOrHide)]
    [InlineData(EscapeBehavior.LeaveFolder)]
    [InlineData(EscapeBehavior.Hide)]
    [InlineData(EscapeBehavior.None)]
    public void The_escape_behaviour_is_the_stored_one(EscapeBehavior behaviour)
    {
        var store = new FakeSettings();
        store.Seed(SettingKeys.WorkspaceEscape.Name, behaviour);

        Assert.Equal(behaviour, new WorkspacePreferences(store).Escape);
    }

    [Fact]
    public void With_no_stored_escape_behaviour_it_is_leave_folder_or_hide()
    {
        // SideNotes' own default (feature inventory A12, CONFIRMED).
        Assert.Equal(EscapeBehavior.LeaveFolderOrHide, new WorkspacePreferences(new FakeSettings()).Escape);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Hiding_on_deactivation_is_the_stored_choice(bool hide)
    {
        var store = new FakeSettings();
        store.Seed(SettingKeys.WorkspaceHideOnDeactivation.Name, hide);

        Assert.Equal(hide, new WorkspacePreferences(store).HideOnDeactivation);
    }

    [Fact]
    public void With_nothing_stored_the_workspace_hides_on_deactivation()
    {
        // The slice 6 design gate: on by default.
        Assert.True(new WorkspacePreferences(new FakeSettings()).HideOnDeactivation);
    }

    // ------------------------------------------------ width resolution

    public static TheoryData<Stored, Stored, double> Chain()
    {
        // own = 500 when valid, global = 700 when valid.
        var data = new TheoryData<Stored, Stored, double>();

        foreach (Stored own in Enum.GetValues<Stored>())
        {
            foreach (Stored global in Enum.GetValues<Stored>())
            {
                double expected = own == Stored.Valid ? 500 : global == Stored.Valid ? 700 : 360;
                data.Add(own, global, expected);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Chain))]
    public void Width_resolves_own_then_global_then_360(Stored own, Stored global, double expected)
    {
        var store = new FakeSettings();
        Plant(store, SettingKeys.WorkspaceWidthByScope.For(Here).Name, own, 500);
        Plant(store, SettingKeys.WorkspaceWidth.Name, global, 700);

        Assert.Equal(expected, new WorkspacePreferences(store).WidthFor(Here));

        // Resolving never writes: a width that did not fit, or fell back, is
        // left exactly as stored.
        Assert.Empty(store.Writes);
    }

    [Fact]
    public void Another_displays_width_is_not_this_ones()
    {
        var store = new FakeSettings();
        store.Seed(SettingKeys.WorkspaceWidthByScope.For("elsewhere").Name, 800.0);

        Assert.Equal(360, new WorkspacePreferences(store).WidthFor(Here));
    }

    [Theory]
    [InlineData(100)]
    [InlineData(5000)]
    public void An_out_of_geometry_width_is_returned_unclamped(double stored)
    {
        // Fitting it to a display is the platform's job, against that
        // display's live work area. Here it is what the user chose.
        var store = new FakeSettings();
        store.Seed(SettingKeys.WorkspaceWidthByScope.For(Here).Name, stored);

        Assert.Equal(stored, new WorkspacePreferences(store).WidthFor(Here));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void A_blank_scope_is_rejected(string scope)
    {
        var preferences = new WorkspacePreferences(new FakeSettings());

        Assert.Throws<ArgumentException>(() => preferences.WidthFor(scope));
        Assert.Throws<ArgumentException>(() => preferences.RememberWidth(scope, 400));
    }

    // -------------------------------------------------- remembering

    [Fact]
    public void Remembering_writes_this_displays_width_then_the_global_one()
    {
        var store = new FakeSettings();

        Assert.True(new WorkspacePreferences(store).RememberWidth(Here, 512.5));

        Assert.Equal(
            [SettingKeys.WorkspaceWidthByScope.For(Here).Name, SettingKeys.WorkspaceWidth.Name],
            store.Writes);
        Assert.Equal(512.5, store.Raw(SettingKeys.WorkspaceWidthByScope.For(Here).Name));
        Assert.Equal(512.5, store.Raw(SettingKeys.WorkspaceWidth.Name));
    }

    [Fact]
    public void A_remembered_width_is_what_this_display_opens_with_next()
    {
        var store = new FakeSettings();
        var preferences = new WorkspacePreferences(store);

        Assert.True(preferences.RememberWidth(Here, 612));

        Assert.Equal(612, preferences.WidthFor(Here));
        Assert.Equal(612, preferences.WidthFor("a-display-with-none-of-its-own"));
    }

    [Fact]
    public void Remembering_does_not_touch_other_displays()
    {
        var store = new FakeSettings();
        store.Seed(SettingKeys.WorkspaceWidthByScope.For("elsewhere").Name, 800.0);

        Assert.True(new WorkspacePreferences(store).RememberWidth(Here, 400));

        Assert.Equal(800.0, store.Raw(SettingKeys.WorkspaceWidthByScope.For("elsewhere").Name));
    }

    [Fact]
    public void A_storage_failure_is_reported_not_thrown_and_the_other_key_is_still_written()
    {
        var store = new FakeSettings { FailWritesTo = SettingKeys.WorkspaceWidthByScope.For(Here).Name };

        bool saved = new WorkspacePreferences(store).RememberWidth(Here, 400);

        Assert.False(saved);
        Assert.Equal(400.0, store.Raw(SettingKeys.WorkspaceWidth.Name));
    }

    [Fact]
    public void A_failed_global_write_still_leaves_this_displays_width()
    {
        var store = new FakeSettings { FailWritesTo = SettingKeys.WorkspaceWidth.Name };

        bool saved = new WorkspacePreferences(store).RememberWidth(Here, 400);

        Assert.False(saved);
        Assert.Equal(400.0, store.Raw(SettingKeys.WorkspaceWidthByScope.For(Here).Name));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void An_invalid_width_is_not_remembered(double width)
    {
        var store = new FakeSettings();

        Assert.False(new WorkspacePreferences(store).RememberWidth(Here, width));
        Assert.Empty(store.Writes);
    }

    [Fact]
    public void A_null_store_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => new WorkspacePreferences(null!));
    }

    private static void Plant(FakeSettings store, string name, Stored state, double value)
    {
        switch (state)
        {
            case Stored.Valid:
                store.Seed(name, value);
                break;
            case Stored.Invalid:
                store.Seed(name, -1.0);
                break;
            case Stored.Corrupt:
                store.Seed(name, "not a number");
                break;
            default:
                break;
        }
    }

    // ------------------------------------- the settings surface (Slice 1)

    [Fact]
    public void With_nothing_stored_the_settings_surface_shows_the_documented_defaults()
    {
        var preferences = new WorkspacePreferences(new FakeSettings());

        Assert.Equal(WorkspaceTheme.System, preferences.Theme);
        Assert.False(preferences.SingleClickOpen);
        Assert.True(preferences.HideOnDeactivation);
        Assert.True(preferences.HotkeyEnabled);
        Assert.Equal("Ctrl+Alt+Win+Space", preferences.HotkeyBinding);
        Assert.Equal(WorkspaceEdge.Right, preferences.Edge);
        Assert.Equal(EscapeBehavior.LeaveFolderOrHide, preferences.Escape);
    }

    public static TheoryData<string, string, object> Setters => new()
    {
        { "SetEdge", SettingKeys.WorkspaceEdge.Name, WorkspaceEdge.Left },
        { "SetEscape", SettingKeys.WorkspaceEscape.Name, EscapeBehavior.None },
        { "SetHideOnDeactivation", SettingKeys.WorkspaceHideOnDeactivation.Name, false },
        { "SetSingleClickOpen", SettingKeys.WorkspaceSingleClickOpen.Name, true },
        { "SetTheme", SettingKeys.AppearanceTheme.Name, WorkspaceTheme.Dark },
        { "SetHotkeyEnabled", SettingKeys.HotkeyEnabled.Name, false },
        { "SetHotkeyBinding", SettingKeys.HotkeyBinding.Name, "Ctrl+Alt+Win+K" },
    };

    [Theory]
    [MemberData(nameof(Setters))]
    public void Each_setter_writes_its_own_key_once_announces_it_and_reads_back(string setter, string key, object value)
    {
        var store = new FakeSettings();
        var announced = new List<string>();
        store.SettingChanged += (_, e) => announced.Add(e.Key.Name);
        var preferences = new WorkspacePreferences(store);

        Assert.True(Set(preferences, setter, value));

        // One write, of that key only, announced once: the owner is told exactly what changed.
        Assert.Equal([key], store.Writes);
        Assert.Equal([key], announced);
        Assert.Equal(value, store.Raw(key));
        Assert.Equal(value, Get(new WorkspacePreferences(store), setter));
    }

    [Theory]
    [InlineData("SetEdge", 7)]
    [InlineData("SetEscape", 9)]
    [InlineData("SetTheme", 5)]
    public void A_value_that_is_not_a_member_is_refused_and_nothing_is_written(string setter, int undefined)
    {
        var store = new FakeSettings();
        var preferences = new WorkspacePreferences(store);
        object value = setter switch
        {
            "SetEdge" => (WorkspaceEdge)undefined,
            "SetEscape" => (EscapeBehavior)undefined,
            _ => (WorkspaceTheme)undefined,
        };

        Assert.False(Set(preferences, setter, value));
        Assert.Empty(store.Writes);
    }

    [Theory]
    [MemberData(nameof(Setters))]
    public void A_storage_failure_is_reported_as_not_kept_and_never_thrown(string setter, string key, object value)
    {
        var store = new FakeSettings { FailWritesTo = key };

        Assert.False(Set(new WorkspacePreferences(store), setter, value));
        Assert.Null(store.Raw(key));
    }

    [Fact]
    public void A_stored_theme_or_click_setting_of_the_wrong_kind_reads_as_its_default()
    {
        var store = new FakeSettings();
        store.Seed(SettingKeys.AppearanceTheme.Name, "Purple");
        store.Seed(SettingKeys.WorkspaceSingleClickOpen.Name, "yes");

        var preferences = new WorkspacePreferences(store);

        Assert.Equal(WorkspaceTheme.System, preferences.Theme);
        Assert.False(preferences.SingleClickOpen);
    }

    private static bool Set(WorkspacePreferences preferences, string setter, object value) => setter switch
    {
        "SetEdge" => preferences.SetEdge((WorkspaceEdge)value),
        "SetEscape" => preferences.SetEscape((EscapeBehavior)value),
        "SetHideOnDeactivation" => preferences.SetHideOnDeactivation((bool)value),
        "SetSingleClickOpen" => preferences.SetSingleClickOpen((bool)value),
        "SetTheme" => preferences.SetTheme((WorkspaceTheme)value),
        "SetHotkeyEnabled" => preferences.SetHotkeyEnabled((bool)value),
        "SetHotkeyBinding" => preferences.SetHotkeyBinding(Noto.Core.Activation.HotkeyChord.Parse((string)value)),
        _ => throw new ArgumentOutOfRangeException(nameof(setter), setter, null),
    };

    private static object Get(WorkspacePreferences preferences, string setter) => setter switch
    {
        "SetEdge" => preferences.Edge,
        "SetEscape" => preferences.Escape,
        "SetHideOnDeactivation" => preferences.HideOnDeactivation,
        "SetSingleClickOpen" => preferences.SingleClickOpen,
        "SetTheme" => preferences.Theme,
        "SetHotkeyEnabled" => preferences.HotkeyEnabled,
        "SetHotkeyBinding" => preferences.HotkeyBinding,
        _ => throw new ArgumentOutOfRangeException(nameof(setter), setter, null),
    };

    /// <summary>
    /// The store's contract in memory: present means right type and valid.
    /// </summary>
    private sealed class FakeSettings : ISettingsStore
    {
        private readonly Dictionary<string, object> _rows = new(StringComparer.Ordinal);

        public event EventHandler<SettingChangedEventArgs>? SettingChanged;

        public List<string> Writes { get; } = [];

        public string? FailWritesTo { get; init; }

        public void Seed(string name, object value) => _rows[name] = value;

        public object? Raw(string name) => _rows.GetValueOrDefault(name);

        public T Read<T>(SettingKey<T> key) => TryRead(key, out T value) ? value : key.Default;

        public bool TryRead<T>(SettingKey<T> key, out T value)
        {
            if (_rows.TryGetValue(key.Name, out object? raw) && raw is T typed && key.IsValid(typed))
            {
                value = typed;
                return true;
            }

            value = key.Default;
            return false;
        }

        public bool Write<T>(SettingKey<T> key, T value)
        {
            if (!key.IsValid(value))
            {
                return false;
            }

            if (key.Name == FailWritesTo)
            {
                throw new StorageException(StorageFailure.WriteFailed, "Simulated failure.");
            }

            _rows[key.Name] = value!;
            Writes.Add(key.Name);
            SettingChanged?.Invoke(this, new SettingChangedEventArgs(key));
            return true;
        }
    }
}
