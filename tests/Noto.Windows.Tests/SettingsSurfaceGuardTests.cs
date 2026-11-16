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
            ("AppearanceNoteFont", @"_main\.ApplyNoteText\(preferences\.NoteFont, preferences\.NoteSize\)"),
            ("AppearanceNoteSize", @"_main\.ApplyNoteText\(preferences\.NoteFont, preferences\.NoteSize\)"),
            ("AppearanceNoteColorStyle", @"_main\.ApplyNoteColorStyle\(preferences\.NoteColorStyle\)"),
            ("BackupAutomatic", @"_ = CheckAutomaticBackup\(\)"),
            ("BackupFrequency", @"_ = CheckAutomaticBackup\(\)"),
            ("EditorFormatToolbar", @"NoteEditorHost\.ShowsFormatToolbar = preferences\.FormatToolbar"),
            ("EditorPasteCodeBlock", @"NoteEditorHost\.PastesCodeBlock = preferences\.PasteCodeBlock"),
            ("EditorFileLinks", @"_main\.ApplyFileLinks\(preferences\.FileLinks\)"),
            ("ImportingPicture", @"NoteEditorHost\.PictureDrops = preferences\.PictureDrop"),
            ("ImportingTextFile", @"NoteEditorHost\.TextFileDrops = preferences\.TextFileDrop"),
            ("WorkspaceEdge", @"_coordinator\?\.ChangeEdge\(DockEdges\.From\(preferences\.Edge\)\)"),
            ("WorkspaceEscape", @"_main\.Escape = preferences\.Escape"),
            ("WorkspaceHideOnDeactivation", @"_coordinator\.HideOnDeactivation = preferences\.HideOnDeactivation"),
            ("WorkspaceSingleClickOpen", @"_main\.SingleClickOpen = preferences\.SingleClickOpen"),
            ("NotesNewPlacement", @"_main\.NewNotePlacement = preferences\.NewNotePlacement"),
            ("HotkeyEnabled", @"ApplyHotkeyEnabled\(settings, GlobalAction\.ShowHide, preferences\.HotkeyEnabled\)"),
            ("NewNoteHotkeyEnabled", @"ApplyHotkeyEnabled\(settings, GlobalAction\.NewNote, preferences\.NewNoteHotkeyEnabled\)"),
            ("ClipboardNoteHotkeyEnabled", @"ApplyHotkeyEnabled\(settings, GlobalAction\.NoteFromClipboard, preferences\.ClipboardNoteHotkeyEnabled\)"),
            ("SearchHotkeyEnabled", @"ApplyHotkeyEnabled\(settings, GlobalAction\.Search, preferences\.SearchHotkeyEnabled\)"),
            ("ChangeSideHotkeyEnabled", @"ApplyHotkeyEnabled\(settings, GlobalAction\.ChangeSide, preferences\.ChangeSideHotkeyEnabled\)"),
            ("KeepOpenHotkeyEnabled", @"ApplyHotkeyEnabled\(settings, GlobalAction\.KeepOpen, preferences\.KeepOpenHotkeyEnabled\)"),
        ];

        var written = Regex.Matches(Preferences, @"public bool Set\w+\([^)]*\) => TryWrite\(SettingKeys\.(\w+),").Select(m => m.Groups[1].Value).ToHashSet();
        string apply = Body(App, "OnSettingChanged");

        // The bindings are the exception, by design (Slice 1.2b): each is registered BEFORE it is written, by the rebind,
        // so the store's announcement must not apply it a second time. Opening the last folder at start (#193) is the
        // other: it is read once, at start, and has nothing to apply while Noto runs.
        Assert.Equal(owners.Select(o => o.Key).Append("HotkeyBinding").Append("NewNoteHotkeyBinding").Append("ClipboardNoteHotkeyBinding").Append("SearchHotkeyBinding").Append("ChangeSideHotkeyBinding").Append("KeepOpenHotkeyBinding").Append("WorkspaceOpenLastFolder").ToHashSet(), written);
        Assert.DoesNotContain("SettingKeys.WorkspaceOpenLastFolder", apply, StringComparison.Ordinal);
        Assert.Contains("OpenLastFolderOnStart = preferences.OpenLastFolder,", App, StringComparison.Ordinal);
        Assert.DoesNotContain("SettingKeys.HotkeyBinding", apply, StringComparison.Ordinal);
        Assert.DoesNotContain("SettingKeys.NewNoteHotkeyBinding", apply, StringComparison.Ordinal);
        Assert.DoesNotContain("SettingKeys.ClipboardNoteHotkeyBinding", apply, StringComparison.Ordinal);
        Assert.DoesNotContain("SettingKeys.SearchHotkeyBinding", apply, StringComparison.Ordinal);
        Assert.DoesNotContain("SettingKeys.ChangeSideHotkeyBinding", apply, StringComparison.Ordinal);
        Assert.DoesNotContain("SettingKeys.KeepOpenHotkeyBinding", apply, StringComparison.Ordinal);

        foreach (var (key, owner) in owners)
        {
            Assert.Equal(1, Regex.Count(apply, $@"key == SettingKeys\.{key}\b"));
            Assert.Matches($@"key == SettingKeys\.{key}\b[^{{]*\{{\s*{owner};", apply);
        }

        // Subscribed once, after every owner exists. (The log's own level subscription, #7, comes earlier by design: the
        // log must follow its level from before the window exists.)
        const string subscription = "settings.SettingChanged += (_, e) => OnSettingChanged(e.Key, settings, preferences);";
        Assert.Equal(1, Regex.Count(App, Regex.Escape(subscription)));
        Assert.True(App.IndexOf(subscription, StringComparison.Ordinal) > App.IndexOf("_coordinator = new WindowCoordinator(", StringComparison.Ordinal));
    }

    [Fact]
    public void A_change_from_another_thread_is_applied_on_the_ui_thread()
    {
        string apply = Body(App, "OnSettingChanged");

        Assert.Matches(@"if \(!_window\.DispatcherQueue\.HasThreadAccess\)\s*\{\s*_ = _window\.DispatcherQueue\.TryEnqueueGuarded\(\(\) => OnSettingChanged\(key, settings, preferences\)\);\s*return;", apply);
    }

    [Fact]
    public void The_settings_surface_writes_only_through_the_typed_preferences_and_never_a_second_store()
    {
        // No direct database, registry or setting-key access: the store has one writer per preference, and Start with
        // Windows is Windows' own registration, set through the same path the tray uses.
        string[] forbidden = ["ISettingsStore", "SqliteSettingsStore", "SettingKeys", "Registry", "LoginRegistration", "Process.Start", ".Write("];

        var used = forbidden.Where(f => Page.Contains(f, StringComparison.Ordinal)).ToList();
        Assert.True(used.Count == 0, "Settings page bypasses the typed preferences: " + string.Join(", ", used));
        Assert.Equal(23, Regex.Count(Page, @"Write\(p => [^;]*p\.Set\w+\("));
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
    public void The_theme_is_set_on_the_drawer_before_the_first_frame_and_system_follows_windows()
    {
        string theme = Body(App, "ApplyTheme");

        // On the drawer, not the window's content, which is pinned Dark under a see-through window (F2).
        Assert.Contains("FrameworkElement drawer = window.Drawer;", theme, StringComparison.Ordinal);
        Assert.Contains("internal FrameworkElement Drawer => WorkspaceContent;", Main, StringComparison.Ordinal);
        Assert.True(App.IndexOf("ApplyTheme(main, preferences.Theme);", StringComparison.Ordinal) < App.IndexOf("_window.Activate();", StringComparison.Ordinal));

        // "System" is the Windows app mode, read when applied, never inherited from the pinned root; and followed live.
        Assert.Matches(@"WorkspaceTheme\.Light => ElementTheme\.Light,\s*WorkspaceTheme\.Dark => ElementTheme\.Dark,\s*_ => SystemIsDark\(\) \? ElementTheme\.Dark : ElementTheme\.Light,", theme);
        Assert.DoesNotContain("ElementTheme.Default", App, StringComparison.Ordinal);
        Assert.Contains("SystemColors.ColorValuesChanged += (_, _) => _ = main.DispatcherQueue.TryEnqueueGuarded(() => ApplyTheme(main, preferences.Theme));", App, StringComparison.Ordinal);

        // A dialog themes from the window's content, so it is given the drawer's theme.
        Assert.Contains("RequestedTheme = Drawer.ActualTheme,", Code(Path.Join(Windows, "MainWindow.Links.cs")), StringComparison.Ordinal);
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

        Assert.Matches(@"if \(!enabled\)\s*\{\s*HotkeyOf\(action\)\?\.Dispose\(\);\s*SetHotkeyOf\(action, null\);", apply);
        Assert.Matches(@"else if \(HotkeyOf\(action\) is null\)\s*\{\s*SetHotkeyOf\(action, StartHotkey\(settings, action, out refusal\)\);", apply);
        Assert.Contains("_main?.ShowHotkeyMessage(action, refusal);", apply, StringComparison.Ordinal);

        // A chord refused at launch is reported in Settings too, not only when it is turned on there: for each action.
        Assert.Contains("_hotkey = StartHotkey(settings, GlobalAction.ShowHide, out string? hotkeyRefusal);", App, StringComparison.Ordinal);
        Assert.Contains("_newNoteHotkey = StartHotkey(settings, GlobalAction.NewNote, out string? newNoteRefusal);", App, StringComparison.Ordinal);
        Assert.Contains("main.ShowHotkeyMessage(GlobalAction.ShowHide, hotkeyRefusal);", App, StringComparison.Ordinal);
        Assert.Contains("main.ShowHotkeyMessage(GlobalAction.NewNote, newNoteRefusal);", App, StringComparison.Ordinal);
        Assert.Contains("_changeSideHotkey = StartHotkey(settings, GlobalAction.ChangeSide, out string? changeSideRefusal);", App, StringComparison.Ordinal);
        Assert.Contains("_keepOpenHotkey = StartHotkey(settings, GlobalAction.KeepOpen, out string? keepOpenRefusal);", App, StringComparison.Ordinal);
        Assert.Contains("main.ShowHotkeyMessage(GlobalAction.ChangeSide, changeSideRefusal);", App, StringComparison.Ordinal);
        Assert.Contains("main.ShowHotkeyMessage(GlobalAction.KeepOpen, keepOpenRefusal);", App, StringComparison.Ordinal);
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
        Assert.Contains("e.Handled = OnEscape(WorkspaceSurface.Settings);", Body(Main, "OnSettingsKeyDown"), StringComparison.Ordinal);
        Assert.Matches(@"case EscapeAction\.LeaveSettings:\s*CloseSettings\(\);\s*return true;", Main);
    }

    [Fact]
    public void Settings_is_built_only_while_it_is_open_and_never_through_deferred_loading()
    {
        // Shown memory is already above its provisional budget (quality-gates.md): nothing of Settings exists until it
        // is opened, and it is released when it is left. It is created in code: measured on the frozen Release build,
        // a static x:Load element under a Window is never realised (FindName returned null), and the compiler's bound
        // x:Load form calls a FindName a Window does not have. Neither may come back. Inside a DataTemplate the bound
        // form calls FindName on the template's own root (the note card's editor, ADR-016 §4), which is allowed.
        Assert.DoesNotContain(MainXaml.Descendants(), e => e.Name.LocalName == "SettingsPage");
        Assert.DoesNotContain(
            MainXaml.Descendants().Where(e => !e.Ancestors().Any(a => a.Name.LocalName == "DataTemplate")).SelectMany(e => e.Attributes()),
            a => a.Name == X + "Load");
        Assert.DoesNotContain("FindName(", Main, StringComparison.Ordinal);
        Assert.DoesNotContain("XamlMarkupHelper", Main, StringComparison.Ordinal);
    }

    [Fact]
    public void Opening_creates_exactly_one_surface_and_a_second_open_reuses_it()
    {
        string open = Body(Main, "OpenSettings");

        // Already open: focus moves into it, no second surface; unless a welcome opened from it is in front, which is
        // then left (ADR-021).
        Assert.Matches(@"if \(SettingsSurface is \{ \} open\)\s*\{\s*if \(WelcomeSurface is not null && _welcomeReturn == open\)\s*\{\s*CloseWelcome\(\);\s*return;\s*\}\s*open\.FocusFirst\(\);\s*return;", open);
        Assert.True(Index(open, "if (SettingsSurface is { } open)") < Index(open, "new SettingsPage()"));

        // Created once per open, beside the surfaces, wired, and remembered before it is shown.
        Assert.Equal(1, Regex.Count(string.Join('\n', Directory.EnumerateFiles(Windows, "*.cs").Select(Code)), @"new SettingsPage\(\)"));
        Assert.Matches(@"var page = new SettingsPage\(\);\s*Grid\.SetRow\(page, Grid\.GetRow\(PaneRoot\)\);\s*page\.KeyDown \+= OnSettingsKeyDown;\s*page\.BackRequested \+= OnSettingsBackRequested;\s*\(\(Panel\)PaneRoot\.Parent\)\.Children\.Add\(page\);\s*SettingsSurface = page;", open);
        Assert.True(Index(open, "SettingsSurface = page;") < Index(open, "page.Show(Settings, _hotkeyMessages);"));
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

    [Fact]
    public void Ctrl_comma_inside_the_editor_is_handed_to_the_same_accelerator_and_nothing_else_is()
    {
        // Measured: the editor's TextBox marks Ctrl+, handled in its own KeyDown, so with focus in it the root's
        // accelerator never ran. The editor host's accelerator pass, which runs before that KeyDown, hands exactly that
        // chord to the window root's accelerators: no second shortcut, no second way into OpenSettings, no other key
        // taken from the editor. What it does at run time is Invoke-Settings.ps1 case 1's and Invoke-NoteStack.ps1
        // case 9's to prove.
        Match accelerator = Regex.Match(Main, @"new KeyboardAccelerator \{ Key = (?<key>\(VirtualKey\)0x[0-9A-F]+), Modifiers = (?<modifiers>VirtualKeyModifiers\.\w+) \};\s*openSettings\.Invoked \+= OnSettingsAccelerator;\s*WorkspaceRoot\.KeyboardAccelerators\.Add\(openSettings\);");
        Assert.True(accelerator.Success, "The root's Settings accelerator is not where it was.");

        // The window's content is WorkspaceRoot, so the host's XamlRoot.Content is the element holding that accelerator.
        Assert.Equal("WorkspaceRoot", (string?)MainXaml.Root!.Elements().Single().Attribute(X + "Name"));

        string key = Regex.Escape(accelerator.Groups["key"].Value);
        string modifiers = Regex.Escape(accelerator.Groups["modifiers"].Value);
        string host = Code(Path.Join(Windows, "NoteEditorHost.xaml.cs"));
        Assert.Matches(
            @"InitializeComponent\(\);\s*NoteEditor\.ProcessKeyboardAccelerators \+= \(_, args\) =>\s*\{\s*if \(HandedOn\(args\.Key, args\.Modifiers\) && XamlRoot\?\.Content is UIElement root\)\s*\{\s*root\.TryInvokeKeyboardAccelerator\(args\);\s*\}\s*\};",
            host);

        // Exactly the window's chords the edit control would keep: Settings, Search and the note text's size (F13).
        Assert.Matches(
            $@"\(modifiers == {modifiers} && key is {key} or VirtualKey\.F or \(VirtualKey\)0xBB or \(VirtualKey\)0xBD or VirtualKey\.Add or VirtualKey\.Subtract\)\s*\|\| \(modifiers == \(VirtualKeyModifiers\.Control \| VirtualKeyModifiers\.Shift\) && key == \(VirtualKey\)0xBB\);",
            host);

        // Wired once, to the editor itself; nothing else redirects or invokes accelerators.
        string code = string.Join('\n', Directory.EnumerateFiles(Windows, "*.cs").Select(Code));
        Assert.Equal(1, Regex.Count(code, @"\.ProcessKeyboardAccelerators \+="));
        Assert.Equal(1, Regex.Count(code, @"TryInvokeKeyboardAccelerator\("));

        // Still one way in: the header button's click and the root accelerator's handler; the tray calls it from App.
        Assert.Equal(2, Regex.Count(Main, @"\bOpenSettings\(\);"));
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
        // Folders: one click or two, as the setting says (J7, C4), never both.
        Assert.Contains("if (SingleClickOpen && IsOnRow<FolderListItem>(e))", Body(Main, "OnFolderListTapped"), StringComparison.Ordinal);
        Assert.Contains("if (!SingleClickOpen)", Body(Main, "OnFolderListDoubleTapped"), StringComparison.Ordinal);
        Assert.Contains("(e.OriginalSource as FrameworkElement)?.DataContext is TItem", Main, StringComparison.Ordinal);

        // Cards are edited in place, not opened (ADR-016 §6): one click on a card that is not editing edits it, whatever
        // the setting; a click in the editor is the editor's. A double click on a card folds it (B12, #190), decided on
        // the second press (NoteStackGuardTests), never by the framework's double tap.
        Assert.Contains("(e.OriginalSource as FrameworkElement)?.DataContext is NoteCard { IsEditing: false } card", Body(Main, "OnNoteListTapped"), StringComparison.Ordinal);
        Assert.DoesNotContain("SingleClickOpen", Body(Main, "OnNoteListTapped"), StringComparison.Ordinal);
        Assert.Null(MainXaml.Descendants().Single(e => (string?)e.Attribute(X + "Name") == "NoteList").Attribute("DoubleTapped"));
    }

    [Fact]
    public void The_second_click_of_a_double_click_that_entered_a_folder_edits_no_card()
    {
        // Measured (the settings harness, case 7): with one click entering, the second click of a double click is not a
        // double tap on the folder list, which is gone, but a tap on the stack, on whatever card is under the pointer.
        Assert.Matches(@"_enteredByClickAt = Environment\.TickCount64;\s*EnterSelectedFolder\(\);", Body(Main, "OnFolderListTapped"));

        string tapped = Body(Main, "OnNoteListTapped");
        Assert.Matches(@"if \(Environment\.TickCount64 - _enteredByClickAt <= _system\.DoubleClickTime\)\s*\{\s*e\.Handled = true;\s*return;\s*\}", tapped);
        Assert.True(Index(tapped, "_system.DoubleClickTime") < Index(tapped, "EditCard("), "A card is edited before the double click is refused.");
    }

    [Fact]
    public void Where_a_new_note_goes_is_chosen_in_general_and_every_way_of_making_a_note_follows_it()
    {
        // #188 (B5, J8): one row in General, listing each placement once by its member name, shown and written through
        // the typed preferences like its neighbours.
        XElement box = Named(PageXaml, "NewNotePlacementSetting");
        Assert.Equal(["Bottom", "Top", "AboveSelected", "BelowSelected"], box.Elements().Select(i => (string?)i.Attribute("Tag")));
        Assert.Equal(["At the bottom", "At the top", "Above the selected note", "Below the selected note"], box.Elements().Select(i => (string?)i.Attribute("Content")));
        Assert.Equal("Where new notes go", (string?)box.Attribute("AutomationProperties.Name"));
        Assert.Equal("OnNewNotePlacementChanged", (string?)box.Attribute("SelectionChanged"));
        Assert.Contains(box.Ancestors(), a => a.Elements().Any(h => (string?)h.Attribute("AutomationProperties.HeadingLevel") == "Level2" && (string?)h.Attribute("Text") == "General"));
        Assert.Contains("Select(NewNotePlacementSetting, preferences.NewNotePlacement.ToString());", Body(Page, "ShowStored"), StringComparison.Ordinal);
        Assert.Contains("p.SetNewNotePlacement(Selected<NewNotePlacement>(NewNotePlacementSetting))", Page, StringComparison.Ordinal);

        // One place makes a note, and it passes the setting on: Ctrl+N, the + button, the card menu, and the new-note and
        // clipboard shortcuts (the tray's included, NewNoteFromShortcut) all reach it, and nothing else creates a note.
        string window = string.Join('\n', Directory.EnumerateFiles(Windows, "*.cs").Select(Code));
        Assert.Equal(1, Regex.Count(window, @"_stack\.Create\("));
        Assert.DoesNotContain("new CreateNote(", window, StringComparison.Ordinal);
        Assert.Contains("_stack.Create(NewNotePlacement, selected, content);", Body(Main, "CreateNoteInOpenFolder"), StringComparison.Ordinal);
        Assert.Contains("CreateNoteInOpenFolder();", Body(Main, "OnCreateNoteAccelerator"), StringComparison.Ordinal);
        Assert.Contains("private void OnNewNoteClick(object sender, RoutedEventArgs e) => CreateNoteInOpenFolder();", Main, StringComparison.Ordinal);
        Assert.Contains("MenuItem(\"New note\", \"Ctrl+N\", () => CreateNoteInOpenFolder())", Main, StringComparison.Ordinal);
        Assert.Contains("CreateNoteInOpenFolder(content);", Body(Code(Path.Join(Windows, "MainWindow.NewNoteShortcut.cs")), "NewNoteFromShortcut"), StringComparison.Ordinal);

        // The session hands the placement to the command, and the command's handler decides through the one rule.
        Assert.Contains("new CreateNote(folder, string.Empty, placement, selected)", Code(SourcePath("src", "Noto.UseCases", "Workspace", "NoteStackSession.cs")), StringComparison.Ordinal);
        Assert.Contains("NewNotePlacementRule.Resolve(command.Placement, command.Selected,", Code(SourcePath("src", "Noto.UseCases", "Notes", "CreateNote.cs")), StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- structure and accessibility

    [Fact]
    public void The_page_has_the_approved_ids_one_title_and_the_seven_sections_in_order()
    {
        string[] ids =
        [
            "SettingsBackButton", "LaunchAtLoginSetting", "HideOnDeactivationSetting", "SingleClickSetting", "OpenLastFolderSetting", "NewNotePlacementSetting", "SideSetting",
            "EscapeSetting", "ThemeSetting", "NoteColorStyleSetting", "HotkeyEnabledSetting", "HotkeyMessage", "RevealDataFolderButton", "ShowWelcomeButton",
        ];
        var names = PageXaml.Descendants().Select(e => (string?)e.Attribute(X + "Name")).OfType<string>().ToHashSet();

        var missing = ids.Where(id => !names.Contains(id)).ToList();
        Assert.True(missing.Count == 0, "Missing ids: " + string.Join(", ", missing));
        Assert.NotNull(Named(MainXaml, "SettingsButton"));

        var headings = PageXaml.Descendants().Where(e => e.Attribute("AutomationProperties.HeadingLevel") is not null).ToList();
        Assert.Equal(["Settings"], headings.Where(h => (string?)h.Attribute("AutomationProperties.HeadingLevel") == "Level1").Select(h => (string?)h.Attribute("Text")));
        Assert.Equal(["General", "Sidebar", "Appearance", "Importing", "Shortcuts", "Data", "Help"], headings.Where(h => (string?)h.Attribute("AutomationProperties.HeadingLevel") == "Level2").Select(h => (string?)h.Attribute("Text")));

        // Messages announce themselves; the text is the name.
        foreach (string message in new[] { "HotkeyMessage", "SettingsNoticeText" })
        {
            XElement element = Named(PageXaml, message);
            Assert.Equal("Assertive", (string?)element.Attribute("AutomationProperties.LiveSetting"));
            Assert.Null(element.Attribute("AutomationProperties.Name"));
        }
    }

    [Fact]
    public void Every_setting_is_one_row_with_its_control_centred_at_the_right_edge()
    {
        // One layout for every setting, so the controls of every group line up in one column at the right: each group
        // holds only rows, spaced alike; each row is the shared row style over a stretching text column and an Auto
        // control column; the control column holds one control, right-aligned and centred, by attribute or its style.
        var styles = XDocument.Load(SourcePath("src", "Noto.UI", "Styles", "Settings.xaml")).Descendants()
            .Where(e => e.Name.LocalName == "Style" && e.Attribute(X + "Key") is not null)
            .ToDictionary(e => (string)e.Attribute(X + "Key")!);
        XElement scroll = Named(PageXaml, "SettingsScroll");
        var groups = scroll.Descendants().Where(e => (string?)e.Attribute("Style") == "{StaticResource SettingsGroupStyle}").ToList();
        var rows = scroll.Descendants().Where(e => e.Name.LocalName == "Grid").ToList();
        var failures = new List<string>();

        Assert.Equal(7, groups.Count);

        foreach (XElement panel in groups.Select(group => Assert.Single(group.Elements())))
        {
            Assert.Equal("{StaticResource SpaceM}", (string?)panel.Attribute("Spacing"));
            Assert.All(panel.Elements(), e => Assert.Equal("Grid", e.Name.LocalName));
        }

        // The settings (the note colour style, #191; the note font and text size, F13; automatic backups, F15, and how
        // often, #199; the formatting toolbar, F8; pasting code as a code block, D39 (#187); the new-note shortcut and its
        // recorder, F17; the clipboard's, F18; Search's, F21; where a new note goes, #188; change side's and keep open's,
        // #198), the Data group's Back up, Restore, Export, Import and their progress line (ADR-020), Help's Welcome tour
        // (ADR-021), opening the last folder at start (#193), and file links and the two Importing choices (#196).
        Assert.Equal(37, rows.Count);

        foreach (XElement row in rows)
        {
            var columns = row.Descendants().Where(e => e.Name.LocalName == "ColumnDefinition").Select(c => (string?)c.Attribute("Width"));
            var controls = row.Elements().Where(e => (string?)e.Attribute("Grid.Column") == "1").ToList();
            XElement? control = controls.Count == 1 ? controls[0] : null;

            if ((string?)row.Attribute("Style") != "{StaticResource SettingRowStyle}" || !columns.SequenceEqual(["*", "Auto"]) || control is null)
            {
                failures.Add($"row {rows.IndexOf(row)} is not a two-column setting row with one control");
                continue;
            }

            string name = (string?)control.Attribute(X + "Name") ?? control.Name.LocalName;

            if (control.Name.LocalName is not ("ToggleSwitch" or "ComboBox" or "Button")
                || Effective(control, "HorizontalAlignment") != "Right"
                || Effective(control, "VerticalAlignment") != "Center")
            {
                failures.Add($"{name} is not a control centred at the row's right edge");
            }
        }

        // Every switch and every choice on the page is such a row's control; none is left stacked under its label.
        failures.AddRange(PageXaml.Descendants()
            .Where(e => e.Name.LocalName is "ToggleSwitch" or "ComboBox")
            .Where(e => e.Parent?.Name.LocalName != "Grid" || (string?)e.Attribute("Grid.Column") != "1")
            .Select(e => $"{(string?)e.Attribute(X + "Name")} is outside a row's control column"));

        // A switch shows no On/Off text, so its track is what sits at the edge; its name is the setting's.
        Assert.Contains(styles["SettingToggleSwitchStyle"].Elements(), s => (string?)s.Attribute("Property") == "OnContent" && (string?)s.Attribute("Value") == string.Empty);
        Assert.Contains(styles["SettingToggleSwitchStyle"].Elements(), s => (string?)s.Attribute("Property") == "OffContent" && (string?)s.Attribute("Value") == string.Empty);

        // As wide as its track: measured, a switch that sized itself stopped 86px short of the right edge.
        Assert.Contains(styles["SettingToggleSwitchStyle"].Elements(), s => (string?)s.Attribute("Property") == "Width" && (string?)s.Attribute("Value") == "{StaticResource SettingSwitchWidth}");

        Assert.True(failures.Count == 0, string.Join("; ", failures));

        string? Effective(XElement control, string property)
        {
            if ((string?)control.Attribute(property) is string set)
            {
                return set;
            }

            string? key = Regex.Match((string?)control.Attribute("Style") ?? string.Empty, @"^\{StaticResource (\w+)\}$").Groups[1].Value;

            return styles.TryGetValue(key, out XElement? style)
                ? style.Elements().Where(s => (string?)s.Attribute("Property") == property).Select(s => (string?)s.Attribute("Value")).FirstOrDefault()
                : null;
        }
    }

    [Fact]
    public void How_often_to_back_up_offers_a_day_and_sidenotes_three_intervals_and_is_off_while_automatic_backup_is()
    {
        // #199 (I8, J38): the row follows Back up automatically, a choice per BackupFrequency member, named for what it
        // sets; shown as stored, written through its one setter, and off while automatic backup is off, as a shortcut's
        // recorder is while its shortcut is. A change is applied at once by the owner (the test above).
        XElement toggle = Named(PageXaml, "AutomaticBackupSetting");
        XElement choice = Named(PageXaml, "BackupFrequencySetting");
        var rows = toggle.Parent!.Parent!.Elements().ToList();

        Assert.Equal(rows.IndexOf(toggle.Parent!) + 1, rows.IndexOf(choice.Parent!));
        Assert.Equal("ComboBox", choice.Name.LocalName);
        Assert.Equal("How often to back up automatically", (string?)choice.Attribute("AutomationProperties.Name"));
        Assert.Equal(
            [("Every day", "Daily"), ("Every 45 minutes", "Every45Minutes"), ("Every 30 minutes", "Every30Minutes"), ("Every 15 minutes", "Every15Minutes")],
            choice.Elements().Select(i => ((string)i.Attribute("Content")!, (string)i.Attribute("Tag")!)));
        Assert.Equal(Enum.GetNames<Noto.Core.Portability.BackupFrequency>(), choice.Elements().Select(i => (string)i.Attribute("Tag")!));

        string shown = Body(Page, "ShowStored");
        Assert.Contains("Select(BackupFrequencySetting, preferences.BackupFrequency.ToString());", shown, StringComparison.Ordinal);
        Assert.Contains("BackupFrequencySetting.IsEnabled = preferences.AutomaticBackups;", shown, StringComparison.Ordinal);
        Assert.Contains("BackupFrequencySetting.IsEnabled = _context.Preferences.AutomaticBackups;", Body(Page, "OnAutomaticBackupToggled"), StringComparison.Ordinal);
        Assert.Contains("p.SetBackupFrequency(Selected<BackupFrequency>(BackupFrequencySetting))", Page, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_leaves_room_for_its_scroll_bar_and_its_controls_are_rounded_as_their_groups_are()
    {
        // #144. The scroll bar overlays its viewer's content, so the viewer pads its content by the bar's width. A
        // choice never changes width when it opens (WinUI shrinks a content-sized ComboBox and opens its list against
        // the moved field). Every control in a group takes the group's radius, through a template that honours it.
        // What this looks like and where the list opens is Invoke-Settings.ps1 case 15's to measure.
        var styles = XDocument.Load(SourcePath("src", "Noto.UI", "Styles", "Settings.xaml")).Descendants()
            .Where(e => e.Name.LocalName == "Style" && e.Attribute(X + "Key") is not null)
            .ToDictionary(e => (string)e.Attribute(X + "Key")!);
        string Setter(string style, string property) =>
            (string)styles[style].Elements().Single(s => (string?)s.Attribute("Property") == property).Attribute("Value")!;
        XElement scroll = Named(PageXaml, "SettingsScroll");

        Assert.Equal("{StaticResource ScrollBarGutter}", (string?)scroll.Attribute("Padding"));
        Assert.Equal("{StaticResource SettingChoiceMinWidth}", Setter("SettingComboBoxStyle", "MinWidth"));

        foreach (string style in new[] { "SettingComboBoxStyle", "SettingButtonStyle", "SettingSubtleButtonStyle" })
        {
            Assert.Equal("{StaticResource RadiusMedium}", Setter(style, "CornerRadius"));
        }

        var unrounded = scroll.Descendants()
            .Where(e => e.Name.LocalName is "Button" or "ComboBox")
            .Select(e => (string?)e.Attribute("Style"))
            .Where(s => s is not ("{StaticResource SettingButtonStyle}" or "{StaticResource SettingSubtleButtonStyle}" or "{StaticResource SettingComboBoxStyle}"))
            .ToList();
        Assert.True(unrounded.Count == 0, "A control in a settings group without a settings style: " + string.Join(", ", unrounded));

        // The shared button template draws the radius it is given, so the style's setter is what the user sees.
        XDocument buttons = XDocument.Load(SourcePath("src", "Noto.UI", "Styles", "Buttons.xaml"));
        XElement template = buttons.Descendants().Single(e => (string?)e.Attribute(X + "Key") == "FolderPaneButtonStyle");
        var radii = template.Descendants().Select(e => (string?)e.Attribute("CornerRadius")).Where(r => r is not null).ToList();
        Assert.Equal(3, radii.Count);
        Assert.All(radii, r => Assert.Equal("{TemplateBinding CornerRadius}", r));
    }

    [Fact]
    public void The_page_itself_is_the_settings_root_ui_automation_can_see()
    {
        // Measured on the frozen Release build: SettingsRoot on the page's inner grid never reached UI Automation; a
        // panel has no automation peer, and neither has a UserControl. The page carries the id and the name and creates
        // its own peer, so the id names an element that exists. What UI Automation then shows, through each entry and
        // after each close, is Invoke-Settings.ps1 case 1's to prove.
        XElement root = PageXaml.Root!;

        Assert.Equal("SettingsRoot", (string?)root.Attribute("AutomationProperties.AutomationId"));
        Assert.Equal("Settings", (string?)root.Attribute("AutomationProperties.Name"));
        Assert.Matches(@"override [\w.]*AutomationPeer OnCreateAutomationPeer\(\) =>\s*new [\w.]*AutomationPeer\(this\);", Page);

        // No id anywhere in the window is set where UI Automation cannot see it.
        string[] peerless = ["Grid", "StackPanel", "Canvas", "RelativePanel", "VariableSizedWrapGrid", "Border", "Viewbox"];
        var hidden = Directory.EnumerateFiles(Windows, "*.xaml")
            .SelectMany(f => XDocument.Load(f).Descendants())
            .Where(e => e.Attribute("AutomationProperties.AutomationId") is not null && peerless.Contains(e.Name.LocalName))
            .Select(e => $"{e.Name.LocalName} {(string?)e.Attribute("AutomationProperties.AutomationId")}")
            .ToList();
        Assert.True(hidden.Count == 0, "AutomationId on an element with no automation peer: " + string.Join(", ", hidden));
    }

    [Fact]
    public void Nothing_beyond_the_approved_scope_is_built()
    {
        // No navigation view, no second window, no new activation surface.
        string views = string.Join('\n', Directory.EnumerateFiles(Windows, "*.xaml").Select(File.ReadAllText));
        string code = string.Join('\n', Directory.EnumerateFiles(Windows, "*.cs").Select(Code));

        Assert.DoesNotContain("NavigationView", views, StringComparison.Ordinal);
        Assert.Equal(1, Regex.Count(code, @"partial class \w+ : Window\b"));
        Assert.DoesNotContain("Storyboard", views, StringComparison.Ordinal);

        // Motion (Slice 1.3) runs on the compositor in one place, never through storyboards or anywhere else.
        var composing = Directory.EnumerateFiles(Windows, "*.cs").Where(f => Code(f).Contains("ElementCompositionPreview", StringComparison.Ordinal)).Select(Path.GetFileName).ToList();
        Assert.Equal(["ContentMotion.cs"], composing);
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
