namespace Noto.Platform.Windows;

/// <summary>
/// Ends Noto's process at once, without the framework's teardown (ADR-017 §10, R17).
/// </summary>
/// <remarks>
/// Measured (Invoke-ExitCycles.ps1, Invoke-Editor.ps1): after Noto's last close work, Windows App SDK 1.x's
/// Microsoft.UI.Xaml.dll 3.2.3 tears the window down and can fault there (0xC0000005, at more than one offset: +0xa0981
/// in 21 of 40 closes made while editing, +0x4a8b5b on a close after leaving the editor). Windows Error Reporting then
/// holds the crashed process about 11 s with every thread suspended: the exit "hang". Nothing of Noto's is left to do
/// by then, so the process ends here instead, with the exit code a clean exit has.
/// </remarks>
public static class ProcessEnd
{
    /// <summary>
    /// Ends the process with <paramref name="exitCode"/>. Only once everything is written and released: no finaliser,
    /// no buffered write and no framework teardown runs after this. Returns only if Windows refused, and then the
    /// ordinary exit follows.
    /// </summary>
    public static void Now(uint exitCode) => _ = NativeMethods.TerminateProcess(NativeMethods.GetCurrentProcess(), exitCode);
}
