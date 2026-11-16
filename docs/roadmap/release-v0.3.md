# v0.3 plan

**Status:** batch 1 in progress. **Planned:** 2026-11-16.
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

## Batch 2: the remaining small and medium slices

| Item | Issue | Rows | Note |
|---|---|---|---|
| The note list's menu, and double-click to fold | #190 | B12, B13, B18 | Double-click must not break one-click editing: needs a short design note |
| Folder history: last folder, recent folders from Back | #193 | C5, C6, G10, C14, J10 | Three parts, one PR each if needed |
| Narrator says a row's state | #201 | (M4) | ADR-016 §8: the state joins the row's name after the title. The harnesses move to automation ids |
| Keys that work with Narrator on | #202 | (M4) | Alternative chords, or Narrator's pass-through documented and tested |
| Global shortcuts: change side, keep open | #198 | G5, G50, J25 | **Decided:** both, unbound by default, rebindable, on the per-action hotkey path of #156–#159 |
| Note colour style: background or bar | #191 | — | Background the default; High Contrast unchanged |

## Batch 3: larger features

| Item | Issue | Rows |
|---|---|---|
| Clipboard behaviours: the **+** menu, copy picture, code detection, link titles, paste spacing | #187 | B3, D32, D39, D41–D43 |
| `noto://` and a command line | #192 | C2, C7, C16, E6 |
| File shortcuts with previews, the import setting | #196 | D34, I3, J16, J30 |
| Code mode for a note | #195 | D27, D28, D45 |

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
