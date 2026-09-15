# ADR-001 — Native Windows stack: C# + WinUI 3 + Win32 interop

**Status:** **Accepted** — validation gate passed 2026-09-14
**Date:** 2026-09-14
**Validated by:** [Window behaviour spike](../architecture/spikes/window-behaviour.md) (issue #2)
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

**The validation gate has passed.** This decision is no longer provisional.

### Gate result

A throwaway spike ([`spikes/WindowBehaviour/`](../../spikes/WindowBehaviour/))
built and ran unpackaged and self-contained on Windows 11 26200, and verified
every capability that could have disqualified WinUI 3:

| Capability | Result |
| ---------- | ------ |
| Borderless / custom chrome | PASS — `SetBorderAndTitleBar` |
| Always-on-top | PASS — `OverlappedPresenter.IsAlwaysOnTop`, no interop |
| Edge docking to the work area | PASS — exact, after frame-inset compensation |
| Opacity | PASS — XAML root, no layered windows |
| Whole-window click-through | PASS — layered + transparent ex-styles |
| Screen-capture exclusion | PASS — `SetWindowDisplayAffinity(0x11)` |
| Global hotkey | PASS — conflicts return a clean `false` |
| Show / hide lifecycle | PASS — `AppWindow.Hide()/Show()` |
| Virtual-desktop visibility | PASS — `DWMWA_CLOAKED` |
| Build unpackaged self-contained | PASS — 0 warnings, 0 errors |

**WPF is no longer the designated fallback.** The two WinUI 3 weaknesses that
motivated the provisional status turned out not to bite: per-pixel
click-through is impossible but is not a Noto requirement, and opacity has a
clean supported route that was verified working.

### What the gate did not cover

Recorded honestly, because these remain open:

- **mixed-DPI multi-monitor** — the test machine had one 96-DPI display. The
  arithmetic is written and DPI-aware but unverified. Must be tested before M2
  completes.
- **Explorer drag & drop** — needs interactive input; scheduled before M3
  capture work.
- **Windows 10 22H2** — not available on the test machine; must be verified
  before v0.9.

None of these is a framework-viability question. Each is behaviour under
conditions, and every API involved is documented as supported on Windows 10
1809+ (capture exclusion: 2004+).

## Rationale

### Why WinUI 3 despite the known weaknesses

Technical research identified three genuine problems (full detail in
[`docs/research/windows-landscape.md`](../research/windows-landscape.md)):

| Risk | Finding | Mitigation |
| ---- | ------- | ---------- |
| **Per-window opacity** | `WS_EX_LAYERED` + `SetLayeredWindowAttributes` fights WinUI 3's DirectComposition renderer. Combined with Mica/Acrylic it produces black or fully transparent regions. | **Set `Opacity` on the root XAML element instead.** Same visual result, zero platform risk. `SetLayeredWindowAttributes` is not used for opacity at all (ADR-007); `WS_EX_LAYERED` appears only as the prerequisite for whole-window click-through. |
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
- The Win32 isolation layer keeps the choice reversible even though the gate has passed

### Negative

- Depends on the Windows App SDK, whose bug turnaround is slow
- No built-in tray icon — requires `H.NotifyIcon.WinUI` or raw `Shell_NotifyIcon`
- Opacity must go through XAML rather than the Win32 route (verified working)
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

- ~~Any validation gate spike fails~~ — **spent**: the gate passed 2026-09-14
- Per-pixel click-through or true per-pixel transparency becomes a genuine user
  requirement → WPF becomes correct
- Windows App SDK stops receiving meaningful investment, or a blocking bug goes
  unfixed for more than two release cycles
- Measured startup time cannot be brought under the one-second budget and the
  runtime is demonstrably the cause
- Microsoft ships a successor framework with a credible migration path

Do **not** revisit because a newer framework is fashionable, or because an
assistant suggests a rewrite would be cleaner.
