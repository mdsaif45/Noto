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

/// <summary>The facts an activation request is judged against.</summary>
/// <param name="ShuttingDown">The window is closing.</param>
/// <param name="Resizing">The user is dragging the inner edge.</param>
/// <param name="Transitioning">A show or hide is already being carried out.</param>
/// <param name="RequestTime">When the request was made, on the <see cref="Environment.TickCount"/> clock.</param>
/// <param name="ReadyTime">When startup finished: a request older than this was made while Noto was starting.</param>
/// <param name="LastTransitionEnd">When the last show or hide finished, if there has been one.</param>
public readonly record struct ActivationContext(
    bool ShuttingDown,
    bool Resizing,
    bool Transitioning,
    int RequestTime,
    int ReadyTime,
    int? LastTransitionEnd);

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
    public static WorkspaceAction Decide(WorkspacePresence presence, ActivationContext context)
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

        return presence switch
        {
            WorkspacePresence.Hidden => WorkspaceAction.Show,
            WorkspacePresence.Minimized => WorkspaceAction.Restore,
            WorkspacePresence.Background => WorkspaceAction.Focus,
            WorkspacePresence.Foreground => duringStartup ? WorkspaceAction.None : WorkspaceAction.Hide,
            _ => WorkspaceAction.None,
        };
    }

    /// <summary>
    /// Whether tick <paramref name="time"/> is earlier than <paramref name="reference"/>
    /// on the <see cref="Environment.TickCount"/> clock, which wraps every 49.7 days.
    /// </summary>
    public static bool IsBefore(int time, int reference) => unchecked(time - reference) < 0;
}
