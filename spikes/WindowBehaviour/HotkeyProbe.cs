using System.Text;

namespace Noto.Spike.WindowBehaviour;

internal sealed record HotkeyAttempt(
    string Description,
    uint Modifiers,
    uint VirtualKey,
    bool Registered,
    int Win32Error,
    string? Warning);

/// <summary>
/// SPIKE — answers the global hotkey questions, including the AltGr conflict.
///
/// Uses <c>RegisterHotKey</c>, never <c>WH_KEYBOARD_LL</c>: a low-level hook
/// sits in the system input path, is silently removed by Windows on a 300ms
/// timeout, and is a common antivirus heuristic trigger.
/// </summary>
internal sealed class HotkeyProbe : IDisposable
{
    private readonly IntPtr _hwnd;
    private readonly List<int> _registered = [];
    private int _nextId = 1;

    internal HotkeyProbe(IntPtr hwnd) => _hwnd = hwnd;

    /// <summary>
    /// On German, French, Polish and other layouts, AltGr is delivered to
    /// applications as Ctrl+Alt. A hotkey registered on Ctrl+Alt+&lt;key&gt;
    /// therefore swallows characters the user is actively typing.
    ///
    /// macOS has no analogue — Option produces those characters and Command
    /// carries shortcuts — which is why SideNotes never had to solve this.
    /// </summary>
    internal static bool IsAltGrConflict(uint modifiers)
    {
        const uint ctrlAlt = Native.MOD_CONTROL | Native.MOD_ALT;
        // Win as a fourth modifier does not rescue it: AltGr still produces
        // Ctrl+Alt, and the user may hold Win independently.
        return (modifiers & ctrlAlt) == ctrlAlt;
    }

    internal HotkeyAttempt TryRegister(string description, uint modifiers, uint vk)
    {
        string? warning = IsAltGrConflict(modifiers)
            ? "AltGr conflict: intercepts typing on DE/FR/PL layouts"
            : null;

        int id = _nextId++;

        // MOD_NOREPEAT stops auto-repeat firing the action continuously while
        // the key is held.
        bool ok = Native.RegisterHotKey(_hwnd, id, modifiers | Native.MOD_NOREPEAT, vk);
        int err = ok ? 0 : System.Runtime.InteropServices.Marshal.GetLastWin32Error();

        if (ok)
        {
            _registered.Add(id);
        }

        return new HotkeyAttempt(description, modifiers, vk, ok, err, warning);
    }

    internal string Report()
    {
        var sb = new StringBuilder();
        sb.AppendLine("GLOBAL HOTKEYS (RegisterHotKey)");
        sb.AppendLine();

        // VK codes
        const uint VK_N = 0x4E, VK_SPACE = 0x20, VK_Q = 0x51, VK_F13 = 0x7C;

        var attempts = new List<HotkeyAttempt>
        {
            // Recommended shapes — no Ctrl+Alt.
            TryRegister("Ctrl+Shift+N  (recommended shape)", Native.MOD_CONTROL | Native.MOD_SHIFT, VK_N),
            TryRegister("Win+Shift+Space (recommended shape)", Native.MOD_WIN | Native.MOD_SHIFT, VK_SPACE),

            // The shape that must be avoided.
            TryRegister("Ctrl+Alt+Q    (AltGr HAZARD)", Native.MOD_CONTROL | Native.MOD_ALT, VK_Q),

            // Conflict handling: register the same chord twice. The second
            // must fail cleanly rather than throw or silently win.
            TryRegister("Ctrl+Shift+N  (duplicate — must fail)", Native.MOD_CONTROL | Native.MOD_SHIFT, VK_N),

            // A chord Windows itself owns, to see how a conflict surfaces.
            TryRegister("Win+L         (OS-reserved — expect failure)", Native.MOD_WIN, 0x4C),

            // No modifier at all: invalid for a global hotkey in practice.
            TryRegister("F13 alone     (no modifier)", 0, VK_F13),
        };

        foreach (var a in attempts)
        {
            string status = a.Registered ? "OK    " : $"FAIL({a.Win32Error})";
            sb.AppendLine($"  [{status}] {a.Description}");
            if (a.Warning is not null)
            {
                sb.AppendLine($"            ^ {a.Warning}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("  Win32 error 1409 = ERROR_HOTKEY_ALREADY_REGISTERED");
        sb.AppendLine("  Conflicts surface as a clean boolean false, not an exception,");
        sb.AppendLine("  so Noto can report them in settings and keep running.");

        return sb.ToString();
    }

    public void Dispose()
    {
        foreach (int id in _registered)
        {
            Native.UnregisterHotKey(_hwnd, id);
        }
        _registered.Clear();
    }
}
