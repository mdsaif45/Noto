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

| Measure | Budget | Baseline (#11), median of 5 (first chord served: 4) | Verdict |
| ------- | ------ | --------------------------- | ------- |
| Cold start to usable | < 1s | **First start after an install or update** (none of Noto's files cached; antivirus scans them first): hotkey registered 1,878 · window 2,128 · tray added 2,338 ms. **A cold start after sign-in is not measured** (see below) | **not met** for a first start after install |
| Warm start | < 300ms | hotkey registered 306 · first chord served 490 · window 432 · folder list 678 ms | **not met** |
| Sidebar open (after first) | < 100ms, perceptually instant | hotkey → shown and in front 29 ms (max 30) | met |
| Note switch | < 50ms | short note: editor 32 · text loaded 56 ms (max 69; medians 56–76 across six runs of this build) | **not met** |
| Search results | < 100ms at 10k notes | — | not measurable: no search exists (#17) |
| Idle CPU | < 0.1% | hidden 16 ms in 60 s (0.026%) · shown 0 ms | met |
| Idle memory | < 150MB working set | hidden 147.6 MB (private 76.7) · shown 161.0 MB (private 87.3) | met hidden, **not met shown** |
| Large note (100KB) render | < 200ms | 100,636 chars: editor 451 · text loaded 479 ms | **not met** |
| Hidden start at sign-in (`--login`) | no budget | hotkey registered 297 · first chord served 490 ms, no window | recorded only |
| Save | no budget | hotkey hide → the row in the database: short 26 · large 27 ms | recorded only |

**"Usable" means the global hotkey responds**, not that every window is built.
Three moments are measured, because they are different claims:

| Moment | What it proves |
| ------ | -------------- |
| hotkey registered | `RegisterHotKey` has succeeded (Noto traces `Noto.Hotkey registered`). The earliest a press can be delivered |
| first chord served | The chord, pressed the moment the hotkey is registered during a `--login` start, has shown the window in front. **This is "the hotkey responds"**: it also needs Noto's UI thread to be pumping messages |
| tray added | The tray icon exists (`Noto.Tray added`). Not a hotkey measure |

Startup is staged deliberately (architecture-overview.md).

**The baseline changed no budget.** Five are not met; they stay as written
and are M4 work (the M4 gate below), not a reason to revise a number. "Large
note render" was measured on the plain-text editor that exists today, not on
rendered markdown (#14).

### The baseline (#11)

`tools/validation/Invoke-Performance.ps1` measures the real application from
outside its process, through the runtime validation harness: an isolated data
root, Noto started by the shell, its own virtual desktop, real data checked
unchanged. Every timing is the kernel's process creation time, a window
becoming visible, an element in UI Automation, a row in the database, or a
line Noto traces. It resolves to about 10–30 ms, and the numbers compare
between runs on one machine, not across machines. Medians are true medians.

From the repository root:

```
dotnet build Noto.sln -c Release
pwsh -NoProfile -File tools/validation/Invoke-Performance.ps1 -NotoExe src\Noto.Windows\bin\Release\net9.0-windows10.0.19041.0\win-x64\Noto.exe [-Runs 5] [-IdleSeconds 60] [-NoteCount 1000]
```

A run takes about 5 minutes at the defaults. It needs a build with `--login`
(#94), and it uses no negative controls, so its summary line reads
`refused 0/0`. Every measure is a median of `-Runs` samples, except first
chord served, which leaves the last `--login` start unchorded for the hidden
idle measurement (4 of 5).

| Recorded | |
| -------- | - |
| Build | frozen Release `c36674b`: `main` @ `acb9e75` with PRs #96–#101 merged locally (#94, #95, #20, #7, #10, #11), .NET SDK 10.0.301 (`global.json` rolls forward from 9.0.205). Later commits to #101 change only the script and these documents, not the application |
| Machine | 20 logical processors, 24 GB, Windows 11, one display, Windows Defender real-time protection on |
| Run | `performance-20261013-141011-0c2351`, `-Runs 5 -IdleSeconds 60 -NoteCount 1000`, every check passed |
| Data | 1,000 notes in 20 folders; the measured folder holds 50, among them a 57-character note and a 100,636-character Markdown-style note |
| Conditions | `cua-driver` and `codex-windows-sandbox-service` idle in the background |

**How the cold start is measured.** The run's first start is from a copy of
the build made with unbuffered I/O (`robocopy /J`) into a new folder, so none
of Noto's own files are in the file cache. That is exactly a first start after
an install or update, including the antivirus scan of new files. It is an
**upper bound** on a cold start after sign-in, where the files are uncached
but already scanned. That second number needs the first Noto start after a
restart, which cannot be produced without ending the session doing the
measuring; it is not recorded here.

**Independent reproduction.** Issue #11 asks for the procedure to be followed
by someone other than its author. Two runs were made by separate agents with
none of the author's context, each from a fresh clone, its own Release build
and only these documents, on the same machine:

| Run | Result |
| --- | ------ |
| `performance-20261013-133632-f86041` | **INVALID** in the large-note stage. It found a race in the script (leaving the editor re-queries the note list); the script was fixed |
| `performance-20261013-141809-767934` | Every check passed; **every verdict above reproduced**. Cold window 1,835 · warm window 470 · hotkey registered 333 · first chord served 491 · short-note load 73 · large-note load 505 ms; idle 147.5 / 160.8 MB |

Not reproduced: a different machine, a different person, or a cold start after
sign-in. Builds are not byte-identical across clones (the paths embedded in
them differ), so a reproduction compares measurements, not binaries.

**Run to run.** Medians of separate runs on the same build differ noticeably:
across the eight runs of this code, the warm window took 417–502 ms and the
short-note load 56–76 ms. One start in five can be about 1 s slower than the rest, which
is why `performance.json` keeps every value with the median, min and max.

**Not covered.** Search (none exists), stores far beyond 1,000 notes (the
search budget's 10,000), more than one display, Windows 10, and rendered
markdown. Each is measured when what it measures exists.

**Motion and the budgets (Slice 1.3).** The drawer's content animates for
250 ms on show and 167 ms on hide, on the compositor. "Sidebar open" is
measured to the window visible and in front, which the animation does not
delay; the content finishes arriving after that. A hide becomes invisible
167 ms later than before; the save before it is unchanged. Expected cost: no
start-up work (one motion object reads seven resources when the window is
built), no idle CPU (no timer or subscription), no work per frame in Noto's
code. To compare before and after, measure warm start, hotkey show and idle
memory with `Invoke-Performance.ps1` on the build before motion and the build
with it, and read each transition's start and end from the `Noto.Motion` trace
(`Invoke-Motion.ps1`).

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
| **Log redaction** | Note content must never reach a log, including via exception data. Held by the log's types (`ExceptionSummary`, `DataRootPath`, typed events) and checked by `DiagnosticsContractTests` and `RollingFileLogTests` (#7). |
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
