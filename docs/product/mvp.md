# Noto — MVP Definition

**Status:** Proposed — awaiting approval
**Last updated:** 2026-09-14

---

## The MVP in one sentence

**A fast edge sidebar with markdown notes, instant search, floating notes, and
application-level contextual notes — local-first, keyboard-first, on Windows.**

---

## The shaping question

The initial project direction proposed a candidate MVP of roughly twenty
features, including screenshot capture, clipboard capture, and basic
application context detection. Research changes the calculus in two directions
at once:

- **Some things are cheaper than assumed.** Screen-capture exclusion is one
  API call. Markdown has no competition on Windows.
- **Some things are much harder than assumed.** Window-level binding requires
  solving durable identity (ADR-006), which has no clean answer.

So the MVP is chosen by a single test:

> **What is the smallest product that demonstrates Noto's actual thesis —
> "notes that live with your work" — and that someone would keep using?**

A sidebar with notes is a nice sidebar. It is not Noto. Context is what makes
it Noto, so some context must ship. But the *full* context ladder (ADR-005) is
a milestone of its own.

**The resolution: ship the bottom rung of the context ladder, and ship it
well.** Application-level binding is deterministic, cheap, reliable, and it
proves the thesis. Window-level binding — the part with unsolved identity
problems — is v0.5, not v0.4.

---

## In scope

### Foundation

- WinUI 3 application shell, self-contained, sparse-packaged (ADR-001, ADR-008)
- structured logging that never records note content
- global error handling and crash recovery
- settings, persisted
- SQLite with hand-written migrations (ADR-003)
- system tray, run at login

### Notes

- create, edit, delete, restore from a recycle bin
- **markdown**: headings, bold, italic, inline code, code blocks, lists,
  **task lists**, links, quotes (ADR-004)
- plain-source editing — live styling is a refinement, not MVP
- folders (single level of nesting)
- tags
- colors
- pin
- archive
- auto-save on every keystroke

### Workspace — the primary surface

- edge-docked sidebar, left or right, as a snapped topmost window (ADR-007)
- auto-hide with slide-in
- adjustable width, remembered
- global shortcut to summon and dismiss
- folder navigation
- note list with reordering and drag/drop
- **complete keyboard navigation** — every action reachable without the mouse
- **every activation surface independently disableable** (principle 7)

### Search

- SQLite FTS5 full-text over note content, titles and tags (ADR-003)
- keyboard-first search UI, results as you type
- filter by folder and tag

### Floating notes

- promote a note to an independent desktop window
- always-on-top
- resize and move, position persisted across restarts
- opacity, via XAML (ADR-007)
- lock
- whole-window click-through ghost mode, with a hotkey and tray item to exit
  (ADR-007)
- multiple floating notes

### Context — application level only

- foreground application detection via `EVENT_SYSTEM_FOREGROUND`, never polling
- correct identity for packaged apps — AUMID, not `ApplicationFrameHost.exe`
- bind a note to an application in one gesture
- show and hide bound notes as applications gain and lose focus, **without
  stealing focus**
- `detached` as a first-class, non-alarming state (ADR-006)
- the confidence floor: when context is unclear, show nothing (ADR-005)

### Capture

- global hotkey quick capture — type, save, return to the previous application
- clipboard capture
- drag and drop text and files into a note

### Privacy

- per-note lock
- screen-capture exclusion, described as "hide from screen sharing" and never
  as security (ADR-007)
- no telemetry

### Quality

- multi-monitor and per-monitor DPI correctness, **including mixed DPI**
- verified on Windows 10 22H2 and Windows 11 24H2
- the performance budgets in principle 3, measured
- backup and export to markdown files

---

## Explicitly out of scope

Each with the reason, so the decision can be revisited on its merits rather
than relitigated from scratch.

### Deferred to later milestones

| Deferred | To | Why |
| -------- | -- | --- |
| **Window-level binding** | M5 / v0.5 | Durable identity (ADR-006) is the hard problem. Application-level proves the thesis without it. |
| **Document / URL-level context** | Post-v1 | Requires UI Automation. This is the real differentiator and deserves its own research and ADR, not a rushed MVP slot. |
| **Window following** | M5 / v0.5 | Depends on window binding. Also the most likely area of patent exposure — needs the freedom-to-operate check first. |
| **Screenshot capture** | M6 | `Windows.Graphics.Capture` works, but capture is a whole subsystem. Clipboard and drag/drop cover the common paths. |
| **Live-styled markdown editing** | M8 | A refinement. Storage format does not change, so this is not a rewrite. |
| **Tables and images in notes** | Post-MVP | Each needs its own justification (ADR-004). |
| **Nested folders beyond one level** | Post-MVP | No evidence of need. Adding depth later is easy; removing it is not. |
| **Reminders** | Post-v1, if ever | Drifts toward task management. Principle 1. |
| **URI protocol, Windows Search, File Explorer integration** | M7 | Real value, not on the critical path to proving the thesis. |
| **Windows Hello note unlock** | M7 | Per-note lock ships first; Hello is an enhancement. |
| **Auto-update** | M8 | Needed before public release, not before the product is validated. |

### Rejected for v1 entirely

| Rejected | Why |
| -------- | --- |
| Cloud sync, accounts | ADR-002. Post-v1 at the earliest, BYO storage if ever. |
| Mobile and web clients | Contradicts the Windows-native focus. |
| Collaboration, sharing, multi-user | Not the product (vision). |
| AI assistant, MCP surface | Interesting, and explicitly **not** the answer to context resolution (ADR-005). Post-v1 at the earliest. |
| Plugin system | Principle 9. Maintenance liability far exceeding the benefit. |
| Backlinks, graph view, daily notes | That is a knowledge base. Not the product. |
| Cross-platform | The entire value is Windows integration. |

---

## Release shape

The MVP is not one release. It is validated incrementally:

```
  v0.1  Foundation          shell, storage, settings, tray, CI
        └─ proves: it builds, it starts, it persists

  v0.2  Notes + Workspace   markdown notes, folders, tags, sidebar,
                            keyboard nav, global shortcut
        └─ proves: it is a usable notes app
        └─ FIRST DOGFOODABLE BUILD

  v0.3  Search + Floating   FTS5 search, floating notes, always-on-top,
                            opacity, ghost mode, capture exclusion
        └─ proves: it is a good notes app

  v0.4  Context (app level) app binding, show/hide on focus, quick capture,
                            clipboard, drag & drop
        └─ proves: THE THESIS

  v0.5  Context (window)    window binding, durable identity, follow
        └─ proves: the differentiator at full strength

  v1.0  Hardening           accessibility, performance, installer,
                            auto-update, security review, docs
```

**v0.2 is the most important milestone in this plan**, because it is the first
build that can be used daily. Everything after it is informed by actually
living with the product rather than reasoning about it.

**v0.4 is the decision point.** If application-level context does not feel
valuable in daily use, the thesis is wrong, and that must be discovered before
building the harder window-level version — not after.

---

## Success criteria

The MVP succeeds if, after two weeks of daily use:

- Noto is still installed and running
- the user has stopped keeping a scratch file
- at least one note has been opened *without being searched for*, because
  context surfaced it
- the performance budgets hold under a realistic note count
- nothing has been lost

The third criterion is the real one. It is the only one that distinguishes Noto
from a good sidebar.

---

## Known risks to the MVP

| Risk | Mitigation |
| ---- | ---------- |
| ADR-001 validation gate fails; framework changes | Spikes are scheduled in M0, when a switch costs days |
| Application-level context feels too coarse to be useful | Discovered at v0.4, before window-level work begins. This is deliberate sequencing. |
| Mixed-DPI multi-monitor defects | Identified as the dominant defect theme in comparable software; in the test matrix from the start (ADR-007) |
| Markdown alienates non-technical users | Target audience already writes markdown; revisit only with evidence |
| Scope creep during M1–M2 | This document, and principle 9 |

---

## Open question for approval

One judgement call is worth flagging explicitly, because reasonable people
would differ:

> **Should window-level binding be in the MVP rather than v0.5?**

The argument for including it: it is what @/Anchored does, and it is the more
impressive demo.

The argument for deferring it — taken here: it depends on durable identity,
which has no clean solution (ADR-006), and building it before validating that
contextual notes are useful at all risks spending the hardest engineering
effort on an unproven premise.

If the preference is to include it, M5 moves before M4 and v0.4 absorbs v0.5.
That is a legitimate choice; it is simply not the recommended one.
