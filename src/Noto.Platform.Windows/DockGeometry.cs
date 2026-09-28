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
    /// <summary>The nominal product minimum.</summary>
    public const double MinimumDip = 240;

    /// <summary>The widest the workspace may be on any display.</summary>
    public const double CeilingDip = 900;

    /// <summary>
    /// The share of a work area the nominal maximum allows. On a work area
    /// narrower than 480 DIP the minimum takes precedence over it; see
    /// <see cref="EffectiveMaximumFor"/>.
    /// </summary>
    public const double MaximumWorkAreaFraction = 0.5;

    /// <summary>
    /// The nominal maximum: the lower of half the work area and the ceiling.
    /// </summary>
    /// <remarks>
    /// Not the bound a width is clamped to. Below a 480 DIP work area it is
    /// smaller than <see cref="MinimumDip"/>; <see cref="EffectiveMaximumFor"/>
    /// is the bound actually applied.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The width is not a positive finite number.</exception>
    public static double MaximumFor(double workAreaWidthDip)
    {
        RequirePositiveFinite(workAreaWidthDip, nameof(workAreaWidthDip));

        return Math.Min(workAreaWidthDip * MaximumWorkAreaFraction, CeilingDip);
    }

    /// <summary>
    /// The lower bound applied on a work area: the nominal minimum, or the
    /// whole work area when that is narrower.
    /// </summary>
    /// <remarks>
    /// On a work area under 240 DIP the physical work area takes precedence.
    /// A width there is below the nominal minimum; it does not satisfy it.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The width is not a positive finite number.</exception>
    public static double EffectiveMinimumFor(double workAreaWidthDip)
    {
        RequirePositiveFinite(workAreaWidthDip, nameof(workAreaWidthDip));

        return Math.Min(MinimumDip, workAreaWidthDip);
    }

    /// <summary>
    /// The upper bound applied on a work area: the nominal maximum, raised to
    /// the minimum where the two cross, and never wider than the work area.
    /// </summary>
    /// <remarks>
    /// Never below <see cref="EffectiveMinimumFor"/>: the inner term is at
    /// least <see cref="MinimumDip"/>, so the result is at least
    /// <c>min(workArea, 240)</c>. The interval is therefore never empty.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The width is not a positive finite number.</exception>
    public static double EffectiveMaximumFor(double workAreaWidthDip) =>
        Math.Min(workAreaWidthDip, Math.Max(MinimumDip, MaximumFor(workAreaWidthDip)));

    /// <summary>
    /// The width the workspace takes on a work area, given the width asked for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Clamped into <c>[EffectiveMinimumFor(w), EffectiveMaximumFor(w)]</c>
    /// (ADR-007 §4). A width outside that range is not an error — it is what
    /// a width remembered on a larger display looks like on a smaller one — so
    /// it is corrected, not rejected. The result never exceeds the work area.
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

        return Math.Clamp(
            requestedDip,
            EffectiveMinimumFor(workAreaWidthDip),
            EffectiveMaximumFor(workAreaWidthDip));
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
        // the width at or under the work area in DIPs, and converting a width
        // no larger than workPx / scale back to pixels cannot exceed workPx.
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
