namespace Noto.Platform.Windows;

/// <summary>Where the workspace window is, as the show/hide toggle sees it.</summary>
public enum WorkspacePresence
{
    /// <summary>Not shown: no <c>WS_VISIBLE</c>, no taskbar button.</summary>
    Hidden,

    /// <summary>Shown but minimized. Not hidden — a distinct state (ADR-007 §4).</summary>
    Minimized,

    /// <summary>Shown and normal, but another window is in the foreground.</summary>
    Background,

    /// <summary>Shown, normal, and the foreground window.</summary>
    Foreground,
}

/// <summary>What one activation request does to the workspace window.</summary>
public enum WorkspaceAction
{
    /// <summary>Nothing — the request is dropped.</summary>
    None,

    /// <summary>Show the hidden window, re-dock it and bring it forward.</summary>
    Show,

    /// <summary>Restore the minimized window, re-dock it and bring it forward.</summary>
    Restore,

    /// <summary>Bring the shown window forward.</summary>
    Focus,

    /// <summary>Hide the window, after saving unsaved editor text.</summary>
    Hide,
}

/// <summary>Where an activation request came from, which decides whether it may hide.</summary>
public enum WorkspaceRequest
{
    /// <summary>The global hotkey: the show/hide toggle.</summary>
    Toggle,

    /// <summary>
    /// A second launch of Noto, handed over by the activation pipe (A17,
    /// ADR-013): show, restore or bring forward — never hide.
    /// </summary>
    Launch,

    /// <summary>
    /// An explicit request to put the workspace away — <c>Ctrl+W</c>, or
    /// Escape where its behaviour hides (#16 slice 6; parity A10, A12): hide,
    /// never show. Pin does not stop it.
    /// </summary>
    Dismiss,

    /// <summary>
    /// The workspace lost activation to another application (#16 slice 6).
    /// </summary>
    /// <remarks>
    /// The mechanism behind parity A13's "close on outside click": hide a
    /// shown workspace, never show — only when
    /// <see cref="ActivationContext.HideOnDeactivation"/> allows it and the
    /// deactivation is still current (<see cref="ActivationGeneration"/>).
    /// </remarks>
    Deactivated,
}

/// <summary>The facts an activation request is judged against.</summary>
/// <param name="ShuttingDown">The window is closing.</param>
/// <param name="Resizing">The user is dragging the inner edge.</param>
/// <param name="Transitioning">A show or hide is already being carried out.</param>
/// <param name="RequestTime">When the request was made, on the <see cref="Environment.TickCount"/> clock.</param>
/// <param name="ReadyTime">When startup finished: a request older than this was made while Noto was starting.</param>
/// <param name="LastTransitionEnd">When the last show or hide finished, if there has been one.</param>
/// <param name="HideOnDeactivation">
/// A <see cref="WorkspaceRequest.Deactivated"/> request may hide: the setting
/// is on and the workspace is not pinned (#16 slice 6).
/// </param>
/// <param name="DeactivationCurrent">
/// A <see cref="WorkspaceRequest.Deactivated"/> request still holds: nothing
/// has activated or brought the workspace forward since the deactivation that
/// made it (<see cref="ActivationGeneration.IsCurrent"/>).
/// </param>
public readonly record struct ActivationContext(
    bool ShuttingDown,
    bool Resizing,
    bool Transitioning,
    int RequestTime,
    int ReadyTime,
    int? LastTransitionEnd,
    bool HideOnDeactivation = false,
    bool DeactivationCurrent = false);

/// <summary>
/// The workspace's activation generation (#16 slice 6, ADR-007 §4 "The
/// drawer"): whether a deactivation still holds when it is decided.
/// </summary>
/// <remarks>
/// <para>
/// A deactivation is decided on the dispatcher, after the activation change,
/// so by then it can be out of date. One counter answers that, with no timer:
/// it advances when activation leaves Noto's process, when it returns, and
/// when Noto brings the workspace forward itself. A deactivation request
/// carries the generation it was made in, and is valid only while that is
/// still the current one.
/// </para>
/// <para>
/// Hides do not advance it: a pending deactivation then finds the workspace
/// hidden and does nothing. Single-threaded by contract — the UI thread that
/// receives <c>WM_ACTIVATEAPP</c> is the one that decides requests. It wraps
/// after 2^32 changes, which equality survives.
/// </para>
/// </remarks>
public sealed class ActivationGeneration
{
    private int _current;

    /// <summary>The generation now.</summary>
    public int Current => _current;

    /// <summary>Activation left for another application: a new generation, which the deactivation request carries.</summary>
    public int Deactivated() => Advance();

    /// <summary>Activation came back to Noto: every pending deactivation is stale.</summary>
    public void Activated() => Advance();

    /// <summary>Noto showed, restored or brought the workspace forward itself: every pending deactivation is stale.</summary>
    public void BroughtForward() => Advance();

    /// <summary>Whether a deactivation made in <paramref name="generation"/> still holds.</summary>
    public bool IsCurrent(int generation) => generation == _current;

    private int Advance() => _current = unchecked(_current + 1);
}

/// <summary>
/// The show/hide toggle's rules (#16 slice 5, ADR-007 §4), as a pure
/// function so they can be tested without a window.
/// </summary>
/// <remarks>
/// <para>
/// <b>The toggle:</b> hidden → show; minimized → restore; shown behind
/// another window → bring forward; shown in front → hide. Every path that
/// ends visible ends normal, docked and focused.
/// </para>
/// <para>
/// <b>A launch is not the toggle</b> (A17): the same, except that a window
/// already shown in front is left as it is. A second launch never hides Noto.
/// </para>
/// <para>
/// <b>Putting it away</b> (#16 slice 6): a dismissal hides a shown window and
/// does nothing else; a deactivation hides a shown window only when
/// <see cref="ActivationContext.HideOnDeactivation"/> allows it and
/// <see cref="ActivationContext.DeactivationCurrent"/> says it still holds,
/// and does nothing else. Neither ever shows, restores or brings anything
/// forward. A current deactivation may still find the window in the
/// foreground: Windows reports that activation is leaving before the
/// foreground has visibly changed.
/// </para>
/// <para>
/// <b>Dropped:</b> any request while the window is closing, while the user is
/// resizing, or while a show or hide is already running. A request made
/// before the last show or hide finished is stale and dropped too, so presses
/// that queued up behind a transition do not replay as a burst of toggles.
/// Requests are judged one at a time as Windows delivers them; nothing is
/// queued.
/// </para>
/// <para>
/// <b>Startup:</b> a request made before startup finished never hides — a
/// press while Noto is launching means "show me". The first one brings the
/// window forward if it is not already; the rest are older than that
/// transition and dropped, which coalesces the burst.
/// </para>
/// </remarks>
public static class WorkspaceToggle
{
    /// <summary>Decides what a request does.</summary>
    public static WorkspaceAction Decide(WorkspaceRequest request, WorkspacePresence presence, ActivationContext context)
    {
        if (context.ShuttingDown || context.Resizing || context.Transitioning)
        {
            return WorkspaceAction.None;
        }

        if (context.LastTransitionEnd is int end && IsBefore(context.RequestTime, end))
        {
            return WorkspaceAction.None;
        }

        bool duringStartup = IsBefore(context.RequestTime, context.ReadyTime);

        return request switch
        {
            WorkspaceRequest.Dismiss => presence is WorkspacePresence.Background or WorkspacePresence.Foreground && !duringStartup
                ? WorkspaceAction.Hide
                : WorkspaceAction.None,

            WorkspaceRequest.Deactivated => presence is WorkspacePresence.Background or WorkspacePresence.Foreground
                && context.DeactivationCurrent && context.HideOnDeactivation && !duringStartup
                ? WorkspaceAction.Hide
                : WorkspaceAction.None,

            _ => presence switch
            {
                WorkspacePresence.Hidden => WorkspaceAction.Show,
                WorkspacePresence.Minimized => WorkspaceAction.Restore,
                WorkspacePresence.Background => WorkspaceAction.Focus,
                WorkspacePresence.Foreground => request == WorkspaceRequest.Launch || duringStartup ? WorkspaceAction.None : WorkspaceAction.Hide,
                _ => WorkspaceAction.None,
            },
        };
    }

    /// <summary>
    /// Whether carrying out <paramref name="action"/> brings the workspace
    /// forward — show, restore or focus — and so makes every deactivation not
    /// yet decided stale (<see cref="ActivationGeneration.BroughtForward"/>).
    /// </summary>
    public static bool BringsForward(WorkspaceAction action) =>
        action is WorkspaceAction.Show or WorkspaceAction.Restore or WorkspaceAction.Focus;

    /// <summary>
    /// Whether tick <paramref name="time"/> is earlier than <paramref name="reference"/>
    /// on the <see cref="Environment.TickCount"/> clock, which wraps every 49.7 days.
    /// </summary>
    public static bool IsBefore(int time, int reference) => unchecked(time - reference) < 0;
}
