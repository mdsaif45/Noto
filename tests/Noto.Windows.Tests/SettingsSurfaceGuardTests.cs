using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// Slice 1's settings surface, held in the source: each preference has one write path and one owner that applies it at
/// once; Start with Windows has one source of truth, Windows'; Settings is a fourth surface that is left back to where
/// it was opened, with focus restored; it is reached by Ctrl+, from every surface, the header and the tray; and it is
/// built only while open. What the running application does with all of this is the settings harness's to prove
/// (tools/validation/Invoke-Settings.ps1), on a frozen Release build.
/// </summary>
public sealed class SettingsSurfaceGuardTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly string Windows = Path.GetDirectoryName(SourcePath("src", "Noto.Windows", "App.xaml.cs"))!;
    private static readonly string App = Code(Path.Join(Windows, "App.xaml.cs"));
    private static readonly string Main = Code(Path.Join(Windows, "MainWindow.xaml.cs"));
    private static readonly string Page = Code(Path.Join(Windows, "SettingsPage.xaml.cs"));
    private static readonly string Coordinator = Code(Path.Join(Windows, "WindowCoordinator.cs"));
    private static readonly string Preferences = Code(SourcePath("src", "Noto.UseCases", "Workspace", "WorkspacePreferences.cs"));
    private static readonly XDocument PageXaml = XDocument.Load(Path.Join(Windows, "SettingsPage.xaml"));
    private static readonly XDocument MainXaml = XDocument.Load(Path.Join(Windows, "MainWindow.xaml"));

    // ---------------------------------------------------------------- one write path, one owner each

    [Fact]
    public void Every_preference_settings_can_write_has_exactly_one_owner_that_applies_it_at_once()
    {
        // The setting, and the one place its runtime effect is applied. A setter with no owner here would store a
        // choice that does nothing until a restart; one with two would apply it twice.
        (string Key, string Owner)[] owners =
        [
            ("AppearanceTheme", @"ApplyTheme\(_main, preferences\.Theme\)"),
            ("WorkspaceEdge", @"_coordinator\?\.ChangeEdge\(DockEdges\.From\(preferences\.Edge\)\)"),
            ("WorkspaceEscape", @"_main\.Escape = preferences\.Escape"),
            ("WorkspaceHideOnDeactivation", @"_coordinator\.HideOnDeactivation = preferences\.HideOnDeactivation"),
            ("WorkspaceSingleClickOpen", @"_main\.SingleClickOpen = preferences\.SingleClickOpen"),
            ("HotkeyEnabled", @"ApplyHotkeyEnabled\(settings, preferences\.HotkeyEnabled\)"),
        ];

        var written = Regex.Matches(Preferences, @"public bool Set\w+\([^)]*\) => TryWrite\(SettingKeys\.(\w+),").Select(m => m.Groups[1].Value).ToHashSet();
        string apply = Body(App, "OnSettingChanged");

        Assert.Equal(owners.Select(o => o.Key).ToHashSet(), written);

        foreach (var (key, owner) in owners)
        {
            Assert.Equal(1, Regex.Count(apply, $@"key == SettingKeys\.{key}\b"));
            Assert.Matches($@"key == SettingKeys\.{key}\b[^{{]*\{{\s*{owner};", apply);
        }

        // Subscribed once, after every owner exists.
        Assert.Equal(1, Regex.Count(App, @"settings\.SettingChanged \+= \(_, e\) => OnSettingChanged\(e\.Key, settings, preferences\);"));
        Assert.True(App.IndexOf("settings.SettingChanged +=", StringComparison.Ordinal) > App.IndexOf("_coordinator = new WindowCoordinator(", StringComparison.Ordinal));
    }

    [Fact]
    public void A_change_from_another_thread_is_applied_on_the_ui_thread()
    {
        string apply = Body(App, "OnSettingChanged");

        Assert.Matches(@"if \(!_window\.DispatcherQueue\.HasThreadAccess\)\s*\{\s*_ = _window\.DispatcherQueue\.TryEnqueue\(\(\) => OnSettingChanged\(key, settings, preferences\)\);\s*return;", apply);
    }

    [Fact]
    public void The_settings_surface_writes_only_through_the_typed_preferences_and_never_a_second_store()
    {
        // No direct database, registry or setting-key access: the store has one writer per preference, and Start with
        // Windows is Windows' own registration, set through the same path the tray uses.
        string[] forbidden = ["ISettingsStore", "SqliteSettingsStore", "SettingKeys", "Registry", "LoginRegistration", "Process.Start", ".Write("];

        var used = forbidden.Where(f => Page.Contains(f, StringComparison.Ordinal)).ToList();
        Assert.True(used.Count == 0, "Settings page bypasses the typed preferences: " + string.Join(", ", used));
        Assert.Equal(6, Regex.Count(Page, @"Write\(p => [^;]*p\.Set\w+\("));
        Assert.Contains("_context.SetLogin(requested)", Page, StringComparison.Ordinal);
    }

    [Fact]
    public void Start_with_windows_has_one_source_of_truth_shared_by_the_tray_and_settings()
    {
        // One registration object, created once; the tray flips it and Settings sets it, both judged on what Windows has.
        Assert.Equal(1, Regex.Count(App, @"_login = Environment\.ProcessPath is string executable \? LoginRegistration\.ForCurrentUser\(executable\) : null;"));
        Assert.Contains("_login?.State()", App, StringComparison.Ordinal);
        Assert.Matches(@"LoginStartupState before = _login\.State\(\);[\s\S]*\(LoginStartupState\.Off, true\) => _login\.Enable\(\),\s*\(LoginStartupState\.On, false\) => _login\.Disable\(\),\s*_ => before,", Body(App, "SetLoginStartup"));
        Assert.DoesNotContain("SettingKeys", Body(App, "SetLoginStartup"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_theme_is_set_on_the_windows_content_before_the_first_frame()
    {
        string theme = Body(App, "ApplyTheme");

        Assert.Contains("window.Content is not FrameworkElement root", theme, StringComparison.Ordinal);
        Assert.Matches(@"WorkspaceTheme\.Light => ElementTheme\.Light,\s*WorkspaceTheme\.Dark => ElementTheme\.Dark,\s*_ => ElementTheme\.Default,", theme);
        Assert.True(App.IndexOf("ApplyTheme(main, preferences.Theme);", StringComparison.Ordinal) < App.IndexOf("_window.Activate();", StringComparison.Ordinal));
    }

    [Fact]
    public void The_side_and_hide_on_deactivation_are_read_live_by_the_coordinator()
    {
        Assert.Matches(@"docked\.ChangeEdge\(edge\);[\s\S]*if \(now\)\s*\{\s*Redock\(\);", Body(Coordinator, "ChangeEdge"));
        Assert.Contains("HideOnDeactivation: HideOnDeactivation && !_pinned", Coordinator, StringComparison.Ordinal);
        Assert.DoesNotContain("bool hideOnDeactivation", Coordinator, StringComparison.Ordinal);
    }

    [Fact]
    public void Turning_the_hotkey_off_releases_it_and_on_registers_it_through_the_startup_path()
    {
        string apply = Body(App, "ApplyHotkeyEnabled");

        Assert.Matches(@"if \(!enabled\)\s*\{\s*_hotkey\?\.Dispose\(\);\s*_hotkey = null;", apply);
        Assert.Matches(@"else if \(_hotkey is null\)\s*\{\s*_hotkey = StartHotkey\(settings, out refusal\);", apply);
        Assert.Contains("_main?.ShowHotkeyMessage(refusal);", apply, StringComparison.Ordinal);

        // A chord refused at launch is reported in Settings too, not only when it is turned on there.
        Assert.Contains("_hotkey = StartHotkey(settings, out string? hotkeyRefusal);", App, StringComparison.Ordinal);
        Assert.Contains("main.ShowHotkeyMessage(hotkeyRefusal);", App, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- navigation and focus

    [Fact]
    public void Settings_returns_to_the_surface_it_was_opened_from_with_focus_restored_and_is_released()
    {
        string open = Body(Main, "OpenSettings");
        string close = Body(Main, "CloseSettings");

        Assert.Contains("_settingsReturn = from;", open, StringComparison.Ordinal);
        Assert.Contains("FocusManager.GetFocusedElement(root) as Control", open, StringComparison.Ordinal);
        Assert.Contains("from.Visibility = Visibility.Collapsed;", open, StringComparison.Ordinal);

        Assert.Contains("UIElement back = _settingsReturn ?? PaneRoot;", close, StringComparison.Ordinal);
        Assert.Contains("back.Visibility = Visibility.Visible;", close, StringComparison.Ordinal);
        Assert.Matches(@"_settingsReturnFocus is \{ IsLoaded: true, Visibility: Visibility\.Visible \} previous\s*\? previous", close);
        Assert.Contains("restore.Focus(FocusState.Programmatic)", close, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_is_reached_by_ctrl_comma_from_every_surface_the_header_and_the_tray()
    {
        // On the workspace root, as Ctrl+W is, so every surface reaches it wherever focus sits.
        Assert.Matches(@"new KeyboardAccelerator \{ Key = \(VirtualKey\)0xBC, Modifiers = VirtualKeyModifiers\.Control \}", Main);
        Assert.Contains("WorkspaceRoot.KeyboardAccelerators.Add(openSettings);", Main, StringComparison.Ordinal);

        Assert.Equal("OnSettingsClick", (string?)Named(MainXaml, "SettingsButton").Attribute("Click"));

        Assert.Contains("new(TrayCommand.Settings, ", App, StringComparison.Ordinal);
        Assert.Matches(@"case TrayCommand\.Settings:\s*[^\n]*\n\s*ActivateFromTray\(Environment\.TickCount, ""settings""\);\s*_main\?\.OpenSettings\(\);", App);
    }

    [Fact]
    public void Escape_in_settings_follows_the_stored_behaviour()
    {
        Assert.Contains("OnEscape(WorkspaceSurface.Settings, e);", Body(Main, "OnSettingsKeyDown"), StringComparison.Ordinal);
        Assert.Matches(@"case EscapeAction\.LeaveSettings:\s*e\.Handled = true;\s*CloseSettings\(\);", Main);
    }

    [Fact]
    public void Settings_is_built_only_while_it_is_open_and_never_through_deferred_loading()
    {
        // Shown memory is already above its provisional budget (quality-gates.md): nothing of Settings exists until it
        // is opened, and it is released when it is left. It is created in code: measured on the frozen Release build,
        // a static x:Load element under a Window is never realised (FindName returned null), and the compiler's bound
        // x:Load form calls a FindName a Window does not have. Neither may come back.
        Assert.DoesNotContain(MainXaml.Descendants(), e => e.Name.LocalName == "SettingsPage");
        Assert.DoesNotContain(MainXaml.Descendants().SelectMany(e => e.Attributes()), a => a.Name == X + "Load");
        Assert.DoesNotContain("FindName(", Main, StringComparison.Ordinal);
        Assert.DoesNotContain("XamlMarkupHelper", Main, StringComparison.Ordinal);
    }

    [Fact]
    public void Opening_creates_exactly_one_surface_and_a_second_open_reuses_it()
    {
        string open = Body(Main, "OpenSettings");

        // Already open: focus moves into it, no second surface.
        Assert.Matches(@"if \(SettingsSurface is \{ \} open\)\s*\{\s*open\.FocusFirst\(\);\s*return;", open);
        Assert.True(Index(open, "if (SettingsSurface is { } open)") < Index(open, "new SettingsPage()"));

        // Created once per open, beside the surfaces, wired, and remembered before it is shown.
        Assert.Equal(1, Regex.Count(string.Join('\n', Directory.EnumerateFiles(Windows, "*.cs").Select(Code)), @"new SettingsPage\(\)"));
        Assert.Matches(@"var page = new SettingsPage\(\);\s*Grid\.SetRow\(page, Grid\.GetRow\(PaneRoot\)\);\s*page\.KeyDown \+= OnSettingsKeyDown;\s*page\.BackRequested \+= OnSettingsBackRequested;\s*\(\(Panel\)PaneRoot\.Parent\)\.Children\.Add\(page\);\s*SettingsSurface = page;", open);
        Assert.True(Index(open, "SettingsSurface = page;") < Index(open, "page.Show(Settings, _hotkeyMessage);"));
    }

    [Fact]
    public void Closing_removes_the_surface_and_leaves_no_reference_so_the_next_open_starts_fresh()
    {
        string close = Body(Main, "CloseSettings");

        Assert.Matches(@"if \(SettingsSurface is not \{ \} page\)\s*\{\s*return;", close);
        Assert.Matches(@"\(\(Panel\)page\.Parent\)\.Children\.Remove\(page\);\s*page\.KeyDown -= OnSettingsKeyDown;\s*page\.BackRequested -= OnSettingsBackRequested;\s*SettingsSurface = null;", close);

        // Nothing but OpenSettings and CloseSettings sets the reference.
        Assert.Equal(2, Regex.Count(Main, @"SettingsSurface = "));
    }

    [Fact]
    public void The_header_button_ctrl_comma_and_the_tray_converge_on_one_open()
    {
        Assert.Contains("private void OnSettingsClick(object sender, RoutedEventArgs e) => OpenSettings();", Main, StringComparison.Ordinal);
        Assert.Matches(@"OnSettingsAccelerator\([^)]*\)\s*\{\s*args\.Handled = true;\s*OpenSettings\(\);", Main);
        Assert.Contains("_main?.OpenSettings();", App, StringComparison.Ordinal);
        Assert.Equal(1, Regex.Count(Main, @"internal void OpenSettings\(\)"));
    }

    private static int Index(string text, string value)
    {
        int index = text.IndexOf(value, StringComparison.Ordinal);
        Assert.True(index >= 0, $"Not found: {value}");
        return index;
    }

    [Fact]
    public void One_click_opens_only_from_a_row_and_never_twice()
    {
        foreach (var (tapped, doubleTapped, item) in new[] { ("OnFolderListTapped", "OnFolderListDoubleTapped", "FolderListItem"), ("OnNoteListTapped", "OnNoteListDoubleTapped", "NoteListItem") })
        {
            Assert.Contains($"if (SingleClickOpen && IsOnRow<{item}>(e))", Body(Main, tapped), StringComparison.Ordinal);
            Assert.Contains("if (!SingleClickOpen)", Body(Main, doubleTapped), StringComparison.Ordinal);
        }

        Assert.Contains("(e.OriginalSource as FrameworkElement)?.DataContext is TItem", Main, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- structure and accessibility

    [Fact]
    public void The_page_has_the_approved_ids_one_title_and_the_five_sections_in_order()
    {
        string[] ids =
        [
            "SettingsBackButton", "LaunchAtLoginSetting", "HideOnDeactivationSetting", "SingleClickSetting", "SideSetting",
            "EscapeSetting", "ThemeSetting", "HotkeyEnabledSetting", "HotkeyMessage", "RevealDataFolderButton",
        ];
        var names = PageXaml.Descendants().Select(e => (string?)e.Attribute(X + "Name")).OfType<string>().ToHashSet();

        var missing = ids.Where(id => !names.Contains(id)).ToList();
        Assert.True(missing.Count == 0, "Missing ids: " + string.Join(", ", missing));
        Assert.Single(PageXaml.Descendants(), e => (string?)e.Attribute("AutomationProperties.AutomationId") == "SettingsRoot");
        Assert.NotNull(Named(MainXaml, "SettingsButton"));

        var headings = PageXaml.Descendants().Where(e => e.Attribute("AutomationProperties.HeadingLevel") is not null).ToList();
        Assert.Equal(["Settings"], headings.Where(h => (string?)h.Attribute("AutomationProperties.HeadingLevel") == "Level1").Select(h => (string?)h.Attribute("Text")));
        Assert.Equal(["General", "Sidebar", "Appearance", "Shortcuts", "Data"], headings.Where(h => (string?)h.Attribute("AutomationProperties.HeadingLevel") == "Level2").Select(h => (string?)h.Attribute("Text")));

        // Messages announce themselves; the text is the name.
        foreach (string message in new[] { "HotkeyMessage", "SettingsNoticeText" })
        {
            XElement element = Named(PageXaml, message);
            Assert.Equal("Assertive", (string?)element.Attribute("AutomationProperties.LiveSetting"));
            Assert.Null(element.Attribute("AutomationProperties.Name"));
        }
    }

    [Fact]
    public void Nothing_beyond_the_approved_scope_is_built()
    {
        // No navigation view, no second window, no recorder (PR C), no motion (PR D), no new activation surface (#16).
        string views = string.Join('\n', Directory.EnumerateFiles(Windows, "*.xaml").Select(File.ReadAllText));
        string code = string.Join('\n', Directory.EnumerateFiles(Windows, "*.cs").Select(Code));

        Assert.DoesNotContain("NavigationView", views, StringComparison.Ordinal);
        Assert.Equal(1, Regex.Count(code, @"partial class \w+ : Window\b"));
        Assert.DoesNotContain("HotkeyRecorder", views, StringComparison.Ordinal);
        Assert.DoesNotContain("Storyboard", views, StringComparison.Ordinal);
        Assert.DoesNotContain("ElementCompositionPreview", code, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- helpers

    private static XElement Named(XDocument document, string name) =>
        document.Descendants().Single(e => (string?)e.Attribute(X + "Name") == name);

    private static string Code(string path) => Regex.Replace(File.ReadAllText(path), @"^\s*//.*$", string.Empty, RegexOptions.Multiline);

    private static string Body(string source, string method)
    {
        var declaration = Regex.Match(source, $@"(private|internal|public)( static)? [\w?<>,\s]+ {method}\(");
        Assert.True(declaration.Success, $"Not found: {method}");
        int open = source.IndexOf('{', declaration.Index);
        int depth = 0;

        for (int i = open; i < source.Length; i++)
        {
            depth += source[i] == '{' ? 1 : source[i] == '}' ? -1 : 0;

            if (depth == 0)
            {
                return source[open..(i + 1)];
            }
        }

        throw new InvalidOperationException($"Unbalanced body for {method}");
    }

    /// <summary>Walks up to the repository root; a guard that silently skips is worse than none.</summary>
    private static string SourcePath(params string[] parts)
    {
        string relative = Path.Join(parts);

        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Join(directory.FullName, relative);

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Could not locate {relative} by walking up from {AppContext.BaseDirectory}");
    }
}
