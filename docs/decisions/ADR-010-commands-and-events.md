# ADR-010 — Commands and events, sized for a desktop application

**Status:** Accepted
**Date:** 2026-09-14

---

## Context

Every mutation in Noto will eventually be reachable from several places:

```
  sidebar button ─┐
  keyboard shortcut ─┤
  global hotkey ─────┼──> "create a note"
  context menu ──────┤
  tray menu ─────────┤
  URL scheme ────────┘   (SideNotes has one — this is parity, not speculation)
```

If each entry point calls the repository directly, the validation, the
auto-save interaction, the search-index update and the undo entry are
duplicated five ways and drift apart. The bug then appears in one entry point
only, which is the worst kind to diagnose.

The parity specification includes a URL scheme and automation-equivalent
surfaces, so this is a requirement of the **first** product, not preparation
for a hypothetical one.

---

## Problem

How do multiple entry points share one implementation of an operation, and how
do independent concerns react to changes, without importing machinery a local
desktop application does not need?

---

## Options considered

| Option | Assessment |
| ------ | ---------- |
| **A. Direct service calls** | Simplest. No indirection. But no single place to add undo, logging or index maintenance — each becomes a cross-cutting edit. |
| **B. Plain command objects with handlers** | One type per operation, one handler, one place for cross-cutting behavior. Small. |
| **C. A mediator library (MediatR or similar)** | Option B plus a dependency, reflection-based dispatch and startup cost against a sub-second budget. |
| **D. CQRS with event sourcing** | Wildly disproportionate. Rejected on sight. |

---

## Decision

**Option B: plain command objects with explicit handlers, plus a minimal
in-process event aggregator. No mediator library, no event sourcing, no bus.**

### Commands

A command is a record describing an intent. A handler executes it.

```
  CreateNote        UpdateNoteContent   DeleteNote      RestoreNote
  MoveNote          ReorderNote         PinNote         UnpinNote
  SetNoteColor      ArchiveNote         FoldNote
  CreateFolder      RenameFolder        MoveFolder      DeleteFolder
  AddAttachment     RemoveAttachment
  ToggleTask
```

Rules:

- **every mutation goes through a command.** No entry point calls a repository
  to change state.
- **queries do not.** Reading is a direct service or repository call. Wrapping
  reads in command ceremony buys nothing.
- **dispatch is explicit** — a registry mapping command type to handler, wired
  in the composition root. No reflection scanning, no startup cost.

This gives one place to add, uniformly: validation, undo/redo, the `UpdatedAt`
stamp, and diagnostics.

### Events

Commands raise events after a successful mutation. Subscribers react.

```
  NoteCreated      NoteUpdated      NoteDeleted     NoteRestored
  NoteMoved        NotePinned       NoteColorChanged
  FolderCreated    FolderRenamed    FolderMoved     FolderDeleted
  AttachmentAdded  AttachmentRemoved
  TaskToggled
```

```
  NoteUpdated
      ├──> UI refresh
      ├──> search index (though FTS5 triggers handle the primary path)
      └──> later: backup, sync, context re-evaluation
```

Rules:

- **in-process, synchronous by default.** One application, one machine.
- **events are facts, not requests.** `NoteUpdated`, never `UpdateNotePlease`.
- **subscribers never mutate domain state.** A subscriber that needs a change
  dispatches a command; events that cause events cause loops.
- **events are not the persistence mechanism.** The database is. Events notify;
  they do not store.

### Undo

Deliberately **not** designed here. Commands make undo *possible* later by
giving one interception point. Implementing undo before notes exist would be
designing against an imagined UI. It is a parity question, resolved in M3.

---

## Rationale

**It is required for parity, not speculation.** SideNotes exposes a URL scheme
and automation. Those are additional entry points to the same operations, which
is exactly what commands solve.

**It is the smallest thing that solves the problem.** A record, a handler
interface, and a dictionary. No dependency, no reflection, no startup cost —
which matters against principle 3's budget.

**It keeps the boundary honest.** "Every mutation is a command" is a rule a
reviewer can check by looking for repository writes outside handlers.

**Why not a mediator library:** it would add a dependency and assembly scanning
to save writing a dictionary. Principle 9 forbids dependencies that do not earn
their place, and startup cost is a hard budget here.

**Why not direct service calls:** they work until the second entry point, and
Noto has five on the parity list.

---

## Consequences

### Positive

- one implementation per operation regardless of entry point
- a single interception point for undo, validation and diagnostics
- handlers are pure-ish and unit-testable without a UI
- future surfaces (CLI, protocol handler, automation) reuse existing handlers
- no dependency, no measurable startup cost

### Negative

- one type per operation — more files
- a small amount of ceremony for trivial operations
- the "no repository writes outside handlers" rule needs review discipline
- an event aggregator, however small, can be misused as a bus if unwatched

### Explicit non-goals

- no mediator or messaging library
- no event sourcing, no event store, no replay
- no async message queue
- no distributed anything
- no cross-process communication

### Testing requirements

- [ ] Each handler unit-tested without UI or platform dependencies
- [ ] Events raised only after the mutation succeeds, never on failure
- [ ] An event subscriber that throws does not fail the command
- [ ] No repository write path exists outside a command handler

---

## Future reconsideration criteria

Revisit if:

- handler registration becomes large enough that explicit wiring is a genuine
  burden — which is a threshold to measure, not assume
- an operation genuinely needs to be asynchronous or queued
- undo requirements turn out to need more than one interception point

Do **not** adopt a mediator library because the explicit registry looks
manual. That is the trade, made deliberately.
