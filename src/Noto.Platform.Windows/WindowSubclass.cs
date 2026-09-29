namespace Noto.Platform.Windows;

/// <summary>
/// What every window subclass in this assembly shares.
/// </summary>
/// <remarks>
/// <para>
/// <b>The single call into the rest of a window-procedure chain.</b> A
/// subclass passes on every message it does not consume, and asks for the
/// default answer when it needs one (the dock's hit test). Both subclasses —
/// <see cref="DockedWindow"/> and <see cref="GlobalHotkey"/> — go through
/// here, so this is deliberately the only call site of
/// <see cref="NativeMethods.DefSubclassProc"/> and the native boundary stays
/// one line wide.
/// </para>
/// <para>
/// CodeQL reports <c>cs/call-to-unmanaged-code</c> at that call, as expected:
/// the signature is all plain types, so the generator emits a direct
/// <c>extern</c>, and there is no managed equivalent (ADR-007 §4).
/// </para>
/// </remarks>
internal static class WindowSubclass
{
    internal static nint CallDefault(nint hwnd, uint msg, nint wParam, nint lParam) =>
        NativeMethods.DefSubclassProc(hwnd, msg, wParam, lParam);
}
