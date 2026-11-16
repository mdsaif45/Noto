using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Noto.Core.Folders;
using Noto.Core.Storage;

namespace Noto;

/// <summary>
/// Folder history (#193; parity C5, C6, G10, C14): the folders last opened, listed by Back's menu and its chevron,
/// switched between by Ctrl+Shift+O, and the one a start can open straight into. Every way into a folder records it
/// (EnterSelectedFolder); every way here leaves the open folder through LeaveFolder, which saves the card being edited
/// first and stays if that fails.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>The folders last opened, most recent first; set by the composition root.</summary>
    internal Func<IReadOnlyList<FolderId>>? RecentOpenedFolders { get; set; }

    /// <summary>Records a folder as opened, given the folders that still exist; set by the composition root.</summary>
    internal Action<FolderId, IReadOnlyList<FolderId>>? FolderOpened { get; set; }

    /// <summary>Whether the first folder load goes straight into the folder last opened (C14, J10); set once at start.</summary>
    internal bool OpenLastFolderOnStart { get; set; }

    /// <summary>
    /// Ctrl+Shift+O (G10, implementation plan §10.2): the folder open before this one, so pressing it again comes back,
    /// as Alt+Tab does; from the folder list, the folder last opened. With no such folder it says so, and stays.
    /// </summary>
    private void OnLastFolderAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;

        if (OtherRecentFolders() is [var previous, ..])
        {
            OpenFolder(previous.Id);
        }
        else if (_openFolderId is null)
        {
            ShowStatus("No folder has been opened yet.");
        }
        else
        {
            ShowNoteStatus("No other folder has been opened recently.");
        }
    }

    /// <summary>Back's context menu (right-click, Shift+F10, the Menu key): the recent folders (C5).</summary>
    private void OnBackContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        args.Handled = true;
        ShowMenu(RecentFoldersMenu(), BackButton, args);
    }

    /// <summary>The chevron beside Back: the same menu, for a pointer or a keyboard that does not know the context menu.</summary>
    private void OnRecentFoldersClick(object sender, RoutedEventArgs e) => RecentFoldersMenu().ShowAt(RecentFoldersButton);

    /// <summary>
    /// The folders opened recently, except the open one, most recent first; choosing one enters it. The first is the one
    /// Ctrl+Shift+O goes to, and says so.
    /// </summary>
    private MenuFlyout RecentFoldersMenu()
    {
        var menu = new MenuFlyout();
        IReadOnlyList<Folder> recent = OtherRecentFolders();

        for (int i = 0; i < recent.Count; i++)
        {
            FolderId id = recent[i].Id;
            menu.Items.Add(MenuItem(recent[i].Name, i == 0 ? "Ctrl+Shift+O" : string.Empty, () => OpenFolder(id)));
        }

        if (recent.Count == 0)
        {
            menu.Items.Add(MenuItem("No other recent folders", string.Empty, static () => { }, enabled: false));
        }

        return menu;
    }

    /// <summary>
    /// The recent folders that still exist, without the open one. A folder that cannot be read is said where the user is,
    /// and nothing is offered.
    /// </summary>
    private IReadOnlyList<Folder> OtherRecentFolders()
    {
        try
        {
            return RecentFolders.Shown(RecentOpenedFolders?.Invoke() ?? [], _listFolders.Execute(), _openFolderId);
        }
        catch (StorageException ex)
        {
            string message = $"Could not read the folders: {ex.Message}";
            if (_openFolderId is null)
            {
                ShowNotice(message);
            }
            else
            {
                ShowNoteNotice(message);
            }

            return [];
        }
    }

    /// <summary>
    /// Enters <paramref name="folder"/> from the folder list or another folder's stack. The open folder is left first,
    /// saving the card being edited; a failed save keeps the stack, the card and its text, and says why (LeaveFolder).
    /// </summary>
    private void OpenFolder(FolderId folder)
    {
        if (_openFolderId == folder)
        {
            return;
        }

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
        if (FolderList.SelectedItem is FolderListItem selected && selected.Id == folder.Value)
        {
            EnterSelectedFolder();
        }
    }

    /// <summary>
    /// A start with the setting on opens the folder last opened, if it still exists (C14, J10). A start at sign-in stays
    /// hidden: the folder list first loads when Noto is first shown, and that show opens in the folder. Anything the
    /// folder list says at this start (a restore's outcome, a failed read) keeps Noto there, where it is said.
    /// </summary>
    private void OpenLastFolderAtStart()
    {
        if (!OpenLastFolderOnStart
            || _state != PaneState.Loaded
            || NoticeText.Visibility == Visibility.Visible
            || StatusText.Visibility == Visibility.Visible)
        {
            return;
        }

        if (OtherRecentFolders() is [var last, ..])
        {
            OpenFolder(last.Id);
        }
    }
}
