using System.Diagnostics;
using System.Runtime.InteropServices;
using Noto.Core.Activation;

namespace Noto.Platform.Windows;

/// <summary>
/// The keys pressed while the shortcut recorder listens (Slice 1.2b): a low-level keyboard hook that exists for one
/// recording and no longer.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a hook.</b> A chord another process has registered is delivered to that process and never reaches Noto's
/// window (measured: the other process received its <c>WM_HOTKEY</c>, the recorder nothing), so the recorder could
/// not try the chord and refuse it. A low-level keyboard hook sees the key before Windows matches it to a
/// registration (measured: Ctrl, Alt and Win held, then the key, with the other process still receiving its press).
/// </para>
/// <para>
/// <b>It observes and never swallows.</b> Every key goes on to the next hook unchanged, so what other applications
/// receive, and Windows' own handling of the Windows key, are exactly as without it. A key is reported only while
/// Noto's window is the foreground window: nothing typed into another application is ever seen.
/// </para>
/// <para>
/// <b>Lifetime.</b> One at a time: installed by <see cref="Start"/>, removed by <see cref="Dispose"/>, which is safe
/// to call more than once. A second <see cref="Start"/> while one is installed is a programming error and throws.
/// </para>
/// <para>
/// <b>Thread.</b> Start it on the UI thread. Windows calls the hook through that thread's message loop and the report
/// runs inside the call, so the report must only queue work: Windows removes a hook that takes too long.
/// </para>
/// </remarks>
public sealed unsafe class RecorderKeyboard : IDisposable
{
    // The installed one: the hook callback is static, as an unmanaged callback must be. UI thread only.
    private static RecorderKeyboard? _installed;

    private readonly nint _owner;
    private readonly Action<int, HotkeyModifiers> _pressed;
    private nint _hook;

    private RecorderKeyboard(nint owner, Action<int, HotkeyModifiers> pressed, nint hook)
    {
        _owner = owner;
        _pressed = pressed;
        _hook = hook;
    }

    /// <summary>Whether a recorder keyboard is installed now.</summary>
    internal static bool IsInstalled => _installed is not null;

    /// <summary>
    /// Installs the hook. <paramref name="pressed"/> is called with each key pressed while <paramref name="owner"/> is
    /// the foreground window, and the modifiers held with it.
    /// </summary>
    /// <returns>The installed observation, or <see langword="null"/> when Windows refused the hook.</returns>
    /// <exception cref="InvalidOperationException">One is already installed.</exception>
    public static RecorderKeyboard? Start(WindowHandle owner, Action<int, HotkeyModifiers> pressed)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(pressed);

        if (_installed is not null)
        {
            throw new InvalidOperationException("A recorder keyboard is already installed.");
        }

        // Until it is the installed one, the callback reports nothing.
        nint hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, &OnKey, NativeMethods.GetModuleHandle(null), 0);

        if (hook == 0)
        {
            Trace.WriteLine($"Noto.Hotkey recorder-keys installed=False error={Marshal.GetLastPInvokeError()}");
            return null;
        }

        _installed = new RecorderKeyboard(owner.Hwnd, pressed, hook);
        Trace.WriteLine("Noto.Hotkey recorder-keys installed=True");
        return _installed;
    }

    /// <summary>Removes the hook, once.</summary>
    public void Dispose()
    {
        if (_hook == 0)
        {
            return;
        }

        _ = NativeMethods.UnhookWindowsHookEx(_hook);
        _hook = 0;

        if (ReferenceEquals(_installed, this))
        {
            _installed = null;
        }

        Trace.WriteLine("Noto.Hotkey recorder-keys removed");
    }

    /// <summary>Whether a hook message is a key press to report: a key going down, while Noto is in front.</summary>
    internal static bool Reports(nint message, bool ownerInFront) =>
        ownerInFront && (message == NativeMethods.WM_KEYDOWN || message == NativeMethods.WM_SYSKEYDOWN);

    /// <summary>The modifiers held with <paramref name="key"/>, the key itself included when it is one.</summary>
    /// <param name="key">The key going down.</param>
    /// <param name="down">Whether a virtual key is down now.</param>
    internal static HotkeyModifiers Held(int key, Func<int, bool> down)
    {
        HotkeyModifiers held = HotkeyModifiers.None;

        if (down(0x11) || key is 0x11 or 0xA2 or 0xA3)
        {
            held |= HotkeyModifiers.Ctrl;
        }

        if (down(0x12) || key is 0x12 or 0xA4 or 0xA5)
        {
            held |= HotkeyModifiers.Alt;
        }

        if (down(0x10) || key is 0x10 or 0xA0 or 0xA1)
        {
            held |= HotkeyModifiers.Shift;
        }

        if (down(0x5B) || down(0x5C) || key is 0x5B or 0x5C)
        {
            held |= HotkeyModifiers.Win;
        }

        return held;
    }

    [UnmanagedCallersOnly]
    private static nint OnKey(int code, nint message, nint data)
    {
        RecorderKeyboard? keyboard = _installed;

        if (code >= 0 && keyboard is not null && Reports(message, NativeMethods.GetForegroundWindow() == keyboard._owner))
        {
            int key = (int)((NativeMethods.KBDLLHOOKSTRUCT*)data)->vkCode;

            try
            {
                keyboard._pressed(key, Held(key, static k => NativeMethods.GetAsyncKeyState(k) < 0));
            }
            catch (Exception ex)
            {
                // Never across the native boundary, where an exception ends the process.
                Trace.WriteLine($"Noto.Hotkey recorder-keys report failed error={ex.GetType().Name}");
            }
        }

        return NativeMethods.CallNextHookEx(0, code, message, data);
    }
}
