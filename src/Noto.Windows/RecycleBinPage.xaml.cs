using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Noto.Core.Commands;
using Noto.Core.Folders;
using Noto.Core.Ordering;
using Noto.Core.Storage;
using Noto.UseCases.Workspace;
using Windows.System;

namespace Noto;

/// <summary>One entry as the Recycle Bin shows it: the title, and what it is and where it was.</summary>
public sealed class RecycleBinItem
{
    /// <summary>Required by the XAML type activator (see <see cref="FolderListItem"/>).</summary>
    public RecycleBinItem()
    {
    }

    internal RecycleBinItem(RecycleBinEntry entry) => Entry = entry;

    internal RecycleBinEntry Entry { get; } = new(RecycleBinEntryKind.Note, string.Empty, string.Empty, string.Empty, 0, default, false, false);

    public string Id => Entry.Id;

    /// <summary>The title, or what an untitled note is called.</summary>
    public string DisplayTitle => Entry.Title.Length > 0 ? Entry.Title : NoteCard.UntitledLabel;

    /// <summary>What it is and where it was: also its help text for assistive technology.</summary>
    public string Detail => Entry switch
    {
        { Kind: RecycleBinEntryKind.Folder, NoteCount: 1 } => "Folder, 1 note",
        { Kind: RecycleBinEntryKind.Folder } e => $"Folder, {e.NoteCount} notes",
        { FolderInBin: true } e => $"Note in {e.FolderName}, which is in the Recycle Bin",
        { FolderName.Length: > 0 } e => $"Note in {e.FolderName}",
        _ => "Note",
    };

    /// <summary>The title: a container with no name of its own announces its item's ToString.</summary>
    public override string ToString() => DisplayTitle;
}

/// <summary>
/// The Recycle Bin surface (Phase B5): shows what is in the bin and offers the engine's restore, permanent delete and
/// empty through <see cref="RecycleBin"/>. Asks first, Cancel being the default, before anything that cannot be undone.
/// </summary>
public sealed partial class RecycleBinPage : UserControl
{
    private RecycleBin? _bin;

    public RecycleBinPage()
    {
        InitializeComponent();

        // Each entry's kind and state for assistive technology, on its container (what UI Automation reports).
        RecycleBinList.ContainerContentChanging += static (_, args) =>
        {
            if (args.Item is RecycleBinItem item)
            {
                Accessible.SetRowState(args.ItemContainer, item.Detail);
            }
        };
    }

    /// <summary>The bin's entries, most recently deleted first.</summary>
    public ObservableCollection<RecycleBinItem> Entries { get; } = [];

    /// <summary>The user asked to go back to the folder list.</summary>
    internal event EventHandler? BackRequested;

    /// <summary>Shows the bin and puts focus in it.</summary>
    internal void Show(RecycleBin bin)
    {
        _bin = bin ?? throw new ArgumentNullException(nameof(bin));
        Reload(focus: null);
    }

    /// <summary>Focus on the first entry, or on Back when the bin is empty.</summary>
    internal void FocusFirst() => FocusEntry(Entries.Count > 0 ? Entries[0].Id : null);

    private void OnBackClick(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

    private RecycleBinItem? Selected => RecycleBinList.SelectedItem as RecycleBinItem;

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyButtons();

    private void OnRestoreClick(object sender, RoutedEventArgs e)
    {
        if (Selected is { } item)
        {
            Restore(item);
        }
    }

    private void OnPurgeClick(object sender, RoutedEventArgs e)
    {
        if (Selected is { } item)
        {
            ConfirmPurge(item);
        }
    }

    private void OnEmptyClick(object sender, RoutedEventArgs e) => ConfirmEmpty();

    /// <summary>Enter restores where restore is offered; Delete deletes permanently, after asking.</summary>
    private void OnListPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.OriginalSource is not ListViewItem { Content: RecycleBinItem item })
        {
            return;
        }

        if (e.Key == VirtualKey.Enter && item.Entry.CanRestore)
        {
            e.Handled = true;
            Restore(item);
        }
        else if (e.Key == VirtualKey.Enter)
        {
            // Not silent (B6, F3): say why, and where the way that works is.
            e.Handled = true;
            Announce("Its folder is in the Recycle Bin. Restore the folder, or use Restore to in this note's menu.");
        }
        else if (e.Key == VirtualKey.Delete)
        {
            e.Handled = true;
            ConfirmPurge(item);
        }
    }

    /// <summary>An entry's menu: Restore where offered, Restore to... for a note, Delete permanently.</summary>
    private void OnListContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        RecycleBinItem? item = args.OriginalSource switch
        {
            ListViewItem { Content: RecycleBinItem row } => row,
            FrameworkElement { DataContext: RecycleBinItem row } => row,
            _ => null,
        };

        if (item is null || _bin is null || RecycleBinList.ContainerFromItem(item) is not ListViewItem container)
        {
            return;
        }

        args.Handled = true;
        RecycleBinList.SelectedItem = item;

        var menu = new MenuFlyout();

        if (item.Entry.CanRestore)
        {
            menu.Items.Add(Item("Restore", "Enter", () => Restore(item)));
        }

        if (item.Entry.Kind == RecycleBinEntryKind.Note)
        {
            var to = new MenuFlyoutSubItem { Text = "Restore to" };
            foreach (Folder folder in Read(_bin.Destinations) ?? [])
            {
                to.Items.Add(Item(folder.Name, string.Empty, () => RestoreTo(item, folder)));
            }

            if (to.Items.Count == 0)
            {
                to.Items.Add(Item("No folders to restore into", string.Empty, static () => { }, enabled: false));
            }

            menu.Items.Add(to);
        }

        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Item("Delete permanently", "Delete", () => ConfirmPurge(item)));

        if (args.TryGetPosition(container, out Windows.Foundation.Point point))
        {
            menu.ShowAt(container, point);
        }
        else
        {
            menu.ShowAt(container);
        }
    }

    private void Restore(RecycleBinItem item)
    {
        if (_bin is null)
        {
            return;
        }

        Act(item, () => _bin.Restore(item.Entry), "Restored");
    }

    private void RestoreTo(RecycleBinItem item, Folder folder)
    {
        if (_bin is null)
        {
            return;
        }

        Act(item, () => _bin.RestoreTo(item.Entry, folder.Id), $"Restored to {folder.Name}");
    }

    /// <summary>Deletes an entry permanently once the user has said so; Cancel, the default, writes nothing.</summary>
    private async void ConfirmPurge(RecycleBinItem item)
    {
        if (_bin is null)
        {
            return;
        }

        string what = item.Entry.Kind == RecycleBinEntryKind.Folder
            ? $"The folder “{item.DisplayTitle}” and its {Notes(item.Entry.NoteCount)} in the Recycle Bin will be deleted permanently."
            : $"“{item.DisplayTitle}” will be deleted permanently.";

        if (await Confirm("Delete permanently?", $"{what} This can't be undone.", "Delete permanently"))
        {
            Act(item, () => _bin.Purge(item.Entry), "Deleted permanently");
        }
    }

    /// <summary>Empties the bin once the user has said so; Cancel, the default, writes nothing.</summary>
    private async void ConfirmEmpty()
    {
        if (_bin is null || Entries.Count == 0)
        {
            return;
        }

        if (!await Confirm("Empty the Recycle Bin?", "Everything in the Recycle Bin will be deleted permanently. This can't be undone.", "Empty"))
        {
            return;
        }

        try
        {
            CommandResult<RecycleBinPurge> result = _bin.Empty();

            if (!result.IsSuccess)
            {
                ShowNotice("The Recycle Bin could not be emptied.");
                Reload(focus: null);
                return;
            }

            Reload(focus: null);
            Announce(result.Value.FoldersKept.Count > 0
                ? "Emptied, except folders that still hold notes"
                : "The Recycle Bin is empty");
        }
        catch (StorageException ex)
        {
            ShowNotice($"Could not empty the Recycle Bin: {ex.Message}");
            Reload(focus: null);
        }
    }

    /// <summary>
    /// One action on one entry: the bin is re-read, focus goes to the next entry, else the previous, else Back, and the
    /// result is announced. A refusal keeps the entry and says why.
    /// </summary>
    private void Act(RecycleBinItem item, Func<CommandResult> action, string done)
    {
        string? next = Removal.Next(Entries, new EntryKey(item.Id), static e => new EntryKey(e.Id))?.Id;

        try
        {
            CommandResult result = action();

            if (!result.IsSuccess)
            {
                ShowNotice(Describe(result.Failure!));
                Reload(item.Id);
                return;
            }

            HideNotice();
            Reload(next);
            Announce(done);
        }
        catch (StorageException ex)
        {
            ShowNotice($"Could not change the Recycle Bin: {ex.Message}");
            Reload(item.Id);
        }
    }

    private void Reload(string? focus)
    {
        if (_bin is null)
        {
            return;
        }

        IReadOnlyList<RecycleBinEntry>? entries = Read(_bin.Entries);

        Entries.Clear();
        foreach (RecycleBinEntry entry in entries ?? [])
        {
            Entries.Add(new RecycleBinItem(entry));
        }

        if (entries is not null)
        {
            BinGuidanceText.Text = Entries.Count == 0
                ? "The Recycle Bin is empty. Deleted notes and folders are kept here until you delete them permanently."
                : string.Empty;
            BinGuidanceText.Visibility = Entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        ApplyButtons();
        FocusEntry(Entries.Any(e => e.Id == focus) ? focus : Entries.FirstOrDefault()?.Id);
    }

    private T? Read<T>(Func<T> read)
        where T : class
    {
        try
        {
            return read();
        }
        catch (StorageException ex)
        {
            ShowNotice($"Could not read the Recycle Bin: {ex.Message}");
            return null;
        }
    }

    private void ApplyButtons()
    {
        BinRestoreButton.IsEnabled = Selected is { Entry.CanRestore: true };
        BinPurgeButton.IsEnabled = Selected is not null;
        EmptyBinButton.IsEnabled = Entries.Count > 0;
    }

    private void FocusEntry(string? id)
    {
        if (Entries.FirstOrDefault(e => e.Id == id) is not { } item)
        {
            _ = DispatcherQueue.TryEnqueueGuarded(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => BinBackButton.Focus(FocusState.Programmatic));
            return;
        }

        RecycleBinList.SelectedItem = item;
        RecycleBinList.ScrollIntoView(item);
        _ = DispatcherQueue.TryEnqueueGuarded(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () =>
            {
                if (RecycleBinList.ContainerFromItem(item) is ListViewItem container)
                {
                    _ = container.Focus(FocusState.Programmatic);
                }
            });
    }

    /// <summary>A dialog for what cannot be undone. Cancel is the default button; only the primary choice acts.</summary>
    private async Task<bool> Confirm(string title, string content, string primary)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = content,
            PrimaryButtonText = primary,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private static string Notes(int count) => count == 1 ? "1 note" : $"{count} notes";

    private static string Describe(CommandFailure failure) => failure.Reason switch
    {
        CommandFailureReason.NotFound => "That item is no longer in the Recycle Bin.",
        CommandFailureReason.InvalidState => "Its folder is in the Recycle Bin: restore the folder, or restore the note to another folder.",
        _ => "That could not be done.",
    };

    private void ShowNotice(string message)
    {
        BinNoticeText.Text = message;
        BinNoticeText.Visibility = Visibility.Visible;
    }

    private void HideNotice()
    {
        BinNoticeText.Text = string.Empty;
        BinNoticeText.Visibility = Visibility.Collapsed;
    }

    private void Announce(string message) =>
        FrameworkElementAutomationPeer.FromElement(RecycleBinList)?.RaiseNotificationEvent(
            AutomationNotificationKind.ActionCompleted,
            AutomationNotificationProcessing.ImportantMostRecent,
            message,
            "NotoRecycleBin");

    private static MenuFlyoutItem Item(string text, string keys, Action action, bool enabled = true)
    {
        var item = new MenuFlyoutItem { Text = text, KeyboardAcceleratorTextOverride = keys, IsEnabled = enabled };
        item.Click += (_, _) => action();
        return item;
    }

    /// <summary>An entry's id as the removal rule's key (ids are unique across folders and notes: both are ULIDs).</summary>
    private readonly record struct EntryKey(string Id);
}
