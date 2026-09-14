# Competitor-Derived Enhancements

**Status:** Placeholder — populated at M7
**Last updated:** 2026-09-14

---

## Why this document is separate

This is deliberately **not** part of
[sidenotes-parity.md](sidenotes-parity.md), and the separation is the point.

```
                    Noto
                      │
          ┌───────────┴───────────┐
          │                       │
   SIDENOTES PARITY        DIFFERENTIATION
   (M3, the first          (M5–M8, after
    product)                hardening)
          │                       │
   "we promised this"      "we chose this"
```

Mixing them makes the first product's completion unknowable. If competitor
features land under the parity banner, "are we at parity?" stops having an
answer, and M3 never ends.

**Nothing here is scheduled until M4 (Hardening) is complete.**

---

## Acceptance test

Every candidate answers all five before it is accepted:

```
  1. Does it solve a real user problem?
  2. Does it fit Noto?                      principles.md
  3. Is it maintainable for years?          principle 9
  4. Does it complicate the core model?     ADR-009
  5. Does it differentiate meaningfully?
```

A "no" on 3 or 4 is disqualifying regardless of the others. A feature that is
good but bends the domain model is the expensive kind of mistake.

---

## Candidate pool

Sourced from [research](../research/competitive-analysis.md). **Recorded, not
committed** — each needs the five questions answered when M7 is reached.

| Source | Capability | Why it was noticed | Milestone |
| ------ | ---------- | ------------------ | --------- |
| Noticky | Screen-capture exclusion | One API call; corroborated by a real user review, not just marketing | M5 |
| Noticky | App-aware notes | The contextual model, done without focus theft | M6 |
| Noticky | Sticky Screenshot — region capture into a note | Capture with no round trip through another tool | M5 |
| Noticky | Floating image overlays, click-through | Genuinely novel presentation | M7 |
| Noticky | MCP server for AI assistants | Category-first; post-v1 at the earliest | post-v1 |
| @/Anchored | Window-anchored notes that follow | The headline competitor capability | M6 |
| @/Anchored | Notes docked to a window's edges | Presentation idea worth evaluating | M7 |
| Notezilla | Reminders with snooze | Four of eight competitors have them; principle 1 rules out a scheduling *subsystem*, not a simple per-note reminder | post-v1 |
| Notezilla | Memoboards | A patch for desktop clutter Noto avoids by design — probably REJECT | — |
| Zhorn | Sleep / wake scheduling | Genuinely good idea, unusual | M7 |
| Zhorn | A public automation API | Only automation surface in the Windows set | M5 |
| MS Sticky Notes | Automatic source capture and recall | The same instinct as contextual notes, narrowly implemented | M6 |
| Simple Sticky | Per-note password lock | Table stakes; already an MVP privacy item | M3 |

---

## Rejected, with reasons

Recorded so they are not re-proposed:

| Rejected | Reason |
| -------- | ------ |
| Mobile and web clients | Contradicts the Windows-native focus (vision) |
| Collaboration, sharing, multi-user | Not the product |
| Plugin marketplace | Principle 9 — maintenance liability far exceeding the benefit |
| Cloud sync as a core feature | ADR-002 |
| AI as the context-resolution mechanism | ADR-005 — determinism is the position |
| Six-note-per-window caps | An artefact of a competitor's constraints, not a feature |

---

## Process

When M7 begins:

1. re-verify each candidate still exists and works as recorded — this research
   is from September 2026 and competitors move
2. answer the five questions in writing
3. accepted candidates become issues with acceptance criteria
4. rejected candidates move to the table above with a reason

Until then this document is a parking lot, and that is its correct state.
