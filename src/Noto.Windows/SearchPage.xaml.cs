using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Noto.Core.Folders;
using Noto.Core.Links;
using Noto.Core.Search;
using Noto.Core.Storage;
using Noto.UseCases.Workspace;
using Windows.System;
using Windows.UI.Core;

namespace Noto;

/// <summary>One result as Search shows it: a folder, or a note with its snippet and folder.</summary>
public sealed class SearchResultItem
{
    /// <summary>Required by the XAML type activator (see <see cref="FolderListItem"/>).</summary>
    public SearchResultItem()
    {
    }

    internal SearchResultItem(FolderHit folder)
    {
        Folder = folder;
        Title = folder.Name;
        TitleMatches = folder.NameMatches;
        Where = "Folder";
    }

    internal SearchResultItem(NoteHit note)
    {
        Note = note;
        Title = note.Title.Length > 0 ? note.Title : NoteCard.UntitledLabel;
        TitleMatches = note.Title.Length > 0 ? note.TitleMatches : [];
        Snippet = note.Snippet;
        SnippetMatches = note.SnippetMatches;
        Where = note.FolderName.Length > 0 ? $"In {note.FolderName}" : "Note";
    }

    internal FolderHit? Folder { get; }

    internal NoteHit? Note { get; }

    public string Title { get; } = string.Empty;

    public string Snippet { get; } = string.Empty;

    public string Where { get; } = string.Empty;

    public Visibility SnippetVisibility => Snippet.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    internal IReadOnlyList<Core.Search.TextRange> TitleMatches { get; } = [];

    internal IReadOnlyList<Core.Search.TextRange> SnippetMatches { get; } = [];

    /// <summary>What assistive technology reads for the result: its title, its snippet and where it is.</summary>
    public override string ToString() => string.Join(", ", new[] { Title, Snippet, Where }.Where(s => s.Length > 0));
}

/// <summary>
/// The Search surface (ADR-018 §3): results as you type, the keyboard kept in the box, a result opened or copied by
/// the window that hosts it. Folders first, then notes; matches highlighted.
/// </summary>
public sealed partial class SearchPage : UserControl
{
    private SearchWorkspaceQuery? _search;
    private FolderId? _scope;

    public SearchPage()
    {
        InitializeComponent();
        ResultList.ContainerContentChanging += OnContainerContentChanging;
    }

    /// <summary>The results of the text in the box.</summary>
    public ObservableCollection<SearchResultItem> Results { get; } = [];

    /// <summary>The user asked to leave Search.</summary>
    internal event EventHandler? BackRequested;

    /// <summary>The user asked to open a result.</summary>
    internal event EventHandler<SearchResultItem>? OpenRequested;

    /// <summary>The user asked to copy a note's text (E3): the text to copy.</summary>
    internal event EventHandler<string>? CopyRequested;

    /// <summary>The user asked to launch a note's first link (#197; parity E4, G48): its destination, allowed.</summary>
    internal event EventHandler<string>? LaunchRequested;

    /// <summary>The user asked to open the folder a note lives in, not the note (#197; parity E5, G49).</summary>
    internal event EventHandler<FolderId>? FolderRequested;

    /// <summary>
    /// Shows Search over <paramref name="search"/>. Opened inside a folder, it can be narrowed to that folder (E9);
    /// it starts across all of them, as SideNotes does.
    /// </summary>
    internal void Show(SearchWorkspaceQuery search, FolderId? folder, string? folderName)
    {
        _search = search ?? throw new ArgumentNullException(nameof(search));
        _scope = folder;
        ScopeToggle.Visibility = folder is null ? Visibility.Collapsed : Visibility.Visible;
        ScopeToggle.Content = $"Only in {folderName}";
        AutomationProperties.SetName(ScopeToggle, $"Only in {folderName}");
        Run();
    }

    /// <summary>Keyboard focus in the box, its text selected, so typing replaces it.</summary>
    internal void FocusBox()
    {
        _ = SearchBox.Focus(FocusState.Programmatic);
        SearchBox.SelectAll();
    }

    /// <summary>A <c>noto://search</c> link's query (#192, E6), in the box as if typed: the results follow.</summary>
    internal void ShowQuery(string query) => SearchBox.Text = query;

    /// <summary>Says why something did not happen, in the status line, announced.</summary>
    internal void ShowNotice(string message) => SearchStatus.Text = message;

    private SearchResultItem? Selected => ResultList.SelectedItem as SearchResultItem;

    /// <summary>
    /// Whether Ctrl+Enter is Search's own: with a result selected it opens it, a note's folder (#197), so the window's
    /// Ctrl+Enter to hide (#255) leaves it alone.
    /// </summary>
    internal bool KeepsCtrlEnter => Selected is not null;

    private void OnBackClick(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

    private void OnQueryChanged(object sender, TextChangedEventArgs e) => Run();

    /// <summary>The scope changes the results; the keyboard goes back to the box, where typing searches.</summary>
    private void OnScopeChanged(object sender, RoutedEventArgs e)
    {
        Run();
        _ = SearchBox.Focus(FocusState.Programmatic);
    }

    private void OnResultClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is SearchResultItem item)
        {
            Open(item);
        }
    }

    /// <summary>
    /// Opens a result. With Ctrl held, a note's folder is entered instead (#197; parity E5, G49): Ctrl+Click, and
    /// Ctrl+Enter for the keyboard. A folder result is itself the folder.
    /// </summary>
    private void Open(SearchResultItem item)
    {
        if (IsDown(VirtualKey.Control) && item.Note is { } note)
        {
            FolderRequested?.Invoke(this, note.FolderId);
            return;
        }

        OpenRequested?.Invoke(this, item);
    }

    /// <summary>The box keeps the keyboard (ADR-018 §3): Up and Down choose, Enter opens, Ctrl+C copies, Ctrl+R launches.</summary>
    private void OnBoxPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Down:
                e.Handled = Move(+1);
                return;
            case VirtualKey.Up:
                e.Handled = Move(-1);
                return;
            case VirtualKey.Enter when Selected is { } item:
                e.Handled = true;
                Open(item);
                return;
        }

        // Ctrl+C copies the text selected in the box when there is some, as in any text box; otherwise the result.
        bool copied = e.Key == VirtualKey.C && (IsDown(VirtualKey.Shift) || SearchBox.SelectionLength == 0) && TryCopy();
        e.Handled = copied || (e.Key == VirtualKey.R && TryLaunch());
    }

    private void OnListPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && Selected is { } item)
        {
            e.Handled = true;
            Open(item);
            return;
        }

        e.Handled = (e.Key == VirtualKey.C && TryCopy()) || (e.Key == VirtualKey.R && TryLaunch());
    }

    /// <summary>Ctrl+C copies the selected note's text, Ctrl+Shift+C without its title line (E3).</summary>
    private bool TryCopy()
    {
        if (!IsDown(VirtualKey.Control) || IsDown(VirtualKey.Menu) || Selected?.Note is not { } note)
        {
            return false;
        }

        CopyRequested?.Invoke(this, IsDown(VirtualKey.Shift) ? SearchText.WithoutTitle(note.Content) : note.Content);
        return true;
    }

    /// <summary>
    /// Ctrl+R launches the selected note's first link the allow-list opens (#197; parity D37, E4, G48), as the editor
    /// finds links. A note without one launches nothing and says so.
    /// </summary>
    private bool TryLaunch()
    {
        if (!IsDown(VirtualKey.Control) || IsDown(VirtualKey.Menu) || IsDown(VirtualKey.Shift) || Selected?.Note is not { } note)
        {
            return false;
        }

        if (LinkTarget.FirstOpenable(note.Content) is { } destination)
        {
            LaunchRequested?.Invoke(this, destination);
        }
        else
        {
            ShowNotice("This note has no link to open.");
        }

        return true;
    }

    /// <summary>Moves the selection, announcing the result reached: focus stays in the box, so nothing else would.</summary>
    private bool Move(int by)
    {
        if (Results.Count == 0)
        {
            return false;
        }

        int index = Math.Clamp(ResultList.SelectedIndex + by, 0, Results.Count - 1);
        ResultList.SelectedIndex = index;
        ResultList.ScrollIntoView(Results[index]);
        FrameworkElementAutomationPeer.FromElement(SearchBox)?.RaiseNotificationEvent(
            AutomationNotificationKind.ActionCompleted,
            AutomationNotificationProcessing.ImportantMostRecent,
            Results[index].ToString(),
            "NotoSearchSelection");
        return true;
    }

    /// <summary>Searches for the text in the box; results replace the last ones, the first selected.</summary>
    private void Run()
    {
        if (_search is null)
        {
            return;
        }

        long started = Stopwatch.GetTimestamp();
        SearchResults results;

        try
        {
            results = _search.Execute(SearchBox.Text, ScopeToggle.IsChecked == true ? _scope : null);
        }
        catch (StorageException)
        {
            Results.Clear();
            SearchStatus.Text = "Search isn't available: the notes could not be read. Your text is kept.";
            return;
        }

        Results.Clear();
        foreach (FolderHit folder in results.Folders)
        {
            Results.Add(new SearchResultItem(folder));
        }

        foreach (NoteHit note in results.Notes)
        {
            Results.Add(new SearchResultItem(note));
        }

        ResultList.SelectedIndex = Results.Count > 0 ? 0 : -1;
        SearchStatus.Text = results.Query.IsEmpty ? "Type to search your notes and folders."
            : results.IsEmpty ? "No notes or folders match."
            : Results.Count == 1 ? "1 result"
            : string.Create(CultureInfo.InvariantCulture, $"{Results.Count} results");

        // Validation channel only, never the log, and never the query: what was searched for is the user's.
        Trace.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Noto.Search results={Results.Count} ms={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F2}"));
    }

    /// <summary>A realised result: its matches highlighted and its name for assistive technology.</summary>
    private static void OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.Item is not SearchResultItem item || args.ItemContainer.ContentTemplateRoot is not StackPanel { Children: [TextBlock title, TextBlock snippet, ..] })
        {
            return;
        }

        Highlight(title, item.TitleMatches);
        Highlight(snippet, item.SnippetMatches);
        AutomationProperties.SetName(args.ItemContainer, item.ToString());
    }

    private static void Highlight(TextBlock text, IReadOnlyList<Core.Search.TextRange> matches)
    {
        if (text.TextHighlighters is not [TextHighlighter highlighter, ..])
        {
            return;
        }

        highlighter.Ranges.Clear();
        foreach (Core.Search.TextRange match in matches)
        {
            highlighter.Ranges.Add(new Microsoft.UI.Xaml.Documents.TextRange { StartIndex = match.Start, Length = match.Length });
        }
    }

    private static bool IsDown(VirtualKey key) =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
}
