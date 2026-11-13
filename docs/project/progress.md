# Noto Development Status

```
Project:            Noto — a Windows-native notes application
Phase:              v0.1 released (2026-11-06); v0.2 built, its owner checks open
Last updated:       2026-11-13
Evidence baseline:  main @ 30a8fea (PR #184, idle memory)
In review:          the parity score (#162)
```

> **Read this first.** v0.1, the Basic Release, shipped on 2026-11-06: a
> docked, topmost notes drawer with a rendered markdown editor, folders, the
> Recycle Bin, search, pictures, links, export, backup and restore, settings,
> and a global shortcut, each runtime-validated on frozen Release builds. v0.2
> added the items in [release-v0.2.md](../roadmap/release-v0.2.md): note
> colours, folding with "+ N lines", fonts and text size, colour swatches,
> highlight and a formatting toolbar, drag and drop to import, three more
> global shortcuts (new note, note from the clipboard, search), clear checked
> tasks, automatic backups, crash-loop detection and idle memory within budget.
> **Open:** the owner's checks (V1–V3, #31), the dock spike (#153), and the
> SideNotes rows still open after the v0.2 score (#30, #31, #185–#199).

```
engine command exists  ≠  a user can reach it  ≠  the parity row is met
WinUI 3 validated      ≠  Noto UI designed
```

This document is the **current state only**. How each piece was delivered and
validated is in [history.md](history.md); what comes next is
[release-v0.2.md](../roadmap/release-v0.2.md) and the milestones below.

**Status vocabulary** (the only words used for status here): implemented ·
validated (implemented and runtime-validated on a frozen Release build) · in
review (built, tested, validated; PR open) · partially implemented · designed,
not implemented · not started · deferred · dropped.

---

## 1. Milestones

| Milestone | Status | GitHub (open / closed) |
| --------- | ------ | ---------------------- |
| **M0** Foundation & Architecture | **Not complete on GitHub** — #5 (code-signing RFC) and #11 (performance harness) open; the harness is in use (quality-gates.md) | 2 / 10 |
| **M1** Core Note Engine | **Complete** — markdown (#14) and the engine (#13) closed; export and backup (#15) closed in v0.2 | 0 / 2 |
| **M2** SideNotes Workspace | **Implemented** — the shell, settings, side switching and motion are built; open: exclusive full-screen (#186); multi-monitor is #30 (v0.2) | 1 / 3 |
| **M3** SideNotes Parity | **Partially implemented** — 122 of 264 rows `[x]`, 103 of 147 MUSTs (§5); the open MUSTs are #185–#199 here, and #30, #31 in v0.2 | 15 / 0 |
| **M4** Hardening | Not started as a milestone; v0.1's performance and stability campaign is recorded in quality-gates.md | 0 / 0 |
| **M5** Windows Enhancements | Not started | 1 / 0 |
| **M6** Contextual Notes | Not started; ADR-005, ADR-006 Proposed | 4 / 0 |
| **M7–M9** | Not started | — |
| **v0.2** | **Built** — every F item merged; open: V1 (#161), V2 (#30), V3 (#32), V4 (#162, in review), #31 and the dock spike (#153) | 6 / 33 |

#17 (FTS5 search) is open in M3 although search is built (ADR-018); closing it is the owner's decision.

---

## 2. M0 — Foundation & Architecture

| Item | Status | Evidence |
| ---- | ------ | -------- |
| Governance, research, ADRs | implemented | ADR-001–021 on `main` |
| Window spike / framework gate | validated on Windows 11 | ADR-001. Windows 10 22H2 not verified (#32, v0.2) |
| Architecture tests, commands, events | implemented | #20, #21: every mutation is a UseCases handler through the dispatcher, enforced over the built output |
| Logging, error handling | implemented | #7, #10: logs never carry note content; one failure boundary; crash-loop detection (v0.2) |
| Performance harness | implemented; **#11 open** | `Invoke-Performance.ps1`; budgets and verdicts in [quality-gates.md](../architecture/quality-gates.md#performance-budgets) |
| Code signing | **#5 open** | ADR-008; the release decision waits for the owner's publisher identity |

---

## 3. M1 — engine foundation vs product capability

| | Engine foundation | Product capability (a user can do it) |
| - | ----------------- | -------------------------------------- |
| Notes | create, update, move, reorder, pin, fold, colour, delete, restore | all of these, from keys, menus and the card bar |
| Folders | create, rename, delete, restore, pin, reorder | all of these, with note counts |
| Tags | the full set | none (and no parity row) |
| Markdown | parsed by Core (ADR-017) | a rendered editor with hidden markup, a formatting menu and toolbar |
| Export, backup, import | ADR-020 | export to Markdown, backup and restore, daily automatic backups, import of folders and dropped files |
| Search | FTS5 (ADR-018) | Ctrl+F, the header button and a global shortcut |
| Pictures, links | ADR-019 | paste, drop and draw pictures; open links and file paths, checked first |

---

## 4. M2 — what exists and what does not

**Implemented and validated**, on one display unless marked:

```
docked to the right or left edge, at a remembered width       Settings › Side
a global shortcut, rebindable                                 Settings › Shortcuts
topmost; put away by Ctrl+W, Escape (four modes), deactivation
keep open (header, tray, Ctrl+D)                              #146
tray icon and menu                                            ADR-014
one Noto per data root; a second launch hands off             ADR-013
start at sign-in, hidden                                      #94
the drawer slides in and out                                  #145
a hidden Noto gives back its unused memory                    #160
```

**Not built:**

```
no Open Bar, no Hot Side, no drag to reveal      #185
exclusive full-screen not validated              #186
multi-monitor and mixed DPI not validated        #30
dock that pushes other windows aside             #153 (spike, not run)
```

---

## 5. SideNotes parity

**Authoritative source: [sidenotes-parity.md](../product/sidenotes-parity.md),
*Current status*.** Re-scored against `main` for v0.2 on 2026-11-13 (#162).

```
264 rows:  122 [x] · 32 [~] · 110 [ ]      (2026-10-13: 15 · 16 · 233)
MUST: 103 of 147 done · 24 partial · 20 not started (B25 and C3 provisional, not scored)
```

Every MUST still open has an issue: #30, #31 (v0.2) and #185–#199 (M2, M3).
Rows scored from code and harness that the owner should confirm by hand: A18,
B4, H14, D1.

---

## 6. Product gaps

| Area | What a SideNotes user expects that Noto lacks | Issues |
| ---- | --------------------------------------------- | ------ |
| **Workspace** | Open Bar, Hot Side, drag to reveal; exclusive full-screen; multi-monitor | #185, #186, #30 |
| **Note management** | A list menu and double-click folding; inline folder create in Move to; the new-note placement setting; colour styles; duplicate; print | #188–#191 |
| **Editor** | Code mode; insert picture (Ctrl+Shift+P); Ctrl+] / Ctrl+[; separator command; file shortcuts with previews | #194–#196 |
| **Capture and clipboard** | A **+** menu with note from clipboard; copy picture; code detection; link titles; drags from other apps | #187, #31 |
| **Integration** | `noto://` URI and a command line | #192 |
| **Search** | Ctrl+R to launch a result's link; Ctrl+Click to open its folder | #197 |
| **Settings** | Folder history; the two remaining global shortcuts; backup frequency; themes | #193, #198, #199 |

---

## 7. UI / UX maturity

| Aspect | Maturity | Evidence |
| ------ | -------- | -------- |
| Design tokens | implemented | ADR-011; contrast checked by `DesignSystemGuardTests`, High Contrast included |
| Typography | implemented | heading steps scaled to the chosen note text size (F13) |
| Components | implemented | rows, cards, menus, flyouts, settings controls, toolbar, all from tokens |
| Themes | implemented | System, Light, Dark in Settings; High Contrast follows Windows; screenshots of Light and Dark per release (`Invoke-Visual`) |
| Surfaces | implemented | folder list, note stack, editor, search, Recycle Bin, settings, welcome tour |
| Motion | implemented | drawer slide (F2), surface transitions; off with Windows' animation effects |
| Accessibility | implemented; **hand check open** | UI Automation names and help text throughout; the hand check with Narrator is V1 (#161) |

---

## 8. Performance

Measured by `tools/validation/Invoke-Performance.ps1` and `Invoke-Baseline.ps1`;
the budgets and verdicts are recorded once, in
[quality-gates.md](../architecture/quality-gates.md#performance-budgets). Idle
memory is within its budget since v0.2 (#160).

---

## 9. Quality gates

```
Tests          main @ 30a8fea: 3169 (Core 1087 · UseCases 128 · Infrastructure 935 · Windows 1019)
Format         dotnet format --verify-no-changes   exit 0
CI             Build & test · Analyze C# · Package · CodeQL · Validate docs & governance
Runtime        tools/validation — every harness on a frozen Release build,
               isolated data, a temporary desktop, negative controls; see history.md
```

**Not validated anywhere:** a real logoff, shutdown or sign-in; Windows 10
(#32); more than one display (#30); a non-US keyboard layout; drags from
apps other than Explorer (#31).

---

## 10. Open work on GitHub

| PR | Issue | State |
| -- | ----- | ----- |
| this score | #162 parity score | in review |

Open issues without a PR: the owner's checks (#30, #31, #32, #161), the dock
spike (#153), the parity issues (#185–#199), #5, #11, #17, and M5–M6 research
(#4, #12, #18, #163, #164).

---

## 11. Decisions future work must respect

| Decision | Source |
| -------- | ------ |
| **Folders are flat** — no `ParentId`, no nesting | parity C3; design §5 |
| **Folder names are not unique** | Case G |
| **Case B / Case D** deletion and restore semantics | contract §7; deletion-semantics |
| **Title is computed, never stored** | parity B16 |
| **ULID C′** | ADR-012 |
| **Local-first SQLite** — no sync architecture until it is built | ADR-002 |
| **Every mutation goes through a UseCases handler** | ADR-010; #20 |
| **Logs never carry note content, messages or paths** — by type | #7 |
| **The markdown source is the truth**; markup hidden by format, never removed | ADR-017 |
| **New global shortcuts are rebindable and off by default** | principle 7; implementation plan §10.1 |
| **A parity row moves only when a user can do every clause** | sidenotes-parity.md |

---

## 12. Deferred and dropped

```
deferred   Purge / retention        Cases E/F — needs a policy decision
deferred   Contextual notes         ADR-005/006 Proposed, M6
deferred   Sync / cloud             ADR-002; post-v1
deferred   Dock that reserves space #153 — a spike first (ADR-007 §4)
dropped    Nested folders           C3: flat is the requirement
dropped    Folder colours, all-notes view, templates   C17, C18, B24
dropped    23 DROP parity rows      each with its reason in sidenotes-parity.md
not on the roadmap   AI, plugins, sharing
```

---

## 13. Next

The owner's checks for v0.2 (V1–V3, #31), then deciding which of the parity
issues (#185–#199) join the next release, and whether to run the dock spike
(#153).

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
