# ADR-007 — Window presentation: opacity, click-through, edge docking

**Status:** Accepted
**Date:** 2026-09-14
**Related:** ADR-001 (framework)

---

## Context

Noto puts windows on the user's desktop in three ways: an edge-docked sidebar,
floating notes, and contextual notes positioned near their bound window. Each
needs presentation behaviors that are unusual for an ordinary application:
always-on-top, adjustable opacity, click-through, and precise positioning
relative to other applications' windows.

Technical research found that several of the obvious implementations interact
badly with WinUI 3's composition-based renderer, and that one commonly-chosen
Windows mechanism carries a serious failure mode. This ADR records the specific
techniques Noto uses, so the dangerous ones are not reintroduced later by
someone reaching for the obvious answer.

---

## Decisions

### 1. Always-on-top — use the managed API

```csharp
if (appWindow.Presenter is OverlappedPresenter p)
    p.IsAlwaysOnTop = true;
```

Fully supported, no interop. Borderless custom chrome likewise via
`SetBorderAndTitleBar`. **Do not** reach for `SetWindowPos(HWND_TOPMOST)`.

### 2. Opacity — XAML, not layered windows

**Set `Opacity` on the root XAML element.**

**Do not** use `WS_EX_LAYERED` + `SetLayeredWindowAttributes`. It fights
DirectComposition; combined with Mica or Acrylic it produces black or
fully-transparent regions. `LWA_COLORKEY` is unreliable.

The XAML route gives the same visual result with no platform risk.

> Historical note worth keeping: `SetLayeredWindowAttributes` was once used as
> a *hack to work around* WinUI 3's forced background. That is how load-bearing
> and fragile the interaction is.

### 3. Click-through — whole-window only

```
  whole-window ghost mode   WS_EX_LAYERED | WS_EX_TRANSPARENT   SUPPORTED
  per-pixel click-through                                       IMPOSSIBLE
```

Per-pixel is structurally impossible in WinUI 3: the content is drawn through
composition, so the window never sees it and cannot know what to pass through.
**Noto does not require it.**

Whole-window ghost mode works and is the feature users actually want. Two rules:

- exiting ghost mode **must** be possible without clicking the note — a global
  hotkey and a tray menu item are both required, since the note itself is
  unclickable while ghosted
- for region-specific hit-testing, subclass and handle `WM_NCHITTEST` returning
  `HTTRANSPARENT`. **Never `SetWindowRgn`**, which also clips rendering.

### 4. Edge docking — a snapped topmost window, not an AppBar

**Do not use `SHAppBarMessage`.**

The failure mode is severe: if Noto crashes without sending `ABM_REMOVE`, the
reserved desktop work area **persists until the user logs off**. The user is
left with a permanently shrunken desktop and no visible cause. It also has
known per-monitor DPI defects.

Instead: a topmost window positioned against the monitor's work area, with
slide-in and auto-hide implemented by Noto.

#### The frame inset — measured, not theoretical

**`AppWindow.MoveAndResize` takes OUTER window coordinates.** The *visible*
frame is inset from those by the invisible resize border, so a panel positioned
naively at the work-area edge sits away from the screen edge:

```
  desired (visible)   (1600,0)-(1920,1032)  320x1032
  naive               (1607,0)-(1913,1025)  306x1025   <- 7px gap, 14px narrow
  compensated         (1600,0)-(1920,1032)  320x1032   <- exact
```

Measured in the [window behaviour spike](../architecture/spikes/window-behaviour.md)
on Windows 11 26200 at 96 DPI. The inset was **7/0/7/7** (L/T/R/B) — *not
uniform*, and in physical pixels, so it differs per DPI.

**Rule:** compute `inset = GetWindowRect − DWMWA_EXTENDED_FRAME_BOUNDS` **per
edge**, compensate, and **re-measure after any DPI change**. Do not cache a
single global value.

The trade-off is accepted knowingly: maximised windows will slide underneath
the sidebar rather than being pushed aside by the shell. That is a cosmetic
difference. A permanently corrupted desktop work area is not.

### 5. Screen-capture exclusion

```c
SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);  // 0x11
```

Windows 10 2004+. Works with WinUI 3. On older builds it degrades to a black
rectangle rather than failing.

> The constant is `0x11`. It is frequently mis-transcribed as `0x2`.

**Language rule, binding on all UI copy and marketing:** this is described as
*"hide from screen sharing"*, **never** as a security feature. Microsoft
explicitly disclaims it as such, and a public bypass exists. Overstating it
would be a false security promise, which principle 10 forbids more strongly
than it requires the feature.

### 6. Positioning arithmetic

**Use `DWMWA_EXTENDED_FRAME_BOUNDS`, never `GetWindowRect`.**

`GetWindowRect` includes roughly 7–8 px of invisible resize border. Using it
misaligns every docked and contextual note by a visible margin. This applies to
every alignment calculation without exception.

### 7. Virtual desktops — derive, do not query

Only three `IVirtualDesktopManager` methods are documented. Everything beyond
them is undocumented, and **the interface IIDs change between Windows builds** —
code built against them breaks on update.

Noto does not use them. "Is the bound window currently visible?" is derived
from the documented `DWMWA_CLOAKED` attribute instead, which is stable and
answers the question Noto actually has.

### 8. Never run elevated

Drag and drop is broken in elevated WinUI 3 processes, and no Noto feature
requires elevation. Elevated windows also cannot be manipulated by an
unelevated Noto — a limitation to document rather than to work around.

---

## Rationale

Each decision follows the same pattern: the obvious implementation is either
incompatible with the framework (opacity, per-pixel click-through) or carries a
failure mode disproportionate to its benefit (AppBar, virtual desktop APIs),
and in every case a supported alternative delivers what Noto actually needs.

The AppBar decision is the clearest example. It is the "correct" Windows way to
dock to an edge, and it is what a competent developer would reach for. The
crash-leaves-desktop-broken failure mode makes it the wrong choice for a
utility that must be unobtrusive, because its worst case is highly visible and
the user cannot diagnose it.

---

## Consequences

### Positive

- no fighting the framework's renderer
- no failure mode that outlives the process
- no dependency on undocumented interfaces that break on Windows updates
- correct alignment from the start, rather than a mysterious 8 px offset
  discovered late

### Negative

- maximised windows overlap the sidebar instead of being pushed aside
- auto-hide and slide-in must be implemented by hand
- per-pixel click-through is permanently unavailable
- capture exclusion cannot be promised as security

### Test matrix

Presentation behavior must be verified on:

- Windows 10 22H2 and Windows 11 24H2
- single monitor, and multi-monitor with **mixed DPI** — research identified
  mixed-DPI multi-monitor as the dominant recurring defect theme in comparable
  software (WindowTop's issue tracker), so this is not optional
- monitor disconnect while a note is positioned on it
- display scaling changed while running

---

## Future reconsideration criteria

Revisit if:

- WinUI 3 gains supported per-window opacity or input pass-through
- a supported API appears for reserving desktop edge space without the
  orphaned-work-area failure
- virtual desktop management becomes properly documented and version-stable
- the ADR-001 validation gate fails and Noto moves to WPF, in which case items
  2 and 3 change substantially — WPF supports both natively

Do **not** reintroduce `SHAppBarMessage` or `SetLayeredWindowAttributes`
because they look like the standard approach. They were evaluated and rejected
for specific, recorded reasons.
