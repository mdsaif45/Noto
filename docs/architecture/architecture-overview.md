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
  |                   Noto.Windows                            |
  |   WinUI 3 app: startup, composition root, XAML, views     |
  +----------------------------------------------------------+
  |                   Noto.UseCases                           |
  |   commands, handlers, events, queries (ADR-010)           |
  +----------------------------------------------------------+
  |                     Noto.Core                             |
  |   Note  Folder  Tag  Attachment  Task  (M1)               |
  |   domain rules and services                               |
  |                                                           |
  |   NO Win32. NO SQL. NO XAML.                              |
  |   NO windows, coordinates, monitors or z-order.           |
  +-------------------------+--------------------------------+
              |                              |
  +-----------v-----------+    +-------------v----------------+
  |  Noto.Infrastructure  |    |   Noto.Platform.Windows      |
  |                       |    |                              |
  |  SQLite, migrations   |    |  P/Invoke, window management |
  |  repositories, FTS5   |    |  hotkeys, tray, clipboard    |
  |  files, settings, log |    |  DPI, monitors, capture      |
  +-----------------------+    +------------------------------+
              |                              |
        +-----v------+              +--------v--------+
        |  SQLite    |              |  Windows APIs   |
        |  + files   |              |                 |
        +------------+              +-----------------+
```

### The two rules

**1. `Noto.Core` depends on nothing platform-specific.**
It defines interfaces; `Infrastructure` and `Windows` implement them.
Dependencies point inward.

**2. `Noto.Core` contains no type that refers to a window, screen, monitor,
coordinate, z-order or presentation surface.** (ADR-009)

A note is not a sidebar note, a floating note or a contextual note. It is a
note, and it may be *presented* in any of those ways — several at once.

Both rules are enforced by architecture tests, not by good intentions.

This is not abstraction for its own sake — principle 9 forbids that. It buys
three specific things:

1. **Testability where it matters.** Domain logic is unit-testable with no UI
   and no desktop session.
2. **A reversible framework decision.** ADR-001 is provisional. If the gate
   fails and Noto moves to WPF, `Core`, `Presentation` and `Infrastructure`
   survive; only `UI` is rewritten.
3. **Additive expansion.** Floating notes (M3) and contextual notes (M6) are
   new presentation kinds, not domain changes. This is what makes deferring
   context to M6 safe rather than merely optimistic.

### What this is not

There is no repository interface per entity, no service interface per service,
no mediator, no event bus. Abstraction appears where there is a second
implementation or a test seam that earns it — nowhere else.

---

## Projects

| Project | Contains | Depends on |
| ------- | -------- | ---------- |
| `Noto.UseCases` | Commands, handlers, events, queries (ADR-010) | Core |
| `Noto.Core` | Domain model and services. Targets plain `net9.0`, so a Windows dependency is impossible rather than merely discouraged | — |
| `Noto.Infrastructure` | SQLite, repositories, migrations, FTS5, files, settings, logging | Core |
| `Noto.Platform.Windows` | All Win32 interop: window management, hotkeys, clipboard, capture, DPI, monitors | Core |
| `Noto.Windows` | The WinUI 3 application: startup, composition root, XAML, views | UseCases, Infrastructure, Platform.Windows |
| `Noto.Core.Tests` | Domain, command and **architecture boundary** tests | Core |
| `Noto.UseCases.Tests` | Use-case and boundary tests | UseCases |
| `Noto.Infrastructure.Tests` | Storage, migration, search tests | Infrastructure |

**The layer is named `Noto.UseCases`, not `Noto.Application`.** The latter
shadows `Microsoft.UI.Xaml.Application` and forces fully-qualified names
throughout the WinUI project — discovered while building the solution, not
theorised.

A separate `Noto.Presentation` project is **not** created yet. ADR-009's
boundary is currently held by `Noto.Core` targeting plain `net9.0` and by the
architecture tests. Presentation view models arrive with the workspace in M2;
creating an empty project for them now would be a directory for appearance.

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

## Extension boundary: contextual notes (M6)

Contextual notes are **milestone M6** — after SideNotes parity and hardening.
No context code is written before then.

What exists now is the *boundary* that makes adding it additive:

```
  M6 adds:                              M6 does NOT change:

  Noto.Windows                          Note
    ContextObserver                     Folder
    (foreground / window events)        Tag
         |                              Attachment
         v                              the commands
  Noto.Core                             the events
    ContextResolver  (pure)             the schema for any of them
    BindingMatcher   (pure)
         |
         v
  Noto.Presentation
    a new presentation kind:
    'contextual'  -> NotePresentations
```

Contextual notes arrive as **a new value in `NotePresentations.Kind`** plus a
resolver. The `Note` type does not gain a field. That is the entire point of
ADR-009.

The design itself is recorded in [ADR-005](../decisions/ADR-005-context-engine.md)
and [ADR-006](../decisions/ADR-006-window-binding-identity.md), both held at
**Proposed** and to be re-validated when M6 begins.

---

## Commands and events

Every mutation goes through a command (ADR-010):

```
  sidebar  ─┐
  shortcut ─┤
  hotkey   ─┼─> CreateNote ─> handler ─> repository ─> NoteCreated event
  menu     ─┤                                              │
  URL      ─┘                                    ┌─────────┴────────┐
                                                 v                  v
                                            UI refresh        later: backup,
                                                              sync, context
```

Reads do not go through commands — wrapping queries in command ceremony buys
nothing. Dispatch is an explicit registry, not reflection, because startup time
is a hard budget.

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
