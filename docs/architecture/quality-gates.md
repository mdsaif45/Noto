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

### v0.1 budgets (2026-11-06, the performance and stability slice)

Measured on frozen Release `4cbd0f4` (the slice's final product code), on AC power, with `Invoke-Baseline.ps1 -Repeat 5`
(`baseline-20261106-123717-530951`) and `Invoke-Performance.ps1` (`performance-20261106-124259-114b9a`). Workspaces:
**S** 5 folders / 50 notes; **L** 105 folders / 10,512 notes (+200 in the Recycle Bin), a 500-card folder, notes of 5, 20,
50, 100 and 250 KB, a note of 20 real PNGs (one 4000 × 3000), 14 MB database, 12 MB of pictures. Times are Noto's own
traces and the latency from the key to them; *median / p90* unless marked. Same machine as #11 (20 logical processors,
24 GB, Windows 11, one display, Defender on); a browser was open and idle.

| Path | v0.1 budget | Measured | Verdict |
| ---- | ----------- | -------- | ------- |
| Hotkey: hidden → shown, in front | < 100 ms | 14 / 29 ms (17 in `Invoke-Performance`) | met |
| Warm start: hotkey registered | ≤ 350 ms | 299–318 ms | met |
| Warm start: hidden `--login` start, first chord served | ≤ 750 ms | 581 ms | met |
| Warm start: process → folder list drawn | ≤ 1,000 ms p90 | S 672 / L 687 (p90 750) ms; the list itself read and drawn in 64 ms at 105 folders | met |
| First start after install (unbuffered copy, antivirus scan) | ≤ 4 s | **As installed** (self-contained, packaging slice): L folder list median 3,613 ms, 5 runs 3,328–4,228. The build measured before shared the machine's .NET: 2,874–3,207 ms. Warm starts unchanged (S 637–682, L 650–676 ms either way) | **met at the median; 2 of 5 runs over** (the owner judges it on the installed v0.1, 2026-11-06: [release-v0.1.md](../roadmap/release-v0.1.md) §2) |
| A folder opened: 500 cards / ~100 cards | ≤ 200 ms p90 | 58 / 155 · 21 / 42 ms | met |
| Scrolling 500 cards (End, Home) | ≤ 100 ms p90 | 19 / 80 · 49 / 69 ms | met |
| A note opened, Enter → first frame drawn | ≤ 100 ms to 20 KB · ≤ 300 ms to 100 KB · ≤ 1 s at 250 KB | 5 KB 48 · 20 KB 78 · 50 KB 139 · 100 KB 264 · 250 KB 611 ms | met |
| A keystroke, key → change seen, any size to 250 KB | ≤ 50 ms p90 | p90 23 (5 KB) · 27 (20 KB) · 21 (50 KB) · 26 (100 KB) · 32 (250 KB) ms | met |
| Bold on a word | ≤ 50 ms | 10–19 ms | met |
| A paste over the whole note | ≤ 300 ms to 100 KB · ≤ 1 s at 250 KB | 50 KB 164 · 100 KB 237 · 250 KB 503 ms | met |
| A save (the save alone) | ≤ 10 ms | 0.8 (5 KB) – 4.3 (250 KB) ms | met |
| Leaving the editor, to the first frame drawn | ≤ 150 ms to 100 KB · ≤ 500 ms at 250 KB | 50 · 127 · 65 · 115 · 307 ms | met |
| Search at 10,000 notes, key → results drawn | ≤ 100 ms p90 | common word 41 / 55–61 · rare 9 / 18 · none 9–11 / 13–16 ms (the query itself 17–18 / 41–43 ms) | met |
| A note of 20 pictures: all drawn | ≤ 200 ms p90 | 73 / 96 ms | met |
| A 2400 × 1600 picture pasted, into the note | ≤ 300 ms p90 | 132 / 195 ms | met |
| Backup, export, import, restore: 2,000 notes, 150 MB of pictures (end to end, Windows' file dialog included) | recorded | 2.4 · 4.0 · 3.1 · 3.2 s (restore: confirm → the new window) | recorded |
| Idle CPU, hidden and shown | < 0.1% | 0 ms in 60 s | met |
| Idle memory | < 150 MB working set | hidden 152.1 MB (private 76.8) · shown 171.0 MB (private 92.3) | **not met** (deferred by the owner, 2026-11-06); **met in v0.2** (F22, below) |

**Why some budgets changed from #11 (decisions, not conveniences):**

- *Warm start < 300 ms* was one number for four different moments. #11 measured them apart (hotkey registered, first chord
  served, window, folder list); the user's need is that the hotkey answers soon after sign-in. The hotkey keeps a 300-class
  budget (≤ 350 ms); the list on screen gets its own (≤ 1 s p90), which a WinUI 3 window cannot reach in 300 ms on this
  machine (the window alone is about 500 ms, on every build since #11).
- *Cold start < 1 s* assumed a cold start after sign-in, which cannot be measured from inside a session (#11). What is
  measured is the first start after an install, files uncached and scanned: it gets the budget it can be held to (≤ 4 s).
  The installed Noto carries its own .NET runtime (ADR-008: nothing to install first), about 100 more files to read
  and scan on a first start than a build using the machine's .NET, which the earlier measurement was. Same machine,
  runs interleaved: +0.6 s at the median. Warm starts do not change.
- *Note switch < 50 ms* and *large note render < 200 ms* predate the styled editor (ADR-017), which styles the note in one
  RTF call: they become budgets per note size, the ones a user feels (≤ 100 ms to 20 KB, ≤ 300 ms to 100 KB).
- *Search < 100 ms at 10,000 notes* is unchanged and now measured: met.
- New budgets where the slice found defects: a keystroke (was 104 ms at 50 KB and 264 ms at 100 KB), a paste over the
  note (was 4.5 s at 50 KB and 15.1 s at 100 KB), and stability (below).

**Before and after this slice** (`Invoke-Baseline.ps1 -Repeat 5` on `main` `3b6a3ac` with only the slice's two timing
traces, `baseline-20261105-160821-9a8a6b`, against the final build; same machine):

| Path | Before | After |
| ---- | ------ | ----- |
| A keystroke, 20 / 50 / 100 KB (median, key → change) | 70 / 103 / 264 ms | 16 / 16 / 20 ms |
| A paste over the whole note, 50 / 100 KB | 4,520 / 15,107 ms | 164 / 237 ms |
| 250 KB note | the run could not measure it (no save within 60 s) | open 611 · key 23 · paste 503 ms |
| Replacing all of a ~100 KB note twice among 50 cards | Noto ended (0xC000027B) | 10 cycles at 100 KB, 6 at 150 KB: none |

**Stability** (`Invoke-Stability.ps1` on the same final build: `stability-20261105-173347-8b5c41`, plus the earlier
build in `stability-20261105-162208-48f44a`): 20 starts and closes (close 84 ms median), 20 edits saved by leaving (20/20
in the database), 100 hide/show, 60 note switches over 5 to 250 KB, 60 folder switches, 20 searches typed, 20 opens of a
20-picture note, and 5 kills while editing followed by a start (database intact, autosaved text kept, 5/5). After every
step the window answered within 2 s. **0 crash events and 0 hangs**; the whole campaign (15 runtime harnesses, 457
checks, the baseline, stability and performance runs) logged 0 crash events for Noto.

**Memory, measured, not yet a budget.** Handles, GDI and USER objects do not grow (100 hide/show: handles +29, GDI +0,
USER −1; folders and search flat). The managed heap stays under 15 MB (9 MB after a full collection, 70 switches). The
rest is WinUI's native memory: about 150 MB of working set for the framework at idle; ordinary use (20 notes edited)
settles at about 225 MB working set / 130 MB private. **A very large note costs a lot and keeps it**: opening and
leaving a 250 KB note repeatedly rises to about 600 MB private and stays there (a high-water mark, not growth: flat from
the 20th to the 40th time); its read view alone, never edited, about 240–340 MB. Every card renders its whole note,
however large. The owner deferred this (2026-11-06); the measured candidate is a preview of very large notes in the stack.

**Idle memory in v0.2 (F22, #160, 2026-11-13).** Measured on frozen Release, on AC power, with the existing measurement
(`Invoke-Performance.ps1 -Runs 5 -IdleSeconds 60 -NoteCount 1000`, `performance-20261113-102642-7568f9`):

| | v0.1 | v0.2 before F22 | v0.2 |
|---|---|---|---|
| Hidden: working set (private) | 152.1 (76.8) MB | 162.3 (81.2) MB | **7.2** (82.2) MB |
| Shown: working set (private) | 171.0 (92.3) MB | 177.7 (94.2) MB | **72.8** (96.9) MB |
| Idle CPU, hidden / shown | 0 / 0 ms in 60 s | 703 → 0 / 0 ms | 0 / 0 ms |
| Hotkey: hidden → shown, in front | 17 ms | — | 16 ms median, 46 ms max (the show after a trim) |

- **What changed.** Once Noto has been hidden for 5 s (after a hide, a hidden start at sign-in, or an automatic backup
  check), it asks Windows to trim its working set (`ProcessMemory.Trim`, `SetProcessWorkingSetSize(-1, -1)`). Nothing is
  freed or lost: private bytes are unchanged, and each page comes back when it is touched. The working set while shown is
  what a show and 60 s shown touch, not the whole framework.
- **What it costs.** The first show after a trim reads its pages back: in front within 46 ms (the budget is 100), but
  the drawer's slide starts about 40 ms later and takes about 250 ms instead of 167–190 (measured from Noto's own
  traces). Later shows are unchanged.
- **Measuring idle.** The first automatic backup check (F15) runs 30 s after a start and writes a whole backup once a
  day: start-up work, not idle. It had landed inside the hidden minute (703 ms of CPU); the measurement now starts once
  that check has run (each check traces how it ended). A second check 30 s after the first, an F15 timer bug, is fixed.
- **Not changed:** WinUI's own memory. The managed heap stays under 15 MB; turning concurrent GC off saved about 1 MB.

#### The #11 budgets, kept as history (2026-10-13)

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
| Save | no budget | hotkey hide → the row seen in the database (*observed*, the method before the save trace): short 26 · large 27 ms | recorded only |

**What "save latency" means.** Since the save trace (#11), a save is timed by
Noto's own `Noto.Save completed` line, written in the editor's save path once
the save has returned from the database (the handler returns after the write
commits; with `synchronous = NORMAL` that is committed, not yet flushed to
disk), and never written for a failed save. Three numbers are recorded per
save:

| Measure | From → to | What it shows |
| ------- | --------- | ------------- |
| **duration** (primary) | measured inside Noto, carried on the trace (`ms=`) | the save alone: the command and the database write |
| **completed** | the hotkey chord → the `Noto.Save completed` trace | the user-visible save on hide: the hotkey, the hide request, the save |
| **observed** | the hotkey chord → the row seen in the database by the harness | not a latency: a correctness check that the row really changed. It was the only save measure before the trace. Each poll is a query from the harness's own process, so it resolves only to that query's cost, in steps of tens of milliseconds |

Each hide must trace exactly one save, stamped after the chord and no sooner
after it than the save it reports took, and the row must hold the new text;
otherwise the run stops. A trace from an earlier save cannot be counted as a
later one.

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
sign-in. No repository document says an agent counts as "someone other than its
author", so these runs do not by themselves meet #11's requirement: a human
reproduction, or a recorded owner decision, does. Builds are not byte-identical across clones (the paths embedded in
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
