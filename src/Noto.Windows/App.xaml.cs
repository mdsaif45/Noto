using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Noto.Core;
using Noto.Core.Activation;
using Noto.Core.Diagnostics;
using Noto.Core.Settings;
using Noto.Core.Storage;
using Noto.Infrastructure.Diagnostics;
using Noto.Infrastructure.Storage;
using Noto.Platform.Windows;
using Noto.UseCases.Folders;
using Noto.UseCases.Lifetime;
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
/// <para>
/// <b>#16 slice 6</b> makes it a drawer: topmost while shown, put away by
/// <c>Ctrl+W</c>, by Escape at the top level, or by losing activation to
/// another application — unless pinned for the session.
/// </para>
/// <para>
/// <b>#16 slice 7</b> adds the notification-area icon and the session's end
/// (ADR-014). The icon is an input surface only: what it asks for goes to the
/// coordinator or to the one close path, like everything else.
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

    /// <summary>Exit code after a failure reached a last-chance boundary (#10). 2 and 3 are A17's refusals.</summary>
    private const int FailedExitCode = 4;

    /// <summary>Exit code when the database could not be opened or brought up to date (#10).</summary>
    private const int StartupFailedExitCode = 5;

    /// <summary>How long a failure on another thread waits for the UI thread to save the open note.</summary>
    private static readonly TimeSpan FailureSaveTimeout = TimeSpan.FromSeconds(5);

    /// <summary>The last-chance boundaries' one handling of a failure (#10).</summary>
    private FailureBoundary? _failures;

    /// <summary>
    /// The notification-area icon (#16 slice 7), or <see langword="null"/> when
    /// Windows refused its window. Removed first when the window closes.
    /// </summary>
    private TrayIcon? _tray;

    /// <summary>The workspace window, for the session's end to save its unsaved text.</summary>
    private MainWindow? _main;

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

        // #7: the log exists before the database and settings, so their
        // failures reach it. It writes only typed events (IStorageLog) at
        // Information until settings are read; creating it does no I/O, so a
        // second launch that hands off below costs nothing here. The Trace
        // lines elsewhere are a separate channel: the runtime validation
        // harness reads them.
        var log = new RollingFileLog(paths.LogsDirectory);

        // #10: the last-chance boundaries. The first failure is recorded, the
        // open note saved once and the application ended through its one
        // shutdown transition; any later one, on any thread, does nothing
        // (FailureBoundary). Not fatal in .NET, an unobserved task is only
        // recorded.
        _failures = new FailureBoundary(log, SaveOnFailure, ExitAfterFailure);
        UiWork.OnFailure = exception => _failures.Fail(UnhandledSource.UiThread, exception);
        UnhandledException += (_, e) =>
        {
            // Handled here, so WinUI does not tear the process down mid-save:
            // ExitAfterFailure ends it once the failure is recorded.
            e.Handled = true;
            _failures.Fail(UnhandledSource.UiThread, e.Exception);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception exception)
            {
                _failures.Fail(UnhandledSource.OtherThread, exception);
            }
        };
        TaskScheduler.UnobservedTaskException += (_, e) => log.UnobservedTaskException(ExceptionSummary.From(e.Exception));

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

        var database = new NotoDatabase(paths.DatabaseFile, log);

        // Creates the file on first run and applies any pending migration. A
        // database that cannot be opened or brought up to date is refused with
        // a message naming the file, never a crash (#10); the log records
        // only why.
        try
        {
            database.Initialize();
        }
        catch (StorageException ex)
        {
            log.StartupFailed(ex.Reason, ExceptionSummary.From(ex));
            StartupNotice.Show(StartupFailureMessage.For(ex.Reason, paths.DatabaseFile));
            ReleaseResources();
            Environment.Exit(StartupFailedExitCode);
            return;
        }

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
        var settings = new SqliteSettingsStore(database, log);
        settings.Load();

        log.Level = settings.Read(SettingKeys.DiagnosticsLogLevel);
        settings.SettingChanged += (_, e) =>
        {
            if (e.Key == SettingKeys.DiagnosticsLogLevel)
            {
                log.Level = settings.Read(SettingKeys.DiagnosticsLogLevel);
            }
        };

        // Phase 1 (architecture-overview.md §startup): the hotkey is
        // registered before any window exists, on its own message-only
        // window, so it does not depend on the workspace window's lifetime.
        _hotkey = StartHotkey(settings);

        // One clock for every handler: two SystemClock reads inside a single
        // user action could straddle a tick and stamp two rows differently.
        IClock clock = SystemClock.Instance;

        var preferences = new WorkspacePreferences(settings);

        var main = new MainWindow(
            new ListFoldersQuery(folders),
            new CreateFolderHandler(folders, clock),
            new RenameFolderHandler(folders, clock),
            new ListNotesInFolderQuery(notes),
            new GetNoteQuery(notes),
            new UpdateNoteContentHandler(notes, clock),
            new CreateNoteHandler(notes, clock),
            new DeleteNoteHandler(notes, clock))
        {
            Escape = preferences.Escape,
        };

        // #16 slice 6: the window's own ways of putting itself away go to the
        // coordinator, like every other request; the window never hides
        // itself. Pin changes only the coordinator's session state.
        main.DismissRequested += (_, _) => OnDismissRequested();
        main.PinChanged += (_, pinned) =>
        {
            if (_coordinator is not null)
            {
                _coordinator.Pinned = pinned;
            }
        };
        _window = main;
        _main = main;

        _windowHandle = WindowHandle.FromHwnd(WindowNative.GetWindowHandle(_window));

        // The one end of the application (ADR-014): Alt+F4, the taskbar's
        // Close, the tray's Quit and the session ending all arrive here,
        // through AppWindow.Closing. Raised once, when the window is
        // destroyed; every disposal below is idempotent besides.
        _window.Closed += (_, _) => ReleaseResources();

        // After MainWindow's own Closing handler, which may cancel the close
        // to keep unsaved text: only a close that goes ahead stops activation.
        // From here a later launch is told Noto is shutting down, and waits to
        // take over instead of handing its request to a closing window.
        //
        // The one shutdown transition (ADR-014), entered once. A second close
        // request can raise Closing again before the window is gone — measured
        // under Restart Manager, which sends its own WM_CLOSE after the one the
        // session's end posts — and must change nothing.
        _window.AppWindow.Closing += (_, e) =>
        {
            if (!e.Cancel)
            {
                BeginShutdownOnce();
            }
        };

        _window.Activate();

        // After Activate: the inset is read from DWM's frame bounds, which
        // describe what is drawn, so they are read once the window is shown.
        // Reading them earlier gave the same result here, but is not relied
        // on. The window may appear at its default position for a moment.
        DockAtLaunch(_window, preferences);

        // Topmost is a shown-state invariant (parity A8), independent of
        // whether docking succeeded: an undocked window is still the
        // workspace the user summoned.
        WindowCoordinator.EnsureTopmost(_window);

        if (_docked is not null)
        {
            _coordinator = new WindowCoordinator(_window, _windowHandle, _docked, preferences, main.SaveBeforeHide, preferences.HideOnDeactivation);
            _docked.AppActivationChanged += (_, e) => OnAppActivationChanged(e);
        }

        // After the coordinator, so every callback finds the wiring final.
        // Callbacks are dispatched only after OnLaunched returns, so one made
        // during startup meets the startup rule (it never hides).
        StartTray();

        // Lets a scripted or CI run verify startup without a human closing the
        // window. Not a product feature.
        if (commandLine.Contains("--smoke-test"))
        {
            _ = _window.DispatcherQueue.TryEnqueueGuarded(Exit);
        }

#if DEBUG
        // Debug builds only (never Release): a fault on demand, so the
        // failure path (#10) can be checked against the real application.
        ArmFaultInjection(_window.DispatcherQueue, paths.Root);
#endif

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
        if (_pendingLaunch.Offer(Environment.TickCount) && !ui.TryEnqueueGuarded(RunPendingLaunch))
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
    /// Adds the notification-area icon (#16 slice 7, ADR-014). A refusal is
    /// recorded, not fatal: Noto runs without the icon, and without session-end
    /// saving, as before this slice.
    /// </summary>
    /// <remarks>
    /// <c>Trace</c> lines prefixed <c>Noto.Tray</c> and <c>Noto.Session</c>
    /// name outcomes only, never content, for the runtime harness.
    /// </remarks>
    private void StartTray()
    {
        try
        {
            _tray = TrayIcon.Create("Noto");
        }
        catch (Win32Exception ex)
        {
            Trace.WriteLine($"Noto.Tray unavailable error={ex.NativeErrorCode}");
            return;
        }

        TrayIcon tray = _tray;
        tray.Invoked += (_, e) => OnTrayInvoked(e.ReceivedAt);
        tray.MenuRequested += (_, e) => OnTrayMenuRequested(tray, e);
        tray.SessionEnding += (_, e) => OnSessionEnding(e);
        tray.SessionEnded += (_, e) => OnSessionEnded(e.Ending);
        tray.Readded += (_, _) => Trace.WriteLine($"Noto.Tray readded added={tray.IsAdded} error={tray.LastError}");
        Trace.WriteLine($"Noto.Tray added={tray.IsAdded} error={tray.LastError}");
    }

    /// <summary>
    /// The icon was selected — a click, Enter or Space: a launch (ADR-014).
    /// </summary>
    /// <remarks>
    /// Not the toggle. On Windows 11 a new icon sits in the overflow, and
    /// opening the overflow takes activation from Noto before the click is
    /// delivered (measured): an in-front Noto is never in front when the click
    /// arrives, so the toggle's "hide" could not happen. A launch shows,
    /// restores or brings Noto forward and never hides it; a second select of
    /// a double click finds it shown and does nothing.
    /// </remarks>
    private void OnTrayInvoked(int receivedAt) => ActivateFromTray(receivedAt, "invoked");

    /// <summary>A launch from the icon, through the one coordinator — or, without one, the hotkey's fallback.</summary>
    private void ActivateFromTray(int requestTime, string source)
    {
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

        Trace.WriteLine($"Noto.Tray {source} action={action}");
    }

    /// <summary>
    /// The icon's menu (parity A6). Where the workspace is, is read before the
    /// menu takes the foreground; what a choice does is decided again when it
    /// is made.
    /// </summary>
    private void OnTrayMenuRequested(TrayIcon tray, TrayMenuRequestedEventArgs request)
    {
        if (_closing || _windowHandle is null)
        {
            return;
        }

        TrayMenuState state = TrayMenu.For(WindowActivation.PresenceOf(_windowHandle), _coordinator?.Pinned ?? false, _coordinator is not null);

        tray.ShowMenu(
            request,
            [
                new(TrayCommand.Show, "&Show Noto", state.ShowEnabled),
                new(TrayCommand.Hide, "&Hide Noto", state.HideEnabled),
                new(TrayCommand.KeepOpen, "&Keep Noto open", state.KeepOpenEnabled, state.KeepOpenChecked, SeparatorBefore: true),
                new(TrayCommand.Quit, "&Quit Noto", Enabled: true, SeparatorBefore: true),
            ],
            OnTrayCommand);
    }

    /// <summary>A menu choice, each through the path that already owns it.</summary>
    private void OnTrayCommand(TrayCommand command)
    {
        if (_closing)
        {
            return;
        }

        Trace.WriteLine($"Noto.Tray command={command}");

        switch (command)
        {
            case TrayCommand.Show:
                ActivateFromTray(Environment.TickCount, "show");
                break;

            case TrayCommand.Hide:
                OnDismissRequested();
                break;

            case TrayCommand.KeepOpen:
                // Through the header's toggle, the one pin path: it raises
                // PinChanged, which sets the coordinator's pin.
                if (_coordinator is not null)
                {
                    _main?.SetPinned(!_coordinator.Pinned);
                }

                break;

            case TrayCommand.Quit:
                Quit();
                break;
        }
    }

    /// <summary>
    /// Quit: the close Alt+F4 makes, through <c>AppWindow.Closing</c> — one
    /// save, a failure cancels, a second close with the same text discards.
    /// </summary>
    /// <remarks>
    /// <c>WM_CLOSE</c> is sent, so Closing — and Closed, if the close went ahead
    /// — have run when it returns (measured). A close a failed save cancelled
    /// leaves Noto open: it is then shown as a launch would, so its notice can
    /// be read even if Noto was minimized. A hidden Noto is never unsaved:
    /// every hide saves first.
    /// </remarks>
    /// <summary>
    /// The one shutdown transition (ADR-014), entered once: from a close that
    /// goes ahead, or from the failure path (#10). From here a later launch is
    /// told Noto is shutting down, and waits to take over instead of handing
    /// its request to a closing window.
    /// </summary>
    private void BeginShutdownOnce()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        _activationPipe?.BeginShutdown();
        _coordinator?.BeginShutdown();
        Trace.WriteLine($"Noto.Instance shutting-down key={_instanceKey?.PathHash}");
    }

    /// <summary>
    /// What the process holds, released when it ends: when the window is
    /// destroyed, or on the failure and startup-failure paths. Every disposal
    /// is idempotent.
    /// </summary>
    private void ReleaseResources()
    {
        // The icon first: before the mutex is released, so a successor's
        // icon never appears beside this one.
        _tray?.Dispose();
        _hotkey?.Dispose();

        // The pipe first, then the mutex: a successor waiting on the
        // mutex must find the pipe's name free when it gets it.
        Trace.WriteLine($"Noto.Instance released key={_instanceKey?.PathHash}");
        _activationPipe?.Dispose();
        _ownership?.Dispose();
    }

    /// <summary>
    /// #10: the editor's own save, once, after a failure. On the UI thread it
    /// runs directly; from another thread it is handed to the UI thread and
    /// waited on for a bounded time, so a UI thread that is itself stuck
    /// cannot hold the process open.
    /// </summary>
    private bool SaveOnFailure()
    {
        if (_main is not { } main || _window?.DispatcherQueue is not { } ui)
        {
            return true;
        }

        if (ui.HasThreadAccess)
        {
            return main.SaveBeforeHide();
        }

        bool saved = false;
        using var done = new ManualResetEventSlim();

        bool queued = ui.TryEnqueueGuarded(() =>
        {
            try
            {
                saved = main.SaveBeforeHide();
            }
            finally
            {
                done.Set();
            }
        });

        return queued && done.Wait(FailureSaveTimeout) && saved;
    }

    /// <summary>
    /// #10: the end after a failure: the one shutdown transition, the same
    /// release the window's Closed runs, then the exit code that says why.
    /// </summary>
    private void ExitAfterFailure()
    {
        BeginShutdownOnce();
        ReleaseResources();
        Environment.Exit(FailedExitCode);
    }

#if DEBUG
    private static DispatcherTimer? _faultTimer;

    /// <summary>
    /// Debug builds only. A file named <c>noto-fault-ui</c> or
    /// <c>noto-fault-thread</c> in the data root throws on the UI thread or on
    /// another thread, once; the file is deleted first. Not compiled into
    /// Release.
    /// </summary>
    private static void ArmFaultInjection(DispatcherQueue ui, string dataRoot)
    {
        // A XAML timer, so a fault thrown in its Tick is a XAML callback's (Application.UnhandledException). Held
        // in a field: a timer only a local refers to can be collected and stop ticking.
        var timer = _faultTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) =>
        {
            string onThread = Path.Join(dataRoot, "noto-fault-thread");
            string onQueue = Path.Join(dataRoot, "noto-fault-queue");
            string onUi = Path.Join(dataRoot, "noto-fault-ui");

            if (File.Exists(onThread))
            {
                File.Delete(onThread);
                new Thread(() => throw new InvalidOperationException("Injected fault on another thread (debug build).")) { IsBackground = true }.Start();
            }

            if (File.Exists(onQueue))
            {
                File.Delete(onQueue);
                _ = ui.TryEnqueueGuarded(() => throw new InvalidOperationException("Injected fault in a queued callback (debug build)."));
            }

            if (File.Exists(onUi))
            {
                File.Delete(onUi);
                throw new InvalidOperationException("Injected fault on the UI thread (debug build).");
            }
        };
        timer.Start();
    }
#endif

    private void Quit()
    {
        if (_windowHandle is null)
        {
            return;
        }

        WindowClose.Request(_windowHandle);

        if (!_closing)
        {
            ActivateFromTray(Environment.TickCount, "quit-cancelled");
        }
    }

    /// <summary>
    /// Windows asks whether the session may end (ADR-014): unsaved text is
    /// saved now, synchronously. A failure refuses a non-critical end, with a
    /// reason Windows shows; nothing is discarded silently.
    /// </summary>
    /// <remarks>
    /// Nothing changes state here: the session's end may still be cancelled.
    /// </remarks>
    private void OnSessionEnding(SessionEndingEventArgs e)
    {
        bool saved = _main?.SaveBeforeHide() ?? true;
        Trace.WriteLine($"Noto.Session query critical={e.Critical} saved={saved}");

        if (!saved && _windowHandle is not null)
        {
            e.Block(_windowHandle, "Noto could not save a note. Cancel to keep it.");
        }
    }

    /// <summary>
    /// The session ends after all: a last synchronous save — for text typed
    /// after the query was answered — then the one close path. Shutdown begins
    /// only in <c>AppWindow.Closing</c>; a later or duplicate close request
    /// (Restart Manager sends its own) finds the window gone and does nothing.
    /// </summary>
    private void OnSessionEnded(bool ending)
    {
        Trace.WriteLine($"Noto.Session ended ending={ending}");

        if (!ending || _closing || _windowHandle is null)
        {
            return;
        }

        bool saved = _main?.SaveBeforeHide() ?? true;
        Trace.WriteLine($"Noto.Session saved={saved}");
        WindowClose.Post(_windowHandle);
    }

    /// <summary>
    /// <c>Ctrl+W</c>, or Escape where its behaviour hides (#16 slice 6):
    /// a dismissal, through the coordinator, which saves unsaved text first.
    /// Without a coordinator (docking failed) nothing hides — as the hotkey
    /// never hides there either.
    /// </summary>
    private void OnDismissRequested() =>
        _ = _coordinator?.OnActivationRequested(WorkspaceRequest.Dismiss, Environment.TickCount);

    /// <summary>
    /// Activation crossed Noto's process boundary (#16 slice 6; parity A13,
    /// "close on outside click").
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Activation loss, not a click.</b> Parity's "outside click" is
    /// implemented as <c>WM_ACTIVATEAPP</c>: activation moving to another
    /// application — by a click, Alt+Tab, a newly launched window, the Start
    /// menu or a virtual-desktop switch. Windows never sends it for a move to
    /// one of Noto's own windows (a context menu, an IME window).
    /// </para>
    /// <para>
    /// <b>Recorded now, decided later</b> (ADR-007 §4, "The activation
    /// generation"): each change is posted, not carried out inside the
    /// activation change, and carries the generation it was made in. A later
    /// change, or Noto bringing itself forward, before it is decided makes it
    /// stale.
    /// </para>
    /// <para>
    /// <b>Never hidden and in the foreground.</b> An activation that landed —
    /// Noto already the foreground window at the message, as the event
    /// observed it then — shows a hidden workspace, as a launch would. One that
    /// did not land (another process's refused foreground request) changes
    /// nothing.
    /// </para>
    /// </remarks>
    private void OnAppActivationChanged(AppActivationEventArgs change)
    {
        if (_coordinator is null || _window is null)
        {
            return;
        }

        int changedAt = Environment.TickCount;

        if (change.Active)
        {
            int returned = _coordinator.OnAppActivated();
            bool landed = change.Landed;

            _ = _window.DispatcherQueue.TryEnqueueGuarded(() =>
            {
                if (_closing || _coordinator is null)
                {
                    return;
                }

                _ = _coordinator.OnActivationReturned(returned, landed, changedAt);
            });

            return;
        }

        int generation = _coordinator.OnAppDeactivated();

        _ = _window.DispatcherQueue.TryEnqueueGuarded(() =>
        {
            if (_closing || _coordinator is null)
            {
                return;
            }

            _ = _coordinator.OnDeactivationRequested(generation, changedAt);
        });
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
    /// let the user drag the window off its edge. Closing it is Alt+F4, the
    /// taskbar menu or the tray's Quit (#16 slice 7).
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
