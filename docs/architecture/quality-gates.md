# Quality Gates — Performance, Testing, Privacy

**Status:** Draft — budgets provisional until measured
**Last updated:** 2026-09-14

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

| Measure | Budget | Confidence |
| ------- | ------ | ---------- |
| Cold start to usable | < 1s | Provisional |
| Warm start | < 300ms | Provisional |
| Sidebar open (after first) | < 100ms, perceptually instant | Provisional |
| Note switch | < 50ms | Provisional |
| Search results | < 100ms at 10k notes | Provisional |
| Idle CPU | < 0.1% | Provisional |
| Idle memory | < 150MB working set | Provisional |
| Large note (100KB) render | < 200ms | Provisional |

**"Usable" means the global hotkey responds**, not that every window is built.
Startup is staged deliberately (architecture-overview.md).

### Provisional means provisional

These numbers are informed estimates, not measurements. Issue #11 establishes
the harness and a baseline; each budget is then confirmed or revised **with
evidence**, and this table is updated.

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
