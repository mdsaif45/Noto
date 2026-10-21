# Noto implementation plan: P0–P11

Status: **Locked 2026-10-20.** The decisions are in section 9; the shortcut map is in section 10. A change to a locked decision is recorded here before code depends on it.

Sources audited:
- `docs/product/*`, `docs/roadmap/roadmap.md`, `docs/project/progress.md`, the ADRs and `quality-gates.md`.
- Code, tests and harness on `feat/slice1-motion`, which is main plus the whole Slice 1 stack.
- GitHub `mdsaif45/Noto`: 10 milestones, 28 issues, 81 PRs.
- Apptorium SideNotes up to **1.6.6 (2026-09-21)**, from its release notes, the App Store, Setapp and the help articles.

The generated tables (sections 1 and 2) come from `gen_plan.py`. It asserts that each of the 269 rows maps exactly once.

The parity doc (`sidenotes-parity.md`) stays the single requirement list. This plan only schedules it, and adds 5 rows in section 2.3, each justified there.

---

## 0. Where we are (facts, not plans)

**Code on `feat/slice1-motion`**

```
Noto.Windows (composition root) --> UseCases --> Core <-- Infrastructure
          |                                         ^
          +--> Platform.Windows ----------------------+        UI = leaf (XAML resources only)
```

- The layering holds. No upward references.
- One soft spot: `App.OnSettingChanged` takes the concrete `SqliteSettingsStore`.

**Engine**
- Note and folder commands are complete: create, update, move, reorder, pin, fold, colour, delete, restore, tags, and the recycle-bin queries.
- Many of them have **no UI**.

**UI**
- Every surface lives in `MainWindow.xaml.cs`: 1944 lines of code-behind, no MVVM by design.
- `App.xaml.cs` is 1231 lines.
- The editor is a plain `TextBox` over the markdown source.
- Notes are saved when you leave the editor or hide the window. There is no autosave yet: parity B19 is recorded as deferred.

**Schema**
- Schema v2: Folders, Notes, Tags, NoteTags, Settings.
- Not created yet: attachments and FTS.

**Not built:** search, backup, import/export, last-folder memory, a global error handler, real logging (only a `NullStorageLog` exists), and the URI/CLI verbs (those are reserved and rejected).

**Tests and CI**
- About 1,163 test methods (1,742 test cases including theory rows).
- 14 guard/boundary classes.
- CI runs format, build, tests and CodeQL.
- The `tools/validation` desktop harness runs manually only.

**Parity score:** 11 rows `[x]` and 10 `[~]` out of 264. Slice 1 is **not re-scored** in the doc.

**Open PRs**
- #106→#109: the Slice 1 stack.
- #98–#105: the M0 backlog.
  - #98 architecture tests
  - #99 logging
  - #100 error boundaries
  - #101 performance baseline
  - #102 icon
  - #103 code-signing docs
  - #104 command and event dispatch
  - #105 contributing docs
- None of these is merged.
- The Slice 1 stack is built on `main` *without* the M0 backlog.

**P0 so far (this session)**
- #107 commit `27fcd36`: a hovered, pressed or disabled off switch keeps its outline and fill. 18/18 mutants killed.
- #108 and #109 rebased: range-diff shows every commit `=`.
- Full stack: 1,742 tests pass; Release and Debug both 0 warnings / 0 errors; format clean.
- Light and Dark runtime: fixed.
- **High-contrast runtime check: pending** (High Contrast is OFF).
- Frozen builds: `noto-frozen-slice1-0ade2fc`, Noto.exe `6ACABC22CA89…`, and `noto-frozen-perf-after-9d71132`, Noto.exe `CDC063295FB6…`. Earlier frozen builds are kept.

---

## 1. Master phase map

| Phase | Goal | Exit (cheap gates on every PR; runtime campaigns deferred to the release candidate) | Rows |
|---|---|---|---|
| P0 | Stabilise Slice 1 | #107 fixed, High Contrast-verified, frozen; a local integration base (Slice 1 → M0 → folder-entry focus) that P1 builds on. No re-scoring (decision 4) | - |
| RC | Release-candidate campaign | The complete frozen desktop / runtime / accessibility / performance campaign, run once over the accumulated candidate; only then are shipped rows re-scored | 2.1 |
| P1 | Note & folder management | Every B/C row except content, colour and global capture is reachable from mouse, keyboard and context menu, with failure paths | 2.1 |
| P2 | Editor engine | Explicit editor model; hidden-markdown live styling; every D-row editing command; continuous persistence | 2.1 |
| P3 | Rich content | Attachment semantics defined; images, file shortcuts, links, colours, formatted paste | 2.1 |
| P4 | Search | FTS5, incremental, keyboard-first, quick copy, within the 10k-note budget | 2.1 |
| P5 | Import / export / backup / recovery | Inspectable backups; verified restore; integrity checks; markdown export; import; print; image export | 2.1 |
| P6 | Windows integration | All six global chords; Open Bar, Hot Side, drag-to-reveal; multi-monitor, DPI and virtual-desktop behaviour defined; `noto://` registered | 2.1 |
| P7 | Theme & personalisation | Note colours, typography, theme files over the existing tokens | 2.1 |
| P8 | Automation | `noto://` verbs and the `noto` CLI over the existing pipe; no HTTP | 2.1 |
| P9 | Premium UX / accessibility | Rebinding in-app shortcuts; per-surface UI Automation, Narrator, focus, High Contrast and reduced-motion conformance; first-run and empty states | NFR |
| P10 | Reliability | Adversarial storage, environment and scale suites; no silent data loss | NFR |
| P11 | Release | Packaging, signing, updates, backup before upgrade, SBOM, licences, privacy, reproducibility | NFR |

### 1.0 Identifier reconciliation (recorded 2026-10-21)

The P1 work is executed in the owner's priority order (decision of 2026-10-20: data semantics first), which differs from this table's numbering. One item has an implementation so far:

| Work | Roadmap identifier (this table) | Design-gate identifier | Implementation identifier | Canonical from now on |
|---|---|---|---|---|
| Create and rename (folders, notes, placement) | P1.4 | P1.2 (`create-and-rename.md`) | branch `p1/2-create-rename-tmp`; its PR will be titled P1.2 | **P1.2** |
| Recycle bin, purge, restore into a folder | P1.1 (semantics) + P1.3 (UI) | P1.1 (deletion-semantics §13) | P1.1 | **P1.1** (engine); the UI keeps its table row |

Every other row keeps this table's identifier until it is started. It is then reconciled here in the same way, with one row and one canonical name, rather than renumbering the table.

### 1.1 Master table: one line per target PR (`*` = new row, section 2.3)

| Phase | Capability | Existing parity rows | Current status | Dependencies | Target PR(s) |
|---|---|---|---|---|---|
| P0 | #107 hovered-off-switch fix; High Contrast runtime check (decision 11) | - | engineering (no parity row) | - | P0.1 |
| P0 | Local integration base: Slice 1 + M0 (#98, #104 incl. #99/#100, #101, #102, #103, #105) + fix/folder-entry-focus, as merge commits | - | engineering (no parity row) | P0.1 | P0.2 |
| P1 | Design gate: card-stack contract, editor-host seam, recycle-bin semantics, shortcut map, P1 a11y contract | - | engineering (no parity row) | P0.2 | P1.0 |
| P1 | Recycle-bin semantics: purge, empty, restore edge cases (engine only) | - | engineering (no parity row) | P1.0 | P1.1 |
| P1 | Card-stack surface: cards, editor-host seam, bottom bars, count (behaviour-preserving) | B17 B18 B25 C19 | 4 missing | P1.0 | P1.2 |
| P1 | Delete / restore UX: recycle bin surface, undo notice, folder-delete confirmation | B7 B23 C11 G15 | 4 partial | P1.1, P1.2 | P1.3 |
| P1 | Creation and rename: Ctrl+N in the stack, placement, title rule, empty-folder guidance | B1 G14 C2 B5 J8 B16 C15 | 5 missing, 2 partial | P1.2 | P1.4 |
| P1 | Move note to folder, recent folders, inline create | B11 G16 C5 | 2 partial, 1 missing | P1.2 | P1.5 |
| P1 | Pin / unpin notes and folders (no default pin chord) | B14 C8 G26 | 2 partial, 1 missing | P1.2 | P1.6 |
| P1 | Duplicate folder (and note, if Q4 confirms) | C12 B22 | 2 missing | P1.2 | P1.7 |
| P1 | Reorder by keyboard, top / bottom, folder sort | B8 B9 G17 G18 C10 | 4 missing, 1 partial | P1.6 | P1.8 |
| P1 | Drag & drop reorder on the OLE drag/drop primitive (#31 spike first) | B10 C9 | 2 partial | P1.8 | P1.9 |
| P1 | Fold / unfold, fold all | B12 B13 G19 G20 G21 G22 | 4 missing, 2 partial | P1.2 | P1.10 |
| P1 | Selection and navigation persistence across restart, show all folders | C14 J10 C7 | 2 missing, 1 partial | P1.2 | P1.11 |
| P1 | Context menus and confirmations, consolidated | - | engineering (no parity row) | P1.3-P1.11 | P1.12 |
| P1 | Keyboard workflow: switch notes, last folder, full chord map | C6 G10 B20 G11 G12 | 5 missing | P1.12 | P1.13 |
| RC | Re-score shipped rows after the frozen release-candidate campaign (decision 4) | A2 A7 A13 A20 C3 C4 C13 F1 G1 G9 G51 I13 J3 J4 J5 J7 J21 J26 J36 | 17 implemented, unscored, 1 partial, 1 implemented | RC campaign | RC.1 |
| P2 | Design gate: ADR editor model + command contract; RichEditBox hidden-markdown spike (perf, IME, UIA, undo) | - | engineering (no parity row) | P1.0 | P2.0 |
| P2 | Continuous persistence (debounced, crash-safe) | B19 | 1 deferred | P2.0 | P2.1 |
| P2 | Markdown block/inline model in Core (pure parser, spans) | - | engineering (no parity row) | P2.0 | P2.2 |
| P2 | Formatting commands as pure transforms + chords | D1 D2 D3 D4 D5 D6 D7 D8 D9 D10 D11 D12 D13 D14 D16 G27 G28 G29 G30 G31 G32 G33 | 22 missing | P2.2 | P2.3 |
| P2 | Structural editing: indent, move line, soft break, clear formatting, tasks | D19 D20 D21 D22 D23 D52* G34 G36 G37 G38 G39 | 11 missing | P2.2 | P2.4 |
| P2 | Live-styled rendering, undo/redo grouping, inline links | D17 D55* | 2 missing | P2.3 | P2.5 |
| P2 | Hidden markup + toggle | D24 G40 | 2 missing | P2.5 | P2.6 |
| P2 | Paste normalisation, code detection, clipboard copy | D39 D41 J19 J20 | 4 missing | P2.5 | P2.7 |
| P2 | Note modes (markdown / plain / code), spell check | D27 D28 J11 D45 | 4 missing | P2.5 | P2.8 |
| P2 | Quick formatting toolbar, Aa menu, empty-note placeholder | D25 D26 J6 B26 | 4 missing | P2.6 | P2.9 |
| P3 | Design gate: content types + attachment persistence semantics (ADR) | - | engineering (no parity row) | P2.0 | P3.0 |
| P3 | Attachment store (migration 3), orphan policy | - | engineering (no parity row) | P3.0 | P3.1 |
| P3 | Images: insert, paste, drop, screenshot paste | D29 D30 G35 | 3 missing | P3.1, P2.5 | P3.2 |
| P3 | Image actions: preview (Space), copy, save, delete | D32 D33 D35 G44 | 4 missing | P3.2 | P3.3 |
| P3 | File & folder shortcuts | D34 J16 | 2 missing | P3.1 | P3.4 |
| P3 | Links: open (scheme-validated), paste URL on selection, link titles (off by default) | D37 D53* D42 | 3 missing | P2.5 | P3.5 |
| P3 | #rrggbb colour swatches + settings | D15 F14 J17 J18 | 4 missing | P2.5 | P3.6 |
| P3 | Formatted paste (HTML/RTF -> markdown), quick copy | D40 D54* | 2 missing | P2.7 | P3.7 |
| P4 | Design gate: FTS5 index, tokenizer, escaping, ranking, snippets (ADR-003 follow-up) | - | engineering (no parity row) | P2.2 | P4.0 |
| P4 | FTS5 index (migration 4) + query use case + 10k-note budget test | - | engineering (no parity row) | P4.0 | P4.1 |
| P4 | Search surface: incremental, highlight, keyboard, open, empty | E1 G45 E8 | 3 missing | P4.1 | P4.2 |
| P4 | Result actions: quick copy, URL, containing folder, scope | E3 G46 G47 D36 E4 G48 E5 G49 E9* | 9 missing | P4.2 | P4.3 |
| P5 | Design gate: backup format, restore flow, crash-safe writes (ADR) | - | engineering (no parity row) | P3.1 | P5.0 |
| P5 | Integrity check, corruption / missing-folder / locked-file paths | - | engineering (no parity row) | P5.0 | P5.1 |
| P5 | Automatic backups, rotation, interval, backup folder | I8 J37 J38 I11 J39 | 5 missing | P5.1 | P5.2 |
| P5 | Manual backup, browse, restore | I9 I10 J40 | 3 missing | P5.2 | P5.3 |
| P5 | Markdown export (bulk), documented schema | I7 I15 | 2 missing | P2.2, P3.1 | P5.4 |
| P5 | Import text/markdown/images/shortcuts; drop-to-create | I1 I2 I3 J29 J30 B4 H14 | 7 missing | P3.2, P3.4 | P5.5 |
| P5 | Clipboard capture -> note; clipboard behaviours scored | B3 D43 D38 H15 | 4 missing | P2.7, P3.7 | P5.6 |
| P5 | Export note to image + settings | I4 J31 J32 J33 J34 | 5 missing | P2.5 | P5.7 |
| P5 | Print | B21 I6 G25 | 3 missing | P5.7 | P5.8 |
| P5 | Share ADAPT: copy / save as / open with | H2 G43 | 2 missing | P5.4 | P5.9 |
| P6 | Global action registry: six rebindable global chords | G50 J25 B2 G2 B6 J9 E2 G3 G4 G5 G6 | 11 missing | P1.10, P4.2, P5.6 | P6.1 |
| P6 | Open Bar edge handle | A3 J2 | 2 missing | P0.2 | P6.2 |
| P6 | Hot Side hover activation (delay, corner blocking) | A4 | 1 missing | P6.2 | P6.3 |
| P6 | Drag-to-reveal at the edge | A15 J27 J28 | 3 missing | P6.2, P5.5 | P6.4 |
| P6 | Activation surfaces: tray toggles, independent switches, Ctrl+Enter, topmost over full screen | A5 H1 J1 A11 G13 A8 | 3 partial, 3 missing | P6.2 | P6.5 |
| P6 | Multi-monitor + live DPI / display change (#30) | A18 | 1 missing | P0.2 | P6.6 |
| P6 | Virtual desktop behaviour defined + tested | A19 | 1 missing | P0.2 | P6.7 |
| P6 | noto:// registration + CLI routing through the existing activation pipe | - | engineering (no parity row) | P0.2 | P6.8 |
| P6 | Touchpad edge gesture: evaluate, likely re-score DROP | A21 | 1 missing | P6.2 | P6.9 |
| P7 | Note colours (6 + none, both styles, default colour) | B15 F10 J23 J24 G23 G24 | 5 missing, 1 partial | P1.11 | P7.1 |
| P7 | Typography: family, size, line height, spacing, fallback | D47 D48 D49 D50 D51 J12 J13 J14 J15 G41 G42 | 11 missing | P2.5 | P7.2 |
| P7 | Theme model over tokens + built-in themes, transparency | F2 F4 F5 F6 F11 | 5 missing | P7.1 | P7.3 |
| P7 | Theme files: install, manage, import / export | F3 F7 J22 | 3 missing | P7.3 | P7.4 |
| P8 | Design gate: automation contract (verbs, output, exit codes, URI trust) | - | engineering (no parity row) | P6.8 | P8.0 |
| P8 | noto:// verbs: open note/folder, add note, copy links | H5 C16 | 2 missing | P8.0 | P8.1 |
| P8 | CLI verbs: create, open, search, get, export, move, pin, fold, theme | H3 H4 E6 E7 I5 F9 | 6 missing | P8.0, P4.1, P5.4 | P8.2 |
| P9 | In-app shortcut rebinding with conflict detection | G52 | 1 missing | P6.1 | P9.1 |
| P11 | Privacy + local-first data statement (no sync in v1), reproducible artifacts | I12 | 1 missing | P10 | P11.5 |

---

## 2. Parity-to-phase mapping

### 2.1 Counts (264 parity rows + 5 new = 269)

| Phase | Rows | MUST | SHOULD | ADAPT | BETTER | DROP |
|---|--:|--:|--:|--:|--:|--:|
| done | 11 | 10 | 0 | 1 | 0 | 0 |
| RC | 19 | 17 | 1 | 1 | 0 | 0 |
| P0 | 0 | 0 | 0 | 0 | 0 | 0 |
| P1 | 44 | 31 | 11 | 0 | 2 | 0 |
| P2 | 50 | 35 | 14 | 1 | 0 | 0 |
| P3 | 18 | 6 | 10 | 2 | 0 | 0 |
| P4 | 12 | 7 | 5 | 0 | 0 | 0 |
| P5 | 31 | 15 | 8 | 6 | 2 | 0 |
| P6 | 26 | 15 | 6 | 4 | 1 | 0 |
| P7 | 25 | 12 | 13 | 0 | 0 | 0 |
| P8 | 8 | 1 | 0 | 7 | 0 | 0 |
| P9 | 1 | 0 | 1 | 0 | 0 | 0 |
| P11 | 1 | 0 | 0 | 1 | 0 | 0 |
| out | 23 | 0 | 0 | 0 | 0 | 23 |
| **total** | **269** | | | | | |

- `done`: already `[x]`, so no work.
- `out`: DROP rows, never built.
- `RC`: rows that are shipped but unscored. They are re-scored only after the frozen release-candidate campaign passes (decision 4), each against its own requirement cell.
- Pair rules are kept:
  - B7 and B23 are scored together.
  - H15 is scored last, in P5.6.
  - The J rows are the settings half of their feature row and ship in the same PR.

### 2.2 Open questions closed by the SideNotes 1.6.x research (resolved in P0.3)

| Q | Row | Finding | Effect |
|---|---|---|---|
| Q5 | B23 | SideNotes has **no trash**. Recovery is only from backups, "not guaranteed". | B23 stays BETTER |
| Q6 | C3 | No nested folders | C3 flat confirmed; ⚠ removed |
| Q8 | D7 | Underline is `==text==` (1.6); highlight is `::text::` | D7 syntax fixed; ⚠ removed |
| Q9 | D18 | No tables | D18 DROP confirmed |
| Q14 | I7 | No Markdown/.txt file export (only Copy Markdown, image export, print) | I7 stays BETTER |
| Q4 | B22 | Folder duplication verified (1.6.3); **note** duplication not found | B22 stays SHOULD ⚠; decide in P1.0 |

Still open: Q1 (A18), Q2 (A19), Q3, Q7, Q10–Q13, Q15–Q17. Noto defines its own behaviour where SideNotes documents none: A18 and A19 in P6.6 and P6.7.

### 2.3 New rows (each backed by SideNotes 1.6.x evidence; no duplicates)

| New | Capability | Level | Evidence | Why not an existing row |
|---|---|---|---|---|
| D52 | Soft line break (Shift+Enter) | MUST | RN 1.6.4 | No row covers line versus paragraph breaks; markdown semantics differ |
| D53 | Paste a URL over a selection to make a link | SHOULD | RN 1.6.4 / 1.6.5 | D42 covers title fetching, not linking a selection |
| D54 | Quick Copy of an element (Ctrl+Click a code, link, quote, header or list item) | SHOULD | Blog 1.6, RN 1.6.3 | E3 is search-window copy; D36 is an emergent pattern |
| D55 | Undo / redo, grouped by command | MUST | D22 already requires "deleted text is undoable" | Every editor command needs a defined undo unit; currently implicit |
| E9 | Search scope: all folders or the current folder | SHOULD | RN 1.4.16 ("search shows all folders by default") | E8 note says filters are "not pulled in". Scope is not a filter, but this needs an E8 note amendment |

Not added (evidence exists, deliberately rejected):
- Window appear-speed setting: Noto follows Windows animation effects.
- Scrollbar toggle.
- "Don't activate app" option.
- Control Center controls.
- Siri.
- Apple Notes task import.
- Writing Tools.
- Showcase folder: replaced by the optional Welcome note (decision 6).
- Localisation: see section 7.

### 2.4 Non-parity engineering register (NFR: P5 / P9 / P10 / P11)

These are not SideNotes features. They are not parity rows. Their source is `quality-gates.md` (non-negotiable coverage and budgets) and the principles.

| NFR | Requirement | Phase / PR |
|---|---|---|
| N1 | Crash-safe writes: WAL plus a single transaction per command; no torn note | P2.1, P5.0, P10.1 |
| N2 | Integrity check at start; read-only open plus restore offer on corruption | P5.1 |
| N3 | Migration safety: safety copy (exists), rollback, failure surfaced, never half-migrated | P5.1, P10.2 |
| N4 | Missing data folder, locked files, invalid settings (settings: exists) | P5.1, P10.6 |
| N5 | Performance budgets: cold start under 1 s, open under 100 ms, note switch under 50 ms, search under 100 ms at 10k notes, a 100 KB note under 200 ms (measured at 2,360 ms) | P2.0 spike, P4.1, P10.3 |
| N6 | Global unhandled-exception boundary; open note saved once (#100) | P0.2 |
| N7 | Structured log that never records content or paths (#99) | P0.2 |
| N8 | UI Automation names and roles, keyboard reachability, focus correctness, High Contrast, reduced motion on every surface | per PR plus P9.3–P9.5 |
| N9 | Mixed-DPI multi-monitor (#30), display change, sleep/resume, Explorer restart, virtual desktops | P6.6, P6.7, P10.5 |
| N10 | AltGr / non-US layout (parity §12a) | P1.0 decision, P9.7 |
| N11 | Packaging, signing, updates, SBOM, licences, privacy, reproducibility | P11 |

### 2.5 Every row

| ID | Capability | Level | Doc | Status now | Phase | PR | Alias / scored with |
|---|---|---|---|---|---|---|---|
| A1 | Screen edge window | MUST | [x] | done | done | - |  |
| A2 | Side changing | MUST | [ ] | implemented, unscored | RC | RC.1 | J3,G5 |
| A3 | Open Bar | MUST | [ ] | missing | P6 | P6.2 | J2 |
| A4 | Hot Side mode | MUST | [ ] | missing | P6 | P6.3 | J1 |
| A5 | Menubar Icon mode | ADAPT | [~] | partial | P6 | P6.5 | H1,J1 |
| A6 | Icon context menu | ADAPT | [x] | done | done | - | H1 |
| A7 | Global show/hide shortcut | MUST | [ ] | implemented, unscored | RC | RC.1 | G1 |
| A8 | Always on top | MUST | [~] | partial | P6 | P6.5 |  |
| A9 | Pin window open | MUST | [x] | done | done | - | G6 |
| A10 | Close window | MUST | [x] | done | done | - | G7 |
| A11 | Hide with modifier+Enter | SHOULD | [ ] | missing | P6 | P6.5 | G13 |
| A12 | Escape behavior | MUST | [x] | done | done | - | G8,G51,J26 |
| A13 | Close on click outside | MUST | [~] | implemented, unscored | RC | RC.1 | J5 |
| A14 | Width resize | MUST | [x] | done | done | - |  |
| A15 | Drag-to-reveal at edge | MUST | [ ] | missing | P6 | P6.4 | J27,J28 |
| A16 | Launch on startup | MUST | [x] | done | done | - | J4 |
| A17 | Second launch focuses | MUST | [x] | done | done | - |  |
| A18 | Multi-display behavior | MUST | [ ] | missing | P6 | P6.6 |  |
| A19 | Virtual desktop behavior | MUST | [ ] | missing | P6 | P6.7 |  |
| A20 | Slide animation | SHOULD | [ ] | partial (Slice 1.3; budget unmeasured) | RC | RC.1 |  |
| A21 | Two-finger edge gesture | ADAPT | [ ] | missing (Windows replacement) | P6 | P6.9 |  |
| B1 | Create note (in app) | MUST | [~] | partial | P1 | P1.4 | G14 |
| B2 | Create note (global) | MUST | [ ] | missing | P6 | P6.1 | G2 |
| B3 | Note from clipboard | MUST | [ ] | missing | P5 | P5.6 | D43,G4,H15 |
| B4 | Create by drag & drop | MUST | [ ] | missing | P5 | P5.5 |  |
| B5 | New note placement | MUST | [ ] | missing | P1 | P1.4 | J8 |
| B6 | Ask for folder on global create | SHOULD | [ ] | missing | P6 | P6.1 | J9 |
| B7 | Delete note | BETTER | [ ] | partial: engine only, no UI | P1 | P1.3 | B23(pair),G15 |
| B8 | Move note up / down | MUST | [ ] | partial: engine only, no UI | P1 | P1.8 | G17,G18 |
| B9 | Move note to top / bottom | SHOULD | [ ] | missing | P1 | P1.8 |  |
| B10 | Drag & drop reordering | MUST | [ ] | partial: engine only, no UI | P1 | P1.9 |  |
| B11 | Move note to folder | MUST | [ ] | partial: engine only, no UI | P1 | P1.5 | G16 |
| B12 | Fold / unfold note | MUST | [ ] | partial: engine only, no UI | P1 | P1.10 | G19,G20 |
| B13 | Fold all / unfold all | MUST | [ ] | partial: engine only, no UI | P1 | P1.10 | G21,G22 |
| B14 | Pin notes | MUST | [ ] | partial: engine only, no UI | P1 | P1.6 | G26 |
| B15 | Note colors | MUST | [ ] | partial: engine only, no UI | P7 | P7.1 | G23,G24,J24,F10 |
| B16 | First line is the title | MUST | [~] | partial | P1 | P1.4 |  |
| B17 | Note bottom bar | SHOULD | [ ] | missing | P1 | P1.2 |  |
| B18 | List bottom bar | SHOULD | [ ] | missing | P1 | P1.2 |  |
| B19 | Auto-save | MUST | [ ] | deferred (recorded exception) | P2 | P2.1 |  |
| B20 | Switch between notes | MUST | [ ] | missing | P1 | P1.13 | G11,G12 |
| B21 | Print a note | SHOULD | [ ] | missing | P5 | P5.8 | I6,G25 |
| B22 | Duplicate note | SHOULD | [ ] | missing | P1 | P1.7 |  |
| B23 | Trash / restore | BETTER | [ ] | partial: engine only, no UI | P1 | P1.3 | B7(pair),C11 |
| B24 | Note templates | DROP | [ ] | out of scope (DROP) | out | - |  |
| B25 | Per-note width | MUST | [ ] | missing | P1 | P1.2 |  |
| B26 | Empty note placeholder | SHOULD | [ ] | missing | P2 | P2.9 |  |
| C1 | Folders | MUST | [x] | done | done | - |  |
| C2 | Create folder | MUST | [ ] | missing | P1 | P1.4 | G14 |
| C3 | Nesting depth | MUST | [ ] | implemented (flat), unscored | RC | RC.1 |  |
| C4 | Enter folder | MUST | [~] | implemented, unscored | RC | RC.1 | G9,J7 |
| C5 | Leave folder / back | MUST | [~] | partial | P1 | P1.5 |  |
| C6 | Switch to last folder | MUST | [ ] | missing | P1 | P1.13 | G10 |
| C7 | Show all folders | MUST | [~] | partial | P1 | P1.11 |  |
| C8 | Pin folders | MUST | [ ] | partial: engine only, no UI | P1 | P1.6 |  |
| C9 | Folder reordering | MUST | [ ] | partial: engine only, no UI | P1 | P1.9 |  |
| C10 | Folder sorting | SHOULD | [ ] | missing | P1 | P1.8 |  |
| C11 | Delete folder | MUST | [ ] | partial: engine only, no UI | P1 | P1.3 | B23 |
| C12 | Duplicate folder | SHOULD | [ ] | missing | P1 | P1.7 |  |
| C13 | Rename folder | MUST | [ ] | implemented, unscored | RC | RC.1 |  |
| C14 | Open last folder on restart | SHOULD | [ ] | missing | P1 | P1.11 | J10 |
| C15 | Empty folder guidance | SHOULD | [ ] | missing | P1 | P1.4 |  |
| C16 | Folder URL | ADAPT | [ ] | missing (Windows replacement) | P8 | P8.1 | H5 |
| C17 | Folder colors | DROP | [ ] | out of scope (DROP) | out | - |  |
| C18 | All-notes / favorites / archive | DROP | [ ] | out of scope (DROP) | out | - |  |
| C19 | Note count | SHOULD | [ ] | missing | P1 | P1.2 |  |
| D1 | Headers | MUST | [ ] | missing | P2 | P2.3 | G29 |
| D2 | Bold | MUST | [ ] | missing | P2 | P2.3 | G27 |
| D3 | Italic | MUST | [ ] | missing | P2 | P2.3 | G28 |
| D4 | Bold italic | MUST | [ ] | missing | P2 | P2.3 |  |
| D5 | Strikethrough | MUST | [ ] | missing | P2 | P2.3 |  |
| D6 | Mark / highlight | SHOULD | [ ] | missing | P2 | P2.3 |  |
| D7 | Underline | SHOULD | [ ] | missing | P2 | P2.3 |  |
| D8 | Quote | MUST | [ ] | missing | P2 | P2.3 |  |
| D9 | Bullet list | MUST | [ ] | missing | P2 | P2.3 | G30 |
| D10 | Ordered list | MUST | [ ] | missing | P2 | P2.3 | G31 |
| D11 | Task (unchecked) | MUST | [ ] | missing | P2 | P2.3 | G32 |
| D12 | Task (checked) | MUST | [ ] | missing | P2 | P2.3 | G33 |
| D13 | Inline code | MUST | [ ] | missing | P2 | P2.3 |  |
| D14 | Code block | MUST | [ ] | missing | P2 | P2.3 |  |
| D15 | Color swatch preview | SHOULD | [ ] | missing | P3 | P3.6 | F14,J17 |
| D16 | Horizontal separator | MUST | [ ] | missing | P2 | P2.3 |  |
| D17 | Inline links | MUST | [ ] | missing | P2 | P2.5 |  |
| D18 | Tables | DROP | [ ] | out of scope (DROP) | out | - |  |
| D19 | Indent / outdent | MUST | [ ] | missing | P2 | P2.4 | G36,G37 |
| D20 | Move line up / down | MUST | [ ] | missing | P2 | P2.4 | G38,G39 |
| D21 | Clear formatting | SHOULD | [ ] | missing | P2 | P2.4 |  |
| D22 | Clear completed tasks | MUST | [ ] | missing | P2 | P2.4 | G34 |
| D23 | Completed tasks to bottom | SHOULD | [ ] | missing | P2 | P2.4 |  |
| D24 | Show / hide markup | MUST | [ ] | missing | P2 | P2.6 | G40 |
| D25 | Quick formatting toolbar | SHOULD | [ ] | missing | P2 | P2.9 | J6 |
| D26 | Formatting menu (Aa) | SHOULD | [ ] | missing | P2 | P2.9 |  |
| D27 | Code mode | MUST | [ ] | missing | P2 | P2.8 |  |
| D28 | Three note modes | SHOULD | [ ] | missing | P2 | P2.8 | J11 |
| D29 | Images | MUST | [ ] | missing | P3 | P3.2 | G35,I2 |
| D30 | Screenshots | MUST | [ ] | missing | P3 | P3.2 |  |
| D31 | Image from iPhone/iPad | DROP | [ ] | out of scope (DROP) | out | - |  |
| D32 | Copy image quickly | SHOULD | [ ] | missing | P3 | P3.3 | H15 |
| D33 | Save image to file | SHOULD | [ ] | missing | P3 | P3.3 |  |
| D34 | File & folder shortcuts | MUST | [ ] | missing | P3 | P3.4 | I3,J16 |
| D35 | Quick Look preview | ADAPT | [ ] | missing (Windows replacement) | P3 | P3.3 | G44 |
| D36 | Text snippets | MUST | [ ] | missing | P4 | P4.3 | E3 |
| D37 | URL handling | MUST | [ ] | missing | P3 | P3.5 | E4 |
| D38 | Save web address quickly | ADAPT | [ ] | missing (Windows replacement) | P5 | P5.6 |  |
| D39 | Code detection on paste | SHOULD | [ ] | missing | P2 | P2.7 | J20,H15 |
| D40 | Better pasting from ChatGPT | SHOULD | [ ] | missing | P3 | P3.7 |  |
| D41 | No auto-spaces on paste | SHOULD | [ ] | missing | P2 | P2.7 | J19,H15 |
| D42 | Smarter pasted links | SHOULD | [ ] | missing | P3 | P3.5 | H15 |
| D43 | Paste clipboard as note | MUST | [ ] | missing | P5 | P5.6 | B3 |
| D44 | Text replacement | DROP | [ ] | out of scope (DROP) | out | - |  |
| D45 | Spell check | ADAPT | [ ] | missing (Windows replacement) | P2 | P2.8 |  |
| D46 | Word count | DROP | [ ] | out of scope (DROP) | out | - |  |
| D47 | Font family | MUST | [ ] | missing | P7 | P7.2 | J12 |
| D48 | Font size | MUST | [ ] | missing | P7 | P7.2 | J13,G41,G42 |
| D49 | Line height | SHOULD | [ ] | missing | P7 | P7.2 | J14 |
| D50 | Paragraph spacing | SHOULD | [ ] | missing | P7 | P7.2 | J15 |
| D51 | Missing-font fallback | MUST | [ ] | missing | P7 | P7.2 |  |
| D52 | Soft line break (Shift+Enter) | MUST | new | missing (new row) | P2 | P2.4 |  |
| D53 | Paste URL over selection makes a link | SHOULD | new | missing (new row) | P3 | P3.5 |  |
| D54 | Quick Copy of an element (Ctrl+Click) | SHOULD | new | missing (new row) | P3 | P3.7 |  |
| D55 | Undo / redo, command-grouped | MUST | new | missing (new row) | P2 | P2.5 |  |
| E1 | Search window | MUST | [ ] | missing | P4 | P4.2 | G45 |
| E2 | Global search shortcut | MUST | [ ] | missing | P6 | P6.1 | G3 |
| E3 | Quick copy from results | MUST | [ ] | missing | P4 | P4.3 | G46,G47,H15,D36 |
| E4 | Launch URL from result | SHOULD | [ ] | missing | P4 | P4.3 | G48,D37 |
| E5 | Open containing folder | SHOULD | [ ] | missing | P4 | P4.3 | G49 |
| E6 | Search via automation | ADAPT | [ ] | missing (Windows replacement) | P8 | P8.2 | H3 |
| E7 | Search via Shortcuts | ADAPT | [ ] | missing (Windows replacement) | P8 | P8.2 | H4 |
| E8 | Live search / filters / in-note find | MUST | [ ] | missing | P4 | P4.2 |  |
| E9 | Search scope: all folders / current folder | SHOULD | new | missing (new row) | P4 | P4.3 |  |
| F1 | Light / dark / system | MUST | [ ] | implemented, unscored | RC | RC.1 | J21 |
| F2 | UI themes | SHOULD | [ ] | missing | P7 | P7.3 | J22 |
| F3 | Custom theme files | SHOULD | [ ] | missing | P7 | P7.4 |  |
| F4 | Theme structure | SHOULD | [ ] | missing | P7 | P7.3 |  |
| F5 | Theme color properties | SHOULD | [ ] | missing | P7 | P7.3 |  |
| F6 | Theme color formats | SHOULD | [ ] | missing | P7 | P7.3 |  |
| F7 | Theme installation | SHOULD | [ ] | missing | P7 | P7.4 | J22 |
| F8 | Theme gallery | DROP | [ ] | out of scope (DROP) | out | - |  |
| F9 | Theme via automation | ADAPT | [ ] | missing (Windows replacement) | P8 | P8.2 | H3/H4 |
| F10 | Note color display style | MUST | [ ] | missing | P7 | P7.1 | J23 |
| F11 | Transparency | SHOULD | [ ] | missing | P7 | P7.3 |  |
| F12 | Density | DROP | [ ] | out of scope (DROP) | out | - |  |
| F13 | Icon refresh | DROP | [ ] | out of scope (DROP) | out | - |  |
| F14 | Disable color swatches | SHOULD | [ ] | missing | P3 | P3.6 | J18,D15 |
| G1 | Show / hide app | MUST | [ ] | implemented, unscored | RC | RC.1 | A7 |
| G2 | New note in current folder | MUST | [ ] | missing | P6 | P6.1 | B2 |
| G3 | Search | MUST | [ ] | missing | P6 | P6.1 | E2 |
| G4 | Note from clipboard | MUST | [ ] | missing | P6 | P6.1 | B3 |
| G5 | Change side | SHOULD | [ ] | missing | P6 | P6.1 | A2 |
| G6 | Pin window | SHOULD | [ ] | missing | P6 | P6.1 | A9 |
| G7 | Close panel | MUST | [x] | done | done | - | A10 |
| G8 | Back / hide (4 modes) | MUST | [x] | done | done | - | A12 |
| G9 | Enter folder | MUST | [ ] | implemented, unscored | RC | RC.1 | C4 |
| G10 | Last-open folder | MUST | [ ] | missing | P1 | P1.13 | C6 |
| G11 | Next note | MUST | [ ] | missing | P1 | P1.13 | B20 |
| G12 | Previous note | MUST | [ ] | missing | P1 | P1.13 | B20 |
| G13 | Hide (optional binding) | SHOULD | [ ] | missing | P6 | P6.5 | A11 |
| G14 | New note / new folder | MUST | [ ] | missing | P1 | P1.4 | B1,C2 |
| G15 | Delete note | MUST | [ ] | partial: engine only, no UI | P1 | P1.3 | B7 |
| G16 | Move note to folder | MUST | [ ] | missing | P1 | P1.5 | B11 |
| G17 | Move note up | MUST | [ ] | missing | P1 | P1.8 | B8 |
| G18 | Move note down | MUST | [ ] | missing | P1 | P1.8 | B8 |
| G19 | Fold note | MUST | [ ] | missing | P1 | P1.10 | B12 |
| G20 | Unfold note | MUST | [ ] | missing | P1 | P1.10 | B12 |
| G21 | Fold all | MUST | [ ] | missing | P1 | P1.10 | B13 |
| G22 | Unfold all | MUST | [ ] | missing | P1 | P1.10 | B13 |
| G23 | Set note color 1–6 | MUST | [ ] | missing | P7 | P7.1 | B15 |
| G24 | Clear note color | MUST | [ ] | missing | P7 | P7.1 | B15 |
| G25 | Print note | SHOULD | [ ] | missing | P5 | P5.8 | B21 |
| G26 | Pin note | SHOULD | [ ] | missing | P1 | P1.6 | B14 |
| G27 | Bold | MUST | [ ] | missing | P2 | P2.3 | D2 |
| G28 | Italic | MUST | [ ] | missing | P2 | P2.3 | D3 |
| G29 | Header | MUST | [ ] | missing | P2 | P2.3 | D1 |
| G30 | Bullet list | MUST | [ ] | missing | P2 | P2.3 | D9 |
| G31 | Ordered list | MUST | [ ] | missing | P2 | P2.3 | D10 |
| G32 | Create / remove task | MUST | [ ] | missing | P2 | P2.3 | D11 |
| G33 | Toggle task state | MUST | [ ] | missing | P2 | P2.3 | D12 |
| G34 | Clear completed tasks | MUST | [ ] | missing | P2 | P2.4 | D22 |
| G35 | Insert picture | MUST | [ ] | missing | P3 | P3.2 | D29 |
| G36 | Indent right | MUST | [ ] | missing | P2 | P2.4 | D19 |
| G37 | Indent left | MUST | [ ] | missing | P2 | P2.4 | D19 |
| G38 | Move line up | MUST | [ ] | missing | P2 | P2.4 | D20 |
| G39 | Move line down | MUST | [ ] | missing | P2 | P2.4 | D20 |
| G40 | Show / hide markup | MUST | [ ] | missing | P2 | P2.6 | D24 |
| G41 | Increase font size | MUST | [ ] | missing | P7 | P7.2 | D48 |
| G42 | Decrease font size | MUST | [ ] | missing | P7 | P7.2 | D48 |
| G43 | Share menu | ADAPT | [ ] | missing (Windows replacement) | P5 | P5.9 |  |
| G44 | Preview image / file | ADAPT | [ ] | missing (Windows replacement) | P3 | P3.3 | D35 |
| G45 | Open search | MUST | [ ] | missing | P4 | P4.2 | E1 |
| G46 | Copy note (incl. line 1) | MUST | [ ] | missing | P4 | P4.3 | E3 |
| G47 | Copy note (excl. line 1) | MUST | [ ] | missing | P4 | P4.3 | E3 |
| G48 | Launch URL from line 2 | SHOULD | [ ] | missing | P4 | P4.3 | E4 |
| G49 | Open containing folder | SHOULD | [ ] | missing | P4 | P4.3 | E5 |
| G50 | Rebind global shortcuts | MUST | [ ] | missing | P6 | P6.1 | J25 |
| G51 | Escape behavior setting | MUST | [~] | implemented, unscored | RC | RC.1 | A12,J26 |
| G52 | Rebind in-app shortcuts | SHOULD | [ ] | missing | P9 | P9.1 |  |
| H1 | Menu bar item | ADAPT | [~] | partial | P6 | P6.5 | A5,A6 |
| H2 | Share extension | ADAPT | [ ] | missing (Windows replacement) | P5 | P5.9 | G43 |
| H3 | AppleScript API | ADAPT | [ ] | missing (Windows replacement) | P8 | P8.2 |  |
| H4 | Apple Shortcuts actions | ADAPT | [ ] | missing (Windows replacement) | P8 | P8.2 |  |
| H5 | URL scheme | MUST | [ ] | missing | P8 | P8.1 | C16 |
| H6 | Alfred workflow | DROP | [ ] | out of scope (DROP) | out | - |  |
| H7 | Hookmark | DROP | [ ] | out of scope (DROP) | out | - |  |
| H8 | PopClip | DROP | [ ] | out of scope (DROP) | out | - |  |
| H9 | Dropzone | DROP | [ ] | out of scope (DROP) | out | - |  |
| H10 | Raycast | DROP | [ ] | out of scope (DROP) | out | - |  |
| H11 | Workspaces | DROP | [ ] | out of scope (DROP) | out | - |  |
| H12 | MindNode | DROP | [ ] | out of scope (DROP) | out | - |  |
| H13 | Things | DROP | [ ] | out of scope (DROP) | out | - |  |
| H14 | Drag from other apps | MUST | [ ] | missing | P5 | P5.5 |  |
| H15 | Clipboard behaviors | MUST | [ ] | missing | P5 | P5.6 | B3,E3,D32,D39,D42,D41 |
| H16 | Dropshare target | DROP | [ ] | out of scope (DROP) | out | - |  |
| H17 | Handoff / Continuity | DROP | [ ] | out of scope (DROP) | out | - |  |
| H18 | Services menu | DROP | [ ] | out of scope (DROP) | out | - |  |
| I1 | Import text files | MUST | [ ] | missing | P5 | P5.5 | J30 |
| I2 | Import images | MUST | [ ] | missing | P5 | P5.5 | J30 |
| I3 | Import as shortcuts | MUST | [ ] | missing | P5 | P5.5 | D34 |
| I4 | Export note to image | SHOULD | [ ] | missing | P5 | P5.7 | J31-J34 |
| I5 | Export via Shortcuts | ADAPT | [ ] | missing (Windows replacement) | P8 | P8.2 | H4 |
| I6 | Print | SHOULD | [ ] | missing | P5 | P5.8 | B21 |
| I7 | Text / markdown export | BETTER | [ ] | missing | P5 | P5.4 |  |
| I8 | Automatic backups | MUST | [ ] | missing | P5 | P5.2 | J37,J38 |
| I9 | Manual backup | MUST | [ ] | missing | P5 | P5.3 | J40 |
| I10 | Restore a backup | MUST | [ ] | missing | P5 | P5.3 | J40 |
| I11 | Backups in cloud drive | ADAPT | [ ] | missing (Windows replacement) | P5 | P5.2 | J39 |
| I12 | iCloud sync | ADAPT | [ ] | missing (Windows replacement) | P11 | P11.5 | J35 |
| I13 | Local storage location | ADAPT | [ ] | implemented, unscored | RC | RC.1 |  |
| I14 | Legacy storage format | DROP | [ ] | out of scope (DROP) | out | - |  |
| I15 | Data portability | BETTER | [ ] | missing | P5 | P5.4 |  |
| J1 | General → Show or Hide Notes | ADAPT | [ ] | missing (Windows replacement) | P6 | P6.5 |  |
| J2 | General → Hide Open Bar | BETTER | [ ] | missing | P6 | P6.2 |  |
| J3 | General → Side | MUST | [ ] | implemented, unscored | RC | RC.1 |  |
| J4 | General → Launch on Startup | MUST | [ ] | implemented, unscored | RC | RC.1 |  |
| J5 | General → Close on outside click | MUST | [ ] | implemented, unscored | RC | RC.1 |  |
| J6 | Notes → Quick Formatting Toolbar | SHOULD | [ ] | missing | P2 | P2.9 |  |
| J7 | Folders → Single-click opening | MUST | [ ] | implemented, unscored | RC | RC.1 |  |
| J8 | Creating Notes → Placement | MUST | [ ] | missing | P1 | P1.4 |  |
| J9 | Creating Notes → Ask for folder | SHOULD | [ ] | missing | P6 | P6.1 |  |
| J10 | Creating Notes → Open last folder on restart | SHOULD | [ ] | missing | P1 | P1.11 |  |
| J11 | Text → Default text formatting | SHOULD | [ ] | missing | P2 | P2.8 |  |
| J12 | Text → Font family | MUST | [ ] | missing | P7 | P7.2 |  |
| J13 | Text → Font size | MUST | [ ] | missing | P7 | P7.2 |  |
| J14 | Text → Line height | SHOULD | [ ] | missing | P7 | P7.2 |  |
| J15 | Text → Paragraph spacing | SHOULD | [ ] | missing | P7 | P7.2 |  |
| J16 | Text → File links | MUST | [ ] | missing | P3 | P3.4 |  |
| J17 | Text → Color copy format | SHOULD | [ ] | missing | P3 | P3.6 |  |
| J18 | Text → Disable color display | SHOULD | [ ] | missing | P3 | P3.6 |  |
| J19 | Text → No auto-spaces on paste | SHOULD | [ ] | missing | P2 | P2.7 |  |
| J20 | Text → Code detection on paste | SHOULD | [ ] | missing | P2 | P2.7 |  |
| J21 | Appearance → Light / Dark / System | MUST | [ ] | implemented, unscored | RC | RC.1 |  |
| J22 | Appearance → Themes → Manage | SHOULD | [ ] | missing | P7 | P7.4 |  |
| J23 | Appearance → Note colors | MUST | [ ] | missing | P7 | P7.1 |  |
| J24 | Appearance → Default new-note color | SHOULD | [ ] | missing | P7 | P7.1 |  |
| J25 | Shortcuts → Global shortcuts | MUST | [ ] | missing | P6 | P6.1 | G50 |
| J26 | Shortcuts → Escape behavior | MUST | [ ] | implemented, unscored | RC | RC.1 | A12,G51 |
| J27 | Importing → DnD on screen edge | MUST | [ ] | missing | P6 | P6.4 |  |
| J28 | Importing → Show while dragging | MUST | [ ] | missing | P6 | P6.4 |  |
| J29 | Importing → Move imported files to Trash | ADAPT | [ ] | missing (Windows replacement) | P5 | P5.5 |  |
| J30 | Importing → Import behavior | MUST | [ ] | missing | P5 | P5.5 |  |
| J31 | Exporting → Image background | SHOULD | [ ] | missing | P5 | P5.7 |  |
| J32 | Exporting → Image margins | SHOULD | [ ] | missing | P5 | P5.7 |  |
| J33 | Exporting → Image ratio | SHOULD | [ ] | missing | P5 | P5.7 |  |
| J34 | Exporting → Bottom bar in image | SHOULD | [ ] | missing | P5 | P5.7 |  |
| J35 | Data → iCloud sync | DROP | [ ] | out of scope (DROP) | out | - |  |
| J36 | Data → Data locations | MUST | [ ] | implemented, unscored | RC | RC.1 |  |
| J37 | Backups → Automatic backups | MUST | [ ] | missing | P5 | P5.2 |  |
| J38 | Backups → Frequency | MUST | [ ] | missing | P5 | P5.2 |  |
| J39 | Backups → Backup folder | ADAPT | [ ] | missing (Windows replacement) | P5 | P5.2 |  |
| J40 | Backups → Browse backups | MUST | [ ] | missing | P5 | P5.3 |  |

---

## 3. Dependency graph

```
                    +-------------------------------- P6.2-P6.8 window track (needs P0 only) --+
                    |                                                                        |
P0 --> P1.0 design --+--> P1.1 split MainWindow --> P1.2 events (#104) --> P1.3..P1.11        |
 |                  |                                                     |                  v
 |                  +--> P2.0 editor ADR+spike --> P2.1 persist           +--> P6.1 global chords
 |                                   |                                    |    (needs P1.10, P4.2, P5.6)
 |                                   +--> P2.2 model --> P2.3/P2.4 commands --> P2.5 render --> P2.6..P2.9
 |                                                 |                              |
 |                                                 |                              +--> P3.x rich content
 |                                                 |                              |      |
 |                                                 +--> P4.0 --> P4.1 FTS5 --> P4.2 --> P4.3
 |                                                                                |      |
 |                                                                    P3.1 --> P5.0 --> P5.1..P5.9
 |                                                                                         |
 +--> P0.2 lands #99 #100 #104 (log, error boundary, events) ---------------------------------+
                                                                                           v
P7 <-- P1.11 (colours), P2.5 (typography)      P8 <-- P6.8 + P4.1 + P5.4      P9 <-- every UI PR
P10 <-- P5 + P6      P11 <-- P10   (signing procurement #5 starts NOW: lead time)
```

### 3.1 Platform primitives: established before their first consumer

Each Windows primitive lands in `Platform.Windows` (or, for XAML-only APIs, `Noto.Windows`) in the PR **before** its first consumer, or as that PR's first commit. Where the API's behaviour is uncertain, a focused runtime check comes first, which the instruction allows.

| Primitive | Layer | Uncertain? (focused runtime check) | Established in | First consumer |
|---|---|---|---|---|
| Card-stack virtualised list with an inline editor host | Windows | Yes: focus, arrow keys, reorder with variable-height items | P1.2 | P1.3–P1.13, P2 |
| Shell: reveal in Explorer, open with default app | Platform (`ShellLaunch` exists) | No | exists | P3.4, P7.4 |
| OLE drag & drop: list reorder, then external drop (Explorer, browser) | Windows + path validation in Platform | **Yes**: #31 (WinUI 3 drop bugs; unpackaged and elevated) | P1.9 (internal reorder), P3.2 (external drop) | P1.9, P3.2, P5.5, P6.4 |
| Clipboard formats: text, HTML, RTF, bitmap, files | Windows (WinRT clipboard); pure conversion in Core | Partly (HTML fragment parsing) | P2.7 | P2.7, P3.7, P5.6 |
| Rich text host (`RichEditBox` / TOM) | Windows | **Yes**: P2.0 spike | P2.0 → P2.5 | P2, P3 |
| Recycle Bin for files (`IFileOperation`) | Platform | No | P5.5 | J29 |
| Printing (`PrintManager` via HWND interop) | Platform | **Yes** (unpackaged) | P5.8 spike | P5.8 |
| Bitmap render of a card (`RenderTargetBitmap`) | Windows | Small | P5.7 | P5.7, I5 |
| Global chord registry (N chords) | Platform (`GlobalHotkey` exists for 1) | No | P6.1 | P6.1, P4/P5 global actions |
| Edge window per monitor (Open Bar, Hot Side) | Platform | **Yes**: full-screen apps, corners, DPI | P6.2 | P6.2–P6.4 |
| Display and DPI change notifications | Platform | **Yes**: #30 mixed DPI | P6.6 | P6.6, P10.5 |
| `noto://` registration (HKCU\Software\Classes) + envelope v2 | Platform (pipe exists) | Small | P6.8 | P8 |
| "Send to → Noto" shortcut (decision 9) | Platform | No | P6.9 | P6.9 |

### 3.2 Reuse rules for P7 and P8

- **P7 reuses the Slice 1 tokens.**
  - A theme is a set of values for existing ADR-011 token keys, nothing else.
  - `DesignSystemGuardTests`' key-parity and contrast rules extend to theme files.
  - The High Contrast dictionary is never themeable: Windows' system colours win.
  - No second styling system.
- **P8 reuses UseCases.**
  - Every CLI and URI verb is an existing command or query, dispatched in the running owner through the same dispatcher (#104).
  - A guard asserts that the automation layer references no repository and holds no rule.
  - A verb with no use case gets one in UseCases first, then the verb.

The phases run P0→P11 in order, as instructed. The only parallel work this plan permits is:
- the P6 window track, which is independent of P1–P5 and can be pulled forward when a reviewer is free;
- code-signing procurement.

---

## 4. Proposed PR sequence

The sequence is the order of the master table (1.1). Each PR is one responsibility. It is stacked only where a dependency forces it; otherwise it is based on `main`.

**Every PR goes through the same steps:**

```
inspect --> design gate (ADR / contract when the PR is a phase's .0) --> implement
        --> unit + integration tests --> targeted mutation run (critical logic)
        --> Release + Debug 0/0 --> full suite --> architecture guards --> dotnet format
        --> git diff --check --> no new analyzer findings --> commit --> rebase dependants --> next
```

**Accessibility is cross-cutting (decision 10).** Every UI PR carries, as gates and guard tests:
- UI Automation name, role and state for each new element;
- keyboard reachability and visible focus;
- a High Contrast mapping to system colours, with no backplate;
- Windows animation effects honoured;
- a live-region announcement for every command result.

P9 is the final conformance pass, not where accessibility starts.

Never pushed to `main`; never auto-merged. A PR that adds a surface also adds or extends its `tools/validation` case and guard tests. Running those cases waits for the release-candidate campaign.

**P9–P11 PRs (NFR, no parity row)**

| PR | Scope |
|---|---|
| P9.2 | First-run, empty and error states, save-state feedback, tooltips: strong empty states plus the optional Welcome note, no tour (decision 6) |
| P9.3 | Per-surface UI Automation / Narrator audit and fixes; live regions for command feedback |
| P9.4 | Focus-correctness audit, including the folder-entry focus fix (merged before P1 per decision 5; P9.4 re-checks it) |
| P9.5 | High Contrast and reduced-motion conformance of every surface added in P1–P8 |
| P9.6 | Responsive width, visual hierarchy, drag/drop feedback pass |
| P9.7 | Non-US layout / AltGr verification (N10) |
| P10.1 | Storage fault injection: kill mid-write, torn WAL, corrupted pages, disk full |
| P10.2 | Migration failure and rollback; restore failure; backup of a corrupt store |
| P10.3 | Scale: 100 KB note, 10k notes, 1k folders, long titles; fix N5 regressions |
| P10.4 | Long-session soak: memory, handles, GDI/USER objects, hook and timer leaks |
| P10.5 | Environment: monitor and DPI change, sleep/resume, Explorer restart, virtual-desktop moves |
| P10.6 | Invalid settings, missing data folder, locked database or attachments, read-only media |
| P11.1 | Revisit ADR-008: unpackaged today versus sparse package plus Velopack; installer and uninstaller |
| P11.2 | Signing (#5) wired into `release.yml`; checksums |
| P11.3 | Update mechanism: opt-in network check; backup before upgrade; migration; rollback |
| P11.4 | SBOM, dependency and licence review, third-party notices |
| P11.5 | Privacy documentation; reproducible artifacts (Deterministic builds are already on) |

### 4.1 Where each phase's code goes (layer rules preserved; no new project except possibly P8)

| Concern | Core | UseCases | Infrastructure | Platform.Windows | UI | Windows |
|---|---|---|---|---|---|---|
| P1 note/folder mutations | rules (exist) | commands (exist) + recent-folders query | SQL (exists) + last-folder setting key | - | row and context-menu styles | presenters, drag/drop, menus |
| P2 editor | **markdown model + pure edit transforms** `(text, selection) → edit` | save policy | - | - | editor styles and tokens | RichEditBox host: applies edits and spans, no rules |
| P3 content | content-reference types, URL scheme allow-list | attach / detach | migration 3, attachment files | shell open / reveal | image chrome | drop and paste glue |
| P4 search | query and result model | search query | FTS5, escaping, migration 4 | - | result styles | search surface |
| P5 data | backup manifest model | backup / restore / export / import orchestration | backup API, file formats, integrity | print interop, Recycle Bin | - | dialogs |
| P6 windows | chord and activation model (exists) | - | - | **all HWND, DPI, monitor, hook, edge and URI-registration code** | handle styles | wiring only |
| P7 themes | theme-definition validation | apply / persist | theme file load | - | **tokens, theme dictionaries** | resource swap |
| P8 automation | verb model | verb handlers = the same commands | - | pipe envelope v2 | - | routing |

Markdown parsing is written by hand, for SideNotes' subset only:
- Core stays dependency-free.
- SideNotes' `::mark::` and `==underline==` are not CommonMark anyway.
- The parser is decided in P2.0. If the spike shows hand-written is too costly, the fallback is Markdig: it is MIT-licensed and reports source spans.

**P8:** `Noto.exe` is a WinExe, which cannot behave as a blocking console command. P8.0 evaluates the minimal fix, a console stub (`noto.com` beside `noto.exe`, or a tiny console project that references only the pipe client), before adding anything.

---

## 5. Risks and blockers

| # | Risk / blocker | Impact | Mitigation | Blocks |
|---|---|---|---|---|
| R1 | **Card stack decided (decision 1)**; the risk is now its implementation. Variable-height virtualised cards with one inline editor: focus, arrow keys, reorder | Rework of P2 if the host seam is wrong | P1.0 contract fixes the editor-host seam; P1.2 focused runtime check | P1 |
| R2 | **The editor is the largest single risk.** Hidden markdown over `RichEditBox` must keep the markdown as the truth (ADR-004), keep IME/TSF, Win+., UI Automation `TextPattern` and spell check working, and keep a sane undo while text is restyled from code. A 100 KB note already takes 2,360 ms against a 200 ms budget. | P2 slips, or an inaccessible editor | P2.0 spike with measured gates. Edits are pure Core transforms, so the host is replaceable. Never a custom-drawn text control (accessibility) | P2–P3 |
| R3 | **M0 backlog unmerged.** #104 (which contains #99 and #100) touches every handler and `MainWindow`; P1 written without it would be reworked. | Merge conflicts in the two god files | P0.2 builds the local integration base (Slice 1 → M0 → focus fix) as merge commits; P1 branches stack on it and rebase onto `main` once you merge on GitHub | P1 |
| R4 | ADR-010 event aggregator not built; `MainWindow` refreshes by hand | Views go stale once CLI, search or menus mutate | P1.2 on top of #104 | P1, P8 |
| R5 | **AltGr rule broken by the defaults.** 11 `Ctrl+Alt+…` chords (G10–G22, G15, G34), several due in P1. G26 and G35 collide on `Ctrl+Shift+P`. | Typing breaks on Polish, German and other layouts | Decide the chord map in P1.0 (decisions 9.2 and 9.3) | P1 |
| R6 | Drag & drop into WinUI 3 (#31 still open); unpackaged and elevation drop limits | B10, B4, H14, A15 | Early spike inside P1.6; P5.5 reuses it | P1.6, P5.5 |
| R7 | Hot Side / Open Bar without global mouse hooks | Edge windows, full-screen apps, corners, multi-monitor edges | A thin topmost edge window per monitor, no low-level hook; corner exclusion zones | P6.2–P6.4 |
| R8 | **Principle conflicts in the instruction**: onboarding (principles forbid tours and splash screens), density (F12 DROP), an in-app motion setting (not in the J table), D42 title fetch (network in a local-first app), update checks (network) | Scope creep, or principle violation | Decisions 9.6–9.8 | P7, P9, P11 |
| R9 | Docs drift: Slice 1 undocumented; progress.md stale; ADR-003 (Dapper), ADR-008 (packaging) and ADR-010 (aggregator) disagree with the code | Wrong decisions downstream | Parity and progress are updated at the RC boundary (decision 4); ADR drift is fixed in the PR that touches that ADR's area | RC |
| R10 | Code-signing cost and lead time (#5) | Release blocked late | Start procurement now, in parallel | P11 |
| R11 | **Deferring the runtime campaigns** lets desktop-only regressions (focus, hover, High Contrast, DPI) pile up; this session found three of them | Large release-candidate fix wave | Keep a guard per surface and update the harness case in the same PR; one cheap focused run per phase exit | RC |
| R12 | The CLI path while Noto is not running: a second SQLite writer would bypass single-instance ownership (ADR-013) | Write races | The CLI always goes through the running owner, which is started hidden if needed | P8 |
| R13 | `noto://` can be fired by any web page | Abuse vector | Read-only and create-only verbs; size limits; no paths; no destructive verbs over the URI | P8 |
| R14 | Printing from an unpackaged WinUI 3 app (`PrintManager` needs HWND interop) | I6 / B21 | Spike in P5.8; fallback is export then system print | P5.8 |
| R15 | Pending carry-overs: the #107 High Contrast runtime check; `fix/folder-entry-focus` (local only, no PR); #16 still open | P0 not formally closed | Decisions 5 and 11 | P0 |
| R16 | **The Slice 1 PR branches on GitHub are stale.** The rebased #107–#109 and the #106 High Contrast fix exist locally only | GitHub reviews old code | Push when you ask (never automatic) | merge |

---

## 6. Windows-native replacements (do not copy SideNotes' mechanism)

| SideNotes (Apple) | Noto (Windows) | Rows |
|---|---|---|
| Menu bar icon and menu | Notification-area (tray) icon and menu; selection = launch (ADR-014) | A5 A6 H1 J1 |
| Spaces / full-screen Spaces | Virtual Desktops (defined behaviour); topmost over full-screen windows | A19 A8 |
| Login items | HKCU `Run` + `--login` hidden start; Windows' state is the truth (ADR-015) | A16 J4 |
| Global shortcuts (Carbon) | `RegisterHotKey` on a message-only window; rebind without ever losing the chord | A7 G1–G6 G50 |
| Trackpad swipe / pinch | Keyboard, menu and double-click are primary; precision-touchpad gesture evaluated, likely DROP | A21 B12 |
| Finder reveal | Explorer `/select`; "Show in Explorer" | F7 J22 J36 D34 |
| macOS Trash | Recycle Bin (`IFileOperation`, `FOF_ALLOWUNDO`) for imported files | J29 |
| Quick Look | In-app image preview on Space; otherwise the Explorer preview / default app | D35 G44 |
| NSSpellChecker | Windows spell checking (`RichEditBox` uses the Windows spell API); off in code mode | D45 |
| Continuity Camera / screenshot tool | Paste or drop from Win+Shift+S / Snipping Tool | D30 (D31 DROP) |
| AppleScript / Shortcuts / URL scheme | `noto://` + `noto` CLI (+ a thin PowerShell module) over the existing single-instance pipe | H3 H4 H5 E6 E7 F9 I5 C16 |
| Share extension / Services / Share menu | Copy, Save As, Open With; "Send to → Noto" inbound (decision 9) | H2 G43 |
| iCloud Drive backups | A user-chosen backup folder (OneDrive or any path) | I11 J39 |
| iCloud sync | None in v1: local-first (ADR-002) | I12 J35 |
| macOS appearance / accent | `UISettings` light/dark, system accent, High Contrast system colours (already built) | F1 J21 |
| Reduce motion | Windows "Animation effects" (`SPI_GETCLIENTAREAANIMATION`), read per transition (already built) | A20 |
| `.sntheme` + ThemeEdit | JSON theme file + published schema, mapped onto ADR-011 tokens; edit in any editor | F3–F7 |
| AppKit drag & drop | OLE drag & drop through WinUI | B4 B10 H14 |
| macOS print / PDF | Windows print dialog ("Microsoft Print to PDF" covers PDF) | B21 I6 |
| Cmd chords | Ctrl chords; no default `Ctrl+Alt+<letter>` (AltGr) | G* |

---

## 7. Explicitly out of scope

- **All 23 DROP rows**: B24 C17 C18 D18 D31 D44 D46 F8 F12 F13 H6–H13 H16–H18 I14 J35.
- **Sync, accounts, cloud**: I12 and J35 (ADR-002). Bring-your-own-folder sync is post-v1.
- **Rejected or post-v1** (roadmap and competitor-enhancements): mobile, web, collaboration, plugins, AI/MCP, telemetry, an HTTP server.
- **Not in SideNotes, and the docs forbid building them speculatively**: nested folders, tables, code-block syntax highlighting, templates, word count, text replacement, URL rich previews, x-callback-url, hot corners.
- **M5–M8 work**:
  - contextual notes (M6);
  - competitor features (M7);
  - Windows Hello lock, capture exclusion, region screenshot capture, notifications and Jump Lists (M5).
  - "Send to → Noto" (decision 9) is the only Explorer integration in scope.
- **Apple-only items**: Control Center controls, Siri / App Intents, Touch Bar, Apple Notes import, Writing Tools, the Alfred, Raycast, PopClip, Dropzone, Hookmark, Things, MindNode and Dropshare integrations.
- **Localisation in v1.** Strings stay centralised, so it can follow later.
- **Per-note lock (CE13).** It has no parity row and three documents disagree on it. It stays out until a row exists.

---

## 8. Order inside each phase (and why)

- **P0.** Check the fix (High Contrast on), then merge order, then the docs re-score. The docs come last, so they describe what actually landed.
- **P1.** (Your priority order; the card-stack surface is inserted as P1.2 because every later P1 UI needs it.)
  1. P1.0 design gate.
  2. P1.1 recycle-bin semantics: engine only, the safest data rules first.
  3. P1.2 card-stack surface plus the editor-host seam: behaviour-preserving.
  4. P1.3 delete / restore UX.
  5. P1.4 creation and rename.
  6. P1.5 move and recent folders.
  7. P1.6 pin.
  8. P1.7 duplicate folder.
  9. P1.8 reorder.
  10. P1.9 drag & drop.
  11. P1.10 fold.
  12. P1.11 selection persistence.
  13. P1.12 context menus and confirmations.
  14. P1.13 keyboard workflow.
- **P2.**
  1. Design gate and spike.
  2. Continuous persistence: a MUST, needed by everything after it, and independent of rendering.
  3. Pure model, then commands on the *plain* editor. They are usable at once and fully unit-testable.
  4. Rendering, then hidden markup.
  5. Paste, modes, then the toolbar, which is the presentation of commands that already exist.
- **P3.** Content ADR, attachment store, images, image actions, shortcuts, links, colours, formatted paste. Storage first, so that every content type shares one lifecycle and one backup story.
- **P4.** ADR, index plus escaping plus a perf test, then the UI, then the result actions.
- **P5.**
  1. ADR.
  2. Integrity and failure paths, before writing any backup.
  3. Automatic backups, then manual backup and restore. Restore is verified, never assumed.
  4. Export, import, clipboard, image, print, share.
- **P6.**
  1. Global chord registry: one owner, six chords.
  2. Open Bar, then Hot Side and drag-to-reveal, which reuse the edge window.
  3. Activation toggles, multi-monitor and DPI, virtual desktops, URI registration, then the gesture evaluation.
- **P7.** Note colours (the domain exists), typography, the theme model, then theme files.
- **P8.** Contract ADR, then the URI verbs, then the CLI verbs, then the PowerShell wrapper.
- **P9.** Rebinding, then the audits in risk order: UI Automation, focus, High Contrast / motion, layout, AltGr.
- **P10.** Storage faults first (data loss), then migration and restore, scale, soak, environment, configuration.
- **P11.** Packaging decision, signing, updates and rollback, SBOM and licences, privacy and reproducibility.

---

## 9. Locked decisions (2026-10-20)

| # | Decision | Consequence in this plan |
|---|---|---|
| 1 | Workspace: SideNotes-style **card stack**, on the unchanged, normalised Folder → Note domain | P1.0 contract; P1.2 surface; the P2 editor plugs into the P1.2 host seam |
| 2 | No `Ctrl+Alt+<letter>` defaults; global defaults are `Ctrl+Shift+Win+<mnemonic>` where Windows does not reserve them | Section 10 |
| 3 | `Ctrl+Shift+P` = insert picture; **no** default pin chord | G26 unbound |
| 4 | Re-score Slice 1 only after the complete frozen release-candidate campaign passes | Phase `RC`; no parity or progress edits before then |
| 5 | Merge order: #106 → #107 → #108 → #109, then the M0 foundations, then `fix/folder-entry-focus` (if its evidence still holds), then P1 | P0.2 local integration base mirrors this; GitHub merges stay yours |
| 6 | Onboarding = strong empty states + an optional Welcome note; no guided tour | P1.4 (C15), P2.9 (B26), P9.2 |
| 7 | Density dropped for v1; no app motion preference, Windows animation settings only | F12 stays DROP; no motion key |
| 8 | Link-title enrichment opt-in, OFF by default; no startup or background network activity | P3.5; a guard that no network API is referenced outside the opt-in path |
| 9 | Explorer integration starts with "Send to → Noto"; no shell extension | P6.9 |
| 10 | Accessibility is cross-cutting in every UI PR; P9 is the final conformance pass | Section 4 gates |
| 11 | The High Contrast P0 check finishes before P1 implementation | P1.1 code starts after P0.1 passes |

## 10. Shortcut map and conflict matrix

**Evidence**

*Measured on this machine* (2026-10-20): every candidate was registered and released at once with `RegisterHotKey`; 1409 = already registered.
- **Taken:** `Ctrl+Shift+Win+B`, `L`, `X`, `0`–`9`, `Space`.
- **Free:** every other `Ctrl+Shift+Win+letter`, `Enter` and the arrows; the current `Ctrl+Alt+Win+Space`, `N`, `F`, `V`, `S` and `P`.

*Documented by Windows:*
- `Win+Ctrl+Shift+B`: graphics driver reset;
- `Win+Ctrl+Shift+<digit>`: open a taskbar app as administrator;
- `Win+(Ctrl+)(Shift+)Space`: input switching.

The owners of `L` and `X` on this machine are unknown. They are treated as reserved, and are re-measured on a clean machine at RC.

### 10.1 Global (rebindable, P6.1; G1 already shipped)

| Row | Action | Was | **Final default** | Why |
|---|---|---|---|---|
| G1 | Show / hide | `Ctrl+Alt+Win+Space` | **`Ctrl+Alt+Win+Space` (unchanged)** | Space is not an AltGr character key. The `Ctrl+Shift+Win` form is **taken by Windows** (measured). Already shipped and validated in Slice 1; changing it would invalidate the frozen evidence for no AltGr gain |
| G2 | New note in current folder | `Ctrl+Alt+Win+N` | **`Ctrl+Shift+Win+N`** | Decision 2; measured free |
| G3 | Search | `Ctrl+Alt+Win+F` | **`Ctrl+Shift+Win+F`** | Decision 2; measured free |
| G4 | Note from clipboard | `Ctrl+Alt+Win+V` | **`Ctrl+Shift+Win+V`** | Decision 2; measured free |
| G5 | Change side | unbound | unbound | Parity |
| G6 | Pin window | unbound | unbound | Parity |

### 10.2 In-app

`Ctrl+Alt` combined with a **non-character** key is kept. AltGr modifies only character keys, so AltGr with an arrow or Backspace types nothing on Windows layouts; this is verified on non-US layouts in P9.7.

| Row | Action | Scope | Was | **Final** |
|---|---|---|---|---|
| G7 | Hide panel | workspace | `Ctrl+W` | `Ctrl+W` |
| G8 | Back / hide | workspace | `Esc` | `Esc` (leaving the editor comes first, then the Escape setting) |
| G9 | Enter folder | folder list | `Ctrl+Down` | `Ctrl+Down` |
| G10 | Last-open folder | workspace | `Ctrl+Alt+O` | **`Ctrl+Shift+O`** (was a `Ctrl+Alt+letter`) |
| G11 / G12 | Next / previous note | stack | `Ctrl+Alt+Down` / `Up` | unchanged |
| G13 | Hide (optional) | workspace | `Ctrl+Enter` | off by default |
| G14 | New note / new folder | stack / folder list | `Ctrl+N` | `Ctrl+N`, **also while editing a card**. The parity note "unbound in the editor" applies to the old drill-in editor; amended at RC |
| G15 | Delete note | stack | `Ctrl+Alt+Backspace` | unchanged (shipped) |
| G16 | Move to folder | stack | `Ctrl+Shift+M` | unchanged |
| G17 / G18 | Move note up / down | stack | `Ctrl+Alt+Shift+Up` / `Down` | unchanged |
| G19 / G20 | Fold / unfold | stack | `Ctrl+Alt+Left` / `Right` | unchanged |
| G21 / G22 | Fold / unfold all | stack | `Ctrl+Alt+Shift+Left` / `Right` | unchanged |
| G23 / G24 | Note colour 1–6 / clear | stack | `Ctrl+1`…`6` / `Ctrl+0` | unchanged |
| G25 | Print | stack | `Ctrl+P` | unchanged |
| G26 | Pin note | stack | `Ctrl+Shift+P` | **unbound** (decision 3) |
| G27 / G28 | Bold / italic | editor | `Ctrl+B` / `Ctrl+I` | unchanged |
| D7 | Underline | editor | (no chord row) | **`Ctrl+U`** (Windows convention) |
| D5 | Strikethrough | editor | (no chord row) | **`Ctrl+Shift+E`** (mirrors SideNotes' `Cmd-Shift-E`) |
| G29 | Header | editor | `Ctrl+Shift+H` | unchanged |
| G30 / G31 | Bullet / ordered list | editor | `Ctrl+L` / `Ctrl+Shift+L` | unchanged |
| G32 / G33 | Task / toggle task | editor | `Ctrl+T` / `Ctrl+.` | unchanged |
| G34 | Clear completed tasks | editor | `Ctrl+Alt+Shift+T` | **`Ctrl+Shift+T`** (was AltGr+Shift+letter) |
| G35 | Insert picture | editor | `Ctrl+Shift+P` | unchanged (decision 3) |
| G36 / G37 | Indent / outdent | editor | `Ctrl+]` / `Ctrl+[` | unchanged; Tab / Shift+Tab inside list items only |
| G38 / G39 | Move line up / down | editor | `Alt+Up` / `Down` | unchanged |
| G40 | Show / hide markup | editor | `Ctrl+Shift+R` | unchanged |
| G41 / G42 | Font size up / down | workspace | `Ctrl+=` (`Ctrl++`) / `Ctrl+-` | unchanged |
| G44 | Preview image / file | editor (embedded item selected) | `Space` | unchanged |
| D52 | Soft line break | editor | - | `Shift+Enter` |
| D55 | Undo / redo | editor | - | `Ctrl+Z` / `Ctrl+Y`, `Ctrl+Shift+Z` |
| G45 | Search | workspace | `Ctrl+F` | unchanged |
| G46 / G47 | Copy note / without line 1 | search window | `Ctrl+C` / `Ctrl+Shift+C` | unchanged |
| G48 | Open URL from line 2 | search window | `Ctrl+R` | unchanged |
| G49 / D54 | Open folder (search) / quick copy (editor) | different scopes | `Ctrl+Click` | resolved in P2.0, together with link opening |
| - | Settings | workspace | `Ctrl+,` | unchanged (shipped) |
| - | Rename folder | folder list | `F2` | unchanged (shipped) |

### 10.3 Conflict matrix

| Chord | Windows / shell | IME / text input | `RichEditBox` built-in | Other Noto scope | Common apps | Verdict |
|---|---|---|---|---|---|---|
| `Ctrl+Alt+Win+Space` | free (measured) | - | - | - | - | keep |
| `Ctrl+Shift+Win+N` / `F` / `V` | free (measured); nothing documented | - | - | - | PowerToys defaults use `Win+Ctrl+Shift` + `M`, `T`, `R` only | use |
| `Ctrl+Shift+Win+Space` / `B` / `L` / `X` / digits | **taken** (measured; B and digits documented) | - | - | - | - | never a default; the recorder already refuses held chords |
| `Ctrl+Shift+O`, `Ctrl+Shift+T`, `Ctrl+Shift+E`, `Ctrl+U` | - | - | `Ctrl+U` is RichEdit underline (same meaning) | none | browser reopen-tab (other app) | use |
| `Ctrl+Alt+arrows` | none | AltGr+arrow types nothing | - | - | **legacy Intel graphics hotkeys rotate the display** (driver-dependent, off by default in current drivers) | keep; rebindable in P9.1; noted at RC |
| `Ctrl+1`–`6`, `Ctrl+0`, `Ctrl+=`, `Ctrl+-`, `Ctrl+L`, `Ctrl+Shift+L`, `Ctrl+E`, `Ctrl+R`, `Ctrl+J`, `Ctrl+[`, `Ctrl+]` | - | - | **RichEdit formatting accelerators** (line spacing, alignment, list, sub/superscript, font size) | - | - | the editor sets `DisabledFormattingAccelerators = All` and owns the map (P2.0, guarded) |
| `Ctrl+.` | - | Microsoft Pinyin IME punctuation toggle while the IME is on | - | - | - | keep; the IME wins while active; rebindable in P9.1 |
| `Ctrl+Space` | - | **IME on / off (CJK)** | - | - | - | never used |
| `Shift+Enter` | - | - | inserts a soft break natively | - | - | D52; canonical markdown decided in P2.0 |
| `Tab` / `Shift+Tab` | - | - | inserts a tab | focus navigation | - | indents inside list items only; elsewhere moves focus. `Esc` always leaves the editor (P1.0 accessibility contract) |
| `Ctrl+Shift+P` | - | - | - | G26 vs G35 | - | G35 only (decision 3) |
| `Ctrl+0` | - | - | - | "reset zoom" convention | - | clear colour (parity); no zoom-reset chord |
| `Ctrl+N` while editing | - | - | - | B1 parity note | - | creates a note (card stack); parity note amended at RC |
| `Ctrl+Click` | - | - | - | G49 vs D54 vs open link | - | P2.0 decides |
