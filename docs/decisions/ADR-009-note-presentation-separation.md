# ADR-009 — A note is not a window: separating domain from presentation

**Status:** Accepted
**Date:** 2026-09-14
**Related:** ADR-005 (Proposed), ADR-006 (Proposed), ADR-007

---

## Context

Noto shows notes in several ways, and will add more:

```
  now (parity)            later
  ─────────────           ─────────────────────
  sidebar list            floating desktop note
  sidebar editor          contextual note
                          overlay, capture preview, ...
```

The tempting model — and the one the initial data model drifted toward — treats
these as *kinds of note*. The schema had a `FloatingWindows` table keyed on
`NoteId`, which quietly says "a note is either floating or it is not."

That model fails on the first real requirement:

> The same note should be visible in the sidebar **and** floating on the
> desktop **and**, later, bound to an application — at the same time.

Under a kind-based model, satisfying that means either duplicating the note or
rewriting the domain. Both are the wrong answer, and both get more expensive
every week.

There is no implementation yet, so this is free to fix now.

---

## Problem

How does a single note support multiple simultaneous presentations, including
presentations that do not exist yet, without the domain model changing?

---

## Options considered

### Option A — Presentation as a note property

`Note.DisplayMode = Sidebar | Floating | Contextual`.

Simple, and wrong. It is single-valued, so a note cannot be in two places. Every
new presentation edits the domain. Rejected.

### Option B — Subtypes

`FloatingNote : Note`, `ContextualNote : Note`.

Worse. Presentation changes become type changes, a note cannot hold two
presentations at once, and persistence gets a discriminator it should not need.

### Option C — Presentation as a separate concept, referencing the note

A note knows nothing about how it is shown. Presentations are their own
records, each referencing a note. A note may have zero, one, or many.

### Option D — A general-purpose entity-component system

Maximum flexibility, far beyond what a notes application needs. Rejected as
overengineering (principle 9).

---

## Decision

**Option C. The domain owns notes. Presentation is a separate concern that
references notes, never the reverse.**

```
                        ┌──────────────────┐
                        │       NOTE       │   domain
                        │                  │
                        │  content         │   knows nothing about
                        │  metadata        │   windows, screens,
                        │  organization    │   position or z-order
                        │  attachments     │
                        └────────┬─────────┘
                                 │  referenced by
             ┌───────────────────┼───────────────────┐
             │                   │                   │
      ┌──────▼──────┐   ┌────────▼───────┐  ┌────────▼────────┐
      │  SIDEBAR    │   │   FLOATING     │  │  CONTEXTUAL     │
      │             │   │                │  │   (M6, later)   │
      │ list order  │   │ x, y, w, h     │  │  binding        │
      │ selection   │   │ monitor        │  │  visibility     │
      │ scroll      │   │ opacity, lock  │  │  rule           │
      └─────────────┘   └────────────────┘  └─────────────────┘

        zero or more presentations, simultaneously, per note
```

### The rule

> **`Noto.Core` contains no type whose name or field refers to a window, a
> screen, a monitor, a coordinate, a z-order, or a presentation surface.**

This is mechanically checkable, and it is enforced by an architecture test in
`Noto.Core.Tests` — not by good intentions.

### Consequences for the data model

`FloatingWindows` (keyed on `NoteId`, implying one floating state per note) is
replaced by a general presentation table:

```sql
CREATE TABLE NotePresentations (
    Id         INTEGER PRIMARY KEY,
    NoteId     INTEGER NOT NULL REFERENCES Notes(Id) ON DELETE CASCADE,
    Kind       TEXT    NOT NULL,   -- 'floating' | 'contextual' | ...
    State      TEXT    NOT NULL,   -- JSON, shape defined per kind
    CreatedAt  TEXT    NOT NULL,
    UpdatedAt  TEXT    NOT NULL
);

CREATE INDEX IX_Presentations_Note ON NotePresentations(NoteId);
CREATE INDEX IX_Presentations_Kind ON NotePresentations(Kind);
```

Three deliberate choices:

- **No `UNIQUE(NoteId, Kind)`.** Two floating windows of the same note on two
  monitors is a legitimate thing to want.
- **`State` is JSON**, because each presentation kind has a different shape and
  a table per kind would mean a migration per presentation. Presentation state
  is read by exactly one component and never queried across kinds, so the usual
  argument against JSON columns does not apply. Contrast `ContextBindings`,
  where `AppIdentity` *is* queried and therefore *is* a column.
- **Sidebar is not stored here.** Sidebar membership is not a presentation
  record; every note is in the sidebar. Its ordering lives on the note as
  `SortOrder`, which is organization, not presentation.

### Where the boundary sits

```
  Noto.Core            Note, Folder, Tag, Attachment, Task
                       services, commands, queries
                       ── no windows, no coordinates ──

  Noto.Presentation    PresentationService
                       sidebar / floating / (later) contextual view models
                       translates domain <-> visual state

  Noto.Windows         WindowCoordinator
                       actual HWNDs, positioning, DPI, interop
```

`Noto.Presentation` is the only layer that knows a note can be *shown*.
`Noto.Windows` is the only layer that knows what a window *is*.

---

## Rationale

**It is the difference between adding a feature and rewriting a model.** With
this separation, floating notes are a new presentation kind plus a view —
the domain is untouched. Contextual notes in M6 are another. Without it, each
is a schema change and a domain change.

**It makes the deferral of context safe.** The clarified strategy puts
contextual notes at M6, after parity and hardening. That deferral is only
safe if the core does not have to change to accept them later. This ADR is
what makes the deferral safe rather than merely optimistic.

**It is checkable.** "Domain must not depend on presentation" is an
aspiration until a test fails the build when `Noto.Core` gains a reference to
`Noto.Windows` or declares a type with a coordinate on it.

**It costs almost nothing now.** One extra table and one project boundary,
decided before any code exists.

---

## Consequences

### Positive

- one note, many simultaneous presentations
- new presentation kinds need no domain change
- the domain is testable with no UI and no desktop session
- M6 contextual notes become additive
- the framework decision stays reversible: if ADR-001's gate fails and Noto
  moves to WPF, `Core` and `Presentation` survive and only the view layer is
  rewritten

### Negative

- one more indirection between a note and its window
- presentation `State` JSON is not queryable, which is accepted because it is
  never queried
- the architecture test must be written and maintained
- a contributor who wants to "just add a field to Note" will occasionally be
  told no

### Testing requirements

- [ ] **Architecture test: `Noto.Core` has no reference to `Noto.Windows`,
      `Noto.Presentation`, or any UI framework assembly**
- [ ] **Architecture test: no `Noto.Core` type exposes a coordinate, size,
      monitor or window handle**
- [ ] A note with sidebar plus two floating presentations round-trips
- [ ] Deleting a note cascades its presentations
- [ ] Deleting a presentation leaves the note intact

---

## Alternatives rejected

| Option | Why |
| ------ | --- |
| **Presentation as a note property (A)** | Single-valued. Cannot express "sidebar and floating at once", which is a real requirement. |
| **Subtypes (B)** | Presentation changes become type changes; a note cannot hold two presentations. |
| **Entity-component system (D)** | Far more machinery than a notes application justifies. |

---

## Future reconsideration criteria

Revisit if:

- presentation state genuinely needs cross-kind querying, which would justify
  promoting fields out of JSON into columns
- a presentation kind appears that cannot be expressed as "a reference to a note
  plus some state"

Do **not** revisit to add a display mode to `Note` because it would be more
convenient in one view model. That convenience is the exact trade this ADR
exists to refuse.
