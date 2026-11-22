using System.Diagnostics;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Noto.Core.Folders;
using Windows.System;
using DispatcherQueuePriority = Microsoft.UI.Dispatching.DispatcherQueuePriority;

namespace Noto;

/// <summary>
/// Ask which folder (#243; parity B6, J9): with the setting on, a note made from a global shortcut (the new-note and
/// clipboard-note shortcuts, and the tray's New note from clipboard, which shares their path) first asks where it
/// goes. Ctrl+N, the + button and a <c>noto://</c> link never ask: their folder is the open one, or the one named.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>Whether a note from a global shortcut asks for its folder (#243); set by the composition root, live.</summary>
    internal bool AskFolder { get; set; }

    // The folder chooser while it is open, so a second press does not stack a second one.
    private Flyout? _folderChooser;

    /// <summary>
    /// A global shortcut's note (F17, F18; the tray's from the clipboard): made at once, or, with Ask which folder on,
    /// once a folder is chosen. Asked once Noto is shown: queued behind the show, as the note itself is.
    /// </summary>
    internal void NewNoteFromGlobalShortcut(bool fromClipboard)
    {
        if (!AskFolder)
        {
            NewNoteFromShortcut(fromClipboard);
            return;
        }

        _ = DispatcherQueue.TryEnqueueGuarded(DispatcherQueuePriority.Low, () => AskFolderForNote(fromClipboard));
    }

    /// <summary>
    /// The folder chooser, under the header over whatever is shown: the folders opened recently first, then the rest
    /// (<see cref="FolderChoice.Order"/>). Typing filters by name, Up and Down choose, Enter (or a click) makes the note
    /// there through the shortcut's own path; Escape, or clicking away, makes nothing and leaves Noto as it was.
    /// </summary>
    /// <remarks>
    /// The keyboard stays in the box, as in Search: the list follows Up and Down, and the folder reached is announced.
    /// The count is the box's live status. With no folder to offer (none yet, or none readable) it does not ask: the
    /// shortcut's own path says what to do.
    /// </remarks>
    private void AskFolderForNote(bool fromClipboard)
    {
        if (WelcomeSurface is not null || _folderChooser is not null)
        {
            return;
        }

        IReadOnlyList<Folder> folders = FolderChoice.Order(RecentOpenedFolders?.Invoke() ?? [], Execute(_listFolders.Execute));
        if (folders.Count == 0)
        {
            NewNoteFromShortcut(fromClipboard);
            return;
        }

        ResourceDictionary resources = Microsoft.UI.Xaml.Application.Current.Resources;
        var box = new TextBox
        {
            PlaceholderText = "Type to filter folders",
            Style = (Style)resources["FolderNameInputStyle"],
            IsSpellCheckEnabled = false,
            MinWidth = 260,
        };
        AutomationProperties.SetName(box, "Folder for the new note");
        AutomationProperties.SetAutomationId(box, "AskFolderBox");
        AutomationProperties.SetHelpText(box, "Type to filter, Up and Down choose a folder, Enter makes the note there, Escape cancels");
        var status = new TextBlock { Style = (Style)resources["FolderPaneGuidanceStyle"] };
        AutomationProperties.SetAutomationId(status, "AskFolderStatus");
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        Accessible.SetAnnounces(status, true);
        var list = new ListView { SelectionMode = ListViewSelectionMode.Single, MaxHeight = 280 };
        AutomationProperties.SetName(list, "Folders");
        AutomationProperties.SetAutomationId(list, "AskFolderList");
        var body = new StackPanel { Spacing = (double)resources["SpaceS"] };
        body.Children.Add(box);
        body.Children.Add(status);
        body.Children.Add(list);

        var flyout = new Flyout { Content = body, Placement = FlyoutPlacementMode.Bottom };
        FolderId? chosen = null;

        void Show()
        {
            IReadOnlyList<Folder> shown = FolderChoice.Filter(folders, box.Text);
            list.Items.Clear();
            foreach (Folder folder in shown)
            {
                var item = new ListViewItem { Content = folder.Name, Tag = folder.Id };
                AutomationProperties.SetName(item, folder.Name);

                // A tap, not ItemClick: an item that is its own container is reported as its content, not itself.
                item.Tapped += (_, _) => Choose(item);
                list.Items.Add(item);
            }

            list.SelectedIndex = shown.Count > 0 ? 0 : -1;
            status.Text = shown.Count switch
            {
                0 => "No folders match.",
                1 => "1 folder",
                _ => string.Create(CultureInfo.InvariantCulture, $"{shown.Count} folders"),
            };
        }

        void Choose(object? item)
        {
            if (item is ListViewItem { Tag: FolderId folder })
            {
                chosen = folder;
                flyout.Hide();
            }
        }

        bool Move(int by)
        {
            if (list.Items.Count == 0)
            {
                return false;
            }

            list.SelectedIndex = Math.Clamp(list.SelectedIndex + by, 0, list.Items.Count - 1);
            list.ScrollIntoView(list.SelectedItem);
            FrameworkElementAutomationPeer.FromElement(box)?.RaiseNotificationEvent(
                AutomationNotificationKind.ActionCompleted,
                AutomationNotificationProcessing.ImportantMostRecent,
                AutomationProperties.GetName((ListViewItem)list.SelectedItem),
                "NotoAskFolderSelection");
            return true;
        }

        box.TextChanged += (_, _) => Show();
        box.PreviewKeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case VirtualKey.Down:
                    e.Handled = Move(+1);
                    break;
                case VirtualKey.Up:
                    e.Handled = Move(-1);
                    break;
                case VirtualKey.Enter:
                    e.Handled = true;
                    Choose(list.SelectedItem);
                    break;
                case VirtualKey.Escape:
                    e.Handled = true;
                    flyout.Hide();
                    break;
            }
        };
        list.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Enter)
            {
                e.Handled = true;
                Choose(list.SelectedItem);
            }
        };
        flyout.Opened += (_, _) => _ = box.Focus(FocusState.Programmatic);

        // Made once the chooser has closed, so its own return of focus cannot take the keyboard from the new note.
        flyout.Closed += (_, _) =>
        {
            _folderChooser = null;
            Trace.WriteLine($"Noto.AskFolder outcome={(chosen is null ? "Cancelled" : "Chosen")}");
            if (chosen is { } folder)
            {
                _ = DispatcherQueue.TryEnqueueGuarded(DispatcherQueuePriority.Low, () => NewNoteFromShortcut(fromClipboard, into: folder));
            }
        };

        Show();
        _folderChooser = flyout;
        flyout.ShowAt(WorkspaceHeader);
        Trace.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Noto.AskFolder shown folders={folders.Count}"));
    }
}
