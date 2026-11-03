# ADR-017 — The editor model: hidden markup over the source

**Status:** Accepted: the architecture (§1–§4). **Provisional:** the long-note constraints (§5). **Release-candidate gates:** §6 and the list under Verification.
**Date:** 2026-11-03
**Related:**
- ADR-004 (markdown content, invisible markdown), ADR-010 (commands and events), ADR-011 (design system), ADR-016 §3 (the `NoteEditorHost` seam)
- `docs/roadmap/basic-release.md` Phase C; `docs/roadmap/implementation-plan.md` P2, §4.1, §10 (shortcut map), risk R2
- Evidence: [the hidden-markdown spike](../architecture/spikes/hidden-markdown.md)

---

## Context

ADR-004 fixes the format: markdown is stored and is the truth, and SideNotes-style invisible markdown, with markup hidden while you type, is a parity requirement. ADR-016 fixes the boundary: `NoteEditorHost` is the only thing that edits text. Outside it there are `Load`, `Markdown`, `ContentChanged`, `LeaveRequested` and `PlaceCaret`, and P2 replaces only its inside.

The implementation plan places the model in Core (a markdown model plus pure edit transforms), the save policy in UseCases, and only "applies edits and spans, no rules" in the host. It names the editor its largest risk (R2).

Before this decision, nothing showed that a WinUI 3 `RichEditBox` could do it, or how fast. The spike measured it.

## Problem

How does the editor show live-styled markdown with its markup hidden, so that:
- the markdown stays the truth;
- every edit is a pure Core transform;
- the WinUI control never owns the model;
- caret, undo, UI Automation, IME and performance all hold?

## Options considered

| Option | What the control holds | Verdict |
|---|---|---|
| **A. Same text, hidden markup** | The markdown source, character for character. Markup characters carry the `Hidden` format | **Chosen** |
| B. Visible text plus an offset map | Only the visible text; Core maps every visible offset to a source offset and back | Rejected |
| C. A custom-drawn text control | Glyphs drawn by Noto (DirectWrite / Win2D) | Rejected |
| D. A web editor in WebView2 | CodeMirror or ProseMirror | Rejected |
| E. The plain `TextBox`, permanently | The source, unstyled | Rejected as the end state (ADR-004). A plain-source fallback for long notes is only a provisional constraint (§5) |

## Decision

This ADR decides three different kinds of thing, and only the first is settled:

| Category | What | Status |
|---|---|---|
| **Accepted architecture** | Markdown source preserved character for character (§1); hidden-markup RichEdit model (§2); Core/host separation and Noto's own undo (§3); incremental formatting (§4) | **Accepted.** Changed only if later evidence shows the model is fundamentally unsuitable |
| **Provisional implementation constraints** | The 400-markup-span threshold; the long-note plain-source fallback; the exact performance behaviour of large notes (§5) | **Provisional.** Derived from the spike's synthetic, markup-dense corpus. Revalidated during the real editor implementation and the RC performance campaign. Not a product requirement |
| **Release-candidate gates** | Narrator behaviour (§6); IME and `Win+.`; spell check; High Contrast; reduced motion; realistic large-note performance | **Open.** Each is decided by testing the real user experience in the RC campaign, not by the spike |

A spike limitation does not become the product specification by being written down here.

### 1. The document is the source

The host is a `RichEditBox` inside `NoteEditorHost`; the seam does not change.

| Rule | Why (spike evidence) |
|---|---|
| The note is loaded with line breaks normalised to LF: CRLF and a lone CR each become `\n` | RichEdit stores one character per break, so the LF source and the document have the same offsets (finding 2) |
| The text is read back with `GetText(TextGetOptions.UseLf)`, never `NoHidden` | It returns exactly the source, hidden markup included (finding 1) |
| Stored markdown is LF from the first P2 save | Today's `TextBox` stores lone CRs. Both read identically (`NoteTitle` already treats `\r`, `\n` and `\r\n` as one break), and LF is what every other tool expects of a markdown file |
| Nothing may put a character into the document that is not in the source: no RichEdit link fields, no list numbering, no embedded objects | Any of them breaks the 1:1 text, so the source would no longer be the document. Links are styled text that Noto opens itself (§3). Images wait for P3.0's own ADR |

### 2. Markup is hidden by format, never removed

- Markup ranges get `CharacterFormat.Hidden = On`. The caret skips them, Backspace deletes the visible character, and a typed character inherits the visible format beside it. RichEdit does all of this itself (findings 4–6).
- **Show markup** (G40, `Ctrl+Shift+R`) restyles markup as dimmed instead of hidden. The text is the same; only the format changes.

### 3. Core owns the model; the host applies it

```
            Core (pure, dependency-free)                    Windows: NoteEditorHost (no rules)
  ┌──────────────────────────────────────────┐     ┌────────────────────────────────────────────┐
  │ MarkdownDocument.Parse(text)             │     │ Load: spans → one RTF string → one SetText │
  │   → blocks, inline spans, markup ranges  │────▶│ Restyle a block: spans → TOM (batched)     │
  │                                          │     │                                            │
  │ EditCommands.X(text, selection)          │     │ A chord (§10) → the Core command → apply   │
  │   → TextEdit? (null: not applicable)     │────▶│   its TextEdit to the document              │
  │                                          │     │                                            │
  │ TextDiff.Between(before, after)          │◀────│ TextChanging (IsContentChanging) → read    │
  │   → the TextEdit the user just made      │     │   GetText(UseLf), report the change        │
  │                                          │     │                                            │
  │ EditHistory: undo / redo of TextEdits    │◀───▶│ Ctrl+Z / Ctrl+Y / Ctrl+Shift+Z             │
  └──────────────────────────────────────────┘     └────────────────────────────────────────────┘
```

**The contract** (types are fixed here; P2.2–P2.5 implement them):

| Type | Shape | Meaning |
|---|---|---|
| `TextSelection` | `(int Anchor, int Active)` | UTF-16 offsets into the LF source; `Anchor == Active` is a caret |
| `TextEdit` | `(int Start, int Length, string Replacement, TextSelection After)` | Replace `[Start, Start + Length)` with `Replacement`, then select `After`. Every change to a note's text, typed or commanded, is one of these |
| Edit command | `static TextEdit? Command(string text, TextSelection selection)` | Pure. `null` when it does not apply, and then nothing happens. Every D-row command (bold, heading, list, task, indent, move line, …) is one |
| `MarkdownDocument` | parsed from a string | Blocks with source ranges; inline spans `(kind, range)`; markup ranges. Hand-written for SideNotes' subset (plan §4.1); Markdig stays the fallback |
| `TextDiff.Between` | `(before, after) → TextEdit` | The common prefix and suffix. 0.43 ms to read 100 KB back, so a keystroke costs nothing measurable |
| `EditHistory` | a stack of applied `TextEdit`s and their inverses | Units: one command; one run of typing (broken by whitespace, a caret move or a pause); one IME composition; one paste |

**The host's whole job**, and nothing more:
- apply a `TextEdit`;
- report a user's change as a `TextEdit`;
- turn Core's span kinds into design tokens (Noto.UI) and apply them;
- route the editor chords of §10 to Core commands.

The host decides no rule; a guard test enforces this (P2.5).

**RichEdit's undo is off.** `UndoLimit = 0`, and `Ctrl+Z`, `Ctrl+Y` and `Ctrl+Shift+Z` go to `EditHistory`. RichEdit records Noto's restyles as undo steps, so its `Ctrl+Z` would undo a restyle instead of the user's typing (finding 8). D55 wants undo grouped by command anyway, and only Core knows what a command is.

**RichEdit's formatting chords are off.** `DisabledFormattingAccelerators = All`; Noto owns the chord map (§10; finding 7).

**A format-only change is ignored.** `TextChanging.IsContentChanging` is false for Noto's own restyles, so they are never reported as edits (finding 10).

### 4. Styling: as few calls as possible

Each formatting call costs 0.2–0.4 ms, and more as the note grows; nothing in the control changes that. Typing itself costs about 1 ms or less at any size. So:

| When | What the host does | Measured (grow layout) |
|---|---|---|
| Editing starts | Parse; write the spans as one RTF string; one `SetText(FormatRtf)`. The text is unchanged (finding 1) | 6.8 ms (541 chars) · 34 ms (2.6 KB) · 79 ms (5 KB) |
| A keystroke inside a block whose span structure is unchanged, away from a span boundary | **Nothing.** The typed character already has the right format (finding 6) | 0.4–0.6 ms (RichEdit's own insert) |
| A keystroke that changes the block's spans, or lands on a span boundary | Re-parse the block; reset and restyle it through TOM, batched. A fence change restyles to the next fence | 1.7–4.7 ms median, ≤ 7.7 ms max up to 5 KB |
| A command | Apply its `TextEdit`, then the same block rule | as above |
| An IME composition | **No restyle while it is active** (`TextCompositionStarted` … `Ended`); one restyle when it ends | — |

### 5. Long notes (PROVISIONAL: an implementation constraint, not a product decision)

**What the spike measured.** On a synthetic corpus of dense markdown (about one span per 12 characters, the worst case):
- styled load: 79 ms at 5 KB, 174 ms at 10 KB, 2.8 s at 100 KB;
- restyling a 100 KB note progressively, in RTF slices, kept the text and the caret, but blocked the UI thread for 230–409 ms per 3 KB slice;
- span-by-span TOM took 35 s;
- plain source loaded 100 KB in 140–324 ms (today: 479 ms) and typed at about 1 ms.

**The working constraint until better evidence exists:**
- a note is live-styled when it holds at most **400 markup spans** (`StyledSpanLimit`), about 5 KB of that dense corpus;
- above it, the note opens as plain source with the same editor and commands;
- the mode is chosen when editing starts and does not change under the caret;
- the read view follows the same rule.

Both the number and the fallback are **provisional**. They are an engineering boundary from one synthetic corpus on one machine, not v0.1 behaviour.

**What the real editor work must establish** (the parser, P2.2, and the rich host, P2.5, each measure it; the RC performance campaign confirms it):

| Question | How |
|---|---|
| How often is the threshold actually reached? | Span counts over a corpus of realistic notes: short lists, meeting notes, long-form prose, code-heavy snippets, checklists, pasted documents. Not only the synthetic dense corpus |
| Do normal long-form notes stay fully styled? | The same corpus, loaded through the real host, on the RC reference machine |
| Is a better incremental strategy possible? | Candidates: styling only what is visible plus a margin, with off-screen text unstyled until it scrolls in; cheaper restyle units; fewer runs per span; work off the UI thread wherever the control allows. Each one measured, not assumed |
| Can the fallback be avoided for practical notes? | It must be, wherever the measurements allow |
| What is the real user-visible budget? | Set from those measurements against quality-gates.md (note switch, large-note render, responsive typing), not from this spike alone |

**What the real host measured (the editor slice, 2026-11-03).** On a frozen Release build, notes built from realistic, markup-heavy content (about 95 spans per 1,000 characters):

| Note | First frame after opening | Work per keystroke |
|---|---|---|
| 5 KB | 18 ms | 3–12 ms |
| 20 KB | 151 ms | 3.5–4 ms |
| 50 KB | 152 ms | 4–15 ms |
| 100 KB | 309 ms | 4–9 ms |

Today's plain TextBox takes 479 ms for 100 KB. **No fallback is implemented: every note is styled.** The constraint stays provisional only until the RC reference machine confirms these numbers. Scrolling a very long note is not yet measured.

**The rule meanwhile:** a normal note is never silently degraded from polished rendered editing to raw markdown merely because it is long. If any fallback survives into v0.1, it is an intentional, documented UX decision, validated on representative real-world notes, with its own visible presentation, and it gets its own entry in this ADR.

### 6. Accessibility (RELEASE-CANDIDATE GATE)

**The finding (spike finding 11):** RichEdit's UI Automation `TextPattern` exposes the underlying markdown source, markup included. This is a fact about the control, not an accepted v0.1 behaviour.

**The release decision, made in the RC campaign by testing Narrator over styled notes:**
- **if** Narrator reading the source markdown is understandable and usable, the behaviour is documented and accepted;
- **if** it is noisy or confusing, it is a **v0.1 accessibility defect**. It is then investigated and fixed with whatever preserves §1's markdown-source invariant:
  - an accessible projection, an automation peer or custom text provider that reports the visible text;
  - Show markup on whenever a screen reader is active (SPI_GETSCREENREADER);
  - another architecture that keeps the invariant.

The spike classed a custom text provider as high risk. That is an implementation preference, not a reason to reject the investigation: the requirement is the user's experience.

**Also RC gates:**
- **Spell check** stays on (`IsSpellCheckEnabled`); whether hidden markup disturbs it is checked visually.
- **IME and `Win+.`:** RichEdit is TSF-native, and §4's composition rule keeps Noto out of the way. Not exercised here: no IME is installed.
- **High Contrast and reduced motion:** the styling uses theme tokens only, and is verified in the RC campaign.

**Unchanged:** `Esc`, `Tab`, `Ctrl+Alt+Up/Down` and the other leave and navigation keys are the host's, exactly as in P1 (ADR-016 §5).

### 7. Order of the remaining Phase C work (implementation plan §8, unchanged)

```
P2.1 continuous persistence ──▶ P2.2 MarkdownDocument ──▶ P2.3 formatting commands ──▶ P2.4 structural editing
  (needs only the seam)            (Core, pure)              (on the plain editor)        (indent, move line, tasks)
                                                                                                │
P2.9 quick formatting ◀── P2.8 modes, spell ◀── P2.7 paste ◀── P2.6 hidden markup ◀── P2.5 RichEditBox host,
                                                                   + toggle              rendering, EditHistory, links
```

P2.3 and P2.4 run on today's plain editor through the same `TextEdit` contract, so they are usable, and fully unit-testable, before the rich host exists.

### 8. P2.1 precisely: continuous persistence (2026-11-03)

Parity B19: notes save themselves; there is no save command. Before P2.1, text was written only when the user left a card, hid or closed the window, or the session ended. A crash or a kill lost everything typed since.

| Rule | Detail |
|---|---|
| **When** | After `Autosave.Quiet` (1 s) without a change. Never later than `Autosave.Longest` (5 s) after the text first became unsaved, however steadily the user types. `Autosave.Delay` (UseCases, pure) is the whole policy |
| **How** | The same save as leaving a card (`TrySaveCard`): one `UpdateNoteContent` dispatch. The card keeps editing |
| **Explicit saves stay** | Leaving, another card, Ctrl+N, move, fold, hide, close and session end still write at once. Any attempt takes the pending write's place |
| **Text typed back to what is stored** | Nothing is written |
| **Failure** | As every save: the text is kept, the card keeps editing, the notice says why. A failed attempt restarts the bound, so a note that cannot be saved is retried at the normal pace, not on every key. No timer overwrites a failed close's "close again to discard" notice |
| **No new UI** | No Save button, no save command, no "saved" indicator (B19) |
| **Durability** | Each write is one committed SQLite transaction (WAL, `synchronous = NORMAL`, `NotoDatabase`). It survives Noto crashing or being killed; a power loss can lose the latest transactions. A crash or kill loses at most the last 5 s of steady typing, or 1 s after a pause |
| **Cost** | One save measured 26–27 ms for short and 100 KB notes (quality-gates.md, #11). That is at most once a second after a pause, and once every 5 s while typing steadily. Each write moves `UpdatedAt`; nothing orders by it |

**Not settled here (later work, not P2.1's):**
- A write while an IME composition is open stores the composition text as the editor reports it; the next write corrects it. The rich host (P2.5) keeps §4's composition rule.
- A database held by another process blocks a write for up to `busy_timeout` (5 s), as it already does for every save. That is P10's storage-fault work.

**Verification:**
- `AutosaveTests`: the bounds, the delay at every boundary, steady typing landing on 5 s.
- The stack guards.
- Runtime, on a frozen build:
  - `Invoke-EditorSaveOnClose.ps1` cases 7 (written while editing; a kill loses none of it; the integrity check; a clean restart) and 8 (the 5 s bound under steady typing);
  - `Invoke-NoteStack.ps1` cases 8, 9, 32 and 40, which now expect the write;
  - every harness that forces a failed save bins the note before typing, so autosave cannot write first.

Parity B19 stays `[ ]` until the RC re-score (implementation plan, decision 4).

### 9. The editor slice precisely (2026-11-03)

What the first user-visible editor holds, and the decisions the code made that this ADR did not already:

| Decision | Why |
|---|---|
| A card's **read view renders** the same parse (`MarkdownReadView`): markup left out, a bullet and a text-style ☐/☑ drawn for list markers and boxes; plain text above 20,000 characters | A stack you scan should read like notes, not source. In high contrast it sets no colour of its own: on a selected row that is HighlightText, and GrayText there measured 1.37:1 |
| The editor answers **UI Automation's Value pattern** with the source (`EditorBoxAutomationPeer`) | RichEditBox exposes only Text; the TextBox it replaced had Value too, and every validation harness reads and writes the note through it |
| **Caret keys are marked handled** after RichEdit moves the caret | Measured: an unhandled Ctrl+End reached the stack's ListView, which moved its selection and edited the last card |
| A command, Enter or paste **first reads back the last keystroke** (`Flush`) | Measured: RichEdit raises TextChanged after the key, so Enter right after typing continued the list one character early |
| An editor **attaches on data-context change** as well as on load | Measured: a recycled container realised with the previous card's data loaded nothing when the editing card arrived |
| **One Format button** on the editing card's bar, a menu of every command with its key; Enter continues and ends lists; Shift+Enter is a plain line break; paste is plain text | Keyboard-first with one quiet affordance; formatted paste is P2.7's |

Still to come (editor polish, §10): Tab to indent list items and Alt+Up/Down to move lines (P2.4), a rendered checkbox inside the editor, formatted paste (P2.7). The IME, spell check and Narrator RC gates of §6 stay RC gates.

### 10. Editor polish precisely (2026-11-03)

| Decision | Why |
|---|---|
| **Tab** in a list item indents it by its marker's width; **Shift+Tab** removes one tab or up to that width of spaces. Outside a list, or a selection that is not all list items, Tab moves focus as before | Nested text lines up under its parent's text, as CommonMark reads it. The P1 contract (Tab never types a tab) holds |
| **Alt+Up/Down** move the selected lines; a selection ending at a line's start does not take that line. **Ctrl+Alt+Up/Down** still leave for another card: they are checked first | G38, G39, and ADR-016 §5 unchanged |
| A task's **checkbox is drawn in the editor**: its marker and box stay in the text, drawn in the editor's own surface colour so they keep their width, and a glyph overlay (`TaskMarks`) draws ☐/☑ over them. The overlay takes no input and is `Raw` to UI Automation. A click with no selection on the drawn box toggles the task through `ToggleTaskDone`; a click on the text or a drag never does | The source never changes shape (§1), offsets stay 1:1, and toggling is the same Core command as Ctrl+. and the menu. With Show markup on, or above 300 tasks, no overlay is drawn and the source boxes show, dimmed. Narrator still reads the source box: §6's RC gate is unchanged |
| **Ctrl+V** pastes clipboard HTML as Markdown (`HtmlToMarkdown`, Core): the §2 subset only, links kept only for http, https, mailto and file, script and style dropped, nothing fetched. With no HTML, or with **Ctrl+Shift+V**, the text is pasted plain. A clipboard held by another app makes the paste do nothing | A paste from a browser keeps its bold, links, lists and headings as Markdown, never as foreign markup. One undo step, as every edit |
| A heading that is not the note's first line has **8 pt above it** | Sections read as sections; the load and the restyle set the same space |
| **A bounded-shutdown safeguard, not a fix**: once the window has closed and every resource is released, `ExitWatchdog` gives the framework's teardown 5 s, then logs `ShutdownOverran` and kills the process (`Process.Kill`, exit code -1, so an overrun is visible to whatever started Noto, not only in the log) | The one exit hang seen was after the last trace, in Windows App SDK teardown. **Its root cause is unknown**; it did not recur in 60 measured cycles (`Invoke-ExitCycles.ps1`: median 121–185 ms, max 222 ms across builds; a dump is written on any overrun). One harness defect was found and fixed (its debug-output listener left the DBWIN objects open, so every later OutputDebugString blocked 10 s), but that does not explain the hang. The investigation stays open as an RC item (implementation plan R17) |
| **Nothing is unsaved when the watchdog arms** | Every write is synchronous on the UI thread. The close first reads back any keystroke not yet reported, then saves (or the user has chosen to discard, or the close is cancelled and the window stays). `Closed`, and with it the watchdog, comes only after that. On `Closed`, `ReleaseResources` closes the database's pooled connections (`NotoDatabase.Release`: the final WAL checkpoint, no handle left) before the mutex is released, and only then is the watchdog armed. No timer or background task writes after `Closed`. Guards: `EditorHostGuardTests`, `TrayLifetimeGuardTests`; the release: `PersistenceTests`; the watchdog's own behaviour: `ExitWatchdogTests` |
| **Every save reads back the last keystroke first** (`ReadBack`) | Measured (`Invoke-Editor.ps1` case 20): a key and Escape in one input batch left the card without the key, because RichEdit reports the change after the next key. Leave, hide, autosave, session end and close all save through one path, which now reads back first |

**Measured, open (performance campaign):** in a 100 KB note, Ctrl+End takes 920–964 ms from the key to the caret, and Ctrl+Home 461–481 ms (`Invoke-Editor.ps1` case 19; one UI Automation poll is 7–10 ms of that). The build before this slice measures the same, so it is not a regression. Its cause is not yet established; §5's large-note work owns it.

## Rationale

- **Option A is the only one where the source never stops being the text.** Fidelity, offsets, caret and typing all come from RichEdit itself (spike findings 1–6). B needs a second text kept consistent through every IME composition, autocorrect, paste and UI Automation read; one mapping slip is data loss. A cannot slip, because there is nothing to map.
- **Pure transforms make the host replaceable** (R2). If `RichEditBox` ever fails a gate, only the host is rewritten; commands, history, parser and tests stay.
- **The measurements are recorded, not generalised.** Each number in §4 and §5 is in the spike's raw output. §4's cost model holds the architecture; §5's numbers are one synthetic corpus on one machine, so they stay provisional until real notes and the RC reference machine are measured.

## Consequences

**Positive:**
- markdown stays the truth in storage, in the editor and in exports;
- every editing rule is a Core unit test;
- typing stays at RichEdit's own speed at any size;
- the seam and its callers are untouched.

**Negative:**
- Noto writes and owns undo and redo;
- every restyle is deliberate, with no free "style everything" pass;
- large-note behaviour is not settled (§5): the real editor work must measure it on realistic notes before v0.1;
- screen-reader behaviour is not settled (§6): the RC Narrator pass decides it, and a noisy result is a v0.1 defect;
- stored text moves from CR to LF on each note's first P2 save. The text itself is unchanged, but it is a write.

**Guards (added with the code they protect, P2.5):**
- no file but the host references the inner `RichEditBox`;
- the host sets `UndoLimit = 0` and `DisabledFormattingAccelerators = All`, and never calls RichEdit's `Undo` or `Redo`;
- the host never uses `Link`, list numbering or embedded objects;
- the host never reads text with `NoHidden`;
- no editing rule lives in Noto.Windows.

## Alternatives rejected

| Option | Why not |
|---|---|
| **B. Visible text plus an offset map** | Two texts and a mapping to keep correct through IME, autocorrect, paste, undo and UI Automation; every caret and selection translated both ways. The spike shows A gets 1:1 offsets free |
| **C. Custom-drawn control** | Text accessibility, IME/TSF, spell check, selection and caret all hand-built: the R2 risk in full |
| **D. WebView2 editor** | ADR-004 forbids a web view for notes. It puts a browser engine, and its memory, into a background utility (principle 9) |
| **E. Plain `TextBox` forever** | ADR-004 rejects it as the end state |
| RichEdit's own undo, made to skip restyles | WinUI's `RichEditTextDocument` has no way to suspend undo recording; its only lever, `ClearUndoRedoHistory`, would erase the user's history with each restyle |
| Progressive styling of long notes **in RTF slices**, as measured | 230–409 ms of UI-thread blocking per slice (§5). Other incremental strategies are not rejected: §5 requires them to be measured |

## Verification

**Each later Phase C PR: cheap gates, plus:**

| PR | Its own gate |
|---|---|
| P2.2 | Parser round-trip and span tests over SideNotes' subset and the spike's fidelity corpus; parse of 100 KB ≤ 10 ms; **span counts over a realistic-note corpus** (§5) |
| P2.3, P2.4 | One pure test per command, including a "not applicable" case; mutation testing |
| P2.5 | A focused runtime check on a frozen build that re-measures §4's numbers and answers §5's questions on the realistic corpus, including at least one better incremental strategy; the guards above; mutation testing of `EditHistory` and `TextDiff` |

**RC campaign (release-candidate gates):**
- Narrator listening over styled notes (decides §6);
- a Japanese IME and `Win+.` in a styled note;
- spell check with markup hidden;
- High Contrast and reduced motion;
- realistic large-note performance on the reference machine (confirms or removes §5's constraints).

## Future reconsideration criteria

Revisit if:
- §6's Narrator gate fails. That is a defect to fix within §1's invariant, not a reason to accept the noise;
- realistic notes or a better incremental strategy remove the need for §5's fallback. It is then deleted, not kept as a precaution;
- a Windows App SDK update changes formatting-call costs. The spike reruns in minutes;
- the real host (P2.5) shows that the model itself, not only its tuning, cannot meet the v0.1 bar. That, and only that, reopens §1–§4;
- P3's images need embedded objects. That needs its own ADR (P3.0) and must keep §1's "nothing not in the source" or replace it explicitly.

Do **not** revisit to make the control the model, or to hold a second, visible-only text. Both are what this ADR exists to prevent.
