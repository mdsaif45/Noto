using System.Runtime.InteropServices;

namespace Noto.Spike.HiddenMarkdown;

/// <summary>Real key input, sent only while the spike's own window is in the foreground.</summary>
internal static class Keys
{
    public const ushort Back = 0x08, Control = 0x11, Shift = 0x10, End = 0x23, Home = 0x24, Left = 0x25, Right = 0x27, B = 0x42, X = 0x58, Z = 0x5A;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, INPUT[] inputs, int size);

    public static bool Foreground(IntPtr hwnd)
    {
        if (GetForegroundWindow() != hwnd)
        {
            SetForegroundWindow(hwnd);
        }

        return GetForegroundWindow() == hwnd;
    }

    /// <summary>Presses <paramref name="key"/> with <paramref name="modifiers"/> held. Refuses unless <paramref name="hwnd"/> is foreground.</summary>
    public static void Press(IntPtr hwnd, ushort key, params ushort[] modifiers)
    {
        if (GetForegroundWindow() != hwnd)
        {
            throw new InvalidOperationException("refusing to send keys: the spike is not the foreground window");
        }

        var inputs = new List<INPUT>();
        inputs.AddRange(modifiers.Select(m => Key(m, false)));
        inputs.Add(Key(key, false));
        inputs.Add(Key(key, true));
        inputs.AddRange(Enumerable.Reverse(modifiers).Select(m => Key(m, true)));
        SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
    }

    private static INPUT Key(ushort vk, bool up) => new()
    {
        Type = 1,
        U = new InputUnion { Ki = new KEYBDINPUT { Vk = vk, Flags = up ? 2u : 0u } },
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint Type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT Mi;
        [FieldOffset(0)] public KEYBDINPUT Ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int Dx, Dy;
        public uint MouseData, Flags, Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort Vk, Scan;
        public uint Flags, Time;
        public IntPtr ExtraInfo;
    }
}
