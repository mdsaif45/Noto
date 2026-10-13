# Quality Gates — Performance, Testing, Privacy

**Status:** Draft — budgets provisional; first baseline measured (#11)
**Last updated:** 2026-10-13

---

## Why these are gates, not aspirations

"No compromise on quality" does not mean "ship nothing until everything is
perfect." It means:

> **Every completed slice is production-quality before the next one starts.**

The failure mode this prevents:

```
  BAD                          GOOD
  ────────────────────         ─────────────────────
  notes      70%               notes      done
  sidebar    20%               sidebar    done
  floating   15%               folders    in progress
  search     30%               search     not started
  context     5%
                               a working product,
  a fragile demo               partially featured
```

Both have the same amount of work in them. Only one can be used.

---

## Performance budgets

Noto is a background utility competing with a scratch file that is **already
open**. The moment a user weighs whether opening Noto is worth it, Noto has
lost.

| Measure | Budget | Baseline (#11), median of 5 | Verdict |
| ------- | ------ | --------------------------- | ------- |
| Cold start to usable | < 1s | **not yet measured** — needs a run immediately after a restart. A first start on a new data root, cache warm, which also creates the database: hotkey ready 599 ms | — |
| Warm start | < 300ms | window 387 · hotkey ready 500 · folder list 599 ms | **not met** |
| Sidebar open (after first) | < 100ms, perceptually instant | hotkey → shown and in front 29 ms (max 61) | met |
| Note switch | < 50ms | short note: editor 29 · text loaded 49 ms (max 58) | **borderline** |
| Search results | < 100ms at 10k notes | — | not measurable: no search exists (#17) |
| Idle CPU | < 0.1% | 0 ms of CPU in 60 s, hidden and shown (under one 15.6 ms clock tick: < 0.03%) | met |
| Idle memory | < 150MB working set | hidden 147.7 MB (private 76.7) · shown 160.0 MB (private 86.2) | met hidden, **not met shown** |
| Large note (100KB) render | < 200ms | 100,636 chars: editor 402 · text loaded 466 ms | **not met** |
| Hidden start at sign-in (`--login`) | no budget | hotkey ready 429 ms, no window | recorded only |
| Save | no budget | hotkey hide → the row in the database: short 35 · large 46 ms | recorded only |

**"Usable" means the global hotkey responds**, not that every window is built.
Noto registers the hotkey before it adds the tray icon, and traces the tray
icon; that trace is the "hotkey ready" time. Startup is staged deliberately
(architecture-overview.md).

**The baseline changed no budget.** Three are not met and one is borderline;
they stay as written and are M4 work (the M4 gate below), not a reason to
revise a number. "Large note render" was measured on the plain-text editor that
exists today, not on rendered markdown (#14).

### The baseline (#11)

`tools/validation/Invoke-Performance.ps1` measures the real application from
outside its process, through the runtime validation harness: an isolated data
root, Noto started by the shell, its own virtual desktop, real data checked
unchanged. Every timing is a poll of what a user would see — a window visible,
an element in UI Automation, a row in the database — so it resolves to about
10–30 ms. The numbers compare between runs on one machine, not across machines.

```
pwsh -NoProfile -File tools/validation/Invoke-Performance.ps1 -NotoExe <Release build>\Noto.exe [-Runs 5] [-IdleSeconds 60] [-NoteCount 1000]
```

| Recorded | |
| -------- | - |
| Build | frozen Release `02e5993`: `main` @ `acb9e75` with the open PRs #96–#100 (#94, #95, #20, #7, #10) merged locally |
| Machine | 20 logical processors, Windows 11, one display |
| Run | `performance-20261013-123947-1a9019`, `-Runs 5 -IdleSeconds 60 -NoteCount 1000`, every check passed |
| Data | 1,000 notes in 20 folders; the measured folder holds 50, among them a 57-character note and a 100,636-character Markdown-style note |
| Conditions | 31 minutes after a restart, after earlier runs (so no start was cold); `cua-driver` and `codex-windows-sandbox-service` idle in the background |

**Not covered.** Search (none exists), stores far beyond 1,000 notes (the
search budget's 10,000), more than one display, Windows 10, and rendered
markdown. Each is measured when what it measures
exists. A cold start is a cold start only when the run is the first Noto start
since Windows started; the script records the boot time to show it.

### Provisional means provisional

These numbers were informed estimates. #11 measured the first baseline above;
each budget is now confirmed or revised **with evidence**, and a revision is
recorded here as a decision with its reason.

A budget that is never measured is decoration. A budget revised because the
code got slow is worse than none — the revision must be justified by a reason
the original number was wrong, not by the current number being convenient.

### Rules

1. **Performance claims in a PR carry numbers.** "It feels fine" is not a
   measurement.
2. **Idle cost is the easiest thing to regress invisibly** — usually by
   registering a hook too broadly. Measure it when touching anything
   event-driven.
3. **Measure at a realistic note count**, not at ten notes.

---

## Testing strategy

Confidence in the foundation, not maximum test count.

```
  Noto.Core.Tests             fast, no OS         the bulk
    domain rules
    command handlers
    events
    ARCHITECTURE tests        <- the boundary guards (#20)

  Noto.Infrastructure.Tests   real SQLite         correctness of data
    every migration upgrade path
    FTS5 index consistency
    search, including query escaping
    backup and restore round trip

  Noto.Windows.Tests          desktop session     narrow, slow, few
    window positioning, DPI arithmetic
    hotkey registration

  Parity regression           per parity row      the definition of done
    each MUST row has a test or documented manual steps

  Manual                      documented          anything visual
    mixed-DPI multi-monitor
    monitor disconnect
    Win10 22H2 and Win11 24H2
```

### Non-negotiable coverage

These protect against the failures that are unrecoverable or invisible:

| Area | Why |
| ---- | --- |
| **Migration upgrade paths** | A migration that works on a fresh install and corrupts an existing one is the worst bug this product can have. Test from populated databases, not empty ones. |
| **Architecture boundaries** | ADR-009's rules are comments until a test fails the build. |
| **Log redaction** | Note content must never reach a log, including via exception data. |
| **FTS5 query escaping** | `MATCH` takes a query language; an apostrophe crashes it. |
| **Backup and restore** | ADR-002 promises the data outlives the app. |
| **Mixed-DPI multi-monitor** | The dominant recurring defect theme in comparable software. |
| **Non-US keyboard layouts** | On German, French and Polish layouts **AltGr arrives as `Ctrl+Alt`**, so a shortcut bound there swallows characters the user is typing. macOS has no analogue, so no competitor faced it. See sidenotes-parity.md §12a. |

### Rules

1. A test that cannot fail is worse than no test. **Verify each guard actually
   goes red** when violated.
2. Anything not automatable gets **documented manual steps in the PR**.
3. Parity rows without a test are not parity — they are claims.

---

## Privacy and data

Noto is local-first (ADR-002). The boundary, stated plainly so it can be
checked:

```
  ON THIS MACHINE ONLY
  ────────────────────────────────────────────
  %LOCALAPPDATA%\Noto\
    noto.db          notes, folders, tags
    attachments\     files, GUID-named
    backups\         plain .db snapshots
    logs\            NEVER note content or user paths

  LEAVES THE MACHINE
  ────────────────────────────────────────────
  nothing
```

### Commitments

- **No telemetry.** Not opt-out — absent. Any future telemetry would be opt-in,
  documented, and an ADR.
- **No network access** in the first release. Nothing to disable, because there
  is nothing there.
- **Logs never contain note content or user file paths**, including inside
  exception messages and stack traces — which capture arguments, and are the
  usual leak.
- **No arbitrary execution** from note content, ever, without an explicit
  documented permission model.
- **Uninstall leaves user data intact** by default, and asks before removing it.

### Threat model boundary

Noto does **not** defend against an attacker who already controls the Windows
user account. Per-note locking defends against a shoulder or a shared screen —
the realistic threat — not against a compromised OS.

Stating this prevents the more damaging error of implying stronger protection
than exists. The same applies to screen-capture exclusion, which is described
as "hide from screen sharing" and **never** as security (ADR-007).

---

## The M4 gate

M4 (Hardening) is a gate, not a phase. Expansion does not begin until:

- [ ] every performance budget measured and met, at a realistic note count
- [ ] every parity MUST row has a passing test or documented manual steps
- [ ] migration verified from every prior version, with data present
- [ ] backup and restore verified
- [ ] accessibility: screen reader, high contrast, focus order, full keyboard
- [ ] mixed-DPI multi-monitor verified on both target Windows versions
- [ ] the shortcut set verified on at least one non-US keyboard layout (AltGr)
- [ ] crash recovery verified
- [ ] install, upgrade and uninstall verified on clean machines
- [ ] no known data-loss defect

The temptation at that point will be to start contextual notes because it is
more interesting than hardening. **That is exactly the moment the gate exists
for.**
