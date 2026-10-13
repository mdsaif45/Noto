# Noto Development Status

```
Project:            Noto — a Windows-native notes application
Phase:              closing M0; the product phase (M3 slices) is prepared, not started
Last updated:       2026-10-13
Evidence baseline:  main @ acb9e75 (PR #93, #16 slice 7)
In review:          PR #96 run at login (#94) · PR #97 Noto icon (#95)
                    PR #98 architecture enforcement (#20) · PR #99 logging (#7)
                    PR #100 failure handling (#10) · PR #101 performance baseline (#11)
```

> **Read this first.** The technical Windows shell is strong: a docked,
> topmost drawer with a global hotkey, a tray, single instance, inner-edge
> resize, and (in review) a hidden start at sign-in — each runtime-validated on
> frozen Release builds. **The Noto product on top of it is still early.**
> A user can create, rename and enter folders, and create, edit and delete
> notes in a plain-text editor. Nothing else a SideNotes user would reach for
> exists yet: no settings, no markdown rendering, no search, no recycle bin, no
> reordering, pinning, colours or folding in the UI, no export or backup.

```
engine command exists  ≠  a user can reach it  ≠  the parity row is met
WinUI 3 validated      ≠  Noto UI designed
```

This document is the **current state only**. How each piece was delivered and
validated is in [history.md](history.md); what comes next is
[product-slices.md](../roadmap/product-slices.md).

**Status vocabulary** (the only words used for status here): implemented ·
validated (implemented and runtime-validated on a frozen Release build) · in
review (built, tested, validated; PR open) · partially implemented · designed,
not implemented · not started · deferred · dropped.

---

## 1. Milestones

| Milestone | Status | GitHub (open / closed) |
| --------- | ------ | ---------------------- |
| **M0** Foundation & Architecture | **Not complete** — every engineering item is built; four are in review; three non-engineering issues are open (§2) | 7 / 6 |
| **M1** Core Note Engine | **Engine complete, product not** — notes, folders and tags are complete in the engine; markdown (#14) and export/backup (#15) are not started (§3) | 3 / 0 |
| **M2** SideNotes Workspace | **Partially implemented** — the shell is validated; settings, multi-monitor, motion and side switching are not built (§4) | 3 / 1 |
| **M3** SideNotes Parity | **Not started as M3 work** — 15 of 264 rows `[x]` with #96 (13 of 147 MUSTs), all by-products of M2 (§5) | 2 / 0 |
| **M4** Hardening | Not started; the performance baseline (#11) is its first input | 1 / 0 |
| **M5** Windows Enhancements | Not started | — |
| **M6** Contextual Notes | Not started; ADR-005, ADR-006 Proposed | 3 / 0 |
| **M7–M9** | Not started | — |

#94 and #95 have no milestone; they are M2 work.

---

## 2. M0 — Foundation & Architecture

| Item | Status | Evidence |
| ---- | ------ | -------- |
| Governance, research, ADRs | implemented | ADR-001–014 on `main`; ADR-015 in PR #96 |
| Window spike / framework gate | validated on Windows 11 | ADR-001. Windows 10 22H2 not verified (#32, M4) |
| Solution structure (ADR-009) | implemented | 6 source, 4 test projects |
| Architecture tests | implemented; **#20 in review** | Core boundary on `main`. PR #98: layer graph and ADR-010 handler rules over the built output, `Noto.UI` isolation. Complete; mutation-tested 8/8 |
| CI | implemented | build and test, C# analysis, CodeQL, docs governance; branch protection |
| Commands (ADR-010) | implemented | every mutation is a UseCases handler |
| Domain events (ADR-010) | **not started** | #21. Nothing consumes an event; ADR-010 allows building it with its first consumer |
| Design tokens (ADR-011) | implemented | #22, PR #56 |
| SQLite, migrations | implemented | schema v1, migration 002, safety copy |
| Settings persistence | implemented | #9, PR #65 |
| Test infrastructure | implemented | SQLite pool isolation (#47, #85); `tools/validation` runtime harness |
| Structured logging | **#7 in review** | PR #99: no event takes a parameter that can carry free text — a signature test enforces it; a migration is logged by its version, a setting by its declared name, never a scope or value; rolling JSON-lines file. Mutation-tested; runtime-validated 8/8 on frozen `c36674b` |
| Error handling | **#10 in review** | PR #100 (stacked on #99): one failure boundary — logged once, the open note saved once, ended once, and the end does not depend on the log or on releasing resources succeeding; startup database failures shown, not crashed. 18/18 mutants killed; runtime-validated 32/32 on frozen `c36674b` (Debug, for its fault trigger) |
| Performance harness and baseline | **#11 in review** | PR #101: `Invoke-Performance.ps1`, the baseline and its budgets in [quality-gates.md](../architecture/quality-gates.md#performance-budgets). Five budgets not met; recorded, not revised. Reproduced by an independent run from a fresh clone and the documents alone. A cold start after sign-in is not measured: the cold number is a first start after install, its upper bound |
| Project board | not started | #19 — needs a `project`-scoped token |
| Code-signing RFC | not started | #5 — research; blocks distribution, not code |

### What closes M0

```
1. merge, in order:  #96 → #97 → #98 → #99 → #100 → #101   (each needs the owner's approval)
2. decide whether #11 accepts the first-start-after-install number as its cold start,
   or wants one run right after a restart as well (one restart, then the documented command)
3. decide #21, #19, #5 — none is foundation code a later slice is blocked on:
     #21 domain events   no consumer yet → move to the milestone of its first consumer
     #19 project board   process, not code → keep open outside M0, or close
     #5  signing RFC     blocks distribution → move to the packaging milestone (M4/M9)
```

When 1–3 hold, M0 is **complete**, and no new M0 work is opened after it.

---

## 3. M1 — engine foundation vs product capability

| | Engine foundation | Product capability (a user can do it) |
| - | ----------------- | -------------------------------------- |
| Notes | create, get, update content, move, reorder, pin, fold, colour, delete, restore, list deleted | create, open, edit (plain text), delete |
| Folders | create, rename, delete, restore, pin, reorder, list, list deleted | create, rename, enter |
| Tags | create, rename, delete, assign, remove, list, list notes for a tag | none (and no parity row) |
| Markdown (#14) | content stored as markdown source | **not started** — the editor is a plain `TextBox` |
| Export and backup (#15) | — | **not started** — ADR-002's promise is unbacked |
| Search (#17) | — | **not started**; FTS5 deferred to M3 |
| Attachments, tasks | folder path named only | not started |

**Of the engine's 30 commands and queries, a user reaches 8**: create and
rename a folder; list folders and the notes in one; get, create, update and
delete a note. #13 is complete in substance and still open on GitHub.

---

## 4. M2 — what exists and what does not

**Implemented mechanisms**, all validated on one display unless marked:

```
docked to the right or left edge, at a remembered width     #16 slices 1–3
inner-edge resize, width remembered per display             #16 slice 3
global hotkey Ctrl+Alt+Win+Space: show / hide / restore     #16 slices 4–5, #82
topmost drawer; put away by Ctrl+W, Escape, deactivation    #16 slice 6
session pin (header toggle, tray)                           #16 slice 6–7
tray icon and menu; session end saves unsaved text          #16 slice 7
one Noto per data root; a second launch hands off           A17
save on leave, on hide and on close                         M2-3, #86, slice 5
hidden start at sign-in (Start Noto with Windows)           #94 — in review
Noto's own icon                                             #95 — in review
```

**What M2 does not provide as a finished product:**

```
no settings surface        every preference lives only in the settings table
no side switching          workspace.edge is honoured; nothing a user presses changes it
no Open Bar, no Hot Side   the hotkey and tray are the only activation surfaces
no motion                  hide is AppWindow.Hide(); no slide
no multi-monitor / mixed DPI correctness    #16 slices 8a/8b, #30
no reorder, drag & drop, pin, colour, fold, move in the UI
no continuous persistence  B19, deferred to M3 by decision
```

---

## 5. SideNotes parity

**Authoritative source: [sidenotes-parity.md](../product/sidenotes-parity.md),
*Current status*.** Re-scored against the running application on 2026-10-13.

```
264 rows · with #96 merged:  15 [x] · 16 [~] · 233 [ ]
on main (A16 not merged):     14 [x] · 16 [~] · 234 [ ]
MUST: 13 of 147 done with #96 (12 on main)
```

The re-score took two rows down (A12, G51: a stored option no user can choose
is not delivered) and eleven up that had never been re-read (A19, C13, G1, G9,
I13 to `[x]`; A7, C2, C15, F1, G14, G15 to `[~]`).

---

## 6. Product gaps

| Area | What a SideNotes user expects that Noto lacks | Parity rows |
| ---- | --------------------------------------------- | ----------- |
| **Workspace** | Settings to change side, escape behaviour, hotkey and outside-click; Open Bar and Hot Side; slide animation; multi-monitor | A2–A4, A12, A18, A20, J1–J5 |
| **Note management** | Recycle bin and restore; reorder (keys and drag); move between folders; pin, colour, fold; duplicate; note and list bottom bars; placement of new notes | B5–B15, B17–B23, B26, C8–C12 |
| **Editor / markdown** | Rendered (invisible) markdown; formatting shortcuts and toolbar; checklists, code, tables policy; autosave | D1–D51, B19, G27–G42 |
| **Content / productivity** | Images and attachments, file and folder links, clipboard and drag-in capture, search | B2–B4, D-content rows, E1–E8 |
| **Integration** | `noto://` URL scheme, CLI, export, backup, import | H1–H5, I1–I15, J27–J40 |
| **Settings / personalization** | A settings surface at all; theme choice; fonts and text size; note colours; rebinding | F1–F14, G50–G52, J6–J26 |
| **UX / polish** | Control states, focus visuals, motion, menus, empty states that guide, Light and High Contrast review | F1, B26, C15, §7 below |

---

## 7. UI / UX maturity

| Aspect | Maturity | Evidence |
| ------ | -------- | -------- |
| Design tokens | implemented, minimal | `Noto.UI/Tokens/Tokens.xaml`: 4 spacing steps, body and caption type (Segoe UI Variable), one radius, two border weights |
| Typography | early | two sizes; no title, subtitle or display steps; no editor typography choices |
| Spacing and layout | early | tokens applied; one header row, three surfaces that replace each other |
| Components | early | styles for folder row, note row, folder-pane button, name input, editor; no context menus, flyouts, dialogs, toggles or settings controls of Noto's own |
| Themes | partially implemented | Light, Dark, High Contrast follow Windows; no manual choice; Light and High Contrast not visually reviewed |
| Surfaces | partially implemented | folder list, note list, editor; Loading / Loaded / Empty / Error + Retry on both lists; no settings, bin or search surface |
| Editor | early | plain `TextBox`; no rendering, no placeholder, no formatting |
| Motion | not started | no transitions or animations anywhere |
| Settings | not started | none |
| Interaction polish | early | keyboard paths exist (`Ctrl+N`, `Ctrl+Down`, `F2`, `Enter`, `Esc`, `Ctrl+W`); 22 UI Automation annotations; one tooltip; no access keys |

The target is SideNotes-level function and polish in Noto's own Windows-native
visual language (ADR-011), not a macOS copy. Slice 1 starts the interaction
foundation ([product-slices.md](../roadmap/product-slices.md)).

---

## 8. Performance

Baseline measured by `tools/validation/Invoke-Performance.ps1` (#11). The
numbers, budgets and verdicts are recorded once, in
[quality-gates.md](../architecture/quality-gates.md#performance-budgets).

---

## 9. Quality gates

```
Tests          main @ acb9e75: 1526
               all six open PRs merged locally (c36674b): 1654 (Core 273 · UseCases 59 · Infrastructure 645 · Windows 677)
Format         dotnet format --verify-no-changes   exit 0
CI             Build & test · Analyze C# · CodeQL · Validate docs & governance
Runtime        tools/validation — every harness on a frozen Release build,
               isolated data, shell-started, negative controls; see history.md
```

UI behaviour is validated by running the application (`tools/validation`);
`Noto.Windows.Tests` covers the platform and source guards, not the XAML.

**Not validated anywhere:** a real logoff, shutdown or sign-in; Windows 10;
more than one display; a non-US keyboard layout.

---

## 10. Open work on GitHub

| PR | Issue | Base | State |
| -- | ----- | ---- | ----- |
| #96 | #94 run at login | `main` | ready; validated 363/363 on frozen `7205ef2`, real registry round trip |
| #97 | #95 Noto icon | `feat/94-run-at-login` | ready; merges after #96 |
| #98 | #20 architecture enforcement | `main` | ready |
| #99 | #7 structured logging | `main` | ready |
| #100 | #10 failure handling | `feat/7-structured-logging` | ready; merges after #99 |
| #101 | #11 performance baseline | `feat/94-run-at-login` (needs `--login`) | ready; retargets to `main` after #96 |

All six merged together (`c36674b`) build with no warnings and pass 1654
tests. Merging #100 after #96 meets one documentation conflict in
`architecture-overview.md` (*Not as described*): keep #96's startup row and
#100's error-policy and logging rows. **#16 stays open**: slices 8a/8b (multi-monitor, mixed DPI)
and 9 (hardening) remain. #69 is fixed by PR #87 and still open; #13 is
complete in substance and still open — closing either is the owner's decision.

---

## 11. Decisions future work must respect

| Decision | Source |
| -------- | ------ |
| **Folders are flat** — no `ParentId`, no nesting | parity C3; design §5 |
| **Folder names are not unique** | Case G |
| **Schema v2 is sufficient** for the engine | contract §12 |
| **Case B / Case D** deletion and restore semantics | contract §7; deletion-semantics |
| **I1/I2** — ordinary queries are active-only; no `includeDeleted` flag | deletion-semantics §6 |
| **Exactly four failure reasons** | contract §6 |
| **Title is computed, never stored** | parity B16 |
| **ULID C′** | ADR-012 |
| **Local-first SQLite** — no sync architecture until it is built | ADR-002 |
| **Every mutation goes through a UseCases handler**; enforced over the built output | ADR-010; #20 |
| **Logs never carry note content, messages or paths** — by type | #7 |
| **One failure boundary** — logged once, saved once, ended once | #10 |
| **Save on leave, hide and close is interim** — B19 continuous persistence is designed with the editor (product slice 3); no save command, button or autosave before it | parity B19 |
| **A parity row moves only when a user can do every clause** | sidenotes-parity.md |

---

## 12. Deferred and dropped

```
deferred   Domain events            #21 — built with its first consumer
deferred   FTS5 search foundation   to M3 with search (#17)
deferred   Purge / retention        Cases E/F — needs a policy decision
deferred   Contextual notes         ADR-005/006 Proposed, M6
deferred   Sync / cloud             ADR-002; post-v1
deferred   Continuous persistence   B19 → product slice 3
dropped    Nested folders           C3: flat is the requirement
dropped    Folder colours, all-notes view, templates   C17, C18, B24
dropped    23 DROP parity rows      each with its reason in sidenotes-parity.md
not on the roadmap   AI, plugins, sharing
```

---

## 13. Next

M0 closes (§2), then **product slice 1 — Workspace + Settings + premium
interaction foundation**, then slice 2 (note management and folders), then
slice 3 (the editor). Scope, parity rows, acceptance criteria and validation:
[product-slices.md](../roadmap/product-slices.md).

---

## 14. Maintaining this document

> Update this document whenever a milestone, slice, decision or feature status
> changes. **Status is derived from repository evidence — code, git history,
> issue state, test results, runtime validation — never from assumption.**

It is a **status document**. It never overrides the ADRs, the parity
specification, the frozen engine contract, deletion semantics or issue
acceptance criteria. Where they disagree, they are right and this is stale.

- **Do not confuse architecture with product.** A validated WinUI host is not a
  designed interface; a tested command handler is not a shipped feature.
- **Do not mark parity rows from the engine side.** A row is done when a user
  can do the thing.
- **Current state only.** When work merges, move its narrative to
  [history.md](history.md) and keep this document short.
