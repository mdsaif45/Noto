# ADR-005 — Context engine: deterministic resolution, layered sources

**Status:** **Proposed** — deferred to M6
**Date:** 2026-09-14
**Related:** ADR-006 (window identity), ADR-002 (local-first)

---

> **Deferred — read this first.** The clarified product strategy makes
> **SideNotes parity** the first product; contextual notes are milestone **M6**,
> after parity and hardening. This ADR is therefore **Proposed, not Accepted**:
> the research behind it is sound and it is the starting point for M6, but it
> must **not** shape the core domain model before then.
>
> [ADR-009](ADR-009-note-presentation-separation.md) is what makes this
> deferral safe — contextual notes arrive as a new presentation kind, additive
> to a domain that does not change to accommodate them.
>
> Re-validate against current research when M6 begins. Decisions made two
> phases early go stale.

## Context

The context engine is Noto's differentiator. It answers one question,
continuously:

> Given what the user is doing right now, which notes should be visible?

Competitive research established the state of the art precisely:

| Product | Binds to | Mechanism |
| ------- | -------- | --------- |
| Notezilla | "webpages, documents, programs, apps, folders, or any window" | **User-authored window-title patterns with `*` wildcards.** CONFIRMED from vendor docs. |
| Zhorn Stickies | applications, websites, documents, folders | Undocumented. INFERRED title-based. |
| @/Anchored | one live OS window | Undisclosed. |
| Noticky (macOS) | an application | Foreground application activation |

And the gap:

```
  app  -------- window -------- document/tab/URL -------- selection
   ^              ^                    ^
   |              |                    |
 Noticky      Anchored            NOBODY
                                  (Anchored's AI exists to guess it)
```

A browser window with 40 tabs is one window. An editor with 12 files open is
one window. Binding at window level means one set of notes for all of it.

---

## Problem

How does Noto determine the user's context, and how does it decide which notes
that context should surface?

Three sub-problems:

1. **What is observed** — what signals are available, at what cost?
2. **How context is resolved** — how do raw signals become a stable identity?
3. **How notes are matched** — how does an identity select notes?

---

## Options considered

### Option A — Window-title pattern matching (the Notezilla model)

User writes `*Google Search*`. Trivial to implement.

Fails principle 5 outright: the user authors and maintains configuration. It is
configuration wearing a context costume. It also breaks silently whenever an
application changes its title format, and produces false positives across
unrelated windows sharing a substring.

### Option B — Bind to the OS window only (the Anchored model)

Deterministic and simple, but coarse. Cannot distinguish tabs or documents.
Requires solving durable window identity (ADR-006).

### Option C — Read window contents with AI and infer context

What @/Anchored's Atlas does. Handles cases nothing else can.

Rejected on three grounds: it is probabilistic where principle 6 demands
determinism; it is expensive against principle 3's budgets; and reading the
content of every window the user focuses is a serious privacy cost for a
product whose entire trust position is local-first (ADR-002). It is a
workaround for not knowing, sold as knowing.

### Option D — Layered deterministic resolution

Resolve the most specific identity available from cheap, reliable signals.
Fall back down the ladder when finer signals are unavailable.

---

## Decision

**Option D: a layered deterministic resolver, with an explicit confidence
floor, and nothing shown below it.**

### The context ladder

Noto resolves the most specific identity it can obtain cheaply and reliably:

```
  SPECIFICITY   SIGNAL                          SOURCE              MVP?
  ----------------------------------------------------------------------
  finest        document / file path            UI Automation        later
                URL (browser address)           UI Automation        later
                window (durable identity)       ADR-006              M5
  coarsest      application (exe / AUMID)       process query        M5
```

**MVP (M5) ships the bottom two rungs only.** Application-level and
window-level binding are achievable with cheap, documented, reliable signals.
The finer rungs are deliberately deferred to their own milestone and ADR —
they are the differentiator, and getting them wrong is worse than not having
them.

### Architecture

```
  OS events (SetWinEventHook, out-of-context)
        |
        v
  +-------------------+
  |  Context Observer |   foreground changes, location changes, minimise
  |  (debounced)      |   scoped to the target thread, never global
  +---------+---------+
            | raw signal: hwnd, pid, exe, title, state
            v
  +-------------------+
  | Context Resolver  |   -> ContextIdentity { kind, key, confidence }
  |  (pure, testable) |      app | window | document | url
  +---------+---------+
            |
            v
  +-------------------+
  |  Binding Matcher  |   query bindings for this identity
  |  (pure, testable) |   below the confidence floor -> empty
  +---------+---------+
            |
            v
  +-------------------+
  | Visibility Policy |   what to show, what to hide, what to leave alone
  +-------------------+
```

The **Resolver** and **Matcher** are pure functions over a signal record. They
take no OS dependency and are unit-testable without a desktop. This is the
single most important structural property of the design: the logic most likely
to be wrong is the logic easiest to test.

### The confidence floor

Every resolved identity carries a confidence. **Below the floor, Noto shows
nothing and says nothing.**

This follows directly from principle 6: a notes app that shows the *wrong*
notes is worse than one that shows none, because the user stops trusting it —
and trust is the whole product.

There is no "best guess" mode. There is no "probably this project".

### Observation rules

Binding constraints, derived from technical research:

1. **Never hook globally.** `SetWinEventHook` is scoped to the target's
   `(pid, tid)` once a binding exists. A global `EVENT_OBJECT_LOCATIONCHANGE`
   hook is the single largest performance mistake available here — PowerToys
   measured 3–5% CPU from mouse movement alone on a comparable hook.
2. **Foreground changes use `EVENT_SYSTEM_FOREGROUND`, never polling.**
3. **Always `WINEVENT_OUTOFCONTEXT`.** In-context injects Noto's DLL into other
   processes — unacceptable for stability and for how it would look to
   antivirus.
4. **Debounce and coalesce.** Alt-tabbing through ten windows must not trigger
   ten resolutions. Coalesce to roughly one frame.
5. **Noto never reads other applications' content** in the MVP. It reads window
   metadata — process, title, position — not what is inside.

### Privacy boundary

This must be stated because the feature invites the question:

- Noto observes **which** application and window are focused, not **what is in
  them**
- observations are never logged, never transmitted, never persisted beyond the
  bindings the user created
- the finer ladder rungs (document path, URL) use UI Automation to read an
  *identifier*, not content — and when they ship, they ship with an explicit
  opt-in and a written privacy note

A context engine is only acceptable because of ADR-002. If Noto were not
local-first, this feature would be surveillance.

---

## Rationale

**Determinism is the product.** Every competitor is either manual (Notezilla's
patterns), coarse (Anchored's windows), or probabilistic (Atlas). The
unoccupied position is automatic *and* exact, and principle 6 commits us to it.

**Layering makes it shippable.** Full document-level resolution is a research
problem. Application-level resolution is a week. The ladder lets Noto deliver
real value in M5 while keeping the harder rungs as clearly-scoped later work,
rather than as a milestone that never completes.

**Pure resolution logic makes it testable.** Windows integration cannot be
meaningfully unit-tested. By reducing OS interaction to producing a signal
record, everything downstream becomes ordinary testable code.

**The confidence floor makes it trustworthy.** It is the difference between a
feature users rely on and one they disable.

---

## Consequences

### Positive

- deterministic, explainable behavior — "this note appeared because you focused
  VS Code" is a sentence we can always say
- core logic testable without a desktop session
- no content reading, so the privacy story is simple and honest
- scoped hooks keep idle cost near zero
- the ladder gives a clear, incremental roadmap

### Negative

- **less capable than AI inference in the hardest cases.** Where Atlas guesses,
  Noto shows nothing. This is a deliberate trade and will occasionally look
  worse in a demo.
- document- and URL-level binding need UI Automation, which is slower, more
  fragile, and application-specific. Deferred for good reason.
- the confidence floor means honest failure rather than a plausible guess.
  Users must understand the model for this to read as reliability.
- application-level binding alone may feel coarse to early users. This is
  acknowledged, and is why the ladder exists.

### Explicit non-goals for the MVP

- reading window or document content
- OCR
- any AI or machine-learned context inference
- browser extensions
- per-tab granularity

---

## Alternatives rejected

| Option | Why |
| ------ | --- |
| **Title patterns (A)** | Violates principle 5 — it is configuration, not context. Fragile and silently breaking. This is precisely what Noto is differentiating *against*. |
| **Window-only (B)** | Adopted as a rung, rejected as the ceiling. 40 tabs sharing one anchor is the gap in the market. |
| **AI inference (C)** | Violates principles 6 and 10, and 3. Reading every focused window's content is a privacy cost Noto will not pay, and a probabilistic answer is not what a trust-dependent feature needs. |

---

## Future reconsideration criteria

Revisit if:

- UI Automation proves reliable and fast enough for document and URL resolution
  across the applications that matter → a new ADR for the finer rungs
- scoped hooks still cost measurable idle CPU → reconsider the observation model
- users consistently report application-level binding as too coarse to be
  useful → accelerate the ladder rather than abandon determinism
- a deterministic browser-tab signal becomes available that does not require an
  extension

Do **not** revisit to add AI-based context inference because it demos well. The
deterministic position is the product, and it is written down here so that a
future assistant proposing "let the model figure out the context" has to
address this record first.
