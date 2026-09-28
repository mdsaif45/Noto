namespace Noto.Platform.Windows;

/// <summary>
/// A native window, as the platform layer receives it from the application.
/// </summary>
/// <remarks>
/// <para>
/// <b>Opaque by construction.</b> It carries a Win32 <c>HWND</c> and exposes
/// nothing about it: no public constructor, no public member returning the
/// handle, and a <see cref="ToString"/> that does not print it. It is a class
/// rather than a struct so equality and hashing are by reference — a value
/// type's generated <c>GetHashCode</c> and <c>ToString</c> would reveal the
/// number, and its <c>default</c> would be a zero handle.
/// </para>
/// <para>
/// <b>Why construction is internal.</b> A WinUI window's handle is obtained in
/// <c>Noto.Windows</c> through <c>WinRT.Interop.WindowNative</c>, which lives
/// in <c>Microsoft.WinUI</c> and must never be referenced from here. Any
/// public API accepting that raw value would put an <c>IntPtr</c> in a public
/// signature, which the platform boundary guard forbids. So the one
/// constructing API, <see cref="FromHwnd"/>, is internal and granted — by
/// <c>InternalsVisibleTo</c> — to <c>Noto.Windows</c> (assembly <c>Noto</c>)
/// and the platform test project only, a list a guard test pins.
/// </para>
/// <para>
/// A handle held here does not keep the window alive and is not validated
/// against it after construction; a call made with a handle whose window has
/// been destroyed fails in the native layer.
/// </para>
/// </remarks>
public sealed class WindowHandle
{
    private readonly nint _hwnd;

    private WindowHandle(nint hwnd) => _hwnd = hwnd;

    /// <summary>The native handle, for this assembly's own Win32 calls.</summary>
    internal nint Hwnd => _hwnd;

    /// <summary>
    /// Wraps the <c>HWND</c> of an existing window.
    /// </summary>
    /// <remarks>
    /// The single construction point. Internal: see the type's remarks for why
    /// it is not public, and which assemblies may call it.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="hwnd"/> is zero.</exception>
    internal static WindowHandle FromHwnd(nint hwnd) => hwnd == 0
        ? throw new ArgumentException("A window handle cannot be zero.", nameof(hwnd))
        : new WindowHandle(hwnd);

    /// <summary>Describes the handle without revealing its value.</summary>
    public override string ToString() => nameof(WindowHandle);
}
