using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Noto.Core;
using Noto.Core.Activation;
using Noto.Core.Settings;
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
/// <b>#16 slice 4</b> adds the global activation hotkey; <b>slice 5</b>
/// makes it show and hide the window (<see cref="WindowCoordinator"/>). The
/// window is not yet always-on-top.
/// </para>
/// <para>
/// <b>A17</b> makes Noto one process per data root (ADR-013): the first
/// launch owns the root; a later one hands its request to the owner and
/// exits before it opens the database or creates a window.
/// </para>
/// </remarks>
public partial class App : Application
{
    private const string DataRootSwitch = "--data-root=";

    /// <summary>
    /// Registers the global activation hotkey (#16 slice 4), if it is enabled.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Settings are read once, here; a change takes effect on the next
    /// launch. The binding is valid by construction: its setting's validity
    /// rule is the <see cref="HotkeyChord"/> grammar, so a malformed stored
    /// value has already fallen back to the default.
    /// </para>
    /// <para>
    /// <b>A refused chord never stops startup.</b> Taken by another process,
    /// reserved by Windows, or refused for any other reason: Noto runs without
    /// the hotkey, no other chord is tried, the stored setting is left alone,
    /// and nothing is retried until the next launch. There is no user-visible
    /// report yet — that needs a surface (tray or settings) that does not
    /// exist — so the refusal is a diagnostic only.
    /// </para>
    /// </remarks>
    private GlobalHotkey? StartHotkey(SqliteSettingsStore settings)
    {
        if (!settings.Read(SettingKeys.HotkeyEnabled))
        {
            return null;
        }

        HotkeyChord chord = HotkeyChord.Parse(settings.Read(SettingKeys.HotkeyBinding));
        GlobalHotkey? hotkey = null;

        try
        {
            hotkey = GlobalHotkey.Create();
            HotkeyRegistration registration = hotkey.Register(chord);

            if (!registration.IsRegistered)
            {
                Debug.WriteLine($"The global hotkey {chord} was not registered: {registration.Status} ({registration.ErrorCode})");
                return null;
            }

            hotkey.Pressed += (_, e) => OnHotkeyPressed(e.MessageTime);

            // Ownership passes to the caller; the finally below must not dispose it.
            GlobalHotkey registered = hotkey;
            hotkey = null;
            return registered;
        }
        catch (Win32Exception ex)
        {
            Debug.WriteLine($"The global hotkey could not be set up: {ex.NativeErrorCode}");
            return null;
        }
        finally
        {
            hotkey?.Dispose();
        }
    }

    /// <summary>
    /// What the hotkey does (#16 slice 5): the show/hide toggle.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Runs synchronously inside the hotkey's <c>WM_HOTKEY</c> handling,
    /// which is what entitles Noto to take the foreground from another
    /// application. The rules are <see cref="WorkspaceToggle"/>'s and are
    /// recorded in ADR-007 §4.
    /// </para>
    /// <para>
    /// If docking failed at launch there is no coordinator, and the hotkey
    /// falls back to slice 4's behaviour: bring the window forward, never hide.
    /// </para>
    /// </remarks>
    private void OnHotkeyPressed(int messageTime)
    {
        if (_coordinator is not null)
        {
            _ = _coordinator.OnActivationRequested(WorkspaceRequest.Toggle, messageTime);
        }
        else if (_windowHandle is not null)
        {
            _ = WindowActivation.BringToForeground(_windowHandle);
        }
    }

    private Window? _window;

    /// <summary>
    /// The dock on <see cref="_window"/>. Held so its <c>Resized</c>
    /// subscription lives as long as the window; the subclass itself is
    /// removed by Windows' own <c>WM_NCDESTROY</c>.
    /// </summary>
    private DockedWindow? _docked;

    /// <summary>
    /// The global activation hotkey, or <see langword="null"/> when it is
    /// disabled or Windows refused it. Owned for the life of the process and
    /// disposed when the workspace window closes.
    /// </summary>
    private GlobalHotkey? _hotkey;

    /// <summary>The workspace window's handle, for the hotkey to bring it forward.</summary>
    private WindowHandle? _windowHandle;

    /// <summary>The show/hide lifecycle; <see langword="null"/> when docking failed.</summary>
    private WindowCoordinator? _coordinator;

    /// <summary>Which data root this process owns (A17).</summary>
    private InstanceKey? _instanceKey;

    /// <summary>Ownership of the data root, held until the window closes.</summary>
    private InstanceOwnership? _ownership;

    /// <summary>
    /// Where later launches hand their requests over; <see langword="null"/>
    /// when Windows refused it (the name squatted), in which case they cannot
    /// reach this process and say so.
    /// </summary>
    private ActivationPipeServer? _activationPipe;

    /// <summary>At most one launch request waiting for the UI thread.</summary>
    private readonly PendingLaunch _pendingLaunch = new();

    /// <summary>A close that will go ahead has begun: no activation starts after it.</summary>
    private volatile bool _closing;

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

        // A17 (ADR-013): decide who owns this data root before anything opens
        // it. A second launch hands its request to the owner and exits here —
        // no database, no window.
        if (!ClaimDataRoot(paths))
        {
            return;
        }

        // Before the database and the window, so a launch arriving during
        // startup is taken now and acted on once the window exists.
        StartActivationPipe();

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

        // Phase 1 (architecture-overview.md §startup): the hotkey is
        // registered before any window exists, on its own message-only
        // window, so it does not depend on the workspace window's lifetime.
        _hotkey = StartHotkey(settings);

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

        _windowHandle = WindowHandle.FromHwnd(WindowNative.GetWindowHandle(_window));
        _window.Closed += (_, _) =>
        {
            _hotkey?.Dispose();

            // The pipe first, then the mutex: a successor waiting on the
            // mutex must find the pipe's name free when it gets it.
            Trace.WriteLine($"Noto.Instance released key={_instanceKey?.PathHash}");
            _activationPipe?.Dispose();
            _ownership?.Dispose();
        };

        // After MainWindow's own Closing handler, which may cancel the close
        // to keep unsaved text: only a close that goes ahead stops activation.
        // From here a later launch is told Noto is shutting down, and waits to
        // take over instead of handing its request to a closing window.
        _window.AppWindow.Closing += (_, e) =>
        {
            if (!e.Cancel)
            {
                _closing = true;
                _activationPipe?.BeginShutdown();
                _coordinator?.BeginShutdown();
                Trace.WriteLine($"Noto.Instance shutting-down key={_instanceKey?.PathHash}");
            }
        };

        _window.Activate();

        // After Activate: the inset is read from DWM's frame bounds, which
        // describe what is drawn, so they are read once the window is shown.
        // Reading them earlier gave the same result here, but is not relied
        // on. The window may appear at its default position for a moment.
        var preferences = new WorkspacePreferences(settings);
        DockAtLaunch(_window, preferences);

        if (_docked is not null && _window is MainWindow main)
        {
            _coordinator = new WindowCoordinator(_window, _windowHandle, _docked, preferences, main.SaveBeforeHide);
        }

        // Lets a scripted or CI run verify startup without a human closing the
        // window. Not a product feature.
        if (commandLine.Contains("--smoke-test"))
        {
            _ = _window.DispatcherQueue.TryEnqueue(Exit);
        }

        // Last: hotkey presses made before this point were made while Noto was
        // starting. They are delivered only after OnLaunched returns (measured)
        // and must not hide the window that has just appeared.
        _coordinator?.MarkReady();
    }

    /// <summary>
    /// Claims the data root (A17, ADR-013): owner, or a second launch that
    /// hands off and exits.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when this process owns the root and starts
    /// normally. Otherwise the process is exiting: 0 handed off, 2 the owner
    /// could not be reached, 3 refused — the last two after a message box.
    /// </returns>
    /// <remarks>
    /// <c>Trace</c>, not <c>Debug</c>: these lines reach
    /// <c>OutputDebugString</c> in Release builds, which is where the runtime
    /// harness reads them. They name outcomes only — never a path.
    /// </remarks>
    private bool ClaimDataRoot(NotoStoragePaths paths)
    {
        _instanceKey = InstanceKey.ForDataRoot(paths.Root);
        InstanceClaim claim = InstanceOwnership.Claim(_instanceKey, ProcessIdentity.IsElevated(), out _ownership);

        Trace.WriteLine(
            $"Noto.Instance claim outcome={claim.Outcome} refusal={claim.Refusal} tookOver={claim.TookOver} "
            + $"recovered={_ownership?.Recovered ?? false} key={_instanceKey.PathHash}");

        if (claim.Outcome == ClaimOutcome.Owner)
        {
            return true;
        }

        if (RefusalMessage(claim) is string message)
        {
            StartupNotice.Show(message);
        }

        // Nothing is open: no database, no window. Exit now, with the code
        // that says why.
        Environment.Exit(claim.Outcome switch
        {
            ClaimOutcome.HandedOff => 0,
            ClaimOutcome.Unreachable => 2,
            _ => 3,
        });

        return false;
    }

    private static string? RefusalMessage(InstanceClaim claim) => (claim.Outcome, claim.Refusal) switch
    {
        (ClaimOutcome.HandedOff, _) => null,
        (ClaimOutcome.Unreachable, _) => "Another Noto instance could not be reached. Please close it or restart Noto.",
        (_, ClaimRefusal.Elevated) => "This Noto launch requires a normal-integrity process. Start Noto without \"Run as administrator\".",
        (_, ClaimRefusal.OtherSession) => "Noto is already running in another Windows session.",
        (_, ClaimRefusal.WrongUser) => "Noto could not verify the running instance, so it did not start a second one.",
        (_, ClaimRefusal.Rejected) => "The running Noto is a different version. Close it, then start Noto again.",
        _ => "Noto could not check for a running instance, so it did not start.",
    };

    /// <summary>
    /// Starts listening for later launches. A refusal — the name already
    /// taken — leaves this process the owner, unreachable by later launches;
    /// it is recorded, not fatal.
    /// </summary>
    private void StartActivationPipe()
    {
        DispatcherQueue ui = DispatcherQueue.GetForCurrentThread();

        PipeStartStatus status = ActivationPipeServer.Start(_instanceKey!, _ => OnLaunchRequested(ui), out _activationPipe, out int error);

        Trace.WriteLine($"Noto.Instance pipe status={status} error={error} key={_instanceKey!.PathHash}");
    }

    /// <summary>
    /// A later launch's request, on the pipe's thread. Never touches the
    /// window: it is handed to the UI thread, folded into any request
    /// already waiting there.
    /// </summary>
    private ActivationReply OnLaunchRequested(DispatcherQueue ui)
    {
        if (_pendingLaunch.Offer(Environment.TickCount) && !ui.TryEnqueue(RunPendingLaunch))
        {
            // The dispatcher is shutting down with the process.
            return ActivationReply.ShuttingDown;
        }

        return ActivationReply.Accepted;
    }

    /// <summary>
    /// The waiting launch, on the UI thread, through the one activation path:
    /// the coordinator, as a launch — which shows, restores or brings Noto
    /// forward and never hides it. Without a coordinator (docking failed) it
    /// falls back to bringing the window forward, as the hotkey does.
    /// </summary>
    private void RunPendingLaunch()
    {
        int requestTime = _pendingLaunch.Take();

        if (_closing)
        {
            return;
        }

        WorkspaceAction action = WorkspaceAction.Focus;

        if (_coordinator is not null)
        {
            action = _coordinator.OnActivationRequested(WorkspaceRequest.Launch, requestTime);
        }
        else if (_windowHandle is not null)
        {
            _ = WindowActivation.BringToForeground(_windowHandle);
        }

        Trace.WriteLine($"Noto.Instance launch action={action} key={_instanceKey?.PathHash}");
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

            // The requested width, not the clamped one: what a restore re-docks at.
            _docked.Remember(widthDip);

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
