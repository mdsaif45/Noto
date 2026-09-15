# Product Opportunity

**Research date:** 2026-09-14
**Status:** Conclusion of the research phase

---

## The thesis in one paragraph

There is a real, uncontested position on Windows: **a well-built notes
application whose context resolution is automatic and deterministic.** The best
products in this category are macOS-only. The Windows products that exist are
either capable but dated (Notezilla), or contextual but not actually notes
applications (@/Anchored). No mainstream Windows product in the set supports
markdown or an edge-docked sidebar. The differentiator is not the idea of contextual notes,
which already exists — it is the *mechanism*, which everyone currently gets
wrong in one of two ways.

---

## 1. The gap, stated precisely

Contextual notes on Windows are **not greenfield**. That assumption had to be
corrected during research:

| Product | Context feature | Mechanism | Evidence |
| ------- | --------------- | --------- | -------- |
| Notezilla | "Stick notes to webpages, documents, programs, apps, folders, or any window" | **User-authored window-title patterns with `*` wildcards** | CONFIRMED — vendor help docs |
| Zhorn Stickies | Attach to application, website, document or folder | Undocumented; inferred title-based | Feature CONFIRMED; mechanism UNKNOWN |
| @/Anchored | Note follows a live window | Undisclosed | Vendor claim only; UNVERIFIED |

This is good news. A paid product has sold this for years, which means the
demand is real and the concept needs no market education.

The opening is that **all three existing mechanisms are unsatisfying**:

```
  APPROACH                      WHO              PROBLEM
  ----------------------------------------------------------------------
  user writes a title pattern   Notezilla,       fragile, manual, silently
                                Zhorn            breaks; it is configuration
                                                 wearing a context costume

  bind to one live window       @/Anchored       coarse: 40 browser tabs
                                                 share one anchor

  read the window with an AI    @/Anchored       probabilistic, heavier,
  and infer                     (Atlas)          privacy cost
  ----------------------------------------------------------------------
  resolve the real document     NOBODY           <- the opportunity
  deterministically
```

The most telling artifact in the whole research: **@/Anchored built an AI to
work around its own binding model.** Atlas reads the anchored window to figure
out what the user is actually looking at, because the anchor itself only knows
which window it is, not which document is inside it. That is a workaround sold
as a feature, and it points directly at the unsolved problem.

---

## 2. Four opportunities, ranked by ratio of value to cost

### 2.1 Markdown — highest ratio, lowest risk

**No mainstream Windows product in the researched set supports markdown.**
OneNote still has "zero native markdown support" as of February 2026, despite
years of requests. The one possible exception, TSNotes, could not be verified —
both of its domains returned HTTP 403 — and it appears in no 2026 roundup.

Meanwhile the target audience — developers, designers, analysts, technical
staff — writes markdown every day.

```
  effort      ██              low: it is the storage format, not a feature
  competition                 none
  audience fit ████████████   exactly what they already write
```

This is unusual: a table-stakes capability for the target user with zero
competition. It also happens to be the *cheapest* format to implement, because
markdown source is plain text that FTS5 indexes directly. (ADR-004)

### 2.2 The edge sidebar — large, uncontested

Every Windows competitor uses the same 2010-era model: notes scattered on the
desktop plus a separate manager window to find them. The SideNotes edge-drawer
model — pull out, browse, dismiss — **does not exist on Windows at all**.

Notezilla's "memoboards" feature is evidence of the problem: it exists to move
notes *off* the cluttered desktop, which is a patch on the model rather than a
fix.

```
  effort      ██████          moderate: docking, auto-hide, DPI
  competition                 none on Windows
  value       ██████████      it is the primary interaction surface
```

### 2.3 Deterministic context — the actual moat

Hardest, and the reason to build Noto at all.

```
  ladder of specificity           status
  -------------------------------------------------------
  document / file path            NOBODY does this
  URL                             NOBODY does this
  window (durable identity)       Anchored, mechanism undisclosed
  application                     Noticky (macOS), tractable on Windows
```

Application-level binding is achievable now and proves the thesis.
Document-level binding is the defensible position, and it is a genuine research
problem (UI Automation, per-application behavior, performance).

The strategic sequencing decision — application level in v0.5, window level in
v0.6, document level post-v1 — exists so the thesis is validated before the
hardest engineering is attempted. (first-release.md, ADR-005)

### 2.4 Cheap wins with high perceived value

Small features that research shows users notice:

| Feature | Cost | Evidence |
| ------- | ---- | -------- |
| Screen-capture exclusion | One API call | Noticky ships it; corroborated by a real user review, not just marketing |
| Whole-window ghost mode | Small | WindowTop proves feasibility |
| Keyboard-first everything | Design discipline | SideNotes' loudest, longest-running complaint |
| Folders **and** tags | Small | SideNotes has folders, Noticky has tags; neither has both |
| Code blocks | Free with markdown | Absent everywhere, core to the audience |

---

## 3. Positioning

```
  NOT competing on feature count
  ----------------------------------------------------------------
  Notezilla has more features than Noto will have at v1.0, and that
  is fine. Notezilla is a mature sticky-notes suite with mobile apps
  and a web client.

  COMPETING ON
  ----------------------------------------------------------------
  1. context that resolves itself     (nobody)
  2. markdown                         (nobody on Windows)
  3. an edge sidebar                  (nobody on Windows)
  4. keyboard-first                   (nobody)
  5. modern, native, quiet            (nobody in this category)
```

The honest one-line position:

> **Noto is for people who work across many applications on Windows and are
> tired of losing the note they wrote twenty minutes ago.**

---

## 4. Risks to the opportunity

| Risk | Severity | Assessment |
| ---- | -------- | ---------- |
| **@/Anchored's "Patent Pending"** | Unclear | Self-asserted, unexamined, scope unknown. Substantial prior art exists — Notezilla and Zhorn have shipped window attachment for years. **Needs a real freedom-to-operate check before window-following becomes a headline claim.** |
| **Context may not be as valuable as assumed** | High | The core bet. Mitigated by sequencing: v0.5 validates it cheaply before v0.6 spends the hard effort. |
| **Deterministic resolution may not be achievable** | High | Document-level resolution via UI Automation is unproven across applications. Fallback: window-level still beats title matching. |
| **Mixed-DPI multi-monitor defects** | Medium | The dominant defect theme in comparable software. In the test matrix from M2, not bolted on later. |
| **Framework risk** | Medium | WinUI 3 is weak at exactly what Noto leans on. Gated by ADR-001 spikes with WPF as fallback. |
| **Markdown alienates non-technical users** | Low | The target audience already writes it. Revisit only with evidence. |
| **A competitor closes the gap** | Low-Medium | @/Anchored is actively developed and could add search and organization. Its coarse binding, and a six-slot-per-window limit it advertises, may indicate architectural constraints — but both are vendor claims, so this is a hypothesis to monitor rather than a finding. |

The patent item is the only one that could invalidate the plan rather than
merely delay it, and it is cheap to check.

---

## 5. What would falsify the thesis

Stated in advance, so the answer is not rationalised later:

- **Two weeks of daily use at v0.5, and contextual notes are never the reason a
  note was found.** If every note is still found by searching, context is
  decoration.
- **Application-level binding proves too coarse to be useful, and document-level
  proves infeasible.** Then Noto is a nice sidebar — a real product, but not the
  one described here, and the positioning must change honestly.
- **Focus-stealing or wrong-note surfacing cannot be eliminated.** A contextual
  feature users disable is worse than no feature.

The v0.5 decision point in the roadmap exists specifically to force this
question early.

---

## 6. Conclusion

The opportunity is real and reasonably well defended:

- an **uncontested position** on Windows
- **validated demand**, evidenced by a paid competitor shipping a shallow
  version for years
- **a clear technical differentiator** — deterministic resolution — that is
  hard enough to defend and tractable enough to build
- **cheap wins** available on the way (markdown, sidebar, capture exclusion)

The main uncertainty is not whether the gap exists. It is whether
**deterministic document-level context resolution is achievable** across the
applications that matter.

That question is answered by building, not by more research — which is why the
research phase ends here.
