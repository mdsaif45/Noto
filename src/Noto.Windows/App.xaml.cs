using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Noto.Core;
using Noto.Infrastructure.Storage;
using Noto.Platform.Windows;
using Noto.UseCases.Folders;
using Noto.UseCases.Notes;
using Noto.UseCases.Workspace;
using Windows.Graphics;
using WinRT.Interop;

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
/// write reach real SQLite and come back. It was deliberately not a workspace:
/// no docking, no hotkey, no settings (#16, #9), and no design system (#22).
/// </para>
/// <para>
/// <b>#16 slices 2 and 3</b> add the first piece of the workspace: the window
/// docks to one vertical edge of the display it opened on — the edge and the
/// width both remembered — and the user can resize it from its inner edge.
/// Not yet always-on-top, hidden or summoned by a hotkey.
/// </para>
/// </remarks>
public partial class App : Application
{
    private const string DataRootSwitch = "--data-root=";

    private Window? _window;

    /// <summary>
    /// The dock on <see cref="_window"/>. Held so its <c>Resized</c>
    /// subscription lives as long as the window; the subclass itself is
    /// removed by Windows' own <c>WM_NCDESTROY</c>.
    /// </summary>
    private DockedWindow? _docked;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // The real user database, in the real location — %LOCALAPPDATA%\Noto.
        // A spike against a temporary file would prove nothing about the
        // application actually starting.
        //
        // --data-root=<path> is not a product feature. It points one process
        // at an isolated data folder so a scripted run can exercise real
        // SQLite persistence — restart included — without touching the
        // user's notes or settings. Without it, startup is exactly as before.
        string[] commandLine = Environment.GetCommandLineArgs();
        string? dataRoot = commandLine
            .FirstOrDefault(a => a.StartsWith(DataRootSwitch, StringComparison.Ordinal))?[DataRootSwitch.Length..];

        var paths = string.IsNullOrWhiteSpace(dataRoot)
            ? NotoStoragePaths.ForCurrentUser()
            : NotoStoragePaths.At(dataRoot);

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

        // Phase 1 (architecture-overview.md §startup): settings are read once,
        // before any window, because the things that depend on them — hotkey
        // registration, the tray — run before the first frame. Every later
        // read is served from memory.
        //
        // Load never throws. A settings failure leaves this session on
        // defaults rather than stopping startup (#9); the database itself
        // failing is a different decision, already made above by Initialize.
        //
        // Not passed to MainWindow: none of its surfaces reads a setting. The
        // dock below reads the edge and width through WorkspacePreferences.
        var settings = new SqliteSettingsStore(database);
        settings.Load();

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

        // After Activate: the inset is read from DWM's frame bounds, which
        // describe what is drawn, so they are read once the window is shown.
        // Reading them earlier gave the same result here, but is not relied
        // on. The window may appear at its default position for a moment.
        DockAtLaunch(_window, new WorkspacePreferences(settings));

        // Lets a scripted or CI run verify startup without a human closing the
        // window. Not a product feature.
        if (commandLine.Contains("--smoke-test"))
        {
            _ = _window.DispatcherQueue.TryEnqueue(Exit);
        }
    }

    /// <summary>
    /// Docks the window to its remembered edge at its remembered width, and
    /// keeps it docked while the user resizes it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The order is the one the #16 slice 3 spike validated: chrome, then the
    /// dock's window subclass, then the first move — which runs through
    /// <see cref="DockedWindow.MoveOwn"/>, so the position guard that refuses
    /// every other reposition lets it through.
    /// </para>
    /// <para>
    /// <b>Chrome.</b> No title bar, no minimise, no maximise. A WinUI 3
    /// caption drag never reaches the window's hit test, so a title bar would
    /// let the user drag the window off its edge. Closing it is Alt+F4 or the
    /// taskbar menu until the tray and hide slices arrive.
    /// </para>
    /// <para>
    /// The geometry and the resize behaviour are the platform's; the width
    /// chain is <see cref="WorkspacePreferences"/>'s. This method only
    /// obtains the handle, sets the presenter and makes the move.
    /// <c>AppWindow.MoveAndResize</c> takes outer coordinates (ADR-007 §4),
    /// so the rectangle is passed through unchanged.
    /// </para>
    /// <para>
    /// A failure leaves the window usable where Windows opened it: if the
    /// dock cannot be installed, the chrome is put back. Only the failures
    /// the platform documents are caught.
    /// </para>
    /// </remarks>
    private void DockAtLaunch(Window window, WorkspacePreferences preferences)
    {
        OverlappedPresenter? presenter = window.AppWindow.Presenter as OverlappedPresenter;

        try
        {
            var handle = WindowHandle.FromHwnd(WindowNative.GetWindowHandle(window));
            DockEdge edge = DockEdges.From(preferences.Edge);

            SetDockedChrome(presenter, docked: true);

            _docked = DockedWindow.Attach(handle, edge);
            _docked.Resized += (_, e) =>
            {
                if (!preferences.RememberWidth(e.Monitor.Value, e.VisibleWidthDip))
                {
                    // The width on screen stays what the user chose; only
                    // remembering it failed.
                    Debug.WriteLine("The workspace width could not be saved.");
                }
            };

            double widthDip = preferences.WidthFor(DisplayMonitors.ForWindow(handle).Id.Value);
            PixelRect outer = WindowDocking.OuterBoundsFor(handle, edge, widthDip);

            _docked.MoveOwn(() => window.AppWindow.MoveAndResize(new RectInt32(outer.Left, outer.Top, outer.Width, outer.Height)));
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or COMException)
        {
            if (_docked is null)
            {
                SetDockedChrome(presenter, docked: false);
            }

            Debug.WriteLine($"Docking failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void SetDockedChrome(OverlappedPresenter? presenter, bool docked)
    {
        if (presenter is null)
        {
            return;
        }

        presenter.SetBorderAndTitleBar(!docked, !docked);
        presenter.IsMaximizable = !docked;
        presenter.IsMinimizable = !docked;
    }
}
