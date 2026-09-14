# Architecture Decision Records

This directory records **why** Noto is built the way it is.

Noto is developed with heavy AI assistance. The characteristic failure of that
approach is architectural drift: the application works, the tests pass, and
nobody can explain why anything is shaped the way it is. Changing it then
becomes archaeology.

These records are the defense against that.

---

## Index

| ADR | Title | Status |
| --- | ----- | ------ |
| [ADR-001](ADR-001-native-windows-stack.md) | Native Windows stack: C# + WinUI 3 + Win32 interop | Accepted, **provisional** |
| [ADR-002](ADR-002-local-first-storage.md) | Local-first, no account, no backend | Accepted |
| [ADR-003](ADR-003-sqlite-data-access.md) | SQLite via Microsoft.Data.Sqlite, not EF Core | Accepted |
| [ADR-004](ADR-004-markdown-content.md) | Markdown as the note content format | Accepted |
| [ADR-005](ADR-005-context-engine.md) | Context engine: deterministic resolution | **Proposed** — deferred to M6 |
| [ADR-006](ADR-006-window-binding-identity.md) | Window binding and durable identity | **Proposed** — deferred to M6 |
| [ADR-007](ADR-007-window-presentation.md) | Window presentation: opacity, click-through, edge docking | Accepted |
| [ADR-008](ADR-008-packaging-and-identity.md) | Packaging: sparse package, self-contained, Velopack | Accepted |
| [ADR-009](ADR-009-note-presentation-separation.md) | A note is not a window: domain/presentation separation | Accepted |
| [ADR-010](ADR-010-commands-and-events.md) | Commands and events, sized for a desktop app | Accepted |
| [ADR-011](ADR-011-design-system.md) | A token-based design system, defined before the screens | Accepted |

### Why two are Proposed

ADR-005 and ADR-006 design the context engine. The clarified product strategy
makes **SideNotes parity** the first product and places contextual notes at
**M6**, after parity and hardening.

Deciding the mechanism of durable window identity before a note can be created
is deciding in the wrong order, and the decision would be stale by the time it
is used. Both are kept — the research is sound and they are the starting point
for M6 — but they are **not** allowed to shape the core domain model before
then.

[ADR-009](ADR-009-note-presentation-separation.md) is what makes that deferral
safe: contextual notes arrive as a new presentation kind, additive to a domain
that does not change to accommodate them.

--- | ----- | ------ |
| [ADR-001](ADR-001-native-windows-stack.md) | Native Windows stack: C# + WinUI 3 + Win32 interop | Accepted, **provisional** |
| [ADR-002](ADR-002-local-first-storage.md) | Local-first, no account, no backend | Accepted |
| [ADR-003](ADR-003-sqlite-data-access.md) | SQLite via Microsoft.Data.Sqlite, not EF Core | Accepted |
| [ADR-004](ADR-004-markdown-content.md) | Markdown as the note content format | Accepted |
| [ADR-005](ADR-005-context-engine.md) | Context engine: deterministic resolution, layered sources | Accepted |
| [ADR-006](ADR-006-window-binding-identity.md) | Window binding and durable identity | Accepted |
| [ADR-007](ADR-007-window-presentation.md) | Window presentation: opacity, click-through, edge docking | Accepted |
| [ADR-008](ADR-008-packaging-and-identity.md) | Packaging: sparse package, self-contained, Velopack | Accepted |

---

## Status values

| Status | Meaning |
| ------ | ------- |
| **Proposed** | Under discussion, not yet decided |
| **Accepted** | Decided and in effect |
| **Provisional** | Accepted, but with an explicit validation gate that could reverse it |
| **Superseded** | Replaced by a later ADR, which is named in the record |
| **Deprecated** | No longer applies, and not replaced |

A superseded ADR is **never deleted**. The reasoning that led to a wrong turn is
as valuable as the correction.

---

## When an ADR is required

Write one when a change:

- introduces or replaces a framework, runtime or major dependency
- changes how data is stored, queried or migrated
- changes the threading, process or window model
- changes a security or privacy boundary
- changes a public data format or on-disk layout
- reverses a decision in an existing ADR

Do **not** write one for ordinary implementation choices. An ADR for every
decision is as useless as none.

---

## When a change contradicts an existing ADR

1. **Identify** the ADR it contradicts.
2. **Explain** what changed: new information, a new constraint, or the original
   reasoning was wrong.
3. **Compare** the alternatives again, with what is known now.
4. **Update** the ADR — supersede it, do not delete it.
5. **Then** implement.

> "The AI suggested it" is not a rationale. Neither is "this was faster to
> generate." If an assistant proposes something that contradicts a record here,
> the record is reviewed first.

---

## Format

```markdown
# ADR-00X — Title

**Status:**
**Date:**
**Supersedes / Superseded by:**

## Context
## Problem
## Options considered
## Decision
## Rationale
## Consequences
## Alternatives rejected
## Future reconsideration criteria
```

The last section matters most. An ADR that cannot be revisited is dogma.
