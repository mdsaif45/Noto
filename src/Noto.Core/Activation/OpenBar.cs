namespace Noto.Core.Activation;

/// <summary>
/// Whether the Open Bar is there, and how it hides (#185; parity A3, J1): SideNotes' three hide modes, its "always
/// hidden" being <see cref="Off"/>, so the bar is one more activation surface that can be switched off on its own
/// (principle 7).
/// </summary>
/// <remarks>Stored by member name in the <c>activation.open-bar.mode</c> setting, so the order is not a contract.</remarks>
public enum OpenBarMode
{
    /// <summary>No bar at all (SideNotes' "always hidden"). The default.</summary>
    Off,

    /// <summary>Shown whenever Noto is hidden (SideNotes' "never hide").</summary>
    AlwaysShown,

    /// <summary>Shown whenever Noto is hidden, fading out once the pointer has left it alone (SideNotes' "mouse inactive").</summary>
    HideWhenIdle,
}

/// <summary>How the Open Bar looks now (#185).</summary>
public enum OpenBarLook
{
    /// <summary>Not there: off, Noto shown, a full-screen application in front, or the session locked.</summary>
    Hidden,

    /// <summary>Drawn at the edge, half see-through.</summary>
    Shown,

    /// <summary>Faded to all but invisible, still there to be pointed at, clicked and dropped on.</summary>
    Faded,
}

/// <summary>
/// The Open Bar's hide modes (#185; parity A3; ADR-007 §10): a pure state machine the platform's bar window feeds, so
/// the rules are tested without a window.
/// </summary>
/// <remarks>
/// <code>
///   Set(mode, away)     away = Noto shown, full-screen app in front or locked  ->  Hidden, timer stopped
///                       HideWhenIdle, coming back or newly on                   ->  Shown, Arm(IdleDelay)
///   PointerMoved()      HideWhenIdle, there                                     ->  Shown, Arm(IdleDelay) again
///   Elapsed()           the one-shot timer fired                                ->  Faded, Disarm
/// </code>
/// <para>
/// <b>One one-shot timer, and only in <see cref="OpenBarMode.HideWhenIdle"/>.</b> It runs only between the bar
/// appearing (or being pointed at) and its fading; nothing polls, so an idle bar costs nothing.
/// </para>
/// </remarks>
public sealed class OpenBar
{
    /// <summary>How long the bar waits, with the pointer elsewhere, before it fades, in milliseconds.</summary>
    public const int IdleDelay = 3000;

    private bool _idle;
    private bool _armed;

    /// <summary>The mode last set.</summary>
    public OpenBarMode Mode { get; private set; }

    /// <summary>Whether the bar must not show now: Noto is shown, a full-screen application is in front, or the session is locked.</summary>
    public bool Away { get; private set; } = true;

    /// <summary>How the bar looks now.</summary>
    public OpenBarLook Look =>
        Mode == OpenBarMode.Off || Away ? OpenBarLook.Hidden
        : Mode == OpenBarMode.HideWhenIdle && _idle ? OpenBarLook.Faded
        : OpenBarLook.Shown;

    /// <summary>What is chosen, and whether the bar must stay away now. Read at every apply and foreground change.</summary>
    public EdgeStep Set(OpenBarMode mode, bool away)
    {
        Mode = mode;
        Away = away;

        if (mode != OpenBarMode.HideWhenIdle || away)
        {
            // Hidden, or never fading: the next appearance starts drawn.
            _idle = false;
            return Disarm();
        }

        // Already faded, or already counting down: a repeated set (another foreground change) changes nothing.
        return _idle || _armed ? EdgeStep.None : Arm();
    }

    /// <summary>The pointer moved over the bar: it is drawn again, and the count starts over.</summary>
    public EdgeStep PointerMoved()
    {
        if (Mode != OpenBarMode.HideWhenIdle || Away)
        {
            return EdgeStep.None;
        }

        _idle = false;
        return Arm();
    }

    /// <summary>The one-shot timer fired: the bar fades, if it still may.</summary>
    public EdgeStep Elapsed()
    {
        if (!_armed)
        {
            return EdgeStep.None;
        }

        _idle = Mode == OpenBarMode.HideWhenIdle && !Away;
        return Disarm();
    }

    private EdgeStep Arm()
    {
        _armed = true;
        return EdgeStep.Arm;
    }

    private EdgeStep Disarm()
    {
        if (!_armed)
        {
            return EdgeStep.None;
        }

        _armed = false;
        return EdgeStep.Disarm;
    }
}
