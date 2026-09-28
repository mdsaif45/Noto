namespace Noto.Platform.Windows;

/// <summary>The vertical screen edge the workspace docks to (parity A2).</summary>
public enum DockEdge
{
    Left,
    Right,
}

/// <summary>
/// The workspace width limits, in device-independent pixels.
/// </summary>
/// <remarks>
/// <para>
/// <b>Provisional values, chosen by the #16 design gate.</b> SideNotes
/// documents no size presets (inventory A14: "no preset size values; free
/// drag"), so these are Noto's own judgement, to be revisited after
/// dogfooding — not measurements and not parity requirements.
/// </para>
/// <para>
/// Widths are authored in DIPs so a remembered width means the same apparent
/// size on every display (#30); conversion to pixels happens once, against a
/// specific display's DPI, in <see cref="DockGeometry"/>.
/// </para>
/// <para>
/// The first-run default belongs to the settings key that stores the width
/// (#16 slice 3), not here.
/// </para>
/// </remarks>
public static class WorkspaceWidth
{
    /// <summary>The narrowest the workspace may be.</summary>
    public const double MinimumDip = 240;

    /// <summary>The widest the workspace may be on any display.</summary>
    public const double CeilingDip = 900;

    /// <summary>
    /// The largest share of a work area the workspace may take. Beyond half,
    /// it stops occupying "one vertical side" (parity A1).
    /// </summary>
    public const double MaximumWorkAreaFraction = 0.5;

    /// <summary>The upper bound for a work area of the given width.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The width is not a positive finite number.</exception>
    public static double MaximumFor(double workAreaWidthDip)
    {
        RequirePositiveFinite(workAreaWidthDip, nameof(workAreaWidthDip));

        return Math.Min(workAreaWidthDip * MaximumWorkAreaFraction, CeilingDip);
    }

    /// <summary>
    /// The width the workspace takes on a work area, given the width asked for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Clamped into <c>[MinimumDip, MaximumFor(workArea)]</c>. A width outside
    /// that range is not an error — it is what a width remembered on a larger
    /// display looks like on a smaller one — so it is corrected, not rejected.
    /// </para>
    /// <para>
    /// <b>When the work area is too narrow for both limits</b> — under
    /// 480 DIP, where half the work area is less than the minimum — the upper
    /// bound wins. The contract does not define this case; keeping the
    /// workspace to at most half the work area preserves the "one vertical
    /// side" rule, where honouring the minimum would let it cover most of a
    /// small display. Flagged for review with this slice.
    /// </para>
    /// <para>
    /// Deciding whether a stored width is <i>valid</i> is the settings layer's
    /// job (#9); this only fits a width to a display. A non-finite width is a
    /// programming error, not a stored value to repair.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="requestedDip"/> is not finite, or the work-area width
    /// is not a positive finite number.
    /// </exception>
    public static double Clamp(double requestedDip, double workAreaWidthDip)
    {
        if (!double.IsFinite(requestedDip))
        {
            throw new ArgumentOutOfRangeException(nameof(requestedDip), requestedDip, "A width must be a finite number.");
        }

        double maximum = MaximumFor(workAreaWidthDip);

        return maximum < MinimumDip
            ? maximum
            : Math.Clamp(requestedDip, MinimumDip, maximum);
    }

    private static void RequirePositiveFinite(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            throw new ArgumentOutOfRangeException(name, value, "Must be a positive finite number.");
        }
    }
}

/// <summary>
/// Where a docked workspace sits on a display.
/// </summary>
/// <remarks>
/// <para>
/// Computes the <b>visible</b> rectangle only. Turning it into the outer
/// rectangle a positioning call needs is <see cref="FrameInset.ToOuter"/>,
/// against an inset measured for the actual window — which is the consumer's
/// step (#16 slice 2), because only it has the window.
/// </para>
/// <para>
/// Placed against the <b>work area</b>, never the monitor bounds, so the
/// taskbar is never covered and a taskbar on any edge is respected (ADR-007).
/// Every coordinate is taken from the work area itself, so a display with a
/// negative or non-zero origin needs no special case.
/// </para>
/// </remarks>
public static class DockGeometry
{
    /// <summary>
    /// The visible bounds of a workspace docked to one edge of a display.
    /// </summary>
    /// <param name="monitor">The display to dock on.</param>
    /// <param name="edge">Which vertical edge.</param>
    /// <param name="requestedWidthDip">
    /// The width asked for, in DIPs; clamped with <see cref="WorkspaceWidth.Clamp"/>.
    /// </param>
    /// <returns>
    /// A placement on <paramref name="monitor"/>, at its DPI, whose bounds
    /// span the work area's full height and sit flush with the chosen edge.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="edge"/> is not a defined edge.</exception>
    public static WindowPlacement VisibleBounds(DisplayMonitor monitor, DockEdge edge, double requestedWidthDip)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        WindowPlacement work = monitor.WorkAreaPlacement;
        PixelRect area = work.Bounds;

        double widthDip = WorkspaceWidth.Clamp(requestedWidthDip, work.PixelsToDip(area.Width));

        // No separate cap against the work area is needed: the clamp keeps
        // the width at or under half the work area, so even rounded up it
        // cannot reach the far edge.
        int widthPx = work.DipToPixels(widthDip);

        PixelRect visible = edge switch
        {
            DockEdge.Left => new PixelRect(area.Left, area.Top, area.Left + widthPx, area.Bottom),
            DockEdge.Right => new PixelRect(area.Right - widthPx, area.Top, area.Right, area.Bottom),
            _ => throw new ArgumentOutOfRangeException(nameof(edge), edge, "Not a dock edge."),
        };

        return new WindowPlacement(monitor.DeviceName, visible, monitor.Dpi);
    }
}
