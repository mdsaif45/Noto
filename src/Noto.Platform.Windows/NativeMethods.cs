using System.Runtime.InteropServices;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace Noto.Platform.Windows;

/// <summary>
/// The Win32 calls the platform primitives need, and no others.
/// </summary>
/// <remarks>
/// <para>
/// Internal by design: every handle and native struct stays inside this
/// assembly, and callers receive <see cref="PixelRect"/>,
/// <see cref="FrameInset"/>, <see cref="DisplayMonitor"/> and
/// <see cref="WindowPlacement"/> instead.
/// </para>
/// <para>
/// Every struct here is blittable — fixed <c>char</c> buffers rather than
/// <c>ByValTStr</c> strings — so every import can be a
/// <see cref="LibraryImportAttribute"/> with no runtime marshalling. The
/// window-behaviour spike needed <c>DllImport</c> for
/// <c>MONITORINFOEXW</c> because it declared the device name as a string;
/// a fixed buffer removes that need.
/// </para>
/// </remarks>
internal static unsafe partial class NativeMethods
{
    internal const uint MONITORINFOF_PRIMARY = 0x00000001;

    /// <summary><c>MDT_EFFECTIVE_DPI</c> — the DPI Windows scales UI by.</summary>
    internal const int MDT_EFFECTIVE_DPI = 0;

    /// <summary>
    /// Asks <c>EnumDisplayDevicesW</c> for the monitor's device interface
    /// path rather than its display-adapter description.
    /// </summary>
    internal const uint EDD_GET_DEVICE_INTERFACE_NAME = 0x00000001;

    internal const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    // Messages the docked-window subclass handles (#16 slice 3).
    internal const uint WM_WINDOWPOSCHANGING = 0x0046;
    internal const uint WM_NCDESTROY = 0x0082;
    internal const uint WM_NCCALCSIZE = 0x0083;
    internal const uint WM_NCHITTEST = 0x0084;
    internal const uint WM_SIZING = 0x0214;
    internal const uint WM_ENTERSIZEMOVE = 0x0231;
    internal const uint WM_EXITSIZEMOVE = 0x0232;

    // Activation crossing the process boundary (#16 slice 6, parity A13).
    internal const uint WM_ACTIVATEAPP = 0x001C;

    // WM_NCHITTEST results.
    internal const int HTCLIENT = 1;
    internal const int HTLEFT = 10;
    internal const int HTRIGHT = 11;
    internal const int HTTOP = 12;
    internal const int HTTOPLEFT = 13;
    internal const int HTTOPRIGHT = 14;
    internal const int HTBOTTOM = 15;
    internal const int HTBOTTOMLEFT = 16;
    internal const int HTBOTTOMRIGHT = 17;

    internal const uint SWP_NOSIZE = 0x0001;
    internal const uint SWP_NOMOVE = 0x0002;
    internal const uint SWP_NOZORDER = 0x0004;
    internal const uint SWP_NOACTIVATE = 0x0010;
    internal const uint SWP_FRAMECHANGED = 0x0020;

    /// <summary><c>SW_RESTORE</c>: activates and restores a minimized window to its normal placement.</summary>
    internal const int SW_RESTORE = 9;

    // Global hotkey (#16 slice 4).
    internal const uint WM_HOTKEY = 0x0312;
    internal const uint MOD_ALT = 0x0001;
    internal const uint MOD_CONTROL = 0x0002;
    internal const uint MOD_SHIFT = 0x0004;
    internal const uint MOD_WIN = 0x0008;
    internal const uint MOD_NOREPEAT = 0x4000;
    internal const int ERROR_HOTKEY_ALREADY_REGISTERED = 1409;

    /// <summary>The parent that makes a window message-only: never shown, never enumerated.</summary>
    internal const nint HWND_MESSAGE = -3;

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly PixelRect ToPixelRect() => new(Left, Top, Right, Bottom);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MONITORINFOEXW
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        public fixed char szDevice[32];
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DISPLAY_DEVICEW
    {
        public uint cb;
        public fixed char DeviceName[32];
        public fixed char DeviceString[128];
        public uint StateFlags;
        public fixed char DeviceID[128];
        public fixed char DeviceKey[128];
    }

    [LibraryImport("kernel32.dll")]
    internal static partial nint GetCurrentProcess();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool TerminateProcess(nint process, uint exitCode);

    /// <summary>Both sizes -1: removes as many pages as possible from the working set (ProcessMemory.Trim).</summary>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetProcessWorkingSetSize(nint process, nint minimum, nint maximum);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EnumDisplayMonitors(
        nint hdc,
        nint lprcClip,
        delegate* unmanaged<nint, nint, RECT*, nint, int> lpfnEnum,
        nint dwData);

    [StructLayout(LayoutKind.Sequential)]
    internal struct WINDOWPOS
    {
        public nint hwnd;
        public nint hwndInsertAfter;
        public int x;
        public int y;
        public int cx;
        public int cy;
        public uint flags;
    }

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetMonitorInfo(nint hMonitor, ref MONITORINFOEXW lpmi);

    [LibraryImport("user32.dll", EntryPoint = "EnumDisplayDevicesW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EnumDisplayDevices(
        string lpDevice,
        uint iDevNum,
        ref DISPLAY_DEVICEW lpDisplayDevice,
        uint dwFlags);

    [LibraryImport("shcore.dll")]
    internal static partial int GetDpiForMonitor(nint hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetWindowRect(nint hWnd, out RECT lpRect);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint CreateWindowEx(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        nint hWndParent,
        nint hMenu,
        nint hInstance,
        nint lpParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyWindow(nint hWnd);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnregisterHotKey(nint hWnd, int id);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetForegroundWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsIconic(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ShowWindow(nint hWnd, int nCmdShow);

    // Workspace show/hide (#16 slice 5).

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindowVisible(nint hWnd);

    /// <remarks>
    /// <c>SetLastError</c> for the same reason as the test imports: a plain
    /// signature makes the generator emit a direct <c>extern</c>, which CodeQL
    /// reports as <c>cs/call-to-unmanaged-code</c>. Harmless here.
    /// </remarks>
    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial nint GetForegroundWindow();

    /// <summary>
    /// When the message being handled was posted, in milliseconds since
    /// the system started — the clock <see cref="Environment.TickCount"/>
    /// reads. Valid only while that message is being handled.
    /// </summary>
    /// <remarks><c>SetLastError</c> only to avoid a direct extern, as above.</remarks>
    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial int GetMessageTime();

    // Single instance (A17, ADR-013). Every import sets SetLastError, which
    // also keeps the generator from emitting a direct extern (see above).

    internal const nint INVALID_HANDLE_VALUE = -1;
    internal const int ERROR_ACCESS_DENIED = 5;
    internal const int ERROR_INSUFFICIENT_BUFFER = 122;
    internal const int ERROR_PIPE_BUSY = 231;

    internal const uint FILE_SHARE_READ = 0x00000001;
    internal const uint FILE_SHARE_WRITE = 0x00000002;
    internal const uint FILE_SHARE_DELETE = 0x00000004;
    internal const uint OPEN_EXISTING = 3;
    internal const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    internal const uint FILE_FLAG_OVERLAPPED = 0x40000000;
    internal const uint FILE_FLAG_FIRST_PIPE_INSTANCE = 0x00080000;
    internal const uint FILE_NAME_NORMALIZED = 0x0;
    internal const uint VOLUME_NAME_DOS = 0x0;

    internal const uint PIPE_ACCESS_DUPLEX = 0x00000003;
    internal const uint PIPE_TYPE_MESSAGE = 0x00000004;
    internal const uint PIPE_READMODE_MESSAGE = 0x00000002;
    internal const uint PIPE_WAIT = 0x00000000;
    internal const uint PIPE_REJECT_REMOTE_CLIENTS = 0x00000008;

    internal const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    internal const uint TOKEN_QUERY = 0x0008;
    internal const int TokenUser = 1;
    internal const int TokenElevation = 20;

    internal const uint MB_OK = 0x00000000;
    internal const uint MB_YESNO = 0x00000004;
    internal const uint MB_DEFBUTTON2 = 0x00000100;
    internal const int IDYES = 6;
    internal const uint MB_ICONWARNING = 0x00000030;
    internal const uint MB_SETFOREGROUND = 0x00010000;

    [StructLayout(LayoutKind.Sequential)]
    internal struct SECURITY_ATTRIBUTES
    {
        public uint nLength;
        public nint lpSecurityDescriptor;
        public int bInheritHandle;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        nint lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        nint hTemplateFile);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true)]
    internal static partial uint GetFinalPathNameByHandle(SafeHandle hFile, char* lpszFilePath, uint cchFilePath, uint dwFlags);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateNamedPipeW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint CreateNamedPipe(
        string lpName,
        uint dwOpenMode,
        uint dwPipeMode,
        uint nMaxInstances,
        uint nOutBufferSize,
        uint nInBufferSize,
        uint nDefaultTimeOut,
        SECURITY_ATTRIBUTES* lpSecurityAttributes);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CancelIoEx(SafeHandle hFile, nint lpOverlapped);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetNamedPipeServerProcessId(SafeHandle pipe, out uint serverProcessId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetNamedPipeServerSessionId(SafeHandle pipe, out uint serverSessionId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ProcessIdToSessionId(uint dwProcessId, out uint pSessionId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial nint OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwProcessId);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool OpenProcessToken(SafeHandle processHandle, uint desiredAccess, out nint tokenHandle);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetTokenInformation(
        SafeHandle tokenHandle,
        int tokenInformationClass,
        void* tokenInformation,
        uint tokenInformationLength,
        out uint returnLength);

    /// <summary>
    /// Lets another process take the foreground — passed by a second launch,
    /// which the user just started and so holds the right, to the running
    /// Noto it hands off to. Measured: without it the owner was refused 3/3.
    /// </summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AllowSetForegroundWindow(uint dwProcessId);

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int MessageBox(nint hWnd, string lpText, string lpCaption, uint uType);

    // The notification-area icon, its menu and session end (#16 slice 7, ADR-014).
    // Every import sets SetLastError, which also keeps the generator from
    // emitting a direct extern (see GetForegroundWindow).

    internal const uint WM_NULL = 0x0000;
    internal const uint WM_CLOSE = 0x0010;
    internal const uint WM_QUERYENDSESSION = 0x0011;
    internal const uint WM_ENDSESSION = 0x0016;
    internal const uint WM_CONTEXTMENU = 0x007B;
    internal const uint WM_APP = 0x8000;
    internal const uint ENDSESSION_CRITICAL = 0x40000000;

    internal const uint WS_POPUP = 0x80000000;

    // The frame a docked window goes without (F2, #145): the sizing border and the caption (border and dialog frame).
    internal const int GWL_STYLE = -16;
    internal const uint WS_THICKFRAME = 0x00040000;
    internal const uint WS_CAPTION = 0x00C00000;
    internal const uint WS_EX_TOOLWINDOW = 0x00000080;

    internal const uint NIM_ADD = 0;
    internal const uint NIM_DELETE = 2;
    internal const uint NIM_SETVERSION = 4;
    internal const uint NIF_MESSAGE = 0x01;
    internal const uint NIF_ICON = 0x02;
    internal const uint NIF_TIP = 0x04;
    internal const uint NIF_SHOWTIP = 0x80;
    internal const uint NOTIFYICON_VERSION_4 = 4;
    internal const uint NIN_SELECT = 0x0400;
    internal const uint NIN_KEYSELECT = 0x0401;

    internal const uint MF_STRING = 0x0000;
    internal const uint MF_GRAYED = 0x0001;
    internal const uint MF_CHECKED = 0x0008;
    internal const uint MF_SEPARATOR = 0x0800;
    internal const uint TPM_RIGHTBUTTON = 0x0002;
    internal const uint TPM_NONOTIFY = 0x0080;
    internal const uint TPM_RETURNCMD = 0x0100;

    /// <summary>The system's generic application icon: shared, never destroyed.</summary>
    internal const nint IDI_APPLICATION = 32512;

    /// <summary>
    /// The resource id of an executable's own icon: the SDK embeds
    /// <c>ApplicationIcon</c> under the same number (#95).
    /// </summary>
    internal const nint AppIconResource = 32512;

    internal const uint IMAGE_ICON = 1;
    internal const int SM_CXSMICON = 49;
    internal const int SM_CYSMICON = 50;

    [LibraryImport("user32.dll", EntryPoint = "LoadImageW", SetLastError = true)]
    internal static partial nint LoadImage(nint hInst, nint name, uint type, int cx, int cy, uint fuLoad);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyIcon(nint hIcon);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial int GetSystemMetrics(int nIndex);

    /// <summary><c>NOTIFYICONDATAW</c>, the Vista-and-later layout, with fixed buffers so it stays blittable.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;
        public fixed char szTip[128];
        public uint dwState;
        public uint dwStateMask;
        public fixed char szInfo[256];
        public uint uTimeoutOrVersion;
        public fixed char szInfoTitle[64];
        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [LibraryImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool Shell_NotifyIcon(uint dwMessage, NOTIFYICONDATAW* lpData);

    [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint RegisterWindowMessage(string lpString);

    [LibraryImport("user32.dll", EntryPoint = "LoadIconW", SetLastError = true)]
    internal static partial nint LoadIcon(nint hInstance, nint lpIconName);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial nint CreatePopupMenu();

    [LibraryImport("user32.dll", EntryPoint = "AppendMenuW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AppendMenu(nint hMenu, uint uFlags, nuint uIDNewItem, string? lpNewItem);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial int TrackPopupMenuEx(nint hMenu, uint uFlags, int x, int y, nint hwnd, nint lptpm);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyMenu(nint hMenu);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PostMessage(nint hWnd, uint Msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW", SetLastError = true)]
    internal static partial nint SendMessage(nint hWnd, uint Msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindow(nint hWnd);

    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ShutdownBlockReasonCreate(nint hWnd, string pwszReason);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ShutdownBlockReasonDestroy(nint hWnd);

    [LibraryImport("dwmapi.dll")]
    internal static partial int DwmGetWindowAttribute(nint hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    // A window whose transparent pixels DWM leaves see-through (F2, #145): blur-behind over an empty region.
    internal const uint DWM_BB_ENABLE = 0x00000001;
    internal const uint DWM_BB_BLURREGION = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    internal struct DWM_BLURBEHIND
    {
        public uint dwFlags;
        public int fEnable;
        public nint hRgnBlur;
        public int fTransitionOnMaximized;
    }

    [LibraryImport("dwmapi.dll")]
    internal static partial int DwmEnableBlurBehindWindow(nint hWnd, in DWM_BLURBEHIND pBlurBehind);

    [LibraryImport("gdi32.dll")]
    internal static partial nint CreateRectRgn(int x1, int y1, int x2, int y2);

    // A window kept off the screen while it is shown: DWM draws nothing of it (F2, #145).
    internal const int DWMWA_CLOAK = 13;

    [LibraryImport("dwmapi.dll")]
    internal static partial int DwmSetWindowAttribute(nint hwnd, int dwAttribute, in int pvAttribute, int cbAttribute);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    internal static partial nint GetWindowLongPtr(nint hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    internal static partial nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeleteObject(nint ho);

    // A Windows.System dispatcher queue on the calling thread, which Windows.UI.Composition needs (F2, #145).
    internal const int DQTYPE_THREAD_CURRENT = 2;
    internal const int DQTAT_COM_STA = 2;

    [StructLayout(LayoutKind.Sequential)]
    internal struct DispatcherQueueOptions
    {
        public int dwSize;
        public int threadType;
        public int apartmentType;
    }

    [LibraryImport("CoreMessaging.dll")]
    internal static partial int CreateDispatcherQueueController(DispatcherQueueOptions options, out nint dispatcherQueueController);

    // Window subclassing, comctl32. Measured under this application's
    // manifest: comctl32 5.82 is loaded and exports all three, so no
    // Common-Controls v6 dependency is needed.

    [LibraryImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWindowSubclass(
        nint hWnd,
        delegate* unmanaged<nint, uint, nint, nint, nuint, nuint, nint> pfnSubclass,
        nuint uIdSubclass,
        nuint dwRefData);

    [LibraryImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool RemoveWindowSubclass(
        nint hWnd,
        delegate* unmanaged<nint, uint, nint, nint, nuint, nuint, nint> pfnSubclass,
        nuint uIdSubclass);

    /// <summary>
    /// The next window procedure in the subclass chain.
    /// </summary>
    /// <remarks>
    /// <b>Called from exactly one place</b>, <c>WindowSubclass.CallDefault</c>,
    /// which every subclass in this assembly shares.
    /// Its signature is all plain types, so the generator emits a direct
    /// <c>extern</c> and CodeQL reports <c>cs/call-to-unmanaged-code</c> at
    /// that call. It is required: a subclass must pass every message it does
    /// not consume down the chain, and there is no managed equivalent for
    /// hit-testing, sizing or position interception (#16 slice 3 D5 spike).
    /// </remarks>
    [LibraryImport("comctl32.dll")]
    internal static partial nint DefSubclassProc(nint hWnd, uint uMsg, nint wParam, nint lParam);

    // The shortcut recorder's keyboard observation (Slice 1.2b): a low-level keyboard hook, for one recording only.
    internal const int WH_KEYBOARD_LL = 13;
    internal const nint WM_KEYDOWN = 0x0100;
    internal const nint WM_SYSKEYDOWN = 0x0104;

    /// <summary><c>KBDLLHOOKSTRUCT</c>: the key a low-level keyboard hook is told about.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [LibraryImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    internal static partial nint SetWindowsHookEx(int idHook, delegate* unmanaged<int, nint, nint, nint> lpfn, nint hmod, uint dwThreadId);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnhookWindowsHookEx(nint hhk);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial short GetAsyncKeyState(int vKey);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint GetModuleHandle(string? lpModuleName);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int SetCurrentProcessExplicitAppUserModelID(string AppID);

    // A file shortcut's icon and type name, as Explorer shows them (#196).
    internal const uint SHGFI_ICON = 0x000000100;
    internal const uint SHGFI_TYPENAME = 0x000000400;
    internal const uint SHGFI_LARGEICON = 0x000000000;
    internal const uint SHGFI_SMALLICON = 0x000000001;
    internal const uint DIB_RGB_COLORS = 0;

    /// <summary><c>SHFILEINFOW</c>, with fixed buffers so it stays blittable.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct SHFILEINFOW
    {
        public nint hIcon;
        public int iIcon;
        public uint dwAttributes;
        public fixed char szDisplayName[260];
        public fixed char szTypeName[80];
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ICONINFO
    {
        public int fIcon;
        public uint xHotspot;
        public uint yHotspot;
        public nint hbmMask;
        public nint hbmColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public nint bmBits;
    }

    /// <summary><c>BITMAPINFO</c> with room for a full colour table, which GetDIBits may write for a mask.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct BITMAPINFO
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
        public fixed uint bmiColors[256];
    }

    [LibraryImport("shell32.dll", EntryPoint = "SHGetFileInfoW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint SHGetFileInfo(string pszPath, uint dwFileAttributes, SHFILEINFOW* psfi, uint cbFileInfo, uint uFlags);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetIconInfo(nint hIcon, ICONINFO* piconinfo);

    [LibraryImport("gdi32.dll", EntryPoint = "GetObjectW")]
    internal static partial int GetObject(nint h, int c, void* pv);

    [LibraryImport("gdi32.dll")]
    internal static partial int GetDIBits(nint hdc, nint hbm, uint start, uint cLines, void* lpvBits, BITMAPINFO* lpbmi, uint usage);

    [LibraryImport("user32.dll")]
    internal static partial nint GetDC(nint hWnd);

    [LibraryImport("user32.dll")]
    internal static partial int ReleaseDC(nint hWnd, nint hDC);

    // Imported files moved to Windows' Recycle Bin (#267): the shell's own delete, to the bin, with no UI of its own
    // but the warning that a file would be deleted outright instead.
    internal const uint FO_DELETE = 0x0003;
    internal const ushort FOF_SILENT = 0x0004;
    internal const ushort FOF_NOCONFIRMATION = 0x0010;
    internal const ushort FOF_ALLOWUNDO = 0x0040;
    internal const ushort FOF_NOERRORUI = 0x0400;
    internal const ushort FOF_WANTNUKEWARNING = 0x4000;

    /// <summary>
    /// <c>SHFILEOPSTRUCTW</c>, at natural alignment: shellapi.h packs it to 1 only on 32-bit Windows, which Noto does not
    /// build for (win-x64, win-arm64).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct SHFILEOPSTRUCTW
    {
        public nint hwnd;
        public uint wFunc;
        public char* pFrom;
        public char* pTo;
        public ushort fFlags;
        public int fAnyOperationsAborted;
        public nint hNameMappings;
        public char* lpszProgressTitle;
    }

    [LibraryImport("shell32.dll", EntryPoint = "SHFileOperationW")]
    internal static partial int SHFileOperation(SHFILEOPSTRUCTW* lpFileOp);

    // Hot Side and drag to reveal (#185, ADR-007 §9): a low-level mouse hook on its own thread, one timer, and the
    // reads made once when a dwell elapses.
    internal const int WH_MOUSE_LL = 14;
    internal const uint WM_QUIT = 0x0012;
    internal const uint WM_LBUTTONDOWN = 0x0201;
    internal const uint WM_LBUTTONUP = 0x0202;
    internal const uint WM_RBUTTONDOWN = 0x0204;
    internal const uint WM_RBUTTONUP = 0x0205;
    internal const uint WM_MBUTTONDOWN = 0x0207;
    internal const uint WM_MBUTTONUP = 0x0208;
    internal const uint WM_XBUTTONDOWN = 0x020B;
    internal const uint WM_XBUTTONUP = 0x020C;
    internal const uint PM_NOREMOVE = 0x0000;
    internal const int SM_CXDRAG = 68;
    internal const int SM_CYDRAG = 69;
    internal const uint GUI_INMOVESIZE = 0x00000002;
    internal const uint DESKTOP_READOBJECTS = 0x0001;

    /// <summary><c>MSLLHOOKSTRUCT</c>: the pointer event a low-level mouse hook is told about.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct MSLLHOOKSTRUCT
    {
        public int x;
        public int y;
        public uint mouseData;
        public uint flags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public int ptX;
        public int ptY;
        public uint lPrivate;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GUITHREADINFO
    {
        public uint cbSize;
        public uint flags;
        public nint hwndActive;
        public nint hwndFocus;
        public nint hwndCapture;
        public nint hwndMenuOwner;
        public nint hwndMoveSize;
        public nint hwndCaret;
        public RECT rcCaret;
    }

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW", SetLastError = true)]
    internal static partial int GetMessage(MSG* lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [LibraryImport("user32.dll", EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PeekMessage(MSG* lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    internal static partial nint DispatchMessage(MSG* lpMsg);

    [LibraryImport("user32.dll", EntryPoint = "PostThreadMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PostThreadMessage(uint idThread, uint Msg, nint wParam, nint lParam);

    [LibraryImport("kernel32.dll")]
    internal static partial uint GetCurrentThreadId();

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial nuint SetTimer(nint hWnd, nuint nIDEvent, uint uElapse, delegate* unmanaged<nint, uint, nuint, uint, void> lpTimerFunc);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool KillTimer(nint hWnd, nuint uIDEvent);

    [LibraryImport("user32.dll")]
    internal static partial nint GetShellWindow();

    [LibraryImport("user32.dll")]
    internal static partial uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetGUIThreadInfo(uint idThread, GUITHREADINFO* pgui);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial nint OpenInputDesktop(uint dwFlags, [MarshalAs(UnmanagedType.Bool)] bool fInherit, uint dwDesiredAccess);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseDesktop(nint hDesktop);

    // The Open Bar (#185 part 2, ADR-007 §10): a small topmost handle that never activates, half see-through, that
    // takes clicks and dropped files, and follows the foreground for the full-screen check.
    internal const int GWL_EXSTYLE = -20;
    internal const uint WS_EX_TOPMOST = 0x00000008;
    internal const uint WS_EX_LAYERED = 0x00080000;
    internal const uint WS_EX_NOACTIVATE = 0x08000000;
    internal const nint HWND_TOPMOST = -1;
    internal const uint LWA_ALPHA = 0x00000002;
    internal const int SW_HIDE = 0;
    internal const int SW_SHOWNOACTIVATE = 4;
    internal const uint WM_PAINT = 0x000F;
    internal const uint WM_ERASEBKGND = 0x0014;
    internal const uint WM_SYSCOLORCHANGE = 0x0015;
    internal const uint WM_SETTINGCHANGE = 0x001A;
    internal const uint WM_MOUSEACTIVATE = 0x0021;
    internal const uint WM_DISPLAYCHANGE = 0x007E;
    internal const uint WM_TIMER = 0x0113;
    internal const uint WM_MOUSEMOVE = 0x0200;
    internal const uint WM_DROPFILES = 0x0233;
    internal const uint WM_DPICHANGED = 0x02E0;
    internal const uint WM_DWMCOLORIZATIONCOLORCHANGED = 0x0320;
    internal const int MA_NOACTIVATE = 3;
    internal const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    internal const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    internal const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
    internal const uint SPI_GETHIGHCONTRAST = 0x0042;
    internal const uint HCF_HIGHCONTRASTON = 0x00000001;
    internal const int COLOR_HIGHLIGHT = 13;

    [StructLayout(LayoutKind.Sequential)]
    internal struct PAINTSTRUCT
    {
        public nint hdc;
        public int fErase;
        public RECT rcPaint;
        public int fRestore;
        public int fIncUpdate;
        public fixed byte rgbReserved[32];
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HIGHCONTRASTW
    {
        public uint cbSize;
        public uint dwFlags;
        public nint lpszDefaultScheme;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetLayeredWindowAttributes(nint hwnd, uint crKey, byte bAlpha, uint dwFlags);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial nint BeginPaint(nint hWnd, PAINTSTRUCT* lpPaint);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EndPaint(nint hWnd, PAINTSTRUCT* lpPaint);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial int FillRect(nint hDC, RECT* lprc, nint hbr);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool InvalidateRect(nint hWnd, nint lpRect, [MarshalAs(UnmanagedType.Bool)] bool bErase);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    internal static partial nint CreateSolidBrush(uint color);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial uint GetSysColor(int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SystemParametersInfo(uint uiAction, uint uiParam, void* pvParam, uint fWinIni);

    [LibraryImport("dwmapi.dll")]
    internal static partial int DwmGetColorizationColor(out uint pcrColorization, [MarshalAs(UnmanagedType.Bool)] out bool pfOpaqueBlend);

    [LibraryImport("shell32.dll", SetLastError = true)]
    internal static partial void DragAcceptFiles(nint hWnd, [MarshalAs(UnmanagedType.Bool)] bool fAccept);

    [LibraryImport("shell32.dll", EntryPoint = "DragQueryFileW", SetLastError = true)]
    internal static partial uint DragQueryFile(nint hDrop, uint iFile, char* lpszFile, uint cch);

    [LibraryImport("shell32.dll", SetLastError = true)]
    internal static partial void DragFinish(nint hDrop);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial nint SetWinEventHook(
        uint eventMin,
        uint eventMax,
        nint hmodWinEventProc,
        delegate* unmanaged<nint, uint, nint, int, int, uint, uint, void> pfnWinEventProc,
        uint idProcess,
        uint idThread,
        uint dwFlags);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnhookWinEvent(nint hWinEventHook);
}
