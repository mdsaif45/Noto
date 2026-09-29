using System.Runtime.InteropServices;
using Noto.Core.Activation;
using Noto.Platform.Windows;
using Xunit;
using Xunit.Abstractions;

namespace Noto.Windows.Tests;

/// <summary>
/// Chord mapping (deterministic) and the global hotkey on a real message-only
/// window (runtime) — #16 slice 4.
/// </summary>
/// <remarks>
/// <para>
/// The runtime tests register real chords with Windows. <c>WM_HOTKEY</c> is
/// delivered by posting it to the hotkey's own window and dispatching it on
/// the test thread, rather than by injecting keystrokes into the desktop: the
/// routing is what is under test here, and real key delivery is proven by the
/// pull request's real-app campaign.
/// </para>
/// <para>
/// The chord used, <c>Ctrl+Alt+Win+F23</c>, is one nothing on a normal
/// machine holds.
/// </para>
/// </remarks>
public sealed partial class GlobalHotkeyTests(ITestOutputHelper output)
{
    private static readonly HotkeyChord TestChord = HotkeyChord.Parse("Ctrl+Alt+Win+F23");

    private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;

    // ------------------------------------------------ mapping (deterministic)

    [Fact]
    public void The_default_chord_maps_to_ctrl_alt_win_space_without_repeat()
    {
        (uint modifiers, uint vk) = HotkeyNative.ToNative(HotkeyChord.Parse("Ctrl+Alt+Win+Space"));

        Assert.Equal(MOD_CONTROL | MOD_ALT | MOD_WIN | MOD_NOREPEAT, modifiers);
        Assert.Equal(0x20u, vk);
    }

    [Theory]
    [InlineData("Ctrl+A", MOD_CONTROL)]
    [InlineData("Alt+A", MOD_ALT)]
    [InlineData("Win+A", MOD_WIN)]
    [InlineData("Ctrl+Shift+A", MOD_CONTROL | MOD_SHIFT)]
    [InlineData("Ctrl+Alt+Shift+Win+A", MOD_CONTROL | MOD_ALT | MOD_SHIFT | MOD_WIN)]
    public void Each_modifier_maps_to_its_own_flag_and_no_repeat_is_always_set(string text, uint expected)
    {
        (uint modifiers, _) = HotkeyNative.ToNative(HotkeyChord.Parse(text));

        Assert.Equal(expected | MOD_NOREPEAT, modifiers);
    }

    [Fact]
    public void Every_supported_key_maps_to_its_virtual_key()
    {
        var expected = new Dictionary<string, uint>();

        for (char c = 'A'; c <= 'Z'; c++)
        {
            expected[c.ToString()] = c;           // VK_A..VK_Z = 0x41..0x5A
        }

        for (char c = '0'; c <= '9'; c++)
        {
            expected[c.ToString()] = c;           // VK_0..VK_9 = 0x30..0x39
        }

        for (int n = 1; n <= 24; n++)
        {
            expected[$"F{n}"] = (uint)(0x6F + n); // VK_F1..VK_F24 = 0x70..0x87
        }

        expected["SPACE"] = 0x20;

        Assert.Equal(expected.Keys.Order(), HotkeyChord.SupportedKeys.Order());

        foreach ((string key, uint vk) in expected)
        {
            Assert.Equal(vk, HotkeyNative.VirtualKeyOf(key));
            Assert.Equal(vk, HotkeyNative.ToNative(HotkeyChord.Parse("Ctrl+" + key)).VirtualKey);
        }
    }

    [Theory]
    [InlineData("ENTER")]
    [InlineData("F0")]
    [InlineData("F25")]
    [InlineData("F01")]
    [InlineData("a")]
    [InlineData("")]
    public void A_key_outside_the_grammar_is_refused(string key)
    {
        Assert.Throws<ArgumentException>(() => HotkeyNative.VirtualKeyOf(key));
    }

    // ---------------------------------------------- registration (runtime)

    [Fact]
    [Trait("Category", DesktopSession.RequiresDesktopTrait)]
    public void A_free_chord_registers()
    {
        if (!DesktopSession.ShouldRun(out string reason))
        {
            output.WriteLine(reason);
            return;
        }

        using var hotkey = GlobalHotkey.Create();

        HotkeyRegistration registration = hotkey.Register(TestChord);

        Assert.Equal(new HotkeyRegistration(HotkeyRegistrationStatus.Registered, 0), registration);
        Assert.True(registration.IsRegistered);
        Assert.True(hotkey.IsRegistered);
        Assert.True(hotkey.IsAttached);
    }

    [Fact]
    [Trait("Category", DesktopSession.RequiresDesktopTrait)]
    public void A_chord_already_held_is_a_conflict_1409()
    {
        if (!DesktopSession.ShouldRun(out string reason))
        {
            output.WriteLine(reason);
            return;
        }

        using var holder = GlobalHotkey.Create();
        using var second = GlobalHotkey.Create();
        Assert.True(holder.Register(TestChord).IsRegistered);

        HotkeyRegistration refused = second.Register(TestChord);

        Assert.Equal(new HotkeyRegistration(HotkeyRegistrationStatus.Conflict, 1409), refused);
        Assert.False(refused.IsRegistered);
        Assert.False(second.IsRegistered);
    }

    [Fact]
    [Trait("Category", DesktopSession.RequiresDesktopTrait)]
    public void A_second_register_on_the_same_instance_is_refused_and_the_first_stands()
    {
        if (!DesktopSession.ShouldRun(out string reason))
        {
            output.WriteLine(reason);
            return;
        }

        using var hotkey = GlobalHotkey.Create();
        using var other = GlobalHotkey.Create();
        Assert.True(hotkey.Register(TestChord).IsRegistered);

        Assert.Throws<InvalidOperationException>(() => hotkey.Register(HotkeyChord.Parse("Ctrl+Alt+Win+F22")));

        Assert.True(hotkey.IsRegistered);
        Assert.Equal(HotkeyRegistrationStatus.Conflict, other.Register(TestChord).Status);
        Assert.True(other.Register(HotkeyChord.Parse("Ctrl+Alt+Win+F22")).IsRegistered);
    }

    [Fact]
    [Trait("Category", DesktopSession.RequiresDesktopTrait)]
    public void Unregistering_frees_the_chord_and_allows_registering_again()
    {
        if (!DesktopSession.ShouldRun(out string reason))
        {
            output.WriteLine(reason);
            return;
        }

        using var hotkey = GlobalHotkey.Create();
        using var other = GlobalHotkey.Create();
        Assert.True(hotkey.Register(TestChord).IsRegistered);

        hotkey.Unregister();

        Assert.False(hotkey.IsRegistered);
        Assert.True(other.Register(TestChord).IsRegistered);
        other.Unregister();
        Assert.True(hotkey.Register(TestChord).IsRegistered);
        hotkey.Unregister();
        hotkey.Unregister(); // idempotent
    }

    [Fact]
    [Trait("Category", DesktopSession.RequiresDesktopTrait)]
    public void Disposing_unregisters_destroys_the_window_and_removes_the_subclass()
    {
        if (!DesktopSession.ShouldRun(out string reason))
        {
            output.WriteLine(reason);
            return;
        }

        var hotkey = GlobalHotkey.Create();
        Assert.True(hotkey.Register(TestChord).IsRegistered);

        hotkey.Dispose();

        Assert.False(hotkey.IsRegistered);
        Assert.False(hotkey.IsAttached);
        Assert.False(Native.IsWindow(hotkey.Hwnd));
        using var next = GlobalHotkey.Create();
        Assert.True(next.Register(TestChord).IsRegistered);
        hotkey.Dispose(); // idempotent
        Assert.Throws<ObjectDisposedException>(() => hotkey.Register(TestChord));
    }

    [Fact]
    [Trait("Category", DesktopSession.RequiresDesktopTrait)]
    public void The_receiver_is_a_message_only_window()
    {
        if (!DesktopSession.ShouldRun(out string reason))
        {
            output.WriteLine(reason);
            return;
        }

        using var hotkey = GlobalHotkey.Create();

        Assert.True(Native.IsWindow(hotkey.Hwnd));
        Assert.False(Native.IsWindowVisible(hotkey.Hwnd));
        // FindWindowEx with HWND_MESSAGE as the parent searches message-only
        // windows only.
        Assert.Equal(hotkey.Hwnd, Native.FindWindowEx(-3, 0, "STATIC", "Noto hotkey"));
    }

    // --------------------------------------------------- delivery (runtime)

    [Fact]
    [Trait("Category", DesktopSession.RequiresDesktopTrait)]
    public void Our_hotkey_message_raises_pressed_exactly_once()
    {
        if (!DesktopSession.ShouldRun(out string reason))
        {
            output.WriteLine(reason);
            return;
        }

        using var hotkey = GlobalHotkey.Create();
        Assert.True(hotkey.Register(TestChord).IsRegistered);
        var pressed = 0;
        hotkey.Pressed += (_, _) => pressed++;

        Deliver(hotkey, GlobalHotkey.HotkeyId);

        Assert.Equal(1, pressed);

        Deliver(hotkey, GlobalHotkey.HotkeyId);
        Assert.Equal(2, pressed);
    }

    [Theory]
    [Trait("Category", DesktopSession.RequiresDesktopTrait)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(GlobalHotkey.HotkeyId + 1)]
    [InlineData(-1)]
    public void A_hotkey_message_with_another_id_is_ignored(int id)
    {
        if (!DesktopSession.ShouldRun(out string reason))
        {
            output.WriteLine(reason);
            return;
        }

        using var hotkey = GlobalHotkey.Create();
        Assert.True(hotkey.Register(TestChord).IsRegistered);
        var pressed = 0;
        hotkey.Pressed += (_, _) => pressed++;

        Deliver(hotkey, id);

        Assert.Equal(0, pressed);
    }

    [Fact]
    [Trait("Category", DesktopSession.RequiresDesktopTrait)]
    public void Nothing_is_raised_when_no_chord_is_registered()
    {
        if (!DesktopSession.ShouldRun(out string reason))
        {
            output.WriteLine(reason);
            return;
        }

        using var hotkey = GlobalHotkey.Create();
        var pressed = 0;
        hotkey.Pressed += (_, _) => pressed++;

        Deliver(hotkey, GlobalHotkey.HotkeyId);
        Assert.True(hotkey.Register(TestChord).IsRegistered);
        hotkey.Unregister();
        Deliver(hotkey, GlobalHotkey.HotkeyId);

        Assert.Equal(0, pressed);
    }

    [Fact]
    [Trait("Category", DesktopSession.RequiresDesktopTrait)]
    public void A_throwing_handler_is_contained()
    {
        if (!DesktopSession.ShouldRun(out string reason))
        {
            output.WriteLine(reason);
            return;
        }

        // An exception must not unwind into Windows (that would end the
        // process): the window procedure survives, and the next press is
        // delivered as normal.
        using var hotkey = GlobalHotkey.Create();
        Assert.True(hotkey.Register(TestChord).IsRegistered);
        var reached = 0;
        hotkey.Pressed += (_, _) => reached++;
        hotkey.Pressed += (_, _) => throw new InvalidOperationException("handler failure");

        Deliver(hotkey, GlobalHotkey.HotkeyId);
        Deliver(hotkey, GlobalHotkey.HotkeyId);

        Assert.Equal(2, reached);
        Assert.True(hotkey.IsAttached);
        Assert.True(hotkey.IsRegistered);
    }

    [Fact]
    public void Bringing_a_null_window_forward_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => WindowActivation.BringToForeground(null!));
    }

    /// <summary>Posts WM_HOTKEY to the hotkey's window and dispatches it on this thread.</summary>
    private static void Deliver(GlobalHotkey hotkey, int id)
    {
        Assert.True(Native.PostMessage(hotkey.Hwnd, 0x0312, id, 0));

        while (Native.PeekMessage(out Native.Msg message, hotkey.Hwnd, 0, 0, 1))
        {
            _ = Native.DispatchMessage(in message);
        }
    }

    private static partial class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct Msg
        {
            public nint Hwnd;
            public uint Message;
            public nint WParam;
            public nint LParam;
            public uint Time;
            public int X;
            public int Y;
        }

        [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool PostMessage(nint hwnd, uint msg, nint wParam, nint lParam);

        [LibraryImport("user32.dll", EntryPoint = "PeekMessageW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool PeekMessage(out Msg message, nint hwnd, uint min, uint max, uint remove);

        [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
        public static partial nint DispatchMessage(in Msg message);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool IsWindow(nint hwnd);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool IsWindowVisible(nint hwnd);

        [LibraryImport("user32.dll", EntryPoint = "FindWindowExW", StringMarshalling = StringMarshalling.Utf16)]
        public static partial nint FindWindowEx(nint parent, nint after, string className, string windowName);
    }
}
