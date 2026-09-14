# Roadmap

**Status:** Proposed
**Last updated:** 2026-09-14
**Strategy:** [strategy-audit-2026-09.md](../product/strategy-audit-2026-09.md)

Milestones are **capability targets, not dates.** Noto has no external deadline,
and inventing one would only distort scope decisions.

---

## The strategy in one line

> **Build an excellent Windows SideNotes first. Everything else comes after.**

```
  FOUNDATION ──> PARITY ──> HARDENING ──> v0.9 ──> EXPANSION ──> v1.0
```

Contextual notes, competitor features and Noto differentiators are real and
planned — they are simply **not the first product**. The architecture is
prepared for them (ADR-009); the schedule does not chase them.

---

## Shape

```
  ┌─ FOUNDATION ────────────────────────────────────────────┐
  │ M0  Foundation & Architecture   ████████████   v0.1     │
  │ M1  Core Note Engine            ████████████   v0.2     │
  └─────────────────────────────────────────────────────────┘
  ┌─ SIDENOTES ─────────────────────────────────────────────┐
  │ M2  SideNotes Workspace         ████████████   v0.3  ◄ dogfood
  │ M3  SideNotes Parity            ████████████████████ v0.5-0.8
  │ M4  Hardening                   ████████████   v0.9     │
  └─────────────────────────────────────────────────────────┘
                        ▲
         ═══════════ SIDENOTES PARITY COMPLETE ═══════════
                        ▼
  ┌─ EXPANSION ─────────────────────────────────────────────┐
  │ M5  Windows Enhancements        ████████                │
  │ M6  Contextual Notes            ████████████████        │
  │ M7  Competitor Features         ████████                │
  │ M8  Noto Differentiators        ████████                │
  └─────────────────────────────────────────────────────────┘
  │ M9  v1.0                        ████████                │
```

Two milestones matter more than the rest:

- **M2** is the first build usable daily. Everything after it is informed by
  living with the product rather than reasoning about it.
- **M4** is the quality gate. Nothing in the expansion phase begins until
  parity is production-quality.

### What v1.0 means

**v1.0 is Noto's polished Windows interpretation of SideNotes** — not
"everything researched." Competitor features and differentiators are v1.1+.

This is what makes "done" knowable. Without it, v1.0 recedes forever.

---

## M0 — Foundation & Architecture

*Exit: the framework is decided on evidence, the solution builds in CI, and the
architectural boundaries are enforced by tests rather than intentions.*

- ✅ repository governance, research, ADRs
- **the window behavior spike** — the framework gate, see below
- solution and project structure per ADR-009's boundaries
- **architecture tests** enforcing that `Noto.Core` has no platform or
  presentation dependency — the rule is worthless unenforced
- command and event infrastructure (ADR-010)
- design token foundation (ADR-011)
- SQLite, connection management, migration runner (ADR-003)
- structured logging that never records note content
- global error handling
- settings persistence
- test infrastructure
- performance measurement harness and baseline
- CI building and testing real code

### The framework gate

ADR-001 accepts WinUI 3 **provisionally**. Noto's window requirements are
unusual, and research found WinUI 3 weak at three of them.

The spike must cover the **full** matrix, not merely "can it show a window":

```
  borderless / custom chrome      always-on-top
  edge positioning                transparency & opacity
  resize & move                   whole-window click-through
  global hotkey                   multi-monitor
  DPI at 100/125/150/200%         mixed-DPI across monitors
  move & resize tracking          minimize / maximize / restore
  screen-capture exclusion        Explorer drag & drop
```

Verified on **Windows 10 22H2 and Windows 11 24H2**.

**If it fails, Noto moves to WPF before application code is written.** This is
first in M0 for exactly that reason: a framework switch costs days here and
months later.

---

## M1 — Core Note Engine

*Exit: the domain works, is fully tested, and has never heard of a window.*

- `Note`, `Folder`, `Tag`, `Attachment`, `Task` — domain types with **no
  platform or presentation dependency** (ADR-009)
- repositories and migrations
- commands and handlers for every mutation (ADR-010)
- domain events
- note content as markdown source (ADR-004)
- soft delete and restore
- attachment storage on disk
- **backup and export to markdown** — ADR-002 promises the data outlives the
  app, and that promise should never be unbacked
- search indexing foundation (FTS5 table and triggers)

> The domain must be exercisable entirely from tests, with no UI. If it cannot
> be, the boundary is wrong and it is cheaper to fix here than anywhere later.

---

## M2 — SideNotes Workspace ◄ v0.3, first dogfoodable

*Exit: Noto is usable daily as an edge-drawer notes app.*

- edge-docked sidebar, snapped topmost window (ADR-007)
- left/right edge, auto-hide, slide-in, adjustable width
- global shortcut
- **every activation surface independently disableable** (principle 7) — the
  direct answer to SideNotes' loudest, longest-running complaint
- note list, folder navigation, selection
- note editor
- create, edit, delete, reorder, drag & drop
- system tray, run at login
- the design system applied to real surfaces (ADR-011)
- per-monitor DPI and multi-monitor correctness

> From here on, Noto is used daily by its author. Every subsequent milestone
> should be re-examined against what that reveals.

---

## M3 — SideNotes Parity ◄ the first product

*Exit: every item in the parity specification is implemented, tested and
checked off.*

**The authoritative definition is
[docs/product/sidenotes-parity.md](../product/sidenotes-parity.md).** This
milestone is complete when that document is complete — not when it feels done.

Broad areas (the specification is the detail):

```
  notes         pin, fold, colors, ordering, duplication
  folders       nesting, moving, ordering, collapse
  content       markdown, formatting, checklists, code,
                images, attachments, file & folder shortcuts
  search        full-text, filtering, keyboard-first
  appearance    themes, light/dark, fonts, text size
  keyboard      the full documented shortcut set
  data          import, export, backup
  integration   URI protocol, clipboard, drag & drop
  floating      notes as independent desktop windows
```

Two rules for this milestone:

1. **Parity is functional, not visual.** Noto reproduces interaction concepts
   in its own visual language and replaces macOS mechanisms with Windows ones —
   menu bar becomes tray, iCloud becomes local-first, AppleScript becomes URI
   protocol and CLI. Do not clone SideNotes pixel-for-pixel.
2. **Every parity item is a tested requirement**, with acceptance criteria, not
   a checkbox someone ticks.

Floating notes live here because SideNotes has them — they are a parity
feature, and under ADR-009 they are a presentation kind rather than a new
milestone's worth of architecture.

---

## M4 — Hardening ◄ v0.9, parity complete

*Exit: the application feels production quality, not like a capable demo.*

- performance: every budget in principle 3, measured under a realistic note
  count
- cold and warm startup, idle CPU, memory
- storage reliability, corruption recovery, migration from every prior version
- backup and restore verified
- crash handling and recovery
- **accessibility**: screen reader, high contrast, focus order, full keyboard
- DPI and multi-monitor hardening, including mixed DPI
- window positioning edge cases — monitor disconnect, scaling change
- editor and drag & drop reliability
- search performance at scale
- automated regression tests covering the parity specification
- packaging, install, upgrade behavior

> **This is a gate, not a phase.** Nothing in the expansion phase begins until
> this is done. Shipping parity at 90% and starting context work is exactly the
> fragile-demo outcome the strategy exists to prevent.

```
  ═══════════════ v0.9 — SIDENOTES PARITY, HARDENED ═══════════════
```

---

## M5 — Windows Enhancements

*The first things Noto does that SideNotes cannot, because it is native.*

- app notifications (requires the sparse package, ADR-008)
- `noto://` protocol activation
- Jump Lists
- File Explorer integration
- Windows Search
- clipboard capture
- screenshot capture via `Windows.Graphics.Capture`
- Windows Hello for locked notes
- **screen-capture exclusion** — one API call, high perceived value (ADR-007)

Not all of these ship. Each is evaluated on merit when reached.

---

## M6 — Contextual Notes

*Now, and not before.*

- foreground application detection, event-driven, never polling
- AUMID resolution for packaged apps
- bind a note to an application
- show and hide on focus, **without stealing focus** — focus theft is
  disqualifying for this feature
- window-level binding and durable identity
- `detached` as a first-class, non-alarming state

**Before this milestone begins:**

1. re-validate [ADR-005](../decisions/ADR-005-context-engine.md) and
   [ADR-006](../decisions/ADR-006-window-binding-identity.md), both currently
   **Proposed** — they were written two phases earlier and will have gone stale
2. the @/Anchored hands-on evaluation
3. the "Patent Pending" prior-art check

Under ADR-009 this arrives as a **new presentation kind**. The `Note` type does
not change.

---

## M7 — Competitor Features

Selectively implement genuinely useful capabilities from Noticky, @/Anchored,
Notezilla, Zhorn Stickies and others.

Every candidate answers five questions before it is accepted:

```
  1. Does it solve a real user problem?
  2. Does it fit Noto?                      (principles.md)
  3. Is it maintainable for years?          (principle 9)
  4. Does it complicate the core model?     (ADR-009)
  5. Does it differentiate meaningfully?
```

Tracked in [competitor-enhancements.md](../product/competitor-enhancements.md),
kept deliberately separate from the parity specification so that "we promised
SideNotes parity" never blurs into "we are making Noto better."

---

## M8 — Noto Differentiators

Genuinely new capabilities, explored only once the foundation is strong.

Deliberately unspecified. Specifying them now would be guessing, and the most
valuable input — a year of using the product — does not exist yet.

---

## M9 — v1.0

- security review against SECURITY.md scope
- performance validation
- documentation and screenshots
- migration and backup verification from every prior version
- signed installer — **confirm certificate cost and process during M0**
- release

---

## Explicitly post-v1

| Deferred | Note |
| -------- | ---- |
| Document- and URL-level context | The deepest differentiator. Needs its own research and ADR. |
| BYO-folder sync | ADR-002. Requires solving the WAL-under-file-sync hazard first. |
| AI / MCP surface | Explicitly **not** the answer to context resolution (ADR-005). |
| Plugin system | Internal extension boundaries only. Expose later if ever justified. |
| Mobile, web, collaboration, cross-platform | Rejected by the vision. |

---

## Sequencing risks

| Risk | Where it bites | Mitigation |
| ---- | -------------- | ---------- |
| Framework decision reverses | M0 → everything | The window spike is the first work in M0 |
| Parity scope is underestimated | M3 | The parity spec is derived from Apptorium's own documentation, not from memory |
| Expansion starts before hardening finishes | M4 → M5 | M4 is a gate, stated as one |
| ADR-005/006 go stale before M6 | M6 | Both marked Proposed with explicit re-validation required |
| Mixed-DPI defects surface late | M2, M3 | In the test matrix from M2 |
| Signing cost discovered at release | M9 | Confirm during M0 |
