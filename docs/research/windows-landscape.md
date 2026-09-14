# Windows Technical Landscape

**Research date:** 2026-09-14
**Raw source:** [`_raw/windows-apis.md`](_raw/windows-apis.md) (~1,000 lines, fully cited)
**Decisions informed:** ADR-001, ADR-003, ADR-007, ADR-008

---

## Purpose

Noto's hard problems are operating-system problems. This document records what
Windows actually supports, what it does not, and where the obvious
implementation is a trap.

It exists so that a future contributor reaching for the standard approach finds
out *here* why it was rejected, rather than discovering it in production.

Labels: **CONFIRMED** = official Microsoft documentation. **COMMUNITY** =
blog, Stack Overflow, or GitHub issue. **UNKNOWN** = unverified.

---

## Risk register

| # | Risk | Severity | Resolution |
| - | ---- | -------- | ---------- |
| R1 | Per-window opacity conflicts with WinUI 3 composition | HIGH | **Mitigated** — use XAML `Opacity` (ADR-007) |
| R2 | Per-pixel click-through impossible in WinUI 3 | HIGH | **Not a requirement** — whole-window works (ADR-007) |
| R3 | Window following will visibly lag | HIGH | **Managed** — scoped hooks, coalescing, hide during drag |
| R4 | No stable window identity exists | HIGH | **Designed around** — composite fingerprint (ADR-006) |
| R5 | Virtual desktop APIs undocumented, IIDs change per build | HIGH | **Avoided** — derive from `DWMWA_CLOAKED` (ADR-007) |
| R6 | Explorer drag & drop has open WinUI 3 bugs | MED-HIGH | **Gated** — validation spike in M0 (ADR-001) |
| R7 | Package identity required for notifications/startup | MED | **Resolved** — sparse package (ADR-008) |
| R8 | No built-in tray icon in WinUI 3 | MED | `H.NotifyIcon.WinUI` or raw `Shell_NotifyIcon` |
| R9 | WinUI 3 maturity; slow bug turnaround | MED | **Gated** — ADR-001 is provisional, WPF is the fallback |
| R10 | AppBar + per-monitor DPI is a minefield | MED | **Avoided** — snapped topmost window (ADR-007) |

**The two that shaped the architecture most:** R4, because it has no clean
solution and forced `detached` to become a first-class state; and R1/R2
together, because they nearly forced a framework change before the workarounds
were found.

---

## A. Context detection and window tracking

### Foreground detection — solved

```
  GetForegroundWindow()
        -> GetWindowThreadProcessId()
        -> OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)
        -> QueryFullProcessImageName()
```

CONFIRMED. Reliable and cheap.

### The packaged-app trap

For UWP and packaged applications this chain returns
**`ApplicationFrameHost.exe` for every one of them** — making every Store app
indistinguishable from every other.

The real identity requires resolving the `AppUserModelID`, typically by
enumerating child windows to find the actual hosted process.

> Without this, application-level binding is useless for a large class of
> applications. It is not optional (ADR-006).

### Reacting to changes — events, never polling

| Event | Use | Cost |
| ----- | --- | ---- |
| `EVENT_SYSTEM_FOREGROUND` | app switch | negligible |
| `EVENT_OBJECT_LOCATIONCHANGE` | window move/resize | **very high if global** |
| `EVENT_SYSTEM_MINIMIZESTART/END` | minimise | negligible |
| `EVENT_SYSTEM_MOVESIZESTART/END` | drag brackets | negligible |

**`SetWinEventHook` must always be `WINEVENT_OUTOFCONTEXT`.** In-context
injects Noto's DLL into other processes — unacceptable for stability, and it
looks exactly like malware to antivirus software.

### R3 — following a window will lag, structurally

`EVENT_OBJECT_LOCATIONCHANGE` is the only viable mechanism (it is Raymond
Chen's prescribed answer), but out-of-context delivery is asynchronous and
cross-process queued. **Noto is structurally frames behind.** No implementation
avoids this.

For scale: PowerToys measured 3–5% CPU from mouse movement alone on a
comparable hook.

Mitigations, in order of importance:

1. **Scope the hook to the target's `(pid, tid)`.** Never `(0, 0)`. This is the
   single most important performance decision in the entire application.
2. **Bracket with `MOVESIZESTART` / `MOVESIZEEND`** — hide or ghost the note
   during an active drag rather than chasing it frame by frame.
3. **Coalesce to roughly 16 ms.**

### R4 — there is no durable window identity

`HWND` is recycled and not persistable. The OS provides nothing stable across
sessions. This is addressed in full by ADR-006.

---

## B. Presentation

Decided in ADR-007. Summary of what the platform supports:

| Capability | Verdict |
| ---------- | ------- |
| Always-on-top | ✅ `OverlappedPresenter.IsAlwaysOnTop` |
| Borderless custom chrome | ✅ `SetBorderAndTitleBar` |
| Window opacity | ⚠️ XAML `Opacity` only; **not** `SetLayeredWindowAttributes` |
| Whole-window click-through | ✅ `WS_EX_LAYERED \| WS_EX_TRANSPARENT` |
| Per-pixel click-through | ❌ Structurally impossible in WinUI 3 |
| Edge docking | ⚠️ Snapped topmost window; **not** `SHAppBarMessage` |
| Capture exclusion | ✅ `SetWindowDisplayAffinity(0x11)` |

### Why not `SetLayeredWindowAttributes` (R1)

It fights WinUI 3's DirectComposition renderer. With Mica or Acrylic it
produces black or fully-transparent regions; `LWA_COLORKEY` is unreliable.

> Historically it was used as a *hack to work around* WinUI 3's forced
> background — which is how load-bearing and fragile the interaction is.

### Why per-pixel click-through cannot work (R2)

WinUI 3 "uses composition to draw its contents with hardware-accelerated
Direct3D, meaning the contents are never really seen by the window itself, so
the window will never know how to pass input through."

Whole-window ghost mode does work, and is what Noto needs. For region
hit-testing, subclass `WM_NCHITTEST` and return `HTTRANSPARENT` — **never
`SetWindowRgn`**, which clips rendering too.

### Why not `SHAppBarMessage` (R10)

If Noto crashes without `ABM_REMOVE`, the reserved desktop work area
**persists until logoff**. The user gets a permanently shrunken desktop with no
visible cause. Plus known per-monitor DPI defects.

Accepted trade-off: maximised windows overlap the sidebar rather than being
pushed aside. Cosmetic, versus a corrupted desktop.

### `DWMWA_EXTENDED_FRAME_BOUNDS`, always

`GetWindowRect` includes roughly 7–8 px of invisible resize border. Every
alignment calculation must use `DWMWA_EXTENDED_FRAME_BOUNDS` instead, or every
docked note is visibly misaligned.

### Capture exclusion

`SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE)` where the constant is
**`0x11`** — frequently mis-transcribed as `0x2`. Windows 10 2004+; degrades to
a black rectangle on older builds; works with WinUI 3.

**Never describe this as security.** Microsoft explicitly disclaims it, and
IOActive published a bypass. It is "hide from screen sharing."

---

## C. Input and capture

### Global hotkeys

**Use `RegisterHotKey` plus `SetWindowSubclass` to receive `WM_HOTKEY`.**

Avoid `WH_KEYBOARD_LL`: it flags antivirus, sits in the system input path
adding latency, and Windows **silently removes hooks that exceed a 300 ms
timeout** — producing a feature that stops working with no error.

`RegisterHotKey`'s limitations (no suppression, conflicts with other
applications) are acceptable; conflicts are reported in settings rather than
fought.

### Screen capture

`Windows.Graphics.Capture` works **unpackaged**. Use
`IGraphicsCaptureItemInterop::CreateForWindow` — no picker is needed since Noto
already knows the `HWND`.

The yellow capture border is accepted. Removing it requires a restricted
capability and full MSIX, which ADR-008 rejects.

Legacy `BitBlt` / `PrintWindow` are unreliable against DWM-composited and
hardware-accelerated windows.

### Drag and drop (R6)

Open WinUI 3 bugs spanning 2020–2025. Validated by an M0 spike (ADR-001).

**Noto must never run elevated** — drag and drop is broken in elevated
processes, and no feature needs elevation.

---

## D. Storage

Decided in ADR-003. The finding that drove it:

> **EF Core cannot model FTS5.** `MATCH` targets the table not a column, and
> `rank`, `snippet()` and `highlight()` are raw-SQL only. The EF issue has been
> open for years.

Plus 100–400 ms of model building at startup, against a sub-one-second budget,
and a SQLite migration strategy that emulates column changes with full table
rebuilds on the user's only copy of their data.

FTS5 is compiled into the `e_sqlite3` build that `Microsoft.Data.Sqlite` ships.
The external-content pattern is documented by the EF Core team's own Brice Lam.

> **`MATCH` takes a query language, not a literal.** Unsanitised input crashes
> on an apostrophe.

---

## E. Packaging

Decided in ADR-008.

| Feature | Needs package identity |
| ------- | ---------------------- |
| `AppNotificationManager` | Yes |
| `StartupTask` | Yes |
| Protocol activation | Yes |
| `Windows.Graphics.Capture` | No |

**`ms-appinstaller:` has been disabled by default since December 2023** (malware
abuse), which removes MSIX's one-click web-install story.

Resolution: sparse package for identity, conventional install location,
`WindowsAppSDKSelfContained=true`, Velopack for updates.

---

## F. Two gotchas that will otherwise cost days

Both produce failures whose symptoms are far from their causes.

### 1. Root every delegate passed to Win32

A collected `WinEventDelegate` or `SUBCLASSPROC` causes an **access violation
on the next callback**. Store them in fields; Microsoft's documentation
recommends `GCHandle`.

The crash appears unrelated to the code that caused it, and is
non-deterministic because it depends on GC timing.

### 2. `DWMWA_EXTENDED_FRAME_BOUNDS`, not `GetWindowRect`

Repeated from section B because it will be gotten wrong at least once. The
7–8 px invisible border makes every alignment subtly wrong in a way that looks
like a layout bug rather than an API misuse.

---

## G. Is WinUI 3 the right choice?

The honest summary from research:

> WinUI 3 is good for Noto's chrome — Fluent UI, Mica, dark mode — and weak at
> precisely the three things Noto leans on hardest: translucency,
> click-through, and drag & drop.

**WPF is better** for per-pixel transparency and hit-testing, via
`AllowsTransparency=true` with `WindowStyle=None`. The cost is dated visuals
and no Mica.

ADR-001 takes WinUI 3 **provisionally**, because the blocking-severity problem
(per-pixel click-through) is not a Noto requirement and the highest remaining
risk (opacity) has a clean workaround — but requires three validation spikes in
M0, with WPF as the designated fallback:

1. opacity and whole-window click-through on Win10 22H2 and Win11 24H2
2. Explorer file drop
3. `EVENT_OBJECT_LOCATIONCHANGE` following during a fast drag, with CPU measured

**If spikes 1 or 2 fail, the framework changes before application code is
written.**

---

## H. The test matrix this implies

Non-negotiable, because this is where comparable software breaks:

- Windows 10 22H2 **and** Windows 11 24H2
- single monitor, and multi-monitor with **mixed DPI**
- monitor disconnected while a note is positioned on it
- display scaling changed while running
- maximise, Aero Snap, virtual desktop switch
- target window occluded

Mixed-DPI multi-monitor is the dominant recurring defect theme in WindowTop's
126-issue tracker — the closest available proxy for what Noto is attempting.
