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
}
