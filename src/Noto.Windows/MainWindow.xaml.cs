using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Noto.Core.Commands;
using Noto.Core.Folders;
using Noto.Core.Notes;
using Noto.Core.Ordering;
using Noto.Core.Storage;
using Noto.Core.Workspace;
using Noto.Platform.Windows;
using Noto.UseCases.Commands;
using Noto.UseCases.Folders;
using Noto.UseCases.Notes;
using Noto.UseCases.Workspace;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

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
    private readonly NoteStackSession _stack;
    private readonly CommandDispatcher _commands;

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

    /// <summary>The selected card, by id. Never an index.</summary>
    private string? _selectedNoteId;

    private NoteState _noteState = NoteState.Loading;

    /// <summary>The editing card's editor while it is realised; the text itself is the stack's.</summary>
    private NoteEditorHost? _editor;

    /// <summary>Where the caret goes once the editing card's editor is realised; <see langword="null"/> when placed.</summary>
    private CaretPlacement? _pendingCaret;

    /// <summary>
    /// The note and the exact text whose save failed when the window was last
    /// asked to close, or <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// The text itself rather than a flag: it cannot be left stale. It applies
    /// only while that note is being edited with exactly that text — an edit,
    /// another card or leaving editing makes it inert without anything having
    /// to clear it.
    /// </remarks>
    private (string NoteId, string Text)? _failedCloseSave;

    /// <summary>Writes unsaved text while the user types (parity B19, P2.1): armed by each change, see <see cref="ArmAutosave"/>.</summary>
    private readonly DispatcherQueueTimer _autosave;

    /// <summary>When the editing card's text last became unsaved (a <see cref="Stopwatch"/> timestamp); 0 when it is saved.</summary>
    private long _unsavedSince;

    /// <summary>When one click last entered a folder (an <see cref="Environment.TickCount64"/>), see <see cref="OnNoteListTapped"/>.</summary>
    private long _enteredByClickAt;

    /// <summary>Windows' own settings, read live: the double-click time.</summary>
    private readonly UISettings _system = new();

    public MainWindow(
        ListFoldersQuery listFolders,
        NoteStackSession stack,
        CommandDispatcher commands)
    {
        // Queries are read directly; every mutation is dispatched (ADR-010, #21). The note stack does both for notes.
        _listFolders = listFolders ?? throw new ArgumentNullException(nameof(listFolders));
        _stack = stack ?? throw new ArgumentNullException(nameof(stack));
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));

        InitializeComponent();

        // Each row's states for assistive technology go on its container (OnRowContainerContentChanging).
        FolderList.ContainerContentChanging += OnRowContainerContentChanging;
        NoteList.ContainerContentChanging += OnRowContainerContentChanging;

        AppWindow.Closing += OnAppWindowClosing;

        _autosave = DispatcherQueue.CreateTimer();
        _autosave.IsRepeating = false;
        _autosave.Tick += OnAutosaveTick;

        // Slice 1.3: the content's motion; the coordinator runs the drawer's, the navigation below a surface's.
        Motion = new ContentMotion(WorkspaceContent);

        // Ctrl+, opens Settings from every surface (Slice 1), wherever focus is. On the root, as Ctrl+W is. Added
        // here because XAML cannot name the comma key (VK_OEM_COMMA has no VirtualKey member).
        var openSettings = new KeyboardAccelerator { Key = (VirtualKey)0xBC, Modifiers = VirtualKeyModifiers.Control };
        openSettings.Invoked += OnSettingsAccelerator;
        WorkspaceRoot.KeyboardAccelerators.Add(openSettings);

        // The shortcut recorder never listens while Noto is not in front: losing activation cancels it (Slice 1).
        Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated)
            {
                SettingsSurface?.CancelRecording();
            }
        };

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
    /// The note stack's state.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="PaneState"/> because the two surfaces are
    /// never active at once and share no transitions. Editing a card is the
    /// stack's own state (<see cref="NoteStackSession.Editing"/>), not a member here.
    /// </remarks>
    private enum NoteState
    {
        Loading,
        Loaded,
        Error,
    }

    public ObservableCollection<FolderListItem> Folders { get; } = [];

    /// <summary>The open folder's cards, in O2 order.</summary>
    public ObservableCollection<NoteCard> Notes { get; } = [];

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
        _ = DispatcherQueue.TryEnqueueGuarded(Refresh);
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
                _commands.Dispatch(new CreateFolder(FolderNameInput.Text));

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
    /// entering a folder (<c>OnFolderListDoubleTapped</c>).
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
            CommandResult result = _commands.Dispatch(new RenameFolder(FolderId.From(id), name));

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

    // ----------------------------------------------------------- note stack

    /// <summary>
    /// Enters a folder and shows its note stack (parity C1, C4).
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
        _stack.Open(_openFolderId.Value);

        // A folder is entered fresh: no card is selected or edited.
        _selectedNoteId = null;

        BackButton.Content = $"‹ {item.Name}";

        PaneRoot.Visibility = Visibility.Collapsed;
        NoteRoot.Visibility = Visibility.Visible;
        Motion.EnterSurface(NoteRoot, forward: true);

        EnterNoteLoading();

        // Queued for the same reason as the folder list's first load: the
        // Loading state is applied and handed back to the framework before
        // the read begins.
        _ = DispatcherQueue.TryEnqueueGuarded(RefreshNotes);

        // Collapsing the folder list took keyboard focus with it, and the
        // framework handed it to the first focusable element left, a header
        // button, where Escape does nothing (measured with UI Automation).
        // Focus is moved onto this surface instead, as LeaveFolder moves it
        // back: low priority, after the read and the layout pass that realises
        // the rows, for the reason FocusRow gives.
        _ = DispatcherQueue.TryEnqueueGuarded(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, MoveFocusToNotes);
    }

    /// <summary>
    /// Returns to the folder list (parity C5, A12 mode 1), saving the card being edited first. A failed save keeps
    /// the stack, the card and its text, and says why.
    /// </summary>
    private void LeaveFolder()
    {
        if (!TryEndEdit())
        {
            return;
        }

        string? folderId = _openFolderId?.Value;

        _openFolderId = null;
        _selectedNoteId = null;
        _stack.Close();
        Notes.Clear();
        HideNoteNotice();
        HideNoteGuidance();

        NoteRoot.Visibility = Visibility.Collapsed;
        PaneRoot.Visibility = Visibility.Visible;
        Motion.EnterSurface(PaneRoot, forward: false);

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
    /// The stack's read path — executes Q2 for the open folder and rebuilds the cards.
    /// </summary>
    /// <remarks>
    /// Rebuilt wholesale rather than diffed: the query already returns O2 order (pinned, SortOrder, Id), so the
    /// stack agrees with the database by construction. The card being edited is rebuilt editing, and its unsaved
    /// text is the stack's, not its editor's: a rebuilt editor loads it back.
    /// </remarks>
    private void RefreshNotes()
    {
        if (_openFolderId is null)
        {
            return;
        }

        try
        {
            ShowCards(_stack.Read());
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

    /// <summary>Replaces the cards with <paramref name="notes"/>, the editing one marked.</summary>
    private void ShowCards(IReadOnlyList<Note> notes)
    {
        Notes.Clear();
        foreach (Note note in notes)
        {
            Notes.Add(new NoteCard(note, note.Id == _stack.Editing));
        }
    }

    /// <summary>
    /// Re-applies the selected note id to the rebuilt stack.
    /// </summary>
    private void RestoreNoteSelection()
    {
        if (_selectedNoteId is null)
        {
            NoteList.SelectedIndex = -1;
            return;
        }

        NoteCard? match = Notes.FirstOrDefault(n => n.Id == _selectedNoteId);

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
        NewNoteButton.IsEnabled = interactive;

        NoteRetryButton.Visibility = _noteState == NoteState.Error
            ? Visibility.Visible
            : Visibility.Collapsed;
        NoteRetryButton.IsEnabled = _noteState == NoteState.Error;

        NoteCountText.Text = interactive ? NoteCount(Notes.Count) : string.Empty;

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
                    ShowNoteGuidance("This folder has no notes yet. Press Ctrl+N or select New note to write one.");
                }
                else
                {
                    HideNoteGuidance();
                }

                break;
        }
    }

    /// <summary>The list bar's count (C19).</summary>
    private static string NoteCount(int count) => count == 1 ? "1 note" : $"{count.ToString(CultureInfo.CurrentCulture)} notes";

    private void OnBackClick(object sender, RoutedEventArgs e) => LeaveFolder();

    /// <summary>
    /// Recovery from a failed note read. Error disables the list, so without
    /// this the surface would be a dead end.
    /// </summary>
    private void OnNoteRetryClick(object sender, RoutedEventArgs e)
    {
        EnterNoteLoading();
        _ = DispatcherQueue.TryEnqueueGuarded(RefreshNotes);
    }

    /// <summary>
    /// Ctrl+N — a new note at the configured placement, edited (parity B1, B5).
    /// </summary>
    /// <remarks>
    /// An accelerator on the stack's root, so it works wherever focus is in it, the editor included, and in an empty
    /// folder, which has no card to take focus (#69).
    /// </remarks>
    private void OnCreateNoteAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        CreateNoteInOpenFolder();
    }

    private void OnNewNoteClick(object sender, RoutedEventArgs e) => CreateNoteInOpenFolder();

    /// <summary>
    /// Enter and F2 edit the focused card (ADR-016 §5).
    /// </summary>
    /// <remarks>
    /// PreviewKeyDown, because a ListView consumes Enter as item activation before a KeyDown handler sees it. Only
    /// from a card itself: keys typed in the editor tunnel through here too, and Enter there is a new line.
    /// </remarks>
    private void OnNoteListPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Ctrl+Alt+Shift+Up/Down move the current card within its pin group (G17, G18). First, so that neither
        // Ctrl+Alt+Up/Down (edit the previous or next card, in the editor) nor the list's arrows take the chord.
        if (e.Key is VirtualKey.Up or VirtualKey.Down && IsControlDown() && IsAltDown() && IsShiftDown())
        {
            e.Handled = true;

            if (CurrentCard() is { } current)
            {
                ReorderCard(current, e.Key == VirtualKey.Up ? RepositionDirection.Up : RepositionDirection.Down);
            }

            return;
        }

        // Ctrl+Alt+Left folds, Ctrl+Alt+Right unfolds (G19, G20); with Shift, every card (G21, G22). Here, on the way
        // down, so a card and its editor behave the same and the TextBox never takes the arrows.
        if (e.Key is VirtualKey.Left or VirtualKey.Right && IsControlDown() && IsAltDown())
        {
            e.Handled = true;
            bool fold = e.Key == VirtualKey.Left;

            if (IsShiftDown())
            {
                FoldAll(fold);
            }
            else if (CurrentCard() is { } id)
            {
                FoldCard(id, fold);
            }

            return;
        }

        if (e.Key is VirtualKey.Enter or VirtualKey.F2 && e.OriginalSource is ListViewItem { Content: NoteCard card })
        {
            e.Handled = true;
            EditCard(card.Id, CaretPlacement.End);
        }
    }

    /// <summary>
    /// A click on a card edits it (ADR-016 §6): a note is edited in place, not opened, so single-click opening (J7)
    /// concerns folders only. A click inside the editor is the editor's.
    /// </summary>
    private void OnNoteListTapped(object sender, TappedRoutedEventArgs e)
    {
        // The second click of a double click that entered this folder (J7): the folder row it was aimed at is gone, so
        // the framework raises it as a tap on the stack, on whatever card is under the pointer by then (measured).
        if (Environment.TickCount64 - _enteredByClickAt <= _system.DoubleClickTime)
        {
            e.Handled = true;
            return;
        }

        if ((e.OriginalSource as FrameworkElement)?.DataContext is NoteCard { IsEditing: false } card)
        {
            e.Handled = true;
            EditCard(card.Id, CaretPlacement.End);
        }
    }

    private void OnNoteSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        _selectedNoteId = (NoteList.SelectedItem as NoteCard)?.Id;

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
    /// Puts keyboard focus on the note surface: the list when it has notes to
    /// focus, its Back button when it has none, so Escape and the keyboard
    /// keep working on it.
    /// </summary>
    private void MoveFocusToNotes()
    {
        Control destination = _noteState == NoteState.Loaded && Notes.Count > 0 ? NoteList : BackButton;

        _ = destination.Focus(FocusState.Programmatic);
    }

    /// <summary>
    /// Puts keyboard focus on a card, so focus always has a destination.
    /// </summary>
    /// <remarks>
    /// The same shape as <see cref="FocusRow"/>, and deferred for the same
    /// reason: a virtualised ListView realises a container only for a row that
    /// is in view, and an unrealised row cannot take focus.
    /// </remarks>
    private void FocusNoteRow(string id)
    {
        NoteCard? item = Notes.FirstOrDefault(n => n.Id == id);

        if (item is null)
        {
            MoveFocusToNotes();
            return;
        }

        NoteList.SelectedItem = item;
        NoteList.ScrollIntoView(item);

        _ = NoteList.DispatcherQueue.TryEnqueueGuarded(
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

    // ------------------------------------------------------- editing a card

    /// <summary>
    /// Edits a card, the caret at <paramref name="caret"/> (ADR-016 §2). The card being edited is left first, saving;
    /// a failed save keeps it editing, with its text and a notice, and nothing else changes.
    /// </summary>
    private void EditCard(string id, CaretPlacement caret)
    {
        if (_noteState != NoteState.Loaded)
        {
            return;
        }

        if (_stack.Editing?.Value == id)
        {
            StartEditor(id, caret);
            return;
        }

        if (!TryEndEdit())
        {
            return;
        }

        CommandResult result = _stack.BeginEdit(NoteId.From(id));

        if (!result.IsSuccess)
        {
            ShowNoteNotice(DescribeNote(result.Failure!));
            return;
        }

        HideNoteNotice();
        SetEditing(id, true);
        StartEditor(id, caret);
    }

    /// <summary>
    /// Leaves editing, saving first (ADR-016 §2). Returns whether no card is being edited now: a failed save keeps the
    /// card editing with its text, and the notice says why.
    /// </summary>
    private bool TryEndEdit()
    {
        if (_stack.Editing is not { } editing)
        {
            return true;
        }

        if (!TrySaveCard())
        {
            return false;
        }

        // Saved: the stored text is the baseline. The card keeps its container and focus, and shows it.
        string stored = _stack.Baseline;
        _ = _stack.EndEdit();

        if (Notes.FirstOrDefault(n => n.Id == editing.Value) is { } card)
        {
            card.Content = stored;
        }

        SetEditing(editing.Value, false);
        return true;
    }

    /// <summary>
    /// Stores the editing card's unsaved text; it keeps editing. Returns whether it may now be left.
    /// </summary>
    /// <param name="noticeSuffix">Appended to the failure notice, if any.</param>
    private bool TrySaveCard(string noticeSuffix = "")
    {
        _editor?.ReadBack();

        if (!_stack.IsDirty)
        {
            StopAutosave();
            return true;
        }

        long started = Stopwatch.GetTimestamp();

        // This attempt is the write any pending autosave was waiting for. If it fails, the bound starts again from
        // now: a note that cannot be saved is retried at the normal pace, not on every key, and no timer replaces
        // this failure's notice (a failed close's "close again" offer included).
        _autosave.Stop();
        _unsavedSince = started;

        try
        {
            CommandResult result = _stack.Save();

            if (result.IsSuccess)
            {
                StopAutosave();

                // #11: the save is written (the handler returns after the database commits). The performance
                // harness times saves from this line; validation channel only, never the log, never content. The
                // number is invariant: a regional decimal comma would be misread as a shorter save.
                Trace.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Noto.Save completed ms={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F2}"));

                // A successful save answers whatever the last failure said.
                HideNoteNotice();
                return true;
            }

            // NotFound or InvalidState: the note was removed or binned while
            // it was being edited, so saving cannot succeed however often it is tried.
            ShowNoteNotice(DescribeNote(result.Failure!) + noticeSuffix);
            return false;
        }
        catch (StorageException ex)
        {
            ShowNoteNotice($"Could not save the note: {ex.Message}" + noticeSuffix);
            return false;
        }
    }

    /// <summary>Marks <paramref name="id"/>'s card editing or not, in place: x:Load adds or removes its editor and bar.</summary>
    private void SetEditing(string id, bool editing)
    {
        if (Notes.FirstOrDefault(n => n.Id == id) is { } card)
        {
            card.IsEditing = editing;
        }
    }

    /// <summary>Shows the editor on <paramref name="id"/>'s card, scrolled into view, and focuses it.</summary>
    private void StartEditor(string id, CaretPlacement caret)
    {
        if (Notes.FirstOrDefault(n => n.Id == id) is not { } card)
        {
            return;
        }

        _pendingCaret = caret;
        NoteList.SelectedItem = card;
        NoteList.ScrollIntoView(card);

        // Already loaded (a card edited again before its editor went away): place the caret now. Otherwise the
        // editor's Loaded does it, once x:Load has realised it.
        if (_editor is { } host && host.Note?.Value == id)
        {
            host.PlaceCaret(caret);
            _pendingCaret = null;
        }
    }

    /// <summary>The editing card's editor was realised (x:Load).</summary>
    private void OnEditorHostLoaded(object sender, RoutedEventArgs e) => AttachEditor(sender as NoteEditorHost);

    /// <summary>
    /// A recycled container can be realised with its previous card's data and given the editing card's only after it
    /// has loaded, and no second Loaded follows (measured: Ctrl+Alt+Down onto a long card far down the stack left an
    /// empty editor). So a loaded editor also attaches when its card arrives.
    /// </summary>
    private void OnEditorHostDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (sender is NoteEditorHost { IsLoaded: true } host)
        {
            AttachEditor(host);
        }
    }

    /// <summary>
    /// Loads the stack's text for the editing card into its editor, once: the text survives the card's container
    /// being recycled. Takes focus if editing was just asked for.
    /// </summary>
    private void AttachEditor(NoteEditorHost? host)
    {
        // Not this card's editor, or it already shows this card.
        if (host is null || host.DataContext is not NoteCard card || _stack.Editing is not { } editing || editing.Value != card.Id
            || (host.Note is { } shown && shown.Value == card.Id))
        {
            return;
        }

        _editor = host;
        host.Load(NoteId.From(card.Id), _stack.Draft);
        host.ContentChanged -= OnEditorContentChanged;
        host.LeaveRequested -= OnEditorLeaveRequested;
        host.ContentChanged += OnEditorContentChanged;
        host.LeaveRequested += OnEditorLeaveRequested;

        if (_pendingCaret is { } caret)
        {
            _pendingCaret = null;
            host.PlaceCaret(caret);
        }
    }

    private void OnEditorHostUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is NoteEditorHost host)
        {
            host.ContentChanged -= OnEditorContentChanged;
            host.LeaveRequested -= OnEditorLeaveRequested;

            if (_editor == host)
            {
                _editor = null;
            }
        }
    }

    // ponytail: the whole source is copied into the stack on every change, so text survives a recycled container;
    // P2's editor model replaces the copy with its own document.
    private void OnEditorContentChanged(object? sender, EventArgs e)
    {
        if (sender is NoteEditorHost { Note: { } note } host)
        {
            _stack.UpdateDraft(note, host.Markdown);
            ArmAutosave();
        }
    }

    /// <summary>
    /// Schedules the write of unsaved text (parity B19, P2.1): after <see cref="Autosave.Quiet"/> without a change, and
    /// by <see cref="Autosave.Longest"/> after the text first became unsaved however steadily the user types. Text
    /// typed back to what is stored needs no write.
    /// </summary>
    private void ArmAutosave()
    {
        if (!_stack.IsDirty)
        {
            StopAutosave();
            return;
        }

        if (_unsavedSince == 0)
        {
            _unsavedSince = Stopwatch.GetTimestamp();
        }

        _autosave.Stop();
        _autosave.Interval = Autosave.Delay(Stopwatch.GetElapsedTime(_unsavedSince));
        _autosave.Start();
    }

    /// <summary>
    /// The write the timer scheduled: the same save as leaving a card, so a failure keeps the text and the notice says
    /// why. The card keeps editing either way.
    /// </summary>
    private void OnAutosaveTick(DispatcherQueueTimer sender, object args) => _ = TrySaveCard();

    /// <summary>Nothing is unsaved: no write is due.</summary>
    private void StopAutosave()
    {
        _autosave.Stop();
        _unsavedSince = 0;
    }

    /// <summary>
    /// The card's Format menu: the same commands as the editor's keys, run on its selection, then the editor has focus
    /// again so typing goes on where it was.
    /// </summary>
    private void OnFormatClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: string name } && _editor is { } editor && Enum.TryParse(name, out EditorCommand command))
        {
            _ = editor.Run(command);
            editor.TakeFocus();
        }
    }

    /// <summary>Escape follows the stored behaviour; Ctrl+Alt+Up/Down edit the previous or next card (G11, G12).</summary>
    private void OnEditorLeaveRequested(object? sender, EditorLeave leave)
    {
        if (_stack.Editing is not { } editing)
        {
            return;
        }

        switch (leave)
        {
            case EditorLeave.Escape:
                _ = OnEscape(WorkspaceSurface.Editor);
                break;

            case EditorLeave.Next or EditorLeave.Previous:
                if (_stack.Neighbour(editing, leave == EditorLeave.Next ? 1 : -1) is { } next)
                {
                    EditCard(next.Value, leave == EditorLeave.Next ? CaretPlacement.Start : CaretPlacement.End);
                }

                break;
        }
    }

    /// <summary>Leaves editing for the focused card (Escape's "leave" step): it saves first and keeps the card.</summary>
    private void LeaveEditor()
    {
        string? editing = _stack.Editing?.Value;

        if (editing is not null && TryEndEdit())
        {
            FocusNoteRow(editing);
        }
    }

    /// <summary>
    /// Saves the card being edited before the window closes — Alt+F4, the taskbar's Close, the tray's Quit, or the
    /// session ending (ADR-014): every close arrives here.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The explicit save that leaving a card performs, at one more point: the window closing. Text typed since the last
    /// autosave (P2.1) is written here at once rather than waiting for the timer.
    /// </para>
    /// <para>
    /// The first close with unsaved text gets exactly one save attempt. If it fails, the close is cancelled, the text
    /// stays, and the notice says so. Closing again with the same text makes no further attempt: it discards and
    /// closes, the choice the notice offered, so a save that can never succeed — the note was removed — cannot trap
    /// the user. Text edited after the failure is a new unsaved state, and its close gets the normal single attempt.
    /// </para>
    /// <para>
    /// The session ending is handled before this (ADR-014): Windows' query saves through <see cref="SaveBeforeHide"/>,
    /// and the close that follows finds nothing unsaved. A kill or a crash (#10) loses only what autosave had not yet
    /// written: at most <see cref="Autosave.Longest"/> of typing.
    /// </para>
    /// </remarks>
    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        _editor?.ReadBack();

        if (!_stack.IsDirty || _stack.Editing is not { } editing)
        {
            return;
        }

        bool closingAgain = _failedCloseSave is { } failed
            && failed.NoteId == editing.Value
            && string.Equals(failed.Text, _stack.Draft, StringComparison.Ordinal);

        // The explicit discard: no second save attempt.
        if (closingAgain || TrySaveCard(CloseAgainToDiscard))
        {
            return;
        }

        _failedCloseSave = (editing.Value, _stack.Draft);
        args.Cancel = true;
    }

    /// <summary>
    /// Saves unsaved text before the workspace is hidden (#16 slice 5).
    /// </summary>
    /// <remarks>
    /// The same explicit save as leaving a card, run once per hide. A failure returns <see langword="false"/> so the
    /// window stays shown, with the text kept and the notice saying why — nothing is hidden that has not been kept.
    /// The card keeps editing: hiding is not leaving.
    /// </remarks>
    /// <returns>Whether the window may be hidden.</returns>
    internal bool SaveBeforeHide() => TrySaveCard();

    private const string CloseAgainToDiscard = " Close the window again to discard the changes and exit.";

    /// <summary>
    /// What Escape does (#16 slice 6; parity A12). Set by the composition root
    /// from the stored setting, at launch and whenever it changes; the default
    /// is SideNotes'.
    /// </summary>
    public EscapeBehavior Escape { get; set; } = EscapeBehavior.LeaveFolderOrHide;

    /// <summary>
    /// Whether one click opens a folder (parity J7, C4); a card is edited with one click either way (ADR-016 §6). Set by the
    /// composition root from the stored setting, at launch and whenever it
    /// changes. Pointer only: the keyboard paths are the same either way.
    /// </summary>
    public bool SingleClickOpen { get; set; }

    /// <summary>The workspace content's motion (Slice 1.3): the drawer's enter and exit, and a surface entering.</summary>
    internal ContentMotion Motion { get; }

    /// <summary>What the settings surface may change. Set once by the composition root; without it, Settings does not open.</summary>
    internal SettingsContext? Settings { get; set; }

    // The surface Settings was opened from, and what had focus there: where leaving Settings goes back to.
    private UIElement? _settingsReturn;
    private Control? _settingsReturnFocus;

    // Why the show/hide shortcut is not active, kept so a Settings opened later shows it too.
    private string? _hotkeyMessage;

    /// <summary>
    /// The settings surface while it is open; <see langword="null"/> otherwise. Created by <see cref="OpenSettings"/>,
    /// removed and cleared by <see cref="CloseSettings"/>, so at most one exists and none outlives being left.
    /// </summary>
    private SettingsPage? SettingsSurface { get; set; }

    /// <summary>
    /// Opens Settings from whichever surface is showing (Slice 1): the header button, <c>Ctrl+,</c>, or the tray.
    /// </summary>
    /// <remarks>
    /// The surface it replaces keeps its state (an open note keeps its text, unsaved or not, and hiding still saves
    /// it), and leaving Settings returns to it with focus where it was. Already open: focus moves into it.
    /// </remarks>
    internal void OpenSettings()
    {
        if (Settings is null)
        {
            return;
        }

        if (SettingsSurface is { } open)
        {
            open.FocusFirst();
            return;
        }

        UIElement from = RecycleBinSurface is { } bin ? bin : NoteRoot.Visibility == Visibility.Visible ? NoteRoot : PaneRoot;

        _settingsReturn = from;
        _settingsReturnFocus = Content?.XamlRoot is { } root ? FocusManager.GetFocusedElement(root) as Control : null;

        // Created now, beside the surfaces it replaces, on top of them; removed again in CloseSettings. Deferred
        // loading (x:Load) cannot do this in a Window (see MainWindow.xaml).
        var page = new SettingsPage();
        Grid.SetRow(page, Grid.GetRow(PaneRoot));
        page.KeyDown += OnSettingsKeyDown;
        page.BackRequested += OnSettingsBackRequested;
        ((Panel)PaneRoot.Parent).Children.Add(page);
        SettingsSurface = page;

        page.Show(Settings, _hotkeyMessage);
        from.Visibility = Visibility.Collapsed;
        page.Visibility = Visibility.Visible;
        Motion.EnterSurface(page, forward: true);

        // After layout: an element made visible this frame cannot take focus until it has been measured.
        _ = DispatcherQueue.TryEnqueueGuarded(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, page.FocusFirst);
        Trace.WriteLine("Noto.Settings opened");
    }

    /// <summary>Leaves Settings for the surface it was opened from, with focus back where it was.</summary>
    private void CloseSettings()
    {
        if (SettingsSurface is not { } page)
        {
            return;
        }

        // A recorder still listening gives the chord back before the page goes.
        page.CancelRecording();

        UIElement back = _settingsReturn ?? PaneRoot;
        back.Visibility = Visibility.Visible;
        page.Visibility = Visibility.Collapsed;
        Motion.EnterSurface(back, forward: false);

        // Released: nothing of the settings tree stays alive while it is not on screen, and nothing refers to it.
        ((Panel)page.Parent).Children.Remove(page);
        page.KeyDown -= OnSettingsKeyDown;
        page.BackRequested -= OnSettingsBackRequested;
        SettingsSurface = null;

        Control restore = _settingsReturnFocus is { IsLoaded: true, Visibility: Visibility.Visible } previous
            ? previous
            : back == NoteRoot ? NoteList
            : FolderList;

        _settingsReturn = null;
        _settingsReturnFocus = null;
        _ = DispatcherQueue.TryEnqueueGuarded(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => restore.Focus(FocusState.Programmatic));
        Trace.WriteLine("Noto.Settings closed");
    }

    /// <summary>
    /// Why the show/hide shortcut is not active, from the hotkey's owner; <see langword="null"/> when it is, or is off
    /// by choice. Shown in Settings now, if open, and whenever it is opened.
    /// </summary>
    internal void ShowHotkeyMessage(string? message)
    {
        _hotkeyMessage = message;
        SettingsSurface?.ShowHotkeyMessage(message);
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e) => OpenSettings();

    private void OnSettingsAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        OpenSettings();
    }

    private void OnSettingsBackRequested(object? sender, EventArgs e) => CloseSettings();

    /// <summary>Escape in Settings follows the stored behaviour: by default it goes back (Slice 1, ADR-007).</summary>
    private void OnSettingsKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = OnEscape(WorkspaceSurface.Settings);
        }
    }

    /// <summary>
    /// The user asked to put the workspace away — <c>Ctrl+W</c>, or Escape
    /// where its behaviour hides. The window never hides itself: the
    /// composition root hands this to the coordinator, which saves first.
    /// </summary>
    public event EventHandler? DismissRequested;

    /// <summary>The header's pin toggle changed (parity A9). Session-only: nothing is stored.</summary>
    public event EventHandler<bool>? PinChanged;

    /// <summary>
    /// <c>Ctrl+W</c> hides the workspace from any surface (parity A10, G7).
    /// </summary>
    private void OnDismissAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        DismissRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// One press of Escape on <paramref name="surface"/>, as the stored
    /// behaviour resolves it. An inline input has already cancelled itself
    /// and handled the key before this runs. Returns whether the press did
    /// something: "nothing" leaves the key unhandled.
    /// </summary>
    private bool OnEscape(WorkspaceSurface surface)
    {
        switch (WorkspaceEscape.Resolve(Escape, surface))
        {
            case EscapeAction.LeaveEditor:
                LeaveEditor();
                return true;

            case EscapeAction.LeaveFolder:
                LeaveFolder();
                return true;

            case EscapeAction.Hide:
                DismissRequested?.Invoke(this, EventArgs.Empty);
                return true;

            case EscapeAction.LeaveSettings:
                CloseSettings();
                return true;

            case EscapeAction.LeaveRecycleBin:
                CloseRecycleBin();
                return true;

            default:
                return false;
        }
    }

    /// <summary>Escape on the folder list — the top level.</summary>
    private void OnPaneRootKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = OnEscape(WorkspaceSurface.FolderList);
        }
    }

    /// <summary>
    /// Pin keeps the workspace open while another application has the
    /// keyboard (parity A9) — losing activation no longer hides it. For this
    /// session only; every launch starts unpinned.
    /// </summary>
    private void OnPinToggled(object sender, RoutedEventArgs e) =>
        PinChanged?.Invoke(this, PinToggle.IsChecked == true);

    /// <summary>
    /// Sets the pin from outside the header — the tray's Keep Noto open (#16
    /// slice 7). Through the toggle itself, so the change takes the one pin
    /// path (<see cref="PinChanged"/>) and the header always shows it. Never
    /// called at launch: every launch starts unpinned.
    /// </summary>
    internal void SetPinned(bool pinned) => PinToggle.IsChecked = pinned;

    /// <summary>The new-note placement setting (B5, J8), set by the composition root from the stored value.</summary>
    public NewNotePlacement NewNotePlacement { get; set; }

    /// <summary>
    /// Creates a note at the configured placement beside the selected card and edits it (parity B1, B5). The card
    /// being edited is saved first; a failed save creates nothing.
    /// </summary>
    private void CreateNoteInOpenFolder()
    {
        if (_noteState != NoteState.Loaded || _openFolderId is null || !TryEndEdit())
        {
            return;
        }

        NoteId? selected = _selectedNoteId is { } id ? NoteId.From(id) : null;

        try
        {
            CommandResult<NoteId> result = _stack.Create(NewNotePlacement, selected);

            if (!result.IsSuccess)
            {
                // CreateNote fails when the FOLDER is missing or binned, so the folder wording applies.
                ShowNoteNotice(Describe(result.Failure!));
                return;
            }

            string created = result.Value.Value;

            _selectedNoteId = created;
            ShowCards(_stack.Notes);
            ApplyNoteState();
            StartEditor(created, CaretPlacement.Start);
            Announce("Note created");
        }
        catch (StorageException ex)
        {
            ShowNoteNotice($"Could not create the note: {ex.Message}");
        }
    }

    /// <summary>
    /// Moves a card's note to the recycle bin (parity B7) and focuses the next card, else the previous.
    /// </summary>
    /// <remarks>
    /// No confirmation (ADR-016 §7): the delete is recoverable, a soft delete into the recycle bin, so a blocking
    /// prompt would cost time and buy nothing. A note deleted while being edited takes its unsaved text with it.
    /// </remarks>
    private void DeleteCard(string id)
    {
        if (_noteState != NoteState.Loaded)
        {
            return;
        }

        NoteId note = NoteId.From(id);
        NoteId? next = _stack.AfterRemoval(note);

        try
        {
            CommandResult result = _stack.Delete(note);

            if (!result.IsSuccess)
            {
                ShowNoteNotice(DescribeNote(result.Failure!));
                return;
            }

            _selectedNoteId = next?.Value;
            ShowCards(_stack.Notes);
            HideNoteNotice();
            ApplyNoteState();
            Announce("Note moved to the Recycle Bin");

            if (next is { } target)
            {
                FocusNoteRow(target.Value);
            }
            else
            {
                MoveFocusToNotes();
            }
        }
        catch (StorageException ex)
        {
            ShowNoteNotice($"Could not delete the note: {ex.Message}");
        }
    }

    /// <summary>The card the keyboard is on: the one being edited, else the focused or selected one.</summary>
    private string? CurrentCard() =>
        _stack.Editing?.Value
        ?? ((FocusManager.GetFocusedElement(Content.XamlRoot) as ListViewItem)?.Content as NoteCard)?.Id
        ?? _selectedNoteId;

    private void OnDeleteCardClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is NoteCard card)
        {
            DeleteCard(card.Id);
        }
    }

    /// <summary>
    /// A card's context menu (ADR-016 §6, §8): right-click, Shift+F10 or the Menu key. Every action in it is also a
    /// key, and every key's action is in it.
    /// </summary>
    private void OnNoteListContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (ItemOf<NoteCard>(args.OriginalSource) is not { } card
            || NoteList.ContainerFromItem(card) is not ListViewItem container)
        {
            return;
        }

        args.Handled = true;
        NoteList.SelectedItem = card;

        var menu = new MenuFlyout();
        menu.Items.Add(MenuItem("Edit", "Enter", () => EditCard(card.Id, CaretPlacement.End)));
        menu.Items.Add(MenuItem("New note", "Ctrl+N", CreateNoteInOpenFolder));

        var move = new MenuFlyoutSubItem { Text = "Move to" };
        foreach (MenuFlyoutItemBase item in MoveDestinations(card.Id))
        {
            move.Items.Add(item);
        }

        menu.Items.Add(move);
        menu.Items.Add(card.IsPinned
            ? MenuItem("Unpin", string.Empty, () => PinCard(card.Id, pinned: false))
            : MenuItem("Pin", string.Empty, () => PinCard(card.Id, pinned: true)));
        menu.Items.Add(card.IsFolded
            ? MenuItem("Unfold", "Ctrl+Alt+Right", () => FoldCard(card.Id, folded: false))
            : MenuItem("Fold", "Ctrl+Alt+Left", () => FoldCard(card.Id, folded: true)));
        menu.Items.Add(MenuItem("Move to top", string.Empty, () => ReorderCard(card.Id, RepositionDirection.Top), _stack.CanReorder(NoteId.From(card.Id), RepositionDirection.Top)));
        menu.Items.Add(MenuItem("Move to bottom", string.Empty, () => ReorderCard(card.Id, RepositionDirection.Bottom), _stack.CanReorder(NoteId.From(card.Id), RepositionDirection.Bottom)));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(MenuItem("Move to the Recycle Bin", "Ctrl+Alt+Backspace", () => DeleteCard(card.Id)));

        ShowMenu(menu, container, args);
    }

    /// <summary>The folders notes were most recently moved to (B11), set by the composition root.</summary>
    internal Func<IReadOnlyList<FolderId>>? RecentMoveFolders { get; set; }

    /// <summary>
    /// Ctrl+Shift+M (G16): the current card's move menu, at the card, from the card or its editor.
    /// </summary>
    private void OnMoveAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;

        if (_noteState != NoteState.Loaded
            || CurrentCard() is not { } id
            || Notes.FirstOrDefault(n => n.Id == id) is not { } card
            || NoteList.ContainerFromItem(card) is not ListViewItem container)
        {
            return;
        }

        var menu = new MenuFlyout();
        foreach (MenuFlyoutItemBase item in MoveDestinations(id))
        {
            menu.Items.Add(item);
        }

        menu.ShowAt(container);
    }

    /// <summary>
    /// Where a card can move (workspace-management.md §3): the recent folders first, then every other folder in
    /// folder-list order. Never the card's own folder: moving there is not a move.
    /// </summary>
    private List<MenuFlyoutItemBase> MoveDestinations(string id)
    {
        var items = new List<MenuFlyoutItemBase>();
        IReadOnlyList<Folder> folders;

        try
        {
            folders = _listFolders.Execute();
        }
        catch (StorageException ex)
        {
            ShowNoteNotice($"Could not read the folders: {ex.Message}");
            return items;
        }

        IReadOnlyList<Folder> recent = RecentFolders.Shown(RecentMoveFolders?.Invoke() ?? [], folders, _openFolderId);
        foreach (Folder folder in recent)
        {
            items.Add(MenuItem(folder.Name, string.Empty, () => MoveCard(id, folder)));
        }

        var all = new MenuFlyoutSubItem { Text = "All folders" };
        foreach (Folder folder in folders.Where(f => f.Id != _openFolderId))
        {
            all.Items.Add(MenuItem(folder.Name, string.Empty, () => MoveCard(id, folder)));
        }

        if (recent.Count > 0)
        {
            items.Add(new MenuFlyoutSeparator());
        }

        items.Add(all.Items.Count > 0 ? all : MenuItem("No other folders", string.Empty, static () => { }, enabled: false));
        return items;
    }

    /// <summary>
    /// Moves a card's note to <paramref name="target"/> (B11). The card being edited is saved first; a failed save
    /// moves nothing. The card leaves the stack and focus goes to the next card, else the previous.
    /// </summary>
    private void MoveCard(string id, Folder target)
    {
        if (_noteState != NoteState.Loaded || (_stack.Editing?.Value == id && !TryEndEdit()))
        {
            return;
        }

        NoteId note = NoteId.From(id);
        NoteId? next = _stack.AfterRemoval(note);

        try
        {
            CommandResult result = _stack.Move(note, target.Id);

            if (!result.IsSuccess)
            {
                ShowNoteNotice(DescribeMove(result.Failure!));
                return;
            }

            _selectedNoteId = next?.Value;
            ShowCards(_stack.Notes);
            HideNoteNotice();
            ApplyNoteState();
            Announce($"Moved to {target.Name}");

            if (next is { } focus)
            {
                FocusNoteRow(focus.Value);
            }
            else
            {
                MoveFocusToNotes();
            }
        }
        catch (StorageException ex)
        {
            ShowNoteNotice($"Could not move the note: {ex.Message}");
        }
    }

    /// <summary>A refused move, in words: the note or the destination is gone or binned.</summary>
    private static string DescribeMove(CommandFailure failure) => failure.Reason switch
    {
        CommandFailureReason.NotFound => "That note or folder no longer exists.",
        CommandFailureReason.InvalidState => "That note or folder is in the recycle bin.",
        _ => "The note could not be moved.",
    };

    /// <summary>
    /// A folder's context menu: right-click, Shift+F10 or the Menu key. Its actions are the folder list's keys.
    /// </summary>
    private void OnFolderListContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (_state != PaneState.Loaded
            || ItemOf<FolderListItem>(args.OriginalSource) is not { } folder
            || FolderList.ContainerFromItem(folder) is not ListViewItem container)
        {
            return;
        }

        args.Handled = true;
        FolderList.SelectedItem = folder;

        var menu = new MenuFlyout();
        menu.Items.Add(MenuItem("Open", "Ctrl+Down", EnterSelectedFolder));
        menu.Items.Add(MenuItem("Rename", "F2", BeginRename));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(MenuItem("New folder", "Ctrl+N", FocusInput));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(folder.IsPinned
            ? MenuItem("Unpin", string.Empty, () => PinFolder(folder.Id, pinned: false))
            : MenuItem("Pin", string.Empty, () => PinFolder(folder.Id, pinned: true)));
        IReadOnlyList<Folder> folders = Execute(_listFolders.Execute);
        menu.Items.Add(MenuItem("Move to top", string.Empty, () => MoveFolder(folder.Id, RepositionDirection.Top), FolderReposition.For(folders, FolderId.From(folder.Id), RepositionDirection.Top).Moves));
        menu.Items.Add(MenuItem("Move to bottom", string.Empty, () => MoveFolder(folder.Id, RepositionDirection.Bottom), FolderReposition.For(folders, FolderId.From(folder.Id), RepositionDirection.Bottom).Moves));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(MenuItem("Delete", "Delete", () => ConfirmDeleteFolder(folder.Id)));
        ShowMenu(menu, container, args);
    }

    /// <summary>
    /// The row a context request is for. Shift+F10 and the Menu key come from the row's container, whose item is its
    /// Content (measured: its DataContext was not the item, and no menu opened); a right-click comes from an element
    /// inside the row's template, whose DataContext is the item.
    /// </summary>
    private static T? ItemOf<T>(object source)
        where T : class =>
        source is ListViewItem { Content: T item } ? item : (source as FrameworkElement)?.DataContext as T;

    /// <summary>
    /// Gives a realised or recycled row its states for assistive technology. The row UI Automation reports is the
    /// container, not the template inside it (measured: help text on the template never reached the focused row), so
    /// "Pinned" and "Folded" are set on the container.
    /// </summary>
    private static void OnRowContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        string help = args.Item switch
        {
            NoteCard card => card.HelpText,
            FolderListItem folder => folder.HelpText,
            _ => string.Empty,
        };

        AutomationProperties.SetHelpText(args.ItemContainer, help);
    }

    /// <summary>At the pointer for a right-click; at the row for Shift+F10 or the Menu key.</summary>
    private static void ShowMenu(MenuFlyout menu, ListViewItem container, ContextRequestedEventArgs args)
    {
        if (args.TryGetPosition(container, out Windows.Foundation.Point point))
        {
            menu.ShowAt(container, point);
        }
        else
        {
            menu.ShowAt(container);
        }
    }

    private static MenuFlyoutItem MenuItem(string text, string keys, Action action, bool enabled = true)
    {
        var item = new MenuFlyoutItem { Text = text, KeyboardAcceleratorTextOverride = keys, IsEnabled = enabled };
        item.Click += (_, _) => action();
        return item;
    }

    /// <summary>
    /// Announces a command's result to assistive technology (ADR-016 §8) without putting text on screen: a
    /// notification from the stack, read once.
    /// </summary>
    private void Announce(string message, UIElement? source = null) =>
        FrameworkElementAutomationPeer.FromElement(source ?? NoteList)?.RaiseNotificationEvent(
            AutomationNotificationKind.ActionCompleted,
            AutomationNotificationProcessing.ImportantMostRecent,
            message,
            "NotoStack");

    private static bool IsControlDown() =>
        InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(CoreVirtualKeyStates.Down);

    private static bool IsShiftDown() =>
        InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(CoreVirtualKeyStates.Down);

    /// <summary>
    /// Pins or unpins a card's note (B14). It re-sorts into or out of the pinned group and keeps focus there; the card
    /// being edited keeps editing with its unsaved text.
    /// </summary>
    private void PinCard(string id, bool pinned)
    {
        if (_noteState != NoteState.Loaded)
        {
            return;
        }

        try
        {
            CommandResult result = _stack.SetPinned(NoteId.From(id), pinned);

            if (!result.IsSuccess)
            {
                ShowNoteNotice(DescribeNote(result.Failure!));
                return;
            }

            _selectedNoteId = id;
            ShowCards(_stack.Notes);
            HideNoteNotice();
            ApplyNoteState();
            Announce(pinned ? "Pinned" : "Unpinned");

            if (_stack.Editing?.Value == id)
            {
                StartEditor(id, CaretPlacement.End);
            }
            else
            {
                FocusNoteRow(id);
            }
        }
        catch (StorageException ex)
        {
            ShowNoteNotice($"Could not pin the note: {ex.Message}");
        }
    }

    /// <summary>
    /// Folds or unfolds a card (B12), in place: it keeps its container and focus. Folding the card being edited saves
    /// and leaves editing first; a failed save folds nothing.
    /// </summary>
    private void FoldCard(string id, bool folded)
    {
        if (_noteState != NoteState.Loaded || (folded && _stack.Editing?.Value == id && !TryEndEdit()))
        {
            return;
        }

        try
        {
            CommandResult result = _stack.SetFolded(NoteId.From(id), folded);

            if (!result.IsSuccess)
            {
                ShowNoteNotice(DescribeNote(result.Failure!));
                return;
            }

            if (Notes.FirstOrDefault(n => n.Id == id) is { } card)
            {
                card.IsFolded = folded;

                if (NoteList.ContainerFromItem(card) is ListViewItem container)
                {
                    AutomationProperties.SetHelpText(container, card.HelpText);
                }
            }

            Announce(folded ? "Folded" : "Unfolded");
            FocusNoteRow(id);
        }
        catch (StorageException ex)
        {
            ShowNoteNotice($"Could not fold the note: {ex.Message}");
        }
    }

    /// <summary>
    /// Folds or unfolds every card in the folder (B13) in one command. Folding all saves and leaves editing first;
    /// focus stays on the current card.
    /// </summary>
    private void FoldAll(bool folded)
    {
        if (_noteState != NoteState.Loaded || (folded && !TryEndEdit()))
        {
            return;
        }

        string? current = CurrentCard();

        try
        {
            CommandResult result = _stack.SetAllFolded(folded);

            if (!result.IsSuccess)
            {
                ShowNoteNotice(Describe(result.Failure!));
                return;
            }

            ShowCards(_stack.Notes);
            ApplyNoteState();
            Announce(folded ? "All notes folded" : "All notes unfolded");

            if (current is not null && _stack.Editing?.Value == current)
            {
                StartEditor(current, CaretPlacement.End);
            }
            else if (current is not null)
            {
                FocusNoteRow(current);
            }
        }
        catch (StorageException ex)
        {
            ShowNoteNotice($"Could not fold the notes: {ex.Message}");
        }
    }

    /// <summary>
    /// Moves a card up, down, to the top or bottom of its pin group (B8, B9). At that edge already, nothing is written.
    /// The card keeps focus at its new place; the card being edited keeps editing, with its caret.
    /// </summary>
    private void ReorderCard(string id, RepositionDirection direction)
    {
        if (_noteState != NoteState.Loaded || !_stack.CanReorder(NoteId.From(id), direction))
        {
            return;
        }

        try
        {
            CommandResult result = _stack.Reorder(NoteId.From(id), direction);

            if (!result.IsSuccess)
            {
                ShowNoteNotice(DescribeNote(result.Failure!));
                return;
            }

            _selectedNoteId = id;
            ShowCards(_stack.Notes);
            ApplyNoteState();
            Announce(Moved(direction));

            if (_stack.Editing?.Value == id)
            {
                StartEditor(id, CaretPlacement.End);
            }
            else
            {
                FocusNoteRow(id);
            }
        }
        catch (StorageException ex)
        {
            ShowNoteNotice($"Could not move the note: {ex.Message}");
        }
    }

    // ----------------------------------------------------------- drag and drop (B4)

    /// <summary>The card or folder being dragged, by id; <see langword="null"/> when no drag is under way.</summary>
    private string? _dragging;

    /// <summary>
    /// A card's drag begins (B10): one card, on a loaded stack. The list's own reorder draws the drag, the insertion point
    /// and Escape to cancel; the drop is decided when it completes.
    /// </summary>
    private void OnNoteDragStarting(object sender, DragItemsStartingEventArgs e)
    {
        if (_noteState != NoteState.Loaded || e.Items.Count != 1 || e.Items[0] is not NoteCard card)
        {
            e.Cancel = true;
            return;
        }

        _dragging = card.Id;
    }

    /// <summary>
    /// A card's drop (workspace-management.md, "B4 precisely"): the landing index goes to the session's rule, which
    /// refuses the other pin group, writes nothing for no move, and otherwise reorders. The visual order is never
    /// trusted: the stack is rebuilt from the session whatever happened, so a refusal snaps back.
    /// </summary>
    private void OnNoteDragCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        string? id = _dragging;
        _dragging = null;

        if (id is null || Notes.FirstOrDefault(n => n.Id == id) is not { } dropped)
        {
            return;
        }

        int landedAt = args.DropResult == DataPackageOperation.None ? -1 : Notes.IndexOf(dropped);
        string before = string.Join('|', _stack.Notes.Select(n => n.Id.Value));

        try
        {
            CommandResult result = landedAt < 0 ? CommandResult.Success() : _stack.Drop(NoteId.From(id), landedAt);

            _selectedNoteId = id;
            ShowCards(_stack.Notes);
            ApplyNoteState();

            if (!result.IsSuccess)
            {
                if (result.Failure!.Reason == CommandFailureReason.InvalidInput)
                {
                    Announce("Not moved: pin it to move it among the pinned notes, or unpin it to move it below them");
                }
                else
                {
                    ShowNoteNotice(DescribeNote(result.Failure));
                }
            }
            else if (before != string.Join('|', _stack.Notes.Select(n => n.Id.Value)))
            {
                Announce("Moved");
            }

            if (_stack.Editing?.Value == id)
            {
                StartEditor(id, CaretPlacement.End);
            }
            else
            {
                FocusNoteRow(id);
            }
        }
        catch (StorageException ex)
        {
            ShowCards(_stack.Notes);
            ShowNoteNotice($"Could not move the note: {ex.Message}");
        }
    }

    /// <summary>A folder's drag begins (C9): one folder, on a loaded list, not while one is being renamed.</summary>
    private void OnFolderDragStarting(object sender, DragItemsStartingEventArgs e)
    {
        if (_state != PaneState.Loaded || _renamingId is not null || e.Items.Count != 1 || e.Items[0] is not FolderListItem folder)
        {
            e.Cancel = true;
            return;
        }

        _dragging = folder.Id;
    }

    /// <summary>
    /// A folder's drop: the same rule over the folder list as it was before the drop, then <c>ReorderFolder</c>. The list
    /// is re-read whatever happened.
    /// </summary>
    private void OnFolderDragCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        string? id = _dragging;
        _dragging = null;

        if (id is null || Folders.FirstOrDefault(f => f.Id == id) is not { } dropped)
        {
            return;
        }

        int landedAt = args.DropResult == DataPackageOperation.None ? -1 : Folders.IndexOf(dropped);

        try
        {
            RepositionDrop<FolderId> drop = landedAt < 0
                ? new RepositionDrop<FolderId>(true, new RepositionTarget<FolderId>(false, null))
                : FolderReposition.Drop(_listFolders.Execute(), FolderId.From(id), landedAt);

            if (!drop.Allowed)
            {
                Announce("Not moved: pin it to move it among the pinned folders, or unpin it to move it below them", FolderList);
            }
            else if (drop.Target.Moves)
            {
                CommandResult result = _commands.Dispatch(new ReorderFolder(FolderId.From(id), drop.Target.After));

                if (!result.IsSuccess)
                {
                    Refresh();
                    ShowNotice(Describe(result.Failure!));
                    return;
                }

                Announce("Moved", FolderList);
            }

            _selectedId = id;
            Refresh();
            FocusRow(id);
        }
        catch (StorageException ex)
        {
            Refresh();
            ShowNotice($"Could not move the folder: {ex.Message}");
        }
    }

    /// <summary>Moves a folder up, down, to the top or bottom of its pin group (C9): re-read, focus follows it.</summary>
    private void MoveFolder(string id, RepositionDirection direction)
    {
        if (_state != PaneState.Loaded)
        {
            return;
        }

        try
        {
            RepositionTarget<FolderId> target = FolderReposition.For(_listFolders.Execute(), FolderId.From(id), direction);

            if (!target.Moves)
            {
                return;
            }

            CommandResult result = _commands.Dispatch(new ReorderFolder(FolderId.From(id), target.After));

            if (!result.IsSuccess)
            {
                Refresh();
                ShowNotice(Describe(result.Failure!));
                return;
            }

            _selectedId = id;
            Refresh();
            FocusRow(id);
            Announce(Moved(direction), FolderList);
        }
        catch (StorageException ex)
        {
            ShowNotice($"Could not move the folder: {ex.Message}");
        }
    }

    private static string Moved(RepositionDirection direction) => direction switch
    {
        RepositionDirection.Up => "Moved up",
        RepositionDirection.Down => "Moved down",
        RepositionDirection.Top => "Moved to top",
        _ => "Moved to bottom",
    };

    /// <summary>A read for a menu: on a storage failure the menu still opens, its moves disabled.</summary>
    private static IReadOnlyList<Folder> Execute(Func<IReadOnlyList<Folder>> read)
    {
        try
        {
            return read();
        }
        catch (StorageException)
        {
            return [];
        }
    }

    // ----------------------------------------------------------- deleting folders, the Recycle Bin (B5)

    /// <summary>The Recycle Bin's session (Phase B5), set once by the composition root.</summary>
    internal RecycleBin? Bin { get; set; }

    /// <summary>The Recycle Bin surface while it is open; <see langword="null"/> otherwise.</summary>
    private RecycleBinPage? RecycleBinSurface { get; set; }

    // What had focus on the folder list when the bin was opened: where leaving it returns.
    private Control? _binReturnFocus;

    /// <summary>
    /// Asks before moving a folder and its notes to the Recycle Bin (C11; ADR-016 §7): a flyout at the row, Cancel
    /// focused. Cancelling writes nothing. No card can be unsaved here: the folder list is reached only by leaving the
    /// stack, which saves first.
    /// </summary>
    private void ConfirmDeleteFolder(string id)
    {
        if (_state != PaneState.Loaded || Bin is null
            || Folders.FirstOrDefault(f => f.Id == id) is not { } folder
            || FolderList.ContainerFromItem(folder) is not ListViewItem container)
        {
            return;
        }

        int notes;
        try
        {
            notes = Bin.NotesIn(FolderId.From(id));
        }
        catch (StorageException ex)
        {
            ShowNotice($"Could not read the folder: {ex.Message}");
            return;
        }

        ResourceDictionary resources = Microsoft.UI.Xaml.Application.Current.Resources;
        var cancel = new Button { Content = "Cancel", Style = (Style)resources["FolderPaneButtonStyle"] };
        var delete = new Button { Content = "Delete", Style = (Style)resources["FolderPaneButtonStyle"] };
        AutomationProperties.SetName(cancel, "Cancel");
        AutomationProperties.SetName(delete, "Delete folder");
        var message = new TextBlock
        {
            Text = $"Delete \u201C{folder.Name}\u201D and its {(notes == 1 ? "1 note" : $"{notes} notes")}? They go to the Recycle Bin.",
            TextWrapping = TextWrapping.Wrap,
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = (double)resources["SpaceS"], HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(delete);
        buttons.Children.Add(cancel);
        var body = new StackPanel { Spacing = (double)resources["SpaceS"] };
        body.Children.Add(message);
        body.Children.Add(buttons);

        var flyout = new Flyout { Content = body };
        cancel.Click += (_, _) => flyout.Hide();
        delete.Click += (_, _) =>
        {
            flyout.Hide();
            DeleteFolder(id);
        };
        flyout.Opened += (_, _) => _ = cancel.Focus(FocusState.Programmatic);
        flyout.Closed += (_, _) =>
        {
            if (Folders.Any(f => f.Id == id))
            {
                FocusRow(id);
            }
        };
        flyout.ShowAt(container);
    }

    /// <summary>
    /// Moves a folder and its active notes to the Recycle Bin (C11): soft, one timestamp, nothing destroyed. Focus goes
    /// to the next folder, else the previous, else the new-folder box.
    /// </summary>
    private void DeleteFolder(string id)
    {
        if (_state != PaneState.Loaded || Bin is null)
        {
            return;
        }

        string? next = Removal.Next(Folders, new FolderKey(id), static f => new FolderKey(f.Id))?.Id;

        try
        {
            CommandResult result = Bin.DeleteFolder(FolderId.From(id));

            if (!result.IsSuccess)
            {
                Refresh();
                ShowNotice(Describe(result.Failure!));
                return;
            }

            _selectedId = next;
            Refresh();
            Announce("Folder moved to the Recycle Bin", FolderList);

            if (next is not null)
            {
                FocusRow(next);
            }
            else
            {
                MoveFocusToList();
            }
        }
        catch (StorageException ex)
        {
            ShowNotice($"Could not delete the folder: {ex.Message}");
        }
    }

    /// <summary>A folder id as the removal rule's key.</summary>
    private readonly record struct FolderKey(string Id);

    private void OnRecycleBinClick(object sender, RoutedEventArgs e) => OpenRecycleBin();

    /// <summary>
    /// Opens the Recycle Bin (Phase B5) from the folder list: built now, beside the surfaces, released when left, as
    /// Settings is. No card is being edited: the folder list is showing.
    /// </summary>
    internal void OpenRecycleBin()
    {
        if (Bin is null || RecycleBinSurface is not null || PaneRoot.Visibility != Visibility.Visible)
        {
            return;
        }

        _binReturnFocus = Content?.XamlRoot is { } root ? FocusManager.GetFocusedElement(root) as Control : null;

        var page = new RecycleBinPage();
        Grid.SetRow(page, Grid.GetRow(PaneRoot));
        page.KeyDown += OnRecycleBinKeyDown;
        page.BackRequested += OnRecycleBinBackRequested;
        ((Panel)PaneRoot.Parent).Children.Add(page);
        RecycleBinSurface = page;

        page.Show(Bin);
        PaneRoot.Visibility = Visibility.Collapsed;
        page.Visibility = Visibility.Visible;
        Motion.EnterSurface(page, forward: true);

        _ = DispatcherQueue.TryEnqueueGuarded(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, page.FocusFirst);
    }

    /// <summary>Leaves the Recycle Bin for the folder list, re-read (a restore may have brought folders back).</summary>
    private void CloseRecycleBin()
    {
        if (RecycleBinSurface is not { } page)
        {
            return;
        }

        PaneRoot.Visibility = Visibility.Visible;
        page.Visibility = Visibility.Collapsed;
        Motion.EnterSurface(PaneRoot, forward: false);

        ((Panel)page.Parent).Children.Remove(page);
        page.KeyDown -= OnRecycleBinKeyDown;
        page.BackRequested -= OnRecycleBinBackRequested;
        RecycleBinSurface = null;

        Refresh();
        Control restore = _binReturnFocus is { IsLoaded: true, Visibility: Visibility.Visible } previous ? previous : RecycleBinButton;
        _binReturnFocus = null;
        _ = DispatcherQueue.TryEnqueueGuarded(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => restore.Focus(FocusState.Programmatic));
    }

    private void OnRecycleBinBackRequested(object? sender, EventArgs e) => CloseRecycleBin();

    /// <summary>Escape in the Recycle Bin follows the stored behaviour: by default it goes back to the folder list.</summary>
    private void OnRecycleBinKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = OnEscape(WorkspaceSurface.RecycleBin);
        }
    }

    /// <summary>Pins or unpins a folder (C8): the list is re-read (O2) and focus follows the folder.</summary>
    private void PinFolder(string id, bool pinned)
    {
        if (_state != PaneState.Loaded)
        {
            return;
        }

        try
        {
            CommandResult result = pinned
                ? _commands.Dispatch(new PinFolder(FolderId.From(id)))
                : _commands.Dispatch(new UnpinFolder(FolderId.From(id)));

            if (!result.IsSuccess)
            {
                Refresh();
                ShowNotice(Describe(result.Failure!));
                return;
            }

            _selectedId = id;
            Refresh();
            FocusRow(id);
            Announce(pinned ? "Pinned" : "Unpinned", FolderList);
        }
        catch (StorageException ex)
        {
            ShowNotice($"Could not pin the folder: {ex.Message}");
        }
    }

    private static bool IsAltDown() =>
        InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu)
            .HasFlag(CoreVirtualKeyStates.Down);

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

        // Delete asks to move the folder and its notes to the Recycle Bin (C11), after a confirmation.
        if (e.Key == VirtualKey.Delete && (FolderList.SelectedItem as FolderListItem ?? (FocusManager.GetFocusedElement(Content.XamlRoot) as ListViewItem)?.Content as FolderListItem) is { } folder)
        {
            e.Handled = true;
            ConfirmDeleteFolder(folder.Id);
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
        // Ctrl+Alt+Shift+Up/Down move the focused folder within its pin group (C9). Ahead of Ctrl+Down, which enters.
        if (e.Key is VirtualKey.Up or VirtualKey.Down && IsControlDown() && IsAltDown() && IsShiftDown())
        {
            e.Handled = true;

            if ((FolderList.SelectedItem as FolderListItem ?? (FocusManager.GetFocusedElement(Content.XamlRoot) as ListViewItem)?.Content as FolderListItem) is { } folder)
            {
                MoveFolder(folder.Id, e.Key == VirtualKey.Up ? RepositionDirection.Up : RepositionDirection.Down);
            }

            return;
        }

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

        // With single-click opening, the first click has already entered the folder, and the second lands on the note
        // list that replaced it: acting on it would open a note nobody chose. There it is a tap, which OnNoteListTapped
        // refuses.
        if (!SingleClickOpen)
        {
            EnterSelectedFolder();
        }
    }

    /// <summary>One click on a folder enters it, when single-click opening is on (parity J7, C4).</summary>
    private void OnFolderListTapped(object sender, TappedRoutedEventArgs e)
    {
        if (SingleClickOpen && IsOnRow<FolderListItem>(e))
        {
            e.Handled = true;
            _enteredByClickAt = Environment.TickCount64;
            EnterSelectedFolder();
        }
    }

    /// <summary>
    /// Whether a click landed on a row, not on the list's empty space: a click there must not open whatever happens
    /// to be selected.
    /// </summary>
    private static bool IsOnRow<TItem>(TappedRoutedEventArgs e) => (e.OriginalSource as FrameworkElement)?.DataContext is TItem;

    /// <summary>
    /// Escape on a card or the stack's controls (parity C5, A12): by default it leaves the folder. In the editor,
    /// Escape is the editor's (<see cref="OnEditorLeaveRequested"/>). Ctrl+Alt+Backspace moves the current card's note
    /// to the recycle bin (B7), from the card or its editor.
    /// </summary>
    private void OnNoteRootKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = OnEscape(WorkspaceSurface.NoteList);
            return;
        }

        if (e.Key == VirtualKey.Back && IsControlDown() && IsAltDown() && CurrentCard() is { } card)
        {
            e.Handled = true;
            DeleteCard(card);
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
        _ = FolderList.DispatcherQueue.TryEnqueueGuarded(
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
