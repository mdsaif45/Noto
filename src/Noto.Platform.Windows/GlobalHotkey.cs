using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Noto.Core.Activation;

namespace Noto.Platform.Windows;

/// <summary>How a hotkey registration turned out.</summary>
public enum HotkeyRegistrationStatus
{
    /// <summary>The chord is held by this instance.</summary>
    Registered,

    /// <summary>
    /// Windows refused the chord as taken: another process holds it, or the
    /// system reserves it (<c>ERROR_HOTKEY_ALREADY_REGISTERED</c>, 1409).
    /// </summary>
    Conflict,

    /// <summary>Windows refused the chord for another reason; see <see cref="HotkeyRegistration.ErrorCode"/>.</summary>
    Failed,
}

/// <summary>The outcome of <see cref="GlobalHotkey.Register"/>.</summary>
/// <param name="Status">What happened.</param>
/// <param name="ErrorCode">The Win32 error when Windows refused the chord; 0 when registered.</param>
public readonly record struct HotkeyRegistration(HotkeyRegistrationStatus Status, int ErrorCode)
{
    public bool IsRegistered => Status == HotkeyRegistrationStatus.Registered;
}

/// <summary>
/// One global hotkey, received by a message-only window (#16 slice 4).
/// </summary>
/// <remarks>
/// <para>
/// <b>Independent of every visible window.</b> The chord is registered to a
/// message-only window (<c>HWND_MESSAGE</c> parent, predefined <c>STATIC</c>
/// class) that this type creates and owns, so the hotkey exists before the
/// workspace window does and survives it being hidden, destroyed or created
/// lazily later (#16 slice 5). A message-only window is never shown and never
/// enumerated.
/// </para>
/// <para>
/// <b>Thread.</b> Create it on the thread that pumps messages — the UI thread.
/// <c>WM_HOTKEY</c> is posted to that thread's queue and dispatched to the
/// window by the ordinary message loop; <see cref="Pressed"/> is raised from
/// inside that dispatch, synchronously. That matters: Windows grants the
/// process handling a hotkey the right to take the foreground, so a
/// <see cref="Pressed"/> handler can call
/// <see cref="WindowActivation.BringToForeground"/> and have it succeed —
/// but only while the message is being handled, not after deferring it.
/// </para>
/// <para>
/// <b>One registration per instance.</b> A second <see cref="Register"/>
/// while registered is a programming error and throws. After
/// <see cref="Unregister"/>, the instance may register again.
/// <c>MOD_NOREPEAT</c> is always set, so holding the chord fires once.
/// </para>
/// <para>
/// <b>Cleanup.</b> <see cref="Dispose"/> unregisters and destroys the
/// window; the subclass is removed by the window's own <c>WM_NCDESTROY</c>.
/// Windows also frees the registration when the process ends.
/// </para>
/// </remarks>
public sealed unsafe class GlobalHotkey : IDisposable
{
    /// <summary>The id the chord is registered under. Any other id is not ours.</summary>
    internal const int HotkeyId = 0x4E4F;

    private const nuint SubclassId = 2;

    private readonly nint _hwnd;
    private GCHandle _self;
    private bool _disposed;

    private GlobalHotkey(nint hwnd) => _hwnd = hwnd;

    /// <summary>
    /// Raised when the registered chord is pressed, on the creating thread,
    /// while <c>WM_HOTKEY</c> is being handled.
    /// </summary>
    /// <remarks>A handler that throws is contained: nothing unwinds into Windows.</remarks>
    public event EventHandler? Pressed;

    /// <summary>Whether a chord is currently registered.</summary>
    public bool IsRegistered { get; private set; }

    /// <summary>The message-only window, for this assembly and its tests.</summary>
    internal nint Hwnd => _hwnd;

    /// <summary>Whether the subclass is still installed. Cleared on <c>WM_NCDESTROY</c>.</summary>
    internal bool IsAttached { get; private set; }

    /// <summary>Creates the message-only window on the calling thread.</summary>
    /// <exception cref="Win32Exception">Windows refused the window or its subclass.</exception>
    public static GlobalHotkey Create()
    {
        nint hwnd = NativeMethods.CreateWindowEx(0, "STATIC", "Noto hotkey", 0, 0, 0, 0, 0, NativeMethods.HWND_MESSAGE, 0, 0, 0);

        if (hwnd == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        var hotkey = new GlobalHotkey(hwnd);
        hotkey._self = GCHandle.Alloc(hotkey);

        if (!NativeMethods.SetWindowSubclass(hwnd, &Proc, SubclassId, (nuint)GCHandle.ToIntPtr(hotkey._self)))
        {
            int error = Marshal.GetLastPInvokeError();
            hotkey._self.Free();
            _ = NativeMethods.DestroyWindow(hwnd);
            throw new Win32Exception(error);
        }

        hotkey.IsAttached = true;
        return hotkey;
    }

    /// <summary>Registers a chord globally.</summary>
    /// <returns>Registered, or why Windows refused: nothing is retried and no other chord is tried.</returns>
    /// <exception cref="InvalidOperationException">A chord is already registered on this instance.</exception>
    /// <exception cref="ObjectDisposedException">The instance has been disposed.</exception>
    public HotkeyRegistration Register(HotkeyChord chord)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (IsRegistered)
        {
            throw new InvalidOperationException("This hotkey is already registered; unregister it first.");
        }

        (uint modifiers, uint vk) = HotkeyNative.ToNative(chord);

        if (NativeMethods.RegisterHotKey(_hwnd, HotkeyId, modifiers, vk))
        {
            IsRegistered = true;
            return new HotkeyRegistration(HotkeyRegistrationStatus.Registered, 0);
        }

        int error = Marshal.GetLastPInvokeError();

        return new HotkeyRegistration(
            error == NativeMethods.ERROR_HOTKEY_ALREADY_REGISTERED ? HotkeyRegistrationStatus.Conflict : HotkeyRegistrationStatus.Failed,
            error);
    }

    /// <summary>Releases the chord. Does nothing when none is registered.</summary>
    public void Unregister()
    {
        if (!IsRegistered)
        {
            return;
        }

        _ = NativeMethods.UnregisterHotKey(_hwnd, HotkeyId);
        IsRegistered = false;
    }

    /// <summary>Unregisters and destroys the message-only window.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Unregister();
        _disposed = true;

        // WM_NCDESTROY, handled below, removes the subclass and frees the handle.
        _ = NativeMethods.DestroyWindow(_hwnd);
    }

    /// <summary>A hotkey message: raises <see cref="Pressed"/> only for our registered id.</summary>
    internal void OnHotkey(nint id)
    {
        if (IsRegistered && id == HotkeyId)
        {
            Pressed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnDestroy()
    {
        _ = NativeMethods.RemoveWindowSubclass(_hwnd, &Proc, SubclassId);
        IsAttached = false;
        _self.Free();
    }

    [UnmanagedCallersOnly]
    private static nint Proc(nint hwnd, uint msg, nint wParam, nint lParam, nuint id, nuint refData)
    {
        if (GCHandle.FromIntPtr((nint)refData).Target is not GlobalHotkey self)
        {
            return WindowSubclass.CallDefault(hwnd, msg, wParam, lParam);
        }

        try
        {
            switch (msg)
            {
                case NativeMethods.WM_HOTKEY:
                    self.OnHotkey(wParam);
                    return 0;

                case NativeMethods.WM_NCDESTROY:
                    self.OnDestroy();
                    break;

                default:
                    break;
            }
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
        {
            // The Pressed handler is arbitrary caller code, and nothing may
            // unwind into Windows. Not unconditional — a process out of
            // memory or stack is not a hotkey failure.
            Debug.WriteLine($"Global hotkey: message 0x{msg:X4} failed: {ex.GetType().Name}: {ex.Message}");
        }

        return WindowSubclass.CallDefault(hwnd, msg, wParam, lParam);
    }
}

/// <summary>A chord's Win32 form: <c>MOD_*</c> flags and a virtual-key code.</summary>
internal static class HotkeyNative
{
    /// <summary>The modifier flags, always with <c>MOD_NOREPEAT</c>, and the virtual-key code.</summary>
    /// <exception cref="ArgumentException">The chord's key is not one the grammar accepts.</exception>
    internal static (uint Modifiers, uint VirtualKey) ToNative(HotkeyChord chord)
    {
        uint modifiers = NativeMethods.MOD_NOREPEAT;

        if (chord.Modifiers.HasFlag(HotkeyModifiers.Ctrl))
        {
            modifiers |= NativeMethods.MOD_CONTROL;
        }

        if (chord.Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            modifiers |= NativeMethods.MOD_ALT;
        }

        if (chord.Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            modifiers |= NativeMethods.MOD_SHIFT;
        }

        if (chord.Modifiers.HasFlag(HotkeyModifiers.Win))
        {
            modifiers |= NativeMethods.MOD_WIN;
        }

        return (modifiers, VirtualKeyOf(chord.Key));
    }

    /// <summary>
    /// Letters and digits are their ASCII codes (<c>VK_A</c> = 'A',
    /// <c>VK_0</c> = '0'); <c>VK_F1</c>–<c>VK_F24</c> are 0x70–0x87;
    /// <c>VK_SPACE</c> is 0x20.
    /// </summary>
    internal static uint VirtualKeyOf(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (key == "SPACE")
        {
            return 0x20;
        }

        if (key.Length == 1 && (char.IsAsciiLetterUpper(key[0]) || char.IsAsciiDigit(key[0])))
        {
            return key[0];
        }

        if (key.Length is 2 or 3 && key[0] == 'F'
            && int.TryParse(key.AsSpan(1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int n)
            && n is >= 1 and <= 24 && key[1] != '0')
        {
            return (uint)(0x6F + n);
        }

        throw new ArgumentException($"'{key}' is not a supported hotkey key.", nameof(key));
    }
}

/// <summary>Bringing a window to the foreground (#16 slice 4).</summary>
public static class WindowActivation
{
    /// <summary>
    /// Makes the window the foreground window and gives it keyboard focus.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Windows only lets a process take the foreground when it is entitled
    /// to — one case being the process handling a hotkey. Call this
    /// synchronously from <see cref="GlobalHotkey.Pressed"/>; deferred to a
    /// later dispatch it may be refused. The slice 4 probe measured that
    /// WinUI's <c>Window.Activate()</c> and <c>AppWindow.Show(true)</c> do not
    /// take the foreground from another application, and that this does.
    /// </para>
    /// <para>
    /// A window that is already in the foreground stays there; this never
    /// hides anything.
    /// </para>
    /// </remarks>
    /// <returns>Whether Windows made the window the foreground window.</returns>
    public static bool BringToForeground(WindowHandle window)
    {
        ArgumentNullException.ThrowIfNull(window);

        return NativeMethods.SetForegroundWindow(window.Hwnd);
    }
}
