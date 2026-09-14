# Architecture Overview

**Status:** Draft
**Last updated:** 2026-09-14
**Decisions:** [ADR-001](../decisions/ADR-001-native-windows-stack.md),
[ADR-003](../decisions/ADR-003-sqlite-data-access.md),
[ADR-005](../decisions/ADR-005-context-engine.md)

---

## Shape

Noto is a **single-process desktop application**. No services, no background
daemon, no IPC, no microservices. One process, several windows.

```
  +----------------------------------------------------------+
  |                     Noto.App                              |
  |         composition root, lifetime, DI wiring             |
  +----------------------------------------------------------+
  |                     Noto.UI                               |
  |   sidebar   note editor   floating notes   settings       |
  |   ViewModels, XAML, navigation                            |
  +----------------------------------------------------------+
  |                     Noto.Core                             |
  |   domain model      note service      context resolver    |
  |   search service    binding matcher   visibility policy   |
  |                                                           |
  |   NO Win32. NO SQL. NO XAML. Pure, testable.              |
  +-------------------------+--------------------------------+
              |                              |
  +-----------v-----------+    +-------------v----------------+
  |  Noto.Infrastructure  |    |       Noto.Windows           |
  |                       |    |                              |
  |  SQLite, migrations   |    |  P/Invoke, WinEvent hooks    |
  |  repositories, FTS5   |    |  window presentation         |
  |  file & attachments   |    |  hotkeys, tray, capture      |
  |  settings, logging    |    |  DPI, monitors               |
  +-----------------------+    +------------------------------+
              |                              |
        +-----v------+              +--------v--------+
        |  SQLite    |              |  Windows APIs   |
        |  + files   |              |                 |
        +------------+              +-----------------+
```

### The one rule

**`Noto.Core` depends on nothing platform-specific.**

It defines interfaces; `Infrastructure` and `Windows` implement them.
Dependencies point inward.

This is not architecture for its own sake — principle 9 forbids that. It buys
two specific things:

1. **Testability where it matters.** Context resolution and binding matching
   are the logic most likely to be wrong and hardest to debug in situ. Keeping
   them free of OS dependencies makes them unit-testable without a desktop
   session.
2. **A reversible framework decision.** ADR-001 is provisional. If the
   validation gate fails and Noto moves to WPF, `Core` and `Infrastructure` are
   untouched; `UI` is rewritten and `Windows` largely survives.

### What this is not

There is no repository interface per entity, no service interface per service,
no mediator, no event bus. Abstraction appears where there is a second
implementation or a test seam that earns it — nowhere else.

---

## Projects

| Project | Contains | Depends on |
| ------- | -------- | ---------- |
| `Noto.App` | Entry point, composition root, app lifetime | all |
| `Noto.UI` | Views, ViewModels, XAML, navigation | Core |
| `Noto.Core` | Domain model, services, context logic | — |
| `Noto.Infrastructure` | SQLite, repositories, migrations, FTS5, files, settings, logging | Core |
| `Noto.Windows` | All Win32 interop, window presentation, hooks, hotkeys, tray, capture | Core |
| `Noto.Core.Tests` | Unit tests | Core |
| `Noto.Infrastructure.Tests` | Storage, migration, search tests | Infrastructure |
| `Noto.Windows.Tests` | Interop tests requiring a desktop session | Windows |

`Noto.Sync` is **not created yet**. ADR-002 defers sync past v1, and empty
directories for appearance are forbidden.

---

## The four surfaces

One note store, four ways a note can be present:

```
                    +----------------------+
                    |     note store       |
                    +----------+-----------+
                               |
     +-----------+-------------+-------------+-----------+
     |           |                           |           |
  SIDEBAR     FLOATING                  CONTEXTUAL    CAPTURE
  docked      independent               shown with    transient
  topmost     desktop window            its context   input window
  window                                
     |           |                           |           |
     +-----------+-------------+-------------+-----------+
                               |
                    +----------v-----------+
                    |  WindowCoordinator   |
                    |  owns every window,  |
                    |  its position, and   |
                    |  its lifetime        |
                    +----------------------+
```

**`WindowCoordinator` is the only component that creates, positions or destroys
windows.** Everything else requests. This is deliberate: window lifetime bugs
in a multi-window always-on-top application are miserable to diagnose, and a
single owner makes them tractable.

---

## Context engine

The differentiator, detailed in [context-engine.md](context-engine.md) and
decided in ADR-005.

```
  Windows                Noto.Windows           Noto.Core
  -------                ------------           ---------

  EVENT_SYSTEM_      ->  ContextObserver   ->   ContextResolver
  FOREGROUND             debounce,              pure function
                         scoped hooks,          signal -> identity
  EVENT_OBJECT_          never global           + confidence
  LOCATIONCHANGE                                      |
  (scoped to bound                                    v
   window only)                                 BindingMatcher
                                                pure function
                                                identity -> notes
                                                      |
                                                      v
                                                VisibilityPolicy
                                                      |
  WindowCoordinator  <----------------------------- show / hide
```

Three properties worth stating here because they constrain everything:

- **Observation never polls.** Foreground changes are events.
- **Hooks are never global.** `EVENT_OBJECT_LOCATIONCHANGE` is scoped to the
  `(pid, tid)` of a specifically bound window. A global hook of that type is
  the largest available performance mistake.
- **Resolution is pure.** `ContextResolver` and `BindingMatcher` take a signal
  record and return a result. No OS calls, fully unit-testable.

---

## Data flow: creating a note

```
  user types in editor
        |
        v
  NoteViewModel          debounced, ~300ms
        |
        v
  NoteService            validation, domain rules
        |
        v
  NoteRepository         Dapper, parameterised SQL
        |
        v
  SQLite                 UPDATE Notes ...
        |
        +--> AFTER UPDATE trigger --> NotesFts reindexed
```

Search index maintenance is a database trigger, not application code. The index
cannot drift from the content, because nothing can update one without the
other (ADR-003).

---

## Threading

```
  UI thread          XAML, ViewModels, all UI mutation
  thread pool        database I/O, file I/O, search
  WinEvent callback  OS-owned; marshal to UI immediately, do nothing here
```

Two rules that prevent the characteristic failures of this application class:

1. **WinEvent callbacks do no work.** They capture a signal record and marshal
   it. Work in the callback blocks the OS event pump.
2. **Delegates passed to Win32 are rooted in fields.** A collected
   `WinEventDelegate` or `SUBCLASSPROC` causes an access violation on the next
   callback — a crash whose cause is nowhere near its symptom. Microsoft's own
   documentation recommends `GCHandle`.

---

## Startup

Principle 3 allows under one second, cold. The work is staged accordingly:

```
  PHASE 1  minimal            open DB, load settings, register hotkey,
           (must be fast)     create tray icon
                              -> Noto is now responsive to its hotkey

  PHASE 2  deferred           window creation, note list, context observer
           (after first       start, floating note restore
            frame or first
            invocation)
```

The sidebar window is not created until it is first needed. The user's
perception of startup is "the hotkey works", not "everything is loaded".

---

## Error handling

Noto sits in the background on someone's work machine. It must fail quietly and
recover.

| Failure | Response |
| ------- | -------- |
| Database unreadable | Refuse to start rather than risk the data; clear message pointing at the file |
| Migration fails | Roll back, keep the old database, report; **never** leave a half-migrated store |
| A window fails to create | Log, continue; do not take the application down |
| Context resolution throws | Log, treat as "no context", show nothing |
| Hotkey registration conflicts | Report in settings; the application still works |
| Unhandled exception | Log, attempt to save open notes, restart cleanly |

**Logs never contain note content or user file paths** (principle 10).

---

## Performance budgets

From principle 3. These are enforced, not aspirational.

| Budget | Target | How it is held |
| ------ | ------ | -------------- |
| Cold start to usable | < 1s | staged startup; no EF Core (ADR-003) |
| Sidebar open | instant | window pre-created after first use, then reused |
| Search | as fast as typing | FTS5, debounced input |
| Idle CPU | ~0 | event-driven; no polling; scoped hooks only |
| Idle memory | small | lazy windows; no full note cache |
| Window follow | no visible lag | coalesced to ~16ms; hidden during drag |

---

## Testing strategy

```
  Noto.Core.Tests             fast, no OS      the bulk of the tests
    context resolution
    binding matching
    visibility rules
    markdown parsing

  Noto.Infrastructure.Tests   real SQLite      correctness of data
    migrations, every upgrade path
    FTS5 sync and search
    backup / restore round trip

  Noto.Windows.Tests          desktop session  narrow, slow, few
    window positioning
    DPI arithmetic
    hotkey registration

  Manual                      documented       anything visual
    mixed-DPI multi-monitor
    monitor disconnect
    Win10 22H2 and Win11 24H2
```

The mixed-DPI multi-monitor case is mandatory, not optional: research
identified it as the dominant recurring defect theme in comparable software.

---

## Open questions

| Question | Resolved by |
| -------- | ----------- |
| Does WinUI 3 survive the validation gate? | ADR-001 spikes, in M0 |
| Markdown renderer choice | M1, must be small and maintained |
| Where the visibility policy lives when several bindings match at once | M5 |
| Attachment storage layout on disk | M1, blocks backup/export |
