# Noto Development Status

```
Project:            Noto — a Windows-native notes application
Current milestone:  M2 — SideNotes Workspace  (in progress)
Current slice:      M2-3 Note Editor  (MERGED); notes can be written, not just read
Overall status:     Engine complete. Three production UI surfaces exist and the
                    UI now writes notes; the workspace shell (docking, hotkey,
                    tray) does not exist.
Last updated:       2026-09-22
Evidence baseline:  main @ 23ee2a5 (PR #63 merged — M2-3 note editor)
```

> **Read this first.** A working engine is not a working product. Noto has a
> tested domain and persistence layer, and three real surfaces on top of it — a
> folder pane, a note list and a plain-source note editor. A user can now
> create, edit and delete a note through the UI. It is still not the
> application a user would recognise as Noto: there is no edge-docked window,
> global hotkey or tray to reach it by, and the editor shows raw markdown
> rather than rendering it.

---

## 1. Progress Line

```
M0  Foundation & Architecture     ██████░░░░  IN PROGRESS   9 issues open
        ↓
M1  Core Note Engine              ████████░░  IN PROGRESS   3 issues open
        ↓
M2  SideNotes Workspace           ████░░░░░░  IN PROGRESS   ◄ folder pane + note list + editor
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
| **M1** Core Note Engine | **IN PROGRESS** (0 closed / 3 open) | **#13 complete in substance** — slices 1–6 merged: all 23 commands and all 7 queries of contract §1. #13 is still OPEN on GitHub | — | #14 markdown — **partially addressed** by M2-3 (plain-source editing only); #15 export. Both still open |
| **M2** SideNotes Workspace | **IN PROGRESS** (1 closed / 2 open) | M2-0 integration spike (#54); **M2-1 Folder Pane merged (PR #57)** — list, create, rename, states, keyboard, focus; **M2-2 Note List merged (PR #60)** — enter a folder, note list, selection, states; **M2-3 Note Editor merged (PR #63)** — plain-source editing, note create and delete, save-on-leave | — | The workspace shell — docking (#16), global hotkey, tray |
| **M3** SideNotes Parity | **NOT STARTED** | — | — | 1 of 264 parity rows done, 5 partial — all as a by-product of M2, not M3 work |
| **M4** Hardening | **NOT STARTED** | — | — | — |
| **M5** Windows Enhancements | **NOT STARTED** (no issues yet) | — | — | — |
| **M6** Contextual Notes | **NOT STARTED** (3 open) | ADR-005, ADR-006 drafted as **Proposed**, deferred to M6 | — | — |
| **M7** Competitor Features | **NOT STARTED** (no issues yet) | — | — | — |
| **M8** Noto Differentiators | **NOT STARTED** (no issues yet) | — | — | — |
| **M9** v1.0 | **NOT STARTED** (no issues yet) | — | — | — |

---

## 3. Last Slice Completed — M2-3, the note editor

**M2-3 — Note Editor** merged as PR #63 → `23ee2a5`. The third production
surface, and the first time the UI writes a note.

```
folder list  ⇄  note list  ⇄  note editor      exactly one visible at a time
```

| Delivered | Detail |
| --------- | ------ |
| Plain-source editor | A multiline `TextBox` over the note's raw markdown. No parsing, no rendering |
| Open a note | `Enter` or double-click from the note list; the editor takes focus |
| Leave | `Esc` or the Back control, which is labelled with the note's current title |
| Create | `Ctrl+N` in the **note list**, opening the created note. Unbound in the editor |
| Delete | `Alt+Ctrl+Backspace` — soft delete, no confirmation dialog (parity B7) |
| Save | **On leave**, when the buffer differs from what was opened. A failed save keeps the user in the editor with the text intact |
| States | Loading / Loaded / Error + Retry. **No Empty state** — an empty note is valid, and its row reads `Untitled note` |
| Title | Recomputed through `NoteTitle.From`, never stored (parity B16) |

**Dirtiness is an exact content comparison**, not a flag: the buffer is compared
against what the note was opened with using `StringComparison.Ordinal`. Typing
something and undoing it correctly reads as clean, so leaving performs no write.

**Three defects were found by running the application and fixed inside the PR.**
`Enter` did nothing because a `ListView` treats it as item activation and marks
it handled before `KeyDown` bubbles — moved to `PreviewKeyDown` (`528d6bd`).
The note list kept a stale title after a save, because the leave path restored
selection against a collection built before the write (`ef25382`). Note failures
reported folder wording (`d7442b0`). Only the last was found by static review.

**What M2-3 deliberately did not do.** No save command or Save button, and no
autosave infrastructure — no debounce, no per-keystroke write, no timer —
because parity B19's continuous-persistence design is M3's to make and a
half-built version would prejudge it. `Ctrl+N` stays unbound in the editor
because nothing documents what it should do with a note open.

---

## 4. Engine's Last Slice — Slice 6, queries

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

## 5. Completed Foundation

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

## 6. SideNotes Parity Progress

**Authoritative source:** `docs/product/sidenotes-parity.md`. The counts below
are read from it; this document never overrides it.

```
264 parity rows total
  1 marked done         [x]   C1 two-level navigation
  5 marked in progress  [~]   B1 B16 C4 C5 C7
258 not started         [ ]
```

Counted from the specification, not asserted here. A `[~]` row states in its
own Notes cell which part is missing, so a partial row cannot later be misread
as a finished one.

**A row is scored against its own requirement cell**, and a pair that states
one decision is scored together. **B7 stays `[ ]` although the delete chord
works**: it soft-deletes, but the recycle bin and restore that make a soft
delete recoverable are B23's requirement and have no UI. The recoverability is
the whole point of that BETTER row, so neither half is marked until both exist.

| Area | Status | Evidence |
| ---- | ------ | -------- |
| Note create / edit / delete | **UI reaches the engine** — no parity row is complete | M2-3 (PR #63): `CreateNote`, `UpdateNoteContent`, `DeleteNote` each have exactly one UI call site. B1 needs its remaining creation surfaces; B7 stays `[ ]` until B23's recycle bin exists; B19 is **not** satisfied — see below |
| Note ordering, pin, colour, fold, move (B8, B10–B15) | **Engine only — no UI** | Slices 1–3; commands exist, nothing invokes them |
| Folder create/rename/delete/pin/reorder (C2, C8, C9, C11, C13) | **Engine only — no UI** | Slice 4; `CreateFolder` and `RenameFolder` are reached by M2-1 (PR #57); delete, pin and reorder are not |
| Tags — create/rename/delete/assign/remove | **Not a parity requirement** | Slice 5; tags appear in **no** parity row and no feature-inventory row — a Noto addition (contract §7) |
| Listing notes, folders and tags (Q2–Q5) | **Engine only — no UI** | Slice 6; queries exist, nothing invokes them |
| Note colours (B15) | **Engine only** | `note1`–`note6`, ADR-011 |
| Recycle bin (B23, C11) | **Engine only** | `ListDeletedNotes` / `ListDeletedFolders` |
| Markdown editing, invisible markdown (ADR-004, D-rows) | **Plain source only** | M2-3 (PR #63) edits raw markdown in a `TextBox`. No parsing, no rendering to native controls, no invisible markdown. #14 open |
| Export (H-rows) | **Not started** | #15 open |
| Everything visual — workspace, sidebar, themes, shortcuts | **Not started** | M2/M3 — the shell, not the three surfaces that exist |
| Nested folders (C3), folder colours (C17), all-notes view (C18) | **DROP / not a requirement** | parity specification |

**No parity row may be marked implemented until the feature is reachable by a
user.** A command handler with tests is engine work, not parity.

---

## 7. UI / UX Status

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
| Design tokens / design system | **DONE** — #22 merged (PR #56): `Noto.UI` with tokens and Light/Dark/HighContrast themes, applied by all three surfaces |
| Workspace layout, sidebar, drawer | **NOT STARTED** — the shell (docking #16, hotkey, tray) is untouched |
| Note editor | **PARTIAL** — M2-3 merged (PR #63): plain-source editing, create, delete, save-on-leave, Loading/Loaded/Error + Retry. Markdown **rendering** and the invisible-markdown editor are not built — #14 |
| Folder UI | **DONE** — M2-1 merged (PR #57): list, create, rename, selection, Loading/Loaded/Empty/Error + Retry, keyboard, focus |
| Note list UI | **DONE** — M2-2 merged (PR #60): enter a folder, note list, selection, Loading/Loaded/Empty/Error + Retry, Esc/Back |
| Typography, components, motion, polish | **PARTIAL** — body/caption type, spacing, radius and three components (FolderRow, NoteRow, Button) exist; motion and the wider component library do not |

The interface is three surfaces that replace each other — the folder list, the
notes of the folder that was entered, and the note that was opened. All three
consume the design system; none is reachable the way the product intends,
because the shell does not exist.

**The UI now writes notes.** M2-3 wired `CreateNote`, `UpdateNoteContent` and
`DeleteNote` to one call site each, so a user can create a note (`Ctrl+N` in
the note list), edit its raw markdown, and delete it (`Alt+Ctrl+Backspace`).
That closes the gap this section recorded for M2-2, when the engine had
supported every note command since M1 and nothing invoked them.

It does not make note handling complete. **Persistence is save-on-leave, not
continuous** — parity B19 requires continuous persistence and stays open, a
recorded exception deferred to M3. Pin, colour, fold, reorder and move-to-folder
remain engine-only. And the editor shows raw markdown: parsing and rendering to
native controls are #14's remaining scope.

```
engine command exists   ≠   a user can reach it
a user can reach it     ≠   the parity row is met
```

```
WinUI 3 validated   ≠   Noto UI designed
```

---

## 8. Current Architecture

```
  Noto.Windows            WinUI 3 app — three surfaces, no workspace shell
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

## 9. Recently Completed

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
2026-09-15  PR #50  Slice 5 — the five tag commands; U12a/U12b amendments
2026-09-15  PR #52  Slice 6 — the four remaining queries; engine surface complete
2026-09-15  PR #54  M2-0 workspace integration spike
2026-09-16  PR #56  #22 design system — Noto.UI, tokens, Light/Dark/HighContrast
2026-09-21  PR #57  M2-1 folder pane — the first production surface
2026-09-22  PR #60  M2-2 note list — enter a folder, list its notes
2026-09-22  PR #61  M2 status and parity reconciliation
2026-09-22  PR #62  M2-3 save model and Ctrl+N scope recorded
2026-09-22  PR #63  M2-3 note editor — plain-source editing, note create and delete
```

---

## 10. Current Work Queue

### NOW

- Nothing in progress. M2-3 merged (PR #63); no open implementation PRs.
- **PR #58 is open** — a dependency bump of Windows App SDK 1.6.250205002 → 2.5.1.
  A major-version SDK change under ADR-008's unpackaged self-contained
  configuration, so it needs its own validation before it is merged.

### NEXT

```
No formally defined next M2 slice.
```

**M2-1, M2-2 and M2-3 were each gated before implementation.** No document
defines an M2-4, so what follows is a scoping decision rather than an
inference. The known remaining M2 work is the workspace shell — the edge-docked
window (#16), the global hotkey and the tray — which is what stands between
three working surfaces and a build that can be used daily.

**The Core Note Engine is complete.** Contract §15 records Slice 6 as *"the last
slice of the Core Note Engine"*, all 23 commands and all 7 queries of §1 exist,
and no document defines a Slice 7.

Issue #13's six acceptance criteria and five testing requirements are all
satisfied by merged code — including *"folders list correctly and notes can
move between them"*, whose two halves are `ListFolders` (Slice 6) and
`MoveNoteToFolder` (Slice 2a). **#13 remains OPEN on GitHub**; closing it is a
repository decision, not something this document asserts.

**#14 is partially addressed and must not be closed on M2-3.** The issue scopes
three things: plain-source editing, rendering to native controls, and formatting
shortcuts that insert markdown syntax. M2-3 delivered the first. The other two
are unbuilt, and the invisible-markdown editor is a separate M3 parity
requirement with its own budget (ADR-004).

### LATER

- #14 — markdown parsing and rendering to native controls; formatting shortcuts
- #15 export and backup
- Remaining M0 issues: #21 commands/events, #20 architecture tests, #7 logging,
  #9 settings, #10 error handling, #11 perf harness (#22 design tokens is
  merged as PR #56)
- M2 — the workspace shell: docking (#16), global hotkey, tray
- Human Light / High Contrast visual review of the three merged surfaces

### DEFERRED

- Purge / retention — Cases E and F, needs a policy decision
- Contextual notes — ADR-005, ADR-006 Proposed, M6
- Sync, cloud, AI, plugins

---

## 11. Slice 4 Scope (as delivered)

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

## 12. Architectural Decisions Future Work Must Respect

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
| **UI implementation has started** — M2-1 folder pane, M2-2 note list and M2-3 note editor are merged, all on #22's tokens; the shell is not | roadmap; `MainWindow.xaml` |
| **Save-on-leave is interim, not the target** — B19 requires continuous persistence and is deferred to M3. No save command, Save button or autosave infrastructure may be added in the meantime | parity B19; PR #62 |

---

## 13. Deferred / Explicitly Not Started

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
Markdown rendering        #14 — M2-3 edits raw source; parsing and rendering
                            to native controls are not built
Invisible markdown        ADR-004, M3 — its own issue and budget
Continuous persistence    parity B19 — deferred to M3; M2-3 saves on leave
Tags                      Slice 5
Note pin/colour/fold/     engine only — no UI reaches them
  reorder/move
AI, plugins, sharing      not on the roadmap

SideNotes UX quality      no macOS access; parity §13 governs. Noto's UI
  benchmark                quality target cannot be measured against
                           SideNotes, only designed toward
```

---

## 14. Quality Gates

Verified on `main` @ `23ee2a5`:

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

**The test count is unchanged by M2-1, M2-2 and M2-3.** All three surfaces live
in `Noto.Windows`, a `WinExe` with `UseWinUI` and no test project, so UI
behaviour is validated by running the application rather than by automated
tests. That is a known gap, not a passing result.

### Outstanding validation — M2-3

Three items were disclosed in PR #63 and remain open. None blocks the merge;
none may be recorded as passed.

| Item | Status |
| ---- | ------ |
| **Human Light / High Contrast visual review** | **OUTSTANDING.** Theme resources were verified structurally — 13 keys across Light/Dark/HighContrast, identical key sets, HighContrast entirely system-driven, no hardcoded values — but resource inspection is not the same as looking at the result. The PR's screenshots are **Dark only**. The UI quality gate is not closed |
| **Delete-failure path (scenario 27)** | **NOT EXERCISED.** A `DeleteNote` failure requires the note to be already gone, which is the same setup as note-missing-on-open; it could not be constructed as a distinct delete failure |
| **Editor Retry after a read failure (scenario 33)** | **NOT EXERCISED.** Needs a `StorageException` during `GetNote`, not inducible without corrupting the database mid-session. The control exists and is wired as the note list's is |

**One performance observation, not a defect.** Opening a 100 KB note measured
~2360 ms against ADR-004's **provisional** `< 200 ms` budget. The engine read is
~23.8 ms; the remainder is WinUI laying out a single 102 KB `TextBox`. Typing
stayed responsive (231 ms for 10 characters), save latency is flat (85–117 ms
regardless of note size), and list virtualisation is intact (21 of 2000 rows
realised). Deliberately not optimised in M2-3; it belongs with M4.

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

## 15. Next Development Path

```
Done: Core Note Engine complete (PR #52) · design system (PR #56)
    ↓
Done: M2-1 folder pane (#57) → M2-2 note list (#60) → M2-3 note editor (#63)
      three surfaces; the UI creates, edits and deletes notes
    ↓
M2 remaining — the workspace shell: edge-docked window (#16), global
      hotkey, tray. This is what stands between the surfaces and a build
      that can be used daily
    ↓
#14 markdown rendering · #15 export — the remaining M1 issues
    ↓
M3 — SideNotes parity: the first product. B19 continuous persistence and
      the invisible-markdown editor are resolved here, not before
```

The roadmap does not hold UI until the backend is finished. M2 is described as
*"the first build usable daily. Everything after it is informed by"* it — and the
navigation half of that is now built. It is not yet usable daily: the surfaces
exist, but nothing docks them to a screen edge or summons them with a hotkey.

---

## 16. Maintaining This Document

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
