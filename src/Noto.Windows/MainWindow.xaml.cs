using System.Collections.ObjectModel;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Noto.Core.Commands;
using Noto.Core.Folders;
using Noto.Core.Notes;
using Noto.Core.Storage;
using Noto.UseCases.Folders;
using Noto.UseCases.Notes;
using Windows.System;
using Windows.UI.Core;

namespace Noto;

/// <summary>
/// The M2-1 Folder Pane.
/// </summary>
/// <remarks>
/// <para>
/// Receives <b>use cases</b>, never repositories or a database. The
/// composition root in <see cref="App"/> owns the concrete Infrastructure
/// types; this class cannot reach SQLite even by accident, which is the
/// boundary ADR-009 requires expressed as a constructor signature.
/// </para>
/// <para>
/// <b>Every mutation re-queries.</b> After a successful command the pane asks
/// the database again rather than editing its own list. ADR-010 is explicit
/// that "events are not the persistence mechanism. The database is." — and the
/// engine makes that concrete: O2 decides where a folder sits, so a create or
/// a rename can move a row and only the query knows where.
/// </para>
/// <para>
/// No MVVM framework, no DI container, no state library. One collection, one
/// selected id and a small state enum are the whole model. Code-behind is the
/// deliberate choice for a single pane; introducing MVVM because this file is
/// growing would be adopting an architecture to solve a file-length problem.
/// </para>
/// </remarks>
public sealed partial class MainWindow : Window
{
    private readonly ListFoldersQuery _listFolders;
    private readonly CreateFolderHandler _createFolder;
    private readonly RenameFolderHandler _renameFolder;
    private readonly ListNotesInFolderQuery _listNotes;
    private readonly GetNoteQuery _getNote;
    private readonly UpdateNoteContentHandler _updateNote;
    private readonly CreateNoteHandler _createNote;
    private readonly DeleteNoteHandler _deleteNote;

    /// <summary>
    /// The selected folder, by id.
    /// </summary>
    /// <remarks>
    /// Never an index. Folder names are not unique (Case G) and O2 can move a
    /// row when it is renamed, so an index goes stale across the re-query that
    /// every mutation performs.
    /// </remarks>
    private string? _selectedId;

    private PaneState _state = PaneState.Loading;

    /// <summary>The row being renamed, or <see langword="null"/>.</summary>
    private string? _renamingId;

    /// <summary>
    /// The folder whose notes are shown, or <see langword="null"/> when the
    /// folder list is the active surface.
    /// </summary>
    /// <remarks>
    /// Doubles as the "which surface is active" flag. A separate boolean could
    /// disagree with it; one field cannot.
    /// </remarks>
    private FolderId? _openFolderId;

    /// <summary>The selected note, by id. Never an index.</summary>
    private string? _selectedNoteId;

    private NoteState _noteState = NoteState.Loading;

    /// <summary>
    /// The note open in the editor, or <see langword="null"/> when the editor
    /// is not the active surface.
    /// </summary>
    /// <remarks>
    /// Doubles as the "is the editor open" flag, the way
    /// <see cref="_openFolderId"/> does for the note surface. Two fields that
    /// can disagree is a state machine with a hole in it.
    /// </remarks>
    private string? _openNoteId;

    /// <summary>
    /// The content the editor was opened with.
    /// </summary>
    /// <remarks>
    /// Dirtiness is this compared against the live text, rather than a flag a
    /// TextChanged handler maintains: a flag has to be cleared in every exit
    /// path and stays true if one is missed, which would make a clean buffer
    /// take the save path. Typing something and undoing it correctly counts as
    /// clean here.
    /// </remarks>
    private string _openNoteBaseline = string.Empty;

    private EditorState _editorState = EditorState.Loading;

    public MainWindow(
        ListFoldersQuery listFolders,
        CreateFolderHandler createFolder,
        RenameFolderHandler renameFolder,
        ListNotesInFolderQuery listNotes,
        GetNoteQuery getNote,
        UpdateNoteContentHandler updateNote,
        CreateNoteHandler createNote,
        DeleteNoteHandler deleteNote)
    {
        _listFolders = listFolders ?? throw new ArgumentNullException(nameof(listFolders));
        _createFolder = createFolder ?? throw new ArgumentNullException(nameof(createFolder));
        _renameFolder = renameFolder ?? throw new ArgumentNullException(nameof(renameFolder));
        _listNotes = listNotes ?? throw new ArgumentNullException(nameof(listNotes));
        _getNote = getNote ?? throw new ArgumentNullException(nameof(getNote));
        _updateNote = updateNote ?? throw new ArgumentNullException(nameof(updateNote));
        _createNote = createNote ?? throw new ArgumentNullException(nameof(createNote));
        _deleteNote = deleteNote ?? throw new ArgumentNullException(nameof(deleteNote));

        InitializeComponent();

        // Loading is a real state with a real transition, entered before any
        // query runs and left only when one finishes.
        //
        // The first query hangs off the root's Loaded event rather than
        // running here: the constructor executes before App calls Activate(),
        // so work started from it finishes before the window is ever shown.
        //
        // It is NOT usually visible, and that is correct rather than a defect.
        // Measured: 7.1ms to read 5003 folders — less than half a 16ms frame,
        // so the pane reaches Loaded before a frame carrying "Loading…" could
        // be presented. The state exists for the case that is slow (a large
        // database on a cold or contended disk) and for Retry, where the query
        // is re-run against a store that has already failed once. Making it
        // reliably visible would mean delaying the query on purpose, which
        // would be a worse product for a nicer screenshot.
        EnterLoading();

        PaneRoot.Loaded += OnPaneFirstLoaded;
    }

    /// <summary>
    /// Runs the first query, once the pane has actually been laid out.
    /// </summary>
    private void OnPaneFirstLoaded(object sender, RoutedEventArgs e)
    {
        // One-shot: this is the initial load, not a re-entry point.
        PaneRoot.Loaded -= OnPaneFirstLoaded;

        Refresh();
    }

    /// <summary>
    /// The pane's state.
    /// </summary>
    /// <remarks>
    /// Scoped to this pane — deliberately not a global workspace state. Empty
    /// is a display variant of Loaded rather than a sibling: it allows exactly
    /// the same interactions and differs only in what is on screen, so making
    /// it a separate state would duplicate every transition.
    /// </remarks>
    private enum PaneState
    {
        Loading,
        Loaded,
        Error,
        Mutating,
    }

    /// <summary>
    /// The note surface's state.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="PaneState"/> because the two surfaces are
    /// never active at once and share no transitions. There is no Mutating
    /// member: M2-2 reads notes and never writes them.
    /// </remarks>
    private enum NoteState
    {
        Loading,
        Loaded,
        Error,
    }

    /// <summary>
    /// The editor's state.
    /// </summary>
    /// <remarks>
    /// No Saving member: a save happens on leave and either succeeds — in
    /// which case the surface is already gone — or fails, which is a notice
    /// over a still-editable buffer rather than a state of its own.
    /// </remarks>
    private enum EditorState
    {
        Loading,
        Loaded,
        Error,
    }

    public ObservableCollection<FolderListItem> Folders { get; } = [];

    /// <summary>The notes of the open folder, in O2 order.</summary>
    public ObservableCollection<NoteListItem> Notes { get; } = [];

    /// <summary>Whether the list is showing the empty variant of Loaded.</summary>
    private bool IsEmpty => _state == PaneState.Loaded && Folders.Count == 0;

    /// <summary>
    /// Puts the pane into <see cref="PaneState.Loading"/> and paints it.
    /// </summary>
    private void EnterLoading()
    {
        _state = PaneState.Loading;
        ApplyState();
    }

    /// <summary>
    /// Recovery from <see cref="PaneState.Error"/>.
    /// </summary>
    /// <remarks>
    /// Error disables the list, the input and Create, so without this the pane
    /// is a dead end and the only way out is restarting the application. The
    /// contract requires Error -> Retry -> Loading.
    /// </remarks>
    private void OnRetryClick(object sender, RoutedEventArgs e)
    {
        EnterLoading();

        // Queued rather than called directly so the state change is applied
        // and handed back to the framework before the read begins, matching
        // the initial load. Whether a frame carrying it is actually presented
        // depends on how long the read takes.
        _ = DispatcherQueue.TryEnqueue(Refresh);
    }

    /// <summary>
    /// The read path — executes Q3 and replaces the displayed list.
    /// </summary>
    /// <remarks>
    /// Rebuilt wholesale rather than diffed. The query already returns folders
    /// in the order the contract requires (O2 — pinned first, then SortOrder,
    /// then Id), so replacing the collection keeps the view agreeing with the
    /// database by construction. Diffing would reimplement that ordering in a
    /// second place.
    /// </remarks>
    private void Refresh()
    {
        try
        {
            IReadOnlyList<Folder> folders = _listFolders.Execute();

            Folders.Clear();
            foreach (Folder folder in folders)
            {
                Folders.Add(new FolderListItem(folder.Id.Value, folder.Name, folder.IsPinned));
            }

            _state = PaneState.Loaded;

            // A successful read answers whatever the last failure said, so the
            // notice must not outlive it — otherwise a recovered Retry still
            // shows "could not read the folders" above a working list.
            HideNotice();

            // Selection survives the re-query only if the folder still exists.
            // A folder deleted elsewhere leaves a stale id that must not keep
            // a phantom row selected.
            RestoreSelection();
            ApplyState();
        }
        catch (StorageException ex)
        {
            // The infrastructure channel. §6 keeps it separate from business
            // failure precisely so the two can be told apart here.
            _state = PaneState.Error;
            ApplyState();
            ShowNotice($"Could not read the folders: {ex.Message}");
        }
    }

    /// <summary>
    /// Re-applies the selected id to the rebuilt list.
    /// </summary>
    private void RestoreSelection()
    {
        if (_selectedId is null)
        {
            FolderList.SelectedIndex = -1;
            return;
        }

        FolderListItem? match = Folders.FirstOrDefault(f => f.Id == _selectedId);

        if (match is null)
        {
            // The selected folder is gone — deleted outside this pane. Drop the
            // selection rather than silently selecting a neighbour, which would
            // be indistinguishable from the user having chosen it.
            _selectedId = null;
            FolderList.SelectedIndex = -1;
            return;
        }

        FolderList.SelectedItem = match;
    }

    /// <summary>
    /// Enables and disables the pane's controls for the current state.
    /// </summary>
    /// <remarks>
    /// Mutating keeps the list <i>visible</i> and merely disables input. A
    /// local SQLite write is sub-millisecond, and swapping the content for a
    /// spinner at that duration is a flicker rather than feedback.
    /// </remarks>
    private void ApplyState()
    {
        bool interactive = _state is PaneState.Loaded;

        FolderNameInput.IsEnabled = interactive;
        CreateButton.IsEnabled = interactive;
        FolderList.IsEnabled = interactive;

        // Retry is the ONLY control offered in Error, and it is offered
        // nowhere else: there is nothing to retry from a state that loaded.
        RetryButton.Visibility = _state == PaneState.Error
            ? Visibility.Visible
            : Visibility.Collapsed;
        RetryButton.IsEnabled = _state == PaneState.Error;

        // Guidance describes the LIST. It is independent of any notice, so a
        // failed mutation no longer erases the fact that the list is empty.
        switch (_state)
        {
            case PaneState.Loading:
                ShowGuidance("Loading…");
                break;

            case PaneState.Error:
                ShowGuidance("The folder list could not be loaded.");
                break;

            default:
                if (IsEmpty)
                {
                    ShowGuidance("No folders yet. Type a name above to create the first one.");
                }
                else
                {
                    HideGuidance();
                }

                break;
        }
    }

    // ---------------------------------------------------------------- create

    private void OnCreateFolderClick(object sender, RoutedEventArgs e) => SubmitCreate();

    /// <summary>
    /// Enter submits from the name box, so creating never requires the mouse.
    /// </summary>
    private void OnCreateInputKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Enter:
                e.Handled = true;
                SubmitCreate();
                break;

            case VirtualKey.Escape:
                e.Handled = true;
                FolderNameInput.Text = string.Empty;
                HideNotice();
                MoveFocusToList();
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// The write path — CreateFolder, then re-query and select the new folder.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The new folder is selected by the <see cref="FolderId"/> the handler
    /// returns, not by matching on name. Names are not unique (Case G), so
    /// after creating a second "Work" a name match could select the wrong row.
    /// </para>
    /// <para>
    /// No duplicate-name handling: duplicates are legal, and
    /// <c>CreateFolder</c> cannot return <c>DuplicateName</c>. The input is
    /// also passed verbatim — the engine stores folder names untrimmed, and
    /// trimming here would make the pane disagree with what was persisted.
    /// </para>
    /// </remarks>
    private void SubmitCreate()
    {
        if (_state != PaneState.Loaded)
        {
            return;
        }

        _state = PaneState.Mutating;
        ApplyState();

        try
        {
            CommandResult<FolderId> result =
                _createFolder.Handle(new CreateFolder(FolderNameInput.Text));

            if (!result.IsSuccess)
            {
                // The BUSINESS failure channel — returned, not thrown, because
                // "that name is empty" is an expected outcome (§6). The typed
                // text is preserved so the user can correct it.
                _state = PaneState.Loaded;
                ApplyState();
                ShowNotice(Describe(result.Failure!));
                FocusInput();
                return;
            }

            string created = result.Value.Value;

            _state = PaneState.Loaded;
            FolderNameInput.Text = string.Empty;
            _selectedId = created;

            Refresh();
            FocusRow(created);
        }
        catch (StorageException ex)
        {
            _state = PaneState.Loaded;
            ApplyState();
            ShowNotice($"Could not create the folder: {ex.Message}");
            FocusInput();
        }
    }

    // ---------------------------------------------------------------- rename

    /// <summary>
    /// Starts an inline rename of the focused row (parity C13).
    /// </summary>
    /// <remarks>
    /// F2 rather than double-click: parity C4 reserves double-click for
    /// entering a folder, which is not implemented yet, and binding it now
    /// would have to be taken back.
    /// </remarks>
    private void BeginRename()
    {
        if (_state != PaneState.Loaded)
        {
            return;
        }

        // Selection normally follows arrow-key focus, but a row can hold focus
        // without being selected (a programmatic or assistive-technology focus
        // change). Renaming the focused row is what the user asked for in that
        // case; falling back keeps F2 from doing nothing at all.
        FolderListItem? item = FolderList.SelectedItem as FolderListItem
            ?? (FocusManager.GetFocusedElement(Content.XamlRoot) as ListViewItem)?.Content as FolderListItem;

        if (item is null)
        {
            return;
        }

        _renamingId = item.Id;
        RenameEditor.Text = item.Name;
        RenameEditor.Visibility = Visibility.Visible;
        HideNotice();

        // The whole name is selected so that typing replaces it, which is what
        // a rename usually is; the caret is still placeable for an edit.
        _ = RenameEditor.Focus(FocusState.Programmatic);
        RenameEditor.SelectAll();
    }

    private void OnRenameEditorKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Enter:
                e.Handled = true;
                CommitRename();
                break;

            case VirtualKey.Escape:
                e.Handled = true;
                CancelRename();
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// Focus loss cancels rather than commits.
    /// </summary>
    /// <remarks>
    /// C13 documents Esc-cancel and an explicit commit; it says nothing about
    /// blur. Committing on blur would persist a value the user may have walked
    /// away from, and a silent write is the more destructive reading of an
    /// underspecified case.
    /// </remarks>
    private void OnRenameEditorLostFocus(object sender, RoutedEventArgs e)
    {
        if (_renamingId is not null)
        {
            CancelRename();
        }
    }

    private void CancelRename()
    {
        string? id = _renamingId;

        _renamingId = null;
        RenameEditor.Visibility = Visibility.Collapsed;
        HideNotice();

        if (id is not null)
        {
            FocusRow(id);
        }
    }

    private void CommitRename()
    {
        if (_renamingId is not { } id)
        {
            return;
        }

        string name = RenameEditor.Text;

        _state = PaneState.Mutating;
        ApplyState();

        try
        {
            CommandResult result = _renameFolder.Handle(new RenameFolder(FolderId.From(id), name));

            if (!result.IsSuccess)
            {
                _state = PaneState.Loaded;
                ApplyState();

                CommandFailureReason reason = result.Failure!.Reason;

                if (reason is CommandFailureReason.NotFound or CommandFailureReason.InvalidState)
                {
                    // The folder is gone or binned: the editor is editing
                    // something that no longer exists, so close it and let the
                    // re-query reconcile the list.
                    _renamingId = null;
                    RenameEditor.Visibility = Visibility.Collapsed;
                    Refresh();
                    ShowNotice(Describe(result.Failure!));
                    return;
                }

                // InvalidInput: the editor stays open with the typed text so
                // the user can fix it rather than retype it.
                ShowNotice(Describe(result.Failure!));
                _ = RenameEditor.Focus(FocusState.Programmatic);
                return;
            }

            _renamingId = null;
            RenameEditor.Visibility = Visibility.Collapsed;
            _state = PaneState.Loaded;

            // Selection is kept by id: a rename can change the row's position
            // under O2, so the index it had is meaningless afterwards.
            _selectedId = id;
            Refresh();
            FocusRow(id);
        }
        catch (StorageException ex)
        {
            _state = PaneState.Loaded;
            ApplyState();
            ShowNotice($"Could not rename the folder: {ex.Message}");
            _ = RenameEditor.Focus(FocusState.Programmatic);
        }
    }

    // ----------------------------------------------------------- note list

    /// <summary>
    /// Enters a folder and shows its notes (parity C1, C4).
    /// </summary>
    /// <remarks>
    /// Selection normally follows arrow-key focus, but a row can hold focus
    /// without being selected, so the focused row is the fallback — the same
    /// rule <see cref="BeginRename"/> uses, for the same reason.
    /// </remarks>
    private void EnterSelectedFolder()
    {
        if (_state != PaneState.Loaded)
        {
            return;
        }

        FolderListItem? item = FolderList.SelectedItem as FolderListItem
            ?? (FocusManager.GetFocusedElement(Content.XamlRoot) as ListViewItem)?.Content as FolderListItem;

        if (item is null)
        {
            return;
        }

        // The id is captured now: the list is rebuilt on return, and an index
        // or a name would not survive that. Names are not unique (Case G).
        _openFolderId = FolderId.From(item.Id);

        // A folder is entered fresh. Nothing establishes remembering which
        // note was selected last time, and inventing it would be a behaviour
        // nobody asked for.
        _selectedNoteId = null;

        BackButton.Content = $"‹ {item.Name}";

        PaneRoot.Visibility = Visibility.Collapsed;
        NoteRoot.Visibility = Visibility.Visible;

        EnterNoteLoading();

        // Queued for the same reason as the folder list's first load: the
        // Loading state is applied and handed back to the framework before
        // the read begins.
        _ = DispatcherQueue.TryEnqueue(RefreshNotes);
    }

    /// <summary>
    /// Returns to the folder list (parity C5, A12 mode 1).
    /// </summary>
    /// <remarks>
    /// Note selection is discarded rather than remembered: no requirement
    /// establishes per-folder note selection, and restoring it silently would
    /// be a product decision made in implementation.
    /// </remarks>
    private void LeaveFolder()
    {
        string? folderId = _openFolderId?.Value;

        _openFolderId = null;
        _selectedNoteId = null;
        Notes.Clear();
        HideNoteNotice();
        HideNoteGuidance();

        NoteRoot.Visibility = Visibility.Collapsed;
        PaneRoot.Visibility = Visibility.Visible;

        // The folder list was left intact, so its selection is still correct;
        // focus has to be put back explicitly.
        if (folderId is not null)
        {
            _selectedId = folderId;
            RestoreSelection();
            FocusRow(folderId);
        }
        else
        {
            MoveFocusToList();
        }
    }

    private void EnterNoteLoading()
    {
        _noteState = NoteState.Loading;
        ApplyNoteState();
    }

    /// <summary>
    /// The note read path — executes Q2 for the open folder.
    /// </summary>
    /// <remarks>
    /// Rebuilt wholesale rather than diffed, for the reason the folder list is:
    /// the query already returns O2 order (pinned, SortOrder, Id), so replacing
    /// the collection keeps the view agreeing with the database by
    /// construction.
    /// </remarks>
    private void RefreshNotes()
    {
        if (_openFolderId is not { } folderId)
        {
            return;
        }

        try
        {
            IReadOnlyList<Note> notes = _listNotes.Execute(folderId);

            Notes.Clear();
            foreach (Note note in notes)
            {
                Notes.Add(new NoteListItem(note.Id.Value, note.Title, note.IsPinned));
            }

            _noteState = NoteState.Loaded;

            // A successful read answers whatever the last failure said.
            HideNoteNotice();
            RestoreNoteSelection();
            ApplyNoteState();
        }
        catch (StorageException ex)
        {
            _noteState = NoteState.Error;
            ApplyNoteState();
            ShowNoteNotice($"Could not read the notes: {ex.Message}");
        }
    }

    /// <summary>
    /// Re-applies the selected note id to the rebuilt list.
    /// </summary>
    private void RestoreNoteSelection()
    {
        if (_selectedNoteId is null)
        {
            NoteList.SelectedIndex = -1;
            return;
        }

        NoteListItem? match = Notes.FirstOrDefault(n => n.Id == _selectedNoteId);

        if (match is null)
        {
            // The note is gone. Drop the selection rather than sliding to a
            // neighbour, which would be indistinguishable from a choice the
            // user made.
            _selectedNoteId = null;
            NoteList.SelectedIndex = -1;
            return;
        }

        NoteList.SelectedItem = match;
    }

    private void ApplyNoteState()
    {
        bool interactive = _noteState == NoteState.Loaded;

        NoteList.IsEnabled = interactive;

        NoteRetryButton.Visibility = _noteState == NoteState.Error
            ? Visibility.Visible
            : Visibility.Collapsed;
        NoteRetryButton.IsEnabled = _noteState == NoteState.Error;

        switch (_noteState)
        {
            case NoteState.Loading:
                ShowNoteGuidance("Loading…");
                break;

            case NoteState.Error:
                ShowNoteGuidance("The notes could not be loaded.");
                break;

            default:
                if (Notes.Count == 0)
                {
                    ShowNoteGuidance("This folder has no notes yet.");
                }
                else
                {
                    HideNoteGuidance();
                }

                break;
        }
    }

    private void OnBackClick(object sender, RoutedEventArgs e) => LeaveFolder();

    /// <summary>
    /// Recovery from a failed note read. Error disables the list, so without
    /// this the surface would be a dead end.
    /// </summary>
    private void OnNoteRetryClick(object sender, RoutedEventArgs e)
    {
        EnterNoteLoading();
        _ = DispatcherQueue.TryEnqueue(RefreshNotes);
    }

    /// <summary>
    /// The note list's keyboard contract.
    /// </summary>
    /// <remarks>
    /// Enter opens the selected note — the binding M2-2 deliberately left free
    /// because opening a note needed an editor to open it into. Ctrl+N creates
    /// one (parity B1, "context-dependent": the same chord makes a folder in
    /// the folder list).
    /// </remarks>
    private void OnNoteListKeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Enter is NOT handled here: a ListView treats it as item activation
        // and marks it handled before KeyDown bubbles, so it never arrives.
        // It is OnNoteListPreviewKeyDown instead — the same fix the folder
        // list needed for Ctrl+Down.
        //
        // Ctrl+N stays on this handler because runtime validation shows it
        // arrives: the list claims Enter, not every chord, and moving a
        // binding that demonstrably works would be a change with no evidence
        // behind it.
        if (e.Key == VirtualKey.N && IsControlDown())
        {
            e.Handled = true;
            CreateNoteInOpenFolder();
        }
    }

    /// <summary>
    /// Enter opens the selected note.
    /// </summary>
    /// <remarks>
    /// PreviewKeyDown, because a ListView consumes Enter as item activation
    /// before a KeyDown handler on the control can see it. Verified at
    /// runtime: with a row selected and focused, Enter on KeyDown did nothing
    /// while Ctrl+N on that same handler opened the editor — so the handler
    /// fires and the key is what differs. Preview runs on the way down, ahead
    /// of the control.
    /// </remarks>
    private void OnNoteListPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            OpenSelectedNote();
        }
    }

    private void OnNoteListDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        e.Handled = true;
        OpenSelectedNote();
    }

    /// <summary>
    /// Opens the selected note, falling back to the focused row.
    /// </summary>
    /// <remarks>
    /// The fallback matches <see cref="BeginRename"/> and
    /// <see cref="EnterSelectedFolder"/>: a row can hold focus without being
    /// selected under programmatic or assistive-technology focus, and doing
    /// nothing in that case would look broken.
    /// </remarks>
    private void OpenSelectedNote()
    {
        if (_noteState != NoteState.Loaded)
        {
            return;
        }

        NoteListItem? item = NoteList.SelectedItem as NoteListItem
            ?? (FocusManager.GetFocusedElement(Content.XamlRoot) as ListViewItem)?.Content as NoteListItem;

        if (item is not null)
        {
            OpenNote(item.Id);
        }
    }

    private void OnNoteSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        _selectedNoteId = (NoteList.SelectedItem as NoteListItem)?.Id;

    private void ShowNoteNotice(string message)
    {
        NoteNoticeText.Text = message;
        NoteNoticeText.Visibility = Visibility.Visible;
    }

    private void HideNoteNotice()
    {
        NoteNoticeText.Text = string.Empty;
        NoteNoticeText.Visibility = Visibility.Collapsed;
    }

    private void ShowNoteGuidance(string message)
    {
        NoteGuidanceText.Text = message;
        NoteGuidanceText.Visibility = Visibility.Visible;
    }

    private void HideNoteGuidance()
    {
        NoteGuidanceText.Text = string.Empty;
        NoteGuidanceText.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Puts keyboard focus on a note row, so focus always has a destination.
    /// </summary>
    /// <remarks>
    /// The same shape as <see cref="FocusRow"/>, and deferred for the same
    /// reason: a virtualised ListView realises a container only for a row that
    /// is in view, and an unrealised row cannot take focus.
    /// </remarks>
    private void FocusNoteRow(string id)
    {
        NoteListItem? item = Notes.FirstOrDefault(n => n.Id == id);

        if (item is null)
        {
            _ = NoteList.Focus(FocusState.Programmatic);
            return;
        }

        NoteList.SelectedItem = item;
        NoteList.ScrollIntoView(item);

        _ = NoteList.DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () =>
            {
                if (NoteList.ContainerFromItem(item) is ListViewItem container)
                {
                    _ = container.Focus(FocusState.Programmatic);
                }
                else
                {
                    _ = NoteList.Focus(FocusState.Programmatic);
                }
            });
    }

    // -------------------------------------------------------- note editor

    /// <summary>Whether the editor holds unsaved changes.</summary>
    /// <remarks>
    /// Computed by comparing the live text against the content the note was
    /// opened with, rather than a flag a TextChanged handler maintains: a flag
    /// must be cleared on every exit path and stays true if one is missed,
    /// which would send a clean buffer down the save path. Typing something
    /// and undoing it correctly reads as clean here.
    /// </remarks>
    private bool EditorIsDirty =>
        _openNoteId is not null
        && !string.Equals(NoteEditor.Text, _openNoteBaseline, StringComparison.Ordinal);

    /// <summary>
    /// Opens a note in the editor.
    /// </summary>
    private void OpenNote(string noteId)
    {
        _openNoteId = noteId;

        NoteRoot.Visibility = Visibility.Collapsed;
        EditorRoot.Visibility = Visibility.Visible;

        _editorState = EditorState.Loading;
        ApplyEditorState();

        // Queued for the same reason the other surfaces queue their first
        // read: the state is applied and handed back before the read begins.
        _ = DispatcherQueue.TryEnqueue(LoadOpenNote);
    }

    /// <summary>
    /// Reads the open note and fills the buffer.
    /// </summary>
    /// <remarks>
    /// <c>GetNoteQuery</c> applies I1, so a note in the recycle bin reports
    /// NotFound exactly as a missing one does. Both mean the same thing to the
    /// user — it is not there — so both return to the list rather than
    /// stranding them in an editor with nothing to edit.
    /// </remarks>
    private void LoadOpenNote()
    {
        if (_openNoteId is not { } id)
        {
            return;
        }

        try
        {
            CommandResult<Note> result = _getNote.Execute(NoteId.From(id));

            if (!result.IsSuccess)
            {
                LeaveEditor(discard: true);
                RefreshNotes();
                ShowNoteNotice(DescribeNote(result.Failure!));
                return;
            }

            NoteEditor.Text = result.Value.Content;
            _openNoteBaseline = result.Value.Content;
            UpdateEditorBackLabel();

            _editorState = EditorState.Loaded;
            HideEditorNotice();
            ApplyEditorState();

            // Focus after the state is applied: Loading disables the box, and
            // a disabled TextBox cannot take focus.
            _ = NoteEditor.Focus(FocusState.Programmatic);
            NoteEditor.Select(0, 0);
        }
        catch (StorageException ex)
        {
            _editorState = EditorState.Error;
            ApplyEditorState();
            ShowEditorNotice($"Could not read the note: {ex.Message}");
        }
    }

    /// <summary>
    /// Leaves the editor, saving first when the buffer is dirty.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Save-on-leave. A dirty buffer is written through
    /// <c>UpdateNoteContent</c> before the surface is swapped; a failed write
    /// keeps the user in the editor with their text intact, because losing
    /// what someone typed is the one outcome this must never produce.
    /// </para>
    /// <para>
    /// This is deliberately NOT parity row B19, which requires continuous
    /// persistence. B19 stays open and is M3 work — see the note on that row.
    /// </para>
    /// </remarks>
    /// <param name="discard">
    /// Skip the save. Used when the note is already gone, where writing would
    /// only produce a second failure for the same cause.
    /// </param>
    private void LeaveEditor(bool discard = false)
    {
        string? noteId = _openNoteId;

        if (!discard && EditorIsDirty && noteId is not null && !TrySaveOpenNote(noteId))
        {
            // Stay put. The buffer is untouched and the notice explains why.
            return;
        }

        _openNoteId = null;
        _openNoteBaseline = string.Empty;
        NoteEditor.Text = string.Empty;
        HideEditorNotice();
        HideEditorGuidance();

        EditorRoot.Visibility = Visibility.Collapsed;
        NoteRoot.Visibility = Visibility.Visible;

        // The note list was left intact; only selection and focus need putting
        // back, and by id, because editing line 1 can move the row under O2.
        if (noteId is not null)
        {
            _selectedNoteId = noteId;
            RestoreNoteSelection();
            FocusNoteRow(noteId);
        }
    }

    /// <summary>
    /// Persists the buffer. Returns whether the editor may now be left.
    /// </summary>
    private bool TrySaveOpenNote(string noteId)
    {
        try
        {
            CommandResult result =
                _updateNote.Handle(new UpdateNoteContent(NoteId.From(noteId), NoteEditor.Text));

            if (result.IsSuccess)
            {
                _openNoteBaseline = NoteEditor.Text;
                return true;
            }

            // NotFound or InvalidState: the note was removed or binned while
            // it was open, so saving cannot succeed however often it is tried.
            ShowEditorNotice(DescribeNote(result.Failure!));
            return false;
        }
        catch (StorageException ex)
        {
            ShowEditorNotice($"Could not save the note: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Creates a note in the open folder and opens it (parity B1).
    /// </summary>
    /// <remarks>
    /// Created empty, and placed wherever O3 puts it. B5's placement options
    /// are a setting that does not exist yet (#9), so nothing here chooses.
    /// </remarks>
    private void CreateNoteInOpenFolder()
    {
        if (_noteState != NoteState.Loaded || _openFolderId is not { } folderId)
        {
            return;
        }

        try
        {
            CommandResult<NoteId> result =
                _createNote.Handle(new CreateNote(folderId, string.Empty));

            if (!result.IsSuccess)
            {
                ShowNoteNotice(Describe(result.Failure!));
                return;
            }

            string created = result.Value.Value;

            _selectedNoteId = created;
            RefreshNotes();
            OpenNote(created);
        }
        catch (StorageException ex)
        {
            ShowNoteNotice($"Could not create the note: {ex.Message}");
        }
    }

    /// <summary>
    /// Soft-deletes the open note (parity B7) and returns to the list.
    /// </summary>
    /// <remarks>
    /// No confirmation dialog. B7 marks Noto BETTER than SideNotes precisely
    /// because the delete is recoverable — a soft delete into the recycle bin
    /// — so a blocking prompt would cost the user time and buy nothing.
    /// </remarks>
    private void DeleteOpenNote()
    {
        if (_editorState != EditorState.Loaded || _openNoteId is not { } id)
        {
            return;
        }

        try
        {
            CommandResult result = _deleteNote.Handle(new DeleteNote(NoteId.From(id)));

            if (!result.IsSuccess)
            {
                // Navigation is untouched: the user stays in an editor whose
                // note still exists.
                ShowEditorNotice(DescribeNote(result.Failure!));
                return;
            }

            // The note is gone, so there is nothing to save and no row to
            // restore. discard skips the save that would otherwise be
            // attempted against a deleted note.
            _selectedNoteId = null;
            LeaveEditor(discard: true);
            RefreshNotes();
        }
        catch (StorageException ex)
        {
            ShowEditorNotice($"Could not delete the note: {ex.Message}");
        }
    }

    private void ApplyEditorState()
    {
        bool interactive = _editorState == EditorState.Loaded;

        NoteEditor.IsEnabled = interactive;

        EditorRetryButton.Visibility = _editorState == EditorState.Error
            ? Visibility.Visible
            : Visibility.Collapsed;
        EditorRetryButton.IsEnabled = _editorState == EditorState.Error;

        switch (_editorState)
        {
            case EditorState.Loading:
                ShowEditorGuidance("Loading…");
                break;

            case EditorState.Error:
                ShowEditorGuidance("The note could not be loaded.");
                break;

            default:
                HideEditorGuidance();
                break;
        }
    }

    private void OnEditorBackClick(object sender, RoutedEventArgs e) => LeaveEditor();

    private void OnEditorRetryClick(object sender, RoutedEventArgs e)
    {
        _editorState = EditorState.Loading;
        ApplyEditorState();

        _ = DispatcherQueue.TryEnqueue(LoadOpenNote);
    }

    /// <summary>
    /// Keeps the Back label showing the note's current title.
    /// </summary>
    /// <remarks>
    /// The title is the first non-empty line (parity B16), so it changes as
    /// the user edits line 1. It is recomputed through the domain rather than
    /// reimplemented here, so there stays one definition of what a title is.
    /// </remarks>
    private void OnNoteEditorTextChanged(object sender, TextChangedEventArgs e) =>
        UpdateEditorBackLabel();

    private void UpdateEditorBackLabel()
    {
        string title = NoteTitle.From(NoteEditor.Text);

        EditorBackButton.Content = string.IsNullOrEmpty(title)
            ? $"‹ {NoteListItem.UntitledLabel}"
            : $"‹ {title}";
    }

    /// <summary>
    /// The editor's keyboard contract.
    /// </summary>
    /// <remarks>
    /// Escape leaves, saving when dirty; Alt+Ctrl+Backspace deletes (B7).
    /// Ctrl+N is deliberately NOT handled here: nothing documents what it does
    /// with a note open, and every plausible answer is multi-document
    /// behaviour this surface does not have.
    /// </remarks>
    private void OnEditorRootKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            LeaveEditor();
            return;
        }

        if (e.Key == VirtualKey.Back && IsControlDown() && IsAltDown())
        {
            e.Handled = true;
            DeleteOpenNote();
        }
    }

    private static bool IsControlDown() =>
        InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(CoreVirtualKeyStates.Down);

    private static bool IsAltDown() =>
        InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu)
            .HasFlag(CoreVirtualKeyStates.Down);

    private void ShowEditorNotice(string message)
    {
        EditorNoticeText.Text = message;
        EditorNoticeText.Visibility = Visibility.Visible;
    }

    private void HideEditorNotice()
    {
        EditorNoticeText.Text = string.Empty;
        EditorNoticeText.Visibility = Visibility.Collapsed;
    }

    private void ShowEditorGuidance(string message)
    {
        EditorGuidanceText.Text = message;
        EditorGuidanceText.Visibility = Visibility.Visible;
    }

    private void HideEditorGuidance()
    {
        EditorGuidanceText.Text = string.Empty;
        EditorGuidanceText.Visibility = Visibility.Collapsed;
    }

    // ------------------------------------------------------- selection/keys

    private void OnFolderSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedId = (FolderList.SelectedItem as FolderListItem)?.Id;

        // A selection change while renaming means the user moved on; the
        // editor is editing a row that is no longer the subject.
        if (_renamingId is not null && _renamingId != _selectedId)
        {
            CancelRename();
        }
    }

    /// <summary>
    /// The list's keyboard contract.
    /// </summary>
    /// <remarks>
    /// Enter is deliberately NOT handled. Activating a row means entering the
    /// folder (parity C4), which needs a note list to enter; binding Enter to
    /// "select" now would have to be un-bound when C4 lands.
    /// </remarks>
    private void OnFolderListKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.F2)
        {
            e.Handled = true;
            BeginRename();
            return;
        }

        // Ctrl+Down is NOT handled here: a ListView consumes arrow keys for
        // its own navigation before KeyDown bubbles to this handler, so the
        // chord never arrives. It is a KeyboardAccelerator instead, which is
        // evaluated ahead of control navigation — the mechanism Ctrl+N
        // already uses. Verified: plain Down does not reach this handler.
    }

    /// <summary>
    /// Ctrl+Down — enter the selected folder (parity C4).
    /// </summary>
    /// <remarks>
    /// <b>PreviewKeyDown, and neither KeyDown nor a KeyboardAccelerator.</b>
    /// A ListView claims the arrow keys for its own navigation and marks them
    /// handled, which happens before KeyDown bubbles to a parent handler and
    /// before accelerators are evaluated — both were tried and neither fired,
    /// while Ctrl+N on the same accelerator collection fires normally, so the
    /// key is what differs rather than the wiring. Preview runs on the way
    /// down, ahead of the control.
    /// </remarks>
    private void OnFolderListPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Down)
        {
            return;
        }

        CoreVirtualKeyStates control = InputKeyboardSource
            .GetKeyStateForCurrentThread(VirtualKey.Control);

        if (control.HasFlag(CoreVirtualKeyStates.Down))
        {
            e.Handled = true;
            EnterSelectedFolder();
        }
    }

    /// <summary>
    /// Double-click enters a folder (parity C4).
    /// </summary>
    /// <remarks>
    /// Double-click and not single-click: C4 documents single-click opening
    /// as an optional setting, and no settings system exists (#9).
    /// </remarks>
    private void OnFolderListDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        e.Handled = true;
        EnterSelectedFolder();
    }

    /// <summary>
    /// Esc leaves the note surface (parity C5, A12 mode 1).
    /// </summary>
    /// <remarks>
    /// Handled on the note surface root so it works wherever focus sits
    /// inside it — the list, Back, or Retry.
    /// </remarks>
    private void OnNoteRootKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            LeaveFolder();
        }
    }

    /// <summary>
    /// Ctrl+N — create, scoped to this pane (parity C2).
    /// </summary>
    /// <remarks>
    /// An accelerator on the pane's root, not a global hotkey: the global
    /// activation surface is a separate M2 concern, and principle 7 requires
    /// each one to be independently disableable.
    /// </remarks>
    private void OnCreateAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;

        if (_state == PaneState.Loaded)
        {
            FocusInput();
        }
    }

    // ------------------------------------------------------------ presentation

    /// <summary>
    /// Turns a <see cref="CommandFailure"/> into something a user can read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><c>DuplicateName</c> is deliberately absent.</b> Folder names are not
    /// unique (Case G): neither <c>CreateFolder</c> nor <c>RenameFolder</c>
    /// checks for collisions, and contract §11 rows 12 and 13 do not list the
    /// reason. Mapping it would advertise an error state that cannot occur and
    /// would describe a supported outcome as a failure.
    /// </para>
    /// <para>
    /// The failure's own <c>Message</c> is not shown verbatim. It is written
    /// for diagnostics and names ids; that is safe but not useful to a user.
    /// </para>
    /// </remarks>
    private static string Describe(CommandFailure failure) => failure.Reason switch
    {
        CommandFailureReason.InvalidInput => "A folder name cannot be empty.",
        CommandFailureReason.NotFound => "That folder no longer exists.",
        CommandFailureReason.InvalidState => "That folder is in the recycle bin.",
        _ => "The folder could not be saved.",
    };

    /// <summary>
    /// Turns a note <see cref="CommandFailure"/> into something a user can read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A sibling of <see cref="Describe"/> rather than a parameterised or
    /// generic version of it. The two differ only in a noun, but the surfaces
    /// they serve fail for different reasons, and a shared mapper that takes an
    /// entity name reads as infrastructure for a problem that is four strings
    /// wide.
    /// </para>
    /// <para>
    /// <b>Not used by note <i>creation</i>.</b> <c>CreateNote</c> fails when
    /// the FOLDER is missing or binned — "no folder", "folder cannot receive a
    /// note" — so that path keeps <see cref="Describe"/> and its folder
    /// wording, which is what the failure is actually about.
    /// </para>
    /// <para>
    /// <c>InvalidInput</c> is unreachable for the note commands M2-3 wires:
    /// <c>UpdateNoteContent</c> rejects only a null content, which the editor
    /// cannot produce, and empty content is legal. It is mapped anyway so the
    /// switch stays total rather than falling to a message about saving.
    /// </para>
    /// </remarks>
    private static string DescribeNote(CommandFailure failure) => failure.Reason switch
    {
        CommandFailureReason.InvalidInput => "That note could not be read.",
        CommandFailureReason.NotFound => "That note no longer exists.",
        CommandFailureReason.InvalidState => "That note is in the recycle bin.",
        _ => "The note could not be saved.",
    };

    /// <summary>
    /// Reports a failure. Carries no automation name of its own, so the text
    /// IS the accessible name and the live region announces it.
    /// </summary>
    private void ShowNotice(string message)
    {
        NoticeText.Text = message;
        NoticeText.Visibility = Visibility.Visible;
    }

    private void HideNotice()
    {
        NoticeText.Text = string.Empty;
        NoticeText.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Describes the list itself — loading, empty, unreadable.
    /// </summary>
    /// <remarks>
    /// A separate line from the notice because both can be true at once: an
    /// empty list and a failed create are two facts the user needs, and
    /// sharing one line meant the second destroyed the first.
    /// </remarks>
    private void ShowGuidance(string message)
    {
        GuidanceText.Text = message;
        GuidanceText.Visibility = Visibility.Visible;
    }

    private void HideGuidance()
    {
        GuidanceText.Text = string.Empty;
        GuidanceText.Visibility = Visibility.Collapsed;
    }

    private void FocusInput()
    {
        _ = FolderNameInput.Focus(FocusState.Programmatic);
        FolderNameInput.SelectAll();
    }

    private void MoveFocusToList()
    {
        // The list when there is one, the name box when there is not — only
        // the target differs, so it is chosen rather than branched around.
        Control destination = Folders.Count > 0 ? FolderList : FolderNameInput;

        _ = destination.Focus(FocusState.Programmatic);
    }

    /// <summary>
    /// Puts keyboard focus on a row, so focus always has a destination.
    /// </summary>
    /// <remarks>
    /// <b>Deferred to the dispatcher on purpose.</b> A row created by the
    /// re-query has no container yet at the moment the command returns — the
    /// ListView realises it during the next layout pass — so calling this
    /// synchronously finds no container and focus silently stays where it was.
    /// Observed with UI Automation: after creating a folder, focus remained on
    /// the name box instead of the new row.
    /// </remarks>
    private void FocusRow(string id)
    {
        FolderListItem? item = Folders.FirstOrDefault(f => f.Id == id);

        if (item is null)
        {
            // The row is gone: fall back to the nearest usable control rather
            // than leaving focus nowhere.
            MoveFocusToList();
            return;
        }

        FolderList.SelectedItem = item;

        // Scroll first: a virtualised ListView only realises a container for a
        // row that is in view, and an unrealised row cannot take focus.
        FolderList.ScrollIntoView(item);

        // Low priority so this runs after the layout pass that realises the
        // container, rather than in the same tick as the command that created
        // it.
        _ = FolderList.DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () =>
            {
                if (FolderList.ContainerFromItem(item) is ListViewItem container)
                {
                    _ = container.Focus(FocusState.Programmatic);
                }
                else
                {
                    // Still unrealised: the list itself is the nearest
                    // deterministic destination, so focus is never lost.
                    _ = FolderList.Focus(FocusState.Programmatic);
                }
            });
    }
}
