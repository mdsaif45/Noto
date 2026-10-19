using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Noto.Platform.Windows;
using Noto.UseCases.Workspace;
using Windows.Graphics;

namespace Noto;

/// <summary>
/// The workspace window's show/hide lifecycle (#16 slice 5, ADR-007 §4).
/// </summary>
/// <remarks>
/// <para>
/// The one place activation requests are carried out. A request is judged by
/// <see cref="WorkspaceToggle.Decide"/> against where the window is now and
/// what the coordinator knows (startup finished, a transition running, the
/// window closing), then carried out synchronously. Requests arrive on the UI
/// thread — from the hotkey's message, from a second launch handed over
/// by the activation pipe (A17, ADR-013), from <c>Ctrl+W</c> or Escape, or
/// from activation leaving Noto's process (#16 slice 6) — one at a time, so they
/// are serialized by construction; nothing is queued here, and a request that
/// cannot run now is dropped.
/// </para>
/// <para>
/// <b>Topmost while shown</b> (#16 slice 6, parity A8). Every path that ends
/// shown asserts it, so it holds whatever happened to the window before.
/// </para>
/// <para>
/// <b>The activation generation</b> (#16 slice 6, ADR-007 §4 "The drawer").
/// <c>WM_ACTIVATEAPP</c> in either direction and every path that brings the
/// window forward advance it, so a deactivation or activation request decided
/// later is carried out only while it still holds.
/// </para>
/// <para>
/// <b>Never hidden and in the foreground</b> (#16 slice 6). When an
/// activation lands on a hidden workspace — it is the foreground window at the
/// activation message — it is shown with a launch's rules, and a minimized one
/// restored. A shown workspace is left alone, and so is one whose activation
/// did not land (another process's refused foreground request).
/// </para>
/// <para>
/// Deliberately small: it owns this lifecycle for the one window that
/// exists, not window creation in general. The coordinator architecture
/// describes is not built (architecture-overview, implementation status).
/// </para>
/// </remarks>
internal sealed class WindowCoordinator(
    Window window,
    WindowHandle handle,
    DockedWindow docked,
    WorkspacePreferences preferences,
    Func<bool> saveBeforeHide,
    ContentMotion motion)
{
    private int _readyTime = Environment.TickCount;
    private int? _lastTransitionEnd;
    private bool _transitioning;
    private bool _shuttingDown;
    private bool _pinned;
    private readonly ActivationGeneration _activation = new();

    // Slice 1.3: a hide whose content is still leaving. Its state is HideTransition's; WorkspaceToggle reads it.
    private readonly HideTransition _hide = new();

    /// <summary>
    /// Pinned open for this session (parity A9): losing activation no longer
    /// hides the workspace. Explicit dismissal and the hotkey still do. Never
    /// stored — every launch starts unpinned.
    /// </summary>
    public bool Pinned
    {
        get => _pinned;
        set
        {
            _pinned = value;
            Trace.WriteLine($"Noto.Workspace pinned={value}");
        }
    }

    /// <summary>
    /// Makes the window topmost if it is not. The shown-state invariant
    /// (parity A8): called at launch, whether or not docking succeeded, and on
    /// every path that ends shown. ADR-007 §1: the managed presenter property,
    /// never <c>SetWindowPos(HWND_TOPMOST)</c>.
    /// </summary>
    /// <summary>
    /// Whether losing activation hides the shown workspace (parity A13), as stored. Set by the composition root at
    /// launch and whenever the setting changes (Slice 1); read at each deactivation, so a change governs the next one.
    /// </summary>
    public bool HideOnDeactivation { get; set; } = true;

    /// <summary>
    /// Docks to <paramref name="edge"/> from now on (Slice 1: the side, changed in settings). A shown or behind
    /// workspace moves now, through the same re-dock a show uses; a hidden or minimized one moves when it is next shown
    /// or restored, which re-docks anyway.
    /// </summary>
    public void ChangeEdge(DockEdge edge)
    {
        docked.ChangeEdge(edge);

        WorkspacePresence presence = WindowActivation.PresenceOf(handle);
        bool now = presence is WorkspacePresence.Foreground or WorkspacePresence.Background;

        if (now)
        {
            Redock();
        }

        Trace.WriteLine($"Noto.Workspace edge={edge} redocked={now}");
    }

    public static void EnsureTopmost(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (window.AppWindow.Presenter is OverlappedPresenter presenter && !presenter.IsAlwaysOnTop)
        {
            presenter.IsAlwaysOnTop = true;
        }
    }

    /// <summary>Startup has finished; requests made before now were made while Noto was launching.</summary>
    public void MarkReady() => _readyTime = Environment.TickCount;

    /// <summary>The window is closing: every request from now on is dropped.</summary>
    public void BeginShutdown() => _shuttingDown = true;

    /// <summary>
    /// Activation left Noto's process (<c>WM_ACTIVATEAPP</c>, <c>FALSE</c>):
    /// starts a new generation, which the deactivation request to post carries.
    /// </summary>
    /// <returns>The generation for <see cref="OnDeactivationRequested"/>.</returns>
    public int OnAppDeactivated()
    {
        int generation = _activation.Deactivated();
        Trace.WriteLine($"Noto.Workspace activation=left generation={generation}");
        return generation;
    }

    /// <summary>
    /// Activation came back to Noto's process (<c>WM_ACTIVATEAPP</c>, <c>TRUE</c>):
    /// starts a new generation, so every deactivation request not yet decided
    /// is stale; the activation request to post carries it.
    /// </summary>
    /// <returns>The generation for <see cref="OnActivationReturned"/>.</returns>
    public int OnAppActivated()
    {
        int generation = _activation.Activated();
        Trace.WriteLine($"Noto.Workspace activation=returned generation={generation}");
        return generation;
    }

    /// <summary>
    /// Decides an activation posted by <see cref="OnAppActivated"/>: a hidden
    /// workspace whose activation landed is shown, a minimized one restored —
    /// only while its <paramref name="generation"/> is still current.
    /// </summary>
    /// <param name="generation">The generation <see cref="OnAppActivated"/> returned.</param>
    /// <param name="landed">
    /// Noto was the foreground window when <c>WM_ACTIVATEAPP(TRUE)</c> was
    /// delivered, as observed then (<see cref="AppActivationEventArgs.Landed"/>).
    /// </param>
    /// <param name="requestTime">When activation returned, on the <see cref="Environment.TickCount"/> clock.</param>
    /// <returns>What was decided; <see cref="WorkspaceAction.None"/> when there was nothing to reconcile.</returns>
    public WorkspaceAction OnActivationReturned(int generation, bool landed, int requestTime) =>
        Handle(WorkspaceRequest.Activated, requestTime, _activation.IsCurrent(generation), $" generation={generation} current={_activation.Current} landed={landed}", landed);

    /// <summary>
    /// Decides a deactivation posted by <see cref="OnAppDeactivated"/>: it may
    /// hide only while its <paramref name="generation"/> is still current.
    /// </summary>
    /// <param name="generation">The generation <see cref="OnAppDeactivated"/> returned.</param>
    /// <param name="requestTime">When activation left, on the <see cref="Environment.TickCount"/> clock.</param>
    /// <returns>What was decided; <see cref="WorkspaceAction.None"/> when the request was dropped.</returns>
    public WorkspaceAction OnDeactivationRequested(int generation, int requestTime) =>
        Handle(WorkspaceRequest.Deactivated, requestTime, _activation.IsCurrent(generation), $" generation={generation} current={_activation.Current}");

    /// <summary>
    /// Handles one activation request: a press of the global hotkey, handled
    /// synchronously inside its <c>WM_HOTKEY</c> so Noto is entitled to take
    /// the foreground; a second launch, whose process passed Noto that right
    /// before handing the request over; an explicit dismissal; or the window
    /// losing activation.
    /// </summary>
    /// <param name="request">
    /// The toggle; a launch, which never hides; a dismissal, which never shows.
    /// A deactivation or an activation goes through <see cref="OnDeactivationRequested"/>
    /// or <see cref="OnActivationReturned"/>: one passed here carries no
    /// generation and is dropped.
    /// </param>
    /// <param name="requestTime">When the request was made, on the <see cref="Environment.TickCount"/> clock.</param>
    /// <returns>What was decided; <see cref="WorkspaceAction.None"/> when the request was dropped.</returns>
    public WorkspaceAction OnActivationRequested(WorkspaceRequest request, int requestTime) =>
        Handle(request, requestTime, generationCurrent: false, string.Empty);

    private WorkspaceAction Handle(WorkspaceRequest request, int requestTime, bool generationCurrent, string detail, bool activationLanded = false)
    {
        WorkspaceAction action = WorkspaceToggle.Decide(
            request,
            WindowActivation.PresenceOf(handle),
            new ActivationContext(
                _shuttingDown,
                docked.IsResizing,
                _transitioning,
                requestTime,
                _readyTime,
                _lastTransitionEnd,
                HideOnDeactivation: HideOnDeactivation && !_pinned,
                GenerationCurrent: generationCurrent,
                ActivationLanded: activationLanded,
                Hiding: _hide.Hiding));

        // The request and what it decided, never content: evidence for the
        // runtime harness, read through the debug-output channel.
        Trace.WriteLine($"Noto.Workspace request={request} action={action}{detail}");

        if (action == WorkspaceAction.None)
        {
            return action;
        }

        _transitioning = true;

        // Noto bringing itself forward supersedes every request not yet
        // decided — even if Windows then refuses the foreground, a launch or
        // toggle that showed the window must not be undone by an older
        // deactivation.
        if (WorkspaceToggle.BringsForward(action))
        {
            _activation.BroughtForward();
        }

        try
        {
            switch (action)
            {
                case WorkspaceAction.Hide:
                    Hide();
                    break;

                case WorkspaceAction.Show:
                    // The content at its starting place before the first frame; it moves only once the window is
                    // docked, so the whole transition runs inside the docked rectangle.
                    motion.PrepareEnter(docked.Edge);
                    window.AppWindow.Show(activateWindow: false);
                    BringForward(redock: true);
                    motion.Enter(docked.Edge, fromCurrent: false);
                    break;

                case WorkspaceAction.Restore:
                    motion.Settle();
                    BringForward(redock: true);
                    break;

                case WorkspaceAction.Focus:
                    motion.Settle();
                    BringForward(redock: false);
                    break;

                case WorkspaceAction.Reverse:
                    // A show while the content was leaving: it comes back from wherever it is; the window, never
                    // hidden, is not moved.
                    _ = _hide.Reverse();
                    motion.Enter(docked.Edge, fromCurrent: true);
                    BringForward(redock: false);
                    Trace.WriteLine("Noto.Motion reversed");
                    break;
            }
        }
        finally
        {
            _transitioning = false;
            _lastTransitionEnd = Environment.TickCount;
        }

        return action;
    }

    /// <summary>
    /// Hides the window — after saving unsaved editor text once. A failed
    /// save keeps the window shown with the editor's notice, so nothing is
    /// hidden that has not been kept.
    /// </summary>
    /// <remarks>
    /// Slice 1.3: after the save, the content leaves (unless animations are off) and the window is hidden only when
    /// it has. A show meanwhile reverses it (<see cref="WorkspaceAction.Reverse"/>).
    /// </remarks>
    private void Hide()
    {
        switch (_hide.Begin(saveBeforeHide, motion.Enabled))
        {
            case HideStep.HideNow:
                window.AppWindow.Hide();
                break;

            case HideStep.Animate:
                int generation = _hide.Generation;

                if (!motion.Exit(docked.Edge, () => OnExitCompleted(generation)))
                {
                    // Animations were turned off between the two reads: hide as if they had been off.
                    OnExitCompleted(generation);
                }

                break;
        }
    }

    /// <summary>
    /// The content has left: hide now, unless this hide was reversed meanwhile, or text typed while it left cannot be
    /// saved, which brings the content back and keeps the window shown with the editor's notice.
    /// </summary>
    private void OnExitCompleted(int generation)
    {
        HideStep step = _hide.Complete(generation, saveBeforeHide);

        switch (step)
        {
            case HideStep.HideNow:
                window.AppWindow.Hide();
                _lastTransitionEnd = Environment.TickCount;
                break;

            case HideStep.Restore:
                motion.Enter(docked.Edge, fromCurrent: true);
                break;
        }

        Trace.WriteLine($"Noto.Motion done kind=exit step={step} hidden={step == HideStep.HideNow}");
    }

    /// <summary>
    /// Ends normal, docked and focused: restored if minimized, re-docked
    /// against the display it is on now, then brought to the foreground.
    /// </summary>
    private void BringForward(bool redock)
    {
        if (!WindowActivation.Restore(handle))
        {
            return;
        }

        if (redock)
        {
            Redock();
        }

        EnsureTopmost(window);
        _ = WindowActivation.BringToForeground(handle);
    }

    /// <summary>
    /// Docks to the current display's work area, DPI and remembered width —
    /// read now, not remembered from before the window was hidden.
    /// </summary>
    private void Redock()
    {
        try
        {
            double widthDip = preferences.WidthFor(DisplayMonitors.ForWindow(handle).Id.Value);
            PixelRect outer = WindowDocking.OuterBoundsFor(handle, docked.Edge, widthDip);

            docked.Remember(widthDip);
            docked.MoveOwn(() => window.AppWindow.MoveAndResize(new RectInt32(outer.Left, outer.Top, outer.Width, outer.Height)));
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or COMException)
        {
            // Left where it is: shown and focused, undocked only if Windows
            // could not describe the display. The dock's guard still holds.
            Debug.WriteLine($"Re-docking on show failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
