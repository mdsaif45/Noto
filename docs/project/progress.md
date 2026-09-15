# Noto Development Status

```
Project:            Noto — a Windows-native notes application
Current milestone:  M1 — Core Note Engine  (in progress)
Current slice:      Slice 4 — Folder Commands  (MERGED); Slice 5 — tags not started
Overall status:     Backend engine under construction. No product UI exists.
Last updated:       2026-09-15
Evidence baseline:  main @ 7b13269 (PR #48 merged)
```

> **Read this first.** A working engine is not a working product. Noto currently
> has a tested domain and persistence layer behind a placeholder window. The
> interface a user would recognise as Noto has not been started.

---

## 1. Progress Line

```
M0  Foundation & Architecture     ██████░░░░  IN PROGRESS   9 issues open
        ↓
M1  Core Note Engine              ████████░░  IN PROGRESS   3 issues open
        ↓
M2  SideNotes Workspace           ░░░░░░░░░░  NOT STARTED   ◄ first real UI
        ↓
M3  SideNotes Feature Parity      ░░░░░░░░░░  NOT STARTED   ◄ the first product
        ↓
M4  Quality / Performance / UX    ░░░░░░░░░░  NOT STARTED
        ↓
M5  Windows-Native Enhancements   ░░░░░░░░░░  NOT STARTED
        ↓
M6  Contextual Notes              ░░░░░░░░░░  NOT STARTED
        ↓
M7  Competitor Features           ░░░░░░░░░░  NOT STARTED
        ↓
M8  Noto Differentiators          ░░░░░░░░░░  NOT STARTED
        ↓
M9  v1.0                          ░░░░░░░░░░  NOT STARTED
```

The bars are a reading aid, not a metric. Milestone status comes from GitHub
issue counts; nothing here is a derived percentage.

**M0 is deliberately shown as in progress, not complete.** Foundational work has
shipped — the solution structure, SQLite, migrations, architecture guards — but
nine M0 issues remain open, including several that later milestones depend on
(design tokens, structured logging, settings persistence, error handling).

---

## 2. Milestone Status

| Milestone | Status | Completed work | Current work | Next |
| --------- | ------ | -------------- | ------------ | ---- |
| **M0** Foundation & Architecture | **IN PROGRESS** (4 closed / 9 open) | Solution structure; ADRs 001–012; WinUI 3 + Windows App SDK validated; SQLite foundation; CI, CodeQL, branch protection | — | #22 design tokens, #21 commands/events, #20 architecture tests, #7 logging, #9 settings, #10 error handling, #11 perf harness |
| **M1** Core Note Engine | **IN PROGRESS** (0 closed / 3 open) | #13 slices 1–4 merged: domain types, title algorithm, failure model, note CRUD, ordering, lifecycle, recycle bin, **the seven folder commands** | — | **#13 Slice 5 — tags**; then #14 markdown, #15 export |
| **M2** SideNotes Workspace | **NOT STARTED** (1 closed / 2 open) | — | — | The first dogfoodable build; where real UI begins |
| **M3** SideNotes Parity | **NOT STARTED** | — | — | 0 of 264 parity rows implemented |
| **M4** Hardening | **NOT STARTED** | — | — | — |
| **M5** Windows Enhancements | **NOT STARTED** (no issues yet) | — | — | — |
| **M6** Contextual Notes | **NOT STARTED** (3 open) | ADR-005, ADR-006 drafted as **Proposed**, deferred to M6 | — | — |
| **M7** Competitor Features | **NOT STARTED** (no issues yet) | — | — | — |
| **M8** Noto Differentiators | **NOT STARTED** (no issues yet) | — | — | — |
| **M9** v1.0 | **NOT STARTED** (no issues yet) | — | — | — |

---

## 3. Last Slice Completed — Slice 4, folder commands

**Slice 4 — Folder Commands** merged as PR #48 → `7b13269`. All seven
operations exist, with the persistence surface they need.

```
CreateFolder   RenameFolder   DeleteFolder   RestoreFolder
PinFolder      UnpinFolder    ReorderFolder
```

**The ordering algorithm was generalised first.** O1–O6 lived inside
`SqliteNoteRepository`; folders needed the same rules, and a second copy is how
two implementations drift apart. The engine now works over any table with an
`Id`, a `SortOrder` and a `DeletedAt`.

```
src/Noto.Infrastructure/Storage/SortOrderEngine.cs       O1–O6, no domain types
src/Noto.Infrastructure/Storage/SortOrderScope.cs        table + scope predicate
src/Noto.Infrastructure/Storage/SortOrderPlacement.cs    first / after / last
src/Noto.Infrastructure/Storage/Timestamps.cs            the shared "O" format
src/Noto.Infrastructure/Storage/SqliteFolderRepository.cs  10 methods
src/Noto.UseCases/Folders/                                 7 commands + guard
```

**Existing note behaviour was preserved.** The extraction modified no test
file, and a normalised comparison against the pre-extraction source showed
every difference to be mechanical parameterisation — the threshold, the
midpoint arithmetic, the `SortOrder`/`Id` tie-break and the `ORDER BY` clauses
are unchanged.

**Two atomic cascades, each on one timestamp:**

```
DeleteFolder    Cases A + B   WHERE FolderId = @f AND DeletedAt IS NULL
RestoreFolder   Case D        WHERE FolderId = @f AND DeletedAt IS NOT NULL
```

Case D restores **every** still-deleted note pointing at the folder, including
one deleted independently beforehand. There is no provenance column and none
was added (deletion-semantics §5).

> **Two defects were found by review after the first green build** and fixed
> before merge. `PinFolder`/`UnpinFolder` stamped `UpdatedAt` even when the
> folder was already in the requested state, violating contract §4's rule that
> a command changing no row stamps nothing; and the repository's `Rename` and
> `SetPinned` omitted `AND DeletedAt IS NULL`. A passing suite, clean CI and
> clean CodeQL did not catch either — the mutation set had no mutant for the
> pin no-op. Both now have sensitivity tests proven to fail without the fix.

---

## 4. Completed Foundation

| Item | Status | Evidence |
| ---- | ------ | -------- |
| Solution structure — Core / UseCases / Infrastructure / Platform / Windows | DONE | 8 projects; dependencies point inward |
| ADRs 001–012 | ACCEPTED (005, 006 Proposed, deferred to M6) | `docs/decisions/` |
| WinUI 3 + Windows App SDK validated | DONE | ADR-001 "validation gate passed" |
| SQLite foundation, WAL, pragmas | DONE | `NotoDatabase`; ADR-003 |
| Schema v1 | DONE | `SchemaV1.cs` |
| **Schema v2 / Migration 002** | DONE | PR #38 → `8b86a5a` — drops `Notes.Title`, adds `Folders.IsPinned` + `DeletedAt` |
| Migration safety copy | DONE | `MigrationSafetyCopy` — `BackupDatabase`, not a file copy |
| Deletion semantics, Cases A–G | ACCEPTED | PR #37 → `a6c3358`; Case D corrected in PR #39 → `4cc669b` |
| **Frozen Core Note Engine contract** | FROZEN | PR #40 → `61bfdfb` — 23 commands, 7 queries, I1–I6, O1–O6 |
| Note engine Slice 1 — types, title, failure model, `CreateNote`, `GetNote` | DONE | PR #41 → `c258c79` |
| Slice 2a — content, move, reorder, ordering, renormalisation | DONE | PR #42 → `77a4f43` |
| Note palette keys `note1`–`note6` | DONE | PR #43 → `a14e2a8` (ADR-011) |
| Slice 2b — pin, fold, colour | DONE | PR #44 → `63efc6d` |
| Slice 3 — delete, restore, recycle bin | DONE | PR #45 → `9d81c21` |
| **ULID C′ contract + implementation** | DONE | PR #46 → `fa0996d` — O(1) state, 80-bit CSPRNG kept |
| **Test-infrastructure pool isolation** | DONE | PR #47 → `4f49003` — replaced process-global `ClearAllPools` |
| Generic ordering engine | DONE | PR #48 → `7b13269` — domain-neutral, shared by notes and folders |
| Slice 4 — the seven folder commands | DONE | PR #48 → `7b13269` — Cases A, B, D, G; two atomic cascades |
| CI — build/test, CodeQL, C# analysis, docs governance | DONE | `.github/workflows/ci.yml`; branch protection on `main` |

---

## 5. SideNotes Parity Progress

**Authoritative source:** `docs/product/sidenotes-parity.md` — unmodified.

```
264 parity rows total
  0 marked implemented
```

| Area | Status | Evidence |
| ---- | ------ | -------- |
| Note CRUD, ordering, lifecycle (B1, B8, B10–B16, B19, B23) | **Engine only — no UI** | Slices 1–3; commands exist, nothing invokes them |
| Folder create/rename/delete/pin/reorder (C2, C8, C9, C11, C13) | **Engine only — no UI** | Slice 4; commands exist, nothing invokes them |
| Note colours (B15) | **Engine only** | `note1`–`note6`, ADR-011 |
| Recycle bin (B23, C11) | **Engine only** | `ListDeletedNotes` / `ListDeletedFolders` |
| Markdown editing, invisible markdown (ADR-004, D-rows) | **Not started** | #14 open |
| Export (H-rows) | **Not started** | #15 open |
| Everything visual — workspace, sidebar, editor, themes, shortcuts | **Not started** | M2/M3 |
| Nested folders (C3), folder colours (C17), all-notes view (C18) | **DROP / not a requirement** | parity specification |

**No parity row may be marked implemented until the feature is reachable by a
user.** A command handler with tests is engine work, not parity.

---

## 6. UI / UX Status

This distinction matters more than any other line in this document.

### Technical Windows foundation — validated

| | Status | Evidence |
| - | ------ | -------- |
| WinUI 3 / Windows App SDK app launches | DONE | ADR-001 validation gate |
| Window creation, edge positioning, topmost | SPIKED | `Noto.Platform.Windows/WindowPlacement.cs`; #2 spike |
| DPI / multi-monitor behaviour investigated | RESEARCHED | `docs/research/` |
| Packaging and identity decided | DECIDED | ADR-008 |

### Actual Noto UI / UX — **not started**

| | Status |
| - | ------ |
| Visual design language | **NOT STARTED** |
| Design tokens / design system | **NOT STARTED** — #22 open |
| Workspace layout, sidebar, drawer | **NOT STARTED** — M2 |
| Note editor | **NOT STARTED** — #14 |
| Folder UI | **NOT STARTED** |
| Typography, components, motion, polish | **NOT STARTED** — ADR-011 defines tokens on paper only |

The entire current interface is 27 lines of XAML whose own comment reads:

> *"Bootstrap placeholder. The real interface begins at M2 (SideNotes Workspace)
> and is built on the design tokens from #22. Deliberately unstyled — there is no
> design system to style it with yet."*

```
WinUI 3 validated   ≠   Noto UI designed
```

---

## 7. Current Architecture

```
  Noto.Windows            WinUI 3 app — placeholder shell only
        ↓
  Noto.UseCases           commands + handlers, one per operation
        ↓
  Noto.Core               domain types, repository INTERFACES, failure model
        ↑
  Noto.Infrastructure     SQLite implementations, migrations, ordering engine
        ↓
      SQLite

  Noto.Platform.Windows   Win32 interop (window placement)
        ↓
  Windows / Win32
```

**Boundaries that are enforced, not merely intended:**

- `Noto.Core` targets plain `net9.0` with **zero package references** — a SQLite
  or WinUI reference is a build error, and architecture guard tests assert it.
- Repository interfaces live in Core, implementations in Infrastructure.
- Three repositories (`INoteRepository`, `IFolderRepository`, `ITagRepository`),
  not a generic `IRepository<T>`. `ITagRepository` does not exist yet.
- No ORM. Hand-written SQL; Dapper was removed.
- Every mutation goes through a command (ADR-010); queries go direct.

---

## 8. Recently Completed

```
2026-09-14  PR #38  Schema v2 / migration 002
2026-09-14  PR #39  Case D correction — RestoreFolder semantics
2026-09-14  PR #40  Core Note Engine contract FROZEN
2026-09-14  PR #41  Slice 1 — domain types, title, CreateNote, GetNote
2026-09-14  PR #42  Slice 2a — ordering engine, renormalisation
2026-09-14  PR #43  ADR-011 — note palette keys note1–note6
2026-09-14  PR #44  Slice 2b — pin, fold, colour
2026-09-14  PR #45  Slice 3 — delete, restore, recycle bin
2026-09-15  PR #46  ULID C′ — O(1) state, ordering semantics settled
2026-09-15  PR #47  Test-infrastructure pool isolation
2026-09-15  PR #48  Slice 4 — ordering engine generalised; the seven folder commands
```

---

## 9. Current Work Queue

### NOW

- Nothing in progress. Slice 4 merged; no open PRs, no working branch.

### NEXT

**Slice 5 — tags**, the last slice of the contract's §15 plan:
`CreateTag`, `RenameTag`, `DeleteTag`, `AssignTagToNote`, `RemoveTagFromNote`
— idempotency (§7), the `NoteTags` cascade, case-insensitive name uniqueness.
`ITagRepository` does not exist yet. This is the one place `DuplicateName` is
actually used, and `DeleteTag` is the engine's only hard delete.

### LATER

- #14 markdown parsing / rendering / editing
- #15 export and backup
- Remaining M0 issues: #22 design tokens, #21 commands/events, #20 architecture
  tests, #7 logging, #9 settings, #10 error handling, #11 perf harness
- M2 — the first real UI

### DEFERRED

- `ListFolders` (Q3) and the other unimplemented queries — no slice assigns them
- Purge / retention — Cases E and F, needs a policy decision
- Contextual notes — ADR-005, ADR-006 Proposed, M6
- Sync, cloud, AI, plugins

---

## 10. Slice 4 Scope (as delivered)

Verified against the frozen contract §11 and §15. Everything in the IN column
is merged; nothing in the OUT column was introduced.

```
IN (all merged)                 OUT (none introduced)
CreateFolder                    MoveFolder          folders are flat; stale ADR-010 wording
RenameFolder                    ListFolders / Q3    no slice assigns it
DeleteFolder     atomic         Nested folders      parity C3 MUST: flat
RestoreFolder    atomic         Folder colours      parity C17 DROP
PinFolder                       IsCollapsed command no command exists
UnpinFolder                     Duplicate-name prevention  names are not unique (Case G)
ReorderFolder    atomic         Purge · Tags · UI · Sync · Migration 003
```

---

## 11. Architectural Decisions Future Work Must Respect

| Decision | Source |
| -------- | ------ |
| **Folders are flat** — no `ParentId`, no nesting | parity C3 MUST; design §5 |
| **Folder names are not unique** — duplicates allowed | Case G; no unique index |
| **Schema v2 is sufficient** — no migration 003 for the engine | contract §12 |
| **No `DeletedWithFolderId`** or any deletion provenance | deletion-semantics §5 |
| **Case D** — restoring a folder restores *every* still-deleted note pointing at it, including one deleted independently beforehand | contract §7; design §8 |
| **Case B** — deleting a folder leaves already-deleted notes untouched | contract §7 |
| **I1/I2** — ordinary queries are active-only; the bin has explicit methods; **no `includeDeleted` flag** | deletion-semantics §6 |
| **One timestamp per transaction**, shared by every row it changes | contract §4 |
| **Exactly four failure reasons** — `NotFound`, `DuplicateName`, `InvalidInput`, `InvalidState` | contract §6 |
| **Title is computed, never stored** | parity B16; migration 002 |
| **ULID C′** — `NewId()` is monotonic per instant; `NewId(t)` draws fresh randomness and keeps no state | ADR-012 |
| **Ordering engine is domain-neutral** — notes and folders share one implementation | this slice |
| **Local-first SQLite** — no sync architecture until it is actually built | ADR-002 |
| **No UI implementation yet** — the real interface starts at M2, on #22's tokens | roadmap; `MainWindow.xaml` |

---

## 12. Deferred / Explicitly Not Started

```
Nested folders            parity C3 — flat is a MUST
Folder colours            parity C17 — DROP
All-notes / favourites    parity C18 — DROP
Note templates            parity B24 — DROP, no evidence it exists
Purge and retention       Cases E/F — needs a policy decision
Contextual notes          ADR-005/006 Proposed, M6
Sync / cloud              ADR-002 defers it; ULID keeps it cheap
Search / FTS5             #17
Export                    #15
Markdown rendering        #14
Tags                      Slice 5
Any product UI            M2
AI, plugins, sharing      not on the roadmap
```

---

## 13. Quality Gates

Verified on `main` @ `7b13269`:

```
Build                0 warnings, 0 errors
Tests                443 / 443 passing
                       Noto.Core.Tests             84
                       Noto.UseCases.Tests          1
                       Noto.Infrastructure.Tests  358   (244 pre-Slice-4 + 114 folder)
Format               dotnet format --verify-no-changes  exit 0
Architecture tests   passing — ADR-009 boundary enforced mechanically
CI                   Build & test · Analyze C# · CodeQL · Validate docs & governance
CodeQL               clean on the merged branch
Branch protection    required checks, linear history, conversation resolution
```

**Known issues:** none open. Four defects found during M1 were all real and are
fixed — a ULID monotonicity bug (PR #46), a process-global SQLite pool race
(PR #47), and in PR #48 the folder pin/unpin `UpdatedAt` no-op violation plus a
missing `DeletedAt` guard on two repository updates.

**Worth remembering:** PR #48 reached 428 passing tests, clean CI and clean
CodeQL while still violating contract §4. Review caught it by challenging the
contract and mutating the implementation, not by reading the test results.

---

## 14. Next Development Path

```
Done: Slice 4 — folder commands merged (PR #48)
    ↓
Slice 5 — tags: the last slice of contract §15
    ↓
#14 markdown · #15 export; M1 complete
    ↓
Remaining M0 work, notably #22 design tokens
    ↓
M2 — SideNotes Workspace: the first real UI and the first dogfoodable build
    ↓
M3 — SideNotes parity: the first product
```

The roadmap does not hold UI until the backend is finished. M2 is described as
*"the first build usable daily. Everything after it is informed by"* it — but it
depends on #22's design tokens, which are still open.

---

## 15. Maintaining This Document

> Update this document whenever a milestone, slice, major architectural decision
> or significant feature status changes. **Status must be derived from repository
> evidence — git history, issue state, test results — never from assumption.**

This is a **status document**. It does not replace, and never overrides:

- the ADRs in `docs/decisions/`
- `docs/product/sidenotes-parity.md`
- `docs/architecture/core-note-engine-contract.md` (FROZEN)
- `docs/architecture/deletion-semantics.md`
- issue acceptance criteria

Where this document and any of those disagree, **they are right and this is
stale**. Fix this one.

Two failure modes to avoid when updating:

- **Do not confuse architecture with product.** A validated WinUI host is not a
  designed interface; a tested command handler is not a shipped feature.
- **Do not mark parity rows implemented from the engine side.** A parity row is
  done when a user can do the thing.
