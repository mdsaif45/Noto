# ADR-015 — Run at login

**Status:** Accepted
**Date:** 2026-10-12
**Related:** ADR-008 (packaging, `StartupTask`); ADR-013 (single instance); ADR-014 (the tray and the lifetime); ADR-007 §4 (startup), §8 (never elevated); A16, J4 (sidenotes-parity); #94, #5

---

## Context

Parity asks for Noto to start when the user signs in, off by default (A16, J4).

ADR-008 chose a signed sparse package, which would give Noto package identity
and with it `StartupTask`. None of that exists yet:
- Noto is unpackaged (`WindowsPackageType=None`);
- there is no manifest, installer or updater;
- signing (#5) is unresolved.

ADR-008 itself names the substitute while unpackaged: the registry `Run` key.

A start at sign-in must not show anything. The user did not ask for the
workspace; Noto should simply be there when they do.

## Problem

1. Which mechanism registers Noto, and where is "on" recorded?
2. How does Windows' own Startup apps setting fit in?
3. How does a sign-in start differ from a launch, and what does it do when Noto
   is already running?

---

## Decision

### The registration: `HKCU\…\Run`, for the unpackaged product

```
  HKCU\Software\Microsoft\Windows\CurrentVersion\Run
      Noto = "<full path of this Noto.exe>" --login        REG_SZ
```

- **Per user,** with no administrator rights.
- **The command never carries a data root.** A login start uses the default
  one.
- **The only writes** are the tray's "Start Noto with Windows" turning it on or
  off. Nothing registers or corrects itself at startup, so another copy of Noto
  (a debug build, a test copy) never takes over the registration by running.

`StartupTask`, the sparse package, signing and an installer are not part of
this decision. When the packaging slice lands, it moves the registration to
`StartupTask` and removes the `Run` value. There is one mechanism per
deployment, never both.

### Windows' registration is the only truth

Noto stores nothing. Every read asks Windows, so a registration removed by Task
Manager, a cleaner or the user is reported as it is, and is never put back.

```
  Run\Noto absent, not a string, unparsable, or for another executable  -> OFF
  Run\Noto = this Noto.exe --login, and StartupApproved\Run\Noto
        absent, or 12 bytes with first byte 0x02                        -> ON
        12 bytes with first byte 0x03                                   -> WINDOWS-DISABLED
        anything else (kind, length, byte)                              -> WINDOWS-DISABLED
```

**`StartupApproved` is Windows' Startup apps setting.**
- **It is undocumented.** The format above was measured (#94 Phase 0): every
  entry on the validation machine is 12 bytes, with first byte `0x02` or
  `0x03`. Writing a `Run` value does not create an entry.
- **Anything unrecognised is not on.** Noto never claims "on" from a state it
  cannot read.
- **Read-only.** `LoginRegistration` alone interprets it, and opens it only
  read-only. Noto never writes or deletes it.
- **The user's choice in Windows wins.** The entry is keyed by the value name,
  so it survives Noto re-pointing the path.

### The tray row

| State | "Start Noto with Windows" | Choosing it |
|---|---|---|
| OFF | enabled, unchecked | registers this executable, then reads the state again |
| ON | enabled, checked | removes the registration, then reads again |
| WINDOWS-DISABLED | **grayed**, unchecked, "(turned off in Windows Startup apps)" | nothing: only Windows can turn it back on |

- **Read as the menu opens.** The row reflects Windows at that moment.
- **Judged again when chosen.** The choice acts on what Windows has then, not
  on what the menu showed.
- **No false success.** Turning it on while Windows has it disabled does not
  report "on".

### The login start: `--login`

`--login` is read once, in `OnLaunched`, as an exact token. It changes two
things and nothing else:

```
  claim    ownership only if the data root is free (TryAcquire) - never a handoff
           held                -> exit 0, silently; the running Noto is left as it is
           elevated or denied  -> exit 3, silently (ADR-007 §8)
           no message box at sign-in
  window   created, docked, coordinated and wired to the tray - never activated
```

**Measured (#94 Phase 0):**
- **Nothing is ever presented.** Across five `--login` starts, a desktop-wide
  watcher of window-show events saw no Noto window shown or uncloaked, and a
  foreground sampler never saw Noto in front.
- **The first show is correct.** The first hotkey, tray click or second launch
  shows the workspace docked, in front and focused, with its folder list
  loaded.
- **A later restore re-docks** exactly at the docked rectangle.

**Why no handoff.** A handoff would show the running workspace, and a sign-in
start must never show anything. `--login` is not forwarded either: the A17
envelope still carries only `Activate`.

**Anyone can pass `--login`.** It grants nothing: it can only start Noto hidden
or make that process exit.

### Single instance (ADR-013)

- **Login start first, user launch second:** the user's launch hands off as
  always, and the workspace shows.
- **User launch first, login start second:** the login start exits; nothing
  changes.
- **Either way,** one owner, and the first visible window comes from the user.

### Failures

| Case | Result |
|---|---|
| The taskbar is not ready | the tray retries on `TaskbarCreated` (ADR-014); the hotkey works meanwhile |
| The tray and the hotkey both fail | Noto runs unseen; a second launch shows it |
| The executable moved after registering | Windows fails silently at sign-in; Noto reads OFF; turning it on again re-points it |
| A registry access failure | the state reads OFF; nothing is reported falsely |
| A crash during the hidden start | silent, as #10 already accepts |

---

## Rationale

- **The OS is the only honest record.** A stored copy would disagree with
  Windows as soon as Task Manager or a cleaner acted. Noto would then lie, or
  re-add what the user removed. `StartupTask` keeps its state in the OS too, so
  the later move keeps the model.
- **Never override the user's Windows choice.** Startup apps is where Windows
  users manage this; an app that silently re-enables itself there is
  misbehaving.
- **Hidden, not briefly shown.** The workspace is a drawer the user summons; a
  sign-in that flashes it is the opposite.

## Consequences

### Positive

- Noto can be ready at sign-in with nothing on screen, from the tray, without
  administrator rights.
- Its state is always Windows' state.

### Negative

- **A real sign-in is not validated end to end.** It cannot be driven from
  inside a session. The validation starts Noto from the shell exactly as Explorer
  starts a `Run` entry, and the registration is validated by one round trip on
  the real key, restored afterwards.
- **The `StartupApproved` format is undocumented.** An unrecognised form reads
  as disabled, so Noto under-claims rather than over-claims.
- **The registration follows the copy that turned it on.** Other copies read
  OFF.
- **Only the default data root starts at sign-in.**

## Future reconsideration criteria

- **The sparse package lands (ADR-008):** move to `StartupTask` and remove the
  `Run` value, once, on the first packaged run.
- **Windows documents or changes the Startup apps format:** revisit the reading.
- **The settings surface arrives (J4):** it shows the same three states, read
  the same way.
