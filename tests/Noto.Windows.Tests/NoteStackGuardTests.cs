using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// The note stack's structure (ADR-016), checked in its source as the other wiring guards are: this project must not
/// reference <c>Noto.Windows</c>. What the stack does at run time is <c>Invoke-NoteStack.ps1</c>'s; its rules are
/// <c>NoteStackSession</c>'s, tested against real SQLite.
/// </summary>
public sealed class NoteStackGuardTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly string Windows = Path.GetDirectoryName(SourcePath("src", "Noto.Windows", "App.xaml.cs"))!;
    private static readonly string Main = Code(Path.Join(Windows, "MainWindow.xaml.cs"));
    private static readonly XDocument MainXaml = XDocument.Load(Path.Join(Windows, "MainWindow.xaml"));

    [Fact]
    public void Only_the_editor_host_references_its_inner_control()
    {
        // ADR-016 §3: P2 replaced the host's inside (ADR-017: a RichEditBox), and nothing outside it may reach it.
        var reaching = Directory.EnumerateFiles(Windows, "*.*")
            .Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".xaml", StringComparison.Ordinal))
            .Where(f => !Path.GetFileName(f).StartsWith("NoteEditorHost.", StringComparison.Ordinal))
            .Where(f => Regex.IsMatch(File.ReadAllText(f), @"\bNoteEditor\b"))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(reaching.Count == 0, "References the editor's inner control: " + string.Join(", ", reaching));
        Assert.Matches(@"<local:EditorBox\s+x:Name=""NoteEditor""", File.ReadAllText(Path.Join(Windows, "NoteEditorHost.xaml")));
    }

    [Fact]
    public void Only_the_card_being_edited_holds_an_editor_and_a_bar_and_it_alone_hides_its_read_view()
    {
        XElement template = Card();

        Assert.Equal("{x:Bind IsEditing, Mode=OneWay}", (string?)Named(template, "CardEditor").Attribute(X + "Load"));
        Assert.Equal("{x:Bind IsEditing, Mode=OneWay}", (string?)Named(template, "CardBar").Attribute(X + "Load"));
        Assert.Equal("{x:Bind IsReading, Mode=OneWay}", (string?)Named(template, "CardReadView").Attribute(X + "Load"));
        Assert.Equal("NoteEditorHost", Named(template, "CardEditor").Name.LocalName);
        Assert.Single(template.Descendants(), e => e.Name.LocalName == "NoteEditorHost");
    }

    [Fact]
    public void A_cards_name_is_its_title_and_its_states_are_its_help_text()
    {
        XElement root = Card().Elements().Single();

        Assert.Equal("{x:Bind DisplayTitle, Mode=OneWay}", (string?)root.Attribute("AutomationProperties.Name"));
        Assert.Equal("{x:Bind HelpText, Mode=OneWay}", (string?)root.Attribute("AutomationProperties.HelpText"));
    }

    [Fact]
    public void A_rows_name_says_its_state_after_its_title_from_the_one_helper_and_follows_it()
    {
        // #201: Narrator reads a list item's name and position, never its help text, so a row's name is its title then
        // its state, from its item's ToString. A container reads that once, when it is made, so the one place every state
        // change goes through (SetRowState) names it again (measured: a fold left the old name, Invoke-NoteStack 23).
        Assert.Matches(
            @"if \(row is SelectorItem \{ Content: NoteCard or FolderListItem \} item\)\s*\{\s*AutomationProperties\.SetName\(row, item\.Content\.ToString\(\)\);",
            Code(Path.Join(Windows, "Accessible.cs")));
        string card = Code(Path.Join(Windows, "NoteCard.cs"));
        string folder = Code(Path.Join(Windows, "FolderListItem.cs"));

        Assert.Contains("public override string ToString() => RowState.Name(DisplayTitle, HelpText);", card, StringComparison.Ordinal);
        Assert.Contains("RowState.Of(IsPinned, IsFolded, HiddenLines, NotePalette.NameOf(ColorKey), Mode == NoteMode.Markdown ? string.Empty : NameOf(Mode));", card, StringComparison.Ordinal);
        Assert.Contains("public override string ToString() => RowState.Name(Name, RowState.Of(IsPinned, folded: false, hiddenLines: 0));", folder, StringComparison.Ordinal);
        Assert.Contains("public string HelpText => RowState.Of(IsPinned, folded: false, hiddenLines: 0,", folder, StringComparison.Ordinal);

        // Each state a card changes in place tells its bindings the help text changed; the name is read from the same.
        foreach (string setter in new[] { "IsFolded", "ColorKey", "Mode", "Content" })
        {
            Assert.Contains("Changed(nameof(HelpText));", Property(card, setter), StringComparison.Ordinal);
        }

        Assert.DoesNotMatch(@"AutomationProperties\.SetName\((args\.ItemContainer|container)\b", Main);
    }

    [Fact]
    public void An_editor_loads_the_stacks_text_for_its_card_so_a_recycled_container_loses_nothing()
    {
        // The text lives in the session, not the control: a realised editor loads it, every change goes back to it,
        // and an editor for a card that is not the editing one loads nothing.
        string loaded = Body(Main, "AttachEditor");
        Assert.Contains("_stack.Editing is not { } editing || editing.Value != card.Id", loaded, StringComparison.Ordinal);
        Assert.Contains("(host.Note is { } shown && shown.Value == card.Id)", loaded, StringComparison.Ordinal);
        Assert.Contains("host.Load(NoteId.From(card.Id), _stack.Draft, card.Mode);", loaded, StringComparison.Ordinal);

        // Both ways a card reaches its editor attach it: realised, or a recycled container given the card afterwards.
        Assert.Contains("private void OnEditorHostLoaded(object sender, RoutedEventArgs e) => AttachEditor(sender as NoteEditorHost);", Main, StringComparison.Ordinal);
        Assert.Matches(@"if \(sender is NoteEditorHost \{ IsLoaded: true \} host\)\s*\{\s*AttachEditor\(host\);", Body(Main, "OnEditorHostDataContextChanged"));
        XElement editor = Named(Card(), "CardEditor");
        Assert.Equal("OnEditorHostDataContextChanged", (string?)editor.Attribute("DataContextChanged"));
        Assert.Contains("_stack.UpdateDraft(note, host.Markdown);", Body(Main, "OnEditorContentChanged"), StringComparison.Ordinal);
    }

    [Fact]
    public void Typing_arms_a_one_shot_autosave_and_every_save_attempt_settles_it()
    {
        // P2.1 (parity B19): each change goes to the session and then arms the write, which saves as leaving does.
        Assert.Matches(@"_stack\.UpdateDraft\(note, host\.Markdown\);\s*ArmAutosave\(\);", Body(Main, "OnEditorContentChanged"));
        Assert.Contains("_autosave.IsRepeating = false;", Main, StringComparison.Ordinal);
        Assert.Contains("_autosave.Tick += OnAutosaveTick;", Main, StringComparison.Ordinal);
        Assert.Contains("private void OnAutosaveTick(DispatcherQueueTimer sender, object args) => _ = TrySaveCard();", Main, StringComparison.Ordinal);

        // The delay is the policy's, counted from when the text first became unsaved; text typed back to what is
        // stored arms nothing.
        string arm = Body(Main, "ArmAutosave");
        Assert.Matches(@"^\{\s*if \(!_stack\.IsDirty\)\s*\{\s*StopAutosave\(\);\s*return;", arm);
        Assert.Matches(@"if \(_unsavedSince == 0\)\s*\{\s*_unsavedSince = Stopwatch\.GetTimestamp\(\);", arm);
        Assert.Matches(@"_autosave\.Stop\(\);\s*_autosave\.Interval = Autosave\.Delay\(Stopwatch\.GetElapsedTime\(_unsavedSince\)\);\s*_autosave\.Start\(\);", arm);

        // Any attempt, explicit or timed, takes the pending write's place. Success clears the bound; failure restarts
        // it, so a note that cannot be saved is not retried on every key and no timer replaces the failure's notice.
        string save = Body(Main, "TrySaveCard");
        Assert.Matches(@"^\{\s*_editor\?\.ReadBack\(\);\s*if \(!_stack\.IsDirty\)\s*\{\s*StopAutosave\(\);\s*return true;", save);
        Assert.Matches(@"_autosave\.Stop\(\);\s*_unsavedSince = started;\s*try", save);
        Assert.Matches(@"if \(result\.IsSuccess\)\s*\{\s*StopAutosave\(\);", save);
        Assert.Matches(@"^\{\s*_autosave\.Stop\(\);\s*_unsavedSince = 0;\s*\}$", Body(Main, "StopAutosave"));
    }

    [Fact]
    public void Every_way_out_of_editing_saves_first_and_a_failed_save_stays()
    {
        // Leaving the folder, editing another card and creating a note each end the current edit, and stop if it fails.
        Assert.Matches(@"^\{\s*if \(!TryEndEdit\(\)\)\s*\{\s*return;", Body(Main, "LeaveFolder"));
        Assert.Contains("if (!TryEndEdit())", Body(Main, "EditCard"), StringComparison.Ordinal);
        Assert.Contains("|| !TryEndEdit()", Body(Main, "CreateNoteInOpenFolder"), StringComparison.Ordinal);
        Assert.Matches(@"if \(!TrySaveCard\(\)\)\s*\{\s*return false;", Body(Main, "TryEndEdit"));

        // A successful save clears a failure's notice, however the user then left: it no longer applies.
        Assert.Matches(@"Noto\.Save completed[^\n]*\n\s*HideNoteNotice\(\);\s*return true;", Body(Main, "TrySaveCard"));

        // Hiding and closing save without leaving.
        Assert.Contains("internal bool SaveBeforeHide() => TrySaveCard();", Main, StringComparison.Ordinal);
        Assert.Contains("TrySaveCard(CloseAgainToDiscard)", Body(Main, "OnAppWindowClosing"), StringComparison.Ordinal);
    }

    [Fact]
    public void Enter_and_F2_edit_only_from_a_card_never_from_the_editor()
    {
        // Keys typed in the editor tunnel through the list's PreviewKeyDown: Enter there is a new line.
        Assert.Contains(
            "if (e.Key is VirtualKey.Enter or VirtualKey.F2 && e.OriginalSource is ListViewItem { Content: NoteCard card })",
            Body(Main, "OnNoteListPreviewKeyDown"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Every_card_action_is_in_its_menu_with_its_key()
    {
        string menu = Body(Main, "OnNoteListContextRequested");

        Assert.Contains(@"MenuItem(""Edit"", ""Enter"",", menu, StringComparison.Ordinal);
        Assert.Contains(@"MenuItem(""New note"", ""Ctrl+N"",", menu, StringComparison.Ordinal);
        Assert.Contains(@"MenuItem(""Move to the Recycle Bin"", ""Ctrl+Alt+Backspace"",", menu, StringComparison.Ordinal);
        Assert.Equal("OnNoteListContextRequested", (string?)Named(MainXaml.Root!, "NoteList").Attribute("ContextRequested"));

        // A keyboard request (Shift+F10, the Menu key) comes from the row's container, whose item is its Content: both
        // menus find their row that way first, or no menu opens from the keyboard (measured on the runtime case).
        Assert.Contains("ItemOf<NoteCard>(args.OriginalSource) is not { } card", menu, StringComparison.Ordinal);
        Assert.Contains("source is ListViewItem { Content: T item } ? item : (source as FrameworkElement)?.DataContext as T;", Main, StringComparison.Ordinal);

        // The folder list's menu, likewise.
        string folders = Body(Main, "OnFolderListContextRequested");
        Assert.Contains(@"MenuItem(""Open"", ""Ctrl+Down"", EnterSelectedFolder)", folders, StringComparison.Ordinal);
        Assert.Contains("ItemOf<FolderListItem>(args.OriginalSource) is not { } folder", folders, StringComparison.Ordinal);
        Assert.Contains(@"MenuItem(""Rename"", ""F2"", BeginRename)", folders, StringComparison.Ordinal);
        Assert.Contains(@"MenuItem(""New folder"", ""Ctrl+N"", FocusInput)", folders, StringComparison.Ordinal);
        Assert.Equal("OnFolderListContextRequested", (string?)Named(MainXaml.Root!, "FolderList").Attribute("ContextRequested"));
    }

    [Fact]
    public void Moving_is_one_path_from_the_key_and_the_menu_saves_first_and_never_offers_the_cards_own_folder()
    {
        // Ctrl+Shift+M (G16) on the stack's root, so it works from a card and from its editor.
        XElement move = MainXaml.Root!.Descendants().Single(e => e.Name.LocalName == "KeyboardAccelerator" && (string?)e.Attribute("Invoked") == "OnMoveAccelerator");
        Assert.Equal("M", (string?)move.Attribute("Key"));
        Assert.Equal("Control,Shift", (string?)move.Attribute("Modifiers"));
        Assert.Equal("NoteRoot", (string?)move.Ancestors().First(e => e.Attribute(X + "Name") is not null).Attribute(X + "Name"));

        // The key's menu and the card menu's "Move to" are built by the same method.
        Assert.Contains("MoveDestinations(id)", Body(Main, "OnMoveAccelerator"), StringComparison.Ordinal);
        Assert.Contains("MoveDestinations(card.Id)", Body(Main, "OnNoteListContextRequested"), StringComparison.Ordinal);

        // Recent first, then all others; the card's own folder is excluded from both.
        string destinations = Body(Main, "MoveDestinations");
        Assert.Contains("RecentFolders.Shown(RecentMoveFolders?.Invoke() ?? [], folders, _openFolderId)", destinations, StringComparison.Ordinal);
        Assert.Contains("folders.Where(f => f.Id != _openFolderId)", destinations, StringComparison.Ordinal);
        Assert.True(destinations.IndexOf("recent", StringComparison.Ordinal) < destinations.IndexOf("\"All folders\"", StringComparison.Ordinal));

        // The card being edited is saved first, and the move goes through the session.
        string moveCard = Body(Main, "MoveCard");
        Assert.Contains("(_stack.Editing?.Value == id && !TryEndEdit())", moveCard, StringComparison.Ordinal);
        Assert.Contains("_stack.Move(note, target)", moveCard, StringComparison.Ordinal);
        Assert.True(moveCard.IndexOf("_stack.AfterRemoval(note)", StringComparison.Ordinal) < moveCard.IndexOf("_stack.Move(", StringComparison.Ordinal));
    }

    [Fact]
    public void Move_to_ends_with_new_folder_which_creates_through_the_panes_command_then_moves_the_ordinary_way()
    {
        // #189: last, after a separator, in both the key's menu and the card menu (they share MoveDestinations).
        string destinations = Body(Main, "MoveDestinations");
        Assert.Matches(
            @"items\.Add\(new MenuFlyoutSeparator\(\)\);\s*items\.Add\(MenuItem\(""New folder…"", string\.Empty, \(\) => _ = DispatcherQueue\.TryEnqueueGuarded\(Microsoft\.UI\.Dispatching\.DispatcherQueuePriority\.Low, \(\) => AskMoveToNewFolder\(id\)\)\)\);\s*return items;",
            destinations);
        Assert.True(destinations.IndexOf("\"All folders\"", StringComparison.Ordinal) < destinations.IndexOf("\"New folder…\"", StringComparison.Ordinal));

        // A named, identified name box; Enter confirms; a refusal is the pane's words, announced, and keeps the box.
        string ask = Body(Main, "AskMoveToNewFolder");
        Assert.Contains(@"AutomationProperties.SetName(name, ""New folder name"");", ask, StringComparison.Ordinal);
        Assert.Contains(@"AutomationProperties.SetAutomationId(name, ""MoveNewFolderName"");", ask, StringComparison.Ordinal);
        Assert.Contains(@"AutomationProperties.SetName(move, ""Create folder and move the note"");", ask, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.SetLiveSetting(refusal, AutomationLiveSetting.Assertive);", ask, StringComparison.Ordinal);
        Assert.Contains("Accessible.SetAnnounces(refusal, true);", ask, StringComparison.Ordinal);
        Assert.Contains("flyout.Opened += (_, _) => _ = name.Focus(FocusState.Programmatic);", ask, StringComparison.Ordinal);
        Assert.Matches(@"if \(e\.Key == VirtualKey\.Enter\)\s*\{\s*e\.Handled = true;\s*Submit\(\);", ask);
        Assert.Contains("Refuse(Describe(created.Failure!));", ask, StringComparison.Ordinal);

        // Saved first, then the pane's own CreateFolder (no second creation path), then the ordinary move into it.
        int save = ask.IndexOf("(_stack.Editing is { } editing && editing.Value == id && !TryEndEdit())", StringComparison.Ordinal);
        int create = ask.IndexOf("_commands.Dispatch(new CreateFolder(name.Text))", StringComparison.Ordinal);
        int move = ask.IndexOf("MoveCard(id, created.Value, FolderName.Normalise(name.Text)!);", StringComparison.Ordinal);
        Assert.True(save >= 0 && save < create && create < move, $"save={save} create={create} move={move}");
        Assert.Equal(2, Regex.Count(Main, @"new CreateFolder\(")); // the pane's create row, and this
    }

    [Fact]
    public void Folder_history_records_every_entry_and_switches_back_through_one_path_that_saves_first()
    {
        // #193. Every route into a folder (click, Enter, Ctrl+Down, Search, the new-note shortcut) is EnterSelectedFolder,
        // which records it in its own list, never the move list.
        string history = Code(Path.Join(Windows, "MainWindow.FolderHistory.cs"));
        string app = Code(Path.Join(Windows, "App.xaml.cs"));
        Assert.Contains("FolderOpened?.Invoke(_openFolderId.Value, [.. Folders.Select(f => FolderId.From(f.Id))]);", Body(Main, "EnterSelectedFolder"), StringComparison.Ordinal);
        Assert.Contains("RecentOpenedFolders = () => preferences.RecentOpenedFolders,", app, StringComparison.Ordinal);
        Assert.Contains("FolderOpened = (folder, existing) => preferences.RememberOpenedFolder(folder, existing),", app, StringComparison.Ordinal);
        Assert.DoesNotContain("RecentMoveFolders", history, StringComparison.Ordinal);

        // Ctrl+Shift+O (G10, §10.2) on the folder list's root and the stack's, so it works from a row, a card and its
        // editor, and from no other surface (a collapsed surface's accelerators do not run).
        var keys = MainXaml.Root!.Descendants().Where(e => e.Name.LocalName == "KeyboardAccelerator" && (string?)e.Attribute("Invoked") == "OnLastFolderAccelerator").ToList();
        Assert.Equal(["PaneRoot", "NoteRoot"], keys.Select(k => (string?)k.Ancestors().First(e => e.Attribute(X + "Name") is not null).Attribute(X + "Name")));
        Assert.All(keys, k => Assert.Equal("O", (string?)k.Attribute("Key")));
        Assert.All(keys, k => Assert.Equal("Control,Shift", (string?)k.Attribute("Modifiers")));

        // The key, Back's menu and the start all take the first recent folder other than the open one, and enter it by
        // one path, which leaves the open folder through LeaveFolder (saving first) and stops if that fails.
        Assert.Contains("RecentFolders.Shown(RecentOpenedFolders?.Invoke() ?? [], _listFolders.Execute(), _openFolderId)", Body(history, "OtherRecentFolders"), StringComparison.Ordinal);
        Assert.Contains("OtherRecentFolders() is [var previous, ..]", Body(history, "OnLastFolderAccelerator"), StringComparison.Ordinal);
        Assert.Contains("() => OpenFolder(id)", Body(history, "RecentFoldersMenu"), StringComparison.Ordinal);
        Assert.Matches(@"LeaveFolder\(\);\s*if \(_openFolderId is not null\)\s*\{\s*return;\s*\}", Body(history, "OpenFolder"));
        Assert.True(Body(history, "OpenFolder").IndexOf("LeaveFolder()", StringComparison.Ordinal) < Body(history, "OpenFolder").IndexOf("EnterSelectedFolder()", StringComparison.Ordinal));

        // Back's context menu (right-click, Shift+F10, the Menu key) and its chevron open the same menu; both are named.
        XElement back = Named(MainXaml.Root!, "BackButton");
        XElement chevron = Named(MainXaml.Root!, "RecentFoldersButton");
        Assert.Equal("OnBackContextRequested", (string?)back.Attribute("ContextRequested"));
        Assert.Equal("OnRecentFoldersClick", (string?)chevron.Attribute("Click"));
        Assert.Equal("Recent folders", (string?)chevron.Attribute("AutomationProperties.Name"));
        Assert.Contains("ShowMenu(RecentFoldersMenu(), BackButton, args);", Body(history, "OnBackContextRequested"), StringComparison.Ordinal);
        Assert.Contains("RecentFoldersMenu().ShowAt(RecentFoldersButton)", history, StringComparison.Ordinal);

        // The start opens the last folder only when the setting says so and the folder list has nothing to say first;
        // after the first load, before the welcome.
        string first = Body(Main, "OnPaneFirstLoaded");
        Assert.Matches(@"Refresh\(\);\s*OpenLastFolderAtStart\(\);\s*OpenWelcomeAtStart\(\);", first);
        Assert.Contains("!OpenLastFolderOnStart", Body(history, "OpenLastFolderAtStart"), StringComparison.Ordinal);
        Assert.Contains("NoticeText.Visibility == Visibility.Visible", Body(history, "OpenLastFolderAtStart"), StringComparison.Ordinal);
    }

    [Fact]
    public void Fold_keys_are_left_to_fold_right_to_unfold_and_shift_for_every_card_from_a_card_or_its_editor()
    {
        // G19-G22, once, on the list's PreviewKeyDown, ahead of the Enter/F2 handling and of the TextBox.
        string keys = Body(Main, "OnNoteListPreviewKeyDown");
        Assert.Matches(
            @"if \(e\.Key is VirtualKey\.Left or VirtualKey\.Right && IsAltDown\(\) && \(IsControlDown\(\) \|\| IsShiftDown\(\)\)\)\s*\{\s*e\.Handled = true;\s*bool fold = e\.Key == VirtualKey\.Left;\s*if \(IsControlDown\(\) && IsShiftDown\(\)\)\s*\{\s*FoldAll\(fold\);\s*\}\s*else if \(CurrentCard\(\) is \{ \} id\)\s*\{\s*FoldCard\(id, fold\);",
            keys);
        Assert.True(keys.IndexOf("VirtualKey.Left or VirtualKey.Right", StringComparison.Ordinal) < keys.IndexOf("VirtualKey.Enter", StringComparison.Ordinal));
    }

    [Fact]
    public void Alt_Shift_Left_and_Right_fold_and_unfold_one_card_because_Narrator_takes_Ctrl_Alt_arrows()
    {
        // #202: Narrator answers every Ctrl+Alt+arrow (and Ctrl+Alt+Shift+arrow) itself. Alt+Shift+Left/Right fold and
        // unfold one card; only Ctrl+Alt+Shift is every card, so Alt+Shift never folds the whole folder.
        string keys = Body(Main, "OnNoteListPreviewKeyDown");
        Assert.Contains("IsAltDown() && (IsControlDown() || IsShiftDown())", keys, StringComparison.Ordinal);
        Assert.Contains("if (IsControlDown() && IsShiftDown())", keys, StringComparison.Ordinal);

        // Ctrl+Alt+Shift+Up/Down (reorder) still come first, so Alt+Shift+Up/Down reach the editor's leave chords.
        Assert.True(keys.IndexOf("IsControlDown() && IsAltDown() && IsShiftDown())", StringComparison.Ordinal) < keys.IndexOf("IsAltDown() && (IsControlDown() || IsShiftDown())", StringComparison.Ordinal));
    }

    [Fact]
    public void Folding_the_card_being_edited_or_folding_all_saves_first_and_a_failed_save_folds_nothing()
    {
        Assert.Contains("(folded && _stack.Editing?.Value == id && !TryEndEdit())", Body(Main, "FoldCard"), StringComparison.Ordinal);
        Assert.Contains("(folded && !TryEndEdit())", Body(Main, "FoldAll"), StringComparison.Ordinal);

        // One card is updated in place, keeping its container and focus; fold all re-reads and refocuses.
        Assert.Contains("card.IsFolded = folded;", Body(Main, "FoldCard"), StringComparison.Ordinal);
        Assert.DoesNotContain("ShowCards(", Body(Main, "FoldCard"), StringComparison.Ordinal);
        Assert.Contains("ShowCards(_stack.Notes);", Body(Main, "FoldAll"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_list_bar_has_a_named_menu_of_fold_all_and_unfold_all_with_their_keys_offered_only_when_they_apply()
    {
        // B13, B18 (#190): a "More" button between the count and New note, so the tab order is the list, the menu, New note.
        XElement button = Named(MainXaml.Root!, "NoteListMenuButton");
        Assert.Equal("More", (string?)button.Attribute("AutomationProperties.Name"));
        Assert.Equal("OnNoteListMenuClick", (string?)button.Attribute("Click"));
        Assert.Null(button.Attribute("IsTabStop"));
        Assert.Equal(
            ["NoteCountText", "NoteListMenuButton", "NewNoteButton"],
            button.Parent!.Elements().Select(e => (string?)e.Attribute(X + "Name")).OfType<string>());
        Assert.Contains("NoteListMenuButton.IsEnabled = interactive;", Body(Main, "ApplyNoteState"), StringComparison.Ordinal);

        // The same commands as Ctrl+Alt+Shift+Left and Right, showing those keys; disabled when they would change nothing.
        string menu = Body(Main, "OnNoteListMenuClick");
        Assert.Contains(@"MenuItem(""Fold all"", ""Ctrl+Alt+Shift+Left"", () => FoldAll(folded: true), _stack.CanFoldAll(folded: true))", menu, StringComparison.Ordinal);
        Assert.Contains(@"MenuItem(""Unfold all"", ""Ctrl+Alt+Shift+Right"", () => FoldAll(folded: false), _stack.CanFoldAll(folded: false))", menu, StringComparison.Ordinal);
    }

    [Fact]
    public void A_double_click_folds_a_card_that_was_not_being_edited_and_never_one_already_in_the_editor()
    {
        // B12 (#190): decided on the second press, which the editor opened by the first click handles, so handled too.
        Assert.Contains(
            "NoteList.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnNoteListPointerPressed), handledEventsToo: true);",
            Main,
            StringComparison.Ordinal);

        // Only a click that began editing a card can start a double-click; a click in the editor began nothing.
        string tapped = Body(Main, "OnNoteListTapped");
        Assert.Matches(@"bool wasFolded = card\.IsFolded;\s*EditCard\(card\.Id, CaretPlacement\.End\);\s*_cardClick = _stack\.Editing is \{ \} editing && editing\.Value == card\.Id", tapped);
        Assert.Matches(@"if \(Environment\.TickCount64 - _foldedByClickAt <= _system\.DoubleClickTime\)\s*\{\s*e\.Handled = true;\s*return;\s*\}", tapped);
        Assert.True(tapped.IndexOf("_foldedByClickAt", StringComparison.Ordinal) < tapped.IndexOf("EditCard(", StringComparison.Ordinal), "The second click's tap would edit the folded card again.");

        // The second press: the left button, soon and close (Windows' rule, in Core), on the card the first click edited.
        // The click is used once; the card leaves editing, saved, and toggles from what it was before the first click.
        string pressed = Body(Main, "OnNoteListPointerPressed");
        Assert.Contains("!point.Properties.IsLeftButtonPressed", pressed, StringComparison.Ordinal);
        Assert.Contains("_cardClick = null;", pressed, StringComparison.Ordinal);
        Assert.Contains("_stack.Editing is not { } editing || editing.Value != first.Id", pressed, StringComparison.Ordinal);
        Assert.Contains("DoubleClick.IsSecond(first.At, first.Position.X, first.Position.Y, now, point.Position.X, point.Position.Y, _system.DoubleClickTime)", pressed, StringComparison.Ordinal);
        Assert.Matches(@"_foldedByClickAt = now;\s*if \(TryEndEdit\(\)\)\s*\{\s*FoldCard\(first\.Id, folded: !first\.WasFolded\);", pressed);
    }

    [Fact]
    public void Menus_name_the_pin_and_fold_action_that_applies_and_pin_has_no_key()
    {
        string card = Body(Main, "OnNoteListContextRequested");
        Assert.Contains(@"card.IsPinned
            ? MenuItem(""Unpin"", string.Empty, () => PinCard(card.Id, pinned: false))
            : MenuItem(""Pin"", string.Empty, () => PinCard(card.Id, pinned: true))", card, StringComparison.Ordinal);
        Assert.Contains(@"card.IsFolded
            ? MenuItem(""Unfold"", ""Ctrl+Alt+Right or Alt+Shift+Right"", () => FoldCard(card.Id, folded: false))
            : MenuItem(""Fold"", ""Ctrl+Alt+Left or Alt+Shift+Left"", () => FoldCard(card.Id, folded: true))", card, StringComparison.Ordinal);

        string folder = Body(Main, "OnFolderListContextRequested");
        Assert.Contains(@"folder.IsPinned
            ? MenuItem(""Unpin"", string.Empty, () => PinFolder(folder.Id, pinned: false))
            : MenuItem(""Pin"", string.Empty, () => PinFolder(folder.Id, pinned: true))", folder, StringComparison.Ordinal);

        // Decision 3: no pin chord anywhere.
        Assert.DoesNotContain(MainXaml.Root!.Descendants(), e => e.Name.LocalName == "KeyboardAccelerator" && ((string?)e.Attribute("Invoked"))?.Contains("Pin", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Folded_and_pinned_states_are_visible_and_announced_not_only_drawn()
    {
        XElement template = Card();
        Assert.Single(template.Descendants(), e => e.Name.LocalName == "FontIcon" && (string?)e.Attribute("Visibility") == "{x:Bind FoldedMarkerVisibility, Mode=OneWay}");

        XElement folderRow = Named(MainXaml.Root!, "FolderList").Descendants().Single(e => e.Name.LocalName == "DataTemplate").Elements().Single();
        Assert.Equal("{x:Bind HelpText}", (string?)folderRow.Attribute("AutomationProperties.HelpText"));

        // UI Automation reports the row's container, not its template (measured by Invoke-NoteStack.ps1 cases 22-30):
        // every realised or recycled row of both lists gets its help text on the container, and a fold changed in
        // place updates it.
        Assert.Contains("FolderList.ContainerContentChanging += OnRowContainerContentChanging;", Main, StringComparison.Ordinal);
        Assert.Contains("NoteList.ContainerContentChanging += OnRowContainerContentChanging;", Main, StringComparison.Ordinal);
        string rows = Body(Main, "OnRowContainerContentChanging");
        Assert.Contains("NoteCard card => card.HelpText,", rows, StringComparison.Ordinal);
        Assert.Contains("FolderListItem folder => folder.HelpText,", rows, StringComparison.Ordinal);
        Assert.Contains("Accessible.SetRowState(args.ItemContainer, help);", rows, StringComparison.Ordinal);
        Assert.Contains("Accessible.SetRowState(container, card.HelpText);", Body(Main, "FoldCard"), StringComparison.Ordinal);
    }

    [Fact]
    public void Reorder_keys_come_first_on_both_lists_so_no_other_chord_takes_them()
    {
        // G17, G18: Ctrl+Alt+Shift+Up/Down. On the stack, ahead of fold and Enter here, and of the editor's own
        // Ctrl+Alt+Up/Down (it tunnels through this handler first). On the folder list, ahead of Ctrl+Down (enter).
        string notes = Body(Main, "OnNoteListPreviewKeyDown");
        string chord = "if (e.Key is VirtualKey.Up or VirtualKey.Down && IsControlDown() && IsAltDown() && IsShiftDown())";
        Assert.StartsWith("{", notes, StringComparison.Ordinal);
        Assert.True(notes.IndexOf(chord, StringComparison.Ordinal) is > 0 and var first && first < notes.IndexOf("VirtualKey.Left or VirtualKey.Right", StringComparison.Ordinal));
        Assert.Contains("ReorderCard(current, e.Key == VirtualKey.Up ? RepositionDirection.Up : RepositionDirection.Down);", notes, StringComparison.Ordinal);

        string folders = Body(Main, "OnFolderListPreviewKeyDown");
        Assert.True(folders.IndexOf(chord, StringComparison.Ordinal) is > 0 and var reorder && reorder < folders.IndexOf("EnterSelectedFolder()", StringComparison.Ordinal));
        Assert.Contains("MoveFolder(folder.Id, e.Key == VirtualKey.Up ? RepositionDirection.Up : RepositionDirection.Down);", folders, StringComparison.Ordinal);
    }

    [Fact]
    public void Reordering_asks_the_rule_and_dispatches_only_a_real_move()
    {
        string card = Body(Main, "ReorderCard");
        Assert.Contains("!_stack.CanReorder(NoteId.From(id), direction)", card, StringComparison.Ordinal);
        Assert.Contains("_stack.Reorder(NoteId.From(id), direction)", card, StringComparison.Ordinal);

        string folder = Body(Main, "MoveFolder");
        Assert.Contains("FolderReposition.For(_listFolders.Execute(), FolderId.From(id), direction)", folder, StringComparison.Ordinal);
        Assert.True(folder.IndexOf("if (!target.Moves)", StringComparison.Ordinal) < folder.IndexOf("_commands.Dispatch(new ReorderFolder(FolderId.From(id), target.After))", StringComparison.Ordinal));

        // Menus: top and bottom, each disabled when the rule says it would not move.
        Assert.Contains("_stack.CanReorder(NoteId.From(card.Id), RepositionDirection.Top)", Body(Main, "OnNoteListContextRequested"), StringComparison.Ordinal);
        Assert.Contains("FolderReposition.For(folders, FolderId.From(folder.Id), RepositionDirection.Bottom).Moves", Body(Main, "OnFolderListContextRequested"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("NoteList", "OnNoteDragStarting", "OnNoteDragCompleted")]
    [InlineData("FolderList", "OnFolderDragStarting", "OnFolderDragCompleted")]
    public void Both_lists_use_their_own_reorder_with_one_completion_handler_and_take_nothing_from_outside(string list, string starting, string completed)
    {
        XElement view = Named(MainXaml.Root!, list);
        Assert.Equal("True", (string?)view.Attribute("CanDragItems"));
        Assert.Equal("True", (string?)view.Attribute("CanReorderItems"));
        Assert.Equal("True", (string?)view.Attribute("AllowDrop"));
        Assert.Equal(starting, (string?)view.Attribute("DragItemsStarting"));
        Assert.Equal(completed, (string?)view.Attribute("DragItemsCompleted"));

        // In-app reorder only: no handler that reads dropped data, so a file or text from outside Noto is not taken.
        Assert.Null(view.Attribute("Drop"));
        Assert.Null(view.Attribute("DragOver"));
        Assert.DoesNotContain("DataView", Main, StringComparison.Ordinal);
        Assert.DoesNotContain("GetStorageItemsAsync", Main, StringComparison.Ordinal);
    }

    [Fact]
    public void A_drop_asks_the_rule_and_the_list_is_rebuilt_from_the_session_or_the_database_whatever_happened()
    {
        string notes = Body(Main, "OnNoteDragCompleted");
        Assert.Contains("_stack.Drop(NoteId.From(id), landedAt)", notes, StringComparison.Ordinal);
        Assert.Contains("args.DropResult == DataPackageOperation.None ? -1 : Notes.IndexOf(dropped)", notes, StringComparison.Ordinal);
        Assert.True(notes.IndexOf("_stack.Drop(", StringComparison.Ordinal) < notes.IndexOf("ShowCards(_stack.Notes);", StringComparison.Ordinal));
        Assert.Contains("StartEditor(id, CaretPlacement.End);", notes, StringComparison.Ordinal);
        Assert.Contains("FocusNoteRow(id);", notes, StringComparison.Ordinal);

        string folders = Body(Main, "OnFolderDragCompleted");
        Assert.Contains("FolderReposition.Drop(_listFolders.Execute(), FolderId.From(id), landedAt)", folders, StringComparison.Ordinal);
        Assert.True(folders.IndexOf("else if (drop.Target.Moves)", StringComparison.Ordinal) < folders.IndexOf("_commands.Dispatch(new ReorderFolder(", StringComparison.Ordinal));
        Assert.Contains("Refresh();\n            FocusRow(id);", folders.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);

        // A folder being renamed is not dragged.
        Assert.Contains("_renamingId is not null", Body(Main, "OnFolderDragStarting"), StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- helpers

    private static XElement Card() =>
        Named(MainXaml.Root!, "NoteList").Descendants().Single(e => e.Name.LocalName == "DataTemplate");

    private static XElement Named(XElement scope, string name) =>
        scope.Descendants().Single(e => (string?)e.Attribute(X + "Name") == name);

    [Fact]
    public void The_stack_is_virtualised_and_keeps_its_scroll_offset_when_a_card_changes_size()
    {
        // The default re-anchors on items as they change: with a ~100 KB card being edited among 50, replacing all of its
        // text twice ended Noto inside WinUI (0xC000027B); keeping the offset never did (the performance and stability
        // slice, measured).
        XElement list = MainXaml.Descendants().Single(e => (string?)e.Attribute(X + "Name") == "NoteList");
        XElement panel = Assert.Single(
            list.Descendants(),
            e => e.Name.LocalName is "ItemsStackPanel" or "StackPanel" or "VirtualizingStackPanel" or "ItemsWrapGrid"
                && e.Ancestors().Any(a => a.Name.LocalName == "ListView.ItemsPanel"));
        Assert.Equal("ItemsStackPanel", panel.Name.LocalName);
        Assert.Equal("KeepScrollOffset", (string?)panel.Attribute("ItemsUpdatingScrollMode"));
    }

    [Fact]
    public void The_card_being_edited_is_never_taller_than_the_stack()
    {
        // Grown without a limit, every keystroke laid out the whole note: 259 ms a key at 100 KB, 18 ms capped (the
        // performance and stability slice). The cap is the stack's height less the card's own chrome, from the tokens.
        Assert.Matches(@"host\.MaxHeight = EditingHeight\(\);\s*_editor = host;", Body(Main, "AttachEditor"));
        Assert.Matches(@"NoteList\.SizeChanged \+= \(_, _\) =>\s*\{\s*if \(_editor is \{ \} editor\)\s*\{\s*editor\.MaxHeight = EditingHeight\(\);", Main);

        string height = Body(Main, "EditingHeight");
        foreach (string token in new[] { "\"CardPadding\"", "\"CardSpacing\"", "\"SpaceXS\"", "\"ControlHeightCompact\"" })
        {
            Assert.Contains(token, height, StringComparison.Ordinal);
        }

        Assert.Contains("double chrome = padding.Top + padding.Bottom + spacing.Top + spacing.Bottom + (double)tokens[\"SpaceXS\"] + (double)tokens[\"ControlHeightCompact\"];", height, StringComparison.Ordinal);
        Assert.Contains("double height = NoteList.ActualHeight - chrome;", height, StringComparison.Ordinal);
        Assert.Contains("return height > 0 ? height : double.PositiveInfinity;", height, StringComparison.Ordinal);
    }

    private static string Code(string path) => Regex.Replace(File.ReadAllText(path), @"^\s*//.*$", string.Empty, RegexOptions.Multiline);

    private static string Body(string source, string method)
    {
        var declaration = Regex.Match(source, $@"(private|internal)(?: static)? [\w<>?,\[\] ]+? {method}\(");
        Assert.True(declaration.Success, $"Not found: {method}");
        int open = source.IndexOf('{', declaration.Index);
        int depth = 0;

        for (int i = open; i < source.Length; i++)
        {
            depth += source[i] switch { '{' => 1, '}' => -1, _ => 0 };

            if (depth == 0)
            {
                return source[open..(i + 1)];
            }
        }

        throw new InvalidOperationException($"Unbalanced body for {method}");
    }

    /// <summary>A property's body: from its declaration's first brace to the brace that closes it.</summary>
    private static string Property(string source, string name)
    {
        var declaration = Regex.Match(source, $@"public [\w?]+ {name}\s*\{{");
        Assert.True(declaration.Success, $"Not found: {name}");
        int open = source.IndexOf('{', declaration.Index);
        int depth = 0;

        for (int i = open; i < source.Length; i++)
        {
            depth += source[i] switch { '{' => 1, '}' => -1, _ => 0 };

            if (depth == 0)
            {
                return source[open..(i + 1)];
            }
        }

        throw new InvalidOperationException($"Unbalanced body for {name}");
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
