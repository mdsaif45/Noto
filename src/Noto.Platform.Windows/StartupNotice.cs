namespace Noto.Platform.Windows;

/// <summary>
/// A plain Windows message box, for a launch that cannot start or hand off
/// (A17, ADR-013).
/// </summary>
/// <remarks>
/// Win32, not WinUI: it is shown before any window exists — and, for a
/// second launch, instead of one. It blocks until dismissed.
/// </remarks>
public static class StartupNotice
{
    /// <summary>Shows <paramref name="message"/> with an OK button.</summary>
    public static void Show(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        _ = NativeMethods.MessageBox(0, message, "Noto", NativeMethods.MB_OK | NativeMethods.MB_ICONWARNING | NativeMethods.MB_SETFOREGROUND);
    }
}
