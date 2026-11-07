using System.Runtime.InteropServices;
using Windows.UI;
using Windows.UI.Composition;

namespace Noto.Platform.Windows;

/// <summary>
/// A window whose transparent pixels show what is behind it (F2, #145): the drawer slides in from its edge with its
/// own background, inside its docked rectangle, and the part it has not yet covered is the desktop, not an empty frame.
/// </summary>
/// <remarks>
/// <para>
/// Two halves, both needed (measured in light and dark, 2026-11-07): DWM must honour the window's per-pixel alpha
/// (<see cref="Enable"/>: blur-behind over an empty region, so nothing is blurred), and the window's backdrop must be
/// transparent (<see cref="ClearBrush"/>, set by the application's backdrop). Without the first, transparent pixels
/// are drawn black.
/// </para>
/// </remarks>
public static class WindowTransparency
{
    private static Compositor? s_compositor;
    private static nint s_queueController;

    /// <summary>Lets DWM draw the window's transparent pixels as see-through.</summary>
    /// <exception cref="COMException">DWM refused.</exception>
    public static void Enable(WindowHandle window)
    {
        ArgumentNullException.ThrowIfNull(window);

        // Empty and off-window: DWM honours alpha everywhere and blurs nothing. DWM copies the region.
        nint region = NativeMethods.CreateRectRgn(-2, -2, -1, -1);

        try
        {
            var blur = new NativeMethods.DWM_BLURBEHIND
            {
                dwFlags = NativeMethods.DWM_BB_ENABLE | NativeMethods.DWM_BB_BLURREGION,
                fEnable = 1,
                hRgnBlur = region,
            };

            Marshal.ThrowExceptionForHR(NativeMethods.DwmEnableBlurBehindWindow(window.Hwnd, blur));
        }
        finally
        {
            NativeMethods.DeleteObject(region);
        }
    }

    /// <summary>
    /// A fully transparent brush for a backdrop target, on the system compositor. Call on the window's thread; the
    /// compositor and its dispatcher queue live as long as the process.
    /// </summary>
    /// <exception cref="COMException">The thread's dispatcher queue could not be created.</exception>
    public static CompositionBrush ClearBrush()
    {
        if (s_compositor is null)
        {
            // WinUI's thread has its own Microsoft.UI queue; the system compositor needs a Windows.System one.
            if (global::Windows.System.DispatcherQueue.GetForCurrentThread() is null && s_queueController == 0)
            {
                var options = new NativeMethods.DispatcherQueueOptions
                {
                    dwSize = Marshal.SizeOf<NativeMethods.DispatcherQueueOptions>(),
                    threadType = NativeMethods.DQTYPE_THREAD_CURRENT,
                    apartmentType = NativeMethods.DQTAT_COM_STA,
                };

                Marshal.ThrowExceptionForHR(NativeMethods.CreateDispatcherQueueController(options, out s_queueController));
            }

            s_compositor = new Compositor();
        }

        return s_compositor.CreateColorBrush(Color.FromArgb(0, 0, 0, 0));
    }
}
