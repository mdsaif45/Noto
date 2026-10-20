using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Noto.Core;
using Noto.Core.Activation;
using Noto.Core.Settings;
using Noto.Core.Workspace;
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
/// <para>
/// <b>Run at login</b> (#94, ADR-015): with <c>--login</c> Noto starts hidden —
/// it takes the data root only if it is free, never hands off, and never
/// activates the window, so nothing is shown until the user asks.
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
    /// Run at launch, and again when Settings turns the hotkey on (Slice 1).
    /// The binding is valid by construction: its setting's validity
    /// rule is the <see cref="HotkeyChord"/> grammar, so a malformed stored
    /// value has already fallen back to the default.
    /// </para>
    /// <para>
    /// <b>A refused chord never stops startup.</b> Taken by another process,
    /// reserved by Windows, or refused for any other reason: Noto runs without
    /// the hotkey, no other chord is tried, the stored setting is left alone,
    /// and nothing is retried until the next launch or until Settings turns it
    /// off and on. The refusal is reported in Settings (Slice 1) as well as
    /// written as a diagnostic.
    /// </para>
    /// </remarks>
    /// <param name="settings">The store the hotkey settings are read from.</param>
    /// <param name="refusal">Why the chord was not registered, for Settings; <see langword="null"/> when it was, or is off.</param>
    private GlobalHotkey? StartHotkey(SqliteSettingsStore settings, out string? refusal)
    {
        refusal = null;

        if (!settings.Read(SettingKeys.HotkeyEnabled))
        {
            return null;
        }

        HotkeyChord chord = HotkeyChord.Parse(settings.Read(SettingKeys.HotkeyBinding));
        (HotkeyRegistration registration, GlobalHotkey? hotkey) = RegisterHotkey(chord);

        if (hotkey is null)
        {
            string shown = HotkeyRebinding.Show(chord);
            refusal = registration.Status == HotkeyRegistrationStatus.Conflict
                ? $"{shown} is in use by another app, so this shortcut is not working. Noto tries again each time it starts, or when you turn the shortcut off and on."
                : $"Windows did not accept {shown}, so this shortcut is not working.";
        }

        return hotkey;
    }

    /// <summary>
    /// Registers <paramref name="chord"/> on a new <see cref="GlobalHotkey"/>, wired to the toggle: the one place a
    /// hotkey registration is made, at start-up and when Settings rebinds (one instance holds one chord, so a rebind
    /// holds the old one on its instance until the new one is registered on another).
    /// </summary>
    /// <returns>The registration, and its owner when it was registered; <see langword="null"/> when refused, with nothing left behind.</returns>
    private (HotkeyRegistration Registration, GlobalHotkey? Hotkey) RegisterHotkey(HotkeyChord chord)
    {
        GlobalHotkey? hotkey = null;

        try
        {
            hotkey = GlobalHotkey.Create();
            HotkeyRegistration registration = hotkey.Register(chord);

            if (!registration.IsRegistered)
            {
                Debug.WriteLine($"The global hotkey {chord} was not registered: {registration.Status} ({registration.ErrorCode})");
                return (registration, null);
            }

            hotkey.Pressed += (_, e) => OnHotkeyPressed(e.MessageTime);

            // Ownership passes to the caller; the finally below must not dispose it.
            GlobalHotkey registered = hotkey;
            hotkey = null;
            return (registration, registered);
        }
        catch (Win32Exception ex)
        {
            Debug.WriteLine($"The global hotkey could not be set up: {ex.NativeErrorCode}");
            return (new HotkeyRegistration(HotkeyRegistrationStatus.Failed, ex.NativeErrorCode), null);
        }
        finally
        {
            hotkey?.Dispose();
        }
    }

    /// <summary>
    /// Settings' shortcut recorder is listening (Slice 1): a press of the show/hide chord while Noto is in front is
    /// ignored, so it cannot hide the Settings the user is recording in. The registration itself is kept throughout,
    /// so no other process can take the chord meanwhile.
    /// </summary>
    private bool _hotkeySuspended;

    /// <summary>Starts or ends the recorder's suspension of the hotkey (Slice 1).</summary>
    private void SuspendHotkey(bool suspended)
    {
        _hotkeySuspended = suspended;
        Trace.WriteLine($"Noto.Hotkey suspended={suspended}");
    }

    /// <summary>
    /// The chords the recorder cannot receive, observed below every application's shortcuts for one recording
    /// (Slice 1.2b): a chord another application holds is delivered to it, never to Noto's window, so it is passed to
    /// the recorder from here, to be tried and refused like any other.
    /// </summary>
    /// <remarks>
    /// Only those: a chord that can be registered now reaches the window, and every key that is not a chord does too,
    /// so each is read once, by the page, as it always was. A press of the chord Noto holds stays the hotkey's, ignored
    /// while recording (the rebind's own "same chord, registered" rule, <see cref="HotkeyRebinding"/>).
    /// </remarks>
    /// <returns>The observation, to dispose when recording ends; <see langword="null"/> when there is none.</returns>
    private RecorderKeyboard? ObserveRecorderKeys(Action<int, HotkeyModifiers> pressed, WorkspacePreferences preferences)
    {
        if (_windowHandle is null)
        {
            return null;
        }

        return RecorderKeyboard.Start(_windowHandle, (key, held) =>
        {
            if (HotkeyRecording.Read(key, held) is not (RecordedKind.Chord, HotkeyChord chord))
            {
                return;
            }

            bool own = _hotkey is { IsRegistered: true }
                && HotkeyChord.TryParse(preferences.HotkeyBinding, out HotkeyChord current)
                && chord == current;

            if (!own && !GlobalHotkey.IsFree(chord))
            {
                pressed(key, held);
            }
        });
    }

    /// <summary>
    /// Rebinds the show/hide hotkey from Settings' recorder (Slice 1): the new chord registered before the old one is
    /// released, then stored; refused, the old one stays registered and stored (<see cref="HotkeyRebinding"/>).
    /// </summary>
    /// <returns>How it ended, and what Settings announces.</returns>
    private (HotkeyRebindOutcome Outcome, string Message) RebindHotkey(HotkeyChord candidate, WorkspacePreferences preferences)
    {
        HotkeyChord current = HotkeyChord.Parse(preferences.HotkeyBinding);

        if (!preferences.HotkeyEnabled)
        {
            return (HotkeyRebindOutcome.Off, HotkeyRebinding.Describe(HotkeyRebindOutcome.Off, HotkeyRegistrationStatus.Failed, candidate, current));
        }

        HotkeyRebindResult<GlobalHotkey> result = HotkeyRebinding.Rebind(
            candidate,
            current,
            _hotkey,
            RegisterHotkey,
            static old => old.Dispose(),
            preferences.SetHotkeyBinding);

        _hotkey = result.Active;

        // A chord now registered clears "the shortcut is not working"; a refusal leaves whatever was true before.
        if (_hotkey is not null)
        {
            _main?.ShowHotkeyMessage(null);
        }

        // The chords, never anything typed into a note: evidence for the settings harness.
        Trace.WriteLine($"Noto.Hotkey rebind outcome={result.Outcome} refusal={result.Refusal} from={current} to={candidate} registered={_hotkey?.IsRegistered ?? false}");
        return (result.Outcome, HotkeyRebinding.Describe(result.Outcome, result.Refusal, candidate, current));
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
        // Only while Noto is in front: a hidden or background Noto is never left without its shortcut, whatever
        // state the recorder is in.
        if (_hotkeySuspended && _windowHandle is not null && WindowActivation.PresenceOf(_windowHandle) == WorkspacePresence.Foreground)
        {
            Trace.WriteLine("Noto.Hotkey pressed ignored=recording");
            return;
        }

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

    /// <summary>
    /// The notification-area icon (#16 slice 7), or <see langword="null"/> when
    /// Windows refused its window. Removed first when the window closes.
    /// </summary>
    private TrayIcon? _tray;

    /// <summary>The workspace window, for the session's end to save its unsaved text.</summary>
    private MainWindow? _main;

    /// <summary>
    /// This executable's registration to start at sign-in (#94, ADR-015), read
    /// from Windows each time; <see langword="null"/> when the executable's path
    /// is unknown.
    /// </summary>
    private LoginRegistration? _login;

    public App()
    {
        InitializeComponent();

        // High contrast (ADR-011): the design system gives every text and fill its own system colour in the
        // HighContrast theme, and a selected row is HighlightText on Highlight. Left on, the framework's automatic
        // adjustment redraws text over a backplate in the window colour, so that row's text came out white on black
        // inside the Highlight fill (measured). Set before any window exists; every element defers to it
        // (ElementHighContrastAdjustment.Application, its default), the settings page created later included.
        HighContrastAdjustment = ApplicationHighContrastAdjustment.None;
    }

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

        // ADR-015: started by Windows at sign-in. A hidden start: nothing is
        // shown until the user asks, and a running Noto is left alone.
        bool login = commandLine.Contains(LoginRegistration.LoginSwitch);
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
        if (!(login ? ClaimForLogin(paths) : ClaimDataRoot(paths)))
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
        _hotkey = StartHotkey(settings, out string? hotkeyRefusal);

        // One clock for every handler: two SystemClock reads inside a single
        // user action could straddle a tick and stamp two rows differently.
        IClock clock = SystemClock.Instance;

        var preferences = new WorkspacePreferences(settings);

        // Start with Windows (#94): Windows' own registration, read and set by the tray and by Settings alike, so
        // there is one source of truth and it is Windows'.
        _login = Environment.ProcessPath is string executable ? LoginRegistration.ForCurrentUser(executable) : null;

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
            SingleClickOpen = preferences.SingleClickOpen,
            Settings = new SettingsContext(
                preferences,
                () => _login?.State(),
                SetLoginStartup,
                () => ShellLaunch.OpenFolder(paths.Root),
                ShellLaunch.OpenStartupAppsSettings,
                chord => RebindHotkey(chord, preferences),
                SuspendHotkey,
                pressed => ObserveRecorderKeys(pressed, preferences)),
        };

        // The theme before the first frame, so a chosen theme is never preceded by Windows' own.
        ApplyTheme(main, preferences.Theme);

        // A chord refused at launch is reported where the user can act on it: in Settings (ADR-007).
        main.ShowHotkeyMessage(hotkeyRefusal);

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
        _window.Closed += (_, _) =>
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
        };

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
            if (!e.Cancel && !_closing)
            {
                _closing = true;
                _activationPipe?.BeginShutdown();
                _coordinator?.BeginShutdown();
                Trace.WriteLine($"Noto.Instance shutting-down key={_instanceKey?.PathHash}");
            }
        };

        // A login start never activates the window: it is created, docked and
        // wired, but nothing is presented until the first toggle or launch
        // shows it (measured, #94 Phase 0: no frame, never in front).
        if (!login)
        {
            _window.Activate();
        }

        // After Activate: the inset is read from DWM's frame bounds, which
        // describe what is drawn, so they are read once the window is shown.
        // A hidden (login) start docks a window never shown; every show
        // re-docks against the display as it is then.
        // Reading them earlier gave the same result here, but is not relied
        // on. The window may appear at its default position for a moment.
        DockAtLaunch(_window, preferences);

        // Topmost is a shown-state invariant (parity A8), independent of
        // whether docking succeeded: an undocked window is still the
        // workspace the user summoned.
        WindowCoordinator.EnsureTopmost(_window);

        if (_docked is not null)
        {
            _coordinator = new WindowCoordinator(_window, _windowHandle, _docked, preferences, main.SaveBeforeHide)
            {
                HideOnDeactivation = preferences.HideOnDeactivation,
            };
            _docked.AppActivationChanged += (_, e) => OnAppActivationChanged(e);
        }

        // Slice 1: a preference changed in Settings reaches the part of Noto that owns it, at once. After every owner
        // exists, so each change finds its owner wired.
        settings.SettingChanged += (_, e) => OnSettingChanged(e.Key, settings, preferences);

        // After the coordinator, so every callback finds the wiring final.
        // Callbacks are dispatched only after OnLaunched returns, so one made
        // during startup meets the startup rule (it never hides).
        StartTray();

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

    /// <summary>
    /// The claim of a login start (#94, ADR-015): ownership if the data root
    /// is free, and nothing else.
    /// </summary>
    /// <remarks>
    /// Never a handoff — handing off would show the running workspace, and a
    /// login start must never show anything. Never a message box at sign-in.
    /// Already owned: exit 0, leaving the owner as it is. Elevated: never owns
    /// (ADR-007 §8); exit 3, silently.
    /// </remarks>
    private bool ClaimForLogin(NotoStoragePaths paths)
    {
        _instanceKey = InstanceKey.ForDataRoot(paths.Root);
        bool elevated = ProcessIdentity.IsElevated();

        try
        {
            _ownership = elevated ? null : InstanceOwnership.TryAcquire(_instanceKey);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or WaitHandleCannotBeOpenedException)
        {
            _ownership = null;
        }

        Trace.WriteLine($"Noto.Instance login-claim owner={_ownership is not null} elevated={elevated} key={_instanceKey.PathHash}");

        if (_ownership is not null)
        {
            return true;
        }

        Environment.Exit(elevated ? 3 : 0);
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

        // Read from Windows as the menu opens: the registration may have been
        // removed or turned off elsewhere since the last time.
        LoginStartupRow login = LoginStartupRow.For(_login?.State() ?? LoginStartupState.Off);

        tray.ShowMenu(
            request,
            [
                new(TrayCommand.Show, "&Show Noto", state.ShowEnabled),
                new(TrayCommand.Hide, "&Hide Noto", state.HideEnabled),
                new(TrayCommand.KeepOpen, "&Keep Noto open", state.KeepOpenEnabled, state.KeepOpenChecked, SeparatorBefore: true),
                new(
                    TrayCommand.StartWithWindows,
                    login.TurnedOffInWindows ? "Start Noto with &Windows (turned off in Windows Startup apps)" : "Start Noto with &Windows",
                    login.Enabled && _login is not null,
                    login.Checked),
                new(TrayCommand.Settings, "S&ettings…", Enabled: true),
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

            case TrayCommand.StartWithWindows:
                ToggleLoginStartup();
                break;

            case TrayCommand.Settings:
                // Shown as a launch would (never hidden), then Settings opened in it.
                ActivateFromTray(Environment.TickCount, "settings");
                _main?.OpenSettings();
                break;

            case TrayCommand.Quit:
                Quit();
                break;
        }
    }

    /// <summary>
    /// Start Noto with Windows (#94, ADR-015): flips the registration, judged on
    /// what Windows has now, not on what the menu showed.
    /// </summary>
    /// <remarks>
    /// Off -> registered; on -> removed. Turned off in Windows' Startup apps:
    /// nothing — Noto never changes that setting, and never reports it as on.
    /// The state read back after the write is the one recorded.
    /// </remarks>
    private void ToggleLoginStartup()
    {
        if (_login is null)
        {
            return;
        }

        LoginStartupState before = _login.State();
        LoginStartupState after = before switch
        {
            LoginStartupState.Off => _login.Enable(),
            LoginStartupState.On => _login.Disable(),
            _ => before,
        };

        Trace.WriteLine($"Noto.Login before={before} after={after}");
    }

    /// <summary>
    /// Start Noto with Windows set from Settings (Slice 1): on or off, judged on what Windows has now, through the
    /// same registration the tray flips. Turned off in Windows' Startup apps stays as it is: Noto never changes that.
    /// </summary>
    /// <returns>What Windows has afterwards; <see langword="null"/> when there is no registration to change.</returns>
    private LoginStartupState? SetLoginStartup(bool on)
    {
        if (_login is null)
        {
            return null;
        }

        LoginStartupState before = _login.State();
        LoginStartupState after = (before, on) switch
        {
            (LoginStartupState.Off, true) => _login.Enable(),
            (LoginStartupState.On, false) => _login.Disable(),
            _ => before,
        };

        Trace.WriteLine($"Noto.Login before={before} after={after} source=settings");
        return after;
    }

    /// <summary>
    /// A stored preference changed (Slice 1): its owner applies it now. Every preference Settings writes has exactly
    /// one owner here; the width, which the dock writes for itself, has none.
    /// </summary>
    /// <remarks>
    /// The store raises the change on the thread that wrote it. Settings writes on the UI thread, which owns every
    /// owner below; a write from anywhere else is handed to it.
    /// </remarks>
    private void OnSettingChanged(SettingKey key, SqliteSettingsStore settings, WorkspacePreferences preferences)
    {
        if (_window is null || _closing)
        {
            return;
        }

        if (!_window.DispatcherQueue.HasThreadAccess)
        {
            _ = _window.DispatcherQueue.TryEnqueue(() => OnSettingChanged(key, settings, preferences));
            return;
        }

        if (key == SettingKeys.AppearanceTheme && _main is not null)
        {
            ApplyTheme(_main, preferences.Theme);
        }
        else if (key == SettingKeys.WorkspaceEdge)
        {
            _coordinator?.ChangeEdge(DockEdges.From(preferences.Edge));
        }
        else if (key == SettingKeys.WorkspaceEscape && _main is not null)
        {
            _main.Escape = preferences.Escape;
        }
        else if (key == SettingKeys.WorkspaceHideOnDeactivation && _coordinator is not null)
        {
            _coordinator.HideOnDeactivation = preferences.HideOnDeactivation;
        }
        else if (key == SettingKeys.WorkspaceSingleClickOpen && _main is not null)
        {
            _main.SingleClickOpen = preferences.SingleClickOpen;
        }
        else if (key == SettingKeys.HotkeyEnabled)
        {
            ApplyHotkeyEnabled(settings, preferences.HotkeyEnabled);
        }
        else
        {
            return;
        }

        // The key, never a value a user typed: evidence for the settings harness.
        Trace.WriteLine($"Noto.Settings applied key={key.Name}");
    }

    /// <summary>
    /// The workspace's theme (Slice 1, parity J21), set on the window's content: every brush is declared per theme
    /// (ADR-011), so the whole tree, popups included, follows it. High contrast, when on, overrides all three.
    /// </summary>
    private static void ApplyTheme(MainWindow window, WorkspaceTheme theme)
    {
        if (window.Content is not FrameworkElement root)
        {
            return;
        }

        root.RequestedTheme = theme switch
        {
            WorkspaceTheme.Light => ElementTheme.Light,
            WorkspaceTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };

        Trace.WriteLine($"Noto.Settings theme={theme} actual={root.ActualTheme}");
    }

    /// <summary>
    /// The show/hide hotkey turned on or off in Settings (Slice 1): registered or released now, through the same
    /// start-up path. A chord that cannot be registered is reported in Settings; the stored choice stays the user's.
    /// </summary>
    private void ApplyHotkeyEnabled(SqliteSettingsStore settings, bool enabled)
    {
        string? refusal = null;

        if (!enabled)
        {
            _hotkey?.Dispose();
            _hotkey = null;
        }
        else if (_hotkey is null)
        {
            _hotkey = StartHotkey(settings, out refusal);
        }

        _main?.ShowHotkeyMessage(refusal);
        Trace.WriteLine($"Noto.Hotkey enabled={enabled} registered={_hotkey?.IsRegistered ?? false}");
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

            _ = _window.DispatcherQueue.TryEnqueue(() =>
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

        _ = _window.DispatcherQueue.TryEnqueue(() =>
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
