# Noto — Windows API Feasibility Research (raw)

Target stack: C# / .NET 8+ / WinUI 3 / Windows App SDK 1.7+ / Win32 interop
Date of research: 2026-09-14
Legend: **[CONFIRMED]** = official Microsoft Learn · **[COMMUNITY]** = blog / GitHub issue / SO · **[UNKNOWN]** = not established

---

## 0. Executive risk table

| # | Risk | Severity | Topic |
|---|---|---|---|
| R1 | Per-window opacity (`SetLayeredWindowAttributes`) conflicts with WinUI 3 composition | **HIGH** | B |
| R2 | Click-through / input pass-through fundamentally unsupported in WinUI 3 | **HIGH** | B |
| R3 | "Note follows window" requires `EVENT_OBJECT_LOCATIONCHANGE`, which is extremely chatty (CPU) and laggy (async cross-process queue) → visible drift/flicker | **HIGH** | A |
| R4 | No stable window identity across sessions — re-binding a note to "the same window" is heuristic only | **HIGH** | A |
| R5 | Virtual desktop APIs beyond `IVirtualDesktopManager` are undocumented and break per Windows build | **HIGH** | A |
| R6 | Drag & drop from Explorer into WinUI 3 has long-standing open bugs | **MEDIUM-HIGH** | E |
| R7 | Packaged identity required for notifications / StartupTask → forces MSIX or sparse package | **MEDIUM** | H |
| R8 | No built-in tray icon in WinUI 3 | **MEDIUM** | H |
| R9 | WinUI 3 platform trust/maturity; slow bug turnaround | **MEDIUM** | H |
| R10 | AppBar + per-monitor DPI is a known correctness minefield | **MEDIUM** | C |

---

# A. WINDOW TRACKING & CONTEXT DETECTION

## A1. Foreground app + active window identification

**[CONFIRMED]** Standard chain:

```
GetForegroundWindow()            -> HWND
  |
  +-> GetWindowThreadProcessId()  -> (tid, pid)
  |
  +-> GetWindowTextW()            -> title
  +-> GetClassNameW()             -> class name
  |
OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid)
  |
  +-> QueryFullProcessImageNameW(h, 0, buf, &len) -> full exe path
```

- `QueryFullProcessImageNameW` (winbase.h) requires only **`PROCESS_QUERY_LIMITED_INFORMATION`** (0x1000). This is the correct API — MS explicitly recommends it over `GetModuleFileNameEx(NULL)` as "more efficient and more reliable."
  https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-queryfullprocessimagenamea
- `GetModuleFileNameExW` needs `PROCESS_QUERY_INFORMATION | PROCESS_VM_READ` (relaxed to LIMITED on Win10+ only when `hModule == NULL`). Avoid it.
  https://learn.microsoft.com/en-us/windows/win32/api/psapi/nf-psapi-getmodulefilenameexw

**Security implication (important for Noto):** a *non-elevated* Noto process **cannot** `OpenProcess` an *elevated* target process, even with `PROCESS_QUERY_LIMITED_INFORMATION` — you get `ERROR_ACCESS_DENIED` (5). So:

```
Noto (medium IL) tracking Notepad (medium IL)  -> OK, full path
Noto (medium IL) tracking an admin console     -> title+class OK, exe path DENIED
```

You still get HWND, title, and class name (those come from USER32, not the process token). **Design for a degraded identity tier** where only title/class is available. Do NOT require elevation — elevating Noto breaks drag & drop (see E4) and UIPI for everything else.

**[COMMUNITY]** Also note `GetForegroundWindow()` can return `NULL` transiently (lock screen, secure desktop / UAC prompt, alt-tab transitions). Always null-check.

## A2. Reacting to foreground change WITHOUT polling

**[CONFIRMED]** `SetWinEventHook` is the correct mechanism.
https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwineventhook

```cpp
HWINEVENTHOOK SetWinEventHook(
  DWORD eventMin, DWORD eventMax,
  HMODULE hmodWinEventProc,   // NULL for OUTOFCONTEXT
  WINEVENTPROC pfnWinEventProc,
  DWORD idProcess,            // 0 = all processes
  DWORD idThread,             // 0 = all threads
  DWORD dwFlags);
```

### Events needed by Noto

| Event | Const | Purpose |
|---|---|---|
| `EVENT_SYSTEM_FOREGROUND` | 0x0003 | active app changed |
| `EVENT_OBJECT_LOCATIONCHANGE` | 0x800B | move/resize (also fires for **caret and cursor** — see cost) |
| `EVENT_SYSTEM_MINIMIZESTART` | 0x0016 | minimize begin |
| `EVENT_SYSTEM_MINIMIZEEND` | 0x0017 | restore |
| `EVENT_SYSTEM_MOVESIZESTART` | 0x000A | user grabbed the title bar |
| `EVENT_SYSTEM_MOVESIZEEND` | 0x000B | user released |
| `EVENT_OBJECT_DESTROY` | 0x8001 | target window gone (Raymond Chen explicitly recommends this alongside LOCATIONCHANGE) |
| `EVENT_OBJECT_NAMECHANGE` | 0x800C | title changed (tab switch in browsers/editors) |
| `EVENT_SYSTEM_CAPTURESTART/END` | 0x0008/9 | optional drag heuristics |

https://learn.microsoft.com/en-us/windows/win32/winauto/event-constants

### Threading / message loop — THE critical constraint

**[CONFIRMED]**, quoted from the `SetWinEventHook` page:

> "The client thread that calls **SetWinEventHook** must have a message loop in order to receive events."

> "For out-of-context events, the event is delivered on the same thread that called **SetWinEventHook**."

> "When you use SetWinEventHook to set a callback in managed code, you should use the **GCHandle** structure to avoid exceptions. This tells the garbage collector not to move the callback."

Consequences for a WinUI 3 app:

```
Option 1 (simplest): hook on the WinUI UI thread
   + UI thread already pumps messages (XAML dispatcher)
   - callbacks contend with XAML layout/render => jank under LOCATIONCHANGE storm
   VERDICT: acceptable for FOREGROUND / MINIMIZE only. NOT for LOCATIONCHANGE.

Option 2 (recommended): dedicated STA "hook thread"
   Thread(ApartmentState.STA) -> CreateWindowExW(HWND_MESSAGE) -> GetMessage/Dispatch loop
   -> SetWinEventHook here, filter+throttle here,
   -> marshal to UI via DispatcherQueue.TryEnqueue
   VERDICT: correct architecture for Noto.
```

You must keep a **static/field-rooted `WinEventDelegate`** (or a `GCHandle`) alive for the hook's lifetime, otherwise the GC collects the delegate and the app crashes with an access violation on the next callback. This is the single most common P/Invoke bug in this area. **[CONFIRMED]** by the GCHandle remark above.

### OUTOFCONTEXT vs INCONTEXT

**[CONFIRMED]**

| | `WINEVENT_OUTOFCONTEXT` (0x0000) | `WINEVENT_INCONTEXT` (0x0004) |
|---|---|---|
| Callback location | your process | **DLL injected into the target process** |
| Requires native DLL | No | **Yes** (`hmodWinEventProc` must be non-NULL) |
| Delivery | **asynchronous, queued**, guaranteed in order | synchronous, immediate |
| Usable from C# | **Yes** | No (managed DLL cannot be injected this way) |
| Bitness | works across 32/64 | silently degrades to OUTOFCONTEXT on bitness mismatch |
| Store/UWP targets | works | **DLL not loaded** unless the app has UIAccess |

> "In some situations, even if you request WINEVENT_INCONTEXT events, the events will still be delivered out-of-context. These scenarios include events from console windows and events from processes that have a different bit-depth."

**Decision for Noto: `WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS`.** INCONTEXT is off the table for a C#-only app, and would flag antivirus anyway. `SKIPOWNPROCESS` is important — otherwise Noto's own window moves re-enter your own handler.

**Reentrancy [CONFIRMED]:** "While a hook function processes an event, additional events may be triggered, which may cause the hook function to reenter before the processing for the original event is finished." Guard with a flag; never do slow work (no file I/O, no DB write, no `OpenProcess` on every callback) inside the callback.

### Performance cost of EVENT_OBJECT_LOCATIONCHANGE — **a real problem**

**[CONFIRMED]** `EVENT_OBJECT_LOCATIONCHANGE` (0x800B) "is sent by the system for the following user interface elements: caret, window object." It fires for **the mouse cursor** too.

**[COMMUNITY — but from the PowerToys team itself]** microsoft/PowerToys issue #1264, "[Perf] Reduce the amount of time the EVENT_OBJECT_LOCATIONCHANGE hook is enabled": simply moving the mouse cursor spikes CPU **3–5%** depending on the machine. Their recommended mitigation: **only register LOCATIONCHANGE while a drag is in progress**, not permanently.
https://github.com/microsoft/PowerToys/issues/1264

**[CONFIRMED]** MS guidance: "register only for the events they need," and "it is more efficient to have multiple hooks registered for specific events... than one hook registered for a larger range of events."
https://learn.microsoft.com/en-us/windows/win32/winauto/winevents-overview

→ Use **multiple narrow hooks** (`eventMin == eventMax`), never `EVENT_MIN..EVENT_MAX`.

## A3. UWP / packaged apps: the ApplicationFrameHost.exe gotcha

**[CONFIRMED + COMMUNITY]** This is a genuine, well-documented trap.

For a UWP/packaged app (Calculator, Settings, Store Mail, Photos...), the **top-level HWND belongs to `ApplicationFrameHost.exe`** — a single shared host process for the session. `GetWindowThreadProcessId(GetForegroundWindow())` therefore returns the *frame host's* PID, not the real app. Every UWP app looks identical.

```
GetForegroundWindow()
   -> HWND class "ApplicationFrameWindow"     <-- the tell
   -> pid = ApplicationFrameHost.exe          <-- WRONG, useless
        |
        v  EnumChildWindows / FindWindowEx
   child HWND class "Windows.UI.Core.CoreWindow"
   -> GetWindowThreadProcessId(child) -> REAL pid (e.g. Calculator.exe)
   -> OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, pid)
   -> GetApplicationUserModelId(hProc, &len, buf)  -> AUMID
```

### The resolution algorithm

1. If `GetClassNameW(hwnd) == "ApplicationFrameWindow"`, enumerate children looking for class `"Windows.UI.Core.CoreWindow"`.
2. **Guard:** the CoreWindow child's PID sometimes *equals* ApplicationFrameHost's PID transiently (during app launch / suspend). Only accept the child if `childPid != framePid`. Otherwise fall back and retry later.
3. Call `GetApplicationUserModelId(hProcess, &length, aumid)` — **appmodel.h**, requires `PROCESS_QUERY_LIMITED_INFORMATION`. Available Windows 8+.
   https://learn.microsoft.com/en-us/windows/win32/api/appmodel/nf-appmodel-getapplicationusermodelid
4. Also useful: `GetPackageFamilyName(hProcess, &len, pfn)` and `GetPackageFullName`.

**[CONFIRMED]** AUMID format = `PackageFamilyName` + `!` + `PRAID` (Package Relative Application ID), e.g. `Microsoft.WindowsCalculator_8wekyb3d8bbwe!App`.

**Return codes:** `GetApplicationUserModelId` returns `APPMODEL_ERROR_NO_APPLICATION` (15703) for a plain Win32 process — this is *the normal, expected path* for Notepad/VS Code/Chrome, not an error. Also returns `ERROR_INSUFFICIENT_BUFFER` (122) on the sizing call.

**Modern note:** Windows 11 has been migrating apps off ApplicationFrameHost (many inbox apps are now WinUI 3 / plain Win32 and report correctly). But the frame-host path still exists and must be handled. Do not assume it's gone.

### Recommended identity record for Noto

```
AppIdentity {
  Tier0: Aumid           (packaged apps, most stable, survives app updates)
  Tier1: ExePath         (Win32; normalize case; watch %LOCALAPPDATA% version dirs!)
  Tier2: WindowClass     (always available, even for elevated)
  Tier3: TitlePattern    (volatile — changes per document/tab)
}
```

**Gotcha:** Electron/Squirrel apps (Slack, Discord, VS Code Insiders...) install into versioned folders like
`%LOCALAPPDATA%\slack\app-4.35.126\slack.exe` — the exe path **changes on every auto-update**. Match on the *filename + a stable ancestor folder*, not the full path.

## A4. Making a note window FOLLOW another window — feasibility

This is the hardest requirement in the brief. Honest assessment:

### What does NOT work
- **`SetParent`** into a foreign process's window — technically possible, catastrophic in practice: cross-process input queue attachment (`AttachThreadInput` semantics), z-order/DPI chaos, and it will hang Noto whenever the host app hangs. **Do not.**
- **`SetWindowsHookEx(WH_CALLWNDPROC)`** — requires a native DLL injected into the target; same disqualifier as WINEVENT_INCONTEXT for a C# app, plus AV flags.
- **DWM** — `DwmRegisterThumbnail` only *mirrors pixels*; it does not tell you geometry and cannot host live interactive content. `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)` is useful for *correct* bounds (see below) but is a pull API, not a notification.

### What actually works
`SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE)` filtered to the *target thread*. **[CONFIRMED]** — this is exactly Raymond Chen's prescribed answer to "How can I write a program that monitors another window for a change in size or position?"
https://devblogs.microsoft.com/oldnewthing/20210104-00/?p=104656

Chen's pattern:
```csharp
GetWindowThreadProcessId(targetHwnd, out uint pid);
uint tid = GetWindowThreadProcessId(targetHwnd, out _);
hook = SetWinEventHook(
    EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE,
    IntPtr.Zero, callback,
    pid, tid,                       // <-- SCOPE TO THE TARGET THREAD. critical.
    WINEVENT_OUTOFCONTEXT);

// in callback: accept only if
//   hwnd == targetHwnd && idObject == OBJID_WINDOW(0) && idChild == CHILDID_SELF(0)
// then GetWindowRect(); it CAN FAIL if the window was destroyed — handle it.
```

Scoping by `(pid, tid)` instead of `(0, 0)` is what makes this affordable — you no longer receive every cursor move system-wide. **This is the single most important perf decision in the whole feature.**

### Latency & flicker — expectations to set

**[CONFIRMED]** OUTOFCONTEXT delivery is **asynchronous and queued** across a process boundary. You are structurally one or more frames behind the target window.

**[COMMUNITY]** Observed behavior in WindowTop / PowerToys-class tools: the follower window visibly **lags and "rubber-bands"** during a fast drag, and during a live resize the follower tears. There is no API that makes a foreign-process follower pixel-locked. Anyone claiming otherwise is either injecting a DLL or is `SetParent`-ing.

Mitigation strategy (what real tools do):

```
MOVESIZESTART  -> enter "drag mode":
                    * optionally HIDE the note (ShowWindow SW_HIDE) or
                      drop to a lightweight ghost/outline
                    * raise LOCATIONCHANGE handling to every event
MOVESIZEEND    -> exit drag mode: snap to final GetWindowRect, show note
idle           -> LOCATIONCHANGE coalesced on a ~16ms timer (one reposition per frame)
```

Reposition with `SetWindowPos(hwndNote, HWND_TOPMOST, x, y, cx, cy, SWP_NOACTIVATE | SWP_NOREDRAW?)`.
**`SWP_NOACTIVATE` is mandatory** — without it the note steals focus on every single move and the target app becomes unusable.

### Use EXTENDED_FRAME_BOUNDS, not GetWindowRect

**[CONFIRMED]** On DWM-composited Windows (Vista+), `GetWindowRect` returns bounds that include the **invisible resize border** — typically ~7–8px of slop on left/right/bottom. Docking a note "flush" using `GetWindowRect` produces a visible gap.

```csharp
DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS /*9*/, out RECT r, Marshal.SizeOf<RECT>());
```
https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/nf-dwmapi-dwmgetwindowattribute

Use `DWMWA_EXTENDED_FRAME_BOUNDS` for all alignment math. Also check `DWMWA_CLOAKED` (14) — a **cloaked** window is on another virtual desktop or is a suspended UWP app; it is "visible" by `IsWindowVisible` but must be treated as hidden. This is the *only* reliable, documented way to detect "target is on a different virtual desktop."

### Polling alternative
**[COMMUNITY]** A 60–100ms `GetWindowRect` poll on a background timer is ~free CPU-wise when scoped to one window and is *more predictable* than the event storm. Pragmatic hybrid:

```
events (FOREGROUND, MOVESIZESTART/END, MINIMIZE*, DESTROY)  -> state machine
polling (16-33ms, only while a note is attached AND target is visible) -> geometry
```
This is the recommended Noto design — it bounds worst-case CPU and removes reentrancy hazards.

## A5. Minimize / maximize / restore / monitor / virtual desktop

| Signal | Mechanism | Confidence |
|---|---|---|
| Minimize start/end | `EVENT_SYSTEM_MINIMIZESTART` (0x16) / `MINIMIZEEND` (0x17) | **[CONFIRMED]** |
| Current state | `GetWindowPlacement()` → `SW_SHOWMINIMIZED` / `SW_SHOWMAXIMIZED` / `SW_SHOWNORMAL`; or `IsIconic()` / `IsZoomed()` | **[CONFIRMED]** |
| Monitor for a window | `MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST)` + `GetMonitorInfo` | **[CONFIRMED]** |
| Monitor set changed | `WM_DISPLAYCHANGE`; WinUI: `DisplayArea.GetFromWindowId()`; `Microsoft.UI.Windowing.DisplayArea` | **[CONFIRMED]** |
| DPI changed | `WM_DPICHANGED` (has *suggested rect* in lParam — you must honor it) | **[CONFIRMED]** |
| Window on current vdesk | `IVirtualDesktopManager::IsWindowOnCurrentVirtualDesktop` | **[CONFIRMED]** |
| Window's vdesk GUID | `IVirtualDesktopManager::GetWindowDesktopId` | **[CONFIRMED]** |
| **Enumerate desktops / desktop names / switch notification** | **undocumented `IVirtualDesktopManagerInternal`** | **[COMMUNITY — BLOCKER-ADJACENT]** |

### IVirtualDesktopManager — what's safe

**[CONFIRMED]** The *documented* interface is small:
`CLSID_VirtualDesktopManager = {AA509086-5CA9-4C25-8F95-589D3C07B48A}`, `IID {A5CD92FF-29BE-454C-8D04-D82879FB3F1B}`, in `shobjidl_core.h`. Three methods: `IsWindowOnCurrentVirtualDesktop`, `GetWindowDesktopId`, `MoveWindowToDesktop`.
https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ivirtualdesktopmanager

**Known limitation [COMMUNITY]:** `GetWindowDesktopId` returns `GUID_NULL` for windows that are not "top-level, owned by the caller's process, and currently shown" — in practice it frequently returns `GUID_NULL` for foreign windows and for minimized windows. It is unreliable as a primary signal.

**[COMMUNITY — HIGH RISK]** Everything richer (enumerating desktops, desktop names, registering for desktop-switch notifications) requires `IVirtualDesktopManagerInternal` / `IVirtualDesktopNotificationService`, which are **undocumented COM interfaces whose IIDs Microsoft changes between Windows builds**. Projects like `MScholtes/VirtualDesktop` and `Ciantic/VirtualDesktopAccessor` maintain per-build IID tables and ship separate binaries for Win10 / Win11 21H2 / 22H2 / 23H2 / 24H2.
- https://github.com/MScholtes/VirtualDesktop
- https://github.com/Ciantic/VirtualDesktopAccessor

**Recommendation for Noto:** do **not** take a dependency on undocumented vdesk internals. Instead, derive the signal you actually need — "is my target currently visible to the user?" — from **`DWMWA_CLOAKED`** (documented) plus `IsWindowVisible`. A window cloaked with `DWM_CLOAKED_SHELL` is on another virtual desktop. This gets you 90% of the behavior with 0% of the build fragility.

## A6. STABLE WINDOW IDENTITY ACROSS SESSIONS — **[HIGH RISK / partially UNKNOWN]**

**[CONFIRMED]** `HWND` is not stable. It is not stable across app restarts, and Windows **recycles HWND values**. A stale HWND can silently resolve to a *different, live* window. Never persist an HWND. (Same for PID and thread ID.)

**There is no OS-provided durable window identity.** This is a genuine platform gap, not something being missed.

What *is* available:

| Candidate | Stability | Notes |
|---|---|---|
| **AUMID** | **Best** for packaged apps | survives reinstall/update; app-level not window-level |
| `GetPackageFamilyName` | Very good | app-level only |
| **Exe path (normalized)** | Good for Win32 | breaks on Squirrel/Electron versioned dirs |
| **Window class name** | Good | `Chrome_WidgetWin_1`, `Notepad`, `CabinetWClass` — app-level, not window-level |
| `GetPropW(hwnd, "AppUserModelID")` | Medium | apps *may* set `SetCurrentProcessExplicitAppUserModelID` / per-window AUMID prop; often absent |
| **Window title** | Poor | per-document, but it's the ONLY per-window discriminator you get |
| `GetWindowLongPtr(GWLP_HINSTANCE)` | Useless across sessions | |

### Practical re-binding design

You cannot re-bind to "that exact window." You can only re-bind to **"a window matching this fingerprint,"** with user confirmation as the escape hatch.

```
NoteBinding {
  aumid?         : string        // strongest, if packaged
  exeName        : string        // "Code.exe"  (filename, NOT full path)
  exeDirAnchor?  : string        // stable ancestor, e.g. "...\\Microsoft VS Code"
  windowClass    : string        // "Chrome_WidgetWin_1"
  titleRegex?    : string        // user-editable, e.g. ".*\\bNoto\\b.*"
  monitorDeviceId?: string       // from DISPLAY_DEVICE / EnumDisplayDevices, for placement
  lastRect       : RECT          // fallback placement
  confidence     : enum { Exact, Probable, Ambiguous }
}

resolve():
  candidates = EnumWindows(visible && !cloaked && has title)
             |> filter(class == windowClass)
             |> filter(exeName matches)
             |> score(titleRegex match, lastRect proximity)
  0 matches   -> note stays "detached", shown in a tray list
  1 match     -> auto-bind
  2+ matches  -> bind to best score, but surface a "re-attach?" affordance
```

**Recommend making detached notes a first-class state**, not an error. Every tool in this space (Sticky Notes, WindowTop, note-pinners) ultimately does this because the OS gives no alternative.

**[UNKNOWN]** Whether any Windows 11 24H2+/25H2 API exposes a durable per-window token. Nothing found. Assume not.

---

# B. ALWAYS-ON-TOP, OPACITY, CLICK-THROUGH

## B1. Always-on-top — **safe, fully supported**

**[CONFIRMED]** Two equivalent routes; prefer the managed one.

```csharp
// Managed (preferred)
var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
var id   = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
var aw   = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(id);
if (aw.Presenter is OverlappedPresenter p) p.IsAlwaysOnTop = true;
```
https://learn.microsoft.com/en-us/windows/apps/develop/ui/manage-app-windows

> "Set **IsAlwaysOnTop** to `true` to keep this window on top of other windows."

Windows App SDK 1.3+ exposes `Window.AppWindow` directly (no interop dance needed). Pre-1.3 use the `GetWindowIdFromWindow` chain above.

```csharp
// Raw Win32 equivalent — interoperates fine
SetWindowPos(hwnd, HWND_TOPMOST /*-1*/, 0,0,0,0, SWP_NOMOVE|SWP_NOSIZE|SWP_NOACTIVATE);
```

**[CONFIRMED]** `AppWindow` is documented to work alongside raw HWND: "The AppWindow class is available for *any* top-level HWND in your app... you can continue to use that framework's entry point." Mixing `SetWindowPos` and `OverlappedPresenter` is supported. **[COMMUNITY]** caveat: the presenter caches its notion of state, so if you toggle topmost via raw `SetWindowPos`, `IsAlwaysOnTop` can read stale. Pick one mechanism and stay with it.

**Useful presenter surface [CONFIRMED]:**
`OverlappedPresenter.Create() / CreateForToolWindow() / CreateForContextMenu() / CreateForDialog()`, plus `SetBorderAndTitleBar(bool hasBorder, bool hasTitleBar)`, `IsResizable`, `IsMinimizable`, `IsMaximizable`, `IsModal`, `PreferredMinimum/MaximumWidth/Height`.

Constraint: **you cannot have a title bar without a border** — `SetBorderAndTitleBar(false, true)` throws `The parameter is incorrect. Invalid combination: Border=false, TitleBar=true.`

`CreateForToolWindow()` is the right starting presenter for a sticky note (`WS_EX_TOOLWINDOW` semantics → no taskbar button, no alt-tab entry).

## B2. Per-window opacity — **[HIGH RISK]**

**[CONFIRMED]** The Win32 API is:
```cpp
SetWindowLongPtr(hwnd, GWL_EXSTYLE, ex | WS_EX_LAYERED);   // 0x00080000
SetLayeredWindowAttributes(hwnd, 0, alpha /*0-255*/, LWA_ALPHA /*2*/);
```
https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setlayeredwindowattributes

**The problem with WinUI 3:** WinUI 3 renders through **`Microsoft.UI.Composition` → DirectComposition → Direct3D**, into a composition surface. The HWND is essentially a host for a composition island; its pixels are not produced by GDI/DWM in the way `WS_EX_LAYERED` was designed around.

**[COMMUNITY]** Reported outcomes vary and are version-dependent:
- `LWA_ALPHA` **does** apply uniform alpha to the whole window on recent Windows App SDK builds — this is the mostly-working case.
- `LWA_COLORKEY` is unreliable — WinUI's composited output doesn't reliably produce the exact key color.
- Interaction with `Mica`/`Acrylic` backdrops (`SystemBackdrop`) is broken: combining a layered style with a `MicaBackdrop` produces black or fully-transparent regions.
- WinUI 3 previously shipped a forced black/white window background that developers removed with `SetLayeredWindowAttributes` as a *hack* (Windows App SDK 1.1 era) — i.e. this API has historically been used to work *around* WinUI 3, which tells you how load-bearing and fragile the interaction is.
- Discussion: https://github.com/microsoft/microsoft-ui-xaml/issues/2956 and https://github.com/microsoft/microsoft-ui-xaml/discussions/8191 ("Is SwapChainPanel's Opacity property supported at all?" — "Blending a swapchain with WinUI 3 content is not currently supported, which is a regression compared to the inbox XAML layer.")

**[CONFIRMED]** Hard API constraint worth knowing: once `SetLayeredWindowAttributes` has been called, subsequent `UpdateLayeredWindow` calls fail until the `WS_EX_LAYERED` bit is cleared and re-set.

### RECOMMENDATION (B2)
**Do not use `WS_EX_LAYERED` for note opacity.** Instead:
```
Set the ROOT XAML element's Opacity (or a Border's Background brush alpha).
   + fully supported, GPU-composited, animatable with Storyboard
   + composes correctly with Mica/Acrylic
   - does NOT make the window frame translucent (use SetBorderAndTitleBar(false,false)
     + a custom XAML chrome, which you want anyway for a sticky note)
```
This gives the visual result users want with zero platform risk. Reserve `WS_EX_LAYERED` for a "ghost mode" experiment behind a feature flag, and test it on Win10 22H2, Win11 23H2, and Win11 24H2+ separately.

## B3. Click-through — **[HIGH RISK / effectively a BLOCKER for partial click-through]**

**[CONFIRMED]** The Win32 mechanism:
```cpp
ex |= WS_EX_LAYERED | WS_EX_TRANSPARENT;   // 0x00080000 | 0x00000020
SetWindowLongPtr(hwnd, GWL_EXSTYLE, ex);
```
`WS_EX_TRANSPARENT` requires `WS_EX_LAYERED` to actually pass input through on composited Windows.

**Two distinct features — do not conflate them:**

| Feature | Verdict |
|---|---|
| **Whole-window click-through** ("ghost mode": note visible, all clicks go to the app underneath) | **[COMMUNITY] WORKS.** Set `WS_EX_LAYERED\|WS_EX_TRANSPARENT`. The catch: you then can't click the note to turn it off — you need a global hotkey (see D) or tray menu to toggle back. |
| **Per-pixel / partial click-through** (clicks pass through the transparent gaps, but hit the opaque note body) | **[COMMUNITY] NOT SUPPORTED in WinUI 3.** |

**[COMMUNITY]** The reason, stated plainly in microsoft-ui-xaml discussion #10746 ("Add input pass-through (not click-through) through transparent parts of a WinUI 3 window"):

> WinUI 3 "uses composition to draw its contents with hardware-accelerated Direct3D, meaning the contents are never really seen by the window itself, so the window will never know how to pass input through."

`WS_EX_LAYERED + LWA_COLORKEY` makes pixels *visually* transparent but Windows still hit-tests them as part of your window. The only workarounds offered are:
- **`SetWindowRgn`** — define a non-rectangular input region. 30-year-old API, hard-edged (no antialiasing, no soft shadows), must be recomputed on every resize/DPI change, and does not follow rounded corners nicely.
- Handle `WM_NCHITTEST` and return `HTTRANSPARENT` for regions you want to pass through. **[COMMUNITY]** This is the cleanest approach and *does* work with WinUI 3 (it operates at the HWND level, before XAML sees the input). Requires subclassing (see D2). Combine with `WS_EX_LAYERED` alpha.

https://github.com/microsoft/microsoft-ui-xaml/discussions/10746
https://learn.microsoft.com/en-us/answers/questions/1418063/winui3-semi-transparent-window-click-through-windo

### RECOMMENDATION (B3)
Ship **binary ghost mode** (whole window click-through, toggled by global hotkey), not per-pixel. Design the note as a solid rounded rect so per-pixel pass-through isn't needed. If you later want it, implement `WM_NCHITTEST` → `HTTRANSPARENT` via subclass, not `SetWindowRgn`.

## B4. Borderless / custom chrome — **safe**

**[CONFIRMED]** Two supported routes:

```csharp
// 1. Fully borderless (what a sticky note wants)
((OverlappedPresenter)appWindow.Presenter).SetBorderAndTitleBar(false, false);
// You now own dragging: implement it yourself.

// 2. Extend content into the title bar, keep system caption buttons
appWindow.TitleBar.ExtendsContentIntoTitleBar = true;
appWindow.TitleBar.SetDragRectangles(new[] { new RectInt32(...) });
// or, XAML-level:  this.ExtendsContentIntoTitleBar = true; this.SetTitleBar(MyGrid);
```
https://learn.microsoft.com/en-us/windows/apps/develop/title-bar

**Gotchas [COMMUNITY]:**
- With `SetBorderAndTitleBar(false, false)` you lose window dragging entirely. Implement via `WM_NCHITTEST` → `HTCAPTION` (clean, gives you snap/shake/aero-snap for free) rather than manual pointer-drag + `AppWindow.Move` (which loses snapping and stutters).
- `SetDragRectangles` coordinates are in **physical pixels**, not DIPs — you must multiply by the current scale factor (`XamlRoot.RasterizationScale` or `GetDpiForWindow(hwnd)/96.0`). Forgetting this is the #1 title-bar bug.
- Rounded corners: `DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE /*33*/, ref DWMWCP_ROUND /*2*/, 4)` — Windows 11 only, silently no-ops on Win10.

---

# C. EDGE DOCKING / APPBAR

## C1. SHAppBarMessage

**[CONFIRMED]** `SHAppBarMessage(DWORD dwMessage, PAPPBARDATA pData)` in `shellapi.h`.
https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shappbarmessage

Lifecycle:
```
ABM_NEW          (0x00) -> register; supply uCallbackMessage (a WM_APP+n you own)
ABM_QUERYPOS     (0x02) -> ask shell to adjust your proposed rect
ABM_SETPOS       (0x03) -> commit; shell returns the FINAL rect (may differ!)
     then MoveWindow to the rect SETPOS returned, not the one you asked for
ABM_GETTASKBARPOS(0x05)
ABM_ACTIVATE     (0x06) -> on WM_ACTIVATE
ABM_WINDOWPOSCHANGED (0x09) -> on WM_WINDOWPOSCHANGED
ABM_SETAUTOHIDEBAREX (0x0B) -> auto-hide, PER MONITOR
ABM_GETAUTOHIDEBAREX (0x0C)
ABM_REMOVE       (0x01) -> MUST be called on exit
```

Your window must handle the `uCallbackMessage` you registered and react to:
- `ABN_POSCHANGED` (0x1) — taskbar/another appbar moved → re-run QUERYPOS/SETPOS
- `ABN_FULLSCREENAPP` (0x2) — a fullscreen app appeared → drop out of topmost
- `ABN_STATECHANGE` (0x0)

**[CONFIRMED]** `ABM_SETAUTOHIDEBAREX` / `ABM_GETAUTOHIDEBAREX` are the **multi-monitor-aware** variants ("extends ABM_SETAUTOHIDEBAR by enabling you to specify a particular monitor"). Always use the `EX` forms.
https://learn.microsoft.com/en-us/windows/win32/shell/abm-setautohidebarex

**[CONFIRMED]** Only **one auto-hide appbar per edge per monitor** is allowed system-wide. If Teams/Slack/a dock already owns that edge, `ABM_SETAUTOHIDEBAREX` fails. Must handle gracefully.

### Failure modes — **[COMMUNITY, MEDIUM-HIGH RISK]**
- **Crash without `ABM_REMOVE` = permanently stolen desktop space.** If Noto crashes or is killed while registered, the shell keeps the reserved work area until logoff (or until another app re-registers). This is a very visible, very angry-support-ticket bug. You need `ABM_REMOVE` in a `SetConsoleCtrlHandler`-equivalent / `AppDomain.CurrentDomain.ProcessExit` / unhandled-exception path, **plus** a startup self-heal.
- **Per-monitor DPI**: appbar rects are in **physical screen pixels**, and the shell's work-area math is global. Real bug report: ramensoftware/windhawk-mods #4449 — after switching from a wide external monitor to a smaller laptop screen with different DPI, "the use of a global taskbar height causes windows to stop using the full available height when maximized (workarea/appbar computation becomes incorrect)."
  https://github.com/ramensoftware/windhawk-mods/issues/4449
- Interaction with Win11 taskbar auto-hide and multiple appbars is genuinely buggy.

## C2. AppBar vs plain topmost window — **recommendation: DON'T use AppBar**

| | AppBar | Topmost window snapped to edge |
|---|---|---|
| Reserves desktop space (maximized apps stop short) | Yes | No |
| Risk of permanently corrupting work area | **Yes** | No |
| Per-monitor DPI correctness | Hard | Normal |
| Multi-monitor | Fiddly (`EX` messages) | Trivial |
| Auto-hide | Shell-managed, one per edge | You implement (easy: mouse-enter/leave + animation) |
| Users' actual expectation for a notes sidebar | mixed | fine |

**Recommendation:** ship a **topmost window positioned at the working-area edge** (`DisplayArea.GetFromWindowId(id, DisplayAreaFallback.Nearest).WorkArea`) with self-implemented slide-in/slide-out. Reserve real AppBar registration as an opt-in "reserve screen space" setting for power users, implemented late, with a robust remove-on-exit + self-heal path. The downside of *not* being an appbar is only that maximized windows will be covered — which for a topmost overlay is arguably the intent.

## C3. DPI awareness

**[CONFIRMED]** WinUI 3 / Windows App SDK apps are **`PerMonitorV2`** DPI aware by default (set in the generated app manifest, `<dpiAwareness>PerMonitorV2</dpiAwareness>` / `DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2`). You generally should not change this.
https://learn.microsoft.com/en-us/windows/win32/hidpi/high-dpi-desktop-application-development-on-windows

Rules for Noto:
- **All Win32 APIs (`GetWindowRect`, `SetWindowPos`, `APPBARDATA.rc`, `SetDragRectangles`) speak PHYSICAL PIXELS.**
- **All XAML speaks DIPs.**
- Conversion: `physical = dip * (GetDpiForWindow(hwnd) / 96.0)`, or use `XamlRoot.RasterizationScale`.
- `Microsoft.UI.Windowing.AppWindow.Move/Resize/MoveAndResize` take **physical pixels** (`Windows.Graphics.PointInt32`/`SizeInt32`), NOT DIPs. Frequent source of "window is the wrong size on my 4K laptop."
- Handle `WM_DPICHANGED` — the `lParam` carries a **suggested RECT** you are expected to apply. WinUI's own window does this; if you're doing custom positioning you must re-derive it.
- **Mixed-DPI follow scenario:** if the target window is on a 150% monitor and the note is dragged to a 100% monitor, your geometry math must be per-monitor. Always recompute from `MonitorFromWindow(target)`.

---

# D. GLOBAL HOTKEYS

## D1. RegisterHotKey vs WH_KEYBOARD_LL

**[CONFIRMED]** `RegisterHotKey(HWND hWnd, int id, UINT fsModifiers, UINT vk)`
https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey

Modifiers: `MOD_ALT` 0x1, `MOD_CONTROL` 0x2, `MOD_SHIFT` 0x4, `MOD_WIN` 0x8, `MOD_NOREPEAT` 0x4000.

**Always pass `MOD_NOREPEAT`** — without it, holding the combo floods you with `WM_HOTKEY`.

| | `RegisterHotKey` | `SetWindowsHookEx(WH_KEYBOARD_LL)` |
|---|---|---|
| Managed-only impl | **Yes** (no DLL needed — LL hooks are the exception among hook types) | Yes |
| Chords / sequences (e.g. `Ctrl+K, N`) | No — single combo only | Yes |
| Suppress the key from other apps | Yes (it's exclusive) | Yes (return 1 from the proc) |
| Conflict behavior | `RegisterHotKey` **fails** (`ERROR_HOTKEY_ALREADY_REGISTERED`, 1409) if taken | Silently wins/races |
| Reserved combos | Cannot take `Win+L`, `Ctrl+Alt+Del`, most `Win+<letter>` shell keys | Also cannot take `Ctrl+Alt+Del` (SAS); can take some Win keys |
| Works over elevated/admin windows | **No (UIPI)** | **No (UIPI)** |
| Antivirus / EDR reaction | None | **Frequently flagged as a keylogger** |
| Perf risk | Zero | **Serious** — your callback is on the input path for EVERY keystroke system-wide |
| Timeout behavior | n/a | If your proc exceeds `LowLevelHooksTimeout` (default 300ms, `HKCU\Control Panel\Desktop`) Windows **silently removes your hook** |

**[CONFIRMED-adjacent / well-established]** UIPI: a medium-integrity process cannot see or intercept input destined for a high-integrity (elevated) window. **Neither** mechanism gives Noto hotkeys while an elevated app (Task Manager, an admin terminal, regedit) has focus, unless Noto is elevated or has UIAccess. Do not promise "works everywhere."

### RECOMMENDATION (D1)
**`RegisterHotKey` only.** For a notes app:
- Zero AV risk, zero input-latency risk, and the OS arbitrates conflicts for you (which is a *feature* — you can tell the user "Ctrl+Shift+N is taken").
- The loss (no chords) is not worth the cost of WH_KEYBOARD_LL. If you truly need chords later, use `RegisterHotKey` for the *leader* key and then a temporary focused-window key capture for the second key.
- **Handle failure explicitly.** `RegisterHotKey` returning false is normal (another app has it) — surface it in Settings, don't swallow it.

## D2. Receiving WM_HOTKEY in WinUI 3 — **[CONFIRMED pattern, needs interop]**

WinUI 3's `Window` does not expose a WndProc. Two supported ways in:

**Option A — `SetWindowSubclass` (recommended).** `commctrl.h`, `Comctl32.dll`.
https://learn.microsoft.com/en-us/windows/win32/api/commctrl/nf-commctrl-setwindowsubclass

```csharp
const int WM_HOTKEY = 0x0312;

private SUBCLASSPROC? _subclassProc;   // MUST be a field — GC will collect a local

void HookWndProc() {
    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
    _subclassProc = new SUBCLASSPROC(WndProc);
    SetWindowSubclass(hwnd, _subclassProc, uIdSubclass: 1, dwRefData: 0);
    RegisterHotKey(hwnd, HOTKEY_ID, MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, (uint)'N');
}

nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam, nuint id, nuint refData) {
    if (msg == WM_HOTKEY && (int)wParam == HOTKEY_ID) {
        DispatcherQueue.TryEnqueue(ShowQuickCapture);
        return 0;
    }
    return DefSubclassProc(hWnd, msg, wParam, lParam);
}
```

Same GC pitfall as the WinEvent delegate: **root the `SUBCLASSPROC`**. Call `RemoveWindowSubclass` + `UnregisterHotKey` on close.

**Option B — `CsWin32` (`Microsoft.Windows.CsWin32`)** source generator gives you `PInvoke.RegisterHotKey`, `PInvoke.SetWindowSubclass`, correct `SafeHandle`s, and `[UnmanagedCallersOnly]`-friendly signatures without hand-written DllImports. **Strongly recommended for this project** given the volume of Win32 surface involved. Add a `NativeMethods.txt` listing the APIs you want.

**[COMMUNITY]** Worked WinUI 3 walkthrough: https://whid.eu/2022/05/13/chapter-5-add-global-hot-key-in-winui-3/

**Design note:** register the hotkey against a **hidden message-only window** on your dedicated hook thread, not against the visible note window. Notes get created and destroyed; the hotkey should not.

---

# E. CAPTURE

## E1. Windows.Graphics.Capture — **[CONFIRMED, works in WinUI 3]**

https://learn.microsoft.com/en-us/windows/apps/develop/media-authoring-processing/screen-capture

- **Min version:** Windows 10 1803 (build 17134) for the base API. `GraphicsCaptureItem.TryCreateFromWindowId`/`DisplayId` and `IsBorderRequired` are later (1903 / 20348+). **Always gate on `GraphicsCaptureSession.IsSupported()`** — MS explicitly says support can be absent for hardware reasons.
- **Desktop only** — "only supported on Windows desktop devices and Windows Mixed Reality immersive headsets."

### Permission / consent model — read carefully

There are **two** distinct paths and they have very different UX:

```
PATH 1: GraphicsCapturePicker  (user-consented, no capability needed)
   picker = new GraphicsCapturePicker();
   InitializeWithWindow.Initialize(picker, hwnd);       // REQUIRED in WinUI 3
   GraphicsCaptureItem item = await picker.PickSingleItemAsync();
   -> secure system UI; user explicitly picks the window/display
   -> YELLOW BORDER drawn around the captured item for the whole session
   -> no manifest capability required

PATH 2: IGraphicsCaptureItemInterop::CreateForWindow(hwnd)  (programmatic)
   -> NO picker, NO per-capture user prompt
   -> still draws the YELLOW BORDER
   -> this is how PowerToys / OBS capture a specific known HWND
```

**[CONFIRMED]** The yellow border is the system's consent indicator: "a yellow notification border is drawn by the system around the actively captured item. In the case of multiple simultaneous capture sessions, a yellow border is drawn around each item being captured."

**Removing the border requires packaged identity + a restricted capability [CONFIRMED]:**
> "Before the system will disable the colored border..., your app must get consent from the user by calling `GraphicsCaptureAccess.RequestAccessAsync`, passing in the value `GraphicsCaptureAccessKind.Borderless`, which displays a prompt to the user. To call `RequestAccessAsync` with `GraphicsCaptureAccessKind.Borderless`, you must declare the **`graphicsCaptureWithoutBorder`** capability in your app's package manifest."

> "If the user denies access, setting the `IsBorderRequired` property to `false` will succeed, but the value will be ignored and the border will be displayed."

https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.graphicscapturesession.isborderrequired

`graphicsCaptureWithoutBorder` is a **restricted capability** → requires MSIX + Store approval justification if you ship via Store. Also relevant: `graphicsCaptureProgrammatic` (allows `CreateForWindow` without the picker in constrained contexts).

**Verdict for Noto:** capturing a snippet of the tracked window is feasible. Accept the yellow border (it's honest UX for a notes app anyway). Use `CreateForWindow` via `IGraphicsCaptureItemInterop` since you already know the HWND — no picker needed. Do **not** chase `graphicsCaptureWithoutBorder`.

### Interop for CreateForWindow (C#)
```csharp
[ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IGraphicsCaptureItemInterop {
    IntPtr CreateForWindow([In] IntPtr window, [In] ref Guid iid);
    IntPtr CreateForMonitor([In] IntPtr monitor, [In] ref Guid iid);
}
```
Get it by QI'ing the `GraphicsCaptureItem` activation factory. `CsWin32` + `WinRT.Interop` reduce this boilerplate.

### Performance
- Zero-copy: frames arrive as `Direct3D11CaptureFrame` → `IDirect3DSurface` on the GPU. Converting to CPU bitmap is the expensive part.
- `FrameArrived` fires on a **background pool thread**. **[CONFIRMED]** "avoid doing heavy work on the UI thread for FrameArrived." In WinUI 3 specifically: "CanvasComposition surface operations must be dispatched to the UI thread using `DispatcherQueue.TryEnqueue`."
- Must always `Dispose()` the frame to return the buffer to the pool, and **never retain a reference** to the frame or its surface.
- For Noto (one-shot screenshot, not video), create pool → `StartCapture` → take first frame → dispose everything. Don't keep a session alive; a live session keeps the yellow border on.

## E2. Legacy BitBlt / PrintWindow — **[COMMUNITY: unreliable, use as fallback only]**

- `BitBlt` from a window DC: **[COMMUNITY]** "BitBlt only works on applications that use the Windows device context — not all apps do." Fails (black) on Chrome, Electron, anything D3D/hardware-accelerated, and all UWP.
- `PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT /*0x2*/)` — Windows 8.1+. Better: handles many DWM-composited windows. **[COMMUNITY]** Still returns black for some D3D/Chromium surfaces; CefSharp issue #2286 documents exactly this regression.
- **[COMMUNITY]** Windows 10 1809 changed clipping: "Device Context contents are no longer updated for parts of windows located offscreen" — partially-offscreen windows capture as garbage.
- Undocumented `DwmGetDxSharedSurface` / `DwmpDxGetWindowSharedSurface` exist but are unexported/undocumented — **do not ship**.

**Verdict:** `Windows.Graphics.Capture` is the primary. `PrintWindow(PW_RENDERFULLCONTENT)` is an acceptable fallback for pre-1803 or when `IsSupported()` is false. Expect black frames and validate (e.g. check the bitmap isn't uniformly one color).

## E3. Clipboard

| | `Windows.ApplicationModel.DataTransfer.Clipboard` | Win32 `AddClipboardFormatListener` |
|---|---|---|
| Read/write | `GetContent()` / `SetContent()`, async, rich formats | `OpenClipboard`/`GetClipboardData`, manual |
| Change notification | `Clipboard.ContentChanged` event | `WM_CLIPBOARDUPDATE` (0x031D) |
| WinUI 3 usable | **Yes** | Yes (needs a WndProc / subclass) |
| Identity required | No | No |

**[COMMUNITY]** Known WinRT `Clipboard` gotchas in WinUI 3 desktop:
- `Clipboard.ContentChanged` is **unreliable in unpackaged desktop apps** and can stop firing; `AddClipboardFormatListener` + `WM_CLIPBOARDUPDATE` is the robust choice for a clipboard-watching feature.
- `Clipboard.GetContent()` can throw `COMException 0x800401D0 (CLIPBRD_E_CANT_OPEN)` when another app holds the clipboard — **always retry with backoff** (3 tries, 50ms). This is not optional; it will happen in the wild.
- Marshal to the UI thread before touching WinRT clipboard APIs.

**Clipboard history [CONFIRMED]:** `Clipboard.IsHistoryEnabled()`, `Clipboard.GetHistoryItemsAsync()`, `Clipboard.SetHistoryItemAsContentAsync()`, `Clipboard.SetContentWithOptions(content, new ClipboardContentOptions { IsAllowedInHistory = true, IsRoamable = false })`. Windows 10 1809+. Requires the user to have clipboard history enabled; degrade gracefully.
https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.datatransfer.clipboard

## E4. Drag & drop from Explorer — **[MEDIUM-HIGH RISK, open bugs]**

**[CONFIRMED API]** `UIElement.AllowDrop = true`, handle `DragOver` (set `e.AcceptedOperation = DataPackageOperation.Copy`) and `Drop` (`await e.DataView.GetStorageItemsAsync()`).

**[COMMUNITY — multiple open microsoft-ui-xaml issues spanning 2020→2025]:**
- #10119 "WinUI 3 Desktop: Drag and drop file from Explorer to app window does not work" — `DragOver` never fires, red "no-drop" cursor. Reportedly works when dragging from Start's Recent list but not from Desktop/Explorer.
- #2715 "Unable to drop files onto Grid in WinUI3 Desktop"
- #10713 "Unable to drag and drop files onto anything in WinUI3 forms"
- #7366 / #10576 — WebView2 + drag/drop broken; works only intermittently after a file-picker interaction.
- **#4433 / #7690 — "Drag-and-drop in elevated WinUI3 applications is not supported"** and causes crashes. (This is actually UIPI, expected, but WinUI crashes rather than degrading.)
  https://github.com/microsoft/microsoft-ui-xaml/issues/10119
  https://github.com/microsoft/WindowsAppSDK/issues/4433

**Mitigations:**
1. **Never run Noto elevated.** (Reinforces the A1 decision.)
2. Set `AllowDrop` on a **`Grid`/`Border` with a non-null `Background`** (even `Transparent`) — a null background means no hit-testing, which causes a large fraction of the "drop doesn't work" reports.
3. Have a Win32 fallback ready: `DragAcceptFiles(hwnd, TRUE)` + handle `WM_DROPFILES` (0x0233) + `DragQueryFile` via your existing subclass. Crude (files only, no rich formats, no drag feedback) but it works reliably. Worth prototyping **early** to de-risk.

---

# F. PRIVACY / SCREEN CAPTURE EXCLUSION

**[CONFIRMED]** `BOOL SetWindowDisplayAffinity(HWND hWnd, DWORD dwAffinity)` — `winuser.h`, `User32.dll`.
https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity

| Flag | Value | Meaning | Min version |
|---|---|---|---|
| `WDA_NONE` | `0x00000000` | no restriction | Win7 |
| `WDA_MONITOR` | `0x00000001` | "displayed only on a monitor. Everywhere else, the window appears **with no content**" (→ **black rectangle** in captures) | Win7 |
| `WDA_EXCLUDEFROMCAPTURE` | **`0x00000011`** | "displayed only on a monitor. Everywhere else, the window **does not appear at all**" | **Windows 10 version 2004 (build 19041)** |

Note the value is `0x11`, not `0x2` — a commonly mis-transcribed constant.

### Constraints — read all of these

1. **[CONFIRMED]** "A handle to the **top-level window**. The window **must belong to the current process**." You cannot protect someone else's window; returns FALSE for non-top-level windows.
2. **[CONFIRMED]** "it works only when the **Desktop Window Manager (DWM) is composing the desktop**." Check `DwmIsCompositionEnabled`. **[COMMUNITY]** RDP sessions can disable DWM → exclusion silently stops working.
3. **[CONFIRMED] Graceful degradation:** "Setting the display affinity to `WDA_EXCLUDEFROMCAPTURE` on previous version of Windows will behave as if `WDA_MONITOR` is applied." So on Win10 1909 and earlier you get a **black box** instead of invisibility — visually worse but not a crash. Version-gate the UI copy accordingly.
4. **[CONFIRMED] It is explicitly NOT a security feature:** "unlike a security feature or an implementation of Digital Rights Management (DRM), there is no guarantee that using SetWindowDisplayAffinity... will strictly protect windowed content, for example where someone takes a photograph of the screen." **[COMMUNITY]** IOActive published a bypass of Signal Desktop's use of exactly this API.
   https://www.ioactive.com/signal-windows-desktop-contentprotection-bypass/

### What it does NOT hide
- Photographs of the screen.
- Accessibility / UI Automation trees (the *text* of your note is still readable via UIA).
- The window's presence in alt-tab, the taskbar, or `EnumWindows`.
- Anything a kernel-level or injected capture does.
- The **taskbar thumbnail/peek preview** — **[COMMUNITY]** behavior here has varied by build; test on your targets.

### WinUI 3 compatibility — **[COMMUNITY] works**
Operates on the HWND below the composition layer, so it is orthogonal to WinUI's renderer. Reports of it working in WinUI 3 / WPF / Electron are consistent.
```csharp
var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
SetWindowDisplayAffinity(hwnd, 0x00000011); // WDA_EXCLUDEFROMCAPTURE
```
**[COMMUNITY]** Caveat: must be re-applied if the HWND is recreated. Also apply it to *every* top-level window Noto creates (each note, flyouts, tray menus are separate HWNDs) — it is per-window, not per-process.

**Market the feature honestly:** "hide from screen sharing," **never** "secure" or "private."

---

# G. STORAGE & SEARCH

## G1. Microsoft.Data.Sqlite vs EF Core

| | `Microsoft.Data.Sqlite` | EF Core + `Microsoft.EntityFrameworkCore.Sqlite` |
|---|---|---|
| What it is | ADO.NET provider (thin) | ORM built *on top of* the above |
| Startup cost | negligible | **model build ~100–400ms first query** (matters for a tray app that must feel instant) |
| Deployment size | small | +several MB, more with migrations assembly |
| AOT / trimming | good | historically painful; improved but still the riskiest part of trimming a WinUI app |
| FTS5 | direct raw SQL, natural fit | awkward (see G2) |
| Schema evolution | you write the DDL | Migrations |

**Recommendation for Noto: `Microsoft.Data.Sqlite` + Dapper (or hand-rolled mapping).**
Rationale:
- A notes app's schema is tiny (notes, bindings, tags, attachments, fts index). EF Core's value is low.
- FTS5 is the *core* feature and EF Core actively gets in the way of it (G2).
- Cold-start latency matters a lot for a hotkey-invoked quick-capture window.
- Avoids the trimming/AOT risk entirely.

If you do use EF Core, use it *only* for the relational tables and drop to `FromSqlRaw` / a second `SqliteConnection` for FTS.

## G2. FTS5 — **usable, but EF Core does not model it**

**[CONFIRMED]** FTS5 is a compiled-in SQLite extension providing virtual tables, `MATCH`, BM25 `rank`, `snippet()`, `highlight()`. `Microsoft.Data.Sqlite` ships SQLitePCLRaw `e_sqlite3` which **includes FTS5 by default**.
https://www.sqlite.org/fts5.html

**[CONFIRMED — Brice Lam, EF Core team]** https://www.bricelam.net/2020/08/08/sqlite-fts-and-efcore.html
The canonical pattern:

```sql
-- Content table (normal, EF-mappable)
CREATE TABLE Notes (Id INTEGER PRIMARY KEY, Title TEXT, Body TEXT, ...);

-- External-content FTS index: no data duplication
CREATE VIRTUAL TABLE NotesFts USING fts5(
    Title, Body,
    content='Notes',
    content_rowid='Id',
    tokenize='unicode61 remove_diacritics 2'
);

-- Triggers keep it in sync
CREATE TRIGGER Notes_ai AFTER INSERT ON Notes BEGIN
  INSERT INTO NotesFts(rowid, Title, Body) VALUES (new.Id, new.Title, new.Body);
END;
CREATE TRIGGER Notes_ad AFTER DELETE ON Notes BEGIN
  INSERT INTO NotesFts(NotesFts, rowid, Title, Body) VALUES('delete', old.Id, old.Title, old.Body);
END;
CREATE TRIGGER Notes_au AFTER UPDATE ON Notes BEGIN
  INSERT INTO NotesFts(NotesFts, rowid, Title, Body) VALUES('delete', old.Id, old.Title, old.Body);
  INSERT INTO NotesFts(rowid, Title, Body) VALUES (new.Id, new.Title, new.Body);
END;
```

Query:
```sql
SELECT n.Id, n.Title,
       snippet(NotesFts, 1, '<b>', '</b>', '…', 24) AS Snip
FROM NotesFts f JOIN Notes n ON n.Id = f.rowid
WHERE NotesFts MATCH @q
ORDER BY rank
LIMIT 50;
```

### EF Core specifics — the friction
- **[CONFIRMED]** `MATCH` goes against the **table name**, not a column (`NotesFts MATCH ...`). EF Core's LINQ provider has no translation for this. EF Core issue #4823 has been open for years.
  https://github.com/dotnet/efcore/issues/4823
- You must create the virtual table and triggers with `migrationBuilder.Sql(...)` in a migration (or plain DDL at startup) — the model builder can't express it.
- Query it via `FromSqlRaw` onto a **keyless entity** (`modelBuilder.Entity<NoteSearchResult>().HasNoKey().ToView(null)`), or — cleaner — just use `SqliteCommand` directly.
- `rank`, `snippet()`, `highlight()`, `bm25()` are all raw SQL only.
- Reference implementation: https://github.com/VahidN/EFCoreSQLiteFTS

**Query-syntax gotcha [important]:** FTS5 `MATCH` takes a *query language*, not a literal. User input containing `"`, `*`, `:`, `-`, `NEAR`, `AND`/`OR`/`NOT` will either error (`SqliteException: fts5: syntax error`) or do something surprising. **You must sanitize/quote user input**, e.g. tokenize on whitespace and wrap each term in double quotes with `""` escaping, appending `*` for prefix search:
```
user: he said "hi" -> query: "he"* "said"* "hi"*
```
Skipping this is guaranteed crash-on-apostrophe.

## G3. Migrations for a shipped desktop app

**[CONFIRMED]** `context.Database.Migrate()` at startup is the documented desktop pattern.
https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying

**[CONFIRMED] SQLite-specific limitations** — this is the real issue:
> "SQLite's schema change capabilities are limited — for example, adding a column is supported, but removing a column is not supported."
> "SQLite doesn't currently support generating idempotent migration scripts."

EF Core works around drop-column/alter-column via a **table rebuild** (create new → copy → drop → rename), which on a large table is slow and, if it fails mid-way on a user's machine, leaves a mess.

**Recommendation (whether or not you use EF Core):**
```
1. PRAGMA user_version  -> integer schema version. Simplest possible migration ledger.
2. Migration runner = ordered list of (version, Action<SqliteConnection>).
3. BEFORE migrating: copy the .db to notes.db.bak-v{n}. Non-negotiable for user data.
4. Run all steps in ONE transaction where possible; SQLite DDL IS transactional.
5. Refuse to run on a NEWER user_version than the app knows (user downgraded) —
   show a clear message rather than corrupting.
6. Prefer additive-only schema changes forever. Never drop a column; deprecate it.
```
For a small app this hand-rolled runner is ~60 lines and strictly more predictable than EF migrations on SQLite.

Also set at open time:
```sql
PRAGMA journal_mode=WAL;      -- concurrent read while writing; big win for a tray app
PRAGMA synchronous=NORMAL;    -- safe with WAL, much faster
PRAGMA foreign_keys=ON;       -- OFF by default in SQLite!
PRAGMA busy_timeout=5000;     -- avoids "database is locked" under WAL
```

## G4. Encryption at rest

**[CONFIRMED]** Microsoft's own guidance page: https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/encryption

Options, ranked:

| Option | License | Notes |
|---|---|---|
| **Windows DPAPI on a field/blob** (`ProtectedData.Protect`, `DataProtectionScope.CurrentUser`) | free, in-box | **Simplest, recommended default.** Encrypt note *bodies*; leave metadata + FTS index plaintext. No native deps, no build changes. **Breaks FTS on encrypted fields** — that's the tradeoff. |
| **SQLCipher (Zetetic, commercial)** | **Paid** for the official packages | Whole-DB encryption, works with `Microsoft.Data.Sqlite` via the `Password` connection-string keyword. **[CONFIRMED, Zetetic]** "Do not include the e_sqlcipher bundles. Use the platform-specific `sqlcipher-*` packages together with the SQLitePCLRaw provider packages." https://www.zetetic.net/sqlcipher/sqlcipher-for-dotnet/ |
| **SQLite3 Multiple Ciphers (`SQLitePCLRaw.bundle_e_sqlite3mc`)** | free (MIT-ish) | Drop-in whole-DB encryption, honors the `Password` keyword. **[CONFIRMED — Brice Lam]** the recommended free route. https://www.bricelam.net/2023/11/10/more-sqlite-encryption.html |
| `SQLitePCLRaw.bundle_e_sqlcipher` | — | **[CONFIRMED] DEPRECATED.** Zetetic: "a third-party package that was never used or supported by Zetetic." **Do not use.** |
| BitLocker / EFS | free | Not app-controlled. Mention in docs; don't build on it. |

**Key management is the actual hard part.** With whole-DB encryption you must store the key somewhere; on Windows the right answer is **DPAPI-protect the key** (`CurrentUser` scope) in a sidecar file, or derive it from a user passphrase with PBKDF2/Argon2. Storing the key in plaintext next to the DB is theater.

**Recommendation:** ship **unencrypted + WAL by default** (users expect notes to be greppable/syncable), and offer **`bundle_e_sqlite3mc` whole-DB encryption as an opt-in "Protect my notes" setting** with a user passphrase. Whole-DB encryption keeps FTS5 working, which field-level DPAPI does not.

---

# H. PACKAGING & DEPLOYMENT

## H1. Packaged (MSIX) vs unpackaged — what needs identity

**[CONFIRMED]** https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/choose-packaging-model
https://learn.microsoft.com/en-us/windows/apps/get-started/intro-pack-dep-proc

> "Many Windows extensibility features... can be used by an app only if that app has **package identity at runtime**, because the operating system needs to be able to identify the caller of the corresponding API."

| Capability Noto may want | Needs package identity? |
|---|---|
| **App notifications (`AppNotificationManager`)** | **YES** |
| Push notifications | YES (or the WinAppSDK singleton workaround) |
| **`StartupTask` (startup on login, managed in Settings)** | **YES** |
| Protocol activation (`noto://`) | **YES** (manifest-declared) |
| File type association | **YES** |
| Background tasks / COM activation | **YES** |
| Share target | YES |
| Restricted capabilities (`graphicsCaptureWithoutBorder`) | YES |
| `Windows.Graphics.Capture` (picker or `CreateForWindow`) | **NO** — works unpackaged |
| `SetWindowDisplayAffinity` | NO |
| `RegisterHotKey`, WinEvent hooks, AppBar, clipboard | NO |
| SQLite / FTS | NO |
| Clipboard history APIs | NO |

**Three deployment shapes [CONFIRMED]:**
1. **Unpackaged** — plain `.exe`, xcopy/installer. Loses everything in the YES column.
2. **Packaged (MSIX)** — full identity, clean install/uninstall, virtualized registry/filesystem. **[COMMUNITY]** complaints: install location is a locked `WindowsApps` folder, harder to debug, users find MSIX install UX confusing, and code-signing is mandatory (a self-signed cert requires the user to manually trust it — a real adoption barrier for a side project).
3. **Packaged with external location (sparse package)** — **[CONFIRMED]** "the lowest-friction fix, letting you register a small identity package alongside your existing app — without changing your installer, giving your app package identity and access to features like notifications and background tasks."
   https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/grant-identity-to-nonpackaged-apps

**Recommendation for Noto: sparse package (option 3).** You get `AppNotificationManager` + `StartupTask` + protocol activation while keeping a normal `Program Files` install, a normal installer, and normal debugging. It still requires a signed `.msix` identity package, but the main app stays conventional.

**Also [CONFIRMED]:** Windows App SDK itself can be deployed **self-contained** (`<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>`) so you don't have to install the WinAppSDK Runtime on the user's machine. **Strongly recommended** — the framework-dependent path has been a top source of "app won't launch on a clean machine" reports.
https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/deployment-architecture

## H2. Startup on login

| | Packaged/sparse | Unpackaged |
|---|---|---|
| API | `Windows.ApplicationModel.StartupTask.GetAsync(id)` → `RequestEnableAsync()` | write `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` |
| Manifest | `<uap5:Extension Category="windows.startupTask" ...>` with `TaskId` | n/a |
| User can disable | **Yes — in Settings > Apps > Startup AND Task Manager** | Task Manager only |
| Can the app force-enable? | **No** — if the user disabled it, `RequestEnableAsync` returns `DisabledByUser` and you must not retry-spam | Yes (bad citizenship) |
| Gotcha | Returns `DisabledByPolicy` / `DisabledByUser`; handle all `StartupTaskState` values | Antivirus/cleaner tools sometimes strip Run keys |

**[CONFIRMED]** https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.startuptask

Prefer `StartupTask` (it shows up in the Windows Startup UI, which users expect and trust). Pass `--startup` / check `AppInstance.GetActivatedEventArgs()` so you can launch minimized to tray.

## H3. System tray icon — **[CONFIRMED gap: WinUI 3 has none]**

There is **no built-in `NotifyIcon`** in WinUI 3. Open since 2020: microsoft-ui-xaml issue #2020 "Proposal: System tray icon for WinUI 3 Desktop."
https://github.com/microsoft/microsoft-ui-xaml/issues/2020

Options:

| Option | Verdict |
|---|---|
| **`H.NotifyIcon.WinUI`** (NuGet, MIT) | **Recommended.** Most mature, actively maintained, XAML-declarative `<tb:TaskbarIcon>`, supports context flyouts, `EfficiencyMode`, and a `ShowWindow`/`HideWindow` helper. https://github.com/HavenDV/H.NotifyIcon |
| Raw `Shell_NotifyIcon` + `NOTIFYICONDATAW` | Full control, ~200 lines. Needed anyway if you want exotic behavior. https://albertakhmetov.com/posts/2025/using-notifyicon-in-winui-3/ |
| `System.Windows.Forms.NotifyIcon` via `<UseWindowsForms>` | Works, but drags WinForms into the process. Avoid. |
| `WinuiTrayIcon`, `SystemTrayWinUI3` | Less mature alternatives. |

**Gotchas for any approach:**
- You **must** handle **`TaskbarCreated`** (`RegisterWindowMessage("TaskbarCreated")`) and re-add the icon — Explorer restarts are common and your icon silently vanishes otherwise. H.NotifyIcon handles this; a hand-rolled one usually doesn't at first.
- Windows 11 hides new tray icons in the overflow by default. Users must drag it out. Set expectations in onboarding.
- A tray-only app must **not** exit when the last window closes. In WinUI 3, `Application.Current.Exit()` is explicit, but closing the last `Window` does terminate the app by default in some templates — override `Window.Closed`, hide instead of close.

## H4. App notifications

**[CONFIRMED]** `Microsoft.Windows.AppNotifications.AppNotificationManager` (Windows App SDK) — the modern replacement for `ToastNotificationManager`.
```csharp
AppNotificationManager.Default.Register();          // call at startup
var n = new AppNotificationBuilder().AddText("Saved").BuildNotification();
AppNotificationManager.Default.Show(n);
```
https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/notifications/app-notifications/

**Requires package identity** (MSIX or sparse). Unpackaged apps get nothing from this API. `Register()` also wires up COM activation for notification clicks, which is itself identity-dependent.

## H5. Auto-update

| Option | Verdict for Noto |
|---|---|
| **Microsoft Store** | Best update UX, free hosting, but Store cert review + the restricted-capability justifications + slow release cadence. |
| **MSIX + `.appinstaller`** | **[CONFIRMED]** MS-recommended for WinUI 3 ("ClickOnce is not supported for WinUI 3 apps — use MSIX with `.appinstaller` instead"). **Major caveat [CONFIRMED]:** the **`ms-appinstaller:` URI protocol has been disabled by default since December 2023** for security reasons — users must download and open the file manually, which kills the one-click web-install story. https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/publish-first-app |
| **Velopack** | **[COMMUNITY] Recommended for a non-Store side project.** Successor to Squirrel/Clowd.Squirrel. Delta updates, self-updating portable builds, one-command packaging, MIT. https://velopack.io/ · https://github.com/velopack/velopack |
| Squirrel.Windows | Effectively unmaintained; Velopack replaced it. |
| ClickOnce | **[CONFIRMED] not supported for WinUI 3.** |

**Recommendation:** **Velopack** for direct distribution (it composes fine with a sparse identity package), with a Store listing later if desired.

## H6. WinUI 3 / Windows App SDK maturity as of 2025-2026 — **is it a safe bet?**

### Positives
- **[CONFIRMED]** It is Microsoft's stated strategic direction for native Windows UI; `AppWindow`, title-bar customization, and windowing APIs have matured substantially and are now genuinely good.
- **[COMMUNITY]** Microsoft announced at Build 2026 a renewed push (WinUI 3 + Windows App SDK + agent integration) and a move to **open-source the Windows App SDK**, plus a faster minor-release cadence after 1.8.
  https://www.thurrott.com/dev/324106/microsoft-to-finally-improve-and-then-open-source-the-windows-app-sdk
- Self-contained deployment removed the worst class of "won't launch" bugs.

### Negatives — the honest list
- **[COMMUNITY]** "Microsoft has greatly neglected the Windows App SDK in the past year or so... ignoring developer questions about bug fixes and functional updates."
- **[COMMUNITY]** "The challenge for WinUI 3 is not merely technical maturity. It is **confidence**. Developers can work around missing controls, awkward packaging, documentation gaps, and migration pain if they believe the platform is going somewhere durable. What they cannot easily absorb is **strategic whiplash**." — the single best summary of the situation.
- Long-open, directly-relevant bugs: drag & drop from Explorer (2020→2025), input pass-through, swapchain/opacity blending, no tray icon, elevated-app crashes.
- **[COMMUNITY]** Missing/weak controls vs WPF: no `DataGrid` in-box (community toolkit only), weaker `RichEditBox`/rich-text story, no real printing story, weaker accessibility tooling.
- **[COMMUNITY]** Documented-but-broken gaps are common enough that you should budget time to prototype every Win32-interop feature before committing to it.

### Verdict for Noto specifically
**Qualified yes, with two caveats.**

WinUI 3 is a *good* fit for Noto's chrome (modern Fluent notes UI, Mica, animations, dark mode) and a *poor* fit for exactly the features Noto leans on hardest: **layered-window opacity, per-pixel click-through, and drag & drop.**

Where WinUI 3 is a poor fit — and the alternative:
```
WinUI 3 strengths  -> note editing UI, command palette, settings, Fluent look
WinUI 3 weaknesses -> opacity, click-through, drag&drop, tray, transparency

If B2/B3 (true translucency + per-pixel click-through) turn out to be
HARD REQUIREMENTS rather than nice-to-haves, WPF is the better choice:
   WPF: AllowsTransparency=true + WindowStyle=None gives real per-pixel
        alpha and hit-testing that WinUI 3 structurally cannot.
   Cost: dated visuals, no Mica/Acrylic without interop, no WinUI controls.
```
**Recommended de-risking order (build these three spikes first, in a throwaway project):**
1. Opacity + ghost mode on a WinUI 3 note window (Win10 22H2 + Win11 24H2).
2. Drag & drop a file from Explorer into a WinUI 3 window.
3. `SetWinEventHook`-driven follow of a Notepad window during a fast drag — measure CPU and visible lag.

If (1) and (2) fail, reconsider the UI framework *before* writing the app, not after.

---

## Appendix: consolidated P/Invoke surface

Recommend **`Microsoft.Windows.CsWin32`** source generator rather than hand-written `DllImport`. `NativeMethods.txt`:

```
GetForegroundWindow
GetWindowThreadProcessId
GetWindowTextW
GetWindowTextLengthW
GetClassNameW
EnumWindows
EnumChildWindows
IsWindowVisible
IsIconic
IsZoomed
GetWindowRect
GetWindowPlacement
SetWindowPos
ShowWindow
GetWindowLongPtrW
SetWindowLongPtrW
SetLayeredWindowAttributes
SetWindowDisplayAffinity
GetWindowDisplayAffinity
SetWinEventHook
UnhookWinEvent
RegisterHotKey
UnregisterHotKey
SetWindowSubclass
RemoveWindowSubclass
DefSubclassProc
OpenProcess
QueryFullProcessImageNameW
GetApplicationUserModelId
GetPackageFamilyName
DwmGetWindowAttribute
DwmSetWindowAttribute
DwmIsCompositionEnabled
MonitorFromWindow
GetMonitorInfoW
GetDpiForWindow
SHAppBarMessage
Shell_NotifyIconW
AddClipboardFormatListener
RemoveClipboardFormatListener
DragAcceptFiles
DragQueryFileW
RegisterWindowMessageW
```

Key constants:
```
WS_EX_LAYERED        0x00080000
WS_EX_TRANSPARENT    0x00000020
WS_EX_TOOLWINDOW     0x00000080
WS_EX_NOACTIVATE     0x08000000
HWND_TOPMOST         (-1)
SWP_NOSIZE 0x1  SWP_NOMOVE 0x2  SWP_NOACTIVATE 0x10  SWP_NOZORDER 0x4
LWA_COLORKEY 0x1  LWA_ALPHA 0x2
WDA_NONE 0x0  WDA_MONITOR 0x1  WDA_EXCLUDEFROMCAPTURE 0x11
EVENT_SYSTEM_FOREGROUND     0x0003
EVENT_SYSTEM_MOVESIZESTART  0x000A
EVENT_SYSTEM_MOVESIZEEND    0x000B
EVENT_SYSTEM_MINIMIZESTART  0x0016
EVENT_SYSTEM_MINIMIZEEND    0x0017
EVENT_OBJECT_DESTROY        0x8001
EVENT_OBJECT_LOCATIONCHANGE 0x800B
EVENT_OBJECT_NAMECHANGE     0x800C
OBJID_WINDOW 0   CHILDID_SELF 0
WINEVENT_OUTOFCONTEXT 0x0000  WINEVENT_SKIPOWNPROCESS 0x0002
WINEVENT_SKIPOWNTHREAD 0x0001 WINEVENT_INCONTEXT 0x0004
WM_HOTKEY 0x0312  WM_DPICHANGED 0x02E0  WM_DISPLAYCHANGE 0x007E
WM_CLIPBOARDUPDATE 0x031D  WM_DROPFILES 0x0233  WM_NCHITTEST 0x0084
HTTRANSPARENT (-1)  HTCAPTION 2
DWMWA_EXTENDED_FRAME_BOUNDS 9  DWMWA_CLOAKED 14  DWMWA_WINDOW_CORNER_PREFERENCE 33
MOD_ALT 0x1 MOD_CONTROL 0x2 MOD_SHIFT 0x4 MOD_WIN 0x8 MOD_NOREPEAT 0x4000
APPMODEL_ERROR_NO_APPLICATION 15703
```

---

## Sources

Official (Microsoft Learn):
- SetWinEventHook — https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwineventhook
- WinEvents overview — https://learn.microsoft.com/en-us/windows/win32/winauto/winevents-overview
- Event constants — https://learn.microsoft.com/en-us/windows/win32/winauto/event-constants
- QueryFullProcessImageName — https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-queryfullprocessimagenamea
- GetModuleFileNameEx — https://learn.microsoft.com/en-us/windows/win32/api/psapi/nf-psapi-getmodulefilenameexw
- GetApplicationUserModelId — https://learn.microsoft.com/en-us/windows/win32/api/appmodel/nf-appmodel-getapplicationusermodelid
- IVirtualDesktopManager — https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ivirtualdesktopmanager
- DwmGetWindowAttribute — https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/nf-dwmapi-dwmgetwindowattribute
- SetLayeredWindowAttributes — https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setlayeredwindowattributes
- SetWindowDisplayAffinity — https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity
- Manage app windows (AppWindow / OverlappedPresenter) — https://learn.microsoft.com/en-us/windows/apps/develop/ui/manage-app-windows
- SHAppBarMessage — https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shappbarmessage
- ABM_SETAUTOHIDEBAREX — https://learn.microsoft.com/en-us/windows/win32/shell/abm-setautohidebarex
- High DPI development — https://learn.microsoft.com/en-us/windows/win32/hidpi/high-dpi-desktop-application-development-on-windows
- RegisterHotKey — https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey
- SetWindowSubclass — https://learn.microsoft.com/en-us/windows/win32/api/commctrl/nf-commctrl-setwindowsubclass
- Screen capture — https://learn.microsoft.com/en-us/windows/apps/develop/media-authoring-processing/screen-capture
- GraphicsCaptureSession.IsBorderRequired — https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.graphicscapturesession.isborderrequired
- Clipboard — https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.datatransfer.clipboard
- Microsoft.Data.Sqlite encryption — https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/encryption
- Applying migrations — https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying
- Choose a packaging model — https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/choose-packaging-model
- Grant identity to non-packaged apps — https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/grant-identity-to-nonpackaged-apps
- WinAppSDK deployment architecture — https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/deployment-architecture
- App notifications — https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/notifications/app-notifications/
- StartupTask — https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.startuptask
- Publish your first Windows app — https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/publish-first-app

Community / GitHub / blogs:
- Raymond Chen, monitoring another window's size/position — https://devblogs.microsoft.com/oldnewthing/20210104-00/?p=104656
- PowerToys #1264 LOCATIONCHANGE perf — https://github.com/microsoft/PowerToys/issues/1264
- PowerToys FancyZones deep dive — https://github.com/microsoft/PowerToys/wiki/Fancy-Zones-deep-dive
- microsoft-ui-xaml #2020 tray icon proposal — https://github.com/microsoft/microsoft-ui-xaml/issues/2020
- microsoft-ui-xaml #2956 transparent XAML island — https://github.com/microsoft/microsoft-ui-xaml/issues/2956
- microsoft-ui-xaml #8191 SwapChainPanel opacity — https://github.com/microsoft/microsoft-ui-xaml/discussions/8191
- microsoft-ui-xaml #10746 input pass-through — https://github.com/microsoft/microsoft-ui-xaml/discussions/10746
- microsoft-ui-xaml #10119 / #2715 / #10713 drag&drop — https://github.com/microsoft/microsoft-ui-xaml/issues/10119
- WindowsAppSDK #4433 elevated drag&drop — https://github.com/microsoft/WindowsAppSDK/issues/4433
- MS Q&A: WinUI3 semi-transparent + click-through — https://learn.microsoft.com/en-us/answers/questions/1418063/winui3-semi-transparent-window-click-through-windo
- efcore #4823 SQLite FTS support — https://github.com/dotnet/efcore/issues/4823
- Brice Lam, SQLite FTS and EF Core — https://www.bricelam.net/2020/08/08/sqlite-fts-and-efcore.html
- Brice Lam, More SQLite encryption — https://www.bricelam.net/2023/11/10/more-sqlite-encryption.html
- Zetetic SQLCipher for .NET — https://www.zetetic.net/sqlcipher/sqlcipher-for-dotnet/
- EFCoreSQLiteFTS sample — https://github.com/VahidN/EFCoreSQLiteFTS
- H.NotifyIcon — https://github.com/HavenDV/H.NotifyIcon
- NotifyIcon in WinUI 3 without libraries — https://albertakhmetov.com/posts/2025/using-notifyicon-in-winui-3/
- Velopack — https://velopack.io/ · https://github.com/velopack/velopack
- MScholtes/VirtualDesktop — https://github.com/MScholtes/VirtualDesktop
- Ciantic/VirtualDesktopAccessor — https://github.com/Ciantic/VirtualDesktopAccessor
- IOActive, Signal contentProtection bypass — https://www.ioactive.com/signal-windows-desktop-contentprotection-bypass/
- windhawk-mods #4449 appbar multi-DPI — https://github.com/ramensoftware/windhawk-mods/issues/4449
- Global hotkey in WinUI 3 — https://whid.eu/2022/05/13/chapter-5-add-global-hot-key-in-winui-3/
- Thurrott, MS to improve + open source WinApp SDK — https://www.thurrott.com/dev/324106/microsoft-to-finally-improve-and-then-open-source-the-windows-app-sdk
