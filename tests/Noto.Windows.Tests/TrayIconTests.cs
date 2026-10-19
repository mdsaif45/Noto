using System.Runtime.InteropServices;
using Noto.Platform.Windows;
using Xunit;
using Xunit.Abstractions;

namespace Noto.Windows.Tests;

/// <summary>
/// The notification-area icon (#16 slice 7, ADR-014): its menu's rules and
/// callback decoding (deterministic), and its hidden window (runtime).
/// </summary>
/// <remarks>
/// The runtime tests deliver the shell's messages to the icon's own window
/// with <c>SendMessage</c>, as Explorer does — the routing is what is under
/// test. Real clicks, the real overflow and a real Explorer restart are the
/// runtime harness's (<c>tools/validation/Invoke-Tray.ps1</c>).
/// </remarks>
public sealed partial class TrayIconTests(ITestOutputHelper output)
{
    private const int Select = 0x0400, KeySelect = 0x0401, ContextMenu = 0x007B, LeftUp = 0x0202, RightUp = 0x0205, MouseMove = 0x0200;

    // ------------------------------------------------ the menu (deterministic)

    [Theory]
    [InlineData(WorkspacePresence.Hidden, true, false, true)]
    [InlineData(WorkspacePresence.Minimized, true, false, true)]
    [InlineData(WorkspacePresence.Background, true, true, true)]
    [InlineData(WorkspacePresence.Foreground, false, true, true)]
    public void The_menu_offers_what_can_change_something(WorkspacePresence presence, bool show, bool hide, bool keepOpen)
    {
        TrayMenuState state = TrayMenu.For(presence, pinned: false, coordinated: true);

        Assert.Equal(new TrayMenuState(show, hide, keepOpen, KeepOpenChecked: false), state);
    }

    [Theory]
    [InlineData(WorkspacePresence.Hidden)]
    [InlineData(WorkspacePresence.Background)]
    public void Keep_open_is_checked_exactly_when_pinned(WorkspacePresence presence)
    {
        Assert.True(TrayMenu.For(presence, pinned: true, coordinated: true).KeepOpenChecked);
        Assert.False(TrayMenu.For(presence, pinned: false, coordinated: true).KeepOpenChecked);
    }

    [Theory]
    [InlineData(WorkspacePresence.Hidden)]
    [InlineData(WorkspacePresence.Minimized)]
    [InlineData(WorkspacePresence.Background)]
    [InlineData(WorkspacePresence.Foreground)]
    public void Without_a_coordinator_only_show_and_quit_remain(WorkspacePresence presence)
    {
        TrayMenuState state = TrayMenu.For(presence, pinned: true, coordinated: false);

        Assert.False(state.HideEnabled);
        Assert.False(state.KeepOpenEnabled);
        Assert.False(state.KeepOpenChecked);
        Assert.Equal(presence != WorkspacePresence.Foreground, state.ShowEnabled);
    }

    [Fact]
    public void Only_an_offered_enabled_item_is_a_choice()
    {
        TrayMenuItem[] items =
        [
            new(TrayCommand.Show, "Show", Enabled: true),
            new(TrayCommand.Hide, "Hide", Enabled: false),
            new(TrayCommand.Quit, "Quit", Enabled: true),
        ];

        Assert.Equal(TrayCommand.Show, TrayIcon.Chosen((int)TrayCommand.Show, items));
        Assert.Equal(TrayCommand.Quit, TrayIcon.Chosen((int)TrayCommand.Quit, items));
        Assert.Null(TrayIcon.Chosen(0, items));                          // dismissed
        Assert.Null(TrayIcon.Chosen((int)TrayCommand.Hide, items));      // disabled
        Assert.Null(TrayIcon.Chosen((int)TrayCommand.KeepOpen, items));  // not offered
        Assert.Null(TrayIcon.Chosen((int)TrayCommand.Settings, items));  // not offered
        Assert.Null(TrayIcon.Chosen(99, items));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    public void Only_unsaved_text_blocks_and_never_a_critical_end(bool critical, bool blockRequested, bool blocks)
    {
        Assert.Equal(blocks, TrayIcon.ShouldBlock(critical, blockRequested));
    }

    // --------------------------------------------- callbacks (deterministic)

    [Theory]
    [InlineData(Select, (int)TrayEvent.Select)]
    [InlineData(KeySelect, (int)TrayEvent.Select)]
    [InlineData(ContextMenu, (int)TrayEvent.Menu)]
    [InlineData(LeftUp, (int)TrayEvent.None)]
    [InlineData(RightUp, (int)TrayEvent.None)]
    [InlineData(MouseMove, (int)TrayEvent.None)]
    [InlineData(0x0203, (int)TrayEvent.None)]
    public void A_callback_is_decoded_from_its_low_word(int eventCode, int expected)
    {
        Assert.Equal((TrayEvent)expected, TrayIcon.Decode(Callback(eventCode)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(0xFFFF)]
    public void A_callback_for_another_icon_id_is_ignored(int id)
    {
        Assert.Equal(TrayEvent.None, TrayIcon.Decode((nint)((id << 16) | Select)));
        Assert.Equal(TrayEvent.None, TrayIcon.Decode((nint)((id << 16) | ContextMenu)));
    }

    // ------------------------------------------------------ window (runtime)

    [Fact]
    [Trait("Category", DesktopSession.RequiresDesktopTrait)]
    public void The_receiver_is_a_hidden_top_level_tool_window_not_a_message_only_one()
    {
        if (!DesktopSession.ShouldRun(out string reason))
        {
            output.WriteLine(reason);
            return;
        }

        using var tray = TrayIcon.Create("Noto test");

        Assert.True(Native.IsWindow(tray.Hwnd));
        Assert.False(Native.IsWindowVisible(tray.Hwnd));
        Assert.Equal(tray.Hwnd, Native.GetAncestor(tray.Hwnd, 2));                       // GA_ROOT: top-level
        Assert.Equal(0, Native.FindWindowEx(-3, 0, "STATIC", "Noto tray"));              // not among message-only windows
        Assert.NotEqual(0, Native.GetWindowLong(tray.Hwnd, -20) & 0x80);                 // WS_EX_TOOLWINDOW: no taskbar button
        Assert.True(tray.IsAttached);
    }

    [Fact]
    [Trait("Category", DesktopSession.RequiresDesktopTrait)]
    public void A_select_raises_invoked_once_and_a_key_select_too()
    {
        if (!DesktopSession.ShouldRun(out string reason))
        {
            output.WriteLine(reason);
            return;
        }

        using var tray = TrayIcon.Create("Noto test");
        var invoked = 0;
        var menus = 0;
        tray.Invoked += (_, _) => invoked++;
        tray.MenuRequested += (_, _) => menus++;

        Send(tray, Select);
        Send(tray, KeySelect);
        Send(tray, LeftUp);
        Send(tray, MouseMove);
        Send(tray, (2 << 16) | Select);

        Assert.Equal(2, invoked);
        Assert.Equal(0, menus);
    }

    [Fact]
    [Trait("Category", DesktopSession.RequiresDesktopTrait)]
    public void The_receive_time_is_read_locally_never_from_the_message_time()
    {
        if (!DesktopSession.ShouldRun(out string reason))
        {
            output.WriteLine(reason);
            return;
        }

        // Explorer sends the callback, so GetMessageTime then reports the last
        // message this thread retrieved — measured stale by seconds. Make that
        // time old on purpose, then deliver the callback as a sent message.
        using var tray = TrayIcon.Create("Noto test");
        int received = 0;
        tray.Invoked += (_, e) => received = e.ReceivedAt;
        Assert.True(Native.PostMessage(tray.Hwnd, 0, 0, 0));
        Assert.True(Native.PeekMessage(out Native.Msg posted, tray.Hwnd, 0, 0, 1));
        int staleMessageTime = (int)posted.Time;
        Thread.Sleep(200);
        int before = Environment.TickCount;

        Send(tray, Select);

        Assert.True(WorkspaceToggle.IsBefore(staleMessageTime, before));
        Assert.False(WorkspaceToggle.IsBefore(received, before), $"received {received} is earlier than the send at {before}");

        // So a launch made by this click is not mistaken for one queued behind
        // a show that ended just before it — which the stale time would be.
        var context = new ActivationContext(false, false, false, received, ReadyTime: before - 1000, LastTransitionEnd: before - 1);
        Assert.Equal(WorkspaceAction.Show, WorkspaceToggle.Decide(WorkspaceRequest.Launch, WorkspacePresence.Hidden, context));
        Assert.Equal(WorkspaceAction.None, WorkspaceToggle.Decide(WorkspaceRequest.Launch, WorkspacePresence.Hidden, context with { RequestTime = staleMessageTime }));
    }

    [Fact]
    [Trait("Category", DesktopSession.RequiresDesktopTrait)]
    public void A_context_menu_request_carries_the_anchor_point()
    {
        if (!DesktopSession.ShouldRun(out string reason))
        {
            output.WriteLine(reason);
            return;
        }

        using var tray = TrayIcon.Create("Noto test");
        TrayMenuRequestedEventArgs? request = null;
        tray.MenuRequested += (_, e) => request = e;

        _ = Native.SendMessage(tray.Hwnd, TrayIcon.CallbackMessage, (nint)((955 << 16) | 1597), Callback(ContextMenu));

        Assert.NotNull(request);
        Assert.Equal((1597, 955), (request.X, request.Y));
    }

    [Fact]
    [Trait("Category", DesktopSession.RequiresDesktopTrait)]
    public void A_command_message_from_outside_chooses_nothing()
    {
        if (!DesktopSession.ShouldRun(out string reason))
        {
            output.WriteLine(reason);
            return;
        }

        // The menu returns its choice to the caller; WM_COMMAND is not handled,
        // so another process cannot pick Quit by sending one.
        using var tray = TrayIcon.Create("Noto test");
        var raised = 0;
        tray.Invoked += (_, _) => raised++;
        tray.MenuRequested += (_, _) => raised++;
        tray.SessionEnding += (_, _) => raised++;
        tray.SessionEnded += (_, _) => raised++;

        for (int command = 1; command <= 4; command++)
        {
            _ = Native.SendMessage(tray.Hwnd, 0x0111, command, 0);
        }

        Assert.Equal(0, raised);
        Assert.True(Native.IsWindow(tray.Hwnd));
    }

    [Fact]
    [Trait("Category", DesktopSession.RequiresDesktopTrait)]
    public void A_session_query_is_allowed_unless_the_handler_blocks_and_a_critical_one_always()
    {
        if (!DesktopSession.ShouldRun(out string reason))
        {
            output.WriteLine(reason);
            return;
        }

        using var tray = TrayIcon.Create("Noto test");
        WindowHandle self = WindowHandle.FromHwnd(tray.Hwnd);
        bool block = false;
        var queries = new List<bool>();
        tray.SessionEnding += (_, e) =>
        {
            queries.Add(e.Critical);
            if (block)
            {
                e.Block(self, "test: unsaved");
            }
        };

        Assert.Equal(1, Native.SendMessage(tray.Hwnd, 0x0011, 0, 0));
        block = true;
        Assert.Equal(0, Native.SendMessage(tray.Hwnd, 0x0011, 0, 0));
        Assert.Equal(1, Native.SendMessage(tray.Hwnd, 0x0011, 0, unchecked((nint)0x40000000)));   // ENDSESSION_CRITICAL

        Assert.Equal([false, false, true], queries.ToArray());
        _ = Native.SendMessage(tray.Hwnd, 0x0016, 0, 0);   // cancelled: the block reason goes
    }

    [Fact]
    [Trait("Category", DesktopSession.RequiresDesktopTrait)]
    public void The_session_end_is_reported_with_whether_it_ends()
    {
        if (!DesktopSession.ShouldRun(out string reason))
        {
            output.WriteLine(reason);
            return;
        }

        using var tray = TrayIcon.Create("Noto test");
        var ended = new List<bool>();
        tray.SessionEnded += (_, e) => ended.Add(e.Ending);

        _ = Native.SendMessage(tray.Hwnd, 0x0016, 0, 0);
        _ = Native.SendMessage(tray.Hwnd, 0x0016, 1, 0);

        Assert.Equal([false, true], ended.ToArray());
    }

    [Fact]
    [Trait("Category", DesktopSession.RequiresDesktopTrait)]
    public void A_throwing_handler_is_contained_and_a_failed_query_lets_the_session_end()
    {
        if (!DesktopSession.ShouldRun(out string reason))
        {
            output.WriteLine(reason);
            return;
        }

        using var tray = TrayIcon.Create("Noto test");
        var reached = 0;
        tray.Invoked += (_, _) => reached++;
        tray.Invoked += (_, _) => throw new InvalidOperationException("handler failure");
        tray.SessionEnding += (_, _) => throw new InvalidOperationException("handler failure");

        Send(tray, Select);
        Send(tray, Select);

        Assert.Equal(2, reached);
        Assert.NotEqual(0, Native.SendMessage(tray.Hwnd, 0x0011, 0, 0));
        Assert.True(tray.IsAttached);
    }

    [Fact]
    [Trait("Category", DesktopSession.RequiresDesktopTrait)]
    public void Taskbar_created_adds_the_icon_again_without_a_duplicate()
    {
        if (!DesktopSession.ShouldRun(out string reason))
        {
            output.WriteLine(reason);
            return;
        }

        using var tray = TrayIcon.Create("Noto test");
        uint taskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");
        var readded = 0;
        tray.Readded += (_, _) => readded++;
        bool shell = Native.FindWindow("Shell_TrayWnd", null) != 0;

        _ = Native.SendMessage(tray.Hwnd, taskbarCreated, 0, 0);
        _ = Native.SendMessage(tray.Hwnd, taskbarCreated, 0, 0);

        Assert.Equal(2, readded);
        if (!shell)
        {
            output.WriteLine("No taskbar on this desktop: the icon itself cannot be checked.");
            return;
        }

        Assert.True(tray.IsAdded, $"error {tray.LastError}");
        Assert.Equal(0, IconRect(tray));
    }

    [Fact]
    [Trait("Category", DesktopSession.RequiresDesktopTrait)]
    public void Disposing_deletes_the_icon_destroys_the_window_and_is_idempotent()
    {
        if (!DesktopSession.ShouldRun(out string reason))
        {
            output.WriteLine(reason);
            return;
        }

        using var tray = TrayIcon.Create("Noto test");

        tray.Dispose();

        Assert.False(tray.IsAdded);
        Assert.False(tray.IsAttached);
        Assert.False(Native.IsWindow(tray.Hwnd));
        tray.Dispose();
        Assert.Throws<ObjectDisposedException>(() => tray.ShowMenu(new TrayMenuRequestedEventArgs(0, 0), [], _ => { }));
    }

    [Fact]
    [Trait("Category", DesktopSession.RequiresDesktopTrait)]
    public void Disposing_from_inside_its_own_event_is_safe()
    {
        if (!DesktopSession.ShouldRun(out string reason))
        {
            output.WriteLine(reason);
            return;
        }

        // Quit closes the window from inside the menu's callback, and the
        // window's Closed disposes the icon there: the same re-entrancy.
        var tray = TrayIcon.Create("Noto test");
        tray.Invoked += (_, _) => tray.Dispose();

        Send(tray, Select);

        Assert.False(tray.IsAttached);
        Assert.False(Native.IsWindow(tray.Hwnd));
        tray.Dispose();
    }

    [Fact]
    [Trait("Category", DesktopSession.RequiresDesktopTrait)]
    public void A_close_message_does_not_destroy_the_icon_window()
    {
        if (!DesktopSession.ShouldRun(out string reason))
        {
            output.WriteLine(reason);
            return;
        }

        // Restart Manager sends WM_CLOSE to every top-level window of the
        // process; only the application's Closed may end this one.
        using var tray = TrayIcon.Create("Noto test");

        _ = Native.SendMessage(tray.Hwnd, 0x0010, 0, 0);

        Assert.True(Native.IsWindow(tray.Hwnd));
        Assert.True(tray.IsAttached);
    }

    [Fact]
    [Trait("Category", DesktopSession.RequiresDesktopTrait)]
    public void A_close_request_to_a_window_already_gone_does_nothing()
    {
        if (!DesktopSession.ShouldRun(out string reason))
        {
            output.WriteLine(reason);
            return;
        }

        // The idempotence the session's end relies on: Restart Manager sends its
        // own WM_CLOSE after Noto posts one, and the second finds no window.
        var tray = TrayIcon.Create("Noto test");
        WindowHandle gone = WindowHandle.FromHwnd(tray.Hwnd);
        tray.Dispose();

        WindowClose.Post(gone);
        WindowClose.Request(gone);

        Assert.False(Native.IsWindow(tray.Hwnd));
    }

    private static nint Callback(int eventCode) => (nint)((TrayIcon.IconId << 16) | (uint)eventCode);

    private static void Send(TrayIcon tray, int eventCode) =>
        _ = Native.SendMessage(tray.Hwnd, TrayIcon.CallbackMessage, 0, eventCode > 0xFFFF ? eventCode : Callback(eventCode));

    private static int IconRect(TrayIcon tray)
    {
        var id = new Native.NotifyIconIdentifier { Size = (uint)Marshal.SizeOf<Native.NotifyIconIdentifier>(), Hwnd = tray.Hwnd, Id = TrayIcon.IconId };
        return Native.Shell_NotifyIconGetRect(in id, out _);
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

        [StructLayout(LayoutKind.Sequential)]
        public struct NotifyIconIdentifier
        {
            public uint Size;
            public nint Hwnd;
            public uint Id;
            public Guid Guid;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [LibraryImport("shell32.dll")]
        public static partial int Shell_NotifyIconGetRect(in NotifyIconIdentifier identifier, out Rect rect);

        [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
        public static partial nint SendMessage(nint hwnd, uint msg, nint wParam, nint lParam);

        [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool PostMessage(nint hwnd, uint msg, nint wParam, nint lParam);

        [LibraryImport("user32.dll", EntryPoint = "PeekMessageW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool PeekMessage(out Msg message, nint hwnd, uint min, uint max, uint remove);

        [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", StringMarshalling = StringMarshalling.Utf16)]
        public static partial uint RegisterWindowMessage(string name);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool IsWindow(nint hwnd);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool IsWindowVisible(nint hwnd);

        [LibraryImport("user32.dll")]
        public static partial nint GetAncestor(nint hwnd, uint flags);

        [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")]
        public static partial int GetWindowLong(nint hwnd, int index);

        [LibraryImport("user32.dll", EntryPoint = "FindWindowExW", StringMarshalling = StringMarshalling.Utf16)]
        public static partial nint FindWindowEx(nint parent, nint after, string className, string windowName);

        [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
        public static partial nint FindWindow(string className, string? windowName);
    }

    [Fact]
    public void Settings_is_appended_so_every_existing_menu_id_is_unchanged()
    {
        // The tray harness reads the menu's rows by these ids (MN_GETHMENU); Settings (Slice 1) adds an id, never moves one.
        Assert.Equal(1, (int)TrayCommand.Show);
        Assert.Equal(2, (int)TrayCommand.Hide);
        Assert.Equal(3, (int)TrayCommand.KeepOpen);
        Assert.Equal(4, (int)TrayCommand.Quit);
        Assert.Equal(5, (int)TrayCommand.StartWithWindows);
        Assert.Equal(6, (int)TrayCommand.Settings);
    }
}
