namespace Noto.Platform.Windows;

/// <summary>
/// A rectangle in physical screen pixels.
/// </summary>
/// <remarks>
/// This type lives in the platform layer and must never reach
/// <c>Noto.Core</c> — ADR-009 forbids the domain from knowing about
/// coordinates.
/// </remarks>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;

    public override string ToString() => $"({Left},{Top})-({Right},{Bottom}) {Width}x{Height}";
}

/// <summary>
/// The inset between a window's outer bounds and its visible frame.
/// </summary>
/// <remarks>
/// <para>
/// Windows reports two different rectangles for the same window:
/// <c>GetWindowRect</c> includes an invisible resize border, while
/// <c>DWMWA_EXTENDED_FRAME_BOUNDS</c> describes what the user actually sees.
/// </para>
/// <para>
/// The window-behaviour spike (issue #2) measured this as <b>7/0/7/7</b>
/// (left/top/right/bottom) on Windows 11 26200 at 96 DPI. Positioning a panel
/// without compensating for it leaves the panel 7px away from the screen edge
/// and 14px narrower than requested.
/// </para>
/// <para>
/// <b>The inset is not uniform and is in physical pixels</b>, so it must be
/// re-measured after a DPI change rather than cached globally. See ADR-007.
/// </para>
/// </remarks>
public readonly record struct FrameInset(int Left, int Top, int Right, int Bottom)
{
    public static FrameInset None => new(0, 0, 0, 0);

    /// <summary>
    /// The inset between a window's outer rectangle and its visible frame,
    /// per edge.
    /// </summary>
    /// <param name="window">
    /// The outer rectangle, as <c>GetWindowRect</c> reports it — including the
    /// invisible resize border.
    /// </param>
    /// <param name="visibleFrame">
    /// The visible frame, as <c>DWMWA_EXTENDED_FRAME_BOUNDS</c> reports it.
    /// </param>
    /// <remarks>
    /// <para>
    /// The exact inverse of <see cref="ToOuter"/>:
    /// <c>Between(w, f).ToOuter(f) == w</c> for any pair.
    /// </para>
    /// <para>
    /// <b>Returned as measured.</b> Each edge is computed independently —
    /// the spike measured 7/0/7/7, not a uniform value — and no edge is
    /// clamped. A negative edge would mean the visible frame extends past
    /// the outer rectangle; that is not something Windows is documented to
    /// report, and hiding it here would hide the evidence.
    /// </para>
    /// <para>
    /// A pure function of two rectangles. It holds no state, so there is no
    /// cached inset to go stale: every measurement is a fresh pair of
    /// rectangles taken for the window and the DPI in effect at that moment.
    /// </para>
    /// </remarks>
    public static FrameInset Between(PixelRect window, PixelRect visibleFrame) => new(
        visibleFrame.Left - window.Left,
        visibleFrame.Top - window.Top,
        window.Right - visibleFrame.Right,
        window.Bottom - visibleFrame.Bottom);

    /// <summary>
    /// Expands a desired <em>visible</em> rectangle into the <em>outer</em>
    /// rectangle that must be passed to a positioning call.
    /// </summary>
    public PixelRect ToOuter(PixelRect visible) => new(
        visible.Left - Left,
        visible.Top - Top,
        visible.Right + Right,
        visible.Bottom + Bottom);
}

/// <summary>
/// Where a window sits: which display, what rectangle, at what scale.
/// </summary>
/// <remarks>
/// A platform value type. The domain never sees it (ADR-009); presentation
/// state that must persist belongs in <c>NotePresentations</c>.
/// </remarks>
public readonly record struct WindowPlacement(
    string DeviceName,
    PixelRect Bounds,
    uint Dpi)
{
    /// <summary>Scale factor, where 96 DPI is 1.0.</summary>
    public double Scale => Dpi / 96.0;

    /// <summary>Converts a device-independent length to physical pixels.</summary>
    public int DipToPixels(double dip) => (int)Math.Round(dip * Scale);

    /// <summary>Converts a physical-pixel length to device-independent pixels.</summary>
    /// <remarks>
    /// Not rounded: a DIP length is a measurement, and rounding it here would
    /// make <c>DipToPixels(PixelsToDip(px))</c> drift at fractional scales.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The placement's DPI is zero.</exception>
    public double PixelsToDip(int pixels) => Dpi == 0
        ? throw new InvalidOperationException("A placement with zero DPI has no scale to convert by.")
        : pixels / Scale;
}
