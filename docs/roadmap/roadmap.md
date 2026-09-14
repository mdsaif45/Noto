# Roadmap

**Status:** Proposed
**Last updated:** 2026-09-14

Milestones are **capability targets, not dates.** Noto has no external deadline,
and inventing one would only distort scope decisions.

---

## Shape

```
  M0  Foundation        ████████████        it builds, starts, persists
  M1  Core Notes        ████████████████    it holds notes
  M2  Workspace         ████████████████    ◄── FIRST DOGFOODABLE (v0.2)
  M3  Search            ████████            it is a good notes app (v0.3)
  M4  Floating Notes    ████████████
  M5  Context Engine    ████████████████    ◄── THE THESIS (v0.4 / v0.5)
  M6  Capture           ██████████
  M7  Windows Integr.   ████████████
  M8  Polish            ████████████████
  v1.0 Release          ████████
```

Two milestones matter more than the rest:

- **M2** is the first build that can be used daily. Everything after it is
  informed by living with the product rather than reasoning about it.
- **M5** validates or refutes the entire product thesis.

---

## M0 — Foundation

*Exit: an empty WinUI 3 app builds in CI, starts, logs, persists settings, and
has a migrated SQLite database.*

- repository governance, ADRs, architecture documentation ✅
- solution and project structure (ADR-001)
- **the three ADR-001 validation spikes** — see below
- structured logging that never records note content
- global error handling
- settings persistence
- SQLite, connection management, migration runner (ADR-003)
- test infrastructure
- CI building and testing for real

### The validation gate

Three throwaway spikes that can reverse the framework decision:

```
  spike 1   opacity + whole-window click-through
            on Win10 22H2 AND Win11 24H2
  spike 2   file drag & drop from Explorer
  spike 3   follow a window via EVENT_OBJECT_LOCATIONCHANGE
            during a fast drag, CPU measured
```

**If spikes 1 or 2 fail, Noto moves to WPF before application code is
written.** They are first in M0 for exactly this reason — a framework switch
costs days here and months later.

---

## M1 — Core Notes

*Exit: notes survive restart and are editable.*

- note, folder, tag, attachment entities and migrations (data-model.md)
- repositories
- create, edit, delete, restore from recycle bin
- markdown editing — plain source (ADR-004)
- task lists, code blocks
- folders (one level), tags, colors, pin, archive
- auto-save
- attachment storage on disk
- **backup and export to markdown** — required early, because ADR-002 promises
  the data outlives the app and that promise should never be unbacked

---

## M2 — Workspace ◄ v0.2, first dogfoodable

*Exit: the primary interaction model works.*

- edge-docked sidebar, snapped topmost window (ADR-007)
- left/right, auto-hide, slide-in, adjustable width
- global shortcut
- folder navigation, note list, reorder, drag & drop
- **complete keyboard navigation**
- **every activation surface independently disableable** (principle 7)
- system tray, run at login
- per-monitor DPI and multi-monitor correctness

> From here on, Noto is used daily by its author. Every subsequent milestone
> should be re-examined against what that reveals.

---

## M3 — Search ◄ v0.3

*Exit: find any note in under a second without touching the mouse.*

- FTS5 external-content table and triggers (ADR-003)
- **query escaping, with the apostrophe test**
- search UI, results as you type
- filter by folder and tag
- keyboard-first throughout

> Placed before floating notes, departing from the initial ordering: search is
> a dependency of the core workflow, while floating notes are an additive
> second mode.

---

## M4 — Floating Notes

*Exit: a note can live on the desktop across restarts.*

- promote a note to an independent window
- always-on-top, resize, move, persisted position
- restore to the correct monitor, and **never off-screen** when it is absent
- opacity via XAML (ADR-007)
- lock
- whole-window ghost mode, with hotkey and tray exit
- **screen-capture exclusion** — one API call, high perceived value
- multiple floating notes

---

## M5 — Context Engine ◄ the thesis

*Exit: notes appear with the work they belong to.*

### v0.4 — application level

- `EVENT_SYSTEM_FOREGROUND` observer, debounced, never polling
- **AUMID resolution for packaged apps** — without it, binding is useless for
  Store applications
- `ContextResolver` and `BindingMatcher` as pure, unit-tested functions
- bind a note to an application in one gesture
- show and hide on focus, **without stealing focus**
- the confidence floor: unclear context shows nothing (ADR-005)

> **Decision point.** If application-level context does not feel valuable in
> two weeks of daily use, the thesis is wrong — and that must be discovered
> before building the harder version.

### v0.5 — window level

- composite fingerprint identity (ADR-006)
- `detached` as a first-class, non-alarming state
- scoped `EVENT_OBJECT_LOCATIONCHANGE`, coalesced, hidden during drag
- follow window position and size
- minimise, restore, monitor change, occlusion

**Before v0.5 begins:** the @/Anchored hands-on evaluation and the
"Patent Pending" prior-art check (competitive-analysis.md §6, items 2 and 3).

---

## M6 — Capture

*Exit: capture without leaving the current application.*

- global hotkey quick capture — type, save, return
- clipboard capture
- drag & drop text and files
- screenshot capture via `Windows.Graphics.Capture`
- image and file to note
- URL capture

---

## M7 — Windows Integration

- app notifications (requires the sparse package, ADR-008)
- `noto://` protocol activation
- File Explorer integration
- Windows Search
- Windows Hello for locked notes
- virtual desktop behavior via `DWMWA_CLOAKED` (ADR-007)
- multi-monitor and DPI hardening

---

## M8 — Polish

- accessibility: screen reader, high contrast, focus order
- **live-styled markdown editing** (ADR-004) — a refinement, not a rewrite
- animation and visual polish
- performance: measure every budget in principle 3 under a realistic note count
- memory profiling
- crash recovery
- settings UX
- signed installer, sparse package, Velopack auto-update (ADR-008)

---

## v1.0

- security review against SECURITY.md scope
- performance validation
- documentation and screenshots
- migration and backup verification **from every prior version**
- code-signing certificate — *confirm cost and process early, not here*
- release

---

## Explicitly post-v1

| Deferred | Note |
| -------- | ---- |
| Document- and URL-level context | The real differentiator. Needs its own research and ADR. |
| BYO-folder sync | ADR-002. Requires solving the WAL-under-file-sync hazard first. |
| Tables and images in notes | Each needs its own justification (ADR-004). |
| AI / MCP surface | Explicitly **not** the answer to context resolution (ADR-005). |
| Mobile, web, collaboration, plugins, cross-platform | Rejected by the vision. |

---

## Sequencing risks

| Risk | Where it bites | Mitigation |
| ---- | -------------- | ---------- |
| Framework decision reverses | M0 → everything | Spikes are first in M0 |
| Thesis is wrong | M5 | v0.4 before v0.5, deliberately |
| Mixed-DPI defects surface late | M2, M4, M5 | In the test matrix from M2 |
| Scope creep after dogfooding | M2 onward | mvp.md and principle 9 |
| Patent exposure | v0.5 | Prior-art check before M5 window work |
| Signing cost discovered at release | v1.0 | **Confirm during M0** |
