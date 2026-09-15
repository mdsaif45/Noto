using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Noto.Core.Commands;
using Noto.Core.Folders;
using Noto.Core.Storage;
using Noto.UseCases.Folders;

namespace Noto;

/// <summary>
/// The M2-0 integration spike window: one read, one write, real SQLite.
/// </summary>
/// <remarks>
/// <para>
/// Receives <b>use cases</b>, never repositories or a database. The
/// composition root in <see cref="App"/> owns the concrete Infrastructure
/// types; this class cannot reach SQLite even by accident, which is the
/// boundary ADR-009 requires expressed as a constructor signature.
/// </para>
/// <para>
/// <b>Refresh is a re-query, not local mutation.</b> After a successful
/// command the window asks the database again rather than inserting into its
/// own list. ADR-010 is explicit that "events are not the persistence
/// mechanism. The database is." — and the engine makes that concrete: a new
/// folder's position comes from O3's ordering rule, so the UI cannot know
/// where the row belongs without reading it back.
/// </para>
/// <para>
/// No MVVM framework, no state-management library: an
/// <see cref="ObservableCollection{T}"/> and a status string are the whole
/// model. Anything larger would be scaffolding for a workspace that does not
/// exist yet.
/// </para>
/// </remarks>
public sealed partial class MainWindow : Window
{
    private readonly ListFoldersQuery _listFolders;
    private readonly CreateFolderHandler _createFolder;

    public MainWindow(ListFoldersQuery listFolders, CreateFolderHandler createFolder)
    {
        _listFolders = listFolders ?? throw new ArgumentNullException(nameof(listFolders));
        _createFolder = createFolder ?? throw new ArgumentNullException(nameof(createFolder));

        InitializeComponent();

        Refresh();
    }

    /// <summary>
    /// The folders currently shown, as display items.
    /// </summary>
    /// <remarks>
    /// <see cref="FolderListItem"/> rather than <see cref="Folder"/>: the XAML
    /// compiler generates setters for every property of a bound type, and the
    /// domain record is init-only by design. See that type for the full note.
    /// </remarks>
    public ObservableCollection<FolderListItem> Folders { get; } = [];

    /// <summary>
    /// The read path — executes Q3 and replaces the displayed list.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Rebuilt wholesale rather than diffed. The query already returns the
    /// folders in the order the contract requires (O2 — pinned first, then
    /// <c>SortOrder</c>, then <c>Id</c>), so replacing the collection keeps the
    /// view agreeing with the database by construction. Diffing would mean
    /// reimplementing that ordering here, in a second place.
    /// </para>
    /// <para>
    /// Synchronous on the UI thread. At spike scale the read is sub-millisecond
    /// against a local file; if that ceases to be true, it is a finding for the
    /// workspace design rather than something to paper over here.
    /// </para>
    /// </remarks>
    private void Refresh()
    {
        try
        {
            SetStatus("Loading…");

            IReadOnlyList<Folder> folders = _listFolders.Execute();

            Folders.Clear();
            foreach (Folder folder in folders)
            {
                Folders.Add(new FolderListItem(folder.Name));
            }

            // Empty is a state, not a failure: §11 Q3 gives this query no
            // failure column, and a database with no folders is ordinary.
            SetStatus(Folders.Count == 0 ? "No folders yet." : string.Empty);
        }
        catch (StorageException ex)
        {
            // The infrastructure channel. §6 keeps it separate from business
            // failure precisely so the two can be told apart here.
            SetStatus($"Could not read folders: {ex.Message}");
        }
    }

    /// <summary>
    /// The write path — executes CreateFolder, then re-queries.
    /// </summary>
    private void OnCreateFolderClick(object sender, RoutedEventArgs e)
    {
        // Mutating: the button is disabled so a second click cannot race the
        // first. The spike has one entry point, but a double-click is still a
        // second entry.
        CreateButton.IsEnabled = false;

        try
        {
            CommandResult<FolderId> result =
                _createFolder.Handle(new CreateFolder(FolderNameInput.Text));

            if (!result.IsSuccess)
            {
                // The BUSINESS failure channel — CommandFailure, returned not
                // thrown, because "that name is empty" is an expected outcome
                // rather than a fault (§6). The window stays usable.
                SetStatus(Describe(result.Failure!));
                return;
            }

            FolderNameInput.Text = string.Empty;

            // Re-query rather than adding the new folder locally. The database
            // decides where it sits.
            Refresh();
        }
        catch (StorageException ex)
        {
            SetStatus($"Could not create the folder: {ex.Message}");
        }
        finally
        {
            CreateButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// Turns a <see cref="CommandFailure"/> into something a user can read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately small: a switch over the four reasons, not a notification
    /// framework. The spike needs the failure to be <i>visible</i> and the
    /// application to stay usable; toasts, dialogs and severity are the
    /// workspace's problem.
    /// </para>
    /// <para>
    /// The failure's own <c>Message</c> is not shown verbatim. It is written
    /// for diagnostics and names ids and fields; that is safe but not useful to
    /// a user, and the mapping is the point of this layer.
    /// </para>
    /// </remarks>
    private static string Describe(CommandFailure failure) => failure.Reason switch
    {
        CommandFailureReason.InvalidInput => "A folder name cannot be empty.",
        CommandFailureReason.DuplicateName => "A folder with that name already exists.",
        CommandFailureReason.NotFound => "That folder no longer exists.",
        CommandFailureReason.InvalidState => "That folder is in the recycle bin.",
        _ => "The folder could not be created.",
    };

    private void SetStatus(string message) => StatusText.Text = message;
}
