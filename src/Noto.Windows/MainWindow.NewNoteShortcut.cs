using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;

namespace Noto;

/// <summary>
/// The global new-note shortcuts (F17, #156; F18, #157; parity B2, B3, G2, G4), the tray's New note from clipboard and
/// the + button's menu (#187): Noto is shown by the app, and the window makes a note ready to type in, as Ctrl+N does,
/// and pastes the clipboard into it as Ctrl+V does when it is from the clipboard.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// The + button's menu opens (B3, D43, #187): New note from clipboard is offered only when the clipboard holds
    /// something Ctrl+V pastes (text, a page's HTML, files or a picture).
    /// </summary>
    private void OnNewNoteMenuOpening(object sender, object e)
    {
        try
        {
            DataPackageView clipboard = Clipboard.GetContent();
            NewNoteFromClipboardItem.IsEnabled = clipboard.Contains(StandardDataFormats.Text)
                || clipboard.Contains(StandardDataFormats.Html)
                || clipboard.Contains(StandardDataFormats.StorageItems)
                || clipboard.Contains(StandardDataFormats.Bitmap);
        }
        catch (COMException)
        {
            // Another app holds the clipboard open: offered, and its paste does nothing, as Ctrl+V's would.
            NewNoteFromClipboardItem.IsEnabled = true;
        }
    }

    /// <summary>The + button's New note from clipboard: the tray's and the shortcut's own path (F18, #157).</summary>
    private void OnNewNoteFromClipboardClick(object sender, RoutedEventArgs e) => NewNoteFromShortcut(fromClipboard: true);

    /// <summary>
    /// A new note in the open folder, edited; on the folder list, in the selected folder, else the first. Settings,
    /// Search and the Recycle Bin are left first; the welcome tour is left alone, being the user's first steps.
    /// </summary>
    /// <remarks>
    /// The note is made once the surfaces it leaves have given their focus back, and once a folder entered here has
    /// loaded its notes: queued behind both, at low priority, so the new note's editor has the keyboard last.
    /// </remarks>
    internal void NewNoteFromShortcut(bool fromClipboard = false)
    {
        if (WelcomeSurface is not null)
        {
            return;
        }

        if (SettingsSurface is not null)
        {
            CloseSettings();
        }

        if (SearchSurface is not null)
        {
            CloseSearch(restoreFocus: false);
        }

        if (RecycleBinSurface is not null)
        {
            CloseRecycleBin();
        }

        if (_openFolderId is null)
        {
            if (_state != PaneState.Loaded)
            {
                return;
            }

            FolderListItem? folder = FolderList.SelectedItem as FolderListItem
                ?? Folders.FirstOrDefault(f => f.Id == _selectedId)
                ?? Folders.FirstOrDefault();

            if (folder is null)
            {
                ShowStatus("Make a folder first: a note goes in a folder.");
                FocusInput();
                return;
            }

            FolderList.SelectedItem = folder;
            EnterSelectedFolder();
        }

        _ = DispatcherQueue.TryEnqueueGuarded(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            string? before = _stack.Editing is { } editing ? editing.Value : null;
            CreateNoteInOpenFolder();

            if (fromClipboard && _stack.Editing is { } created && created.Value != before)
            {
                PasteInto(created.Value);
            }
        });
    }

    // The new note the clipboard is pasted into once its editor is realised (AttachEditor), when it is not yet.
    private string? _pendingPaste;

    private void PasteInto(string note)
    {
        if (_editor is { } host && host.Note is { } shown && shown.Value == note)
        {
            host.PasteClipboard();
        }
        else
        {
            _pendingPaste = note;
        }
    }
}
