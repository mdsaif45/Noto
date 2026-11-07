using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Noto.Platform.Windows;

namespace Noto;

/// <summary>
/// The window's backdrop: nothing (F2, #145). The drawer draws its own background and slides it in from its edge; what
/// it has not yet covered shows the desktop. See <see cref="WindowTransparency"/>.
/// </summary>
internal sealed class TransparentBackdrop : SystemBackdrop
{
    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);

        try
        {
            nint hwnd = Win32Interop.GetWindowFromWindowId(xamlRoot.ContentIslandEnvironment.AppWindowId);
            WindowTransparency.Enable(WindowHandle.FromHwnd(hwnd));
            connectedTarget.SystemBackdrop = WindowTransparency.ClearBrush();
        }
        catch (COMException ex)
        {
            // The drawer still works; only its slide shows the window's own fill where it has not yet arrived.
            Trace.WriteLine($"Noto.Motion transparency unavailable error=0x{ex.HResult:X8}");
        }
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        base.OnTargetDisconnected(disconnectedTarget);
        disconnectedTarget.SystemBackdrop = null;
    }
}
