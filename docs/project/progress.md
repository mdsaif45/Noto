# Noto Development Status

```
Project:            Noto — a Windows-native notes application
Current milestone:  M2 — SideNotes Workspace  (in progress)
Current slice:      M2-2 Note List  (MERGED); folder pane and note list usable
Overall status:     Engine complete. The first two production UI surfaces exist;
                    the workspace shell (docking, hotkey, tray) does not.
Last updated:       2026-09-22
Evidence baseline:  main @ 667f294 (PR #60 merged — M2-2 note list)
```

> **Read this first.** A working engine is not a working product. Noto has a
> tested domain and persistence layer, and two real surfaces on top of it — a
> folder pane and a note list. It is still not the application a user would
> recognise as Noto: there is no note editor, and no edge-docked window, global
> hotkey or tray to reach it by.

---

## 1. Progress Line

```
M0  Foundation & Architecture     ██████░░░░  IN PROGRESS   9 issues open
        ↓
M1  Core Note Engine              ████████░░  IN PROGRESS   3 issues open
        ↓
M2  SideNotes Workspace           ███░░░░░░░  IN PROGRESS   ◄ folder pane + note list
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
| **M0** Foundation & Architecture | **IN PROGRESS** (4 closed / 9 open) | Solution structure; ADRs 001–012; WinUI 3 + Windows App SDK validated; SQLite foundation; CI, CodeQL, branch protection; **#22 design system merged (`Noto.UI`)** | — | #21 commands/events, #20 architecture tests, #7 logging, #9 settings, #10 error handling, #11 perf harness |
| **M1** Core Note Engine | **IN PROGRESS** (0 closed / 3 open) | **#13 complete in substance** — slices 1–6 merged: all 23 commands and all 7 queries of contract §1. #13 is still OPEN on GitHub | — | #14 markdown, #15 export — both still open |
| **M2** SideNotes Workspace | **IN PROGRESS** (1 closed / 2 open) | M2-0 integration spike (#54); **M2-1 Folder Pane merged (PR #57)** — list, create, rename, states, keyboard, focus; **M2-2 Note List merged (PR #60)** — enter a folder, note list, selection, states | — | The workspace shell — docking (#16), global hotkey, tray — and the note editor |
| **M3** SideNotes Parity | **NOT STARTED** | — | — | 0 of 264 parity rows implemented |
| **M4** Hardening | **NOT STARTED** | — | — | — |
| **M5** Windows Enhancements | **NOT STARTED** (no issues yet) | — | — | — |
| **M6** Contextual Notes | **NOT STARTED** (3 open) | ADR-005, ADR-006 drafted as **Proposed**, deferred to M6 | — | — |
| **M7** Competitor Features | **NOT STARTED** (no issues yet) | — | — | — |
| **M8** Noto Differentiators | **NOT STARTED** (no issues yet) | — | — | — |
| **M9** v1.0 | **NOT STARTED** (no issues yet) | — | — | — |

---

## 3. Last Slice Completed — Slice 6, queries

**Slice 6 — Queries** merged as PR #52 → `4e9e09e`. The four queries the §15
slice plan never assigned, completing the engine's public surface.

```
Q2 ListNotesInFolder   Q3 ListFolders   Q4 ListTags   Q5 ListNotesForTag
```

**All 23 commands and all 7 queries of contract §1 now exist.** The contract
records Slice 6 as *"the last slice of the Core Note Engine"*.

| Query | Ordering | Source |
| --- | --- | --- |
| Q2 `ListNotesInFolder` | `IsPinned DESC, SortOrder ASC, Id ASC` | O2 in full |
| Q3 `ListFolders` | `IsPinned DESC, SortOrder ASC, Id ASC` | O2 in full |
| Q4 `ListTags` | `Name COLLATE NOCASE ASC` | **U13a** |
| Q5 `ListNotesForTag` | `UpdatedAt DESC` | **U13b** |

**Two contract amendments were gated before implementation.** §11's Q4 row
carried an *empty* obligation column and Q5's specified no order, so neither
could be written without choosing one — a product-visible decision the contract
had not made. Q2 needed no amendment: its obligation already cited O1/O2.

**Q5 deliberately has no secondary tie-break.** Two notes can share an
`UpdatedAt` (§4 gives one transaction's rows one timestamp), and the contract
does not say how they order. Recorded as open in §5a rather than resolved by
invention, so the tests assert membership for ties and not a relative order.

**No migration.** Every table and index the four queries need already existed.

> **A real test weakness was found by mutation before merge.** The ordering
> tie-break tests seeded fresh ULIDs, which are creation-ordered — so insertion
> order always equalled id order, and the tests passed whether or not the query
> ordered by `Id`. Measured: with `Id ASC` SQLite returns AAA, MMM, ZZZ;
> without it, insertion order. Ties are now seeded in *descending* id order.
> 19 mutants killed, 1 equivalent — the equivalent one drops an explicit
> `COLLATE NOCASE` that the column declaration already supplies, verified
> against SQLite rather than assumed, and no test was manufactured to kill it.

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
| Progress document | DONE | PR #49 → `a5c3b1f` |
| **U12a / U12b contract amendments** | ACCEPTED | PR #50 → `66845c4` — §7a tag-name normalisation; §7 `RemoveTagFromNote` |
| **Slice 5 — the five tag commands** | DONE | PR #50 → `66845c4` — hard delete, relationship idempotency, no migration |
| Progress document | DONE | PR #51 → `f86afac` |
| **U13a / U13b contract amendments** | ACCEPTED | PR #52 → `4e9e09e` — §5a query ordering |
| **Slice 6 — the four remaining queries** | DONE | PR #52 → `4e9e09e` — Q2–Q5; no migration; engine surface complete |
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
| Tags — create/rename/delete/assign/remove | **Not a parity requirement** | Slice 5; tags appear in **no** parity row and no feature-inventory row — a Noto addition (contract §7) |
| Listing notes, folders and tags (Q2–Q5) | **Engine only — no UI** | Slice 6; queries exist, nothing invokes them |
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

### Actual Noto UI / UX — **partially built**

| | Status |
| - | ------ |
| Visual design language | **PARTIAL** — expressed as tokens and three components; no wider language yet |
| Design tokens / design system | **DONE** — #22 merged (PR #56): `Noto.UI` with tokens and Light/Dark/HighContrast themes, applied by both surfaces |
| Workspace layout, sidebar, drawer | **NOT STARTED** — the shell (docking #16, hotkey, tray) is untouched |
| Note editor | **NOT STARTED** — #14 |
| Folder UI | **DONE** — M2-1 merged (PR #57): list, create, rename, selection, Loading/Loaded/Empty/Error + Retry, keyboard, focus |
| Note list UI | **DONE** — M2-2 merged (PR #60): enter a folder, note list, selection, Loading/Loaded/Empty/Error + Retry, Esc/Back |
| Typography, components, motion, polish | **PARTIAL** — body/caption type, spacing, radius and three components (FolderRow, NoteRow, Button) exist; motion and the wider component library do not |

The interface is two surfaces that replace each other — the folder list, and the
notes of the folder that was entered. Both consume the design system; neither is
reachable the way the product intends, because the shell does not exist.

Notably absent, and deliberately so: creating, editing or deleting a **note**.
M2-2 reads notes; nothing in the UI writes one. The engine has supported every
note command since M1 — capability is not the same as a usable product.

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

- Nothing in progress. Slice 6 merged; no open implementation PRs.

### NEXT

```
No formally defined next implementation slice.
```

**The Core Note Engine is complete.** Contract §15 records Slice 6 as *"the last
slice of the Core Note Engine"*, all 23 commands and all 7 queries of §1 exist,
and no document defines a Slice 7.

Issue #13's six acceptance criteria and five testing requirements are all
satisfied by merged code — including *"folders list correctly and notes can
move between them"*, whose two halves are `ListFolders` (Slice 6) and
`MoveNoteToFolder` (Slice 2a). **#13 remains OPEN on GitHub**; closing it is a
repository decision, not something this document asserts.

What comes next is a scoping decision, not an inference. The candidates already
carry issues — #14 markdown, #15 export, #22 design tokens, M2's workspace —
but none is formally established as the next slice.

### LATER

- #14 markdown parsing / rendering / editing
- #15 export and backup
- Remaining M0 issues: #21 commands/events, #20 architecture tests, #7 logging,
  #9 settings, #10 error handling, #11 perf harness (#22 design tokens is
  merged as PR #56)
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
| **UI implementation has started** — M2-1 folder pane and M2-2 note list are merged, both on #22's tokens; the shell is not | roadmap; `MainWindow.xaml` |

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
Note editor, note CRUD    M2/M3 — the folder pane and note list now exist
AI, plugins, sharing      not on the roadmap

SideNotes UX quality      no macOS access; parity §13 governs. Noto's UI
  benchmark                quality target cannot be measured against
                           SideNotes, only designed toward
```

---

## 13. Quality Gates

Verified on `main` @ `4e9e09e`:

```
Build                0 warnings, 0 errors
Tests                650 / 650 passing
                       Noto.Core.Tests            119
                       Noto.UseCases.Tests          1
                       Noto.Infrastructure.Tests  530   (474 pre-Slice-6 + 56 query)
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

**One recorded follow-up.** A `UNIQUE` violation on `Tags.Name` surfaces as
`StorageException`, not `DuplicateName`. Unreachable under §10's frozen
"single user, single process" model — the check-then-insert window needs
concurrent writers — so it is deferred rather than fixed.

**Worth remembering:** PR #48 reached 428 passing tests, clean CI and clean
CodeQL while still violating contract §4, and PR #50 passed 110 tests with a
redundant write that state could not observe. Both were caught by challenging
the contract and mutating the implementation, not by reading test results.

**Worth remembering:** PR #48 reached 428 passing tests, clean CI and clean
CodeQL while still violating contract §4. Review caught it by challenging the
contract and mutating the implementation, not by reading the test results.

---

## 14. Next Development Path

```
Done: Slice 6 — queries merged (PR #52); Core Note Engine complete
    ↓
#14 markdown · #15 export — the remaining M1 issues
    ↓
#22 design system — MERGED (PR #56)
    ↓
M2 — SideNotes Workspace: the first real UI and the first dogfoodable build
    ↓
M3 — SideNotes parity: the first product
```

The roadmap does not hold UI until the backend is finished. M2 is described as
*"the first build usable daily. Everything after it is informed by"* it — and it
depends on #22's design tokens, which are now merged and available.

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
