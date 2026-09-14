# ADR-006 — Window binding and durable identity

**Status:** **Proposed** — deferred to M6
**Date:** 2026-09-14
**Related:** ADR-005 (context engine)

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

ADR-005 establishes that Noto binds notes to context. This ADR addresses the
hardest unsolved problem underneath that: **a window has no durable identity.**

The user's mental model is simple and reasonable:

> "I put a note on this window. Next week I open that window again, and my note
> is there."

The operating system does not support that model.

- `HWND` is a per-session handle. It is recycled, and it is meaningless after
  the target process exits.
- Window titles are volatile. Browsers and editors rewrite them per tab, per
  file, and per dirty state.
- There is **no OS-provided durable window identifier.**

Research could not determine how @/Anchored solves this — the mechanism is
never disclosed on any page. It must persist *something*, and whatever it is
has failure modes the marketing does not mention. This is the genuinely hard
part, and the real moat.

---

## Problem

What does a note bind *to*, such that the binding survives closing the window,
restarting the application, and restarting Windows — without producing false
matches?

---

## Options considered

| Option | Durable? | False matches | Notes |
| ------ | -------- | ------------- | ----- |
| **A. `HWND`** | No | No | Useless across restarts |
| **B. Process ID** | No | No | Recycled, per-session |
| **C. Executable path / AUMID** | Yes | **Many** — all windows of the app match | This is app-level binding |
| **D. Window title, exact** | Partially | Some | Breaks constantly |
| **E. Title pattern authored by user** | Yes | Some | The Notezilla model. Rejected by ADR-005. |
| **F. Composite fingerprint** | Partially | Few | Combination, scored |

---

## Decision

**A composite fingerprint (Option F), scored, with `detached` as a first-class
note state.**

### The fingerprint

A binding persists a record, not a handle:

```
  WindowFingerprint
  ├── appIdentity      AUMID if packaged, else executable path   [required]
  ├── windowClass      Win32 class name                          [strong]
  ├── titleSignature   normalised, volatile parts stripped       [weak]
  └── createdAt        for tie-breaking among equal candidates
```

Signals are ranked by how much they can be trusted:

```
  AUMID / exe path   ####################  strong, stable
  window class       ##############        strong, stable
  title signature    #####                 weak, volatile
```

### Matching

When a candidate window appears, Noto scores it against stored fingerprints:

```
  app identity mismatch            ->  not a match, stop
  app + class match, one candidate ->  match
  app + class match, N candidates  ->  use title signature to disambiguate
                                       still ambiguous -> DO NOT GUESS
  app matches, class differs       ->  not a match
```

If scoring cannot produce a single confident answer, **no match is made.** This
is ADR-005's confidence floor applied to identity.

### `detached` is a normal state, not an error

This is the most important design consequence.

A note whose window is not currently present is **detached** — a legitimate,
visible, non-alarming state:

```
  attached    the bound window exists; note behaves contextually
  detached    the window is not here right now; note is reachable in the
              sidebar, labelled with what it was bound to
  unbound     no binding; an ordinary note
```

Detached is not a failure. It is what Tuesday looks like. The UI must never
present it as an error, and the note must never become unreachable — it is
always in the sidebar and always findable by search.

The user can re-attach a detached note to a window in one gesture, which
updates the fingerprint.

### Title normalisation

Title signatures strip known-volatile components before storage:

```
  "* " or "● " dirty markers          -> removed
  " - Google Chrome" app suffixes     -> removed
  "(3) " unread counts                -> removed
  "Page 4 of 12"-style counters       -> removed
```

Normalisation rules are **data, not code**, so they can be corrected without a
release, and they are covered by unit tests with real-world title samples.

Title signature is never used alone. It only disambiguates between candidates
that already match on app identity and window class.

---

## Rationale

**No single signal works, so a composite is the only honest answer.** Each
signal fails differently; combining them narrows failure without pretending it
is eliminated.

**Ranking prevents the Notezilla failure.** Title is the most tempting signal
because it looks the most specific. It is also the least stable. Treating it as
a weak disambiguator rather than a primary key is the difference between the
two designs.

**AUMID matters more than it looks.** For packaged and UWP applications, the
naive process query returns `ApplicationFrameHost.exe` for everything, which
would make every Store app indistinguishable. Resolving the real
AppUserModelID is required for app identity to mean anything.

**`detached` as a first-class state is what makes this survivable.** Every
alternative design has to answer "what happens when the fingerprint does not
match?" If the answer is an error state or a lost note, the feature is a
liability. If the answer is "the note is in the sidebar, labelled, one click
from re-attaching", then imperfect matching is merely imperfect rather than
harmful.

This inverts the usual framing: the design assumes matching will sometimes
fail, and makes failure cheap.

---

## Consequences

### Positive

- bindings survive restarts for the common cases
- false positives are rare, because app identity and class must both agree
- graceful, legible behavior when matching fails
- notes are never lost or unreachable
- normalisation rules are correctable without a code change

### Negative

- **multiple identical windows of the same application are genuinely ambiguous.**
  Two Notepad windows with different files cannot be reliably distinguished by
  this design. That is the document-level rung of ADR-005's ladder, and it is
  not solved here.
- title normalisation is an ongoing maintenance surface — applications change
  their title formats
- a user who expects perfect recall will occasionally be disappointed, and the
  UI must set that expectation honestly
- **6-note-per-window style limits are not adopted**; ambiguity is handled by
  refusing to guess instead

### Testing requirements

- fingerprint round-trip: bind, close, restart, re-match
- ambiguity: two windows of the same app must **not** cross-match
- title normalisation against a corpus of real titles from common applications
- detached state: note remains visible, searchable and re-attachable
- packaged app identity resolves to AUMID, not `ApplicationFrameHost.exe`

---

## Alternatives rejected

| Option | Why |
| ------ | --- |
| **HWND (A) / PID (B)** | Not durable. Non-starters. |
| **Executable only (C)** | This is app-level binding, which Noto also supports — but as a *different, explicitly chosen* binding kind, not as window binding pretending to work. |
| **Exact title (D)** | Breaks on the first tab switch. |
| **User-authored patterns (E)** | Rejected by ADR-005 and principle 5. Noto exists partly to not be this. |

---

## Future reconsideration criteria

Revisit if:

- a durable OS-level window identifier becomes available
- UI Automation gives a reliable document or file path, which would move
  disambiguation up the ladder and largely dissolve the ambiguity problem —
  **this is the expected evolution**
- title normalisation maintenance becomes disproportionate to its value
- measured real-world re-match rates are poor enough that the feature reads as
  unreliable rather than merely imperfect

Track the real-world re-match rate. If it is low, the honest response is to
narrow the feature's promise, not to loosen the matching and start guessing.
