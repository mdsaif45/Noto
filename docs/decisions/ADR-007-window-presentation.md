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

#### Workspace width — provisional limits

> **Added 2026-09-29 after the #16 design gate.** The width limits had been
> agreed but were recorded in no document. Adding them does not change the
> docking decision above.

**These values are judgement, not measurement.** SideNotes documents no size
presets (inventory A14: free drag, no preset values), so the numbers below were
chosen by the #16 design gate and are to be revisited after dogfooding. They
are not parity requirements and must not be cited as SideNotes behaviour.

**Nominal values** — the product's intent:

```
  default            360 DIP     first run, before the user has resized
  minimum            240 DIP
  ceiling            900 DIP
  nominal maximum    min(w × 0.5, 900)        w = current work-area width, DIP
```

**Effective bounds** — what a width is actually clamped to:

```
  effective minimum  min(240, w)
  effective maximum  min(w, max(240, min(w × 0.5, 900)))

  width = clamp(requested, effective minimum, effective maximum)
```

```
  work area    effective min    effective max
  1920 DIP     240              900        nominal limits apply
   800 DIP     240              400        half the work area
   480 DIP     240              240        half meets the minimum
   432 DIP     240              240        half (216) is below it: minimum wins
   200 DIP     200              200        below the minimum: work area wins
```

Widths are authored in **DIPs** and converted to pixels against the DPI of the
display the workspace is on, so a remembered width is the same apparent size on
every display. They are constrained against the **current work area** of that
display, never its full bounds, and the workspace **never exceeds the work
area**.

**Narrow work areas.** Taken alone, the nominal limits describe an empty
interval whenever the work area is narrower than 480 DIP — a 1080 px portrait
display at 250% is 432 DIP, giving a minimum of 240 and a maximum of 216. The
effective bounds resolve this in two steps, with no further breakpoint:

- between 240 and 480 DIP, the **minimum takes precedence** over half the work
  area, and the workspace is 240 DIP;
- below 240 DIP, the **physical work area takes precedence** over the minimum,
  and the workspace fills the work area. A width there is below the nominal
  minimum; it does not satisfy it.

The interval is never empty: the inner term is at least 240, so the effective
maximum is at least `min(w, 240)`, which is the effective minimum.

#### Edge, resize and remembered width

> **Added 2026-09-29 by #16 slice 3.** Records the contract its design gate
> closed. The docking decision and the width limits above are unchanged.

**Edge.** The setting `workspace.edge` holds a Core value, `WorkspaceEdge`
(`Left` / `Right`, stored by member name). Default `Right` (parity A2, J3). A
value that is not exactly a member name — `left`, `2`, blank — reads as
`Right`, is logged, and its row is left as it was. The edge is read at launch,
and (amended 2026-10-19, Slice 1) a change from the settings surface takes
effect at once: the dock adopts the new edge (`DockedWindow.ChangeEdge`) and a
shown or behind workspace is re-docked by the same path a show uses; a hidden
or minimized one moves when it is next shown or restored. A value that is not a
member is refused when written. The platform keeps its own `DockEdge` and maps
the setting onto it.

**Remembered width.** Two settings, both in DIPs:

```
  workspace.width::<display>   the width last chosen on that display
  workspace.width              the width last chosen on any display

  open with:  workspace.width::<display>  if stored and valid
              -> workspace.width          if stored and valid
              -> 360
              then clamp(width, effective minimum, effective maximum) for that display
```

`<display>` is the platform's monitor identity (`MonitorId`), passed to Core as
opaque text. **Valid is not the same as fits:** any finite width above zero is
valid data. A stored 100 DIP or 5000 DIP is used and clamped against the live
work area where it is shown; the row is **never** rewritten because it did not
fit, so a width remembered on a large display survives a session on a small
one. A missing, corrupt or invalid value moves on to the next step of the
chain; it does not jump to the default.

**When a width is saved.** Once, when a user resize ends — never per drag
step, and never for a move Noto makes itself. The value saved is the
**visible** width (`DWMWA_EXTENDED_FRAME_BOUNDS`) in DIPs, never the outer
width: the outer width includes the invisible border, and restoring it would
widen the window by that border on every launch. It is written to the
display's own key first, then to `workspace.width`; each is attempted even if
the other fails. A failed write is logged and changes nothing on screen.

**Resize — a native subclass, not the presenter.** The docked window is
resizable from its inner edge only: the left edge of a right-docked window,
the right edge of a left-docked one. The docked edge stays on the work-area
edge and the height stays the work area's. Implemented as a window subclass
(`SetWindowSubclass`) in `Noto.Platform.Windows`:

```
  WM_NCHITTEST          only the inner edge is a resize edge; docked edge,
                        top and bottom answer as client area
  WM_SIZING             each drag step refitted: docked edge fixed, full
                        work-area height, width clamped in DIPs
  WM_WINDOWPOSCHANGING  any move or resize Noto did not make — a snap,
                        Win+Arrow, another process — sent back to the dock
  WM_EXITSIZEMOVE       the drag has ended: report the visible width once
```

Noto's own `AppWindow.MoveAndResize` runs inside an own-move guard, which is
the only thing the position check lets through. The subclass is installed
after `Activate` and before the first dock, and removed at `WM_NCDESTROY`.

**Chrome.** `SetBorderAndTitleBar(false, false)`, not minimisable, not
maximisable. A WinUI 3 caption drag does not pass through the window's
`WM_NCHITTEST`, so a title bar would let the user drag the window off its
edge and could not be refused. The invisible resize border — and so the
7/0/7/7 inset — remains without a visible border.

Why not the managed alternative, measured by the slice's spike on Windows App
SDK 2.5.1: `OverlappedPresenter.PreferredMinimumWidth` / `PreferredMaximumWidth`
bound the **outer** size, in units that cannot be distinguished at 100% scale;
they cannot restrict which edges resize, cannot refuse a snap or a move, and
nothing in the managed API marks the end of a drag. Re-docking from
`AppWindow.Changed` instead fights the drag visibly.

`DefSubclassProc` is called from exactly one place. Its signature is all plain
types, so CodeQL reports `cs/call-to-unmanaged-code` there; it is required,
because a subclass must pass on every message it does not consume, and there
is no managed equivalent for this interception.

#### Minimized and restored

> **Added by the restore/re-dock fix.** Records the invariant the position
> guard keeps across minimize and restore.

**Minimized is not hidden.** The docked window has no minimize button and the
shell's gestures (taskbar click, `Win+M`, `Win+D`) did not minimize it when
measured, but `SC_MINIMIZE` or `ShowWindow` from any process does. A minimized
window still reports `IsWindowVisible`, keeps its taskbar button and parks at
an off-screen position; a hidden window (slice 5) has none of these. The two
are distinct states and are never treated as one.

**A restore re-docks at the remembered width, on the window's own edge.**
Windows restores by proposing the placement saved before minimizing while the
window still sits at its parking position. The guard docks that proposal like
any other reposition, with three inputs chosen so minimized-state geometry
never leaks in:

```
  display   the one under the window when it is on a display; otherwise the
            one under the proposed (saved) placement — never the display
            nearest the parking position
  width     the remembered width: the width the application asked for at
            launch, or the user's last resize — never the minimized frame's
            width, never a refused move. Clamped to the landing display's
            work area; the clamp does not change what is remembered
  inset     measured fresh while the window is placed; for a restore, the
            last inset measured while placed — a minimized window's inset is
            not its docked inset
```

With no remembered width or no placed inset, the restore is let through to
Windows' saved placement rather than guessed. Minimize itself passes the guard
unchanged. No transition state is kept, so a duplicate, interrupted or failed
restore leaves nothing behind. A restore does not report a resize and writes
no width.

**Activation restores first.** Bringing a minimized window forward restores
it (re-docked as above) and only then asks for the foreground; taking the
foreground alone leaves it minimized (measured). The result counts as success
only if the window ended up restored and in the foreground.

Not covered: restoring onto a display that was disconnected while the window
was minimized lands on the display nearest the saved placement — unvalidated,
since only one display was available when this was written.

#### Global activation hotkey

> **Added 2026-09-29 by #16 slice 4.** Records the contract its design gate
> and decision pass closed. Hide and show belong to slice 5 (below).

**Settings.** `activation.hotkey.enabled` (default `true`) and
`activation.hotkey.binding` (default `Ctrl+Alt+Win+Space`, parity G1). Both
are read at launch. Amended 2026-10-19 (Slice 1): turning the hotkey off in
Settings releases it at once, and turning it on registers the stored binding
through the start-up path. A refusal is reported in Settings, as the first
user-visible surface the refusal paragraph below defers to, and the stored
choice stays the user's.

**Rebinding** (Slice 1.2b, parity G50). Settings' recorder takes a chord by
the grammar below; `HotkeyChord` decides what is valid, and `Ctrl+Alt+<letter>`
is allowed with a warning about AltGr (§12a). The old chord stays registered
until the new one is:

```
  same chord, registered           -> unchanged
  register new (its own instance)
    refused                        -> old registered and stored, announced
    registered -> release old -> store new
      stored                       -> rebound
      not stored -> register old again
        registered -> release new  -> back to old, announced
        refused                    -> new kept for the session, old used next
                                      start, announced (never silent)
```

While the recorder listens, the chord stays registered (so no other process
can take it) and a press of it while Noto is in front is ignored, so it
cannot hide the page being recorded in. Escape, leaving the recorder, Noto
losing activation, and closing Settings all cancel and give the chord back.
The procedure is `HotkeyRebinding` (platform, tested without Windows); the
registrations are `GlobalHotkey`'s, made in one place in the composition root;
the store is the typed preferences'. The stored binding is written only by the
rebind, after registering, so the store's change notification does not apply
it a second time.

**The binding grammar** — `HotkeyChord` in `Noto.Core`, and nothing wider:

```
  separator    +      whitespace around a token ignored; inside a token, an
                      empty token, or any non-ASCII character: invalid
  modifiers    Ctrl  Alt  Shift  Win    case-insensitive, exact names, each once, any order
  key          exactly one, last:  A-Z   0-9 (main row)   F1-F24   Space   (case-insensitive)
  requirement  at least one of Ctrl, Alt, Win — no modifier, or Shift alone, is invalid
  canonical    Ctrl+Alt+Shift+Win+KEY — for comparison and diagnostics only
```

A chord Windows reserves (`Win+L`) is valid syntax and refused when it is
registered. `Ctrl+Alt+<letter>` is valid syntax: parity §12a forbids it only
as a default.

**Validity lives in the setting.** The binding's `SettingKey` validity rule is
the grammar, so a malformed stored value falls back to the default through
the ordinary settings path — reported, the row left exactly as it was — and
the default chord is registered. The platform never receives text it cannot
register. Turning a chord into `MOD_*` flags and a virtual-key code is the
platform's; `MOD_NOREPEAT` is always set.

**Receiver: a message-only window, not the workspace window.** The chord is
registered to a message-only `STATIC` window (`HWND_MESSAGE` parent) that
`Noto.Platform.Windows` creates and subclasses, after settings load and
before any other window. So the hotkey exists before the workspace window
does, and survives it being hidden or created lazily later. Only `WM_HOTKEY`
carrying the registered id raises `Pressed`. Unregistered and destroyed when
the workspace window closes; Windows frees it at process exit in any case.
The subclass shares the dock's single `DefSubclassProc` call.

**What pressing it does (slice 4).** The workspace window comes to the
foreground with keyboard focus; already in front and focused, nothing
changes. It never hides — slice 5 changes that (see *Show and hide*). A minimized window is restored and re-docked first
(see *Minimized and restored*). The foreground switch uses `SetForegroundWindow`
called **synchronously while `WM_HOTKEY` is being handled** — the moment
Windows entitles the hotkey's process to take the foreground. Measured on
Windows App SDK 2.5.1: WinUI's `Window.Activate()` and `AppWindow.Show(true)`
do not take the foreground from another application; this does.

**Runtime evidence.** The foreground claims above are validated by the
harness in `tools/validation/` (#83).
- Noto is started by the shell, not by the test.
- The other application is brought forward by real input.
- Before and after every press, an unrelated process asking for the
  foreground must be refused.

Its slice 4 campaign passes 36/36, both before and after the restore fix.
Minimized activation (see *Minimized and restored*) passes 36/36 on both
edges: restored, not cloaked, the foreground window, with keyboard focus, at
exactly its docked rectangle. A build without the fix stays minimized (4/4).
The earlier campaign launched Noto from the test's own process tree, where
foreground rights can be inherited, so it is superseded. The negative results
above (`Activate()` and `Show(true)` fail to take the foreground) are
unaffected, because inherited rights can only make a call succeed.

**When the chord is refused** — held by another process, reserved by Windows
(both `ERROR_HOTKEY_ALREADY_REGISTERED`, 1409), or refused for any other
reason — Noto starts normally without the hotkey. No other chord is tried,
the stored setting is left alone, and nothing is retried until the next
launch. **The refusal is a diagnostic only:** #16 and the window-behaviour
spike require a conflict to be *reported* to the user, and no surface exists
to report it on (no tray, no settings UI, and #7's logging is not built).
That requirement is **deferred** to the first user-visible surface, not met.

#### Show and hide

> **Added 2026-10-05 by #16 slice 5.** Supersedes the slice 5 acceptance
> criteria recorded with slice 4. A second launch (A17) was not part of it;
> it is now — see *A second launch* below and ADR-013.

**The toggle** (`WorkspaceToggle`, a pure function, and `WindowCoordinator`,
which carries it out). Each press of the hotkey is judged against where the
window is now, read from Windows each time:

```
  hidden                       ->  show: re-dock, then foreground with keyboard focus
  shown, minimized             ->  restore: re-dock, then foreground with keyboard focus
  shown, another window front  ->  bring forward
  shown, in front              ->  hide
```

"In front" means the main window is the foreground window. A focused child
control or an open popup leaves it in front, so it counts.

**Hidden is `AppWindow.Hide()`** on the same window instance: never destroyed,
never cloaked, no second window. Measured: a hidden window has no
`WS_VISIBLE`, no taskbar button, and the foreground moves to another window
(Windows' default). Hidden and minimized stay distinct; a window hidden while
minimized shows normal.

**Every show re-docks against the display as it is now.** The window is shown
without activation, restored if it was minimized, docked using the display it
is on, that display's work area and DPI, and the width remembered for that
display (the settings chain in *Edge, resize and remembered width*), then
brought to the foreground. Nothing is taken from before it was hidden. It
always ends normal, docked and focused.

**Unsaved editor text** is saved before hiding, through the same explicit
save as leaving the editor. It is not autosave (parity B19 stays M3's). If the
save fails, the window is not hidden: the text stays and the editor's notice
says why. Hiding is not leaving, so the editor stays open. With motion (Slice
1.3, below) the same save runs once more when the content has left, so text
typed during the exit is kept too; it writes only if the text changed. That
save failing brings the content back and keeps the window shown, with the
notice. There is no third save.

**Close** is unchanged by this slice: closing quits. Unsaved text gets one
save; a failure cancels the close, and a second close with the same text
discards it (#86). The tray (#16 slice 7) kept this: Alt+F4 and the taskbar's
Close still quit, and the tray's Quit is the same close — see ADR-014.

**Requests are serialized and never queued.** They arrive one at a time on the
UI thread from the hotkey's message, and each is decided and carried out
synchronously; with motion (Slice 1.3) a show's or hide's content keeps moving
on the compositor after its request has been carried out. Dropped:

```
  while the window is closing              (a close that goes ahead)
  while the user is resizing               (WM_HOTKEY is delivered inside the size loop)
  while a show or hide is being carried out (the synchronous part)
  made before the last show or hide was carried out (stale; the press's message time is known)
  made during startup and the window is in front   (startup presses never hide)
```

**Motion** (amended 2026-10-19, Slice 1.3; the design is ADR-011's). The
window never moves; only its content does, inside the docked rectangle.

- *A show* is carried out as before (shown, docked, in front), then its content
  arrives. Requests during the arrival are decided as for a shown window: a
  hide interrupts it, a launch does nothing.
- *A hide* saves first (above); its content then leaves, and the window is
  hidden when it has. While it leaves the window is still shown, and the hide
  is **Hiding**: the hotkey, a launch, or activation landing back on Noto
  **reverses** it (the content returns, the window was never hidden), never
  dropped; Escape, `Ctrl+W`, the tray's Hide and deactivation do nothing, the
  hide being already under way. A window minimized meanwhile is restored by a
  reversal.
- The exit's completion hides the window only if it is still the current hide:
  a reversal, or anything that brings Noto forward, makes it stale. It does not
  restamp the last transition: the hide was decided when the exit began, so a
  press made during the exit but delivered after it completed shows the window
  again, as the reversal it was meant to be. If a close began meanwhile, the
  completion does nothing; the close owns the window.
- With Windows' animation effects off there is no motion: a show and a hide
  complete within their request, exactly as before.

A burst of presses made during startup therefore brings the window forward
once; the rest are older than that transition and dropped.

**Startup** is unchanged: a manual launch opens shown, normal and docked, on
the current topology. Hidden is never persisted. A start at sign-in (`--login`,
ADR-015) is hidden: the window is created and docked but never activated, and
the first toggle or launch shows it through the show path above.

**Virtual desktops.** The hotkey never moves the window between desktops; it
does what Windows does. Measured on Windows 11 (10.0.26300):

```
  Noto shown on desktop A, hotkey on desktop B   ->  Windows switches to A; Noto in front
  Noto hidden, hotkey on desktop B               ->  Noto is shown on B; no switch
```

A shown window is activated where it is, and Windows switches to its desktop.
A hidden window that is shown appears on the current desktop. Keeping a hidden
window on its last desktop was considered and not adopted: it needs
`IVirtualDesktopManager` (§7), its desktop id is unreliable for a window that is
not shown, and Noto is one window with no per-desktop content to preserve.
Windows 10 is not yet measured (#32).

**Alt+Tab**, measured through the real switcher: a hidden window is never
selectable, and a shown one is.

**A second launch** (A17, ADR-013) arrives through the same coordinator as a
`Launch` request, not a toggle. It follows every rule above — the drops,
startup, stale requests, re-docking, the virtual-desktop contract — with one
difference: **a launch never hides**.

```
  hidden                       ->  show on the current desktop
  shown, minimized             ->  restore
  shown, another window front  ->  bring forward (on another desktop: Windows switches to it)
  shown, in front              ->  nothing
```

#### The drawer

> **Added 2026-10-06 by #16 slice 6**, after its design gate. Parity A8, A9,
> A10, A12, A13, G7, G8, G51.

**Topmost while shown.** `OverlappedPresenter.IsAlwaysOnTop` (§1) is set at
launch whether or not docking succeeded, and asserted again on every path that
ends shown. Measured: a shown Noto is above a maximized window and above a
borderless full-screen window, including one that is itself topmost (the most
recently activated topmost window wins). An **exclusive** full-screen
application (DXGI exclusive mode) is out of reach: summoning Noto makes it
leave exclusive mode, as Alt+Tab does. Not validated. A hidden Noto has no
place in the z-order; a minimized one stays minimized until restored.

**Putting it away.** Two more request kinds reach the same coordinator.
Neither ever shows, restores or brings anything forward:

```
                        Toggle   Launch   Dismiss   Deactivated
  hidden                show     show     -         -
  shown, minimized      restore  restore  -         -
  shown, behind         focus    focus    hide      hide, if allowed and current
  shown, in front       hide     -        hide      hide, if allowed and current
```

A deactivation can still read "in front": Windows tells Noto that activation
is leaving before the foreground has visibly changed. The generation (below)
is what makes it safe to act on.

- **Dismiss** is explicit: `Ctrl+W` from any surface (A10, G7), or Escape
  where its behaviour hides. Pin does not stop it.
- **Deactivated** is parity A13's "close on outside click". The user's term
  and the mechanism differ: Noto implements it as **workspace deactivation** —
  activation moving to a window of another process, by a click, Alt+Tab, the
  Start menu, the desktop or a virtual-desktop switch. Activation moving to
  one of Noto's own windows (the editor's context menu, an IME window) does
  not count. The signal is `WM_ACTIVATEAPP` on the docked window's subclass,
  which Windows sends only when activation crosses the process boundary. So
  Noto's own windows are excluded by Windows, not by inference. "Allowed"
  means the `workspace.hide-on-deactivation` setting is on (the default) and
  the workspace is not pinned. "Current" is the activation generation below.
- The existing drops apply to both: closing, resizing, mid-transition, stale,
  and **nothing is put away during startup**.
- **Every hide saves first** (`SaveBeforeHide`, the explicit save). A failed
  save keeps the workspace shown, with the editor's notice, and focus is not
  taken back. Each dismissal is one attempt.

**The activation generation.** A deactivation is decided on the dispatcher,
after the activation change has finished, never inside it. By then it may be
out of date. One monotonic counter, owned by the coordinator, says whether it
still holds:

```
  WM_ACTIVATEAPP(FALSE)    generation++  ->  post Deactivated(generation)
  WM_ACTIVATEAPP(TRUE)     generation++  ->  post Activated(generation, landed)
                           landed = Noto's window was the foreground at this message
  Show / Restore / Focus   generation++      (Noto brought itself forward)

  A request made in generation n is valid only if generation == n when it is decided.
  Valid Deactivated -> the table above, with every existing drop.
  Valid Activated   -> the reconciliation below.
```

| Sequence | Result |
|---|---|
| FALSE → Deactivated(n) → still deactivated | hides: valid, allowed, shown |
| FALSE → TRUE → Deactivated(n) | ignored: stale |
| FALSE → TRUE → FALSE → Deactivated(n), Deactivated(n+2) | the first is ignored; the second hides |
| FALSE → `Dismiss` | `Dismiss` as before. A later Deactivated finds the window hidden |
| FALSE → `Launch` | the launch shows or focuses (generation++), so Deactivated(n) is stale. **A launch never hides** |
| FALSE → `Toggle` | `Toggle` as before. If it brought Noto forward, Deactivated(n) is stale |
| pinned, or the setting off | never hides automatically; `Dismiss`, Escape and the hotkey still do |
| during a resize, during startup, while closing | dropped, as every request is |
| hidden → TRUE, landed → Activated(n) | shown (generation++). A later Deactivated from before it is stale |
| hidden → TRUE, landed → Activated(n) → shown → TRUE (from that show) | the second Activated finds the window shown: nothing |
| hidden or minimized → TRUE, **not landed** → Activated(n) | nothing: it stays hidden or minimized. The generation still advances |
| shown → TRUE → FALSE → Activated(n) | ignored: stale; a hidden window stays hidden |

**Never hidden and in the foreground.** A hidden Noto must never stay the
foreground window. Windows delivers `WM_ACTIVATEAPP(TRUE)` in two situations,
told apart by one observation made **at that message**: whether Noto's window
is already the foreground window.

- **Genuine activation: landed.** Windows has already given Noto the
  foreground when the message arrives. Measured in 47 of 47 cases: Alt+Tab
  completing to a hidden Noto (the switcher takes activation, Noto hides, then
  the switch completes to Noto), a taskbar click on a minimized Noto, and
  Noto's own show or restore. A current `Activated` request reconciles it
  with a launch's rules: hidden → show, minimized → restore. A shown window is
  left alone.
- **Background activation: did not land.** Another process asked Windows to
  put Noto in front, and Windows refused (the foreground lock). It still
  delivers `WM_ACTIVATEAPP(TRUE)` (with no losing thread), but the foreground
  stays where it was. Measured in 14 of 14 cases, hidden and minimized. Noto is
  not the foreground thread, so no keystroke can reach it, and it **must not
  react**: a refused foreign request must never make a hidden or minimized
  Noto visible, restored, foreground or focused.

The observation is taken once, in the window procedure, and carried with the
request. The dispatcher never reads the foreground later: posted work can wait
seconds while Noto is minimized (measured 540 ms and 2.15 s), and reading
afterwards is what caused the A13 race. A reconciliation never hides. Pin and
the setting do not apply to it, because they govern putting the workspace
away. Closing still drops it, so nothing is shown again during shutdown. Its
staleness is only the generation's, not the time-based rule. No window class,
shell switcher or timer is special-cased.

Hide, `Dismiss` and the hotkey's hide do not advance the generation: a pending
deactivation then finds the window hidden and does nothing. No timer, sleep or
retry is involved. Every rule is decided by the order of events on the UI
thread, which owns the counter.

**Escape** has SideNotes' four behaviours, stored in `workspace.escape`
(default leave-folder-or-hide), chosen in Settings and applied to the next
press (amended 2026-10-19, Slice 1). An inline input (renaming or naming a
folder) cancels itself first, in every mode. Noto's editor is one level deeper
than SideNotes': leaving it is the same kind of step as leaving a folder.
Settings is a level above whichever surface opened it, so the two "leave" modes
return there.

```
                       settings        editor        note list     folder list
  LeaveFolderOrHide    leave settings  leave editor  leave folder  hide         (default)
  LeaveFolder          leave settings  leave editor  leave folder  nothing
  Hide                 hide            hide          hide          hide
  None                 nothing         nothing       nothing       nothing
```

**Settings applied as they change** (Slice 1). The edge, Escape, hiding on
deactivation, single-click opening, the theme and the hotkey's on/off are each
applied by one owner when the store announces the change: the coordinator
(edge, deactivation), the window (Escape, single click, the theme on its
content) and the hotkey. Hiding on deactivation is read at each deactivation,
so a change governs the next one.

**Pin** (A9) is a toggle in the workspace header, reachable by Tab and Space;
there is no global chord (G6 is unbound by default). While pinned, losing
activation does not hide the workspace; `Ctrl+W`, Escape and the hotkey still
do. Session-only: never stored, so every launch starts unpinned.

**Virtual desktops.** A desktop switch is a deactivation. Unpinned, switching
desktops hides Noto, and the hotkey shows it on the new desktop (the hidden
case above). Pinned — or with the setting off — Noto stays shown on its own
desktop, and the hotkey from another desktop makes Windows switch back (the
shown case above). Both measured. The §4 contract itself is unchanged; Noto
still uses no `IVirtualDesktopManager` method (§7).

**The tray** (#16 slice 7, ADR-014) adds entry points, not rules. Selecting the
icon is a `Launch` — not the toggle: on Windows 11, opening the overflow takes
activation from Noto before the click arrives, so an in-front Noto is never in
front by then (measured). Its menu's Show is a `Launch`, Hide a `Dismiss`, and
Keep Noto open is the header's pin path. The menu's own window takes the
foreground; for a hidden Noto that activation does not land, so nothing is
shown.

**Measured, not predicted: Noto's own taskbar button.** Pressing it while
Noto is in front does not take activation from Noto, and Windows does not
minimize a window that is not minimizable — so it does nothing. The design
gate had predicted "hidden". The owner accepted the measured behaviour as a
deviation; there is no workaround.

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
answers the question Noto actually has. Show and hide (§4) rely on Windows' own
desktop handling and use none of these methods.

### 8. Never run elevated

Drag and drop is broken in elevated WinUI 3 processes, and no Noto feature
requires elevation. Elevated windows also cannot be manipulated by an
unelevated Noto — a limitation to document rather than to work around.

### 9. Edge activation — Hot Side and drag to reveal

> **Added 2026-11-17 by #185 part 1** (parity A4, A15, J27, J28; principle 7).
> The Open Bar (A3), an always-present edge handle, is #185 part 2 (§10).
> Nothing in §4 changes: a reveal is one more entry point into the
> same coordinator.

**Two settings, both off by default, each independent.** Settings › Sidebar:
*Show Noto when the pointer rests at the screen edge* (`activation.hot-side.enabled`,
Hot Side) and *Show Noto when you drag something to the screen edge*
(`activation.drag-reveal.enabled`). Off by default because a reveal the user did
not ask for is the complaint principle 7 records against SideNotes' edge
trigger. Applied at once (the composition root's one owner, `ApplyEdgeReveal`),
like every Slice 1 setting.

**Mechanism: one low-level mouse hook, not an edge window.** The implementation
plan (R7) had proposed a thin topmost window per edge. It is not adopted for
these two: a window on the outermost pixel columns takes that edge from every
application under it — the scroll bar of a maximized window is the common case,
and Fitts' law puts the user there on purpose — and it cannot observe a drag
that another application owns. `SetWindowsHookEx(WH_MOUSE_LL)` observes without
taking anything: every event goes on to the next hook unchanged. The Open Bar,
which *is* a visible handle, may still be a window (part 2).

```
  both off            no hook, no thread                          (nothing to cost)
  either on           EdgePointer: a background thread that installs the hook,
                      waits in GetMessage, and owns one timer
  hook callback       buttons held <- the message; EdgeReveal.Moved (numbers in,
                      a step out); at most SetTimer/KillTimer; CallNextHookEx
  timer elapses       read once: shown? full-screen? locked? moving a window?
                      -> reveal: handed to the UI thread -> coordinator, Launch
  both off again,     WM_QUIT to the thread: it stops the timer and unhooks on
  or at exit          itself; ReleaseResources disposes it
```

- **Its own thread, never the UI thread.** A low-level hook runs in the message
  loop of the thread that installed it, and every pointer move on the system
  waits for it; Windows removes a hook that exceeds `LowLevelHooksTimeout`
  without telling it. The UI thread can be held by layout, a save or a dialog, so
  the hook is on a thread that does nothing else. The recorder's keyboard hook
  (Slice 1.2b) stays on the UI thread: it exists for one recording only.
- **The callback is minimal.** No allocation, no trace, no lock, no read of
  another window per event (guarded by `EdgeRevealGuardTests`). The rules are a
  pure state machine in Core, `EdgeReveal`, unit tested without a pointer.
- **Idle cost.** No hook while both are off. While one is on, the cost is per
  pointer event; nothing polls. The one timer runs only while a gesture is armed
  at the edge (a dwell), and stops when it elapses or the pointer leaves. The
  idle-CPU budget (quality-gates: < 0.1%) is unaffected by construction: no
  pointer movement, no work.

**The edge.** The docked edge of the display the workspace is on, read when a
setting or the side changes and on every hide (the one state a reveal starts
from), never per event:

```
  x        the display's bounds edge (where the pointer stops), the outermost 2 physical px
  y        the work area's span less a 48 DIP corner at each end
           (a maximized window's caption buttons; the taskbar is outside the work area)
  beyond   low-level input is reported before the pointer is clipped, so a point past
           the edge is at the edge — unless another display touches that edge
           ("shared"), when it is on that display and never counts
```

A topology change while Noto is hidden is picked up at the next hide (no display
broadcast reaches a thread with no window). Unvalidated: a mixed-DPI
multi-display layout (#30).

**The gestures.**

| | Hot Side (A4) | Drag to reveal (A15, J27, J28) |
|---|---|---|
| Armed when | pointer in the band, **no button held** | left button held and moved more than the system drag threshold (`SM_CXDRAG`/`SM_CYDRAG`) from where it was pressed, from any application, pointer in the band |
| Dwell | 300 ms, resting: moving more than 3 px restarts it | 150 ms from arrival; moving along the edge keeps it |
| Disarmed | leaving the band, any button pressed | leaving the band, the button released (resting there afterwards is Hot Side's, if on) |

**Never revealed** — read once, on the hook's thread, when the dwell elapses:

```
  Locked     the session is locked or another desktop (a secure prompt) has the input:
             OpenInputDesktop fails
  Shown      Noto is shown, in front or behind (WorkspacePresence); a minimized Noto is
             restored, as a launch restores it
  Immersive  the foreground window covers the docked display's whole bounds and has no
             caption (the desktop's own windows excluded); a maximized window under an
             auto-hiding taskbar keeps its caption, so it is not full-screen
  Moving     the foreground thread is in a move or size loop (GUI_INMOVESIZE): a window
             dragged to the edge to Snap it is not a drop
```

**Once per arrival.** An elapsed dwell, revealed or refused, is spent until the
pointer leaves the band: a pointer left at the edge never brings Noto back after
the user has put it away.

**Through the coordinator, as a launch.** The hook's thread hands the decision
time to the UI thread (`DispatcherQueue`), where it becomes
`WorkspaceRequest.Launch`: show, restore or bring forward, never hide, with every
drop of §4 (closing, resizing, mid-transition, stale, startup). No new request
kind and no new rule.

**Focus — measured.** The hotkey takes the foreground because `WM_HOTKEY`
entitles it to; a pointer resting on another application's window gives Noto no
such right, and Noto does not inject input to acquire one (that is what the
harness's negative controls exist to catch). So a reveal ends shown, docked and
topmost, and `SetForegroundWindow` is asked for as for any launch. **Measured
2026-11-17** (`Invoke-Settings.ps1` case 23, Windows 11 26300, one display):
Windows refuses it. Noto is shown at the edge (`shown=True`) but the other
application keeps the foreground (`foreground=False`). The user's click, or the
hotkey, brings it in front. For a drag this is what matters: a drop needs no
foreground. Hot Side is therefore a way to *see* Noto at a glance, not to start
typing in it; that is accepted, and a focus-taking reveal is not pursued (it
would need injected input).

**Not decided here.** SideNotes' hide-when-the-pointer-leaves behaviour (an
edge-revealed panel retracting on its own) is not part of A4 or A15 as scored;
Noto's existing rules (hide on deactivation, `Ctrl+W`, Escape, the hotkey) put
it away. A text-selection drag that reaches the edge reveals Noto like any drag
(a drag from any application counts); only a window move is excluded.

**Evidence.** `EdgeRevealTests` (Core: dwell, jitter, buttons, drag threshold,
wrong display, shared edge, full-screen, locked, shown, once per arrival, clock
wrap); `EdgeRevealGuardTests` (the hook only while on, its callback, its thread,
the zone and full-screen arithmetic, the coordinator path); the settings guards;
`Invoke-Settings.ps1` case 23 (the real pointer).

### 10. Edge activation — the Open Bar

> **Added 2026-11-18 by #185 part 2** (parity A3, J1, J27; principle 7). §9's
> objection to an edge window was to a full-height strip used for *hover*; the
> Open Bar is a different thing, a small visible handle the user aims at.
> Nothing in §4 or §9 changes: a click is one more entry into the same
> coordinator.

**Two settings, in Settings › Sidebar.** *Open Bar*: Off / Always shown / Hide
when the mouse is idle (`activation.open-bar.mode`, default **Off**; SideNotes'
"always hidden" is Off, so the bar is independently disableable). *Drop files on
the Open Bar* (`activation.open-bar-drop.enabled`, J27, default **on**: the bar
itself is opt-in, and a drop aimed at it is no surprise). Applied at once by the
composition root's one owner, `ApplyOpenBar`.

**Mechanism: one small window, only while on.**

```
  Off                 no window, no event hook                        (nothing to cost)
  on                  OpenBarWindow: a STATIC popup, subclassed, on the UI thread
                      WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST | WS_EX_LAYERED
                      + one out-of-context EVENT_SYSTEM_FOREGROUND hook (UI thread's loop)
  Noto shown/hidden   coordinator Shown / Hidden -> ApplyOpenBar -> Configure(notoHidden)
  click (WM_LBUTTONUP) -> App.OpenFromBar -> coordinator, Launch
  drop (WM_DROPFILES) -> Launch, then MainWindow.ImportDroppedOnBar(files)
  Off again, or exit  Dispose: unhook, DestroyWindow; ReleaseResources disposes it
```

- **Geometry.** 6 × 96 DIP at the docked display's DPI, never taller than the
  work area, flush with the **work area's** docked edge (where Noto docks, so
  beside a taskbar on that side), centred on its height. The rest of the edge,
  scroll bars and caption buttons included, stays the application's.
- **Never activates.** `WS_EX_NOACTIVATE` and `MA_NOACTIVATE`: a click leaves
  the foreground where it was until the launch brings Noto forward (the click
  is the input that entitles it to). `WM_NCHITTEST` answers `HTCLIENT`, as a
  static control would otherwise let the pointer through.
- **Look.** The system accent (`DwmGetColorizationColor`) at 60 % opacity
  (`SetLayeredWindowAttributes`, `LWA_ALPHA`; §2's "XAML, not layered" is about
  the workspace, not this GDI-painted handle). High contrast: `COLOR_HIGHLIGHT`,
  opaque (ADR-011). Recoloured on `WM_SETTINGCHANGE`, `WM_SYSCOLORCHANGE` and
  `WM_DWMCOLORIZATIONCOLORCHANGED`.
- **Re-placed** at every configure (each show, hide, setting and side change),
  and on `WM_DISPLAYCHANGE`, `WM_DPICHANGED` and `WM_SETTINGCHANGE`, which a
  top-level window receives (§9's hook thread could not).

**When it shows.** A pure state machine in Core, `OpenBar`, unit tested:

```
  Hidden     Off; Noto shown (a minimized Noto counts as hidden: a launch restores it);
             a full-screen app in front of the docked display (§9's FillsDisplay);
             the input desktop elsewhere (§9's lock check)
  Shown      Always shown; Hide when idle, until 3 s pass with no pointer over it
  Faded      Hide when idle, after those 3 s: alpha 1/255, invisible but still hit-testable,
             so the pointer over it (WM_MOUSEMOVE) draws it again and restarts the count,
             and a click or drop still works
```

The full-screen and lock reads are made at every configure and at every
foreground change; nothing polls. The one timer is the one-shot fade, running
only between the bar appearing (or being pointed at) and its fading.

**Drop: the shell's own, not an OLE target.** `DragAcceptFiles` while the drop
setting is on; `WM_DROPFILES` gives the paths (`DragQueryFile`), then Noto is
shown as for a click and the files go where a drop on Noto would: the open
folder's stack, else the selected folder (else the first), through the same
importer, result line and Undo as F9 (`ImportOnStack`, `ImportOnRow`). No COM
`IDropTarget`, no second import path.

**Decided against.**

- *Fading by the §9 hook when Hot Side or drag is on.* "Near" the bar would be
  the pointer in §9's band; the bar's own `WM_MOUSEMOVE` gives "over" it with no
  cross-thread signal and one path whatever the other settings are.
- *Session notifications for the lock.* The bar lives on the user's desktop,
  which Windows neither shows nor routes input to while locked; the lock check
  at each re-read is enough.

**Known limits.** A window that goes full-screen without a foreground change
(a browser's F11, a video's full-screen button) is noticed at the next
foreground change, so the bar can sit over it until then; following the
foreground window's location changes would close that, at a per-move cost. A
drag over a *faded* bar does not draw it (no `WM_MOUSEMOVE` during a drag), but
the drop is still taken; drag to reveal (§9) shows Noto for such a drag.
Unvalidated: a mixed-DPI multi-display layout (#30).

**Evidence.** `OpenBarTests` (Core: the three modes, away, the one-shot fade,
pointer, return, mode switches); `OpenBarGuardTests` (the window's styles, no
activation, the hit test, the click, drops only when on, placement, the one
timer, the foreground hook, the bar only while on and destroyed when off or at
exit, the show/hide subscriptions, the coordinator path, one import path); the
settings guards; `Invoke-Settings.ps1` case 24 (a real click on the bar).

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
