# The First Release — SideNotes Parity on Windows

**Status:** Proposed
**Last updated:** 2026-09-14
**Supersedes:** the earlier `mvp.md`, which framed the first release around
validating the contextual-notes thesis

---

## The first product, in one sentence

**Noto v0.9 is an excellent Windows implementation of SideNotes** — the edge
drawer, the notes, the folders, the invisible-markdown editor, the search, the
keyboard — built native, local-first, and hardened to production quality.

Everything else comes after.

---

## Why "MVP" was the wrong frame

The earlier document asked:

> What is the smallest product that demonstrates Noto's thesis — "notes that
> live with your work" — and that someone would keep using?

That question optimises for **validating an idea**. It produced a plan where
contextual notes arrived at v0.5 and much of SideNotes' actual feature set was
scattered across later milestones or deferred indefinitely.

The clarified strategy asks a different question:

> What is the smallest product that is genuinely **good**, on its own terms,
> and that a solid foundation can grow from?

The answer is SideNotes parity. That target is:

- **already validated** — SideNotes is a mature paid product with years of users
- **objectively definable** — it is enumerable from Apptorium's own documentation
- **genuinely absent on Windows** — no Windows product offers the edge-drawer model
- **a foundation**, not a demo — contextual notes, capture and competitor
  features all sit naturally on top of it

We are not trying to discover whether this is worth building. We are building it.

---

## What "parity" means

**The authoritative definition is
[sidenotes-parity.md](sidenotes-parity.md).** It enumerates every documented
SideNotes feature with a Noto requirement and testable acceptance criteria.

Two rules govern it:

### 1. Parity is functional, not visual

Noto reproduces **interaction concepts** in its own visual language. It does
not clone SideNotes pixel-for-pixel.

```
  PRESERVE     the edge drawer, folders, the note list,
               invisible markdown, the keyboard model

  IMPROVE      the UX where research found complaints
               (SideNotes' undisableable trigger, keyboard-only activation)

  REPLACE      macOS mechanisms with Windows ones
               menu bar    -> system tray
               iCloud      -> local-first (ADR-002)
               AppleScript -> URI protocol, CLI, PowerShell
```

### 2. Every parity item is a tested requirement

A checkbox someone ticks is not parity. Each row carries acceptance criteria
specific enough to write a test from.

---

## Release shape

```
  v0.1  Foundation          M0   framework decided on evidence, solution
                                 builds, boundaries enforced by tests
                                 commands, events, design tokens, storage

  v0.2  Core Note Engine    M1   the domain: notes, folders, tags,
                                 attachments, tasks — fully tested,
                                 no UI, no windows

  v0.3  Workspace           M2   the edge drawer, note list, editor, tray
        └─ FIRST DOGFOODABLE BUILD

  v0.5  Parity — content    M3   invisible markdown editor, formatting,
  v0.6  Parity — organize        checklists, colors, pinning, folding,
  v0.7  Parity — find            folders, search, keyboard, themes
  v0.8  Parity — data            import, export, backup, floating notes

  v0.9  Hardened            M4   performance, reliability, accessibility,
                                 DPI, migration, packaging
        └─ ═══ SIDENOTES PARITY COMPLETE ═══
```

**v0.3 matters most in the near term.** It is the first build usable daily, and
everything after it is informed by living with the product rather than
reasoning about it.

**v0.9 is the real milestone.** It is the point at which Noto is a product
rather than a project.

---

## In scope for the first release

Driven by the parity specification, not by this list. Summarised:

| Area | Notes |
| ---- | ----- |
| **Foundation** | Native Windows shell, local SQLite, migrations, logging, settings, tray, startup, error handling, crash recovery |
| **Domain** | Notes, folders (flat), tags, attachments, tasks — with commands and events (ADR-010) |
| **Workspace** | Edge drawer, left/right, auto-hide, width, global shortcut, note list, folder navigation, drag & drop |
| **Editor** | **Invisible markdown** (ADR-004, revised), formatting, checklists, code mode, colour swatches, links, separators |
| **Organization** | Pinning, folding, reordering, colours, moving between folders |
| **Search** | FTS5 full-text, keyboard-first |
| **Floating notes** | A presentation kind (ADR-009), not a separate architecture |
| **Appearance** | Light/dark/system, high contrast, fonts, text size, design tokens (ADR-011) |
| **Keyboard** | The full parity shortcut set; **every activation surface independently disableable** |
| **Data** | Import, **export to markdown**, backup, restore |
| **Integration** | `noto://` protocol, clipboard, drag & drop |
| **Privacy** | Per-note lock, no telemetry, capture exclusion |
| **Quality** | Performance budgets measured, accessibility, mixed-DPI multi-monitor, Win10 22H2 and Win11 24H2 |

---

## Explicitly out of scope for the first release

Each deferred to a named milestone, with the reason:

| Deferred | To | Why |
| -------- | -- | --- |
| **Contextual notes** — app and window binding | M6 | Not a SideNotes feature. Arrives as a new presentation kind (ADR-009), so deferring it costs nothing architecturally. |
| **Window following, durable identity** | M6 | Depends on contextual notes. Also the likeliest patent exposure — needs the prior-art check first. |
| **Screenshot capture** | M5 | A Windows enhancement, not parity. |
| **Notifications, Jump Lists, Explorer integration, Windows Hello** | M5 | Windows enhancements. Real value, not parity. |
| **Competitor features** | M7 | [competitor-enhancements.md](competitor-enhancements.md), deliberately kept separate. |
| **Tables in the editor** | post-parity | SideNotes' documentation does not include them, so their absence is **not** a parity gap. |
| **Nested folders** | post-parity | SideNotes appears flat. Deeper nesting is not parity and must not be added speculatively. |
| **Cloud sync, accounts** | post-v1 | ADR-002. Bring-your-own storage if ever. |
| **AI, MCP, plugins, mobile, web, collaboration** | rejected or post-v1 | Vision and principle 9. |

---

## Where Noto exceeds SideNotes in the first release

Deliberately small, and each justified rather than opportunistic:

| Noto does | SideNotes | Why it is in scope anyway |
| --------- | --------- | ------------------------- |
| **Export to markdown files** | No documented bulk or text export | ADR-002 promises the data outlives the app. Shipping without it would leave that promise unbacked. |
| **Soft delete and a recycle bin** | No trash or undo for deleted notes; recovery is backups only | For a local-first product whose data cannot be recovered from a server, destructive delete on a keystroke is unacceptable. |
| **Keyboard-only activation, every trigger disableable** | Three mutually exclusive activation modes; keyboard-only is the most-requested missing feature | Principle 7, and a direct answer to years of user complaints. |

These are the only deliberate excesses. Everything else beyond parity waits.

---

## Success criteria

The first release succeeds if, after two weeks of daily use:

- Noto is still installed and running
- the user has stopped keeping a scratch file
- nothing has been lost
- the performance budgets hold at a realistic note count
- nothing about it feels like a prototype

The last one is the real test. A capable demo and a product differ by exactly
the work in M4, and that milestone is a gate for this reason.

---

## Known risks

| Risk | Mitigation |
| ---- | ---------- |
| Framework gate fails and Noto moves to WPF | The window spike is the first work in M0, before any application code |
| **Parity scope is larger than it looks** — the inventory found ~273 items, 47 shortcuts and 40 preferences | The specification is derived from source documentation, so the size is known rather than discovered. M3 is deliberately the longest milestone. |
| The invisible-markdown editor is underestimated | SideNotes needed a full editor rebuild (1.5) to ship it. Treated as a major piece of work, not a display toggle. |
| Mixed-DPI multi-monitor defects | In the test matrix from M2 |
| Scope creep from competitor features during M3 | [competitor-enhancements.md](competitor-enhancements.md) exists to hold them, and M4 is a gate |
| Several parity rows are provisional | ~17 inventory items need hands-on verification; marked provisional in the specification rather than guessed |
