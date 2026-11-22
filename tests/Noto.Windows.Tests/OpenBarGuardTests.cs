using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Noto.Core.Activation;
using Noto.Platform.Windows;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// The Open Bar (#185 part 2; parity A3, J1, J27; ADR-007 §10), held below the window and in the source: a topmost tool
/// window that never activates, placed at the docked edge, that exists only while the setting is on, and whose click
/// and drop reach Noto through the coordinator as a launch. Nothing is shown on the screen here: what the running
/// application does with a real click is Invoke-Settings.ps1 case 24's to prove.
/// </summary>
public sealed partial class OpenBarGuardTests
{
    private const int ExStyle = -20, Style = -16;
    private const uint NoActivate = 0x08000000, ToolWindow = 0x00000080, Topmost = 0x00000008, Layered = 0x00080000, AcceptFiles = 0x00000010, Popup = 0x80000000;
    private const uint MouseActivate = 0x0021, HitTest = 0x0084, LeftUp = 0x0202, Close = 0x0010;

    private static readonly string Windows = Path.GetDirectoryName(SourcePath("src", "Noto.Windows", "App.xaml.cs"))!;
    private static readonly string App = Code(Path.Join(Windows, "App.xaml.cs"));
    private static readonly string Bar = Code(SourcePath("src", "Noto.Platform.Windows", "OpenBarWindow.cs"));

    // ---------------------------------------------------------------- the window

    [Fact]
    public void The_bar_is_a_topmost_tool_window_that_never_activates_and_is_destroyed_once()
    {
        using GlobalHotkey owner = GlobalHotkey.Create();
        WindowHandle workspace = WindowHandle.FromHwnd(owner.Hwnd);

        Assert.False(OpenBarWindow.IsCreated);

        for (int i = 0; i < 3; i++)
        {
            using OpenBarWindow bar = OpenBarWindow.Create(workspace, static (_, _) => { });
            nint hwnd = bar.Hwnd;
            uint ex = (uint)Native.GetWindowLong(hwnd, ExStyle);

            Assert.True(OpenBarWindow.IsCreated);
            Assert.Equal(NoActivate | ToolWindow | Topmost | Layered, ex & (NoActivate | ToolWindow | Topmost | Layered));
            Assert.Equal(Popup, (uint)Native.GetWindowLong(hwnd, Style) & Popup);
            Assert.Equal(OpenBarWindow.Title, Native.Text(hwnd));
            Assert.False(Native.IsWindowVisible(hwnd));
            Assert.Throws<InvalidOperationException>(() => OpenBarWindow.Create(workspace, static (_, _) => { }));

            // Restart Manager's WM_CLOSE does not end it; only Dispose does, once.
            _ = Native.SendMessage(hwnd, Close, 0, 0);
            Assert.True(Native.IsWindow(hwnd));

            bar.Dispose();
            Assert.False(Native.IsWindow(hwnd));
            Assert.False(OpenBarWindow.IsCreated);
            bar.Dispose();
        }
    }

    [Fact]
    public void A_click_opens_noto_takes_the_pointer_and_refuses_activation()
    {
        using GlobalHotkey owner = GlobalHotkey.Create();
        var opened = new List<int>();
        using OpenBarWindow bar = OpenBarWindow.Create(WindowHandle.FromHwnd(owner.Hwnd), (_, files) => opened.Add(files.Count));

        Assert.Equal(3, Native.SendMessage(bar.Hwnd, MouseActivate, 0, 0));   // MA_NOACTIVATE
        Assert.Equal(1, Native.SendMessage(bar.Hwnd, HitTest, 0, 0));         // HTCLIENT: a static control would let it through
        _ = Native.SendMessage(bar.Hwnd, LeftUp, 0, 0);

        Assert.Equal([0], opened);
    }

    [Fact]
    public void Configured_while_noto_is_shown_it_stays_hidden_and_takes_drops_only_when_on()
    {
        using GlobalHotkey owner = GlobalHotkey.Create();
        using OpenBarWindow bar = OpenBarWindow.Create(WindowHandle.FromHwnd(owner.Hwnd), static (_, _) => { });

        bar.Configure(OpenBarMode.AlwaysShown, drops: true, DockEdge.Right, notoHidden: false);
        Assert.False(Native.IsWindowVisible(bar.Hwnd));
        Assert.Equal(AcceptFiles, (uint)Native.GetWindowLong(bar.Hwnd, ExStyle) & AcceptFiles);

        bar.Configure(OpenBarMode.HideWhenIdle, drops: false, DockEdge.Left, notoHidden: false);
        Assert.False(Native.IsWindowVisible(bar.Hwnd));
        Assert.Equal(0u, (uint)Native.GetWindowLong(bar.Hwnd, ExStyle) & AcceptFiles);
    }

    [Theory]
    [InlineData(0, 0, 1920, 1032, 96u, DockEdge.Right, 1914, 468, 1920, 564)]       // 6 x 96, flush right, centred
    [InlineData(0, 0, 1920, 1032, 96u, DockEdge.Left, 0, 468, 6, 564)]
    [InlineData(0, 0, 2880, 1572, 144u, DockEdge.Right, 2871, 714, 2880, 858)]      // 150%: 9 x 144
    [InlineData(-1920, 0, 0, 1040, 96u, DockEdge.Left, -1920, 472, -1914, 568)]     // a display left of the primary
    [InlineData(48, 0, 1920, 1080, 96u, DockEdge.Left, 48, 492, 54, 588)]           // the taskbar on the docked side
    [InlineData(0, 0, 800, 60, 96u, DockEdge.Right, 794, 0, 800, 60)]               // never taller than the work area
    public void It_sits_flush_with_the_docked_edge_of_the_work_area_centred_at_the_display_scale(
        int left, int top, int right, int bottom, uint dpi, DockEdge side, int barLeft, int barTop, int barRight, int barBottom)
    {
        Assert.Equal(new PixelRect(barLeft, barTop, barRight, barBottom), OpenBarWindow.Placement(new PixelRect(left, top, right, bottom), dpi, side));
    }

    [Fact]
    public void Its_only_timer_is_the_fade_and_it_follows_the_foreground_out_of_context_for_the_full_screen_check()
    {
        Assert.Equal(1, Regex.Count(Bar, @"NativeMethods\.SetTimer\("));
        Assert.Contains("NativeMethods.SetTimer(_hwnd, FadeTimer, OpenBar.IdleDelay, null)", Body(Bar, "Apply"), StringComparison.Ordinal);
        Assert.Matches(@"SetWinEventHook\(\s*NativeMethods\.EVENT_SYSTEM_FOREGROUND,\s*NativeMethods\.EVENT_SYSTEM_FOREGROUND,[\s\S]*NativeMethods\.WINEVENT_OUTOFCONTEXT \| NativeMethods\.WINEVENT_SKIPOWNPROCESS\)", Body(Bar, "Create"));
        Assert.Contains("NativeMethods.UnhookWinEvent(_foreground)", Body(Bar, "Dispose"), StringComparison.Ordinal);

        // §9's own checks, reused: never over a full-screen application, never while the session is locked.
        Assert.Contains("EdgePointer.InputElsewhere() || EdgePointer.ForegroundFills(_display)", Body(Bar, "Refresh"), StringComparison.Ordinal);
        Assert.Contains("NativeMethods.DragAcceptFiles(_hwnd, drops);", Body(Bar, "Configure"), StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- the composition root

    [Fact]
    public void The_bar_exists_only_while_the_setting_is_on_and_is_destroyed_when_off_or_at_exit()
    {
        string apply = Body(App, "ApplyOpenBar");

        Assert.Matches(
            @"OpenBarMode mode = preferences\.OpenBar;\s*if \(mode == OpenBarMode\.Off \|\|[^)]*\)\s*\{\s*_openBar\?\.Dispose\(\);\s*_openBar = null;\s*return;\s*\}",
            apply);
        Assert.Matches(@"if \(_openBar is null\)\s*\{[\s\S]*_openBar = OpenBarWindow\.Create\(_windowHandle, OpenFromBar\);", apply);
        Assert.Contains("_openBar.Configure(mode, preferences.OpenBarDrop, _docked.Edge, hidden);", apply, StringComparison.Ordinal);
        Assert.Contains("WindowActivation.PresenceOf(_windowHandle) is WorkspacePresence.Hidden or WorkspacePresence.Minimized", apply, StringComparison.Ordinal);

        string allWindows = string.Join('\n', Directory.EnumerateFiles(Windows, "*.cs").Select(Code));
        Assert.Equal(1, Regex.Count(allWindows, @"OpenBarWindow\.Create\("));
        Assert.Contains("_openBar?.Dispose();", Body(App, "ReleaseResources"), StringComparison.Ordinal);
    }

    [Fact]
    public void It_is_applied_at_launch_on_every_show_and_hide_and_when_its_settings_or_the_side_change()
    {
        Assert.Matches(
            @"_coordinator\.Hidden \+= \(_, _\) => ApplyOpenBar\(preferences\);\s*_coordinator\.Shown \+= \(_, _\) => ApplyOpenBar\(preferences\);\s*ApplyOpenBar\(preferences\);",
            App);

        string changed = Body(App, "OnSettingChanged");
        Assert.Matches(@"key == SettingKeys\.OpenBarMode \|\| key == SettingKeys\.OpenBarDrop\)\s*\{\s*ApplyOpenBar\(preferences\);", changed);
        Assert.Matches(@"key == SettingKeys\.WorkspaceEdge\)\s*\{[^}]*ApplyOpenBar\(preferences\);", changed);

        // The coordinator says so after every action but a hide: show, restore, focus and a reversed hide.
        string coordinator = Code(Path.Join(Windows, "WindowCoordinator.cs"));
        Assert.Matches(@"if \(action != WorkspaceAction\.Hide\)\s*\{\s*Shown\?\.Invoke\(this, EventArgs\.Empty\);\s*\}\s*return action;", Body(coordinator, "Handle"));
    }

    [Fact]
    public void A_click_or_a_drop_shows_noto_through_the_coordinator_as_a_launch_and_the_files_go_where_a_drop_on_noto_would()
    {
        Assert.Matches(
            @"if \(_closing \|\| _coordinator is null\)\s*\{\s*return;\s*\}\s*WorkspaceAction action = _coordinator\.OnActivationRequested\(WorkspaceRequest\.Launch, requestTime\);[\s\S]*if \(files\.Count > 0\)\s*\{\s*_main\?\.ImportDroppedOnBar\(files\);",
            Body(App, "OpenFromBar"));

        // One import path for a drop on the stack, on a folder row and on the bar.
        string drop = Code(Path.Join(Windows, "MainWindow.Drop.cs"));
        Assert.Contains("await ImportOnStack(dropped.Paths, folder);", Body(drop, "OnNoteListDrop"), StringComparison.Ordinal);
        Assert.Contains("await ImportOnRow(dropped.Paths, row);", Body(drop, "OnFolderListDrop"), StringComparison.Ordinal);
        string bar = Body(drop, "ImportDroppedOnBar");
        Assert.Contains("await ImportOnStack(files, folder);", bar, StringComparison.Ordinal);
        Assert.Contains("await ImportOnRow(files, row);", bar, StringComparison.Ordinal);

        // A drop is taken only while its setting is on.
        Assert.Matches(@"if \(self\._drops && files\.Count > 0\)\s*\{\s*self\.Open\(files\);", Bar);
    }

    private static string Code(string path) => Regex.Replace(File.ReadAllText(path), @"^\s*//.*$", string.Empty, RegexOptions.Multiline);

    private static string Body(string source, string method)
    {
        var declaration = Regex.Match(source, $@"(private|internal|public)( static| async)? [\w?<>,\s]+ {method}\(");
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

    private static partial class Native
    {
        [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")]
        public static partial int GetWindowLong(nint hwnd, int index);

        [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
        public static partial nint SendMessage(nint hwnd, uint msg, nint wParam, nint lParam);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool IsWindow(nint hwnd);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool IsWindowVisible(nint hwnd);

        [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW")]
        private static unsafe partial int GetWindowText(nint hwnd, char* text, int max);

        public static unsafe string Text(nint hwnd)
        {
            char* text = stackalloc char[64];
            return new string(text, 0, GetWindowText(hwnd, text, 64));
        }
    }
}
