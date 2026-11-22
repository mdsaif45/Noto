using System.Text.RegularExpressions;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// #242 (parity C10) in the source: a folder's note sort is chosen from the list bar's More menu, its current one
/// checked, through the one session path, and kept on the folder; the stack opens in the folder's sort; in a sort, the
/// move keys and drag are refused with the reason and the menu's moves are disabled; a sorted folder places the note
/// it left on Escape. That the cards really reorder is the note stack harness's case 53.
/// </summary>
public sealed class FolderSortGuardTests
{
    private static readonly string Windows = Path.GetDirectoryName(SourcePath("src", "Noto.Windows", "App.xaml.cs"))!;
    private static readonly string Main = Code(Path.Join(Windows, "MainWindow.xaml.cs"));
    private static readonly string App = Code(Path.Join(Windows, "App.xaml.cs"));
    private static readonly string Row = Code(Path.Join(Windows, "FolderListItem.cs"));

    [Fact]
    public void The_more_menu_offers_sort_by_with_every_sort_and_the_folders_own_checked()
    {
        string menu = Body(Main, "OnNoteListMenuClick");
        Assert.Contains("var sortBy = new MenuFlyoutSubItem { Text = \"Sort by\" };", menu, StringComparison.Ordinal);
        Assert.Contains("foreach (NoteSort sort in Enum.GetValues<NoteSort>())", menu, StringComparison.Ordinal);
        Assert.Contains("Text = NoteSorts.NameOf(sort),", menu, StringComparison.Ordinal);
        Assert.Contains("GroupName = \"NoteSort\",", menu, StringComparison.Ordinal);
        Assert.Contains("IsChecked = _stack.Sort == sort,", menu, StringComparison.Ordinal);
        Assert.Contains("item.Click += (_, _) => SortNotes(sort);", menu, StringComparison.Ordinal);

        // After Fold all and Unfold all, which keep their place.
        Assert.True(Index(menu, "\"Unfold all\"") < Index(menu, "menu.Items.Add(sortBy);"));
    }

    [Fact]
    public void A_sort_is_set_through_the_session_kept_on_the_folders_row_and_said_aloud()
    {
        string sort = Body(Main, "SortNotes");
        Assert.Equal(1, Regex.Count(Main, @"_stack\.SetSort\("));
        Assert.Contains("CommandResult result = _stack.SetSort(sort);", sort, StringComparison.Ordinal);
        Assert.Contains("row.NoteSort = sort;", sort, StringComparison.Ordinal);
        Assert.Contains("ShowCards(_stack.Notes);", sort, StringComparison.Ordinal);
        Assert.Contains("Announce(sort == NoteSort.Manual ? \"Manual order\" : $\"Sorted by {NoteSorts.NameOf(sort).ToLowerInvariant()}\");", sort, StringComparison.Ordinal);
        Assert.Contains("StartEditor(current, CaretPlacement.End);", sort, StringComparison.Ordinal);
        Assert.Contains("FocusNoteRow(current);", sort, StringComparison.Ordinal);

        Assert.Contains(".Register<SetFolderNoteSort, CommandResult>(new SetFolderNoteSortHandler(folders, clock).Handle)", App, StringComparison.Ordinal);
    }

    [Fact]
    public void The_stack_opens_in_the_folders_stored_sort()
    {
        Assert.Contains("public NoteSort NoteSort { get; set; }", Row, StringComparison.Ordinal);
        Assert.Contains("{ NoteSort = folder.NoteSort }", Body(Main, "Refresh"), StringComparison.Ordinal);
        Assert.Contains("_stack.Open(_openFolderId.Value, item.NoteSort);", Body(Main, "EnterSelectedFolder"), StringComparison.Ordinal);
    }

    [Fact]
    public void In_a_sort_the_move_keys_and_drag_are_refused_with_the_reason_and_the_menus_moves_disabled()
    {
        // The keys (and the menu's moves, which go through the same method): refused with the notice, before anything.
        string reorder = Body(Main, "ReorderCard");
        Assert.Matches(@"if \(_noteState == NoteState\.Loaded && !_stack\.IsManual\)\s*\{\s*ShowNoteNotice\(NoteSorts\.ManualOnly\(_stack\.Sort\)\);\s*return;", reorder);
        Assert.True(Index(reorder, "!_stack.IsManual") < Index(reorder, "_stack.Reorder("));

        // A drag never begins.
        string drag = Body(Main, "OnNoteDragStarting");
        Assert.Matches(@"if \(!_stack\.IsManual\)\s*\{\s*e\.Cancel = true;\s*ShowNoteNotice\(NoteSorts\.ManualOnly\(_stack\.Sort\)\);\s*return;", drag);
        Assert.True(Index(drag, "!_stack.IsManual") < Index(drag, "_dragging = card.Id;"));

        // Move to top and bottom ask CanReorder, which is false in any sort (the session's rule, tested against SQLite).
        Assert.Contains("_stack.CanReorder(NoteId.From(card.Id), RepositionDirection.Top)", Body(Main, "OnNoteListContextRequested"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_sorted_folder_places_the_note_it_left_on_escape_never_while_typing()
    {
        string leave = Body(Main, "LeaveEditor");
        Assert.Matches(@"if \(!_stack\.IsManual\)\s*\{\s*RefreshNotes\(\);\s*\}\s*FocusNoteRow\(editing\);", leave);

        // Not on a save, not on the other ways out of editing: a card never moves under the caret or a click.
        Assert.DoesNotContain("RefreshNotes()", Body(Main, "TrySaveCard"), StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshNotes()", Body(Main, "TryEndEdit"), StringComparison.Ordinal);
        Assert.DoesNotContain("IsManual", Body(Main, "EditCard"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_note_stack_harness_proves_the_sort_at_run_time()
    {
        string harness = File.ReadAllText(SourcePath("tools", "validation", "Invoke-NoteStack.ps1"));
        Assert.Contains("Add-Result $run '53 Sort by › Name: the cards in title order, the pinned one first", harness, StringComparison.Ordinal);
        Assert.Contains("Add-Result $run '53 sorted, Ctrl+Alt+Shift+Up is refused with the reason", harness, StringComparison.Ordinal);
        Assert.Contains("Add-Result $run '53 Sort by › Manual: the cards in the order they were before", harness, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- helpers

    private static int Index(string text, string value)
    {
        int index = text.IndexOf(value, StringComparison.Ordinal);
        Assert.True(index >= 0, $"Not found: {value}");
        return index;
    }

    private static string Code(string path) => Regex.Replace(File.ReadAllText(path), @"^\s*//.*$", string.Empty, RegexOptions.Multiline);

    private static string Body(string source, string method)
    {
        var declaration = Regex.Match(source, $@"\n    (private|internal|public)( static)?( async)? [\w?<>,()\s]+ {method}\(");
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
