using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Noto.Core.Folders;
using Noto.Core.Notes;
using Noto.Platform.Windows;
using Noto.UseCases.Workspace;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using DispatcherQueuePriority = Microsoft.UI.Dispatching.DispatcherQueuePriority;

namespace Noto;

/// <summary>Search (ADR-018): the surface, and opening or copying what it found.</summary>
public sealed partial class MainWindow
{
    private UIElement? _searchReturn;
    private Control? _searchReturnFocus;

    /// <summary>Where an opened search result's caret goes once its editor attaches: the first match.</summary>
    private int? _pendingCaretAt;

    /// <summary>Search, set once by the composition root.</summary>
    internal SearchWorkspaceQuery? Search { get; set; }

    /// <summary>The Search surface while it is open; <see langword="null"/> otherwise.</summary>
    private SearchPage? SearchSurface { get; set; }

    /// <summary>
    /// Opens Search (G45, Ctrl+F) from the folder list, a note list or the editor. Already open: focus goes back to
    /// its box. The surface it covers keeps its state; leaving returns to it with focus where it was.
    /// </summary>
    internal void OpenSearch()
    {
        if (Search is null || SettingsSurface is not null || RecycleBinSurface is not null || WelcomeSurface is not null)
        {
            return;
        }

        if (SearchSurface is { } open)
        {
            open.FocusBox();
            return;
        }

        // What was typed is written before Search covers it (ADR-018 §3). A failed save keeps the text and says why;
        // Search still opens, and the card is still editing underneath.
        _ = TrySaveCard();

        UIElement from = NoteRoot.Visibility == Visibility.Visible ? NoteRoot : PaneRoot;
        _searchReturn = from;
        _searchReturnFocus = Content?.XamlRoot is { } root ? FocusManager.GetFocusedElement(root) as Control : null;

        var page = new SearchPage();
        Grid.SetRow(page, Grid.GetRow(PaneRoot));
        page.KeyDown += OnSearchKeyDown;
        page.BackRequested += OnSearchBackRequested;
        page.OpenRequested += OnSearchOpenRequested;
        page.CopyRequested += OnSearchCopyRequested;
        ((Panel)PaneRoot.Parent).Children.Add(page);
        SearchSurface = page;

        FolderId? scope = from == NoteRoot ? _openFolderId : null;
        string? scopeName = scope is { } folder ? Folders.FirstOrDefault(f => f.Id == folder.Value)?.Name : null;
        page.Show(Search, scope, scopeName);
        from.Visibility = Visibility.Collapsed;
        page.Visibility = Visibility.Visible;
        Motion.EnterSurface(page, forward: true);

        _ = DispatcherQueue.TryEnqueueGuarded(DispatcherQueuePriority.Low, page.FocusBox);
        Trace.WriteLine("Noto.Search opened");
    }

    /// <summary>
    /// The global search shortcut (F21, #159; parity E2, G3): Noto is shown by the app, and Search opens as Ctrl+F opens
    /// it, from wherever Noto was. Settings and the Recycle Bin are left first; the welcome tour is left alone.
    /// </summary>
    internal void OpenSearchFromShortcut()
    {
        if (WelcomeSurface is not null)
        {
            return;
        }

        if (SettingsSurface is not null)
        {
            CloseSettings();
        }

        if (RecycleBinSurface is not null)
        {
            CloseRecycleBin();
        }

        // After the surfaces left have given their focus back, so Search's box has the keyboard last.
        _ = DispatcherQueue.TryEnqueueGuarded(DispatcherQueuePriority.Low, OpenSearch);
    }

    /// <summary>Leaves Search for the surface it was opened from; with focus back where it was, unless a result takes it.</summary>
    private void CloseSearch(bool restoreFocus = true)
    {
        if (SearchSurface is not { } page)
        {
            return;
        }

        UIElement back = _searchReturn ?? PaneRoot;
        back.Visibility = Visibility.Visible;
        page.Visibility = Visibility.Collapsed;
        Motion.EnterSurface(back, forward: false);

        ((Panel)page.Parent).Children.Remove(page);
        page.KeyDown -= OnSearchKeyDown;
        page.BackRequested -= OnSearchBackRequested;
        page.OpenRequested -= OnSearchOpenRequested;
        page.CopyRequested -= OnSearchCopyRequested;
        SearchSurface = null;

        Control restore = _searchReturnFocus is { IsLoaded: true, Visibility: Visibility.Visible } previous
            ? previous
            : back == NoteRoot ? NoteList
            : FolderList;

        _searchReturn = null;
        _searchReturnFocus = null;

        if (restoreFocus)
        {
            _ = DispatcherQueue.TryEnqueueGuarded(DispatcherQueuePriority.Low, () => restore.Focus(FocusState.Programmatic));
        }

        Trace.WriteLine("Noto.Search closed");
    }

    private void OnSearchBackRequested(object? sender, EventArgs e) => CloseSearch();

    /// <summary>Escape in Search follows the stored behaviour: by default it goes back.</summary>
    private void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = OnEscape(WorkspaceSurface.Search);
        }
    }

    private void OnSearchOpenRequested(object? sender, SearchResultItem item)
    {
        if (item.Note is { } note)
        {
            OpenFromSearch(note.FolderId, note.Id, note.Caret);
        }
        else if (item.Folder is { } folder)
        {
            OpenFromSearch(folder.Id, null, 0);
        }
    }

    /// <summary>
    /// Opens a result (ADR-018 §3): enters its folder (leaving the open one, saving first), then edits the note with
    /// the caret at its first match. A failed save keeps the stack, the card and its text, and says why.
    /// </summary>
    private void OpenFromSearch(FolderId folder, NoteId? note, int caret)
    {
        CloseSearch(restoreFocus: false);

        if (_openFolderId != folder)
        {
            if (_openFolderId is not null)
            {
                LeaveFolder();
                if (_openFolderId is not null)
                {
                    return;
                }
            }

            _selectedId = folder.Value;
            RestoreSelection();
            if (FolderList.SelectedItem is not FolderListItem selected || selected.Id != folder.Value)
            {
                return;
            }

            // Queues the stack's read and its focus; the edit below is queued after both.
            EnterSelectedFolder();
        }
        else if (note is null)
        {
            MoveFocusToNotes();
        }

        if (note is { } id)
        {
            _ = DispatcherQueue.TryEnqueueGuarded(DispatcherQueuePriority.Low, () => EditFromSearch(id, caret));
        }
    }

    private void EditFromSearch(NoteId note, int caret)
    {
        EditCard(note.Value, CaretPlacement.Start);
        if (_stack.Editing != note)
        {
            return;
        }

        if (_editor is { } host && host.Note == note)
        {
            host.PlaceCaretAt(caret);
        }
        else
        {
            _pendingCaretAt = caret;
        }
    }

    /// <summary>
    /// Quick copy (E3): the text goes to the clipboard, Search closes and Noto hides, so the app the user came from
    /// has focus and a paste lands there (D36). A clipboard another app holds open copies nothing, and says so.
    /// </summary>
    private void OnSearchCopyRequested(object? sender, string text)
    {
        try
        {
            var package = new DataPackage();
            package.SetText(text);
            Clipboard.SetContent(package);
        }
        catch (COMException)
        {
            SearchSurface?.ShowNotice("Nothing was copied: another app is using the clipboard. Try again.");
            return;
        }

        CloseSearch();

        // Queued behind CloseSearch's focus restore, at the same priority: that focus lands while Noto is still in
        // front. Run after the hide, focusing the hidden window activates it again, and Noto came back (measured: about
        // one Ctrl+C in two).
        _ = DispatcherQueue.TryEnqueueGuarded(DispatcherQueuePriority.Low, () => DismissRequested?.Invoke(this, EventArgs.Empty));
    }
}
