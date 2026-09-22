using Microsoft.UI.Xaml;
using Noto.Core;
using Noto.Infrastructure.Storage;
using Noto.UseCases.Folders;
using Noto.UseCases.Notes;

namespace Noto;

/// <summary>
/// Application entry point and composition root.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the only place in the application that names a concrete
/// Infrastructure type.</b> Views receive use cases and never see
/// <see cref="NotoDatabase"/>, a repository, or SQLite — which is what keeps
/// the dependency direction one-way (ADR-009) and makes the boundary
/// mechanically checkable rather than a convention.
/// </para>
/// <para>
/// Wiring is <b>manual</b>. The graph is six objects with no lifetime
/// complexity, and ADR-010 asks only that dispatch be "wired in the
/// composition root" — not that a container do it. A container earns its cost
/// when wiring becomes error-prone; this does not qualify yet.
/// </para>
/// <para>
/// <b>M2-0 integration spike.</b> The purpose is to prove one read and one
/// write reach real SQLite and come back. It is deliberately not a workspace:
/// no docking, no hotkey, no settings (#16, #9), and no design system (#22).
/// </para>
/// </remarks>
public partial class App : Application
{
    private Window? _window;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // The real user database, in the real location — %LOCALAPPDATA%\Noto.
        // A spike against a temporary file would prove nothing about the
        // application actually starting.
        var paths = NotoStoragePaths.ForCurrentUser();

        // Required before opening the database. SQLite creates the FILE on
        // demand but never its parent DIRECTORY, so a first run on a clean
        // machine fails with "unable to open database file" without this.
        paths.EnsureCreated();

        var database = new NotoDatabase(paths.DatabaseFile);

        // Creates the file on first run and applies any pending migration.
        database.Initialize();

        // Repositories are stateless over the database; each call opens and
        // disposes its own connection (NotoDatabase.OpenConnection), so a
        // single instance is safe to share and there is nothing to dispose at
        // shutdown — NotoDatabase deliberately holds no connection between
        // calls and is not IDisposable.
        var folders = new SqliteFolderRepository(database);
        var notes = new SqliteNoteRepository(database);

        // One clock for every handler: two SystemClock reads inside a single
        // user action could straddle a tick and stamp two rows differently.
        IClock clock = SystemClock.Instance;

        _window = new MainWindow(
            new ListFoldersQuery(folders),
            new CreateFolderHandler(folders, clock),
            new RenameFolderHandler(folders, clock),
            new ListNotesInFolderQuery(notes),
            new GetNoteQuery(notes),
            new UpdateNoteContentHandler(notes, clock),
            new CreateNoteHandler(notes, clock),
            new DeleteNoteHandler(notes, clock));

        _window.Activate();

        // Lets a scripted or CI run verify startup without a human closing the
        // window. Not a product feature.
        if (Environment.GetCommandLineArgs().Contains("--smoke-test"))
        {
            _ = _window.DispatcherQueue.TryEnqueue(Exit);
        }
    }
}
