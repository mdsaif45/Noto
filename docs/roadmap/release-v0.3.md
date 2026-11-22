# v0.3 plan

**Status:** batches 1–4 merged (PRs #208–#231, 2026-11-17); batches 5 and 6 built, awaiting their harness runs. **Planned:** 2026-11-16.
**GitHub:** the [v0.3 milestone](https://github.com/mdsaif45/Noto/milestone/12).
**Related:** [release-v0.2.md](release-v0.2.md) (the last release), [release-v0.2-checklist.md](release-v0.2-checklist.md),
[sidenotes-parity.md](../product/sidenotes-parity.md), [roadmap.md](roadmap.md)

v0.2 shipped on 2026-11-15. Its parity re-score (#162) left **20 MUST rows open**, filed as #185–#199. The Narrator check
(#161) added two hardening bugs, #201 and #202. v0.3 closes them. The aim: the v0.1 promise, "a SideNotes replacement
on Windows", holds for every MUST row a user can reach.

The v0.1 rule still holds: one user-visible slice per PR, the same gates (build with warnings as errors, unit and guard
tests, `dotnet format`, and the runtime harness for the area).

---

## How the order was chosen

1. **Small and fully specified first.** The behaviour is built or half built, and only the surface or a command is
   missing. These close MUST rows at the lowest risk.
2. **Hardening next.** #201 and #202 need one ADR-016 decision and a Narrator run each.
3. **Then the larger features.** #187, #192, #196 and #195 each touch several rows and need design notes.
4. **Last, the edge and platform work:** #185 (the edge surfaces), #186 (exclusive full-screen) and #203 (Dock). These
   need ADR-007 decisions and, for #203, the two-monitor half of #153.

## Batch 1: five slices, built in parallel

| # | Item | Issue | Rows | Size | Decision taken |
|---|---|---|---|---|---|
| 1 | Settings: where a new note goes | #188 | B5, J8 | S | The four placements as one choice row. Every way to create a note follows it |
| 2 | Automatic backup frequency | #199 | I8, J38 | S | Every day stays the default; 45, 30 and 15 minutes added; retention stays the newest seven |
| 3 | Editor: insert picture, indent chords, separator | #194 | D16, D19, D29, G35–G37 | M | Ctrl+Shift+P picks a picture through the existing attachment path; Ctrl+] / Ctrl+[ as Tab / Shift+Tab; a Format menu separator |
| 4 | Search: Ctrl+R opens a result's link, Ctrl+Click its folder | #197 | D37, E4, E5, G48, G49 | S–M | The first safe link in the note, through the existing link rule; no safe link shows a notice |
| 5 | Move to: a new folder inline | #189 | B11 | S–M | "New folder…" at the end of Move to, with the folder rules of the **+** button |

## Batch 2: five slices, built in parallel

| # | Item | Issue | Rows | Size | Decision taken |
|---|---|---|---|---|---|
| 6 | The note list's menu, and double-click to fold | #190 | B12, B13, B18 | M | A "…" menu on the list's bar with Fold all and Unfold all. A double-click folds only where it can't be editing: a card that isn't being edited. Inside an editor it stays word selection |
| 7 | Folder history | #193 | C5, C6, G10, C14, J10 | M | A list of recently opened folders, separate from Move to's. Ctrl+Shift+O switches between the last two folders. Back lists the recent ones. "Open the last folder when Noto starts", off by default |
| 8 | Global shortcuts: change side, keep open | #198 | G5, G50, J25 | S–M | Both off by default and rebindable, on #159's path. Pressed while hidden, Noto stays hidden. Ctrl+D is unchanged |
| 9 | Note colour style: background or bar | #191 | — | S | Background, today's look, is the default. Bar draws the edge marker without the tint. High Contrast is unchanged |
| 10 | Keys that work with Narrator on | #202 | (M4) | S | Ctrl+Alt+arrows stay. Alternative chords that Narrator leaves alone are added for fold, unfold, previous and next note, and shown in the menus |

**Moved out of batch 2:** #201, *Narrator says a row's state*. It changes every row's accessible name, which nearly every
harness finds rows by, so it gets a PR of its own after batches 1 and 2 land, with the harnesses moved to automation
ids in the same change.

## Batch 3: five slices, built in parallel

#187 holds five clipboard behaviours, so it is two slices.

| # | Item | Issue | Rows | Size | Decision taken |
|---|---|---|---|---|---|
| 11 | The **+** menu, and copy a picture | #187 (1 of 2) | B3, D43, D32 | S–M | The + button's click is unchanged. Its context menu offers New note and New note from clipboard, through #157's path. A picture in a note gets Copy picture |
| 12 | Paste behaviours | #187 (2 of 2) | D39, D41, D42 | M | Code detection wraps pasted code in a fence, **off by default**. Link titles are **not fetched**, since Noto never uses the network (principle 4). Pasting a URL over selected text makes a Markdown link instead, off by default. Auto-spaces: none are added, and a test pins that |
| 13 | `noto://` and a command line | #192 | H3, H5, C2, C7, C16, E6 | M–L | ADR-022. `new`, `open`, `folders`, `new-folder` and `search`, with the same verbs on the command line. One parser in Core. A URI may only create or show, never delete, move or read files. The scheme is registered per user by the installer's hooks; a portable build never registers it. Folders get Copy link |
| 14 | File shortcuts with previews | #196 | D34, I3, J16, J30 | M–L | A file link shows its shell icon and name, in Standard or Compact style. A double-click opens it through `ShellLaunch`'s checks. Two drop settings, whose defaults keep today's behaviour |
| 15 | Code mode | #195 | D27, D28, D45 | M–L | Markdown (the default), Code (monospace, raw, spell check off) or Plain text, per note. A schema migration defaults existing notes to Markdown. Mode › in the card menu, and a default for new notes in Settings |

## Batches 1–3: merged

All 15 slices merged on 2026-11-17, one PR each (two for #187). Each was rebased on the one before it, and its runtime
harness passed on the rebased build before it merged. The harnesses found three product defects, all fixed before
merging:
- Copy picture was offered for another note's picture: text controls share one menu.
- Copy picture looked at the whole paragraph, not the clicked line.
- A note made from a `noto://` link did not take the new-note mode.

After the batches, MUST rows stand at 138 of 147 done ([sidenotes-parity.md](../product/sidenotes-parity.md),
*Current status*).

## Batch 4: the last parity rows within reach, and housekeeping

| # | Item | Issue | Rows | Decision taken |
|---|---|---|---|---|
| 16 | Narrator says a row's state | #201 | (M4) | The state follows the title in the row's name, from the same words as its help text (ADR-016 §8 amended). The harnesses find rows by title |
| 17 | Hot Side, and drag to reveal | #185 (1 of 2) | A4, A15, J27, J28 | One low-level mouse hook, present only while either is on. Both off by default. A short dwell at the docked edge reveals Noto, never over a full-screen app (ADR-007 amended) |
| 18 | Search and performance close-out | #17, #11 | — | Audited against their own scope. Small gaps filled, then closed with the evidence |
| 19 | Dependency updates | #221–#223 | — | Each checked for breaking changes, Microsoft.Data.Sqlite 10 most of all. Taken only if the gates and the harness's PowerShell load still pass |
| 20 | The parity recount, and this status | — | — | Done with this change |

**Then:** #185's Open Bar (A3) and the v0.3 release. #186 and #203 stay after it: each needs an ADR-007 decision and
hardware this machine lacks.

## Batch 4: merged

#201, #185 part 1, #17 and #11 close-outs, the dependency updates (#229) and the recount all merged on 2026-11-17.
Measured on the way:
- **Hot Side** shows Noto but does not take the foreground: Windows refuses it (ADR-007 §9).
- **SQLite 3.53** reads a double-quoted string as a column name. Noto's SQL never relied on that; one harness did, and
  is fixed.

## Batch 5: the Open Bar, and the first SHOULD rows (v0.4)

The MUST rows within reach close with the Open Bar. The SHOULD rows begin in a new
[v0.4 milestone](https://github.com/mdsaif45/Noto/milestone/13).

| # | Item | Issue | Rows | Decision taken |
|---|---|---|---|---|
| 21 | The Open Bar | #185 (2 of 2) | A3, J1, J27 | A visible edge handle with SideNotes' three hide modes, its own toggle, built on #231's edge geometry (ADR-007 §9) |
| 22 | Duplicate a note, and a folder | #232 | B22, C12 | One command each. A copy shares its pictures' files, and the orphan sweep counts every reference |
| 23 | The backup folder | #233 | I11, J39 | Any folder the user picks, OneDrive included. Only `auto-*` files are ever pruned. A missing folder falls back to the default and says so |
| 24 | Line height and paragraph spacing | #234 | D49, D50, J14, J15 | Three presets each; Normal is today's exact look. The editor and the read view share the values |
| 25 | Clear formatting, and save a picture | #235 | D21, D33 | Inline marks only (block structure and pictures untouched), one undo step. Save picture as… copies the kept file byte for byte |

**Not yet:** Print (B21, G25, I6). WinUI 3 printing for an unpackaged app needs a spike first.

## Batch 6: more SHOULD rows, and a print spike (v0.4)

| # | Item | Issue | Rows | Decision taken |
|---|---|---|---|---|
| 26 | Sort a folder's notes | #242 | C10 | Manual (default), Name, Created or Modified, per folder (schema v6). Pinned first. Sorting is a view, so Manual restores the user's order |
| 27 | Ask which folder for a shortcut note | #243 | B6, J9 | Off by default. A keyboard-first chooser for the global shortcuts only; Ctrl+N and + never ask |
| 28 | Move a checked task to the bottom | #244 | D23 | Off by default. Within its own list at its own level, nested items with it, one undo step |
| 29 | A default colour for new notes, and chips off | #245 | J24, F14, J18 | Every creation path uses the default colour; duplicates and imports keep their own |
| 30 | Printing: research | #246 | B21, G25, I6 | `PrintManagerInterop` + `PrintDocument` from the read view's renderer, at 0 bytes. The feature is #248 |

**Merging, batches 5 and 6:** every PR is waiting for its runtime harness. The desktop preflight is blocked while a
`cua-driver` process runs. When it is clear, each PR is rebased, its harness run, and merged, one at a time.

## Later in v0.3, or v0.4: the edge and the platform

| Item | Issue | Why last |
|---|---|---|
| Edge activation: Open Bar, Hot Side, drag to reveal | #185 | New always-present surfaces, so ADR-007 needs amending first |
| Topmost over exclusive full-screen | #186 | Needs a decision in ADR-007 and a real exclusive full-screen app |
| Dock mode | #203 | After #153's two-monitor half |

## Carried from v0.2: the owner's checks

#161 §2–§5 (High Contrast, scaling, IME), #30 (mixed DPI), #32 (Windows 10 22H2) and #153's two-monitor half. Each
needs a setting changed, hardware, or another machine. They move to this milestone unchanged.

## Not v0.3

M5 (#163, a screenshot that keeps its source) and M6 (#164, #18, #4, #12), as before.
