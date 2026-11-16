using System.Diagnostics;
using System.Runtime.InteropServices;
using Noto.Core.Automation;
using Noto.Core.Commands;
using Noto.Core.Folders;
using Noto.Core.Storage;
using Noto.UseCases.Folders;
using Windows.ApplicationModel.DataTransfer;
using DispatcherQueuePriority = Microsoft.UI.Dispatching.DispatcherQueuePriority;

namespace Noto;

/// <summary>
/// A <c>noto://</c> link or a command-line verb (#192, ADR-022), done through the paths the window already has: the
/// new-note path with its placement rule, entering a folder, the folder list, CreateFolder, and Search. And a folder's
/// Copy link (C16).
/// </summary>
/// <remarks>
/// The app has shown Noto before a request gets here, so nothing a link asks for happens out of sight. A request only
/// ever creates or shows (the grammar has nothing else); one that cannot be done says why where the user is looking.
/// </remarks>
public sealed partial class MainWindow
{
    // Requests that arrived before the folder list first loaded, done once it has.
    private readonly List<AutomationRequest> _automationWaiting = [];
    private bool _automationReady;

    /// <summary>
    /// Does what a link asks, once the folder list has first loaded. Settings and the Recycle Bin are left first, as the
    /// new-note shortcut leaves them; the welcome tour is left alone, being the user's first steps, so a link that
    /// arrives during it is refused.
    /// </summary>
    internal void RunAutomation(AutomationRequest request)
    {
        if (!_automationReady)
        {
            _automationWaiting.Add(request);
            return;
        }

        string verb = request.GetType().Name;

        if (WelcomeSurface is not null)
        {
            Trace.WriteLine($"Noto.Automation refused reason=Welcome verb={verb}");
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

        Trace.WriteLine($"Noto.Automation run verb={verb}");

        switch (request)
        {
            case AutomationRequest.NewNote note:
                NewNoteFromAutomation(note);
                break;

            case AutomationRequest.OpenFolder open:
                if (FindFolder(open.Folder) is { } target)
                {
                    OpenFromSearch(target, null, 0);
                }

                break;

            case AutomationRequest.ShowFolders:
                if (LeaveForFolderList())
                {
                    _ = DispatcherQueue.TryEnqueueGuarded(DispatcherQueuePriority.Low, MoveFocusToList);
                }

                break;

            case AutomationRequest.NewFolder created:
                NewFolderFromAutomation(created.Name);
                break;

            case AutomationRequest.Search search:
                OpenSearchFromShortcut();

                // Behind OpenSearch, which the line above queued at the same priority.
                _ = DispatcherQueue.TryEnqueueGuarded(DispatcherQueuePriority.Low, () => SearchSurface?.ShowQuery(search.Query));
                break;
        }
    }

    /// <summary>The folder list has first loaded: what was waiting for it is done now, in order.</summary>
    private void RunAutomationAtStart()
    {
        _automationReady = true;

        foreach (AutomationRequest request in _automationWaiting)
        {
            RunAutomation(request);
        }

        _automationWaiting.Clear();
    }

    /// <summary>
    /// A note holding the link's text: in its folder, entered first, else where Ctrl+N would put it. Made by the new-note
    /// shortcut's path, so the placement setting and the save of the card being edited apply as they do to Ctrl+N.
    /// </summary>
    private void NewNoteFromAutomation(AutomationRequest.NewNote request)
    {
        if (request.Folder is { } reference)
        {
            if (FindFolder(reference) is not { } folder)
            {
                return;
            }

            if (_openFolderId != folder)
            {
                OpenFromSearch(folder, null, 0);

                // Not entered: the card being edited could not be saved, which has said so.
                if (_openFolderId != folder)
                {
                    return;
                }
            }
        }

        NewNoteFromShortcut(content: request.Text);
    }

    /// <summary>A new folder through CreateFolder, as the name box makes one: selected on the folder list.</summary>
    private void NewFolderFromAutomation(string name)
    {
        if (!LeaveForFolderList() || _state != PaneState.Loaded)
        {
            return;
        }

        try
        {
            CommandResult<FolderId> result = _commands.Dispatch(new CreateFolder(name));

            if (!result.IsSuccess)
            {
                ShowNotice(Describe(result.Failure!));
                return;
            }

            _selectedId = result.Value.Value;
            Refresh();
            FocusRow(_selectedId);
            ShowStatus("Folder created.");
        }
        catch (StorageException ex)
        {
            ShowNotice($"Could not create the folder: {ex.Message}");
        }
    }

    /// <summary>Leaves Search and the open folder (saving first) for the folder list.</summary>
    /// <returns>Whether the folder list is shown: false when the card being edited could not be saved.</returns>
    private bool LeaveForFolderList()
    {
        CloseSearch(restoreFocus: false);

        if (_openFolderId is not null)
        {
            LeaveFolder();
        }

        return _openFolderId is null;
    }

    /// <summary>The folder a link names, by id or by name; when there is none, says so where the user is.</summary>
    private FolderId? FindFolder(string reference)
    {
        if (AutomationRequest.FindFolder(Execute(_listFolders.Execute), reference) is { } folder)
        {
            return folder.Id;
        }

        Trace.WriteLine("Noto.Automation refused reason=NoSuchFolder");

        if (_openFolderId is null)
        {
            ShowStatus("The folder in that link wasn't found.");
        }
        else
        {
            ShowNoteStatus("The folder in that link wasn't found.");
        }

        return null;
    }

    /// <summary>
    /// Copy link (C16): the folder's <c>noto://open/&lt;id&gt;</c>. Its id, never its name: names are not unique (Case G)
    /// and change, the id does not.
    /// </summary>
    private void CopyFolderLink(string folderId)
    {
        try
        {
            var package = new DataPackage();
            package.SetText(NotoUri.Format(new AutomationRequest.OpenFolder(folderId)));
            Clipboard.SetContent(package);
        }
        catch (COMException)
        {
            ShowNotice("Nothing was copied: another app is using the clipboard. Try again.");
            return;
        }

        ShowStatus("Link copied.");
    }
}
