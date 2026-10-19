using System.Globalization;

namespace Noto.Platform.Windows;

/// <summary>
/// The arithmetic of Noto's motion (Slice 1.3), kept apart from the compositor so every direction and value is tested
/// without a window. The window itself never moves: only its content does, inside the docked rectangle.
/// </summary>
/// <remarks>
/// Durations and the easing curve are WinUI's own (<c>ControlNormalAnimationDuration</c> and the like), read by key at
/// run time; distances are Noto's design tokens. Nothing here duplicates a value.
/// </remarks>
public static class MotionPlan
{
    /// <summary>
    /// Where the content starts when the drawer is shown: <paramref name="distance"/> toward the docked edge, so it
    /// slides in away from that edge (right edge: positive X).
    /// </summary>
    public static double EnterFrom(DockEdge edge, double distance) => TowardEdge(edge, distance);

    /// <summary>Where the content ends when the drawer is hidden: <paramref name="distance"/> toward the docked edge.</summary>
    public static double ExitTo(DockEdge edge, double distance) => TowardEdge(edge, distance);

    /// <summary>
    /// Where an entering surface starts: going forward (into a folder, a note, Settings) it enters from the right;
    /// going back, from the left.
    /// </summary>
    public static double SurfaceFrom(bool forward, double distance) => forward ? Math.Abs(distance) : -Math.Abs(distance);

    /// <summary>
    /// Whether a transition is shown: only with Windows' "Animation effects" on. Off, the final state is applied at
    /// once: no motion, no delayed hide.
    /// </summary>
    public static bool Animate(bool animationsEnabled) => animationsEnabled;

    /// <summary>A WinUI duration resource, <c>00:00:00.250</c>.</summary>
    /// <exception cref="FormatException">Not a positive duration.</exception>
    public static TimeSpan ParseDuration(string text)
    {
        TimeSpan value = TimeSpan.Parse(text, CultureInfo.InvariantCulture);
        return value > TimeSpan.Zero ? value : throw new FormatException("A motion duration must be positive.");
    }

    /// <summary>A WinUI key spline resource, <c>0,0,0,1</c>: the two control points of a cubic Bezier.</summary>
    /// <exception cref="FormatException">Not four numbers.</exception>
    public static (float X1, float Y1, float X2, float Y2) ParseSpline(string text)
    {
        string[] parts = text.Split(',');

        if (parts.Length != 4)
        {
            throw new FormatException("A key spline is four numbers.");
        }

        float[] v = parts.Select(p => float.Parse(p.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture)).ToArray();
        return (v[0], v[1], v[2], v[3]);
    }

    private static double TowardEdge(DockEdge edge, double distance) => edge switch
    {
        DockEdge.Right => Math.Abs(distance),
        DockEdge.Left => -Math.Abs(distance),
        _ => throw new ArgumentOutOfRangeException(nameof(edge), edge, "Not a dock edge."),
    };
}
