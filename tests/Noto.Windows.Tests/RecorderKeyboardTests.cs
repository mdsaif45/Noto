using System.Text.RegularExpressions;
using Noto.Core.Activation;
using Noto.Platform.Windows;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// The shortcut recorder's keyboard observation (Slice 1.2b): what it reports, that it never swallows a key, and that
/// it exists for one recording at a time and is removed once. No key is sent here: what it observes in the running
/// application, a chord another process holds included, is Invoke-Settings.ps1 case 14's to prove.
/// </summary>
public sealed class RecorderKeyboardTests
{
    private const int KeyDown = 0x0100;
    private const int KeyUp = 0x0101;
    private const int SysKeyDown = 0x0104;
    private const int SysKeyUp = 0x0105;

    [Theory]
    [InlineData(KeyDown, true, true)]
    [InlineData(SysKeyDown, true, true)]
    [InlineData(KeyUp, true, false)]
    [InlineData(SysKeyUp, true, false)]
    [InlineData(KeyDown, false, false)]
    [InlineData(SysKeyDown, false, false)]
    public void Only_a_key_going_down_while_noto_is_in_front_is_reported(int message, bool ownerInFront, bool reported)
    {
        Assert.Equal(reported, RecorderKeyboard.Reports(message, ownerInFront));
    }

    [Theory]
    [InlineData(0x79, new[] { 0x11, 0x12, 0x5B }, HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Win)]
    [InlineData(0x79, new[] { 0x11, 0x12, 0x5C }, HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Win)]
    [InlineData(0x41, new[] { 0x10, 0x11 }, HotkeyModifiers.Ctrl | HotkeyModifiers.Shift)]
    [InlineData(0x41, new int[0], HotkeyModifiers.None)]
    [InlineData(0xA2, new int[0], HotkeyModifiers.Ctrl)]
    [InlineData(0xA4, new[] { 0x11 }, HotkeyModifiers.Ctrl | HotkeyModifiers.Alt)]
    [InlineData(0x5B, new[] { 0x11, 0x12 }, HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Win)]
    [InlineData(0xA1, new int[0], HotkeyModifiers.Shift)]
    public void The_modifiers_are_the_ones_held_and_the_key_itself_when_it_is_one(int key, int[] down, HotkeyModifiers held)
    {
        Assert.Equal(held, RecorderKeyboard.Held(key, down.Contains));
    }

    [Fact]
    public void Every_key_goes_on_to_the_next_hook_unchanged()
    {
        // Observing only: the callback has one return, the next hook's answer, so no key is ever swallowed and what other
        // applications receive is unchanged. A report that throws is contained, never crossing into Windows.
        string source = File.ReadAllText(SourcePath("src", "Noto.Platform.Windows", "RecorderKeyboard.cs"));
        string onKey = source[source.IndexOf("private static nint OnKey(", StringComparison.Ordinal)..];

        Assert.Equal(1, Regex.Count(onKey, @"\breturn\b"));
        Assert.Matches(@"return NativeMethods\.CallNextHookEx\(0, code, message, data\);\s*\}\s*\}\s*$", onKey);
        Assert.Matches(@"try\s*\{\s*keyboard\._pressed\(key, Held\(key, [^;]*\)\);\s*\}\s*catch \(Exception ex\)", onKey);
        Assert.Matches(@"Reports\(message, NativeMethods\.GetForegroundWindow\(\) == keyboard\._owner\)", onKey);
    }

    [Fact]
    public void It_is_installed_once_per_recording_and_removed_once_however_often_it_is_started_and_stopped()
    {
        using GlobalHotkey owner = GlobalHotkey.Create();
        WindowHandle window = WindowHandle.FromHwnd(owner.Hwnd);

        Assert.False(RecorderKeyboard.IsInstalled);

        for (int i = 0; i < 25; i++)
        {
            RecorderKeyboard? keyboard = RecorderKeyboard.Start(window, static (_, _) => { });
            Assert.NotNull(keyboard);
            Assert.True(RecorderKeyboard.IsInstalled);

            keyboard.Dispose();
            Assert.False(RecorderKeyboard.IsInstalled);

            // Removing twice removes nothing more, and does not take another recording's hook.
            keyboard.Dispose();
            Assert.False(RecorderKeyboard.IsInstalled);
        }
    }

    [Fact]
    public void A_second_one_while_one_is_installed_is_refused_and_the_first_stays()
    {
        using GlobalHotkey owner = GlobalHotkey.Create();
        WindowHandle window = WindowHandle.FromHwnd(owner.Hwnd);

        using RecorderKeyboard? first = RecorderKeyboard.Start(window, static (_, _) => { });
        Assert.NotNull(first);

        Assert.Throws<InvalidOperationException>(() => RecorderKeyboard.Start(window, static (_, _) => { }));
        Assert.True(RecorderKeyboard.IsInstalled);

        first.Dispose();
        Assert.False(RecorderKeyboard.IsInstalled);

        using RecorderKeyboard? next = RecorderKeyboard.Start(window, static (_, _) => { });
        Assert.NotNull(next);
    }

    [Fact]
    public void A_chord_is_free_only_while_no_process_holds_it_and_asking_holds_nothing()
    {
        HotkeyChord chord = HotkeyChord.Parse("Ctrl+Alt+Shift+Win+F24");
        Assert.True(GlobalHotkey.IsFree(chord));

        // Asking released it at once: it is still free, and can be registered.
        Assert.True(GlobalHotkey.IsFree(chord));

        using (GlobalHotkey holder = GlobalHotkey.Create())
        {
            Assert.True(holder.Register(chord).IsRegistered);
            Assert.False(GlobalHotkey.IsFree(chord));
        }

        Assert.True(GlobalHotkey.IsFree(chord));
    }

    private static string SourcePath(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Join([directory.FullName, .. parts]);

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(string.Join('/', parts));
    }
}
