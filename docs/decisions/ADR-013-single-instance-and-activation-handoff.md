# ADR-013 — Single instance and activation handoff

**Status:** Accepted
**Date:** 2026-10-05
**Related:** ADR-007 §4 (show and hide), §7 (virtual desktops), §8 (never run elevated); ADR-008 (packaging); A17 (sidenotes-parity)

---

## Context

Before this decision, launching Noto while it was running started a second
process. That process:

- opened the same database;
- created a second workspace window;
- lost the global hotkey to the first one, silently.

The engine contract assumes one user and one process (contract §10), so a second
process on the same database is a defect, not a feature.

SideNotes parity row A17 asks for the opposite: a second launch brings the
running Noto forward.

## Problem

1. Which process owns a data root?
2. How does a second launch hand its request to the owner?
3. What does the owner do with it?

All three must work from the very first instant of startup until the last
instant of shutdown, and must survive the owner crashing.

---

## Options considered

| Option | Verdict |
|---|---|
| **Named mutex only** | Cannot activate the owner or carry a request. Insufficient alone. |
| **Named mutex + named pipe** | **Chosen.** Ownership is the mutex's own. The pipe carries one request, gets a reply, and tells "accepted" apart from "shutting down". |
| Mutex + message-only window (`WM_COPYDATA`) | Ties the handoff to the UI thread, so a hung owner and a dead one look the same. Anyone on the desktop at equal or higher integrity can send to it. |
| Named event | No payload and no reply. |
| Windows App SDK `AppInstance` | Its key is the **executable path**, measured even with package identity, so it cannot guard one data root against two builds. Redirection is fire-and-forget: no reply, so the shutdown race cannot be closed. Rejected; it was the earlier session preference, revisited at the A17 design gate. |
| COM local server / DDE | Registration and complexity far beyond the need. |

---

## Decision

### Identity

```
  key  = current user's SID  +  SHA-256( normalise( GetFinalPathNameByHandle(data root) ) )[0..32 hex]
  mutex: Global\Noto.Instance.<SID>.<hash>
  pipe:  \\.\pipe\Noto.Instance.<SID>.<hash>
```

- **Keyed by the data root, not the executable.** The rule is one database, one
  process, whichever build launched it.
- **Normalised.** Junctions, symbolic links, `subst` drives and short names are
  resolved by Windows. The path is then normalised: no `\\?\` prefix, no
  trailing separator, invariant upper case.
- **Hashed.** The hash keeps the folder path out of the object namespace.
- Code: `InstanceKey`.

### Startup order (`App.OnLaunched`)

```
  1 resolve data root          4 take mutex, no wait (InstanceOwnership.Claim)
  2 EnsureCreated (folders)        owner  -> 5 start pipe server, then database, settings, hotkey, window
  3 compute key                    second -> hand off, exit: no database, no window
```

- The ownership decision is made **before** the database is opened.
- The pipe exists **before** the window.
- A request that arrives during startup is posted to the UI thread's
  dispatcher. That dispatcher runs only after `OnLaunched` returns, when the
  coordinator exists.

### The claim (second launch)

```
  mutex free / abandoned ----------------------------> owner
  held -> pipe: Accepted ----------------------------> exit 0
               ShuttingDown -> wait <= 10 s for mutex -> owner (took over)  | else exit 2
               Rejected / other user / other session -> message box, exit 3
               no answer -> retry mutex + pipe every 100 ms for 5 s -> message box, exit 2
```

Before sending, the client passes the foreground right to the owner with
`AllowSetForegroundWindow(ownerPid)`.

- The second launch was started by the user, so it holds that right.
- Measured without the call: the owner was refused the foreground in 3 of 3
  trials.

### Protocol, version 1

```
  request: "NOTO" | version (1 B) = 1 | kind (1 B) | payload length (uint16 LE) | UTF-8 payload    max 8 KiB
  kinds:   1 Activate (payload must be empty)   2 Uri (reserved)   3 Cli (reserved)
  reply:   0 Accepted   1 ShuttingDown   2 Rejected
```

The decoder rejects every one of these:

- a bad magic number;
- a version other than 1;
- any kind other than Activate, including the two reserved kinds;
- a length field that does not match the bytes that follow;
- an Activate request with a payload;
- a request larger than 8 KiB.

There is no negotiation: a newer client is rejected and says so.

`Uri` and `Cli` are reserved so that `noto://` links and command-line verbs
need no second channel later. Their payloads are not defined, and they are
rejected until a feature defines them. Code: `ActivationEnvelope`.

### Pipe server

`ActivationPipeServer` serves **one instance, one connection at a time**:

```
  connect -> read one request (2 s) -> validate -> hand on -> write one reply (2 s) -> wait for hang-up (2 s) -> disconnect
```

- **No unbounded waits.** Every wait is bounded. A stalled read or write is
  cancelled with `CancelIoEx`, so one bad client cannot block the next.
- **No queue.** Clients that arrive while another is being served wait on their
  own side, in `WaitNamedPipe`.
- **Handing a request on.** The server stamps the time it was received. It
  offers the request to `PendingLaunch`, which holds at most one request
  waiting for the UI thread. Later requests fold into that one, keeping the
  newest time.

### Activation

There is one path:

```
  pipe -> dispatcher -> WindowCoordinator.OnActivationRequested(Launch, time) -> WorkspaceToggle.Decide(Launch, …)
```

**A launch is not the toggle:**

- A hidden window is shown on the current desktop.
- A minimized window is restored.
- A window behind another app is brought forward.
- A window already shown in front is **left as it is**.

**A launch never hides Noto.** Everything else in ADR-007 §4 applies unchanged:

- the drop rules;
- startup;
- stale requests;
- the virtual-desktop contract.

### Security

The trust boundary is the user: another process running as the same user is
already inside it.

**The pipe:**

- Its DACL is protected and has one entry, the current user, so administrators
  and SYSTEM are not granted access.
- `PIPE_REJECT_REMOTE_CLIENTS` refuses network clients.
- `FILE_FLAG_FIRST_PIPE_INSTANCE` with a single instance means the name cannot
  be joined.
- A name already taken (squatting) is recorded, and the owner runs without a
  handoff endpoint.

**The client:**

- It checks that the pipe's server runs as the same user, in the same Windows
  session (`GetNamedPipeServerProcessId`, the process token, and
  `GetNamedPipeServerSessionId`).
- It does this before anything is sent.
- An unreadable token counts as "not the same user".

**Elevation (ADR-007 §8):**

- An elevated launch never owns. It hands off to a running owner, or, with
  none running, it is refused with a message box.
- An owner it found shutting down also gets a refusal, not a takeover.

**Requests:** v1's one command shows a window, so nothing destructive is
reachable. Future payloads must be validated by their feature against an
allow-list and never executed as text.

### Shutdown

- **A close that goes ahead** (not cancelled by an unsaved-text failure):
  - the pipe answers `ShuttingDown`;
  - the coordinator drops every request.
- **When the window closes:** the pipe is closed first, then the mutex is
  released, so a successor can create the pipe.
- **Residual race:** a request accepted a moment before the close goes ahead is
  dropped. The second launch exits 0 and no Noto remains. This needs a launch
  and a close within milliseconds of each other. It is documented, not
  mitigated.

### Crash recovery

- When the owner dies, its pipe dies with it.
- The mutex is either destroyed (no other handle) or abandoned. Abandoned
  counts as acquired (`Recovered`), so a crash never locks a root.

### Exit codes (second launch)

| Code | Meaning |
|---|---|
| 0 | Handed off (or it took over and ran normally) |
| 2 | An owner holds the root but could not be reached in time. Message box. |
| 3 | Refused: elevated with no owner, another session, another user, a rejected protocol, or an ownership name that cannot be opened. Message box. |

### Diagnostics

`Trace` lines prefixed `Noto.Instance`. They reach `OutputDebugString` in
Release builds.

- **What they record:** each claim (outcome, refusal, took over, recovered),
  the pipe's start status, each launch request's action, and the moments
  shutdown begins and ownership is released.
- **What they never contain:** paths or note content.
- They are not a logging framework (#7). They exist so the runtime harness can
  see what happened.

---

## Rationale

- **The mutex is the only correct ownership primitive.** Acquiring it is
  atomic, so two launches cannot both own. Its abandoned state is how a crash
  is recovered.
- **The pipe is the smallest channel that can reply.** The reply is what makes
  shutdown safe: "accepted" and "shutting down" are different answers. The
  pipe is also served off the UI thread, so a hung UI still answers. The
  request then waits in the dispatcher; P4 decided that no age rule is added.
- **Keying by data root** protects the thing that matters, the database. Two
  builds on one root are routed to one owner rather than refused.

## Consequences

### Positive

- One process per data root, enforced before the database is opened.
- A second launch shows Noto: hidden, minimized, behind another app, or on
  another desktop.
- Crash recovery is immediate.
- The envelope reserves `noto://` and CLI kinds without implementing them.

### Negative

- The handoff and the activation are two hops (pipe → dispatcher). A launch
  arriving while the UI thread is hung waits for it.
- A same-user process can squat the pipe name. The owner detects and records
  this but cannot prevent it. Same-user processes are inside the trust boundary.
- The residual shutdown race above.
- **Not validated:** a second Windows session and another user's process
  against a real Noto, since both need a second account. The client's checks
  are tested with Windows' own system pipes and pure tests.

## Non-goals

- `noto://` handling and command-line verbs: their payloads and validation.
- A generic IPC or command bus.
- The tray, always-on-top, login startup.
- Logging (#7) and crash handling (#10).

## Future reconsideration criteria

- **A protocol handler or CLI verbs arrive:** define the kind's payload, its
  allow-list, and its version.
- **Noto ships as a sparse package (ADR-008):** the mechanism is unchanged,
  because it depends on neither identity nor `AppInstance`. Re-run the runtime
  harness on the packaged build.
- **Multi-session use on one root becomes a requirement:** today another
  session is refused.
