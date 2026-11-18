using Noto.Core.Workspace;

namespace Noto.Core.Activation;

/// <summary>The pointer buttons held, as the platform tracks them from the button messages it observes (#185).</summary>
[Flags]
public enum PointerButtons
{
    None = 0,
    Left = 1,
    Right = 2,
    Middle = 4,
    Extra = 8,
}

/// <summary>What brings Noto out at the docked edge (#185; parity A4, A15).</summary>
public enum EdgeGesture
{
    /// <summary>Nothing: no gesture is under way at the edge.</summary>
    None,

    /// <summary>Hot Side (A4): the pointer resting at the edge with no button held.</summary>
    Rest,

    /// <summary>Drag to reveal (A15): a drag in progress, from any application, reaching the edge.</summary>
    Drag,
}

/// <summary>What the platform does with its one timer after an event (#185): the edge's dwell, or the Open Bar's fade.</summary>
public enum EdgeStep
{
    /// <summary>Nothing changes: no timer, or the running one stays.</summary>
    None,

    /// <summary>Start the timer, or restart it, for <see cref="EdgeReveal.Dwell"/> (or <see cref="OpenBar.IdleDelay"/>) milliseconds.</summary>
    Arm,

    /// <summary>Stop the timer.</summary>
    Disarm,
}

/// <summary>How an armed gesture ended when its dwell elapsed (#185).</summary>
public enum EdgeOutcome
{
    /// <summary>Nothing was armed, the timer was early or stale, or the gesture's setting is now off.</summary>
    None,

    /// <summary>Show Noto.</summary>
    Reveal,

    /// <summary>Not revealed: Noto is already shown.</summary>
    Shown,

    /// <summary>Not revealed: the foreground application fills the whole of the docked side's display.</summary>
    Immersive,

    /// <summary>Not revealed: the session is locked, or another desktop (a secure prompt) has the input.</summary>
    Locked,

    /// <summary>Not revealed: the foreground application is moving or sizing a window (Snap uses the same edge).</summary>
    Moving,
}

/// <summary>
/// The docked edge, in the platform's physical units (#185).
/// </summary>
/// <param name="Side">The side Noto docks to.</param>
/// <param name="Left">The left edge of the display Noto docks to, inclusive.</param>
/// <param name="Right">The right edge of that display, exclusive.</param>
/// <param name="Top">The top of the span the edge is live over, inclusive: the work area less a corner.</param>
/// <param name="Bottom">The bottom of that span, exclusive.</param>
/// <param name="Shared">
/// Another display lies beyond the docked edge: a point past the edge is on that display, not pushed against this one.
/// Otherwise a point past the edge (low-level input is reported before the pointer is clipped) is at the edge.
/// </param>
public readonly record struct EdgeZone(WorkspaceEdge Side, int Left, int Right, int Top, int Bottom, bool Shared)
{
    /// <summary>Whether (<paramref name="x"/>, <paramref name="y"/>) is within <see cref="EdgeReveal.Band"/> of the edge.</summary>
    public bool Holds(int x, int y)
    {
        if (y < Top || y >= Bottom || Right - Left < EdgeReveal.Band)
        {
            return false;
        }

        // Past the edge counts too (the hook sees it before the pointer is clipped), unless another display is there.
        bool inBand = Side == WorkspaceEdge.Right ? x >= Right - EdgeReveal.Band : x < Left + EdgeReveal.Band;
        bool onThisDisplay = Side == WorkspaceEdge.Right ? x < Right : x >= Left;
        return inBand && (onThisDisplay || !Shared);
    }

    /// <summary><paramref name="x"/> moved inside the display, where the pointer itself stops.</summary>
    public int Clamp(int x) => Math.Clamp(x, Left, Right - 1);
}

/// <summary>What is switched on and where the edge is (#185). Replaced whole, never changed in place.</summary>
/// <param name="HotSide">Hot Side is on.</param>
/// <param name="Drag">Drag to reveal is on.</param>
/// <param name="Zone">The docked edge.</param>
/// <param name="DragSlopX">How far the pointer moves sideways from a press before it is a drag (the system's drag threshold).</param>
/// <param name="DragSlopY">How far it moves up or down.</param>
public sealed record EdgeRevealOptions(bool HotSide, bool Drag, EdgeZone Zone, int DragSlopX, int DragSlopY);

/// <summary>Why an elapsed gesture may not reveal, read once when its dwell elapses (#185).</summary>
/// <param name="Shown">Noto is already shown, in front or behind.</param>
/// <param name="Immersive">The foreground application fills the whole of the docked side's display.</param>
/// <param name="Locked">The session is locked, or another desktop has the input.</param>
/// <param name="Moving">The foreground application is moving or sizing a window.</param>
public readonly record struct RevealContext(bool Shown, bool Immersive, bool Locked, bool Moving);

/// <summary>What an elapsed dwell decided, and for which gesture.</summary>
public readonly record struct EdgeDecision(EdgeGesture Gesture, EdgeOutcome Outcome);

/// <summary>
/// Hot Side and drag to reveal (#185; parity A4, A15; ADR-007 §9): whether the pointer at the docked edge brings Noto
/// out. A pure state machine, fed every pointer event by the platform's low-level hook and asked once when a dwell
/// elapses, so the rules are tested without a pointer.
/// </summary>
/// <remarks>
/// <para>
/// <b>Allocation-free per event.</b> <see cref="Moved"/> runs inside the hook, which stalls every pointer move on the
/// system while it runs: it compares numbers and returns. The reads that cost something (whether Noto is shown, the
/// foreground application, the lock) are made once, by the platform, when a dwell elapses (<see cref="Elapsed"/>).
/// </para>
/// <code>
///   pointer in the band, no button held, Hot Side on   ->  Arm(300 ms); moving more than 3 px re-arms
///   pointer in the band, a drag under way, Drag on     ->  Arm(150 ms); moving along the edge keeps it
///   leaves the band, a button changes the gesture      ->  Disarm
///   dwell elapsed                                      ->  Reveal, unless shown / immersive / locked / moving
/// </code>
/// <para>
/// <b>Once per arrival.</b> An elapsed dwell, revealed or refused, is spent until the pointer leaves the band, so a
/// pointer left resting at the edge never brings Noto back after the user has put it away.
/// </para>
/// </remarks>
public sealed class EdgeReveal
{
    /// <summary>How close to the edge counts as at it: the outermost two pixel columns.</summary>
    public const int Band = 2;

    /// <summary>How far a resting pointer may move and still be resting.</summary>
    public const int RestTolerance = 3;

    /// <summary>How long the pointer rests at the edge before Hot Side reveals, in milliseconds.</summary>
    public const int RestDwell = 300;

    /// <summary>How long a drag stays at the edge before it reveals, in milliseconds: shorter, as the user is carrying something.</summary>
    public const int DragDwell = 150;

    /// <summary>How early a timer may fire and still count (a system timer's granularity).</summary>
    public const int TimerSlack = 16;

    private bool _leftDown;
    private int _pressX;
    private int _pressY;
    private bool _dragging;
    private bool _spent;
    private int _anchorX;
    private int _anchorY;
    private int _due;

    /// <summary>The gesture the timer is armed for; <see cref="EdgeGesture.None"/> when it is not.</summary>
    public EdgeGesture Armed { get; private set; }

    /// <summary>The armed gesture's dwell, in milliseconds.</summary>
    public int Dwell => Armed == EdgeGesture.Drag ? DragDwell : RestDwell;

    /// <summary>One pointer event: a move or a button, at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    /// <param name="x">Where the pointer is, as the hook reports it (possibly past the display's edge).</param>
    /// <param name="y">Where the pointer is, vertically.</param>
    /// <param name="buttons">The buttons held after this event.</param>
    /// <param name="time">When, on the system's millisecond tick clock.</param>
    /// <param name="options">What is on and where the edge is; <see langword="null"/> when the edge is unknown.</param>
    public EdgeStep Moved(int x, int y, PointerButtons buttons, int time, EdgeRevealOptions? options)
    {
        bool left = (buttons & PointerButtons.Left) != 0;

        if (left && !_leftDown)
        {
            _pressX = x;
            _pressY = y;
        }

        _leftDown = left;

        if (!left)
        {
            _dragging = false;
        }
        else if (!_dragging && options is not null
            && (Math.Abs(x - _pressX) > options.DragSlopX || Math.Abs(y - _pressY) > options.DragSlopY))
        {
            _dragging = true;
        }

        if (options is null || !options.Zone.Holds(x, y))
        {
            _spent = false;
            return Disarm();
        }

        EdgeGesture gesture = _dragging
            ? (options.Drag ? EdgeGesture.Drag : EdgeGesture.None)
            : (buttons == PointerButtons.None && options.HotSide ? EdgeGesture.Rest : EdgeGesture.None);

        if (gesture == EdgeGesture.None)
        {
            return Disarm();
        }

        if (_spent)
        {
            return EdgeStep.None;
        }

        int at = options.Zone.Shared ? x : options.Zone.Clamp(x);

        // A drag counts from its arrival at the edge, wherever it moves along it; a rest restarts whenever it moves.
        if (Armed == gesture
            && (gesture == EdgeGesture.Drag || (Math.Abs(at - _anchorX) <= RestTolerance && Math.Abs(y - _anchorY) <= RestTolerance)))
        {
            return EdgeStep.None;
        }

        Armed = gesture;
        _anchorX = at;
        _anchorY = y;
        _due = unchecked(time + Dwell);
        return EdgeStep.Arm;
    }

    /// <summary>The timer fired: whether the armed gesture reveals Noto. Disarms either way.</summary>
    /// <param name="time">Now, on the system's millisecond tick clock.</param>
    /// <param name="options">What is on now: a setting turned off since arming reveals nothing.</param>
    /// <param name="context">What may refuse it, read now.</param>
    public EdgeDecision Elapsed(int time, EdgeRevealOptions? options, RevealContext context)
    {
        EdgeGesture gesture = Armed;

        if (gesture == EdgeGesture.None || unchecked(time - _due) < -TimerSlack)
        {
            return new EdgeDecision(gesture, EdgeOutcome.None);
        }

        Armed = EdgeGesture.None;
        _spent = true;

        bool on = options is not null && (gesture == EdgeGesture.Drag ? options.Drag : options.HotSide);

        EdgeOutcome outcome = !on ? EdgeOutcome.None
            : context.Locked ? EdgeOutcome.Locked
            : context.Shown ? EdgeOutcome.Shown
            : context.Immersive ? EdgeOutcome.Immersive
            : context.Moving ? EdgeOutcome.Moving
            : EdgeOutcome.Reveal;

        return new EdgeDecision(gesture, outcome);
    }

    private EdgeStep Disarm()
    {
        if (Armed == EdgeGesture.None)
        {
            return EdgeStep.None;
        }

        Armed = EdgeGesture.None;
        return EdgeStep.Disarm;
    }
}
