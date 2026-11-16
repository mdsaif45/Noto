namespace Noto.Core.Workspace;

/// <summary>
/// Whether a press is the second click of a double-click (B12, #190): Windows' own rule, a press within the double-click
/// time of the first click and within a small square around it.
/// </summary>
/// <remarks>
/// The stack decides this itself rather than waiting for the framework's double tap: the first click replaces a card's
/// read view with its editor, so the second lands on another element, and a press inside the editor also toggles a
/// checklist box when it is released. Deciding on the press folds the card before the editor acts on the release.
/// </remarks>
public static class DoubleClick
{
    /// <summary>
    /// How far the second click may land from the first on either axis, in device-independent pixels: Windows' default
    /// double-click square is 4 pixels wide.
    /// </summary>
    // ponytail: a fixed square, not the user's SM_CXDOUBLECLK; read it through the platform layer if anyone changes it.
    public const double Slop = 4;

    /// <summary>
    /// Whether a press at <paramref name="at"/> (milliseconds), at (<paramref name="x"/>, <paramref name="y"/>), is the
    /// second click of the one at <paramref name="firstAt"/>, (<paramref name="firstX"/>, <paramref name="firstY"/>).
    /// </summary>
    public static bool IsSecond(long firstAt, double firstX, double firstY, long at, double x, double y, long doubleClickTime) =>
        at >= firstAt
        && at - firstAt <= doubleClickTime
        && Math.Abs(x - firstX) <= Slop
        && Math.Abs(y - firstY) <= Slop;
}
