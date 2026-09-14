# Research

Research conducted before implementation, September 2026.

## Deliverables

| Document | What it answers |
| -------- | --------------- |
| [competitive-analysis.md](competitive-analysis.md) | What the eight competing products do (plus WindowTop, studied as a feasibility proof rather than a competitor), and where the market is split |
| [feature-matrix.md](feature-matrix.md) | Every feature, every competitor, and Noto's decision with a reason |
| [windows-landscape.md](windows-landscape.md) | What Windows supports, what it does not, and where the obvious implementation is a trap |
| [ux-analysis.md](ux-analysis.md) | Interaction lessons, drawn mainly from competitors' unresolved complaints |
| [product-opportunity.md](product-opportunity.md) | The conclusion: where the gap is and whether it is worth building |

## Raw material

[`_raw/`](_raw/) holds the unedited agent research output, with every source
URL. The deliverables above are the consolidated, cross-checked versions. When
the two disagree, the deliverable is authoritative — but the raw files are kept
so that any claim can be traced back.

## Evidence labelling

Every factual claim about a competitor is labelled:

| Label | Meaning |
| ----- | ------- |
| **CONFIRMED** | Stated on the vendor's site, docs, or store listing, with a URL |
| **INFERRED** | Reasoned from confirmed facts or screenshots; not stated by the vendor |
| **UNKNOWN** | Could not be verified — recorded as unknown rather than guessed |

Two caveats carried through every document:

1. **@/Anchored has no independent coverage.** No reviews, no community
   threads, no repository. Every capability claim is the vendor's own.
2. **TSNotes could not be fetched** — both official domains returned HTTP 403.
   That section is the weakest here.

## What research changed

Three assumptions from the initial project direction did not survive:

| Assumption | Finding |
| ---------- | ------- |
| Contextual notes on Windows are greenfield | **No.** Notezilla and Zhorn have shipped window attachment for years — via user-authored title patterns. The differentiator is the mechanism, not the idea. |
| EF Core is the default data layer | **No.** It cannot model FTS5, and search is core. (ADR-003) |
| WinUI 3 is a straightforward choice | **Provisionally.** It is weak at translucency, click-through and drag/drop. Gated on validation spikes. (ADR-001) |

## Outstanding verification

Recorded in [competitive-analysis.md §6](competitive-analysis.md#6-open-verification-items).
None blocks the MVP. Two should be done before the Context Engine milestone:

- hands-on evaluation of @/Anchored against the 16-item checklist in
  [`_raw/anchored-windowtop.md`](_raw/anchored-windowtop.md)
- a prior-art check on @/Anchored's asserted "Patent Pending"
