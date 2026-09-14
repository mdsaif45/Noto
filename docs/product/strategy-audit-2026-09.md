# Strategy Alignment Audit — September 2026

**Status:** Complete
**Verdict:** **CHANGE** — proceed, but not into implementation until the items below are closed.

---

## Why this audit exists

The product direction was clarified after the initial planning phase. The
original plan treated **contextual notes as the thesis to validate**, and
sequenced everything around discovering whether that thesis held.

The clarified direction is different:

> Build an exceptionally solid foundation, achieve comprehensive **SideNotes
> feature parity on Windows**, harden it to production quality — and only then
> add Windows enhancements, contextual notes, competitor features and Noto
> differentiators.

This audit re-examines every existing artifact against that direction.

---

## Repository state at audit time

| | |
| --- | --- |
| Commits | 5, all documentation |
| Implementation | **none** — no `src/`, no `tests/`, no solution |
| Open PR | #1, governance and research |
| Issues | 18 |
| Milestones | 10 |
| Labels | 34 |
| Docs | 25 files |
| CI | 3 checks, passing |

**The single most important fact: there is zero implementation.** Every finding
below is cheap to act on now and expensive later. This is the correct moment
for a strategy correction.

---

## GO — correct, keep unchanged

| Area | Why it stands |
| ---- | ------------- |
| Repository governance | Issue forms, PR template, CODEOWNERS, Dependabot, branch protection, security features. Strategy-independent and well-built. |
| CI workflows | Degrade cleanly with no solution present, start building when one appears. Correct design. |
| Research corpus | Nine products, sourced and evidence-labelled. The findings do not change because the sequencing did. |
| ADR discipline | The mechanism — record decisions before implementing, review before contradicting — is the right defense against AI-assisted drift. |
| ADR-002 (local-first) | Independent of sequencing. Still correct. |
| ADR-003 (SQLite, not EF Core) | The reasoning is about FTS5 and startup cost, not about context. Unaffected. |
| ADR-007 (window presentation) | The `SHAppBarMessage` and `SetLayeredWindowAttributes` rejections are needed *earlier* now, since the sidebar is M2. |
| ADR-008 (packaging) | Unaffected. |
| Issues #2–#11 | Foundation work. Valid under any sequencing. |
| Principles | Principle 5 ("context should be automatic") describes how context should work *when built*, not when to build it. No conflict. |

---

## CHANGE — misaligned, corrected in this pass

### C1. The roadmap was sequenced around validating context

Context was M5 and the declared thesis. Under the clarified direction, parity
is the first product and context is an extension after hardening.

```
  BEFORE                          AFTER
  ─────────────────────────       ──────────────────────────────
  M0 Foundation                   M0 Foundation & Architecture
  M1 Core Notes                   M1 Core Note Engine
  M2 Workspace                    M2 SideNotes Workspace
  M3 Search                       M3 SideNotes Parity
  M4 Floating Notes               M4 Hardening
  M5 Context Engine ◄ THESIS      ──── v0.9 PARITY COMPLETE ────
  M6 Capture                      M5 Windows Enhancements
  M7 Windows Integration          M6 Contextual Notes
  M8 Polish                       M7 Competitor Features
  v1.0                            M8 Noto Differentiators
                                  M9 v1.0
```

Search and floating notes are no longer milestones of their own — both are
SideNotes features, so they belong inside M3 parity.

### C2. v1.0 meant "everything researched"

Now: **v1.0 is Noto's polished Windows interpretation of SideNotes.** Competitor
features and differentiators are v1.1+. This is what makes "done" knowable.

### C3. ADR-005 and ADR-006 were premature

Both are thorough and both design a full context engine — confidence floors,
composite fingerprint identity, `detached` states — for a capability now two
phases away.

This is the "architecture astronautics" failure mode. Deciding the mechanism of
window identity before a note can be created is deciding in the wrong order,
and the decision would be stale by the time it is used.

**Demoted to Proposed.** Not deleted — the research behind them is sound and
they become the starting point for M6. What they must *not* do is shape the
core domain model before then.

### C4. Context was baked into the core architecture

`architecture-overview.md` placed `ContextResolver`, `BindingMatcher` and
`VisibilityPolicy` inside `Noto.Core` — the domain layer — for a deferred
capability.

Replaced with an extension boundary: the domain exposes what a future context
engine would need, and nothing else.

### C5. No presentation abstraction existed

This was the most serious architectural gap, and it is the one the clarified
direction cares most about.

`FloatingWindows` was a table keyed on `NoteId` — which encodes exactly the
trap to avoid:

```
  WRONG                              RIGHT
  ─────────────────────────          ──────────────────────────────
  Note                               Note (domain)
   └─ is a floating note               │
                                       ├─ presented in sidebar
  a floating note is a                 ├─ presented as floating
  different kind of note               └─ presented contextually

                                     one note, many presentations,
                                     simultaneously
```

A note must be able to appear in the sidebar **and** float **and** later be
contextual, without the domain model changing. Addressed in ADR-009.

### C6. No command or event model

Nothing defined how an operation invoked from the UI, a keyboard shortcut, a
tray menu, a future CLI or a future automation surface shares one
implementation. Without it, business logic duplicates per entry point — and
SideNotes parity includes a URL scheme and automation, so this is needed for
parity, not speculation. Addressed in ADR-010.

### C7. No design system

Parity means building many UI surfaces. Without tokens defined first, values
scatter and every later visual change becomes a sweep. Addressed in ADR-011.

### C8. No parity specification

The word "parity" appeared **nowhere** in the repository. There was no
objective definition of what the first product must contain, which means no way
to know when it is finished. Addressed by `sidenotes-parity.md`.

---

## BLOCK — must be resolved before implementation

| # | Blocker | Resolution | Status |
| - | ------- | ---------- | ------ |
| B1 | **Framework undecided.** ADR-001 is provisional pending spikes. Writing UI before it is settled risks rewriting it. | Issues #2–#4, expanded to cover the full window-behavior matrix. | Open, first work |
| B2 | **No parity definition.** Cannot build toward an undefined target. | `docs/product/sidenotes-parity.md`, derived from Apptorium's own documentation. | In progress |
| B3 | **No presentation abstraction.** Building the sidebar first would harden the wrong model. | ADR-009. | Resolved |
| B4 | **No command/event boundary.** Retrofitting after the UI exists means rewriting every call site. | ADR-010. | Resolved |
| B5 | **No design tokens.** Building screens first scatters values. | ADR-011. | Resolved |

B1 and B2 remain genuinely open. B1 is the first implementation work; B2 needs
Apptorium's documentation, not a decision.

---

## Deliberately NOT done

Restraint is part of the audit. The following were considered and rejected as
premature under "prepare extension boundaries, not speculative systems":

| Not built | Why |
| --------- | --- |
| Plugin SDK, runtime, manifest | Internal extension boundaries are enough. Expose later only if needed. |
| Sync engine | Portable storage and clean export are the preparation. Nothing more. |
| Full context engine | ADR-005/006 stay Proposed until M6. |
| Event sourcing, CQRS, mediator library | A local desktop app. A plain command interface and a small event aggregator suffice. |
| DI framework beyond the built-in container | No demonstrated need. |
| AI/MCP surfaces | Post-v1, and explicitly not the answer to context resolution. |

---

## Quality-bar check

The questions the clarified direction asks, answered honestly:

| Question | Answer |
| -------- | ------ |
| Can one note exist in sidebar, floating and contextual views without changing the domain model? | **Yes**, after ADR-009. Not before. |
| Is SideNotes parity objectively measurable? | **In progress** — parity spec is being written from source documentation. |
| Can features be added without rewriting the foundation? | **Yes**, after ADR-009/010/011. |
| Can the visual design evolve without rewriting every screen? | **Yes**, after ADR-011 — provided tokens land before the screens. |
| Are the hard Windows behaviors validated? | **No.** Spikes #2–#4 are the first work. This is B1. |
| Can the schema evolve safely? | **Yes.** Hand-written migrations with tested upgrade paths (ADR-003). |
| Can regressions be detected automatically? | **Partly.** CI runs; the test projects do not exist yet. |
| Is the order Foundation → SideNotes → Hardening → Expansion? | **Yes**, after this pass. |

---

## Bottom line

The previous phase produced good governance, good research and good ADR
discipline. Its defect was one of **sequencing and emphasis**: it optimised the
plan around proving the contextual-notes thesis, when the actual first product
is an excellent Windows SideNotes.

Because there is no implementation, correcting this costs documentation changes
and issue re-planning — nothing more.

**Proceed to the foundation. Do not proceed to feature work until B1 and B2 are
closed.**
