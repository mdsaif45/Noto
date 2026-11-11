namespace Noto;

/// <summary>
/// The global new-note shortcut (F17, #156; parity B2, G2): Noto is shown by the app, and the window makes a note ready
/// to type in, as Ctrl+N does.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// A new note in the open folder, edited; on the folder list, in the selected folder, else the first. Settings,
    /// Search and the Recycle Bin are left first; the welcome tour is left alone, being the user's first steps.
    /// </summary>
    /// <remarks>
    /// The note is made once the surfaces it leaves have given their focus back, and once a folder entered here has
    /// loaded its notes: queued behind both, at low priority, so the new note's editor has the keyboard last.
    /// </remarks>
    internal void NewNoteFromShortcut()
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

        _ = DispatcherQueue.TryEnqueueGuarded(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, CreateNoteInOpenFolder);
    }
}
