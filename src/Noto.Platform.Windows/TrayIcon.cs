using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Noto.Platform.Windows;

/// <summary>A command chosen from the notification-area icon's menu (#16 slice 7, parity A6).</summary>
public enum TrayCommand
{
    /// <summary>Show Noto: a launch — show, restore or bring forward, never hide.</summary>
    Show = 1,

    /// <summary>Hide Noto: a dismissal — saves first, never shows.</summary>
    Hide,

    /// <summary>Keep Noto open: flips the session pin (parity A9).</summary>
    KeepOpen,

    /// <summary>Quit Noto: the close Alt+F4 makes.</summary>
    Quit,

    /// <summary>Start Noto with Windows: flips the sign-in registration (#94, ADR-015).</summary>
    StartWithWindows,

    /// <summary>Show Noto with its settings open (Slice 1). Appended: the ids above are the harness's.</summary>
    Settings,
}

/// <summary>One row of the menu.</summary>
/// <param name="Command">What choosing it does.</param>
/// <param name="Label">Its text, from the application.</param>
/// <param name="Enabled">Whether it can be chosen now.</param>
/// <param name="Checked">Whether it shows a check mark.</param>
/// <param name="SeparatorBefore">Whether a separator line comes before it.</param>
public readonly record struct TrayMenuItem(TrayCommand Command, string Label, bool Enabled, bool Checked = false, bool SeparatorBefore = false);

/// <summary>Which menu rows are available, for where the workspace is.</summary>
/// <param name="ShowEnabled">Show can change something: the workspace is not already in front.</param>
/// <param name="HideEnabled">Hide can change something: the workspace is shown, and there is a coordinator to hide it.</param>
/// <param name="KeepOpenEnabled">Pin applies: there is a coordinator.</param>
/// <param name="KeepOpenChecked">Pinned for this session.</param>
public readonly record struct TrayMenuState(bool ShowEnabled, bool HideEnabled, bool KeepOpenEnabled, bool KeepOpenChecked);

/// <summary>The menu's rules (#16 slice 7, ADR-014), as a pure function.</summary>
/// <remarks>
/// The menu only offers; what a choice does is decided again, when it is made,
/// by the coordinator against where the workspace is then. Quit is always
/// available and so is not part of the state.
/// </remarks>
public static class TrayMenu
{
    /// <summary>The rows for a workspace at <paramref name="presence"/>.</summary>
    /// <param name="presence">Where the workspace is, read before the menu takes the foreground.</param>
    /// <param name="pinned">Whether it is pinned for this session.</param>
    /// <param name="coordinated">Whether a coordinator exists (docking succeeded).</param>
    public static TrayMenuState For(WorkspacePresence presence, bool pinned, bool coordinated) => new(
        ShowEnabled: presence != WorkspacePresence.Foreground,
        HideEnabled: coordinated && presence is WorkspacePresence.Background or WorkspacePresence.Foreground,
        KeepOpenEnabled: coordinated,
        KeepOpenChecked: coordinated && pinned);
}

/// <summary>The icon was selected: a click, or Enter or Space on it.</summary>
/// <param name="receivedAt">When Noto received the selection; see <see cref="ReceivedAt"/>.</param>
public sealed class TrayInvokedEventArgs(int receivedAt) : EventArgs
{
    /// <summary>
    /// When the callback was handled, on the <see cref="Environment.TickCount"/>
    /// clock — read locally, never from <c>GetMessageTime</c>: Windows sends
    /// the callback rather than posting it, so the message time is the time of
    /// some earlier message (measured, #16 slice 7 Phase 0).
    /// </summary>
    public int ReceivedAt { get; } = receivedAt;
}

/// <summary>The icon's menu was asked for: a right-click, Shift+F10 or the menu key.</summary>
public sealed class TrayMenuRequestedEventArgs : EventArgs
{
    internal TrayMenuRequestedEventArgs(int x, int y)
    {
        X = x;
        Y = y;
    }

    /// <summary>Where the menu opens, in screen pixels: the point Windows gave with the request.</summary>
    internal int X { get; }

    /// <inheritdoc cref="X"/>
    internal int Y { get; }
}

/// <summary>Windows asks whether the session may end (<c>WM_QUERYENDSESSION</c>).</summary>
public sealed class SessionEndingEventArgs : EventArgs
{
    internal SessionEndingEventArgs(bool critical) => Critical = critical;

    /// <summary>A critical shutdown: Windows ends the session whatever Noto answers.</summary>
    public bool Critical { get; }

    internal WindowHandle? BlockWindow { get; private set; }

    internal string? BlockReason { get; private set; }

    /// <summary>
    /// Asks Windows not to end the session, with <paramref name="reason"/>
    /// shown against <paramref name="window"/> on its shutdown screen. Ignored
    /// for a critical shutdown, which cannot be refused.
    /// </summary>
    public void Block(WindowHandle window, string reason)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        BlockWindow = window;
        BlockReason = reason;
    }
}

/// <summary>Windows says whether the session is ending after all (<c>WM_ENDSESSION</c>).</summary>
/// <param name="ending">Whether the session ends now.</param>
public sealed class SessionEndedEventArgs(bool ending) : EventArgs
{
    /// <summary>The session ends now; <see langword="false"/> when it was cancelled.</summary>
    public bool Ending { get; } = ending;
}

/// <summary>
/// Noto's notification-area icon and its menu, on a hidden top-level window
/// (#16 slice 7, ADR-014).
/// </summary>
/// <remarks>
/// <para>
/// <b>An input surface only.</b> It raises what the user did and what Windows
/// asked; it never shows, hides or closes anything itself. The application
/// hands every request to the coordinator or to the close path.
/// </para>
/// <para>
/// <b>A hidden top-level window, not a message-only one.</b> <c>TaskbarCreated</c>
/// is broadcast, and so are the session messages: a message-only window
/// receives neither (measured, Phase 0). The window is <c>WS_POPUP</c> and a
/// tool window, never shown, so it has no taskbar button and no Alt+Tab entry.
/// It exists whether or not docking succeeded, which is why the session
/// messages are taken here.
/// </para>
/// <para>
/// <b>Identity: this window and one id</b>, not a GUID. Windows ties a GUID
/// icon to the executable's path while it is unsigned, and Noto runs from
/// several paths (#5). Adding an icon that already exists fails rather than
/// duplicating it (measured), so re-adding is idempotent.
/// </para>
/// <para>
/// <b>Explorer restarts.</b> On <c>TaskbarCreated</c> the icon is deleted, if
/// it still exists, and added again. Nothing else happens. A failed add is not
/// fatal: the next <c>TaskbarCreated</c> tries again.
/// </para>
/// <para>
/// <b>Thread.</b> Create it on the UI thread. Every event is raised there,
/// from inside the window procedure; a handler that throws is contained.
/// </para>
/// </remarks>
public sealed unsafe class TrayIcon : IDisposable
{
    /// <summary>The message the icon's callbacks arrive as.</summary>
    internal const uint CallbackMessage = NativeMethods.WM_APP + 1;

    /// <summary>The icon's id under this window. Callbacks for any other id are not ours.</summary>
    internal const uint IconId = 1;

    private const nuint SubclassId = 3;

    private readonly nint _hwnd;
    private readonly uint _taskbarCreated;
    private readonly string _tooltip;
    private readonly nint _icon;
    private GCHandle _self;
    private bool _disposed;
    private bool _inMenu;
    private WindowHandle? _blocking;

    private TrayIcon(nint hwnd, uint taskbarCreated, string tooltip)
    {
        _hwnd = hwnd;
        _taskbarCreated = taskbarCreated;
        _tooltip = tooltip;

        // #95: the executable's own icon, at the shell's small-icon size so
        // Windows does not scale it; the system's generic icon only when the
        // executable has none (a test host).
        nint own = NativeMethods.LoadImage(
            NativeMethods.GetModuleHandle(null),
            NativeMethods.AppIconResource,
            NativeMethods.IMAGE_ICON,
            NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSMICON),
            NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSMICON),
            0);
        HasOwnIcon = own != 0;
        _icon = HasOwnIcon ? own : NativeMethods.LoadIcon(0, NativeMethods.IDI_APPLICATION);
    }

    /// <summary>The icon was selected. The application treats it as a launch.</summary>
    public event EventHandler<TrayInvokedEventArgs>? Invoked;

    /// <summary>The menu was asked for. Answer with <see cref="ShowMenu"/>, from the handler.</summary>
    public event EventHandler<TrayMenuRequestedEventArgs>? MenuRequested;

    /// <summary>
    /// Windows asks whether the session may end. A handler saves synchronously
    /// and may <see cref="SessionEndingEventArgs.Block"/>; nothing may be
    /// deferred, because the process can end as soon as the session does.
    /// </summary>
    public event EventHandler<SessionEndingEventArgs>? SessionEnding;

    /// <summary>Windows says whether the session ends after all.</summary>
    public event EventHandler<SessionEndedEventArgs>? SessionEnded;

    /// <summary>Explorer restarted and the icon was added again; <see cref="IsAdded"/> says whether that worked.</summary>
    public event EventHandler? Readded;

    /// <summary>Whether the last attempt to add the icon succeeded.</summary>
    public bool IsAdded { get; private set; }

    /// <summary>
    /// Whether the icon is the executable's own (#95), loaded here and
    /// destroyed with the tray, rather than Windows' shared generic one.
    /// </summary>
    public bool HasOwnIcon { get; }

    /// <summary>The Win32 error of the last failed add; 0 after a success.</summary>
    public int LastError { get; private set; }

    /// <summary>The hidden window, for this assembly and its tests.</summary>
    internal nint Hwnd => _hwnd;

    /// <summary>Whether the subclass is still installed. Cleared on <c>WM_NCDESTROY</c>.</summary>
    internal bool IsAttached { get; private set; }

    /// <summary>Creates the hidden window on the calling thread and adds the icon.</summary>
    /// <param name="tooltip">The icon's tooltip, at most 127 characters.</param>
    /// <exception cref="Win32Exception">Windows refused the window or its subclass. A refused icon is not an exception: see <see cref="IsAdded"/>.</exception>
    public static TrayIcon Create(string tooltip)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tooltip);

        if (tooltip.Length > 127)
        {
            throw new ArgumentOutOfRangeException(nameof(tooltip), "A tooltip holds at most 127 characters.");
        }

        nint hwnd = NativeMethods.CreateWindowEx(NativeMethods.WS_EX_TOOLWINDOW, "STATIC", "Noto tray", NativeMethods.WS_POPUP, 0, 0, 0, 0, 0, 0, 0, 0);

        if (hwnd == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        var tray = new TrayIcon(hwnd, NativeMethods.RegisterWindowMessage("TaskbarCreated"), tooltip);
        tray._self = GCHandle.Alloc(tray);

        if (!NativeMethods.SetWindowSubclass(hwnd, &Proc, SubclassId, (nuint)GCHandle.ToIntPtr(tray._self)))
        {
            int error = Marshal.GetLastPInvokeError();
            tray._self.Free();
            _ = NativeMethods.DestroyWindow(hwnd);
            throw new Win32Exception(error);
        }

        tray.IsAttached = true;
        tray.Add();
        return tray;
    }

    /// <summary>
    /// Shows the menu at the point the request gave, waits for the user, and
    /// passes a chosen command to <paramref name="onCommand"/> — before the
    /// foreground is tidied up, so the command can bring Noto forward.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The menu window must be in front, or it does not close when the user
    /// clicks elsewhere: the hidden window takes the foreground first. Windows
    /// granted that at every measured request (Phase 0, M2). That window is
    /// not the workspace, so the activation it brings is one that did not land
    /// and a hidden workspace stays hidden (ADR-007 §4).
    /// </para>
    /// <para>
    /// The chosen command is returned by the menu loop, never sent as
    /// <c>WM_COMMAND</c>: nothing outside this call can choose one. A second
    /// request while the menu is open is ignored.
    /// </para>
    /// <para>
    /// Afterwards the hidden window is never left in front: if it still is,
    /// the foreground goes back to the window that had it.
    /// </para>
    /// </remarks>
    public void ShowMenu(TrayMenuRequestedEventArgs request, IReadOnlyList<TrayMenuItem> items, Action<TrayCommand> onCommand)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(onCommand);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_inMenu)
        {
            return;
        }

        nint menu = NativeMethods.CreatePopupMenu();

        if (menu == 0)
        {
            return;
        }

        _inMenu = true;
        nint previous = NativeMethods.GetForegroundWindow();

        try
        {
            foreach (TrayMenuItem item in items)
            {
                if (item.SeparatorBefore)
                {
                    _ = NativeMethods.AppendMenu(menu, NativeMethods.MF_SEPARATOR, 0, null);
                }

                uint flags = NativeMethods.MF_STRING
                    | (item.Enabled ? 0 : NativeMethods.MF_GRAYED)
                    | (item.Checked ? NativeMethods.MF_CHECKED : 0);
                _ = NativeMethods.AppendMenu(menu, flags, (nuint)item.Command, item.Label);
            }

            _ = NativeMethods.SetForegroundWindow(_hwnd);
            int chosen = NativeMethods.TrackPopupMenuEx(
                menu,
                NativeMethods.TPM_RETURNCMD | NativeMethods.TPM_RIGHTBUTTON | NativeMethods.TPM_NONOTIFY,
                request.X,
                request.Y,
                _hwnd,
                0);

            // KB135788: lets the menu close properly when the user clicks away.
            _ = NativeMethods.PostMessage(_hwnd, NativeMethods.WM_NULL, 0, 0);

            if (Chosen(chosen, items) is TrayCommand command)
            {
                onCommand(command);
            }
        }
        finally
        {
            _ = NativeMethods.DestroyMenu(menu);
            _inMenu = false;

            if (!_disposed && NativeMethods.GetForegroundWindow() == _hwnd && previous != 0 && previous != _hwnd && NativeMethods.IsWindow(previous))
            {
                _ = NativeMethods.SetForegroundWindow(previous);
            }
        }
    }

    /// <summary>Deletes the icon and destroys the window. Idempotent, and safe from inside one of this icon's own events.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ReleaseBlock();
        Delete();

        // WM_NCDESTROY, handled below, removes the subclass and frees the handle.
        _ = NativeMethods.DestroyWindow(_hwnd);

        // Only an icon this tray loaded; the shared generic one is never destroyed.
        if (HasOwnIcon)
        {
            _ = NativeMethods.DestroyIcon(_icon);
        }
    }

    /// <summary>
    /// Whether to refuse the session's end: only with unsaved text, and never
    /// for a critical shutdown, which cannot be refused.
    /// </summary>
    internal static bool ShouldBlock(bool critical, bool blockRequested) => blockRequested && !critical;

    /// <summary>What a callback's <c>lParam</c> says, for the v4 protocol: the event in the low word, the icon id in the high word.</summary>
    internal static TrayEvent Decode(nint lParam)
    {
        if ((uint)((lParam >> 16) & 0xFFFF) != IconId)
        {
            return TrayEvent.None;
        }

        return (uint)(lParam & 0xFFFF) switch
        {
            NativeMethods.NIN_SELECT or NativeMethods.NIN_KEYSELECT => TrayEvent.Select,
            NativeMethods.WM_CONTEXTMENU => TrayEvent.Menu,
            _ => TrayEvent.None,
        };
    }

    /// <summary>The command a menu loop returned, if it is one of the items offered and enabled.</summary>
    internal static TrayCommand? Chosen(int returned, IReadOnlyList<TrayMenuItem> items)
    {
        foreach (TrayMenuItem item in items)
        {
            if ((int)item.Command == returned && item.Enabled)
            {
                return item.Command;
            }
        }

        return null;
    }

    internal void OnCallback(nint wParam, nint lParam)
    {
        // Local receive time: the callback is sent, so GetMessageTime is stale.
        int receivedAt = Environment.TickCount;

        switch (Decode(lParam))
        {
            case TrayEvent.Select:
                Invoked?.Invoke(this, new TrayInvokedEventArgs(receivedAt));
                break;

            case TrayEvent.Menu when !_inMenu:
                // v4: the anchor point is in wParam, in screen coordinates.
                MenuRequested?.Invoke(this, new TrayMenuRequestedEventArgs((short)(wParam & 0xFFFF), (short)((wParam >> 16) & 0xFFFF)));
                break;

            default:
                break;
        }
    }

    internal bool OnQueryEndSession(nint lParam)
    {
        var args = new SessionEndingEventArgs(((uint)lParam & NativeMethods.ENDSESSION_CRITICAL) != 0);
        ReleaseBlock();
        SessionEnding?.Invoke(this, args);

        if (!ShouldBlock(args.Critical, args.BlockReason is not null) || args.BlockWindow is not { } window)
        {
            return true;
        }

        _blocking = window;
        _ = NativeMethods.ShutdownBlockReasonCreate(window.Hwnd, args.BlockReason!);
        return false;
    }

    internal void OnEndSession(bool ending)
    {
        ReleaseBlock();
        SessionEnded?.Invoke(this, new SessionEndedEventArgs(ending));
    }

    internal void OnTaskbarCreated()
    {
        if (_disposed)
        {
            return;
        }

        Delete();
        Add();
        Readded?.Invoke(this, EventArgs.Empty);
    }

    private void Add()
    {
        NativeMethods.NOTIFYICONDATAW data = Data();

        if (NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, &data))
        {
            _ = NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_SETVERSION, &data);
            IsAdded = true;
            LastError = 0;
        }
        else
        {
            IsAdded = false;
            LastError = Marshal.GetLastPInvokeError();
        }
    }

    private void Delete()
    {
        NativeMethods.NOTIFYICONDATAW data = Data();
        _ = NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, &data);
        IsAdded = false;
    }

    private NativeMethods.NOTIFYICONDATAW Data()
    {
        var data = new NativeMethods.NOTIFYICONDATAW
        {
            cbSize = (uint)sizeof(NativeMethods.NOTIFYICONDATAW),
            hWnd = _hwnd,
            uID = IconId,
            uFlags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP | NativeMethods.NIF_SHOWTIP,
            uCallbackMessage = CallbackMessage,
            hIcon = _icon,
            uTimeoutOrVersion = NativeMethods.NOTIFYICON_VERSION_4,
        };

        for (int i = 0; i < _tooltip.Length; i++)
        {
            data.szTip[i] = _tooltip[i];
        }

        return data;
    }

    private void ReleaseBlock()
    {
        if (_blocking is { } window)
        {
            _ = NativeMethods.ShutdownBlockReasonDestroy(window.Hwnd);
            _blocking = null;
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
        if (GCHandle.FromIntPtr((nint)refData).Target is not TrayIcon self)
        {
            return WindowSubclass.CallDefault(hwnd, msg, wParam, lParam);
        }

        try
        {
            if (msg == CallbackMessage)
            {
                self.OnCallback(wParam, lParam);
                return 0;
            }

            if (self._taskbarCreated != 0 && msg == self._taskbarCreated)
            {
                self.OnTaskbarCreated();
                return 0;
            }

            switch (msg)
            {
                case NativeMethods.WM_QUERYENDSESSION:
                    return self.OnQueryEndSession(lParam) ? 1 : 0;

                case NativeMethods.WM_ENDSESSION:
                    self.OnEndSession(wParam != 0);
                    return 0;

                case NativeMethods.WM_CLOSE:
                    // Only Dispose ends this window: the default would destroy
                    // it, and Restart Manager sends WM_CLOSE to every top-level
                    // window of the process (measured, Phase 0).
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
            // The handlers are arbitrary caller code, and nothing may unwind
            // into Windows. A failed session query falls through to the
            // default answer, which lets the session end.
            Debug.WriteLine($"Tray icon: message 0x{msg:X4} failed: {ex.GetType().Name}: {ex.Message}");
        }

        return WindowSubclass.CallDefault(hwnd, msg, wParam, lParam);
    }
}

/// <summary>What one icon callback means.</summary>
internal enum TrayEvent
{
    /// <summary>Not ours, or not one Noto acts on (a button press, a pointer move).</summary>
    None,

    /// <summary><c>NIN_SELECT</c> or <c>NIN_KEYSELECT</c>: a click, Enter or Space.</summary>
    Select,

    /// <summary><c>WM_CONTEXTMENU</c>: a right-click, Shift+F10 or the menu key.</summary>
    Menu,
}

/// <summary>Asking a window to close the way Alt+F4 does (#16 slice 7, ADR-014).</summary>
public static class WindowClose
{
    /// <summary>
    /// Sends <c>WM_CLOSE</c> and returns once the window has handled it.
    /// Measured on Windows App SDK 2.5.1: <c>AppWindow.Closing</c>, and
    /// <c>Window.Closed</c> if the close went ahead, have run by then.
    /// </summary>
    public static void Request(WindowHandle window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _ = NativeMethods.SendMessage(window.Hwnd, NativeMethods.WM_CLOSE, 0, 0);
    }

    /// <summary>Posts <c>WM_CLOSE</c>, handled after the current message. A window destroyed by then never sees it.</summary>
    public static void Post(WindowHandle window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _ = NativeMethods.PostMessage(window.Hwnd, NativeMethods.WM_CLOSE, 0, 0);
    }
}
