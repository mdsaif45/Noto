using System.Text.RegularExpressions;
using Noto.Core.Activation;
using Noto.Core.Workspace;
using Noto.Platform.Windows;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// Hot Side and drag to reveal (#185; parity A4, A15; ADR-007 §9), held in the source and below the window: the hook
/// exists only while a setting is on, its callback only compares and passes on, the docked edge and the full-screen
/// check are what the ADR says, and a reveal reaches Noto through the coordinator as a launch. No pointer is moved
/// here: what the running application does with a real pointer is Invoke-Settings.ps1 case 23's to prove.
/// </summary>
public sealed class EdgeRevealGuardTests
{
    private static readonly string Windows = Path.GetDirectoryName(SourcePath("src", "Noto.Windows", "App.xaml.cs"))!;
    private static readonly string App = Code(Path.Join(Windows, "App.xaml.cs"));
    private static readonly string Pointer = Code(SourcePath("src", "Noto.Platform.Windows", "EdgePointer.cs"));

    // ---------------------------------------------------------------- the hook, only while on

    [Fact]
    public void The_hook_is_installed_only_while_a_setting_is_on_and_removed_when_both_are_off_or_at_exit()
    {
        string apply = Body(App, "ApplyEdgeReveal");

        Assert.Matches(
            @"bool hotSide = preferences\.HotSide;\s*bool drag = preferences\.DragReveal;\s*if \(\(!hotSide && !drag\) \|\|[^)]*\)\s*\{\s*_edgePointer\?\.Dispose\(\);\s*_edgePointer = null;\s*return;\s*\}",
            apply);
        Assert.Matches(@"if \(_edgePointer is null\)\s*\{[\s\S]*_edgePointer = EdgePointer\.Start\(_windowHandle, ", apply);
        Assert.Contains("_edgePointer?.Configure(hotSide, drag, _docked.Edge);", apply, StringComparison.Ordinal);

        // One place starts it, and the process's end removes it.
        string allWindows = string.Join('\n', Directory.EnumerateFiles(Windows, "*.cs").Select(Code));
        Assert.Equal(1, Regex.Count(allWindows, @"EdgePointer\.Start\("));
        Assert.Contains("_edgePointer?.Dispose();", Body(App, "ReleaseResources"), StringComparison.Ordinal);

        // Applied at launch, when either setting or the side changes, and on every hide (the edge read again).
        Assert.Contains("_coordinator.Hidden += (_, _) => ApplyEdgeReveal(preferences);", App, StringComparison.Ordinal);
        Assert.Matches(@"key == SettingKeys\.WorkspaceEdge\)\s*\{\s*_coordinator\?\.ChangeEdge\(DockEdges\.From\(preferences\.Edge\)\);\s*ApplyEdgeReveal\(preferences\);", Body(App, "OnSettingChanged"));
        Assert.Matches(@"_coordinator\.Hidden \+= \(_, _\) => ApplyEdgeReveal\(preferences\);\s*ApplyEdgeReveal\(preferences\);", App);
    }

    [Fact]
    public void It_is_installed_once_and_removed_once_however_often_it_is_started_and_stopped()
    {
        using GlobalHotkey owner = GlobalHotkey.Create();
        WindowHandle window = WindowHandle.FromHwnd(owner.Hwnd);

        Assert.False(EdgePointer.IsInstalled);

        for (int i = 0; i < 10; i++)
        {
            EdgePointer? pointer = EdgePointer.Start(window, static _ => { });
            Assert.NotNull(pointer);
            Assert.True(EdgePointer.IsInstalled);
            Assert.Throws<InvalidOperationException>(() => EdgePointer.Start(window, static _ => { }));

            pointer.Dispose();
            Assert.False(EdgePointer.IsInstalled);

            pointer.Dispose();
            Assert.False(EdgePointer.IsInstalled);
        }
    }

    // ---------------------------------------------------------------- the callback, minimal

    [Fact]
    public void The_callback_only_compares_and_passes_every_event_on_unchanged()
    {
        string onMouse = Body(Pointer, "OnMouse");

        // One return, the next hook's answer: nothing is swallowed. A failure is contained, never crossing into Windows,
        // and is the only thing traced.
        Assert.Equal(1, Regex.Count(onMouse, @"\breturn\b"));
        Assert.Matches(@"return NativeMethods\.CallNextHookEx\(0, code, message, data\);\s*\}$", onMouse);
        Assert.Matches(@"catch \(Exception ex\) when \(ex is not \(OutOfMemoryException or StackOverflowException\)\)", onMouse);
        Assert.True(onMouse.IndexOf("return NativeMethods.CallNextHookEx", StringComparison.Ordinal) > onMouse.IndexOf("catch (Exception", StringComparison.Ordinal));

        // Per event: the buttons, the state machine, at most one timer call. No allocation, no trace, no lock, no wait,
        // and none of the reads a dwell makes.
        string onPointer = Body(Pointer, "OnPointer");
        string perEvent = onMouse[..onMouse.IndexOf("catch (Exception", StringComparison.Ordinal)] + onPointer;
        string[] forbidden = ["new ", "$\"", "Trace.", "lock ", "Wait(", "Sleep(", "PresenceOf", "ForegroundFills", "InputElsewhere", "ForegroundMoving", "DisplayMonitors", "_reveal("];
        var used = forbidden.Where(f => perEvent.Contains(f, StringComparison.Ordinal)).ToList();
        Assert.True(used.Count == 0, "The hook does more than compare: " + string.Join(", ", used));
        Assert.Contains("_machine.Moved(x, y, _buttons, time, _configuration?.Options)", onPointer, StringComparison.Ordinal);

        // The machine it asks allocates nothing per event either.
        string machine = Body(Code(SourcePath("src", "Noto.Core", "Activation", "EdgeReveal.cs")), "Moved");
        Assert.DoesNotContain("new ", machine, StringComparison.Ordinal);
    }

    [Fact]
    public void The_hook_lives_on_a_thread_of_its_own_never_the_ui_thread()
    {
        string start = Body(Pointer, "Start");
        string run = Body(Pointer, "Run");

        Assert.Matches(@"new Thread\(\(\) => pointer\.Run\(ready\)\) \{ IsBackground = true", start);
        Assert.Matches(@"SetWindowsHookEx\(NativeMethods\.WH_MOUSE_LL, &OnMouse, ", run);
        Assert.Matches(@"while \(NativeMethods\.GetMessage\(&message, 0, 0, 0\) > 0\)[\s\S]*StopTimer\(\);\s*_ = NativeMethods\.UnhookWindowsHookEx\(_hook\);", run);
        Assert.Contains("PostThreadMessage(_threadId, NativeMethods.WM_QUIT, 0, 0)", Body(Pointer, "Dispose"), StringComparison.Ordinal);
        Assert.Equal(1, Regex.Count(Pointer, @"SetWindowsHookEx\("));
    }

    [Theory]
    [InlineData(0x0201u, PointerButtons.None, PointerButtons.Left)]
    [InlineData(0x0202u, PointerButtons.Left | PointerButtons.Right, PointerButtons.Right)]
    [InlineData(0x0204u, PointerButtons.Left, PointerButtons.Left | PointerButtons.Right)]
    [InlineData(0x0205u, PointerButtons.Right, PointerButtons.None)]
    [InlineData(0x0207u, PointerButtons.None, PointerButtons.Middle)]
    [InlineData(0x0208u, PointerButtons.Middle, PointerButtons.None)]
    [InlineData(0x020Bu, PointerButtons.None, PointerButtons.Extra)]
    [InlineData(0x020Cu, PointerButtons.Extra, PointerButtons.None)]
    [InlineData(0x0200u, PointerButtons.Left, PointerButtons.Left)]   // a move changes nothing
    [InlineData(0x020Au, PointerButtons.None, PointerButtons.None)]   // nor does the wheel
    public void The_buttons_held_follow_the_button_messages(uint message, PointerButtons before, PointerButtons after)
    {
        Assert.Equal(after, EdgePointer.Track(message, before));
    }

    // ---------------------------------------------------------------- the edge and the exclusions

    [Fact]
    public void The_edge_is_the_display_edge_over_the_work_area_less_a_corner_at_each_end()
    {
        var display = Display(new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1032), 96);

        EdgeZone zone = EdgePointer.ZoneFor(display, [display], DockEdge.Right);

        Assert.Equal(new EdgeZone(WorkspaceEdge.Right, 0, 1920, 48, 1032 - 48, Shared: false), zone);

        // The corner scales with the display: 48 DIP at 150% is 72 physical pixels.
        var scaled = Display(new PixelRect(0, 0, 2880, 1620), new PixelRect(0, 0, 2880, 1572), 144);
        Assert.Equal(72, EdgePointer.ZoneFor(scaled, [scaled], DockEdge.Left).Top);
        Assert.Equal(WorkspaceEdge.Left, EdgePointer.ZoneFor(scaled, [scaled], DockEdge.Left).Side);
    }

    [Fact]
    public void An_edge_another_display_touches_is_shared_and_one_beside_the_other_edge_is_not()
    {
        var primary = Display(new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1032), 96);
        var right = Display(new PixelRect(1920, 0, 3840, 1080), new PixelRect(1920, 0, 3840, 1080), 96, "\\\\.\\DISPLAY2");
        var below = Display(new PixelRect(0, 1080, 1920, 2160), new PixelRect(0, 1080, 1920, 2160), 96, "\\\\.\\DISPLAY3");

        Assert.True(EdgePointer.ZoneFor(primary, [primary, right], DockEdge.Right).Shared);
        Assert.False(EdgePointer.ZoneFor(primary, [primary, right], DockEdge.Left).Shared);
        Assert.False(EdgePointer.ZoneFor(primary, [primary, below], DockEdge.Right).Shared);
        Assert.True(EdgePointer.ZoneFor(right, [primary, right], DockEdge.Left).Shared);
    }

    [Theory]
    [InlineData(0, 0, 1920, 1080, 0x16000000L, true)]           // borderless, the whole display: full-screen
    [InlineData(-8, -8, 1928, 1088, 0x16CF0000L, false)]        // maximized under an auto-hiding taskbar: it keeps its caption
    [InlineData(0, 0, 1920, 1040, 0x16000000L, false)]          // borderless, but the taskbar shows
    [InlineData(1920, 0, 3840, 1080, 0x16000000L, false)]       // full-screen on another display
    public void Full_screen_is_covering_the_whole_display_with_no_caption(int left, int top, int right, int bottom, long style, bool fills)
    {
        Assert.Equal(fills, EdgePointer.FillsDisplay(new PixelRect(left, top, right, bottom), (nint)style, new PixelRect(0, 0, 1920, 1080)));
    }

    [Fact]
    public void A_dwell_reads_shown_full_screen_the_lock_and_a_window_move_once_when_it_elapses()
    {
        string due = Body(Pointer, "OnDue");

        Assert.Matches(@"Shown: WindowActivation\.PresenceOf\(_workspace\) is WorkspacePresence\.Background or WorkspacePresence\.Foreground,", due);
        Assert.Contains("Immersive: ForegroundFills(configuration.Display),", due, StringComparison.Ordinal);
        Assert.Contains("Locked: InputElsewhere(),", due, StringComparison.Ordinal);
        Assert.Contains("Moving: ForegroundMoving());", due, StringComparison.Ordinal);
        Assert.Matches(@"if \(decision\.Outcome == EdgeOutcome\.Reveal\)\s*\{\s*_reveal\(now\);\s*\}", due);

        // Decisions only, never a position.
        Assert.Contains("Trace.WriteLine($\"Noto.Edge gesture={decision.Gesture} outcome={decision.Outcome}\");", due, StringComparison.Ordinal);
        Assert.Contains("OpenInputDesktop(0, false, NativeMethods.DESKTOP_READOBJECTS)", Body(Pointer, "InputElsewhere"), StringComparison.Ordinal);
        Assert.Contains("(info.flags & NativeMethods.GUI_INMOVESIZE) != 0", Body(Pointer, "ForegroundMoving"), StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- the coordinator path

    [Fact]
    public void A_reveal_is_carried_to_the_ui_thread_and_shows_noto_through_the_coordinator_as_a_launch()
    {
        Assert.Contains(
            "_edgePointer = EdgePointer.Start(_windowHandle, requestTime => _ = ui.TryEnqueueGuarded(() => RevealAtEdge(requestTime)));",
            Body(App, "ApplyEdgeReveal"),
            StringComparison.Ordinal);
        Assert.Matches(
            @"if \(_closing \|\| _coordinator is null\)\s*\{\s*return;\s*\}\s*WorkspaceAction action = _coordinator\.OnActivationRequested\(WorkspaceRequest\.Launch, requestTime\);",
            Body(App, "RevealAtEdge"));
    }

    private static DisplayMonitor Display(PixelRect bounds, PixelRect workArea, uint dpi, string device = "\\\\.\\DISPLAY1") =>
        new(MonitorId.From(device), device, bounds, workArea, dpi, isPrimary: bounds.Left == 0 && bounds.Top == 0);

    private static string Code(string path) => Regex.Replace(File.ReadAllText(path), @"^\s*//.*$", string.Empty, RegexOptions.Multiline);

    private static string Body(string source, string method)
    {
        var declaration = Regex.Match(source, $@"(private|internal|public)( static)? [\w?<>,\s]+ {method}\(");
        Assert.True(declaration.Success, $"Not found: {method}");
        int open = source.IndexOf('{', declaration.Index);
        int depth = 0;

        for (int i = open; i < source.Length; i++)
        {
            depth += source[i] == '{' ? 1 : source[i] == '}' ? -1 : 0;

            if (depth == 0)
            {
                return source[open..(i + 1)];
            }
        }

        throw new InvalidOperationException($"Unbalanced body for {method}");
    }

    /// <summary>Walks up to the repository root; a guard that silently skips is worse than none.</summary>
    private static string SourcePath(params string[] parts)
    {
        string relative = Path.Join(parts);

        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Join(directory.FullName, relative);

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Could not locate {relative} by walking up from {AppContext.BaseDirectory}");
    }
}
