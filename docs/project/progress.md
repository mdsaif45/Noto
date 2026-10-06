# Noto Development Status

```
Project:            Noto — a Windows-native notes application
Current milestone:  M2 — SideNotes Workspace  (in progress)
Current slice:      #16 slice 6 — the drawer (topmost, putting it away, pin),
                    PR #92, in review. Runtime-validated; one measured
                    deviation from its design gate awaits the owner's decision
                    (Noto's own taskbar button does nothing)
Overall status:     Engine complete. Three production UI surfaces exist and the
                    UI now writes notes; settings persist. The window docks to
                    its remembered edge at its remembered width and resizes
                    from its inner edge (#16 slices 1–3); a global hotkey
                    shows and hides it (#16 slices 4–5), restoring and
                    re-docking it when it is minimized (#82). Unsaved editor
                    text is saved before the window closes (#86) or hides.
                    One process per data root: a second launch brings the
                    running Noto forward and exits (A17).
                    In review (#16 slice 6): topmost while shown; put away by
                    Ctrl+W, Escape or losing activation; pin for a session.
                    The tray does not exist.
Last updated:       2026-10-06
Evidence baseline:  main @ 61cd0c7 (PR #91 merged — A17 complete)
```

> **Read this first.** A working engine is not a working product. Noto has a
> tested domain and persistence layer, and three real surfaces on top of it — a
> folder pane, a note list and a plain-source note editor. A user can now
> create, edit and delete a note through the UI, and the window opens docked to
> a screen edge and can be resized from its inner edge. A global hotkey shows
> and hides it. It is still not the application a user would recognise as
> Noto: there is no tray, the drawer behaviour (#16 slice 6) is in review, and the editor shows raw
> markdown rather than rendering it.

---

## 1. Progress Line

```
M0  Foundation & Architecture     ██████░░░░  IN PROGRESS   7 issues open
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
seven M0 issues remain open, including several that later milestones depend on
(structured logging, error handling, the performance harness).

---

## 2. Milestone Status

| Milestone | Status | Completed work | Current work | Next |
| --------- | ------ | -------------- | ------------ | ---- |
| **M0** Foundation & Architecture | **IN PROGRESS** (6 closed / 7 open) | Solution structure; ADRs 001–013; WinUI 3 + Windows App SDK validated; SQLite foundation; CI, CodeQL, branch protection; **#22 design system merged (`Noto.UI`)** | **#9 settings persistence merged (PR #65)** — typed keys, defaults, change notification | #21 commands/events, #20 architecture tests, #7 logging, #10 error handling, #11 perf harness |
| **M1** Core Note Engine | **IN PROGRESS** (0 closed / 3 open) | **#13 complete in substance** — slices 1–6 merged: all 23 commands and all 7 queries of contract §1. #13 is still OPEN on GitHub | — | #14 markdown — **partially addressed** by M2-3 (plain-source editing only); #15 export. Both still open |
| **M2** SideNotes Workspace | **IN PROGRESS** (1 closed / 3 open) | M2-0 integration spike (#54); **M2-1 Folder Pane merged (PR #57)** — list, create, rename, states, keyboard, focus; **M2-2 Note List merged (PR #60)** — enter a folder, note list, selection, states; **M2-3 Note Editor merged (PR #63)** — plain-source editing, note create and delete, save-on-leave; **#16 slice 1 merged (PR #74)** — window platform primitives; **#16 slice 2 merged (PR #76)** — basic edge docking at launch; **#16 slice 3 merged (PR #78)** — edge setting, inner-edge resize, per-display width persistence; **#16 slice 4 merged (PR #80)** — global activation hotkey; **#82** minimized restore; **#86** save on close; **#87** Ctrl+N in an empty folder; **#16 slice 5 merged (PR #89)** — show/hide **A17 single instance merged (PRs #90, #91)** — one process per data root | **#16 slice 6, the drawer** — PR #92, in review | The tray, always-on-top, #16 slices 6–8. #16 remains OPEN; animation and multi-monitor are later slices |
| **M3** SideNotes Parity | **NOT STARTED** | — | — | 2 of 264 parity rows done on `main` (C1, A17), 5 partial; 8 done and 9 partial with #16 slice 6 — all as a by-product of M2, not M3 work |
| **M4** Hardening | **NOT STARTED** | — | — | — |
| **M5** Windows Enhancements | **NOT STARTED** (no issues yet) | — | — | — |
| **M6** Contextual Notes | **NOT STARTED** (3 open) | ADR-005, ADR-006 drafted as **Proposed**, deferred to M6 | — | — |
| **M7** Competitor Features | **NOT STARTED** (no issues yet) | — | — | — |
| **M8** Noto Differentiators | **NOT STARTED** (no issues yet) | — | — | — |
| **M9** v1.0 | **NOT STARTED** (no issues yet) | — | — | — |

---

## 3. Last UI Surface Completed — M2-3, the note editor

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
| Save | **On leave**, when the buffer differs from what was opened. A failed save keeps the user in the editor with the text intact. **Also when the window closes**, added after M2-3: a failed save cancels the close, and a second close with the same text discards it |
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
| Solution structure — Core / UseCases / Infrastructure / Platform / Windows | DONE | 10 projects in `Noto.sln` (6 source, 4 test); dependencies point inward |
| ADRs 001–013 | ACCEPTED (005, 006 Proposed, deferred to M6) | `docs/decisions/` |
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
| **Test-infrastructure pool isolation** | DONE | PR #47 → `4f49003` — replaced process-global `ClearAllPools` in fixture teardown; PR #85 → `bbde12e` replaced the 25 calls that remained in test bodies |
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
  8 marked done         [x]   C1 · A17 · A1 A10 A12 A14 G7 G8 (#16 slice 6; A14 re-scored to slice 3)
  9 marked in progress  [~]   B1 B16 C4 C5 C7 · A8 A9 A13 G51 (#16 slice 6)
247 not started         [ ]
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
| Display enumeration, work areas, frame inset, dock geometry | DONE | #16 slice 1, PR #74 → `c2473d3` |
| Edge docking of the real window, left and right | DONE — single display only | #16 slice 2, PR #76 → `126f3f0`; `AppWindow.MoveAndResize`, visible frame flush with the work area (ADR-007 §4) |
| Edge setting, inner-edge resize, per-display width persistence | DONE — single display only | #16 slice 3, PR #78 → `b17a1ae`; native window subclass, borderless chrome, `workspace.edge` / `workspace.width` settings (ADR-007 §4) |
| Global activation hotkey — brings the window to the foreground | DONE — foreground only; no hide | #16 slice 4, PR #80 → `3d2d23f`; `Ctrl+Alt+Win+Space` by default, message-only receiver, `activation.hotkey.enabled` / `activation.hotkey.binding` settings read at startup (ADR-007 §4); foreground evidence from the corrected harness, `tools/validation/` (PR #83 → `8f6d762`) |
| Restore from minimized, and activation of a minimized window | DONE — single display only | PR #82 → `63014bf`. A restore re-docks at the remembered requested width, on the window's own edge and display. The hotkey restores a minimized window before taking the foreground. Minimized stays distinct from hidden (ADR-007 §4, *Minimized and restored*) |
| Show and hide through the global hotkey | DONE — single display | #16 slice 5, PR #89. Hidden is `AppWindow.Hide()`; every show re-docks against the current display and ends normal, docked and focused; unsaved editor text is saved once before hiding (ADR-007 §4, *Show and hide*) |
| DPI / multi-monitor behaviour investigated | RESEARCHED | `docs/research/` |
| Packaging and identity decided | DECIDED | ADR-008 |

### Actual Noto UI / UX — **partially built**

| | Status |
| - | ------ |
| Visual design language | **PARTIAL** — expressed as tokens and three components; no wider language yet |
| Design tokens / design system | **DONE** — #22 merged (PR #56): `Noto.UI` with tokens and Light/Dark/HighContrast themes, applied by all three surfaces |
| Workspace layout, sidebar, drawer | **PARTIAL** — the window docks to its remembered vertical edge (right by default) at its remembered width, and resizes from its inner edge only (#16 slices 2–3); a global hotkey shows and hides it (#16 slices 4–5); tray and always-on-top are not built. There is no settings UI: the edge and the hotkey are changed only through their settings |
| Note editor | **PARTIAL** — M2-3 merged (PR #63): plain-source editing, create, delete, save-on-leave, Loading/Loaded/Error + Retry; save before the window closes added by PR #86. Markdown **rendering** and the invisible-markdown editor are not built — #14 |
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

It does not make note handling complete. **Persistence is save-on-leave (and
on close), not continuous** — parity B19 requires continuous persistence and stays open, a
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
  not a generic `IRepository<T>`. All three exist; `ITagRepository` arrived with Slice 5.
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
2026-09-28  PR #74  #16 slice 1 — window platform primitives
2026-09-28  PR #75  ADR-007 — workspace width limits recorded
2026-09-29  PR #76  #16 slice 2 — basic edge docking
2026-09-29  PR #77  #16 slices 1–2 status reconciliation
2026-09-29  PR #78  #16 slice 3 — edge setting, resize and width persistence
2026-09-29  PR #79  #16 slice 3 status reconciliation
2026-09-29  PR #80  #16 slice 4 — global activation hotkey
2026-09-29  PR #81  #16 slice 4 status reconciliation
2026-10-04  PR #83  Corrected runtime-validation harness (tools/validation/)
2026-10-04  PR #82  #16 restore re-docks at the remembered width; minimized activation restores
2026-10-05  PR #72  Windows SDK BuildTools 10.0.26100.1742 → 10.0.28000.2705
2026-10-05  PR #73  coverlet.collector 10.0.1 → 10.1.0
2026-10-05  PR #84  #16 slice 4 foreground evidence from the corrected harness
2026-10-05  PR #85  Test SQLite pool isolation; remaining layer-direction guards
2026-10-05  PR #86  Unsaved editor text is saved before the window closes
2026-10-05  PR #87  Ctrl+N creates the first note in an empty folder
2026-10-05  PR #88  Repository status truth before the Slice 5 gate
2026-10-05  PR #89  #16 slice 5 — show and hide through the global hotkey
2026-10-06  PR #90  A17 — single-instance mechanism: mutex ownership, activation pipe
2026-10-06  PR #91  A17 — one Noto per data root: startup wiring, harness, ADR-013
```

---

## 10. Current Work Queue

### NOW

- **#16 slice 6, the drawer, is PR #92** (in review). Topmost while shown;
  put away by `Ctrl+W`, by Escape (four behaviours, A12) or by losing
  activation to another application (A13, on by default); a session-only pin.
  Every hide saves first. See *Outstanding validation — #16 slice 6*. One
  deviation from its design gate is measured and awaits the owner's decision:
  pressing Noto's own taskbar button does nothing.
- **A17 single instance is merged: PR #90 (platform,
  `6e56213`) and PR #91 (integration).** A second launch hands its request to the
  running Noto over a named pipe and exits; ownership of a data root is a
  named mutex, decided before the database opens (ADR-013). See
  *Outstanding validation — A17*.
- **Windows App SDK is 2.5.1** — merged as PR #68 (`4d100f2`), a major-version
  change under ADR-008's unpackaged self-contained configuration. Validated by
  a controlled A/B runtime campaign against the real application on both 1.6
  and 2.5.1: 25 scenarios, no regression observed. **PR #58 carried the same
  bump and was closed as superseded**, not merged — it was based before M2-1,
  M2-2, M2-3, #9 and #67 existed.
- **`Noto.Windows.Tests` exists** — PR #67. The narrow platform test project
  `quality-gates.md` specifies, referencing `Noto.Platform.Windows` and nothing
  above it.
- **#16 slices 1 and 2 are merged.** Slice 1 (PR #74 → `c2473d3`): display
  enumeration, work areas, monitor identity, per-display DPI, frame-inset
  measurement and dock geometry. Slice 2 (PR #76 → `126f3f0`): at launch the
  real window docks flush against the right edge of its display's work area
  (left via the `--dock-left` validation switch), at the 360 DIP default width
  fitted by the ADR-007 §4 width contract. **#16 remains OPEN.**
- **#16 slice 3 is merged** (PR #78 → `b17a1ae`). The edge comes from the
  `workspace.edge` setting (`--dock-left` is removed); the width from
  `workspace.width::<display>`, then `workspace.width`, then 360, clamped where
  it is shown and never rewritten. The window resizes from its inner edge only,
  through a native window subclass, and a finished resize saves the visible
  width. Settings gained declared key families and `TryRead`.
- **#16 slice 4 is merged** (PR #80 → `3d2d23f`). A global hotkey,
  `Ctrl+Alt+Win+Space` by default, brings the docked window to the foreground
  with keyboard focus; it never hides it (that is slice 5). As merged, it
  left a minimized window minimized; #82 fixed that (below). It is registered
  after settings load and before the window, on a message-only window, from
  `activation.hotkey.enabled` and `activation.hotkey.binding`, read at startup
  only. A malformed binding falls back to the default with its row untouched; a
  refused chord registers nothing else and is reported through `Debug` only.
- **The corrected runtime-validation harness is merged** (PR #83 → `8f6d762`):
  `tools/validation/`. It drives the real `Noto.exe` with foreground rights
  controlled. Noto is started by the shell, the foreground is set by real
  input, and a negative control brackets every press. It replaces the slice 4
  campaign, which was contaminated by inherited foreground rights.
- **The minimized restore and activation defect is fixed** (PR #82 →
  `63014bf`). Before it, a restore rebuilt the dock from minimized-state
  geometry: a shrunken width, a display chosen by the parking position, and a
  frame inset measured while minimized. The hotkey also left a minimized
  window minimized. Now a restore re-docks at the remembered requested width,
  on the window's own edge and display, and activation restores before taking
  the foreground. See *Outstanding validation — minimized restore (#82)*.
- **#16 slice 5, show/hide, is merged** (PR #89 → `582ae91`). The hotkey
  toggles the window: hidden → show, minimized → restore, behind → bring
  forward, in front → hide. Runtime-validated on one display (see
  *Outstanding validation — #16 slice 5*).
- **The pre-Slice 5 small-work campaign is merged** (PRs #72, #73, #84–#87).
  - Closing the window no longer loses unsaved editor text (PR #86). One
    explicit save runs at close; a failure cancels the close and keeps the
    text; a second close with the same text discards it with no further
    attempt. Not autosave: B19 stays M3's.
  - `Ctrl+N` creates the first note in an empty folder (PR #87), as a
    keyboard accelerator on the note surface. #69 is still open on GitHub;
    closing it is the owner's decision.
  - Test bodies no longer reset the process-wide SQLite pool, and the
    remaining layer directions are guarded (PR #85).
  - Dependency updates #72 and #73. The BuildTools update produced
    byte-identical Release output.

### NEXT

```
#16 Edge-Docked Sidebar — slices 1–4, the #82 restore fix and slice 5
(show/hide, PR #89) merged. A17 single instance merged (PRs #90, #91).
Next after it: the tray and always-on-top, then #16 slices 6–8.
```

**M2-1, M2-2 and M2-3 were each gated before implementation, and #16 is now
gated too.** Its contract is closed for slices 1–6 (platform primitives → edge
docking → resize and width persistence → hotkey → show/hide → animation), all
of which are hardware-independent. Slice 7 splits in two, because the
validation environment now has two displays but only one scale factor:

- **7a — multi-monitor geometry.** Implementable, and runtime-verifiable in the
  current environment: two displays, the second at negative X with a vertical
  offset. Covers negative coordinates, monitor identity, per-display work
  areas and moving between displays.
- **7b — mixed-DPI.** Can be designed with the rest of the platform behaviour,
  but **runtime validation remains blocked on #30**: both displays run at
  96 DPI (100%), and #30 requires displays at *different* scale factors. Runtime
  scale changes are blocked for the same reason. No mixed-DPI claim may be made.

Two displays is the state of the development environment, not a product
assumption — #16 is still designed for arbitrary monitor topology. Slice 8
(accessibility and UX hardening) needs human visual review.

**The second display was not connected during slice 2 validation**
(2026-09-29: one display, 1920×1080 at 96 DPI). Slice 7a's runtime
verification needs it reconnected; nothing multi-monitor has been validated at
runtime yet.

Its prerequisites are cleared: #9 settings merged (PR #65), the platform test
foundation merged (#67), and the SDK upgrade merged and runtime-validated
(#68), so #16 will be built once on the version that ships rather than twice.

**Slices 1–5 are the #16 implementation so far**, with A17 merged. The
rest of the workspace shell — the tray and always-on-top —
remains what stands between three working surfaces and a build that can be
used daily.

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
  #10 error handling, #11 perf harness (#22 design tokens is merged as PR #56;
  #9 settings as PR #65)
- M2 — the rest of the workspace shell: #16 slices 5–8 (show/hide,
  animation, multi-monitor, hardening), tray
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
| **UI implementation has started** — M2-1 folder pane, M2-2 note list and M2-3 note editor are merged, all on #22's tokens; the shell has edge docking, inner-edge resize and a foreground hotkey only (#16 slices 1–4) | roadmap; `MainWindow.xaml`; `App.xaml.cs` |
| **Save-on-leave is interim, not the target** — B19 requires continuous persistence and is deferred to M3. No save command, Save button or autosave infrastructure may be added in the meantime. Saving once when the window closes is the same explicit save at one more lifecycle point, not autosave | parity B19; PR #62 |

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
Tags UI                   engine done (Slice 5); no UI, and not a parity requirement
Note pin/colour/fold/     engine only — no UI reaches them
  reorder/move
AI, plugins, sharing      not on the roadmap

SideNotes UX quality      no macOS access; parity §13 governs. Noto's UI
  benchmark                quality target cannot be measured against
                           SideNotes, only designed toward
```

---

## 14. Quality Gates

Verified on `main` @ `61cd0c7`, and on the #16 slice 6 branch (PR #92):

```
Build                0 warnings, 0 errors
Tests                1338 / 1338 on main; 1391 / 1391 on PR #92
                       Noto.Core.Tests            239   (119 + 40 #16 slice 3 + 80 slice 4)
                       Noto.UseCases.Tests         43   (1 + 33 #16 slice 3 + 1 #85 + 8 slice 6)
                       Noto.Windows.Tests         480   (35 #67 · +100 slice 1 · +32 slice 2 · +54 slice 3 · +27 slice 4 · +36 #82 · +1 #85 · +25 slice 5 · +125 A17 · +45 slice 6)
                       Noto.Infrastructure.Tests  629   (530 + 42 settings + 44 #16 slice 3 + 12 slice 4 + 1 #85)
Format               dotnet format --verify-no-changes  exit 0
Architecture tests   passing — ADR-009 boundary enforced mechanically
CI                   Build & test · Analyze C# · CodeQL · Validate docs & governance
CodeQL               no alert introduced by a merged PR. Open on main: 5 in
                       source (#193, #194 by design; #107, #127, #159 in
                       MainWindow, open since 2026-09-21/22) and 114 in
                       generated obj/ files (#223 is A17's generated interop)
Branch protection    required checks, linear history, conversation resolution
```

**The test count is unchanged by M2-1, M2-2 and M2-3.** All three surfaces live
in `Noto.Windows`, a `WinExe` with `UseWinUI`, and no test project covers it —
`Noto.Windows.Tests` sits below the XAML layer by design and must not reference
it. UI behaviour is therefore still validated by running the application rather
than by automated tests. That is a known gap, not a passing result. The 42
added by #9 are engine tests, in `Noto.Infrastructure.Tests`.

**`Noto.Windows.Tests` now exists** (#67). Its 35 original tests cover the
platform value types — frame-inset arithmetic, DIP↔pixel conversion, rectangle
invariants — plus guard tests asserting that neither it nor
`Noto.Platform.Windows` references the XAML layer. #16 slices 1–4 added
deterministic geometry tests and **runtime tests** (`RequiresDesktop`) that
call the real Win32 and DWM APIs on real windows, including the slice 3 window
subclass and the slice 4 hotkey registration and delivery; a headless agent
filters them out rather than fail. CI still builds and runs unit tests and never
launches the application, which is why #68 and #16 slices 2–4 required runtime
campaigns against the real application rather than relying on CI.

### Outstanding validation — #16 slice 6

`tools/validation/Invoke-Drawer.ps1` ran on a Release build of PR #92: an
isolated data root, Noto and every other window started by the shell, keys
sent to Noto only while it was in front, and every foreground claim bracketed
by a negative control. **54/54 checks (run `drawer-20261006-113225-2c47c1`), 0 invalid, negative controls refused 8/8.** Noto's `Noto.Workspace` trace shows each
request and what the coordinator decided.

Passed:
- topmost while shown: the window style, and `WindowFromPoint` over a
  maximized window and over borderless full-screen windows (one itself topmost)
- losing activation hides it — a click on another app, a click on the desktop,
  a virtual-desktop switch — and saves unsaved text first; a failed save keeps
  it shown, with the notice, and focus is not taken back
- pinned, or with the setting off, losing activation leaves it shown, above
  the other window; the hotkey still hides a pinned, focused Noto
- `Ctrl+W` on each surface hides it, saving first; a failed save keeps it shown
- Escape: all four behaviours on the editor, note list and folder list (12
  checks); Escape in the new-folder box cancels the input and never hides
- not deactivations: Noto's own context menu, an inner-edge drag across
  another window, a minimize
- the pin works by keyboard (focus, Space) and is never stored
- after an automatic hide, the hotkey and a second launch show it again
- virtual desktops: unpinned, a switch hides it and the hotkey shows it on the
  new desktop; pinned, it stays on its own desktop and the hotkey switches back

Regression on the same build: `Invoke-ShowHide.ps1` 35/35,
`Invoke-SingleInstance.ps1` 61/61, `Invoke-MinimizedActivation.ps1` 36/36,
`Invoke-Slice4Foreground.ps1` 36/36, `Invoke-EditorSaveOnClose.ps1` 19/19. The
first three and the last turn hiding on deactivation off in their roots, so
they keep validating the toggle, launch and close contracts unchanged; the
default is `Invoke-Drawer.ps1`'s.

| Item | Status |
| ---- | ------ |
| **Noto's own taskbar button** | **DEVIATION from the design gate, awaiting the owner's decision.** The gate predicted "hidden". Measured: the press does not take activation from Noto, and Windows does not minimize a window that is not minimizable — nothing happens |
| **Exclusive full-screen** | Out of reach (summoning Noto ends exclusive mode); **not validated** |
| **Startup** | A deactivation during launch never hides — unit-tested; its timing cannot be forced by the harness |
| **Windows 10, multi-display** | **NOT VALIDATED** (#32; one display connected) |

### Outstanding validation — A17 single instance

`tools/validation/Invoke-SingleInstance.ps1` ran on a Release build of the
PR #91 branch: isolated data roots, Noto and every second launch started by
the shell, each launch preceded by a real desktop click (the user's act), and
every foreground claim bracketed by a negative control (6/6 refused).
**61/61 checks** (run `single-instance-20261005-222315-0c7806`). A second
launch was caught the instant it appeared and watched to its exit; Noto's own `Noto.Instance` diagnostics were read from the
debug-output channel.

Passed:
- one owner: mutex held, pipe served by the owner's pid, one process, one window
- a second launch exits 0 in about 200 ms, never shows a window or a message
  box, and never opens the database (new opens denied on the file meanwhile)
- the owner, behind another app, minimized or hidden, comes forward docked
  with foreground and keyboard focus; already in front, it is left as it is —
  a launch never hides Noto
- shown on another virtual desktop: Windows switches to it. Hidden: shown on
  the current desktop, no switch (ADR-007 §4, unchanged)
- during startup (0, 150, 400 ms apart): one process survives, shown
- five simultaneous launches: one owner, four handed off, one window
- during shutdown (the owner's UI thread frozen the instant shutdown began):
  the new launch waits, then takes over once the old one exits (`tookOver=True`)
- a killed owner leaves no mutex or pipe; the next launch owns at once
- an unreachable owner (process suspended): message box at about 5.3 s, exit 2
- malformed, oversized, wrong-version, unknown and reserved kinds: `Rejected`;
  the owner is not activated and stays healthy
- an unrelated process cannot take the mutex or add a pipe instance; clients
  with the user SID disabled, at low integrity, or anonymous are refused by the
  pipe's ACL; a pipe served by another user (Windows' `epmapper`) is refused by
  Noto's client before anything is sent
- pipe squatting is detected and recorded (`NameTaken`); a second launch still
  never becomes a second workspace (exit 2)
- the elevated launch (`Invoke-SingleInstanceElevated.ps1`, one UAC approval,
  13/13): with no owner it is refused — message box, exit 3, never holds the
  mutex; with a normal owner it hands off (exit 0) and the owner stays the owner

Regression on the same build: `Invoke-ShowHide.ps1` 35/35 (7/7 controls
refused), `Invoke-MinimizedActivation.ps1` 36/36 (12/12),
`Invoke-Slice4Foreground.ps1` 36/36 (15/15), `Invoke-EditorSaveOnClose.ps1`
19/19. Mutation: 29/29 platform mutants killed by tests; of 10 integration
mutants, 7 killed at runtime and 8 by the source guards — two (a second
coordinator, the pipe after the window) are equivalent at runtime and caught
only by the guards, as is the mutex released before the pipe closes.

| Item | Status |
| ---- | ------ |
| **Another Windows session, another user's Noto** | **NOT VALIDATED end to end** — needs a second account. The client's checks are tested against Windows' own system pipes and by pure tests |
| **Residual shutdown race** | A launch accepted a moment before a close goes ahead is dropped; documented in ADR-013, not mitigated |
| **Packaged build (ADR-008)** | Not built yet; re-run the harness when the sparse package lands |
| **Windows 10** | **NOT VALIDATED** (#32) |

### Outstanding validation — #16 slice 5

`tools/validation/Invoke-ShowHide.ps1` ran on a Release build of PR #89: an
isolated data root, Noto started by the shell, every foreground claim bracketed
by a negative control (7/7 refused). 35/35 checks.

Passed:
- launch visible and docked; hide; show at the exact docked rectangle with
  foreground and keyboard focus
- restore from minimized; show from hidden-while-minimized
- unsaved text saved once before hiding; a failed save keeps the window shown
  with the text and the notice
- a tight burst of chords; a chord during a resize drag (ignored); a chord
  during shutdown (dropped, exit code 0); chords during startup (coalesced,
  never hidden)
- the taskbar button gone while hidden and back when shown
- virtual desktops, as decided (ADR-007 §4): a hidden Noto summoned from
  another desktop is shown on the current desktop with no switch; a shown
  Noto summoned from another desktop makes Windows switch back to it
- Alt+Tab through the real switcher, not a proxy: Noto shown is selectable;
  Noto hidden is never selected across a full cycle. The walk runs last,
  because an injected Alt press can unlock the foreground for the next
  `SetForegroundWindow` and would contaminate any later negative control

Closing ran under `Invoke-EditorSaveOnClose.ps1` (19/19). Regression:
`Invoke-MinimizedActivation.ps1` 36/36, `Invoke-Slice4Foreground.ps1` 36/36 —
its case B now expects the hide.

| Item | Status |
| ---- | ------ |
| **Windows 10** | **NOT VALIDATED** — virtual-desktop and Alt+Tab behaviour measured on Windows 11 only (#32) |
| **Multi-display, mixed DPI, display disconnected while hidden** | **NOT VALIDATED.** One display at 96 DPI; mixed DPI blocked by #30 |
| **Physical keyboard, IME** | **NOT VALIDATED** |
| **Second launch (A17)** | Covered by A17 (ADR-013) — see *Outstanding validation — A17* |
| **Session end or a kill while editing** | Unsaved text is still lost (#10; B19 is M3's) |

### Outstanding validation — minimized restore (#82)

**Validated.** The guard's decision is a pure function, covered by
deterministic tests that construct the displays. These include a display left
of the primary, which is the layout behind the off-screen restore. The
runtime tests use real Win32 windows on both edges and cover:
- `SW_RESTORE` and `SC_RESTORE` return to the exact docked rectangle
- a 480 DIP resize survives minimize and restore
- a clamped width never replaces the remembered one
- minimized activation restores and re-docks

Eight scripted mutants were all killed.

The corrected harness (`Invoke-MinimizedActivation.ps1`) ran on both edges, 3
trials each: minimized, then the hotkey with another app in front. Each trial
checked that Noto was restored (`WS_VISIBLE`, not minimized, not cloaked), was
the foreground window, held keyboard focus, and sat at exactly its docked
rectangle. Result: **36/36**, with every negative control refused. That ran on
a build whose product code is byte-identical to `63014bf`. A build without the
fix reproduced the defect in 4/4 trials.

| Item | Status |
| ---- | ------ |
| **Second display at runtime** | **NOT VALIDATED.** One display only. The off-screen restore with a display left of the primary is covered by deterministic tests, not by a real two-display run |
| **Display disconnected while minimized** | **NOT VALIDATED.** The window lands on the display nearest the saved placement (ADR-007 §4) |
| **Mixed DPI** | **NOT VALIDATED — blocked by #30** |
| **Shell gestures** | The docked window has no minimize button, and taskbar click, `Win+M` and `Win+D` did not minimize it when measured. Minimizing was driven by `SC_MINIMIZE` |
| **Hidden window** | **Out of scope.** Hide and show are slice 5; minimized and hidden stay distinct states |

### Outstanding validation — #16 slice 4

**Foreground evidence comes from the corrected harness**, `tools/validation/`
(PR #83 → `8f6d762`). Its `README.md` sets out the method:
- Noto and every helper are started by the running shell, not by the test.
- The other app is brought to the foreground by a real click and typing, and
  the typed text must arrive.
- Before and after every press, an unrelated shell-started process asks for
  the foreground and must be refused; any grant invalidates the run.
- Each run uses an isolated data root and a temporary virtual desktop, and
  cleans up every process it started.

The `Invoke-Slice4Foreground.ps1` campaign was run from a clean checkout. On
the slice 4 product code (`main` before #82), it gave **36/36**, with all 16
negative controls refused:
- another app in front → Noto is the foreground window with keyboard focus (3/3)
- already in front and focused → it stays, visible, with the same rectangle and
  no hide
- focus moved away → it is regained
- disabled → nothing is registered, and the chord leaves the other app in front
- chord held by another process before launch → Noto starts and docks, the
  holder keeps the chord and receives the press
- malformed binding `Ctrl+Hyper+Q` → the default registers and works, and the
  row is unchanged
- the chord is held while Noto runs and freed on exit
- no settings row changes, and every launch has a fresh, shell-started pid

On #82's product code, byte-identical to what `main` now has, it gave
**36/36** again, with all 16 controls refused: no regression. Both views of
the real data folders were unchanged. Visibility, minimized state, cloaking,
the foreground window and keyboard focus are each recorded separately.

The six real-app mutants were killed under the slice's original method: wiring,
the foreground call, registration order ×2, the wrong key, and WinUI
`Activate()`. That method could only make a mutant *survive*, through inherited
foreground rights, so those kills still stand. The slice 3 campaign also re-ran
on both edges under that method without regression. That is geometry evidence,
which foreground rights do not affect.

What the campaign did **not** establish:

| Item | Status |
| ---- | ------ |
| **Minimized window** | **NOT MET by slice 4; fixed by #82.** On the slice 4 code, pressing the chord made a minimized Noto the foreground window but left it minimized, with no keyboard focus; the corrected harness reproduced this in 4/4 trials. See *Outstanding validation — minimized restore (#82)* |
| **Negative-control caveat** | Windows occasionally grants the control probe the foreground. It was first seen on never-activated windows; on `6917692` it also happened right after a session unlock and on the first probe at a just-launched Noto. The cause is not established. Any grant makes the run INVALID, and only runs with every control refused count. See `tools/validation/README.md` |
| **Physical keyboard** | **NOT VALIDATED.** Every press was injected with `SendInput`; no key was pressed on a physical keyboard |
| **IME / AltGr layouts** | **NOT VALIDATED.** Only US keyboard layouts are installed on the validation machine |
| **User-visible conflict reporting** | **NOT IMPLEMENTED — deferred.** #16 requires it; a refused chord is reported through `Debug` only, until a surface (tray or settings) exists to report it on |
| **CodeQL** | **#193 open by design; #194 open, analysed.** #193 is the same `DefSubclassProc` call as #189, moved into the shared `WindowSubclass.CallDefault`; #189 is fixed on `main` as a result. #194 (`cs/missed-using-statement`, a note) is the ownership transfer in `StartHotkey`, which CA2000 requires and a `using` cannot express. Both review threads were resolved with that rationale; neither alert was dismissed or suppressed, and the CodeQL configuration is unchanged |

### Outstanding validation — #16 slice 3

The real-application campaign ran both edges on the real `Noto.exe` against
isolated data folders, with real mouse drags, Win+Arrow keys and an external
`SetWindowPos`: inner-edge resize, the 240 / 900 DIP clamps, the docked edge
and full height held, snaps and foreign moves refused, the visible width saved
to both keys at drag end, and edge and width restored after a restart. The
real data folders were unchanged. What it did **not** establish:

| Item | Status |
| ---- | ------ |
| **Second display at runtime** | **NOT VALIDATED.** One display only. The per-display width chain across two displays, the position guard across monitors, and the 7 px outer overhang onto a neighbouring display — which could be grabbable there — are covered by deterministic tests at most |
| **Mixed DPI** | **NOT VALIDATED — blocked by #30.** One display at 96 DPI; resize limits at other scales are covered by deterministic tests only |
| **Human visual review** | **OUTSTANDING.** The borderless appearance, resize-cursor feel, and closing without a caption button (Alt+F4 or the taskbar menu until the tray and hide slices) have not been looked at by a person |
| **CodeQL `cs/call-to-unmanaged-code` #189** | **Superseded by #193 (#16 slice 4).** The single `DefSubclassProc` call the subclass requires; the review thread was resolved with the rationale in ADR-007 §4, and the alert was not dismissed or suppressed. Slice 4 moved the call into `WindowSubclass.CallDefault`, so #189 is fixed on `main` and the same finding continues as #193 |


### Outstanding validation — #16 slice 2

The real-application campaign measured both edges on the real `Noto.exe`: the
visible frame flush with the work-area edge (0 px gap), the taskbar excluded,
and the outer rectangle overhanging by exactly the measured 7/0/7/7 invisible
border. What it did **not** establish:

| Item | Status |
| ---- | ------ |
| **Mixed DPI** | **NOT VALIDATED — blocked by #30.** One display at 96 DPI; no scale other than 100% was exercised |
| **Multi-monitor at runtime** | **NOT VALIDATED.** No second display was connected during the run. Negative and non-zero monitor origins are covered by deterministic tests only |
| **Mutations W5, N1, N10** | **RUNTIME-UNVERIFIED.** Each picks a display other than the window's at the native boundary; with one display they cannot fail. The runtime tests that would kill them iterate every connected display, but have not run on two |
| **Mutation A7 — docking before `Activate`** | **SURVIVED, retained by decision.** It produced identical bounds; docking stays after `Activate`, because reading the frame inset from a window not yet shown is not relied on |

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

### Outstanding validation — Windows App SDK 2.5.1 (#68)

The A/B campaign validated 25 scenarios on both 1.6 and 2.5.1 with no
regression observed. Three things it did **not** establish, recorded so the
coverage is not read as wider than it was:

| Item | Status |
| ---- | ------ |
| **High Contrast** | **NOT VALIDATED.** Applying a contrast theme needs the interactive Settings UI; the registry and `.theme` routes did not activate it, confirmed by `SystemInformation.HighContrast` reading `False`. Blocked identically on both SDK versions, so it is evidence neither for nor against a 2.5.1 regression |
| **Note-list virtualization / scrolling** | **NOT EXERCISED.** The fixture's 7 rows occupy roughly 245 px in a ~625 px list, so the list never scrolls and no row is virtualized away. The *editor* was scrolled over an 11,898-character note on both versions; that is not the same thing |
| **Mixed-DPI and multi-monitor** | **NOT VALIDATED.** The campaign ran the application on one 1920×1080 display at 96 DPI and exercised no multi-monitor or mixed-DPI behaviour. Current status is under #16 slices 7a/7b |

**A pre-existing UI gap was found, and is not an SDK regression.** With a
folder open that contains no notes, `Ctrl+N` cannot create the first note: the
handler is attached to the note `ListView`, and an empty `ListView` has no item
to take focus. Reproduced identically on both SDK versions, so it predates the
upgrade and belongs to M2-3. Tracked as **#69**; no fix was included in #68.
**Fixed by PR #87** (runtime-validated, including a pre-fix control that
reproduced it); #69 is still open on GitHub pending the owner's decision.

**Known issues** (open):
- unsaved editor text is lost if the session ends or the process is killed
  while editing (#10; B19 continuous persistence is M3's);
- #69 is fixed (PR #87) but still open on GitHub, pending the owner's decision;
- the harness's negative control is occasionally granted for an unknown cause
  (see `tools/validation/README.md`); such runs are INVALID and repeated.

Four defects found during M1 were all real and are fixed — a ULID monotonicity
bug (PR #46), a process-global SQLite pool race (PR #47), and in PR #48 the
folder pin/unpin `UpdatedAt` no-op violation plus a missing `DeletedAt` guard on
two repository updates.

**One recorded follow-up.** A `UNIQUE` violation on `Tags.Name` surfaces as
`StorageException`, not `DuplicateName`. Unreachable under §10's frozen
"single user, single process" model — the check-then-insert window needs
concurrent writers — so it is deferred rather than fixed.

**Worth remembering:** PR #48 reached 428 passing tests, clean CI and clean
CodeQL while still violating contract §4, and PR #50 passed 110 tests with a
redundant write that state could not observe. Both were caught by challenging
the contract and mutating the implementation, not by reading test results.

---

## 15. Next Development Path

```
Done: Core Note Engine complete (PR #52) · design system (PR #56)
    ↓
Done: M2-1 folder pane (#57) → M2-2 note list (#60) → M2-3 note editor (#63)
      three surfaces; the UI creates, edits and deletes notes
    ↓
Done: #16 slice 1 platform primitives (#74) → slice 2 basic edge docking (#76)
      → slice 3 edge setting, resize, width persistence (#78)
      → slice 4 global activation hotkey (#80) → restore/re-dock fix (#82)
      → slice 5 show/hide (#89)
    ↓
Done: A17 single instance (#90, #91)
    ↓
In review: #16 slice 6, the drawer (#92)
    ↓
M2 remaining — the rest of the workspace shell: the tray and run at login,
      the settings surface, #16 animation, multi-monitor and hardening. This is
      what stands between the surfaces and a build that can be used daily
    ↓
#14 markdown rendering · #15 export — the remaining M1 issues
    ↓
M3 — SideNotes parity: the first product. B19 continuous persistence and
      the invisible-markdown editor are resolved here, not before
```

The roadmap does not hold UI until the backend is finished. M2 is described as
*"the first build usable daily. Everything after it is informed by"* it — and the
navigation half of that is now built. It is not yet usable daily: the surfaces
exist and open docked to a screen edge, and a global hotkey shows and hides
them, but there is no tray and nothing keeps them on top.

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
