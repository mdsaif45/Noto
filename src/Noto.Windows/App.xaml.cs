using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Noto.Core;
using Noto.Infrastructure.Storage;
using Noto.Platform.Windows;
using Noto.UseCases.Folders;
using Noto.UseCases.Notes;
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
/// <b>#16 slice 2</b> adds the first piece of the workspace: the window docks
/// to one vertical edge of the display it opened on, once, at launch. Not yet
/// always-on-top, hidden, resizable by the user or remembered.
/// </para>
/// </remarks>
public partial class App : Application
{
    /// <summary>
    /// The workspace width until #16 slice 3 stores one per display: the
    /// nominal default of ADR-007 §4, fitted to each display by
    /// <see cref="WorkspaceWidth.Clamp"/>.
    /// </summary>
    private const double DefaultWorkspaceWidthDip = 360;

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

        // Phase 1 (architecture-overview.md §startup): settings are read once,
        // before any window, because the things that depend on them — hotkey
        // registration, the tray — run before the first frame. Every later
        // read is served from memory.
        //
        // Load never throws. A settings failure leaves this session on
        // defaults rather than stopping startup (#9); the database itself
        // failing is a different decision, already made above by Initialize.
        //
        // Held but not yet passed to MainWindow: nothing in the three merged
        // surfaces reads a setting, and the two keys #9 registers are both
        // for the hotkey that #16 builds. Wiring it into the window now would
        // be adding a parameter with no caller.
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
        //
        // Right is the default edge (parity A2/J3). --dock-left is not a
        // product feature: it lets a scripted run verify the left edge until
        // slice 3 makes the edge a setting.
        DockToEdge(
            _window,
            Environment.GetCommandLineArgs().Contains("--dock-left") ? DockEdge.Left : DockEdge.Right);

        // Lets a scripted or CI run verify startup without a human closing the
        // window. Not a product feature.
        if (Environment.GetCommandLineArgs().Contains("--smoke-test"))
        {
            _ = _window.DispatcherQueue.TryEnqueue(Exit);
        }
    }

    /// <summary>
    /// Moves the window flush against one vertical edge of its display's work
    /// area.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The geometry is the platform layer's (<see cref="WindowDocking"/>);
    /// this only obtains the handle and makes the move. The move is
    /// <c>AppWindow.MoveAndResize</c>, which takes outer coordinates (ADR-007
    /// §4) — so the rectangle is passed through unchanged, never adjusted
    /// here.
    /// </para>
    /// <para>
    /// A failure leaves the window where Windows opened it. An undocked
    /// window is still a usable one, and docking is not worth failing
    /// startup over. Only the failures the platform documents are caught.
    /// </para>
    /// </remarks>
    private static void DockToEdge(Window window, DockEdge edge)
    {
        try
        {
            var handle = WindowHandle.FromHwnd(WindowNative.GetWindowHandle(window));
            PixelRect outer = WindowDocking.OuterBoundsFor(handle, edge, DefaultWorkspaceWidthDip);

            window.AppWindow.MoveAndResize(new RectInt32(outer.Left, outer.Top, outer.Width, outer.Height));
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or COMException)
        {
            Debug.WriteLine($"Docking to the {edge} edge failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
