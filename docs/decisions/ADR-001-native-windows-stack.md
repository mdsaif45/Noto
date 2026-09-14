# ADR-001 — Native Windows stack: C# + WinUI 3 + Win32 interop

**Status:** Accepted — **provisional, subject to a validation gate**
**Date:** 2026-09-14
**Supersedes:** —

---

## Context

Noto is a Windows-only desktop utility. Its hard problems are not ordinary UI
controls; they are operating-system integration problems:

```
  global hotkeys          window tracking        always-on-top
  edge docking            foreground detection   per-monitor DPI
  virtual desktops        screen capture         capture exclusion
  clipboard               system tray            multi-monitor
```

Noto is also a background utility competing for a user's RAM and startup time
against their actual work, with a hard budget of under one second to usable and
"small enough to forget about" at idle (principle 3).

There is **no cross-platform requirement**. Noto is Windows-only by design, so
the usual argument for a portable framework — amortising effort across
platforms — does not apply. We would pay the abstraction cost and receive
nothing for it.

---

## Problem

Which UI framework and runtime should Noto be built on?

The tension is specific and was uncovered during technical research:

> WinUI 3 is the modern, well-supported choice for Windows UI, and it is good
> at exactly the parts of Noto that are easy. It is weak at three of the things
> Noto leans on hardest.

---

## Options considered

### Option A — C# + .NET + WinUI 3 + Windows App SDK + Win32 interop

Modern Microsoft-supported stack. Fluent design, Mica, native dark mode,
`AppWindow`/`OverlappedPresenter` for window management, full P/Invoke access.

### Option B — C# + .NET + WPF + Win32 interop

Mature, stable, enormous ecosystem. Critically, **real per-pixel transparency**
(`AllowsTransparency=true` with `WindowStyle=None`) and hit-testing that WinUI 3
structurally cannot provide. Dated visuals; no Mica; no modern Fluent controls
without third-party libraries.

### Option C — C++ / Win32 or WinUI 3 in C++

Smallest footprint, fastest startup, no runtime dependency. Dramatically slower
to develop, and hostile to AI-assisted development, which is an explicit
constraint on this project.

### Option D — Electron / Tauri / Flutter / Wails

Rejected early. Each interposes an abstraction layer between Noto and precisely
the Win32 APIs that constitute its entire value. Electron additionally violates
the startup and memory budgets outright. Tauri is lighter but still requires
dropping to Win32 for every feature that matters, with no portability benefit
to show for it.

> Circumstantially, @/Anchored's installer naming suggests a Tauri build
> (INFERRED, low confidence). Its ~50 MB claimed footprint is not competitive
> with Zhorn Stickies at 2.8 MB.

---

## Decision

**Build on C# + .NET + WinUI 3 + Windows App SDK, with Win32 interop isolated
in a dedicated platform layer.**

**This decision is provisional.** It is subject to a validation gate that must
pass before the Floating Notes milestone (M4) begins.

### The validation gate

Three throwaway spikes, each an independent issue in M0:

| # | Spike | Pass condition |
| - | ----- | -------------- |
| 1 | Window opacity and whole-window click-through | Works on Windows 10 22H2 **and** Windows 11 24H2 |
| 2 | File drag and drop from Explorer into a WinUI 3 window | Works reliably, unelevated |
| 3 | Follow another window via `EVENT_OBJECT_LOCATIONCHANGE` during a fast drag | No visible drift; CPU cost measured and acceptable |

**If spikes 1 or 2 fail, this ADR is superseded in favour of Option B (WPF),
and that decision is made before application code is written — not after.**

The spikes are deliberately scheduled in M0, when switching frameworks costs
days rather than months.

---

## Rationale

### Why WinUI 3 despite the known weaknesses

Technical research identified three genuine problems (full detail in
[`docs/research/windows-landscape.md`](../research/windows-landscape.md)):

| Risk | Finding | Mitigation |
| ---- | ------- | ---------- |
| **Per-window opacity** | `WS_EX_LAYERED` + `SetLayeredWindowAttributes` fights WinUI 3's DirectComposition renderer. Combined with Mica/Acrylic it produces black or fully transparent regions. | **Set `Opacity` on the root XAML element instead.** Same visual result, zero platform risk. The Win32 route is reserved for an experimental ghost mode behind a flag. |
| **Per-pixel click-through** | Structurally impossible. WinUI 3 draws through composition, so "the contents are never really seen by the window itself, so the window will never know how to pass input through." | **Noto only needs whole-window click-through**, which works via `WS_EX_LAYERED \| WS_EX_TRANSPARENT`. Per-pixel is not a requirement. |
| **Explorer drag and drop** | Open WinUI 3 bugs spanning 2020–2025. | Spike 2 validates it early. Capture has fallbacks (clipboard, hotkey) if drop is unreliable. |

The decisive observation: **the blocking-severity problem (per-pixel
click-through) is not a Noto requirement, and the highest-rated remaining risk
(opacity) has a clean, fully-supported workaround.** WPF's advantage is real but
applies to a capability Noto does not need.

### What WinUI 3 gives in exchange

- `OverlappedPresenter.IsAlwaysOnTop` and `SetBorderAndTitleBar` — always-on-top
  and borderless custom chrome are fully supported, no interop required
- Mica, native dark mode, system theme and accent following — principle 8
  ("would a Windows user notice this is not a real Windows app?")
- Current Fluent design without third-party control libraries
- The supported forward direction, rather than a framework in maintenance

### Why not C++

Startup and footprint would be best-in-class, but development speed matters
more for a project of this size, and the project explicitly relies on
AI-assisted development where C# has a substantial advantage.

### Why the Win32 isolation layer matters

All P/Invoke, `SetWinEventHook` plumbing, window subclassing and DPI arithmetic
live in a single platform layer, never scattered through UI code.

This is not abstraction for its own sake (which principle 9 forbids). It exists
for two concrete reasons: it is what makes the framework decision **reversible**
if the gate fails, and interop is where the genuinely dangerous bugs live.

---

## Consequences

### Positive

- Modern, native-feeling Windows application
- Always-on-top and custom chrome need no risky interop
- Full Win32 access when required
- Fast iteration with AI assistance
- The framework choice stays reversible until the M4 gate

### Negative

- Depends on the Windows App SDK, whose bug turnaround is slow
- No built-in tray icon — requires `H.NotifyIcon.WinUI` or raw `Shell_NotifyIcon`
- Opacity must go through XAML rather than the Win32 route
- Per-pixel click-through is permanently off the table
- Some deployment paths require package identity (see ADR-008)

### Interop rules, binding on all contributors

Two failure modes found in research that will otherwise appear as
non-deterministic crashes:

1. **Root every `WinEventDelegate` and `SUBCLASSPROC` in a field.** A collected
   delegate causes an access violation on the next callback. Microsoft's own
   documentation recommends `GCHandle`.
2. **Use `DWMWA_EXTENDED_FRAME_BOUNDS`, never `GetWindowRect`,** for all
   alignment arithmetic. `GetWindowRect` includes roughly 7–8 px of invisible
   resize border, which will silently misalign every docked note.

Additionally: **Noto must never run elevated.** Drag and drop is broken in
elevated WinUI 3 processes, and no Noto feature requires elevation.

---

## Alternatives rejected

| Option | Why rejected |
| ------ | ------------ |
| **WPF** | Genuinely better for per-pixel transparency and hit-testing, but Noto does not need those. Cost is dated visuals, no Mica, and building Fluent from parts. **Held as the designated fallback if the gate fails.** |
| **C++ / Win32** | Best footprint, unacceptable development velocity for this team size and workflow. |
| **Electron** | Violates startup and memory budgets. Would need Win32 interop anyway. |
| **Tauri** | Lighter than Electron, but every feature that matters drops to Win32, with no portability payoff for a Windows-only product. |
| **Flutter / Wails** | Same argument, weaker Windows integration story. |

---

## Future reconsideration criteria

Revisit this ADR if:

- **Any validation gate spike fails** → switch to WPF before M4
- Per-pixel click-through or true per-pixel transparency becomes a genuine user
  requirement → WPF becomes correct
- Windows App SDK stops receiving meaningful investment, or a blocking bug goes
  unfixed for more than two release cycles
- Measured startup time cannot be brought under the one-second budget and the
  runtime is demonstrably the cause
- Microsoft ships a successor framework with a credible migration path

Do **not** revisit because a newer framework is fashionable, or because an
assistant suggests a rewrite would be cleaner.
