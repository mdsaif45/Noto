using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Noto.Core.Folders;
using Noto.Core.Notes;
using Noto.Core.Storage;
using Noto.Infrastructure.Portability;
using Noto.UseCases.Notes;
using Windows.ApplicationModel.DataTransfer;

namespace Noto;

/// <summary>
/// Files dropped on a folder or on its note stack (F9, #152): each Markdown or text file a new note, each picture a new
/// note holding it, first in that folder, through the importer. One drop is one result and one Undo, which moves its
/// notes to the Recycle Bin. The files are copied, never moved or changed.
/// </summary>
public sealed partial class MainWindow
{
    // The last drop's notes, for its Undo; forgotten once anything else is said on the line it was said on.
    private IReadOnlyList<NoteId>? _undoable;

    /// <summary>
    /// On the lists, handled events too: a list's own reorder handles drags, and files must be offered to it anyway. A
    /// drop on the card being edited is the editor's, which adds the files to that note.
    /// </summary>
    private void AttachDropTargets()
    {
        NoteList.AddHandler(UIElement.DragEnterEvent, new DragEventHandler(OnNoteListDragOver), handledEventsToo: true);
        NoteList.AddHandler(UIElement.DragEnterEvent, new DragEventHandler(TraceFormats), handledEventsToo: true);
        FolderList.AddHandler(UIElement.DragEnterEvent, new DragEventHandler(TraceFormats), handledEventsToo: true);
        NoteList.AddHandler(UIElement.DragOverEvent, new DragEventHandler(OnNoteListDragOver), handledEventsToo: true);
        NoteList.AddHandler(UIElement.DropEvent, new DragEventHandler(OnNoteListDrop), handledEventsToo: true);
        FolderList.AddHandler(UIElement.DragEnterEvent, new DragEventHandler(OnFolderListDragOver), handledEventsToo: true);
        FolderList.AddHandler(UIElement.DragOverEvent, new DragEventHandler(OnFolderListDragOver), handledEventsToo: true);
        FolderList.AddHandler(UIElement.DropEvent, new DragEventHandler(OnFolderListDrop), handledEventsToo: true);
    }

    /// <summary>What a drag that enters a list offers, by format name only (#31: which sources give files).</summary>
    private static void TraceFormats(object sender, DragEventArgs e) =>
        System.Diagnostics.Trace.WriteLine($"Noto.Drop enter formats={string.Join(',', e.DataView.AvailableFormats)}");

    private void OnNoteListDragOver(object sender, DragEventArgs e)
    {
        if (ForStack(e))
        {
            Offer(e, NoteFolderTitle.Text);
        }
    }

    private void OnFolderListDragOver(object sender, DragEventArgs e)
    {
        if (ForFolder(e) is { } folder)
        {
            Offer(e, folder.Name);
        }
    }

    private async void OnNoteListDrop(object sender, DragEventArgs e)
    {
        if (!ForStack(e) || _openFolderId is not { } folder)
        {
            return;
        }

        e.Handled = true;
        if (await Dropped(e, ShowNoteNotice) is not { } dropped)
        {
            return;
        }

        FilesImport? import;
        try
        {
            import = TryEndEdit() ? await Import(dropped.Paths, folder, ShowNoteNotice) : null;
        }
        finally
        {
            DroppedItems.Release(dropped.Copies);
        }

        if (import is null || _openFolderId != folder)
        {
            return;
        }

        RefreshNotes();
        Said(import, into: null, ShowNoteStatus, ShowNoteNotice, NoteUndoImportButton);
        if (import.Notes.Count > 0)
        {
            FocusNoteRow(import.Notes[0].Value);
        }
    }

    private async void OnFolderListDrop(object sender, DragEventArgs e)
    {
        if (ForFolder(e) is not { } row)
        {
            return;
        }

        e.Handled = true;
        if (await Dropped(e, ShowNotice) is not { } dropped)
        {
            return;
        }

        FilesImport? import;
        try
        {
            import = await Import(dropped.Paths, FolderId.From(row.Id), ShowNotice);
        }
        finally
        {
            DroppedItems.Release(dropped.Copies);
        }

        if (import is null)
        {
            return;
        }

        Refresh();
        Said(import, row.Name, ShowStatus, ShowNotice, UndoImportButton);
    }

    /// <summary>Undo: the drop's notes go to the Recycle Bin, where they can be restored or deleted.</summary>
    private void OnUndoImportClick(object sender, RoutedEventArgs e)
    {
        bool onStack = ReferenceEquals(sender, NoteUndoImportButton);
        if (_undoable is not { } notes || (onStack && !TryEndEdit()))
        {
            return;
        }

        int binned = 0;
        try
        {
            foreach (NoteId note in notes)
            {
                binned += _commands.Dispatch(new DeleteNote(note)).IsSuccess ? 1 : 0;
            }
        }
        catch (StorageException ex)
        {
            (onStack ? (Action<string>)ShowNoteNotice : ShowNotice)($"Could not undo the import: {ex.Message}");
        }

        string said = string.Create(CultureInfo.CurrentCulture, $"Import undone: {DataPortability.Count(binned, "note")} moved to the Recycle Bin.");
        if (onStack)
        {
            RefreshNotes();
            ShowNoteStatus(said);
            MoveFocusToNotes();
        }
        else
        {
            Refresh();
            ShowStatus(said);
            _ = FolderList.Focus(FocusState.Programmatic);
        }

        Announce(said);
    }

    /// <summary>Files over the stack, not over the editor, while it can take them.</summary>
    private bool ForStack(DragEventArgs e) =>
        e.DataView.Contains(StandardDataFormats.StorageItems) && _noteState == NoteState.Loaded && Portability is { Busy: false } && !InEditor(e.OriginalSource);

    /// <summary>The folder row files are over, while the list can take them.</summary>
    private FolderListItem? ForFolder(DragEventArgs e) =>
        e.DataView.Contains(StandardDataFormats.StorageItems) && _state == PaneState.Loaded && Portability is { Busy: false }
            ? RowAt(e)
            : null;

    /// <summary>
    /// The row under the pointer, by its container's bounds: a drag's source element is whatever is hit, often not
    /// inside the row's template (measured: a drop beside a row's name found no row).
    /// </summary>
    private FolderListItem? RowAt(DragEventArgs e)
    {
        foreach (FolderListItem item in Folders)
        {
            if (FolderList.ContainerFromItem(item) is ListViewItem row)
            {
                var at = e.GetPosition(row);
                if (at.X >= 0 && at.Y >= 0 && at.X < row.ActualWidth && at.Y < row.ActualHeight)
                {
                    return item;
                }
            }
        }

        return null;
    }

    private static void Offer(DragEventArgs e, string folder)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = $"Import into {folder}";
        e.Handled = true;
    }

    private static bool InEditor(object source)
    {
        for (var element = source as DependencyObject; element is not null; element = VisualTreeHelper.GetParent(element))
        {
            if (element is NoteEditorHost)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The dropped files' paths, read before the drop ends (a browser's picture copied first, <see cref="DroppedItems"/>);
    /// <see langword="null"/> when they cannot be read.
    /// </summary>
    private static async Task<(IReadOnlyList<string> Paths, string? Copies)?> Dropped(DragEventArgs e, Action<string> notice)
    {
        DragOperationDeferral deferral = e.GetDeferral();
        try
        {
            return await DroppedItems.PathsOf(e.DataView);
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            notice("Those files couldn't be read.");
            return null;
        }
        finally
        {
            deferral.Complete();
        }
    }

    private async Task<FilesImport?> Import(IReadOnlyList<string> files, FolderId folder, Action<string> notice)
    {
        if (Portability is not { Busy: false } portability)
        {
            return null;
        }

        try
        {
            return await portability.ImportFiles(files, folder);
        }
        catch (PortabilityException ex)
        {
            notice(ex.Message == "That folder is no longer there." ? ex.Message : "Those files couldn't be imported.");
            return null;
        }
    }

    /// <summary>One result for the whole drop, quietly; what was not imported on the notice line; Undo when it added notes.</summary>
    private void Said(FilesImport import, string? into, Action<string> status, Action<string> notice, UIElement undo)
    {
        string said = import.Notes.Count == 0
            ? "Nothing was imported."
            : string.Create(CultureInfo.CurrentCulture, $"Imported {DataPortability.Count(import.Notes.Count, "note")}{(into is null ? string.Empty : $" into {into}")}.");
        status(said);
        Announce(said);

        if (NotImported(import) is { Length: > 0 } why)
        {
            notice(why);
        }

        if (import.Notes.Count > 0)
        {
            _undoable = import.Notes;
            undo.Visibility = Visibility.Visible;
        }
    }

    private static string NotImported(FilesImport import)
    {
        string files = import.Skipped.Count switch
        {
            0 => string.Empty,
            1 => $"{import.Skipped[0].File} wasn't imported: {Reason(import.Skipped[0].Kind)}.",
            _ => string.Create(CultureInfo.CurrentCulture, $"{import.Skipped.Count} files weren't imported, such as {import.Skipped[0].File}: {Reason(import.Skipped[0].Kind)}."),
        };
        string pictures = import.ImageIssues.Count == 0
            ? string.Empty
            : $"{DataPortability.Count(import.ImageIssues.Count, "picture")} couldn't be brought in; {(import.ImageIssues.Count == 1 ? "its note still refers to it" : "their notes still refer to them")} as before.";

        return string.Join(' ', new[] { files, pictures }.Where(s => s.Length > 0));
    }

    private static string Reason(ImportIssueKind kind) => kind switch
    {
        ImportIssueKind.TooLarge => "it is larger than 25 MB",
        ImportIssueKind.NotText => "it isn't UTF-8 text",
        ImportIssueKind.ImageTooLarge => "the picture is too large",
        ImportIssueKind.ImageNotAnImage => "it isn't a picture Noto can show",
        _ => "only Markdown, text files and pictures can be imported",
    };

    /// <summary>A drop's Undo is offered until anything else is said on its line, or the surface changes.</summary>
    private void ForgetUndo()
    {
        _undoable = null;
        UndoImportButton.Visibility = Visibility.Collapsed;
        NoteUndoImportButton.Visibility = Visibility.Collapsed;
    }
}
