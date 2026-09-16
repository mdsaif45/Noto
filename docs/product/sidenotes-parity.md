# SideNotes Parity Specification

**Status:** Draft
**Last updated:** 2026-09-14
**Milestone:** [M3 — SideNotes Parity](../roadmap/roadmap.md#m3--sidenotes-parity--the-first-product)
**Source:** [sidenotes-feature-inventory.md](../research/_raw/sidenotes-feature-inventory.md) — SideNotes 1.6.5, macOS

---

## What this document is

This is the **objective definition of done** for M3, and therefore for Noto's
first product.

Every row traces to a numbered item in the feature inventory, which was built
from Apptorium's own documentation. Nothing here was invented. If a capability
is not in the inventory, it is not a parity requirement — it is a feature
request, and it belongs in
[competitor-enhancements.md](competitor-enhancements.md) or a later milestone.

> **The rule:** **M3 is complete when every `MUST` row is `[x]`.**
>
> `SHOULD` rows that are still `[ ]` at the end of M3 are explicitly listed as
> known gaps at the M4 gate and re-decided there — they do not silently slip.
> `DROP` rows are never checked; they are decisions, not work.

Two constraints inherited from the roadmap, restated because they are the ones
most likely to be violated in practice:

1. **Parity is functional, not visual.** Noto reproduces interaction concepts
   in its own visual language (ADR-011) and replaces macOS mechanisms with
   Windows ones. Nothing in this document requires visual cloning, and no row
   may be read as requiring it.
2. **Every parity item is a tested requirement** with acceptance criteria, not
   a checkbox someone ticks. Rows become GitHub issues under the M3 milestone;
   the `Issue` column is populated when they do.

---

## How to read it

### Columns

```
  Status  ID  Feature  SideNotes behavior  Noto requirement  Level  Issue  Notes
    │     │                                                    │      │
    │     └── inventory ID — A1, D24, J7 … traceable to source │      └── created from this doc
    │                                                          └── the decision vocabulary
    └── [ ] not started · [~] in progress · [x] done
```

Cells are deliberately short. **Reasoning lives in the prose paragraph under
each table**, not crammed into a cell.

**On IDs.** Categories A–F, H and I reuse the inventory's own IDs verbatim, so
every row is traceable to a numbered source item — all 172 are present here,
none invented. The inventory's **G (shortcuts)** and **J (preferences)**
sections are unnumbered tables, so this document assigns `G1`–`G52` and
`J1`–`J40` and each row carries the SideNotes chord or setting it derives from
in the `SideNotes` column. Those IDs originate here and are stable from now on.

### Decision vocabulary

| Level | Meaning |
| ----- | ------- |
| **MUST** | Parity is not achieved without it. A Windows SideNotes missing this is not a Windows SideNotes. |
| **SHOULD** | Parity is materially weaker without it. Not disqualifying alone; several missing is. |
| **ADAPT** | SideNotes has it; Noto achieves the **same user outcome** by a different Windows mechanism. The outcome is the requirement, the mechanism is not. |
| **DROP** | macOS-specific with no sensible Windows equivalent. A reason is always given. |
| **BETTER** | Noto deliberately exceeds SideNotes. Justified individually, and kept small. |

**`DROP` rows are deliberate non-goals with reasons, not omissions.** They are
listed precisely so that nobody re-discovers them in six months and files them
as missing work. A `DROP` with no reason is a bug in this document.

### Provisional rows

The inventory labels every claim **CONFIRMED**, **INFERRED** or **UNKNOWN**.
That labelling is preserved here.

- Rows sourced from **INFERRED** or **UNKNOWN** evidence are marked
  **`⚠ provisional`** in Notes.
- A provisional row's *requirement* may be correct, but its *fidelity to
  SideNotes* is unverified. See [Open questions](#13-open-questions).
- **A provisional row may not be the sole justification for a MUST.** Where a
  provisional item is nonetheless MUST, it is because Noto needs the capability
  on its own merits, not because SideNotes is proven to have it.

---

## 1. A — Sidebar / Workspace

| Status | ID | Feature | SideNotes behavior | Noto requirement | Level | Issue | Notes |
| :----: | -- | ------- | ------------------ | ---------------- | :---: | :---: | ----- |
| `[ ]` | A1 | Screen edge window | Occupies one vertical screen edge as an overlay panel | Edge-docked topmost panel (ADR-007) | MUST | — | M2 |
| `[ ]` | A2 | Side changing | Left / right edge; default right | Left / right, switchable; default right | MUST | — | M2 |
| `[ ]` | A3 | Open Bar | Semi-transparent edge strip; click to reveal. Hide: never / mouse inactive / always | Edge handle with the same three hide modes | MUST | — | `always` must not require another surface |
| `[ ]` | A4 | Hot Side mode | Hover the screen edge to reveal | Hover activation, independently disableable | MUST | — | Principle 7 |
| `[ ]` | A5 | Menubar Icon mode | Menu-bar icon toggles visibility | **System tray** icon toggles visibility | ADAPT | — | macOS menu bar → Windows tray |
| `[ ]` | A6 | Icon context menu | Right-click menu-bar icon → actions | Tray icon right-click context menu | ADAPT | — | |
| `[ ]` | A7 | Global show/hide shortcut | `⌃⌥⌘␣`, configurable | Global hotkey, configurable, works when unfocused | MUST | — | See G |
| `[ ]` | A8 | Always on top | Over other apps incl. full-screen; Stage Manager compatible | Topmost over normal and full-screen windows | MUST | — | Stage Manager has no analogue; drop that half |
| `[ ]` | A9 | Pin window open | Configurable shortcut pins the panel visible | Pin panel open, keyboard + tray reachable | MUST | — | |
| `[ ]` | A10 | Close window | `⌘W` hides the panel | `Ctrl+W` hides the panel | MUST | — | |
| `[ ]` | A11 | Hide with modifier+Enter | Optional `⌘↩` binding to hide | Optional `Ctrl+Enter` binding to hide | SHOULD | — | Optional, off by default |
| `[ ]` | A12 | Escape behavior | 4 modes: leave-folder-or-hide / leave-folder / hide / none | The same 4 modes, same default | MUST | — | |
| `[ ]` | A13 | Close on click outside | Panel hides on outside click | Same, as a setting | MUST | — | |
| `[ ]` | A14 | Width resize | Free drag on the window edge; no presets | Free drag resize, width persisted | MUST | — | M2 |
| `[ ]` | A15 | Drag-to-reveal at edge | Dragging content to the edge reveals the panel; disableable | Same; two independent toggles as in SideNotes | MUST | — | Disableable per principle 7 |
| `[ ]` | A16 | Launch on startup | Auto-launch at login | Run at login, off by default | MUST | — | M2 |
| `[ ]` | A17 | Second launch focuses | Relaunch brings existing instance forward | Single-instance; second launch shows the panel | MUST | — | |
| `[ ]` | A18 | Multi-display behavior | Undocumented | Deterministic monitor choice + follow-focus option | MUST | — | ⚠ provisional — UNKNOWN in every source |
| `[ ]` | A19 | Virtual desktop behavior | Undocumented (Spaces) | Defined, documented Virtual Desktop behavior | MUST | — | ⚠ provisional — UNKNOWN |
| `[ ]` | A20 | Slide animation | Panel slides out from the edge | Slide-in/out within the perf budget | SHOULD | — | ⚠ provisional — INFERRED from review |
| `[ ]` | A21 | Two-finger edge gesture | Trackpad edge gesture; known to conflict | Precision-touchpad gesture **if** available, **off by default** | ADAPT | — | ⚠ provisional — INFERRED. Keyboard path mandatory |

**A1–A4, A14, A16** are M2 work, not M3 — they are listed here because parity
is defined by the whole set, and M3's exit criterion reads this table. They
arrive checked.

**A3 and A4 are the product's loudest research finding.** SideNotes users have
complained for years that they must choose between a visible Open Bar that
opens accidentally and an extra menu-bar icon, with no keyboard-only mode.
Principle 7 makes every activation surface independently disableable, which
turns SideNotes' `Hide Open Bar = always` restriction (only available in Hot
Side and Menubar modes) into an unconditional capability. That is recorded as a
BETTER row in section 12 rather than bloating this table.

**A8 splits.** "Always on top, including over full-screen apps" is a MUST and
is the harder half. "Stage Manager compatible" is meaningless on Windows and is
silently dropped — Windows Virtual Desktops are covered by A19 instead.

**A18 and A19 are the largest provisional rows in the document.** Multi-display
behavior is undocumented on every Apptorium page, and the inventory says so
explicitly. Noto cannot copy an undocumented behavior, so these rows specify
*that a deterministic behavior exists and is documented*, not *which* behavior.
They are MUST because a multi-monitor Windows user hits this on day one — not
because SideNotes is known to do it well. The actual behavior is decided in M2
and recorded, not inherited.

**A21 is the one gesture row.** SideNotes' two-finger edge gesture is a
documented source of conflict. Noto may implement a precision-touchpad
equivalent where the hardware supports it, but it ships **off by default** and
never as the only path to anything (principle 7).

---

## 2. B — Note management

| Status | ID | Feature | SideNotes behavior | Noto requirement | Level | Issue | Notes |
| :----: | -- | ------- | ------------------ | ---------------- | :---: | :---: | ----- |
| `[ ]` | B1 | Create note (in app) | `⌘N`; creates a folder in folder list | `Ctrl+N`, context-dependent | MUST | — | |
| `[ ]` | B2 | Create note (global) | `⌃⌥⌘N`; panel opens with note active | Global hotkey, note focused and ready to type | MUST | — | |
| `[ ]` | B3 | Note from clipboard | New note from clipboard; long-press **+** or global shortcut | Same, via global hotkey and **+** menu | MUST | — | |
| `[ ]` | B4 | Create by drag & drop | Drop text / files / images to create a note | Same, Explorer and app drag sources | MUST | — | |
| `[ ]` | B5 | New note placement | top / bottom / over current / under current | The same four options | MUST | — | |
| `[ ]` | B6 | Ask for folder on global create | Setting: prompt for destination folder | Same setting | SHOULD | — | |
| `[ ]` | B7 | Delete note | `⌥⌘⌫`; confirmation popover since 1.6 | `Alt+Ctrl+Backspace`; **soft delete**, no blocking confirm | BETTER | — | See B23 / section 12 |
| `[ ]` | B8 | Move note up / down | `⇧⌥⌘↑` / `⇧⌥⌘↓` | Same action, Windows chord | MUST | — | |
| `[ ]` | B9 | Move note to top / bottom | Exists; chord unpublished | Move to top / bottom, keyboard reachable | SHOULD | — | ⚠ provisional — UNKNOWN chord |
| `[ ]` | B10 | Drag & drop reordering | Reorder notes and folders; note-onto-note disabled | Same, incl. the note-onto-note prohibition | MUST | — | |
| `[ ]` | B11 | Move note to folder | `⇧⌘M`; recent folders; create folder inline | Same, incl. recent folders and inline create | MUST | — | |
| `[ ]` | B12 | Fold / unfold note | Collapse to first line; gear, double-click, pinch, keyboard | Collapse to first line; menu, double-click, keyboard | MUST | — | Pinch → touchpad only, optional |
| `[ ]` | B13 | Fold all / unfold all | `⇧⌥⌘←` / `⇧⌥⌘→`; also the **…** menu | Same, keyboard and list menu | MUST | — | |
| `[ ]` | B14 | Pin notes | Pinned notes stay at list top | Same | MUST | — | |
| `[ ]` | B15 | Note colors | Six colors + empty; `⌘0`–`⌘6`; Background or Bar style | Six colors + none; `Ctrl+0`–`Ctrl+6`; both styles | MUST | — | Six, not "some" |
| `[ ]` | B16 | First line is the title | Title = line 1; survives folding; excluded by quick-copy; in note URL | Same rule, everywhere the title is used | MUST | — | No separate title field |
| `[ ]` | B17 | Note bottom bar | Per-note action bar (format / share / move) | Per-note action affordance; Noto's own layout | SHOULD | — | Functional, not visual |
| `[ ]` | B18 | List bottom bar | List-level **…** menu + note count | List-level menu + note count | SHOULD | — | |
| `[ ]` | B19 | Auto-save | Continuous persistence; no save command | Continuous persistence; no save command | MUST | — | ⚠ provisional — INFERRED (no save cmd documented) |
| `[ ]` | B20 | Switch between notes | `⌘⌥↓` / `⌘⌥↑` | Same action, Windows chord | MUST | — | |
| `[ ]` | B21 | Print a note | `⌘P` | `Ctrl+P` | SHOULD | — | Top historic complaint; fixed in 1.6.3 |
| `[ ]` | B22 | Duplicate note | Not documented (folder duplication is) | Duplicate note | SHOULD | — | ⚠ provisional — UNKNOWN in SideNotes |
| `[ ]` | B23 | Trash / restore | **None.** Recovery is backups only | Soft delete + recycle bin + restore | BETTER | — | ADR-002; M1 |
| `[ ]` | B24 | Note templates | Not documented | **Not a parity requirement** | DROP | — | No evidence it exists. Do not build speculatively |
| `[ ]` | B25 | Per-note width | Notes fill panel width; no per-note size | Notes fill panel width | MUST | — | ⚠ provisional — INFERRED |
| `[ ]` | B26 | Empty note placeholder | Placeholder UI for empty notes | Placeholder text in an empty note | SHOULD | — | |

**B7 and B23 are one decision, not two.** SideNotes deletes immediately behind
a confirmation popover, and has no trash — the inventory is explicit that
recovery is via backups only. Noto inverts this: soft delete into a recycle
bin, which removes the need for a modal confirmation on every delete. This is a
BETTER row and is justified in section 12; it is also already M1 work, so it
costs M3 nothing.

**B16 is load-bearing and easy to get wrong.** SideNotes has no title field.
Line 1 *is* the title: it is what remains when a note is folded, what
`⌘⌥C` excludes from quick-copy, and what appears in the note URL. Noto must
adopt the same rule rather than adding a title column, because adding one would
change quick-copy, folding and URL semantics all at once.

**B22 is a SHOULD on Noto's merits, not SideNotes'.** Folder duplication is
CONFIRMED in SideNotes 1.6.3; note duplication is not documented anywhere.
Noto does it because it is trivially cheap once folder duplication exists, not
because parity demands it. If it slips, parity is not affected.

**B24 is a genuine non-goal.** No source mentions templates. Building them
would be inventing a SideNotes feature, which this document forbids.

---

## 3. C — Folders / organization

| Status | ID | Feature | SideNotes behavior | Noto requirement | Level | Issue | Notes |
| :----: | -- | ------- | ------------------ | ---------------- | :---: | :---: | ----- |
| `[ ]` | C1 | Folders | Folder list is top level; entering shows notes | Same two-level navigation model | MUST | — | |
| `[ ]` | C2 | Create folder | `⌘N` in folder list; also via automation | `Ctrl+N` in folder list; also via URI / CLI | MUST | — | |
| `[ ]` | C3 | Nesting depth | Flat, one level (no sub-folders documented) | **Flat, one level** | MUST | — | ⚠ provisional — INFERRED from absence. Do not add nesting |
| `[ ]` | C4 | Enter folder | `⌘↓`; double-click, or single-click if enabled | `Ctrl+Down`; same single-click setting | MUST | — | |
| `[ ]` | C5 | Leave folder / back | `⎋`; long-press back → recent folders | `Esc`; back control exposes recent folders | MUST | — | |
| `[ ]` | C6 | Switch to last folder | `⌘⌥O` | Same action, Windows chord | MUST | — | |
| `[ ]` | C7 | Show all folders | Command to return to the folder list | Same, incl. from URI / CLI | MUST | — | |
| `[ ]` | C8 | Pin folders | Pinned folders stay at list top | Same | MUST | — | |
| `[ ]` | C9 | Folder reordering | Drag & drop up / down | Same | MUST | — | |
| `[ ]` | C10 | Folder sorting | Exists since 1.3; keys not enumerated | Sort by name / created / modified / manual | SHOULD | — | ⚠ provisional — UNKNOWN options |
| `[ ]` | C11 | Delete folder | Confirmation popover | Soft delete to recycle bin, notes included | MUST | — | Consistent with B23 |
| `[ ]` | C12 | Duplicate folder | Copy a folder with contents | Same | SHOULD | — | |
| `[ ]` | C13 | Rename folder | Inline rename; Esc cancels | Inline rename; Esc cancels | MUST | — | |
| `[ ]` | C14 | Open last folder on restart | Setting | Same setting | SHOULD | — | |
| `[ ]` | C15 | Empty folder guidance | Helper text in an empty folder | Same | SHOULD | — | Principle 2: shown only in the empty state |
| `[ ]` | C16 | Folder URL | `Copy URL` from context menu | `noto://` folder URI, copyable | ADAPT | — | See H5 |
| `[ ]` | C17 | Folder colors | Not documented | **Not a parity requirement** | DROP | — | Theme has folder *styling*, not per-folder color |
| `[ ]` | C18 | All-notes / favorites / archive | None documented | **Not a parity requirement** | DROP | — | Pinning + search are the documented access paths |
| `[ ]` | C19 | Note count | Shown in the list bottom bar | Same | SHOULD | — | |

**C3 is the most important restraint in this document.** Every source describes
a flat folder → notes hierarchy; no sub-folder feature is documented anywhere.
The inventory labels this INFERRED-from-absence and instructs treating it as
flat unless disproven. Noto matches it. **Deeper nesting is not a parity
requirement and must not be added speculatively** — it is a second
organizational paradigm, which principle 9 defaults to no, and it would change
navigation, move, search scoping and URI shape simultaneously. If nesting is
ever wanted, it is an M7 candidate with its own justification.

**C17 and C18 are dropped for the same reason as B24:** absence of evidence in
the source. Note colors are CONFIRMED; folder colors are not. Pinning (B14,
C8) is the documented analogue of favorites, and cross-folder search (E1) is
the documented analogue of an all-notes view. Building either would be
inventing scope.

**C11 deviates deliberately.** SideNotes confirms-then-deletes; Noto soft
deletes. Folder deletion taking its notes with it into the recycle bin is the
only coherent behavior once B23 exists.

### C2 / C13 — the M2-1 Folder Pane interpretation

The M2-1 Folder Pane design gate had to decide how C2 and C13 are actually
driven. The distinction matters, so it is stated explicitly:

| | Source |
| --- | --- |
| `⌘N` creates a folder *in the folder list*; Noto's equivalent is `Ctrl+N` | **SideNotes documented capability** (C2) |
| Rename is inline, and `Esc` cancels it | **SideNotes documented capability** (C13) |
| `Ctrl+N` is scoped to the folder pane, not global | **Noto interpretation** — the global hotkey is a separate M2 surface, and principle 7 requires each activation surface to be independently disableable |
| `F2` also starts a rename | **Noto interpretation** — the Windows convention for inline rename; no macOS analogue exists to copy |
| Double-click does *not* start a rename | **Noto interpretation** — C4 reserves double-click for *enter folder* |
| Focus loss cancels a rename rather than committing it | **Noto interpretation** — C13 documents only Esc-cancel; committing on blur would persist a value the user may have abandoned |

**Folder names are deliberately not unique, and this is an engine rule rather
than a parity one.** The frozen engine contract makes duplicate folder names
legal (Case G), so `CreateFolder` and `RenameFolder` never return
`DuplicateName`. The Folder Pane therefore has **no duplicate-name error
state** — showing one would report a supported outcome as a failure. Tag names
are the opposite case and *are* unique; the two must not be generalised to each
other.

---

## 4. D — Editor & content

### 4.1 D.1 — Markdown & formatting

| Status | ID | Feature | SideNotes behavior | Noto requirement | Level | Issue | Notes |
| :----: | -- | ------- | ------------------ | ---------------- | :---: | :---: | ----- |
| `[ ]` | D1 | Headers | `#`…`#####`, 5 levels; `⇧⌘H` | 5 levels; `Ctrl+Shift+H` | MUST | — | ADR-004 |
| `[ ]` | D2 | Bold | `**text**`; `⌘B` | Same; `Ctrl+B` | MUST | — | |
| `[ ]` | D3 | Italic | `*text*`; `⌘I` | Same; `Ctrl+I` | MUST | — | |
| `[ ]` | D4 | Bold italic | `***text***` | Same | MUST | — | |
| `[ ]` | D5 | Strikethrough | `~~text~~` | Same | MUST | — | |
| `[ ]` | D6 | Mark / highlight | `::text::` (non-standard) | Same syntax, same rendering | SHOULD | — | Non-standard; stored verbatim, export stays lossless |
| `[ ]` | D7 | Underline | Added 1.6; syntax undocumented | Underline via shortcut + toolbar | SHOULD | — | ⚠ provisional — UNKNOWN syntax. Needs a syntax decision |
| `[ ]` | D8 | Quote | `> text` | Same | MUST | — | |
| `[ ]` | D9 | Bullet list | `* item`; `⌘L` | `-` / `*`; `Ctrl+L` | MUST | — | |
| `[ ]` | D10 | Ordered list | `1. item`; `⇧⌘L` | Same; `Ctrl+Shift+L` | MUST | — | |
| `[ ]` | D11 | Task (unchecked) | `[ ]`; `⌘T` toggles line to task | `- [ ]`; `Ctrl+T` | MUST | — | ADR-004 uses GFM task lists |
| `[ ]` | D12 | Task (checked) | `[x]`; `⌘.` toggles state | `- [x]`; `Ctrl+.` | MUST | — | |
| `[ ]` | D13 | Inline code | `` `code` `` | Same | MUST | — | |
| `[ ]` | D14 | Code block | ` ``` ` fenced | Fenced, with language | MUST | — | |
| `[ ]` | D15 | Color swatch preview | `#rrggbb` renders a swatch | Same | SHOULD | — | |
| `[ ]` | D16 | Horizontal separator | `---` or Aa menu | `---` + formatting menu | MUST | — | |
| `[ ]` | D17 | Inline links | `[text](url)` | Same; scheme validated before open | MUST | — | ADR-004 security: http/https/mailto/file only |
| `[ ]` | D18 | Tables | Absent from all documentation | **Not a parity requirement** | DROP | — | ⚠ provisional — UNKNOWN. ADR-004 defers tables |
| `[ ]` | D19 | Indent / outdent | `⌘]` / `⌘[` | `Ctrl+]` / `Ctrl+[` | MUST | — | |
| `[ ]` | D20 | Move line up / down | `⌘⌥[` / `⌘⌥]` | `Alt+Up` / `Alt+Down` | MUST | — | Windows editor convention; see G |
| `[ ]` | D21 | Clear formatting | Exists; chord undocumented | Strip formatting from selection | SHOULD | — | ⚠ provisional — UNKNOWN chord |
| `[ ]` | D22 | Clear completed tasks | Deletes checked tasks; `⇧⌥⌘T` | Same; deleted text is undoable | MUST | — | |
| `[ ]` | D23 | Completed tasks to bottom | Checked tasks auto-move to list bottom | Same, as a setting | SHOULD | — | |
| `[ ]` | D24 | Show / hide markup | **Invisible markdown**, markup hidden by default; `⇧⌘R` | **Live-styled editing, markup hidden by default**; `Ctrl+Shift+R` toggles | MUST | — | **ADR-004 (revised): a parity MUST, not deferred** |
| `[ ]` | D25 | Quick formatting toolbar | Floating toolbar on selection; above / below / disabled | Same three options | SHOULD | — | Mouse convenience; keyboard path exists already |
| `[ ]` | D26 | Formatting menu (Aa) | Per-note menu of formatting commands | Equivalent menu; Noto's own layout | SHOULD | — | |

**D24 is the single highest-risk row in this document and it is non-negotiable.**
ADR-004 was revised on 2026-09-14 specifically to move live-styled editing —
"invisible markdown" — out of M8 and into M3. SideNotes rebuilt its entire
editor in 1.5 to ship it, which is a realistic signal of the cost. A Windows
SideNotes that shows raw `**asterisks**` by default is not at parity however
good the rest is. Plain-source editing is a valid *intermediate* state during
M2; it is not an acceptable end state.

**The supported syntax is a deliberate subset, and matching it is the point.**
ADR-004's in-scope list and the inventory's D1–D17 agree almost exactly. Tables
(D18) are absent from SideNotes' documentation and deferred by ADR-004 — they
are therefore **not a parity gap**, and adding them during M3 would be scope
creep dressed as completeness.

**D6 (`::mark::`) is non-standard markdown** and will not render in other
tools. It is a SHOULD rather than MUST for that reason, and because it is
stored verbatim in the markdown source, export remains lossless regardless
(ADR-002, ADR-004).

**D7 needs a decision before it can be built.** SideNotes added underline in
1.6 but published neither syntax nor shortcut. Noto must pick a representation
that survives round-tripping to plain markdown. Listed in section 13.

**D20 deviates from a literal mapping on purpose.** `⌘⌥[` / `⌘⌥]` translates
to `Ctrl+Alt+[` / `Ctrl+Alt+]`, which no Windows user would guess. `Alt+Up` /
`Alt+Down` is the Visual Studio / VS Code convention for exactly this action
and is what the target audience already has in their fingers.

### 4.2 D.2 — Note modes

| Status | ID | Feature | SideNotes behavior | Noto requirement | Level | Issue | Notes |
| :----: | -- | ------- | ------------------ | ---------------- | :---: | :---: | ----- |
| `[ ]` | D27 | Code mode | Per-note monospace mode; spell check off | Per-note code mode; monospace; spell check off | MUST | — | |
| `[ ]` | D28 | Three note modes | code / plain text / markdown; global default | Same three modes + a global default | SHOULD | — | ⚠ provisional — INFERRED naming (Setapp listing) |

**D27 is CONFIRMED and D28 is not.** Code mode is documented directly by
Apptorium; the *three-way* mode set appears only in a Setapp listing. Noto
implements code mode as a MUST and the three-way default as a SHOULD. All three
modes share one storage format (markdown source, ADR-004) — the mode affects
rendering and spell check, never what is written to disk, so this costs nothing
architecturally.

### 4.3 D.3 — Content types

| Status | ID | Feature | SideNotes behavior | Noto requirement | Level | Issue | Notes |
| :----: | -- | ------- | ------------------ | ---------------- | :---: | :---: | ----- |
| `[ ]` | D29 | Images | Embed inline; `⇧⌘P`, drag, paste | Same; `Ctrl+Shift+P`, drag, paste | MUST | — | Attachment store, M1 |
| `[ ]` | D30 | Screenshots | Screenshots as note content via drop | Same via drop and paste | MUST | — | Capture API is M5, not parity |
| `[ ]` | D31 | Image from iPhone/iPad | Continuity Camera | **No equivalent** | DROP | — | Apple Continuity. No Windows analogue |
| `[ ]` | D32 | Copy image quickly | Copy an embedded image to clipboard | Same | SHOULD | — | |
| `[ ]` | D33 | Save image to file | Export an embedded image | Same | SHOULD | — | |
| `[ ]` | D34 | File & folder shortcuts | Drop a file → shortcut with preview; standard / compact | Same; both preview styles; double-click opens | MUST | — | Path validation, principle 10 |
| `[ ]` | D35 | Quick Look preview | Space previews an image or file | **In-app preview** on Space for images; Explorer preview otherwise | ADAPT | — | Quick Look is a macOS system service |
| `[ ]` | D36 | Text snippets | Notes as reusable snippets via search + quick copy | Same pattern; no separate snippet feature | MUST | — | Emergent from E3, not new machinery |
| `[ ]` | D37 | URL handling | URLs clickable; `⌘R` launches from search | Clickable, scheme-validated; `Ctrl+R` from search | MUST | — | ADR-004 security rules |
| `[ ]` | D38 | Save web address quickly | Via the system Share extension | Clipboard capture + URI protocol | ADAPT | — | No Windows share sheet equivalent |
| `[ ]` | D39 | Code detection on paste | Detects pasted code → code block or code note | Same, with the same setting | SHOULD | — | |
| `[ ]` | D40 | Better pasting from ChatGPT | Cleanup of pasted LLM output | Sane paste cleanup for HTML/rich sources | SHOULD | — | Generalized; no per-product special case |
| `[ ]` | D41 | No auto-spaces on paste | Setting | Same setting | SHOULD | — | |
| `[ ]` | D42 | Smarter pasted links | Resolves link titles when accessible | Same, **off by default** | SHOULD | — | Title resolution is a network call — principle 4 and 10 |
| `[ ]` | D43 | Paste clipboard as note | Note from clipboard contents | Same (= B3) | MUST | — | Duplicate of B3 |
| `[ ]` | D44 | Text replacement | macOS system Text Replacement works in notes | **No equivalent; do not build one** | DROP | — | Windows has no system-wide equivalent service |
| `[ ]` | D45 | Spell check | Active in notes; off in code mode | Windows spell check; off in code mode | ADAPT | — | Windows spell-check API, not NSSpellChecker |
| `[ ]` | D46 | Word count | Not documented | **Not a parity requirement** | DROP | — | Note count (C19) exists; word count does not |

**D34 is a security surface as well as a feature.** File and folder shortcuts
store user paths and open them on double-click. Principle 10 requires every
path validated, and the logging rule forbids recording user file paths. This
row does not ship without both.

**D35 and D38 are the two ADAPTs in this group and they resolve differently.**
Quick Look has a clean Windows outcome — press Space, see the thing — reachable
by an in-app image preview plus Explorer's own preview for other file types.
The Share extension does not: Windows has no equivalent system share sheet, so
the *outcome* (capture the current web address into a note fast) is delivered
by clipboard capture and the `noto://` protocol instead. Neither adapts the
mechanism; both preserve the outcome.

**D42 is the one row where Noto is deliberately more conservative than
SideNotes.** Resolving a link's title means fetching the URL. Principle 4 says
Noto works completely offline, and principle 10 says notes are sensitive by
default — a paste that silently phones out to a host named in the clipboard is
not acceptable as a default. The feature ships; the default is off.

**D44 is dropped rather than adapted** because the outcome it delivers is a
system-wide OS service, not an app feature. Building a snippet-expansion engine
inside Noto to replace it would be a new subsystem to maintain for years
(principle 9) that no source asks for.

### 4.4 D.4 — Typography

| Status | ID | Feature | SideNotes behavior | Noto requirement | Level | Issue | Notes |
| :----: | -- | ------- | ------------------ | ---------------- | :---: | :---: | ----- |
| `[ ]` | D47 | Font family | Any installed font, across UI elements | Font family selection; token-driven (ADR-011) | MUST | — | |
| `[ ]` | D48 | Font size | `⌘+` / `⌘−` | `Ctrl+=` / `Ctrl+-`, persisted | MUST | — | |
| `[ ]` | D49 | Line height | Tight / Normal / Relaxed | The same three presets | SHOULD | — | |
| `[ ]` | D50 | Paragraph spacing | Tight / Normal / Relaxed | The same three presets | SHOULD | — | |
| `[ ]` | D51 | Missing-font fallback | Crash fixed in 1.6.1 | Font fallback; never fails on a missing font | MUST | — | A bug in SideNotes is a requirement here |

**D51 is included because the inventory recorded the crash.** A user-selected
font can be uninstalled between sessions. SideNotes crashed on it once; Noto
must fall back deterministically and say nothing (principle 6).

---

## 5. E — Search

| Status | ID | Feature | SideNotes behavior | Noto requirement | Level | Issue | Notes |
| :----: | -- | ------- | ------------------ | ---------------- | :---: | :---: | ----- |
| `[ ]` | E1 | Search window | Searches notes **and** folders, cross-folder; `⌘F` | Same scope; `Ctrl+F` | MUST | — | FTS5, ADR-003 |
| `[ ]` | E2 | Global search shortcut | `⌃⌥⌘F` recommended, configurable; works over full-screen apps | Configurable global hotkey; works when unfocused | MUST | — | |
| `[ ]` | E3 | Quick copy from results | `⌘C` copies all; `⌘⌥C` excludes line 1; window closes, focus returns | `Ctrl+C` / `Ctrl+Shift+C`; same close-and-return-focus | MUST | — | Focus return is the whole point |
| `[ ]` | E4 | Launch URL from result | Title on line 1, URL on line 2 → `⌘R` opens it | `Ctrl+R`, scheme-validated | SHOULD | — | |
| `[ ]` | E5 | Open containing folder | Modifier+click on a result | `Ctrl+Click` on a result | SHOULD | — | ⚠ provisional — UNKNOWN modifier (`⌃` vs `⌘`) |
| `[ ]` | E6 | Search via automation | AppleScript `search`/`search notes`/`search folders` | Search via `noto://` URI and CLI | ADAPT | — | See H3 |
| `[ ]` | E7 | Search via Shortcuts | Find Notes / Find Folders actions | Covered by the CLI | ADAPT | — | See H4 |
| `[ ]` | E8 | Live search / filters / in-note find | Not documented | Incremental results as you type | MUST | — | ⚠ provisional — UNKNOWN in SideNotes; required by principle 3 |

**E3 is not a copy shortcut, it is the snippet workflow.** SideNotes' documented
use as a snippet store (D36) depends entirely on this behavior: search, copy,
window closes, the *previously frontmost app regains focus* so the paste lands
where the user was working. Implementing the copy without the focus return
delivers none of the value. This is one of the few rows where the exact
interaction sequence, not just the capability, is the requirement.

**E3's `⌘⌥C` maps to `Ctrl+Shift+C`, not `Ctrl+Alt+C`.** See section 7 — the
literal `Alt` mapping collides with nothing standard but is awkward; `Ctrl+Shift+C`
is the conventional "copy variant" chord on Windows.

**E8 is MUST despite being UNKNOWN in SideNotes.** The inventory could not
confirm whether SideNotes' search is incremental. Noto requires it regardless,
because principle 3 states search results must be "as fast as typing" — a
search that requires pressing Enter fails that budget by construction. Filters
and in-note find are *not* pulled in with it; neither is documented, and
neither is required by a principle.

---

## 6. F — Appearance & themes

| Status | ID | Feature | SideNotes behavior | Noto requirement | Level | Issue | Notes |
| :----: | -- | ------- | ------------------ | ---------------- | :---: | :---: | ----- |
| `[ ]` | F1 | Light / dark / system | Follow system, or set manually | Same three states; follows Windows theme | MUST | — | Principle 8 |
| `[ ]` | F2 | UI themes | Installable color themes | Installable color themes | SHOULD | — | Built on ADR-011 tokens |
| `[ ]` | F3 | Custom theme files | `.sntheme` = JSON, schema-documented, editable in any editor | JSON theme file + published schema | SHOULD | — | Format is Noto's own |
| `[ ]` | F4 | Theme structure | `light` / `dark` / `any`; sections incl. notes.clean + 6 colors | Equivalent structure; must cover the six note colors | SHOULD | — | Structure follows ADR-011, not SideNotes' |
| `[ ]` | F5 | Theme color properties | background, text ×3, link, button, quote, mark, code, checkmark, preview | Token set covering the same surfaces | SHOULD | — | |
| `[ ]` | F6 | Theme color formats | `#rgb`, `#rrggbb`, `#rrggbbaa` | Same three | SHOULD | — | |
| `[ ]` | F7 | Theme installation | Double-click a theme file to install; manage + reveal in Finder | Double-click to install; manage + reveal in Explorer | SHOULD | — | File association needs the package (ADR-008) |
| `[ ]` | F8 | Theme gallery | Vendor-hosted downloadable gallery | **Not in M3** | DROP | — | A website, not an app feature |
| `[ ]` | F9 | Theme via automation | AppleScript / Shortcuts set theme | Set theme via CLI | ADAPT | — | |
| `[ ]` | F10 | Note color display style | Background (default) / Bar | Same two styles | MUST | — | |
| `[ ]` | F11 | Transparency | Open Bar semi-transparent; theme alpha supported | Alpha in themes; edge handle translucent | SHOULD | — | ⚠ provisional — UNKNOWN whether a window opacity setting exists |
| `[ ]` | F12 | Density | No global density setting; line height + compact file links only | Same — no global density setting | DROP | — | ⚠ provisional — INFERRED. Do not invent one |
| `[ ]` | F13 | Icon refresh | Icons refreshed across releases | **Not applicable** | DROP | — | Noto has its own icon set (ADR-011) |
| `[ ]` | F14 | Disable color swatches | Setting to hide `#rrggbb` swatches | Same setting | SHOULD | — | Pairs with D15 |

**F1 and F10 are the only MUSTs here, and that is intentional.** Light/dark
following the Windows theme is a principle-8 requirement, and the six note
colors (B15) are meaningless without their two display styles. Everything else
in this category is theming *depth* — genuinely present in SideNotes, genuinely
not what makes a notes panel a notes panel.

**F2–F7 are one piece of work, not six.** They describe a single capability:
a documented JSON theme format with an installer. Noto's theme file is built on
ADR-011's design tokens, so its structure is Noto's own — copying SideNotes'
section names would couple Noto's design system to a competitor's internals for
no user benefit. Parity here is "users can install and author themes", not
"the file looks the same".

**F8, F12, F13 are dropped as non-features.** A hosted gallery is a website.
Density is not a documented SideNotes setting at all (the inventory found only
line-height presets and compact file links, and labels the density row
INFERRED). Icon refreshes are release notes, not capabilities.

---

## 7. G — Keyboard shortcuts

47 documented SideNotes chords. Mapping rule: **⌘→Ctrl, ⌥→Alt, ⇧→Shift,
⌃→(nothing; folded into the global chord)**. Literal mapping is the default;
deviations are marked **↯** and justified below the table.

Global chords use `Ctrl+Alt+Win+<key>` where SideNotes uses `⌃⌥⌘<key>` —
Windows has no fourth modifier, and `Ctrl+Alt+Shift` collides with too much.

### 7.1 Global (work when Noto is hidden) — all configurable

| Status | ID | Action | SideNotes | Noto (proposed) | Level | Issue | Notes |
| :----: | -- | ------ | --------- | --------------- | :---: | :---: | ----- |
| `[ ]` | G1 | Show / hide app | `⌃⌥⌘␣` | `Ctrl+Alt+Win+Space` | MUST | — | ⚠ may collide with IME switchers |
| `[ ]` | G2 | New note in current folder | `⌃⌥⌘N` | `Ctrl+Alt+Win+N` | MUST | — | |
| `[ ]` | G3 | Search | `⌃⌥⌘F` | `Ctrl+Alt+Win+F` | MUST | — | |
| `[ ]` | G4 | Note from clipboard | configurable, no default | `Ctrl+Alt+Win+V` | MUST | — | Noto proposes a default |
| `[ ]` | G5 | Change side | configurable | unbound by default | SHOULD | — | |
| `[ ]` | G6 | Pin window | configurable | unbound by default | SHOULD | — | |

### 7.2 Window / navigation

| Status | ID | Action | SideNotes | Noto (proposed) | Level | Issue | Notes |
| :----: | -- | ------ | --------- | --------------- | :---: | :---: | ----- |
| `[ ]` | G7 | Close panel | `⌘W` | `Ctrl+W` | MUST | — | |
| `[ ]` | G8 | Back / hide (4 modes) | `⎋` | `Esc` | MUST | — | See A12 |
| `[ ]` | G9 | Enter folder | `⌘↓` | `Ctrl+Down` | MUST | — | |
| `[ ]` | G10 | Last-open folder | `⌘⌥O` | `Ctrl+Alt+O` | MUST | — | |
| `[ ]` | G11 | Next note | `⌘⌥↓` | `Ctrl+Alt+Down` | MUST | — | |
| `[ ]` | G12 | Previous note | `⌘⌥↑` | `Ctrl+Alt+Up` | MUST | — | |
| `[ ]` | G13 | Hide (optional binding) | `⌘↩` | `Ctrl+Enter` | SHOULD | — | Off by default; see A11 |

### 7.3 Note management

| Status | ID | Action | SideNotes | Noto (proposed) | Level | Issue | Notes |
| :----: | -- | ------ | --------- | --------------- | :---: | :---: | ----- |
| `[ ]` | G14 | New note / new folder | `⌘N` | `Ctrl+N` | MUST | — | Context-dependent |
| `[ ]` | G15 | Delete note | `⌥⌘⌫` | `Ctrl+Alt+Backspace` ↯ | MUST | — | Order swapped for Windows convention |
| `[ ]` | G16 | Move note to folder | `⇧⌘M` | `Ctrl+Shift+M` | MUST | — | |
| `[ ]` | G17 | Move note up | `⇧⌥⌘↑` | `Ctrl+Alt+Shift+Up` | MUST | — | |
| `[ ]` | G18 | Move note down | `⇧⌥⌘↓` | `Ctrl+Alt+Shift+Down` | MUST | — | |
| `[ ]` | G19 | Fold note | `⌥⌘←` | `Ctrl+Alt+Left` | MUST | — | |
| `[ ]` | G20 | Unfold note | `⌥⌘→` | `Ctrl+Alt+Right` | MUST | — | |
| `[ ]` | G21 | Fold all | `⇧⌥⌘←` | `Ctrl+Alt+Shift+Left` | MUST | — | |
| `[ ]` | G22 | Unfold all | `⇧⌥⌘→` | `Ctrl+Alt+Shift+Right` | MUST | — | |
| `[ ]` | G23 | Set note color 1–6 | `⌘1`…`⌘6` | `Ctrl+1`…`Ctrl+6` | MUST | — | |
| `[ ]` | G24 | Clear note color | `⌘0` | `Ctrl+0` | MUST | — | ⚠ collides with "reset zoom" convention |
| `[ ]` | G25 | Print note | `⌘P` | `Ctrl+P` | SHOULD | — | |
| `[ ]` | G26 | Pin note | none documented | `Ctrl+Shift+P` ↯ | SHOULD | — | Collides with D29 insert-picture — unresolved |

### 7.4 Editing

| Status | ID | Action | SideNotes | Noto (proposed) | Level | Issue | Notes |
| :----: | -- | ------ | --------- | --------------- | :---: | :---: | ----- |
| `[ ]` | G27 | Bold | `⌘B` | `Ctrl+B` | MUST | — | |
| `[ ]` | G28 | Italic | `⌘I` | `Ctrl+I` | MUST | — | |
| `[ ]` | G29 | Header | `⇧⌘H` | `Ctrl+Shift+H` | MUST | — | |
| `[ ]` | G30 | Bullet list | `⌘L` | `Ctrl+L` | MUST | — | |
| `[ ]` | G31 | Ordered list | `⇧⌘L` | `Ctrl+Shift+L` | MUST | — | |
| `[ ]` | G32 | Create / remove task | `⌘T` | `Ctrl+T` | MUST | — | |
| `[ ]` | G33 | Toggle task state | `⌘.` | `Ctrl+.` | MUST | — | |
| `[ ]` | G34 | Clear completed tasks | `⇧⌥⌘T` | `Ctrl+Alt+Shift+T` | MUST | — | |
| `[ ]` | G35 | Insert picture | `⇧⌘P` | `Ctrl+Shift+P` | MUST | — | ⚠ collides with G26 and with command-palette convention |
| `[ ]` | G36 | Indent right | `⌘]` | `Ctrl+]` | MUST | — | Tab also indents in lists |
| `[ ]` | G37 | Indent left | `⌘[` | `Ctrl+[` | MUST | — | Shift+Tab also outdents |
| `[ ]` | G38 | Move line up | `⌘⌥[` | `Alt+Up` ↯ | MUST | — | VS/VS Code convention |
| `[ ]` | G39 | Move line down | `⌘⌥]` | `Alt+Down` ↯ | MUST | — | VS/VS Code convention |
| `[ ]` | G40 | Show / hide markup | `⇧⌘R` | `Ctrl+Shift+R` | MUST | — | ⚠ browser hard-reload muscle memory; in-app only |
| `[ ]` | G41 | Increase font size | `⌘+` | `Ctrl+=` / `Ctrl++` | MUST | — | |
| `[ ]` | G42 | Decrease font size | `⌘−` | `Ctrl+-` | MUST | — | |
| `[ ]` | G43 | Share menu | `⌘↩` | tray / note menu, no chord ↯ | ADAPT | — | No Windows share sheet; see H2 |
| `[ ]` | G44 | Preview image / file | `Space` | `Space` | ADAPT | — | In-app preview; see D35 |

### 7.5 Search window

| Status | ID | Action | SideNotes | Noto (proposed) | Level | Issue | Notes |
| :----: | -- | ------ | --------- | --------------- | :---: | :---: | ----- |
| `[ ]` | G45 | Open search | `⌘F` | `Ctrl+F` | MUST | — | |
| `[ ]` | G46 | Copy note (incl. line 1) | `⌘C` | `Ctrl+C` | MUST | — | |
| `[ ]` | G47 | Copy note (excl. line 1) | `⌘⌥C` | `Ctrl+Shift+C` ↯ | MUST | — | Conventional copy-variant chord |
| `[ ]` | G48 | Launch URL from line 2 | `⌘R` | `Ctrl+R` | SHOULD | — | |
| `[ ]` | G49 | Open containing folder | `⌃`/`⌘ + Click` | `Ctrl+Click` | SHOULD | — | ⚠ provisional — sources differ |

### 7.6 Customization

| Status | ID | Feature | SideNotes behavior | Noto requirement | Level | Issue | Notes |
| :----: | -- | ------- | ------------------ | ---------------- | :---: | :---: | ----- |
| `[ ]` | G50 | Rebind global shortcuts | Record-shortcut UI for the global set | Same, for the global set | MUST | — | |
| `[ ]` | G51 | Escape behavior setting | 4 options | Same 4 options | MUST | — | = A12 |
| `[ ]` | G52 | Rebind in-app shortcuts | Undocumented whether possible | Rebindable, with conflict detection | SHOULD | — | ⚠ provisional — UNKNOWN in SideNotes |

### Collisions with standard Windows shortcuts

```
  CHORD                  COLLIDES WITH                          RESOLUTION
  ─────────────────────  ─────────────────────────────────────  ──────────────────────
  Ctrl+Alt+Win+Space     IME / input-method switchers (varies)  configurable; test first
  Ctrl+0          (G24)  "reset zoom" in almost every app       keep; Noto has no zoom
  Ctrl+Shift+P    (G35)  command palette (VS Code, Explorer-ish) keep; in-app only
  Ctrl+Shift+P    (G26)  ALSO G35 insert-picture — INTERNAL      UNRESOLVED — see below
  Ctrl+Shift+R    (G40)  browser hard reload (muscle memory)     keep; in-app only
  Ctrl+[ / Ctrl+] (G36/7) Esc-equivalent in some terminals       keep; in-app only
  Ctrl+Alt+<key>         AltGr on many non-US layouts            MUST verify on DE/FR/PL
  Ctrl+W          (G7)   "close document" everywhere             correct — it closes the panel
```

**One internal collision is unresolved and must be decided before G26/G35 are
built.** Pin-note and insert-picture cannot both be `Ctrl+Shift+P`. Insert
picture (G35) has the stronger claim — it maps literally from `⇧⌘P` and is
CONFIRMED in SideNotes; pin-note has no documented chord at all. Provisional
resolution: G35 keeps `Ctrl+Shift+P`, G26 takes an unbound-by-default slot.
Recorded in section 13.

**`Ctrl+Alt+<key>` is the systemic risk, not any individual chord.** On German,
French, Polish and several other keyboard layouts, `AltGr` is delivered to
applications as `Ctrl+Alt`, so every `Ctrl+Alt+<letter>` chord can swallow a
character the user was trying to type. Eight rows use it (G10, G11, G12, G15,
G17–G22, G34). This must be tested on non-US layouts during M3, and G52's
rebinding is the mitigation of last resort. SideNotes does not have this
problem because macOS has no AltGr.

**The deviations (↯) are five, and each has one reason.** G15 reorders modifiers
to Windows convention. G38/G39 adopt the editor convention the target audience
already knows. G47 uses the conventional copy-variant chord. G43 has no chord
because it has no Windows equivalent to bind one to.

---

## 8. H — Integration & automation

| Status | ID | Feature | SideNotes behavior | Noto requirement | Level | Issue | Notes |
| :----: | -- | ------- | ------------------ | ---------------- | :---: | :---: | ----- |
| `[ ]` | H1 | Menu bar item | Menu-bar icon + right-click menu | **System tray** icon + context menu | ADAPT | — | = A5, A6 |
| `[ ]` | H2 | Share extension | System share sheet; also a web-address share extension | **Copy / save-as / open-with**, no share sheet | ADAPT | — | Windows has no NSSharingService analogue |
| `[ ]` | H3 | AppleScript API | Full scripting API over folders and notes | **`noto://` URI + CLI (+ PowerShell module)** | ADAPT | — | ADR-010 makes this cheap |
| `[ ]` | H4 | Apple Shortcuts actions | 12+ automation actions | **CLI is the automation surface**; no GUI-flow integration | ADAPT | — | No Windows equivalent worth targeting |
| `[ ]` | H5 | URL scheme | `sidenotes://open/<uuid>`, `add-note-with-text/<text>` | `noto://` with the same two capabilities | MUST | — | Registered protocol handler; ADR-008 |
| `[ ]` | H6 | Alfred workflow | Launcher workflow | **Not in M3** — CLI enables it | DROP | — | Third party writes the plugin, not Noto |
| `[ ]` | H7 | Hookmark | macOS-only linking app | **No equivalent** | DROP | — | Mac-only product |
| `[ ]` | H8 | PopClip | macOS-only | **No equivalent** | DROP | — | Mac-only product |
| `[ ]` | H9 | Dropzone | macOS-only | **No equivalent** | DROP | — | Mac-only product |
| `[ ]` | H10 | Raycast | Primarily macOS | **Not in M3** | DROP | — | Windows version emerging; third-party plugin if ever |
| `[ ]` | H11 | Workspaces | Apptorium's own Mac app | **Not applicable** | DROP | — | Vendor's own product |
| `[ ]` | H12 | MindNode | Mind maps from notes | **No equivalent** | DROP | — | Mac/iOS-only product |
| `[ ]` | H13 | Things | Tasks from notes via Shortcuts | **No equivalent** | DROP | — | Apple-platform-only product |
| `[ ]` | H14 | Drag from other apps | Text, files, folders, images from any app | Same, via Windows OLE drag & drop | MUST | — | Explorer is the main source |
| `[ ]` | H15 | Clipboard behaviors | Note-from-clipboard, quick copy, copy image, code detect, link titles, no auto-space | All six (= B3, E3, D32, D39, D42, D41) | MUST | — | Aggregate row |
| `[ ]` | H16 | Dropshare target | Mac share destination | **No equivalent** | DROP | — | Mac-only product |
| `[ ]` | H17 | Handoff / Continuity | Not documented | **Not applicable** | DROP | — | Apple-only concept |
| `[ ]` | H18 | Services menu | Not documented | **Not applicable** | DROP | — | macOS-only concept |

**Eleven of eighteen rows here are DROP, and that is the correct answer.**
SideNotes' integration surface is largely a list of Mac software — Hookmark,
PopClip, Dropzone, MindNode, Things, Dropshare, Workspaces, Alfred. These are
not features SideNotes built; they are products that exist on macOS and do not
exist on Windows. "Noto lacks Hookmark integration" is not a parity gap, it is
a statement about Hookmark. See [section 11](#11-where-noto-deliberately-differs).

**H3, H4 and H5 collapse into one piece of work.** ADR-010 requires every
mutation to go through a command, so a URI handler, a CLI and a future
automation surface all dispatch the same commands rather than reimplementing
logic per entry point. That is why the automation ADAPT is cheap: the command
layer already has to exist, and the CLI is a thin shell over it. H5 is the MUST
here because it is the only genuinely portable concept of the three; H3 and H4
are ADAPT because the *outcome* (script Noto from outside) is preserved while
the mechanism is entirely different.

**H2 needs honesty about what is lost.** SideNotes' share sheet reaches Messages,
Notes, Reminders and dozens of installed apps with zero per-app work. Windows'
share contract requires packaging and reaches far less. Noto's answer is the
primitives — copy, save as image, save as markdown, open with — which covers
the documented use cases (D38, I4) without pretending to be a share sheet.

---

## 9. I — Import / export / backup / sync

| Status | ID | Feature | SideNotes behavior | Noto requirement | Level | Issue | Notes |
| :----: | -- | ------- | ------------------ | ---------------- | :---: | :---: | ----- |
| `[ ]` | I1 | Import text files | Drop text files, or whole folders, to create notes | Same, incl. bulk folder import | MUST | — | |
| `[ ]` | I2 | Import images | Dropped images embedded; behavior configurable | Same | MUST | — | |
| `[ ]` | I3 | Import as shortcuts | Files become previewed shortcuts, not content | Same; standard / compact styles | MUST | — | = D34 |
| `[ ]` | I4 | Export note to image | Share → save/copy image; background, margins, ratio, bottom bar | Same, with the same four export settings | SHOULD | — | |
| `[ ]` | I5 | Export via Shortcuts | Shortcuts action wrapping image export | Image export via CLI | ADAPT | — | |
| `[ ]` | I6 | Print | `⌘P` | `Ctrl+P` | SHOULD | — | = B21 |
| `[ ]` | I7 | Text / markdown export | **None.** Image export and print only | **Bulk export to markdown files** | BETTER | — | ADR-002; ships in M1 |
| `[ ]` | I8 | Automatic backups | Auto backup to the data folder; 15 / 30 / 45 min; auto-cleanup | Same, incl. the three intervals and cleanup | MUST | — | |
| `[ ]` | I9 | Manual backup | Browse Backups → Make backup; manual backups locked from cleanup | Same, incl. the lock | MUST | — | |
| `[ ]` | I10 | Restore a backup | Restore from a chosen backup | Same, verified in M4 | MUST | — | |
| `[ ]` | I11 | Backups in cloud drive | Backup folder may be iCloud Drive | **User-chosen backup folder** (OneDrive etc.) | ADAPT | — | Noto writes to a folder; the user picks it |
| `[ ]` | I12 | iCloud sync | Automatic cross-device sync; last-write-wins; iCloud canonical | **No sync in v1** — local-first only | ADAPT | — | ADR-002. BYO-folder sync is post-v1 |
| `[ ]` | I13 | Local storage location | SQLite in Application Support; paths vary by channel | SQLite under `%LOCALAPPDATA%`; one location | ADAPT | — | ADR-003; one channel, one path |
| `[ ]` | I14 | Legacy storage format | Pre-1.3 JSON format | **Not applicable** | DROP | — | Noto has no legacy format |
| `[ ]` | I15 | Data portability | Effectively via SQLite + backups | Documented schema + markdown export + open backups | BETTER | — | ADR-002 makes this a contract |

**I12 is the biggest single deviation in this document, and it is decided, not
open.** SideNotes' iCloud sync is a headline feature: automatic, cross-device,
with a documented last-write-wins conflict rule. Noto ships **no sync** in v1.
ADR-002 makes local-first non-negotiable and principle 4 forbids core
functionality requiring an account or network. The roadmap places BYO-folder
sync explicitly post-v1, gated on solving the WAL-under-file-sync hazard first.
This is ADAPT rather than DROP because the underlying user need — "my notes are
safe and I can get at them elsewhere" — is met by I7, I8–I11 and I15: real
export, real backups, a documented schema, and a backup folder the user can
point at OneDrive. It is not the same capability and this document does not
pretend otherwise.

**I7 is the clearest genuine gap in SideNotes and the cheapest to beat.** The
inventory found no documented `.md`, `.txt` or PDF export and no bulk export at
all — data portability is "the SQLite file and the backups." ADR-002 promises
the opposite, and since note content is already stored as markdown source
(ADR-004), export is a file copy. It lands in M1, so it costs M3 nothing.

**I8–I10 are MUST and are tested in M4, not merely implemented in M3.** A
backup system that has never been restored from is not a backup system. The
roadmap's M4 already lists "backup and restore verified" as a hardening item.

---

## 10. J — Preferences

40 documented settings across 10 panes. Principle 9 says preferences are not
free — each is a branch in behavior and a test-matrix entry — so this table is
also the list of every configuration surface M3 is allowed to ship.

| Status | ID | Pane → Setting | SideNotes options | Noto requirement | Level | Issue | Notes |
| :----: | -- | -------------- | ----------------- | ---------------- | :---: | :---: | ----- |
| `[ ]` | J1 | General → Show or Hide Notes | Open Bar / Hot Side / Menubar Icon | Edge handle / Hover / Tray — **independently toggled** | ADAPT | — | Not exclusive modes; see A3/A4 and §12 |
| `[ ]` | J2 | General → Hide Open Bar | never / mouse inactive / always | Same three, **unconditionally available** | BETTER | — | SideNotes gates `always` on other modes |
| `[ ]` | J3 | General → Side | Left / Right (default Right) | Same | MUST | — | = A2 |
| `[ ]` | J4 | General → Launch on Startup | on / off | Same, off by default | MUST | — | = A16 |
| `[ ]` | J5 | General → Close on outside click | on / off | Same | MUST | — | = A13 |
| `[ ]` | J6 | Notes → Quick Formatting Toolbar | above / below / disabled | Same three | SHOULD | — | = D25 |
| `[ ]` | J7 | Folders → Single-click opening | on / off | Same | MUST | — | = C4 |
| `[ ]` | J8 | Creating Notes → Placement | top / bottom / over / under current | Same four | MUST | — | = B5 |
| `[ ]` | J9 | Creating Notes → Ask for folder | on / off | Same | SHOULD | — | = B6 |
| `[ ]` | J10 | Creating Notes → Open last folder on restart | on / off | Same | SHOULD | — | = C14 |
| `[ ]` | J11 | Text → Default text formatting | Markdown / Code (/ plain) | Same | SHOULD | — | = D28 |
| `[ ]` | J12 | Text → Font family | any installed font | Same | MUST | — | = D47 |
| `[ ]` | J13 | Text → Font size | adjustable | Same | MUST | — | = D48 |
| `[ ]` | J14 | Text → Line height | Tight / Normal / Relaxed | Same | SHOULD | — | = D49 |
| `[ ]` | J15 | Text → Paragraph spacing | Tight / Normal / Relaxed | Same | SHOULD | — | = D50 |
| `[ ]` | J16 | Text → File links | standard / compact | Same | MUST | — | = D34 |
| `[ ]` | J17 | Text → Color copy format | output format for `#rrggbb` | Same | SHOULD | — | |
| `[ ]` | J18 | Text → Disable color display | on / off | Same | SHOULD | — | = F14 |
| `[ ]` | J19 | Text → No auto-spaces on paste | on / off | Same | SHOULD | — | = D41 |
| `[ ]` | J20 | Text → Code detection on paste | code block / code note | Same | SHOULD | — | = D39 |
| `[ ]` | J21 | Appearance → Light / Dark / System | manual or follow system | Same | MUST | — | = F1 |
| `[ ]` | J22 | Appearance → Themes → Manage | install, list, reveal | Install, list, reveal in Explorer | SHOULD | — | = F2, F7 |
| `[ ]` | J23 | Appearance → Note colors | Background / Bar | Same | MUST | — | = F10 |
| `[ ]` | J24 | Appearance → Default new-note color | one of six, or empty | Same | SHOULD | — | = B15 |
| `[ ]` | J25 | Shortcuts → Global shortcuts | record 6 global chords | Same 6, rebindable | MUST | — | = G50 |
| `[ ]` | J26 | Shortcuts → Escape behavior | 4 options | Same 4 | MUST | — | = A12 |
| `[ ]` | J27 | Importing → DnD on screen edge | enable / disable | Same | MUST | — | = A15 |
| `[ ]` | J28 | Importing → Show while dragging | enable / disable | Same | MUST | — | = A15 |
| `[ ]` | J29 | Importing → Move imported files to Trash | on / off | Move to **Recycle Bin**, off by default | ADAPT | — | Destructive — never default on |
| `[ ]` | J30 | Importing → Import behavior | images and text files | Same | MUST | — | = I1, I2 |
| `[ ]` | J31 | Exporting → Image background | color or transparent | Same | SHOULD | — | = I4 |
| `[ ]` | J32 | Exporting → Image margins | adjustable | Same | SHOULD | — | = I4 |
| `[ ]` | J33 | Exporting → Image ratio | selectable | Same | SHOULD | — | = I4 |
| `[ ]` | J34 | Exporting → Bottom bar in image | on / off | Same | SHOULD | — | = I4 |
| `[ ]` | J35 | Data → iCloud sync | enable / disable | **Absent** — no sync in v1 | DROP | — | = I12, ADR-002 |
| `[ ]` | J36 | Data → Data locations | reveal data folders | Reveal data folder in Explorer | MUST | — | One location, not two |
| `[ ]` | J37 | Backups → Automatic backups | enable / disable | Same, **on by default** | MUST | — | = I8 |
| `[ ]` | J38 | Backups → Frequency | 15 / 30 / 45 min | Same three | MUST | — | = I8 |
| `[ ]` | J39 | Backups → Backup folder | custom, incl. iCloud Drive | Custom folder, any path incl. OneDrive | ADAPT | — | = I11 |
| `[ ]` | J40 | Backups → Browse backups | make / restore / lock | Same | MUST | — | = I9, I10 |

**Most J rows are the settings-surface half of a row elsewhere** — the `=` in
Notes says which. They are enumerated separately because the settings window is
itself a deliverable with a structure, and because this table is the complete
list of permitted preferences. **A preference not in this table does not ship in
M3 without amending this document.**

**J1 is an ADAPT with real product consequence.** SideNotes treats activation as
three mutually exclusive modes, which is exactly why its `Hide Open Bar =
always` option is only available in two of them, and exactly why users have
asked for years for keyboard-only activation. Noto makes the three surfaces —
edge handle, hover, tray — independently toggleable, so "all off, global hotkey
only" is reachable. That also makes J2's third option unconditional, which is
recorded as BETTER.

**J29 flips a default deliberately.** Moving imported source files to the
Recycle Bin is destructive and touches files Noto does not own. It ships off.

**J35 is absent rather than disabled.** A greyed-out sync toggle would advertise
a capability that does not exist and invite the question every release
(principle 2, principle 6).

---

## 11. Where Noto deliberately differs

Every ADAPT and DROP in one place, with its reason. **None of these is an
omission.**

```
  macOS mechanism            Noto mechanism              Level   Why
  ─────────────────────────  ──────────────────────────  ──────  ────────────────────────────
  menu bar icon              system tray icon            ADAPT   Windows' equivalent surface
  iCloud sync                local-first, no sync in v1  ADAPT   ADR-002, principle 4
  iCloud Drive backups       user-chosen backup folder   ADAPT   OneDrive etc. is just a path
  AppleScript API            noto:// URI + CLI + PS      ADAPT   ADR-010 commands, one impl
  Apple Shortcuts            CLI (evaluate only)         ADAPT   no equivalent worth targeting
  Share Extension            copy / save-as / open-with  ADAPT   no NSSharingService on Windows
  Quick Look (Space)         in-app preview             ADAPT   OS service -> app feature
  macOS spell check          Windows spell-check API     ADAPT   different API, same outcome
  Stage Manager compat       (nothing)                   DROP    no analogue; A19 covers desktops
  Continuity Camera          (nothing)                   DROP    Apple-only device pairing
  macOS Text Replacement     (nothing)                   DROP    OS service, not an app feature
  Alfred workflow            CLI enables third parties   DROP    Noto ships no launcher plugin
  Hookmark / PopClip /       (nothing)                   DROP    Mac-only PRODUCTS — their
  Dropzone / MindNode /                                          absence is a fact about them,
  Things / Dropshare /                                           not a gap in Noto
  Workspaces / Raycast
  Handoff, Services menu     (nothing)                   DROP    Apple-only OS concepts
  trackpad gestures          precision touchpad, opt-in  ADAPT   keyboard path MANDATORY (P7)
  theme gallery website      (nothing in M3)             DROP    a website, not an app feature
```

**On the Mac-only third-party integrations (H7–H13, H16).** Eight of SideNotes'
listed integrations are with products that only exist on macOS. Noto cannot
integrate with software that does not run on Windows, and building Windows
lookalikes of them is not parity — it is eight new products. The CLI (H3/H4)
is the general answer: anyone who wants Noto in PowerToys Run, Flow Launcher or
their own script can build it against a stable command surface. Noto does not
ship those plugins in M3.

**On gestures (A21, B12 pinch).** SideNotes' trackpad gestures are a documented
source of conflict, and the inability to disable the edge trigger is the app's
longest-running complaint. Noto may implement precision-touchpad equivalents
where hardware supports them, but principle 7 is absolute: **every gesture has a
keyboard equivalent, and every gesture is disableable.** No gesture is the only
path to anything.

**On sync (I12).** This is the one ADAPT where the user outcome is genuinely
reduced, and it is stated plainly rather than softened. SideNotes users get
automatic cross-device sync; Noto v1 users get their notes on one machine, with
real export, real backups, and a backup folder they may point at any sync
service. That is a deliberate trade made by ADR-002, not an implementation
shortfall.

---

## 12. Where Noto deliberately exceeds SideNotes

Five BETTER rows, grouped into three themes below. Kept small on purpose —
scope discipline is what makes M3 finishable, and "better than SideNotes" is an
infinite well.

| ID | Gap in SideNotes | Noto | Why it is justified |
| -- | ---------------- | ---- | ------------------- |
| I7, I15 | **No bulk / text / markdown export.** Image export and print only; portability is the SQLite file and backups | Bulk export to markdown files; documented schema; open backup format | **ADR-002 promises the data outlives the app.** Content is already markdown source (ADR-004), so export is a copy. Ships in M1 — costs M3 nothing |
| B7, B23 | **No trash or undo for deleted notes.** Recovery is backups only; a confirmation popover is the only protection | Soft delete + recycle bin + restore | Same promise. A confirmation dialog is not data safety, it is a speed bump. Also ships in M1 |
| J2 (with A3, A4, J1) | **No keyboard-only activation.** Users must accept a visible edge bar or a menu-bar icon; `Hide Open Bar = always` is gated on being in another mode | Every activation surface independently disableable — including all of them at once | **The most-requested missing SideNotes feature in reviews**, and principle 7 states it directly. Costs one settings decision over the parity implementation |

A3, A4 and J1 stay MUST/ADAPT rather than BETTER because the activation
*surfaces* are plain parity. Only J2's unconditional `always`, and the
independence between the surfaces, exceed SideNotes.

**Flat folders are explicitly not a BETTER row.** SideNotes appears to be flat,
one level — INFERRED from documentation absence, not confirmed. Noto matches it
for parity (C3). **Deeper nesting is not a parity requirement and must not be
added speculatively.** It would be a second organizational paradigm (principle 9
defaults to no) and it would change navigation, move, search scoping and URI
shape at once. If it is ever wanted, it is an M7 candidate with its own case.

**The boundary this table defends.** Noto has a long list of things it could do
better than SideNotes; the roadmap puts them in M5–M8 and
`competitor-enhancements.md`, deliberately separate from this document so that
"we promised SideNotes parity" never blurs into "we are making Noto better."
The rows above earn their place because two of the three themes are already M1
work mandated by an ADR, and the third is a settings decision rather than a
feature.

---

## 12a. Keyboard constraint: `Ctrl+Alt` and AltGr

**This is a real defect risk, not a preference, and it has no macOS analogue.**

On German, French, Polish and several other layouts, **AltGr is delivered to
applications as `Ctrl+Alt`**. A global or in-app shortcut bound to
`Ctrl+Alt+<key>` therefore intercepts characters the user is actively typing:

```
  German layout     AltGr+Q  ->  @        <- swallowed by Ctrl+Alt+Q
  Polish layout     AltGr+A  ->  a-ogonek <- swallowed by Ctrl+Alt+A
  French layout     AltGr+E  ->  euro     <- swallowed by Ctrl+Alt+E
```

SideNotes never faced this: macOS uses Option for the same characters, and its
shortcuts use Command.

**Eight rows in section G map a SideNotes chord onto `Ctrl+Alt+<key>`.** Each
must be re-mapped before implementation.

### Rules

1. **No default shortcut uses `Ctrl+Alt+<letter>`.** Prefer `Ctrl+Shift+<key>`,
   `Alt+<key>` or a chord.
2. **Every shortcut is rebindable** — the mitigation of last resort, and
   required anyway by principle 7.
3. **Test on a non-US layout** before shipping the shortcut set. This belongs in
   the M4 gate.

One internal collision is also unresolved: **pin-note and insert-picture both
map to `Ctrl+Shift+P`.** Resolve when G rows become issues.

---

## 13. Open questions

The inventory lists 17 items that no source documents and that must be resolved
by **hands-on testing of SideNotes** before the corresponding rows are final.
Until then those rows are **provisional**: the requirement may be right, but its
fidelity to SideNotes is unverified.

| # | Question | Rows | Effect if the answer differs |
| - | -------- | ---- | ---------------------------- |
| Q1 | **Multi-display: which monitor, does it follow focus?** | A18 | Highest impact. Undocumented in *every* source. Noto must define its own behavior in M2 regardless |
| Q2 | Virtual desktop / Spaces behavior | A19 | Windows Virtual Desktop behavior must be defined either way |
| Q3 | Exact chord for move-note-to-top/bottom | B9 | Chord only; capability is confirmed |
| Q4 | Does note-level duplication exist? | B22 | If no, B22 drops from SHOULD to a Noto extra |
| Q5 | Is there any trash / undo for deletion? | B23 | If yes, the BETTER row in §12 becomes a MUST |
| Q6 | Confirm folders are flat, one level | C3 | If nesting exists, C3 flips from MUST-flat to MUST-nested — a large change |
| Q7 | Folder sorting options | C10 | Which sort keys are parity vs. extra |
| Q8 | Underline syntax and shortcut | D7 | Noto must choose a markdown-safe representation regardless |
| Q9 | Confirm tables are unsupported | D18 | If supported, D18 flips DROP → MUST and contradicts ADR-004's deferral |
| Q10 | Clear-formatting shortcut | D21 | Chord only |
| Q11 | Search: incremental? filters? in-note find? | E8 | Noto requires incremental regardless (principle 3); filters/in-note find stay out unless confirmed |
| Q12 | Open-containing-folder modifier (`⌃` vs `⌘`) | E5, G49 | Chord only |
| Q13 | Window transparency / opacity setting | F11 | If a real setting exists, F11 gains a preference row |
| Q14 | Confirm no text / markdown / PDF export | I7 | If it exists, the §12 BETTER row becomes plain parity |
| Q15 | Are non-global shortcuts rebindable? | G52 | Determines whether G52 is MUST or SHOULD |
| Q16 | macOS Services integration — confirm absent | H18 | Already DROP; confirmation only |
| Q17 | Handoff support — confirm absent | H17 | Already DROP; confirmation only |

**Q1 is the one that actually matters.** Multi-display behavior is undocumented
on every Apptorium page, was noted as absent by the reviewer the inventory
cites, and is the first thing a Windows user with two monitors encounters. No
amount of reading resolves it. Noto's behavior will be decided on its own merits
in M2 and written down — but until SideNotes is tested by hand, A18 cannot claim
to be a parity row at all, only a Noto requirement.

**Q6 and Q9 are the two that could move real scope.** If SideNotes turns out to
support sub-folders or tables, C3 and D18 invert, and both are substantial. Both
are currently INFERRED-from-absence, which is the weakest evidence class in the
inventory. Neither should be built speculatively before testing.

**Rows depending on Q3, Q10 and Q12 are chord-only questions** and do not block
implementation — the capability is confirmed in each case, only the exact
SideNotes keystroke is unknown, and Noto is remapping chords anyway (section 7).

---

## 14. Coverage summary

### Rows by level

```
  MUST     ██████████████████████████████████████████████████████  147
  SHOULD   ████████████████████████                                 66
  ADAPT    ████████                                                 23
  DROP     ████████                                                 23
  BETTER   ██                                                        5
                                                                  ─────
  TOTAL                                                             264
```

| Level | Count | Meaning for M3 |
| ----- | ----: | -------------- |
| **MUST** | 147 | M3 is not complete until all are `[x]` |
| **SHOULD** | 66 | Gaps listed and re-decided at the M4 gate |
| **ADAPT** | 23 | Windows mechanism, same outcome; counted as work |
| **DROP** | 23 | Decisions, never checked, never re-litigated without a reason |
| **BETTER** | 5 | Deliberate excess; 4 of 5 already land in M1 |
| **Total rows** | **264** | Traced to ~273 inventory items (K. pricing / version history / privacy are not requirements) |

### Rows by category

| Cat | Category | Rows | MUST | SHOULD | ADAPT | DROP | BETTER | Primarily lands in |
| --- | -------- | ---: | ---: | -----: | ----: | ---: | -----: | ------------------ |
| A | Sidebar / Workspace | 21 | 16 | 2 | 3 | 0 | 0 | **M2**, verified in M3 |
| B | Note management | 26 | 16 | 7 | 0 | 1 | 2 | M1 domain + **M3** UI |
| C | Folders / organization | 19 | 11 | 5 | 1 | 2 | 0 | M1 domain + **M3** UI |
| D | Editor & content | 51 | 28 | 16 | 3 | 4 | 0 | **M3** — the largest block |
| E | Search | 8 | 4 | 2 | 2 | 0 | 0 | **M3** |
| F | Appearance & themes | 14 | 2 | 8 | 1 | 3 | 0 | **M3**, on M0 tokens |
| G | Keyboard shortcuts | 52 | 42 | 8 | 2 | 0 | 0 | **M3** |
| H | Integration & automation | 18 | 3 | 0 | 4 | 11 | 0 | **M3** (URI/CLI) |
| I | Import / export / backup | 15 | 6 | 2 | 4 | 1 | 2 | M1 export + **M3** rest |
| J | Preferences | 40 | 19 | 16 | 3 | 1 | 1 | **M3** |

### Where the work actually is

```
  M1   domain: soft delete, markdown storage, export, attachments   ── B, C, I
  M2   the panel itself: edge dock, activation, DPI, tray           ── A
  M3   ████████████████████████████████████████  everything else
       editor (D, 51 rows)  ·  shortcuts (G, 52)  ·  prefs (J, 40)
       search (E)  ·  themes (F)  ·  URI + CLI (H)  ·  floating notes
  M4   verification of the above under load, at scale, accessibly
```

Three categories — **D (editor), G (shortcuts), J (preferences)** — are 143 of
264 rows, 54% of the document, and they hold 89 of the 147 MUSTs. That is where
M3 is won or lost. Categories A
(M2) and the M1 slices of B, C and I arrive already checked; they are listed
because M3's exit criterion reads the whole table, not because M3 builds them.

**Floating notes** appear in the roadmap's M3 list but have no inventory row —
SideNotes has them, the inventory did not enumerate them as a numbered feature.
Under ADR-009 they are a presentation kind, not a domain change. They are M3
work governed by the roadmap, not by a row here; if hands-on testing produces
SideNotes-specific floating-note behaviors, they get rows in a revision of this
document.

---

## Revision rules

1. **Adding a row requires an inventory reference.** If it is not in the
   inventory, it is not parity — file it against M5–M8 instead.
2. **Changing a `MUST` to a `SHOULD` requires a reason recorded here**, not a
   quiet edit.
3. **Resolving an open question updates the affected rows and removes the
   `⚠ provisional` mark** in the same change.
4. **A `DROP` is re-opened only with new evidence**, not with a preference.
5. This document and [ADR-004](../decisions/ADR-004-markdown-content.md) must
   stay consistent on invisible markdown; if they diverge, the ADR is reviewed
   first (principle 11).
