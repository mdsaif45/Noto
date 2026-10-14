# Roadmap

**Status:** Proposed — milestone status reconciled with the code on 2026-10-13
**Last updated:** 2026-10-13
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

## Where it stands (2026-10-13)

Every item below carries one status, from this vocabulary only:

| Status | Means |
| ------ | ----- |
| **implemented** | Built and merged on `main`, with tests |
| **validated** | Implemented, and runtime-validated on a frozen Release build |
| **in review** | Built, tested and validated on a branch; its PR is open |
| **partially implemented** | Some of the item is built; what is missing is named |
| **designed, not implemented** | An ADR or contract exists; no code |
| **not started** | Nothing exists |
| **deferred** | Moved to a later milestone, with the reason |
| **dropped** | Deliberately not done, with the reason |

```
M0  Foundation          #20 #7 #10 #11 in review (#98 #99 #100 #101); #21 in review (#104,
                        draft); #5 research in review (#103); #19 decided (#105)   NOT COMPLETE
M1  Core Note Engine    engine complete for notes, folders, tags (#13);
                        markdown (#14) and export/backup (#15) not started     ENGINE DONE, PRODUCT NO
M2  Workspace           shell validated: dock, resize, hotkey, drawer, tray,
                        single instance, run at login (#96); icon in review (#102);
                        multi-monitor, slide-in, settings UI not built          PARTIAL
M3  Parity              15 of 264 rows [x] (13 of 147 MUSTs)                    NOT STARTED as M3 work
M4+                                                                          NOT STARTED
```

The detail, with evidence, is [docs/project/progress.md](../project/progress.md).
The parity count is read from
[sidenotes-parity.md](../product/sidenotes-parity.md), never asserted here.

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

| Item | Status | Evidence |
| ---- | ------ | -------- |
| Repository governance, research, ADRs | implemented | `docs/`, ADR-001–015 |
| **Window behaviour spike** — framework gate | validated on Windows 11 | ADR-001. Windows 10 22H2 is not verified (#32, M4) |
| Solution and project structure per ADR-009 | implemented | 6 source and 4 test projects |
| **Architecture tests** — `Noto.Core` has no platform or presentation dependency | implemented | Core boundary guards on `main`; #20 adds the layer graph, ADR-010 handler rules and `Noto.UI` isolation over the built output — in review (PR #98) |
| CI builds and tests the real solution | implemented | build and test, C# analysis, CodeQL, docs governance |
| Command infrastructure (ADR-010) | in review | Every mutation is a UseCases handler returning `CommandResult`; #20 enforces it. The registry and dispatcher are in PR #104 (#21, draft) |
| Domain event infrastructure (ADR-010) | in review | #21, PR #104 (draft): `EventHub`, events only after success; `NoteCreated` is the reference |
| Design token foundation (ADR-011) | implemented | #22, PR #56: `Noto.UI` |
| SQLite, connection management, migration runner (ADR-003) | implemented | schema v1, migration 002, safety copy |
| Structured logging that never records note content | in review | #7, PR #99 |
| Global error handling | in review | #10, PR #100 |
| Settings persistence | implemented | #9, PR #65 |
| Test infrastructure | implemented | pool isolation (#47, #85); runtime validation harness (`tools/validation`) |
| Performance measurement harness and baseline | in review | #11; `quality-gates.md` |
| Project board | decided: optional | #19, option B: GitHub Issues are the source of truth (PR #105) |
| Code-signing RFC | in review | #5, PR #103: options, prices, lead times and the unsigned fallback recorded in ADR-008. The roadmap requires this to be confirmed **during M0**; the publisher's identity is the owner's input |

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

| Item | Status | Evidence |
| ---- | ------ | -------- |
| `Note`, `Folder`, `Tag` domain types, no platform dependency (ADR-009) | implemented | frozen engine contract; #13 slices 1–6 |
| `Attachment`, `Task` domain types | not started | no consumer until D-rows (M3) |
| Repositories and migrations | implemented | three repositories; schema v2 |
| Commands and handlers for every mutation (ADR-010) | implemented | 23 commands, 7 queries. Dispatching them through a registry is #21 (M0), not built |
| Domain events | not started | #21 (M0) |
| Note content as markdown source (ADR-004) | implemented | content is stored as markdown source |
| Markdown parsing and rendering (#14) | not started | the editor is a plain `TextBox`; invisible markdown is M3 |
| Soft delete and restore | implemented | engine only; no recycle-bin UI (parity B23) |
| Attachment storage on disk | designed, not implemented | `NotoStoragePaths` names the folder; nothing writes to it |
| **Backup and export to markdown** (#15) | not started | ADR-002's promise is unbacked until this exists |
| Search indexing foundation (FTS5) | deferred to M3 | `SchemaV1.cs`: FTS5 arrives with search (#17) |

**Engine foundation is not product capability.** Every command above is
tested and reachable from code; a user reaches eight of them (create and
rename folder; list folders and notes; get, create, update and delete a note).

> The domain must be exercisable entirely from tests, with no UI. If it cannot
> be, the boundary is wrong and it is cheaper to fix here than anywhere later.

---

## M2 — SideNotes Workspace ◄ v0.3, first dogfoodable

*Exit: Noto is usable daily as an edge-drawer notes app.*

| Item | Status | Evidence |
| ---- | ------ | -------- |
| Edge-docked sidebar, topmost drawer (ADR-007) | validated | #16 slices 1–3, 6 |
| Left / right edge | partially implemented | the `workspace.edge` setting is honoured; nothing a user can press switches it (parity A2) |
| Auto-hide (put away on deactivation) | validated | #16 slice 6 |
| Slide-in | not started | hide is `AppWindow.Hide()`; no animation (parity A20) |
| Adjustable width | validated | inner-edge resize, remembered per display (#16 slice 3) |
| Global shortcut | validated | `Ctrl+Alt+Win+Space`, shows and hides (#16 slices 4–5) |
| **Every activation surface independently disableable** (principle 7) | partially implemented | the hotkey has a stored on/off with no UI; the tray cannot be disabled; Open Bar and Hot Side do not exist |
| Note list, folder navigation, selection | validated | M2-1, M2-2 |
| Note editor | partially implemented | plain-source `TextBox`; save on leave, hide and close; no autosave (B19) |
| Create, edit, delete | implemented | note create, edit, delete; folder create and rename |
| Reorder, drag & drop, folder delete, pin, colour, fold, move | not started in the UI | engine commands exist |
| System tray | validated | #16 slice 7 |
| Run at login | validated | #94, PR #96 |
| Single instance | validated | A17, PRs #90, #91 |
| The design system applied to real surfaces (ADR-011) | implemented | all three surfaces use `Noto.UI` tokens |
| Per-monitor DPI and multi-monitor correctness | not started | #16 slices 8a/8b; #30 |
| Settings surface | not started | every preference lives only in the settings table |

**What M2 does not yet provide as a finished product:** a way to change any
preference, multi-monitor placement, motion, a recycle bin, reordering or
organising notes, and rendered markdown. The shell is strong; the product on
top of it is early.

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
  folders       flat (C3), moving notes between them, ordering, collapse
  content       markdown, formatting, checklists, code,
                images, attachments, file & folder shortcuts
  search        full-text, incremental, keyboard-first (no filters: E8)
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
milestone's worth of architecture. They have no parity row yet (see the parity
document's coverage summary).

**M3 is delivered as product slices**, each a user-visible capability with its
parity rows, acceptance criteria and runtime validation:
[product-slices.md](product-slices.md).

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
- Jump Lists
- File Explorer integration
- Windows Search
- clipboard capture beyond parity B3 / D38 (which are M3)
- screenshot capture via `Windows.Graphics.Capture`
- Windows Hello for locked notes
- **screen-capture exclusion** — one API call, high perceived value (ADR-007)

Not all of these ship. Each is evaluated on merit when reached. `noto://`
protocol activation was listed here; it is parity H5, a MUST, so it moved to M3.

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
