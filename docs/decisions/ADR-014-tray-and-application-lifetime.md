# ADR-014 — The notification-area icon and the application's lifetime

**Status:** Accepted
**Date:** 2026-10-12
**Related:** ADR-007 §4 (show, hide, the drawer), §8 (never run elevated); ADR-013 (single instance); ADR-001 (tray: raw `Shell_NotifyIcon`); ADR-008 (identity, run at login); A5, A6, A9 (sidenotes-parity); #10, #5

---

## Context

Before #16 slice 7, Noto had no tray, and the only ways to end it were Alt+F4 and
the taskbar's Close. Both went through `AppWindow.Closing`, where unsaved text
gets one save (#86) and shutdown begins (ADR-013). Nothing handled the session
ending: logging off or shutting down Windows with unsaved text lost it.

Parity asks for a tray icon that brings the panel up (A5) and a context menu
(A6), and for the pin to be reachable from the tray (A9). The design gate also
had to settle what ends Noto, and how the session's end keeps the save contract.

## Problem

1. Where does the icon live, and how does it survive Explorer restarting?
2. What does selecting it do, and what does its menu offer?
3. What ends Noto, and how is shutdown entered exactly once?
4. What happens to unsaved text when the session ends?

---

## Decision

### The icon is an input surface

`TrayIcon` (`Noto.Platform.Windows`) owns a hidden top-level window and the icon
on it. It raises what the user did and what Windows asked, and changes nothing
itself. The composition root hands each request to the path that already owns
it:

```
  select (click, Enter, Space)  ->  WindowCoordinator  WorkspaceRequest.Launch
  menu Show                     ->  WindowCoordinator  WorkspaceRequest.Launch
  menu Hide                     ->  WindowCoordinator  WorkspaceRequest.Dismiss
  menu Keep Noto open           ->  the header's pin toggle -> PinChanged -> coordinator.Pinned
  menu Quit                     ->  WM_CLOSE -> AppWindow.Closing   (what Alt+F4 does)
  session ending                ->  the explicit save, then WM_CLOSE -> AppWindow.Closing
```

No new request kind, coordinator or state. `WorkspaceToggle` is unchanged.

### The window

- **Hidden and top-level, not message-only.** `TaskbarCreated` and the
  session messages are delivered to top-level windows only. Measured: a
  broadcast reached the workspace window and this one, and not a message-only
  window.
- `WS_POPUP` and a tool window, never shown: no taskbar button, no Alt+Tab
  entry.
- It exists whether or not docking succeeded. That is why the session messages
  are taken here.
- It ignores `WM_CLOSE`. Restart Manager sends one to every top-level window of
  the process (measured), and only `TrayIcon.Dispose` may end this window.

### Identity, Explorer restarts, failure

- **The window and one id**, not a GUID. While Noto is unsigned, Windows ties a
  GUID icon to the executable's path, and Noto runs from several paths (#5).
- **Adding an icon that already exists fails** rather than duplicating it
  (measured).
- **On `TaskbarCreated`** the icon is deleted, if it still exists, then added
  again with `NOTIFYICON_VERSION_4`. Nothing else happens: no request, no show
  or hide, no new process, and ownership is untouched.
- **A failed add is recorded, not fatal.** The next `TaskbarCreated` tries again.
  If the window itself cannot be created, Noto runs without the icon and without
  session-end saving, as before this decision.
- **The icon is Noto's own** (#95, `docs/branding/`). It was Windows' generic
  application icon when this was written. The tray loads the executable's
  embedded icon at the shell's small-icon size, and falls back to the generic
  one only when the executable has none.

### Selecting the icon is a launch, not the toggle

On Windows 11 a new icon sits in the overflow ("Show hidden icons"). Phase 0
measured what happens when it is reached there:
- Opening the overflow takes activation from Noto. `WM_ACTIVATEAPP(FALSE)`
  arrives before the click is delivered, in every valid round.
- An unpinned Noto is then already hidden (parity A13). A pinned Noto, or one
  with hiding on deactivation turned off, is now behind.
- So a Noto that was in front is never in front when the click arrives, and the
  toggle's "in front → hide" cannot happen.

Selection is therefore a **launch**:

```
  hidden -> show    minimized -> restore    behind -> bring forward    in front -> nothing
```

- A double click delivers up to two selections. The second finds Noto in front
  and does nothing. No timer or debounce is involved.
- Noto is not inferred to have been "in front before the overflow opened". No
  shell window is special-cased.

**The time stamp is Noto's own.** Explorer *sends* the callback, so
`GetMessageTime` returns the time of an earlier message. Measured: one value
repeated across events seconds apart. The receive time is read with
`Environment.TickCount` when the callback is handled. That is the clock
`ActivationContext.RequestTime` already uses, and its wrap-safe comparison.

### The menu

```
  Show Noto          enabled unless Noto is in front                       -> launch
  Hide Noto          enabled when shown and a coordinator exists           -> dismissal (saves first)
  ───────
  Keep Noto open     ✓ when pinned; enabled when a coordinator exists      -> the pin path
  ───────
  Quit Noto          always                                                -> the close path
```

- **Opened by** a right-click, Shift+F10 or the menu key (`WM_CONTEXTMENU`).
  Enter and Space on the icon arrive as `NIN_SELECT`.
- **The states** are a pure function (`TrayMenu.For`) of where the workspace is,
  read before the menu takes the foreground.
- **What a choice does** is decided again by the coordinator when it is made.
- **Native menu.** It is a Win32 popup: keyboard, Narrator and positioning work
  as Windows provides. It does not follow the dark theme (ADR-011; revisit in
  M4).
- **Taking the foreground.** The hidden window takes the foreground before the
  menu shows, so the menu closes when the user clicks elsewhere. Windows granted
  this at every measured request.
  - That window is not the workspace, so for a hidden Noto the activation does
    not land and nothing is shown (ADR-007 §4).
  - Afterwards the hidden window is never left in front: if it still is, the
    foreground goes back to where it was.
- **The choice is returned by the menu loop** (`TPM_RETURNCMD`). `WM_COMMAND` is
  not handled, so another process cannot choose Quit by sending one.
- **Not in the menu:** settings, diagnostics, update checks. Run at login was
  added later as "Start Noto with Windows" (ADR-015), read from Windows when the
  menu opens.

### Closing still quits

Alt+F4 and the taskbar's Close quit, as before. `Ctrl+W` and Escape put the
workspace away (A10, A12). Hide-to-tray on close was considered and not adopted,
for three reasons:
- it would duplicate `Ctrl+W`;
- it would change the #86 contract and its validation;
- it would leave Noto unquittable if the icon failed.

### One shutdown transition

```
  Alt+F4 ─────────────┐
  taskbar Close ──────┤
  tray Quit ──────────┼─> WM_CLOSE ─> AppWindow.Closing ── cancelled (failed save) ─> still running
  WM_ENDSESSION(TRUE) ┤                     │
  Restart Manager ────┘                     └ goes ahead, first time only:
                                                _closing; pipe answers ShuttingDown; coordinator drops all
                                                └> Window.Closed (once):
                                                     icon deleted -> hotkey -> pipe -> mutex released
```

- **Exactly one place enters shutdown:** `AppWindow.Closing`, when the close
  goes ahead and shutdown has not already begun.
- **A second close request is harmless.** Measured under Restart Manager: it
  sends its own `WM_CLOSE` after the one Noto posts, and Closing was raised again
  before the window was gone. That second raising changes nothing.
- **Once the window is destroyed,** a close request reaches nothing.
- **Cleanup runs once,** on Closed, and every disposal is idempotent besides.
- **The icon goes first,** before the mutex is released, so a successor's icon
  never appears beside it.
- `Window.Closed` is also raised for `Application.Exit` (`--smoke-test`), which
  does not raise Closing (measured), so the icon is removed there too.

### Quit

Quit sends `WM_CLOSE` and returns once it has been handled. Closing and, if the
close went ahead, Closed have run by then (measured).

| Case | Result |
|---|---|
| Clean editor | exits |
| Unsaved text, save succeeds | saved, exits |
| Save fails | close cancelled, text kept, notice shown; Noto is then shown by a launch, restored if minimized, so the notice can be read |
| Quit again, same text | discards and exits (#86) |
| Hidden | always clean: every hide saves first, and nothing can be typed while hidden |

### The session's end

`WM_QUERYENDSESSION` and `WM_ENDSESSION` reach the icon's window. Phase 0
measured WinUI itself: it answers the query with TRUE and raises no Closing for
either message. Noto's handler is therefore the only one that acts.

```
  WM_QUERYENDSESSION   the explicit save (SaveBeforeHide), synchronously
                       saved          -> allow
                       not saved      -> refuse, with ShutdownBlockReasonCreate:
                                         "Noto could not save a note. Cancel to keep it."
                       critical end   -> allow; it cannot be refused
                       no state changes: the end may still be cancelled

  WM_ENDSESSION(FALSE) the block reason is removed; nothing else

  WM_ENDSESSION(TRUE)  the explicit save again, synchronously (text typed after the query was answered)
                       then WM_CLOSE is posted: the one close path above
```

- Nothing is deferred to the dispatcher: the process may end as soon as the
  session does.
- **A query that comes again saves again.** A clean editor is a no-op.
- **No save interleaves another.** One UI thread runs every save, and SQLite
  does not pump messages.
- **A second launch during this** is unchanged (ADR-013). Before the close goes
  ahead it is accepted; after, it is told `ShuttingDown`.

**Residual losses, documented and not mitigated:**
- a critical or forced end whose save fails;
- an edit made between the query and the end that then cannot be saved;
- a kill or a crash (#10).

**Not validated end to end:** a real logoff, shutdown or restart; they cannot be
driven from inside the session. Restart Manager — the route an installer uses —
is validated.

### Single instance (ADR-013)

- **Owner only.** The icon is created only after the claim returns Owner, so a
  second launch never shows one.
- **The handoff is unchanged.** A second launch still arrives as `Launch`
  through the pipe.
- **No reactivation during shutdown.** Tray input is ignored once closing, and
  the coordinator drops every request.
- **Explorer restarting** touches only the icon, never the mutex or the pipe.

### Trust

- **Same process, same user**, as in ADR-013.
- **Forged callbacks.** Another process of the same user can post the icon's
  callback. That does what a click does: a launch, or opening the menu.
- **No forged choices.** A menu choice cannot be forged, because `WM_COMMAND` is
  not handled.
- **No elevation concerns.** Noto never owns a data root elevated (ADR-007 §8),
  so no message filter is needed for `TaskbarCreated`.

### Run at login: decided in ADR-015

The paragraphs below record the boundary as it stood when this was written.

Run at login (A16, J4) is the next decision. It depends on this one: Noto
started at login can run with only the icon, and the icon must tolerate a
taskbar that is not there yet, which the `TaskbarCreated` recovery provides. It
still needs:
- a mechanism: `HKCU\…\Run` while unpackaged, `StartupTask` with identity
  (ADR-008);
- a start mode that does not show the workspace, and how a second launch treats
  it;
- off by default.

Validating it writes the user's registry, which needs the owner's approval. The
tray does not depend on run at login.

---

## Rationale

- **Every request keeps one owner.** The coordinator still owns presence, the
  header owns the pin, and `AppWindow.Closing` owns the end. The tray adds
  entry points, not rules. That is what kept slice 6's activation protections
  intact without change.
- **Measured over assumed.** The toggle was the approved design until Phase 0
  showed the overflow takes activation first. Launch is what the platform
  allows.
- **The session's end gets the same save as a close,** at the only moment
  Windows guarantees Noto can still act. A failure is refused aloud rather than
  lost quietly.

## Consequences

### Positive

- Noto can be summoned, put away, pinned and quit from the notification area.
- Logging off with unsaved text saves it, or refuses with a reason.
- There is one shutdown transition, and cleanup is idempotent.
- An Explorer restart restores the icon and changes nothing else.

### Negative

- **Parity A5 is partial.** The icon brings Noto up but does not toggle it away.
  Hiding is the menu, `Ctrl+W`, Escape, the hotkey or a click elsewhere.
- **The icon starts in the overflow,** and a shown, right-docked, topmost Noto
  covers the overflow. While Noto is shown, the icon is reachable by keyboard
  (Win+B), not by mouse.
- The menu does not follow the dark theme.
- **After a crash the old icon lingers** until the pointer passes over it.
  Identity by window is the cost of not using a GUID while unsigned.
- **The icon was a placeholder at first;** #95 replaced it with Noto's own.

## Future reconsideration criteria

- **Noto is signed with a stable install path (#5, ADR-008):** a GUID identity
  would let a new instance replace a crashed one's icon.
- **Windows changes overflow activation,** or an icon on the taskbar itself is
  measured not to take activation: reconsider the toggle for that case only.
- **The settings surface arrives:** the tray toggle (J1) and #16's "each
  activation surface can be turned off".
