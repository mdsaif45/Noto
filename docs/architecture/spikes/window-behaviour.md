# Spike — Window Behaviour

**Issue:** [#2](https://github.com/mdsaif45/Noto/issues/2)
**Date:** 2026-09-14
**Status:** Complete
**Verdict:** **WinUI 3 + Windows App SDK = GO**
**Code:** [`spikes/WindowBehaviour/`](../../../spikes/WindowBehaviour/) · raw output: [`spike-results.sample.txt`](../../../spikes/WindowBehaviour/spike-results.sample.txt)

---

## Objective

ADR-001 accepted WinUI 3 **provisionally**. Research warned it was weak at three
things Noto leans on hardest — per-window translucency, click-through, and
Explorer drag & drop — but those conclusions came from documentation and
community reports, not from running code.

> **Can WinUI 3 + Windows App SDK + reasonable Win32 interop deliver Noto's
> window model?**

If not, the framework had to change **before** application code existed.

---

## Environment

| | |
| --- | --- |
| OS | Windows 11, build **10.0.26200** |
| .NET SDK | 10.0.301 (spike targets `net8.0-windows10.0.19041.0`) |
| Runtime | .NET 8.0.31, 64-bit |
| Windows App SDK | 1.6.x |
| Deployment | **Unpackaged**, `WindowsAppSDKSelfContained=true` |
| DPI awareness | `PerMonitorV2` via application manifest |
| Display | 1 monitor, 1920×1080, 96 DPI (100%), taskbar bottom |

> **Single-monitor, single-DPI machine.** Mixed-DPI and multi-monitor behaviour
> were **not** empirically exercised. This is the spike's main limitation and is
> recorded as such rather than glossed. The *enumeration and arithmetic* are
> implemented and correct on this machine; the behaviour under two differently
> scaled monitors is unverified.

---

## Results

| # | Capability | Result | Evidence |
| - | ---------- | ------ | -------- |
| 1 | Borderless / custom chrome | **PASS** | `SetBorderAndTitleBar(false,false)` → `HasBorder=False, HasTitleBar=False` |
| 1 | Always-on-top | **PASS** | `OverlappedPresenter.IsAlwaysOnTop = true` → `True`, no interop |
| 1 | Resize / minimize / maximize | **PASS** | `True/True/True` |
| 2 | Monitor + work-area enumeration | **PASS** | `(0,0)-(1920,1080)`, work area `(0,0)-(1920,1032)`, taskbar inferred `bottom` |
| 3 | DPI query | **PASS** | `GetDpiForWindow` → 96; DIP→px factor computed |
| 4 | Frame bounds vs `GetWindowRect` | **CONFIRMED** | **7px difference measured on both sides** |
| 5 | Edge docking | **PASS** *(after fix)* | requested `(1600,0)-(1920,1032)` → achieved **exactly** |
| 6 | Opacity | **PASS** | XAML root `Opacity=0.85` applied cleanly |
| 7 | Whole-window click-through | **PASS** | `WS_EX_LAYERED\|WS_EX_TRANSPARENT` set and restored |
| 8 | Screen-capture exclusion | **PASS** | `SetWindowDisplayAffinity(0x11)` → read back `0x11` |
| 9 | Virtual-desktop visibility | **PASS** | `DWMWA_CLOAKED` readable; no undocumented APIs |
| 10 | Global hotkey | **PASS** | registered, conflicts return clean `false` |
| 11 | Show / hide lifecycle | **PASS** | `AppWindow.Hide()/Show()` toggles `IsVisible` |
| — | Build unpackaged self-contained | **PASS** | 0 warnings, 0 errors |
| — | Runs and produces output | **PASS** | exit code 0 |

Not exercised: **mixed-DPI**, **multi-monitor**, **Explorer drag & drop**.

---

## The two findings that matter

### 1. `MoveAndResize` uses the OUTER rect — a 7px docking bug

The most valuable result. Positioning a panel naively at the work-area edge
leaves it **7px away from the screen edge**:

```
  desired (visible)   (1600,0)-(1920,1032)  320x1032
  naive MoveAndResize (1607,0)-(1913,1025)  306x1025
  gap at right edge   7px          <- visible, wrong, easy to miss
```

`AppWindow.MoveAndResize` takes **outer window coordinates**, which include the
invisible resize border. The *visible* frame is inset by it.

Compensating by the measured per-edge inset fixes it exactly:

```
  measured inset L/T/R/B   7/0/7/7
  compensated              (1600,0)-(1920,1032)  320x1032   <- exact match
```

> **Production rule.** Edge docking must compute
> `inset = GetWindowRect − DWMWA_EXTENDED_FRAME_BOUNDS` **per edge**, and
> compensate. The inset is not uniform — top was 0 while left/right/bottom were
> 7 — and it is in physical pixels, so it will differ per DPI. It must be
> re-measured after any DPI change, not cached globally.

This is exactly the class of defect that is cheap now and looks like an
unexplained layout bug in six months.

### 2. `RegisterHotKey` conflicts are clean, and AltGr is real

```
  [OK       ] Ctrl+Shift+N       recommended shape
  [FAIL 1409] Win+Shift+Space    already taken by something on this machine
  [OK       ] Ctrl+Alt+Q         REGISTERS FINE — and that is the danger
  [FAIL 1409] Ctrl+Shift+N       duplicate, correctly rejected
  [FAIL 1409] Win+L              OS-reserved, correctly rejected
  [OK       ] F13 alone          no modifier, accepted
```

Two conclusions:

- **Conflicts surface as a clean `false` with `ERROR_HOTKEY_ALREADY_REGISTERED`
  (1409)** — not an exception, not a crash. Noto can report them in settings
  and keep running. `Win+Shift+Space` failing on an ordinary machine shows this
  is a routine condition, not an edge case.
- **`Ctrl+Alt+Q` registered successfully.** Windows offers no protection here.
  On German, French and Polish layouts AltGr is delivered as Ctrl+Alt, so this
  hotkey would silently intercept characters the user is typing. **The platform
  will not stop us making this mistake — only a rule will.**

---

## Decisions for production

| # | Decision | Basis |
| - | -------- | ----- |
| D1 | **WinUI 3 + Windows App SDK, unpackaged self-contained** | Builds and runs clean; every required capability verified |
| D2 | **Edge docking compensates for the frame inset, per edge, re-measured on DPI change** | The 7px measurement above |
| D3 | **All alignment maths uses `DWMWA_EXTENDED_FRAME_BOUNDS`** | 7px error confirmed empirically |
| D4 | **Panel width is authored in DIPs and scaled per-monitor** | Otherwise the panel changes apparent size across displays |
| D5 | **Never `SHAppBarMessage`** | ADR-007; `MoveAndResize` against `rcWork` is sufficient and has no crash-corrupts-desktop failure mode |
| D6 | **Opacity via XAML root, never `SetLayeredWindowAttributes`** | Works cleanly; ADR-007 confirmed |
| D7 | **Ghost mode needs a non-click exit** — global hotkey **and** tray item | A click-through window cannot be clicked to undo itself |
| D8 | **No default hotkey uses `Ctrl+Alt+<letter>`; all hotkeys rebindable** | AltGr, measured above |
| D9 | **Hotkey conflicts are reported in settings, never fatal** | Error 1409 is a clean boolean |
| D10 | **`DWMWA_CLOAKED` for virtual-desktop visibility** | Documented and version-stable, unlike `IVirtualDesktopManager` |
| D11 | **`RegisterHotKey` + subclass, never `WH_KEYBOARD_LL`** | Confirmed sufficient |
| D12 | **`[assembly: DefaultDllImportSearchPaths(System32)]`** | CA5392; prevents DLL planting |
| D13 | **`LibraryImport` where possible, `DllImport` where required** | See below |

### Interop detail worth carrying forward

`LibraryImport` (source-generated) is not a blanket replacement for `DllImport`:

- it **requires `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`** — the generator
  emits unsafe code (`SYSLIB1062`)
- it **cannot marshal `MONITORINFOEXW`** because of the fixed-size inline string
  (`SYSLIB1051`); that one needs `DllImport`
- a delegate passed to `EnumDisplayMonitors` must be **rooted for the duration
  of the call** (`GC.KeepAlive`) — a collected delegate is an access violation
  whose symptom appears nowhere near its cause
- a `ref` parameter forces **all** lambda parameters to be explicitly typed

---

## Limitations

**Stated plainly, because the spike's value depends on being honest about what
it did not prove.**

| Not verified | Why | Mitigation |
| ------------ | --- | ---------- |
| **Mixed-DPI multi-monitor** | Test machine has one 96-DPI display | The highest residual risk. The arithmetic is implemented and DPI-aware; behaviour is unverified. **Must be tested before M2 completes.** |
| **Multi-monitor movement** | Same | Same |
| **DPI 125/150/200%** | Same | Manifest declares `PerMonitorV2`; scaling maths is written but only exercised at 100% |
| **Explorer drag & drop** | Needs interactive input | Scheduled as a follow-up before M3 capture work |
| **Edge-hover activation** | Needs interactive input | UX behaviour, not a framework question |
| **Windows 10 22H2** | Not available here | All APIs used are Win10 1809+; capture exclusion needs 2004+. **Must be verified before v0.9.** |
| **Focus behaviour under real use** | `foreground is spike: False` in headless mode — expected, since nothing was activated | Revisit during M2 |

None of these threatens the framework decision: each is about *behaviour under
conditions*, not about whether the API exists. The capabilities that could have
disqualified WinUI 3 were all verified.

---

## Rejected approaches

| Rejected | Why |
| -------- | --- |
| `SHAppBarMessage` for edge docking | A crash without `ABM_REMOVE` leaves the desktop work area shrunken **until logoff**, with no visible cause. `MoveAndResize` against `rcWork` achieves the same visual result with no such failure. Trade-off accepted: maximised windows overlap the panel rather than being pushed aside. |
| `SetLayeredWindowAttributes` for opacity | Fights DirectComposition. XAML `Opacity` verified working. |
| `WH_KEYBOARD_LL` for hotkeys | `RegisterHotKey` verified sufficient; the hook sits in the input path, is silently removed on a 300ms timeout, and is an antivirus heuristic trigger. |
| `IVirtualDesktopManager` | Undocumented beyond three methods; IIDs change between Windows builds. `DWMWA_CLOAKED` answers the actual question. |
| Per-pixel click-through | Structurally impossible in WinUI 3 — and not a Noto requirement. Whole-window ghost mode is what is needed, and it works. |

---

## Architecture implications

The spike suggests a **small** platform abstraction, sized to what was actually
needed rather than to theoretical purity:

```
  Noto.Windows
  ├── IWindowHost          create / show / hide / close, HWND access
  ├── IDisplayService      monitors, work areas, DPI, edge-dock arithmetic
  ├── IHotkeyService       register / unregister, conflict reporting
  └── WindowPlacement      a value type: monitor + rect + DPI
```

Four types, each mapping to a probe that proved necessary. Notably **not**
proposed: `IWindowPresenter` and `WindowPresentationMode`. Presentation is
already ADR-009's `NotePresentations`; adding a parallel platform-level
presenter abstraction before a second presentation kind exists would be
abstraction without a second implementation, which principle 9 forbids.

**No change to ADR-009 is required.** Nothing in the spike touched the domain,
and nothing needs to. `WindowPlacement` is a platform value type living in
`Noto.Windows` — it never enters `Noto.Core`.

---

## Follow-up

| Action | Where |
| ------ | ----- |
| Update ADR-001 — remove provisional status | ADR-001 |
| Record D2 (frame-inset compensation) in ADR-007 | ADR-007 |
| Verify mixed-DPI multi-monitor | before M2 completes |
| Verify on Windows 10 22H2 | before v0.9 |
| Verify Explorer drag & drop | before M3 capture work |
| Delete this spike once M0 lands `Noto.Windows` | M0 |
