using System.Runtime.InteropServices;
using System.Text;

namespace Noto.Spike.WindowBehaviour;

internal sealed record MonitorInfo(
    string Device,
    bool IsPrimary,
    Native.RECT Bounds,
    Native.RECT WorkArea,
    uint DpiX,
    uint DpiY)
{
    public double Scale => DpiX / 96.0;

    /// <summary>
    /// The taskbar's edge, derived from where the work area is inset from the
    /// monitor bounds. Noto must not dock over the taskbar.
    /// </summary>
    public string TaskbarEdge =>
        WorkArea.Left > Bounds.Left ? "left"
        : WorkArea.Right < Bounds.Right ? "right"
        : WorkArea.Top > Bounds.Top ? "top"
        : WorkArea.Bottom < Bounds.Bottom ? "bottom"
        : "none/auto-hide";

    public string Orientation => Bounds.Width >= Bounds.Height ? "landscape" : "portrait";
}

/// <summary>
/// SPIKE — answers the multi-monitor and DPI questions with measured values
/// from the machine the spike runs on.
/// </summary>
internal static class MonitorProbe
{
    internal static List<MonitorInfo> Enumerate()
    {
        var results = new List<MonitorInfo>();

        // The delegate must stay rooted for the duration of the call.
        // A ref parameter forces all lambda parameters to be explicitly typed.
        Native.MonitorEnumProc callback = (IntPtr hMonitor, IntPtr _, ref Native.RECT _, IntPtr _) =>
        {
            var mi = new Native.MONITORINFOEXW
            {
                cbSize = Marshal.SizeOf<Native.MONITORINFOEXW>()
            };

            if (!Native.GetMonitorInfoW(hMonitor, ref mi))
            {
                return true; // keep enumerating
            }

            uint dpiX = 96, dpiY = 96;
            // Non-zero HRESULT leaves the 96 default, which is the correct
            // assumption on a system without per-monitor DPI.
            _ = Native.GetDpiForMonitor(hMonitor, Native.MDT_EFFECTIVE_DPI, out dpiX, out dpiY);

            results.Add(new MonitorInfo(
                Device: mi.szDevice,
                IsPrimary: (mi.dwFlags & Native.MONITORINFOF_PRIMARY) != 0,
                Bounds: mi.rcMonitor,
                WorkArea: mi.rcWork,
                DpiX: dpiX,
                DpiY: dpiY));

            return true;
        };

        Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        GC.KeepAlive(callback);

        return results;
    }

    /// <summary>
    /// Computes where an edge-docked panel should sit on a given monitor.
    /// This is the calculation the production sidebar will need.
    /// </summary>
    internal static Native.RECT ComputeEdgeDock(
        MonitorInfo monitor, bool rightEdge, int widthDip)
    {
        // Width is authored in DIPs and must be scaled per-monitor. Using a
        // pixel width directly gives a panel that changes apparent size when
        // moved between differently-scaled displays.
        int widthPx = (int)Math.Round(widthDip * monitor.Scale);

        var wa = monitor.WorkArea;

        return new Native.RECT
        {
            Left = rightEdge ? wa.Right - widthPx : wa.Left,
            Top = wa.Top,
            Right = rightEdge ? wa.Right : wa.Left + widthPx,
            Bottom = wa.Bottom
        };
    }

    internal static string Report()
    {
        var sb = new StringBuilder();
        var monitors = Enumerate();

        sb.AppendLine($"MONITORS: {monitors.Count}");
        sb.AppendLine();

        foreach (var m in monitors)
        {
            sb.AppendLine($"  {m.Device}{(m.IsPrimary ? "  [PRIMARY]" : "")}");
            sb.AppendLine($"    bounds     {m.Bounds}");
            sb.AppendLine($"    work area  {m.WorkArea}");
            sb.AppendLine($"    dpi        {m.DpiX}x{m.DpiY}  ({m.Scale:P0} scaling)");
            sb.AppendLine($"    taskbar    {m.TaskbarEdge}");
            sb.AppendLine($"    orient     {m.Orientation}");

            var left = ComputeEdgeDock(m, rightEdge: false, widthDip: 320);
            var right = ComputeEdgeDock(m, rightEdge: true, widthDip: 320);
            sb.AppendLine($"    dock L     {left}   (320 DIP -> {left.Width}px)");
            sb.AppendLine($"    dock R     {right}   (320 DIP -> {right.Width}px)");
            sb.AppendLine();
        }

        var distinctScales = monitors.Select(m => m.DpiX).Distinct().Count();
        sb.AppendLine(distinctScales > 1
            ? $"  MIXED DPI PRESENT ({distinctScales} distinct scale factors) — the important case"
            : "  Uniform DPI across all monitors — mixed-DPI NOT exercised on this machine");

        return sb.ToString();
    }
}
