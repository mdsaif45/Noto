# ADR-017 — The editor model: hidden markup over the source

**Status:** Accepted
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
| E. The plain `TextBox`, permanently | The source, unstyled | Rejected as the end state (ADR-004); kept as the fallback for long notes (§5) |

## Decision

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

### 5. Long notes open as plain source

A note is live-styled only when it holds at most **400 markup spans** (`StyledSpanLimit`, provisional). The span count is Core's: the spike's parse costs 2.6–4.2 ms per 100 KB.

- That is about 5 KB of markup-dense text and much more of ordinary prose. Here it loads styled in 79 ms; a laptop at half this machine's speed stays inside the 200 ms large-note budget.
- Above the limit, the note opens as **plain source**: markup visible, no styling, the same editor and commands. The card's bottom bar says so. The 100 KB note loads in 140–324 ms, against today's 479 ms, and types at about 1 ms.
- The mode is chosen when editing starts and stays until the editor is left: a note that grows past the limit while it is edited does not change under the caret.
- The read view uses the same limit. A styled `RichTextBlock` takes about 0.5 ms to build for a short note, but about 500 ms to first frame at 100 KB.

**Why not progressive styling of a long note:** restyling a 100 KB note in RTF slices keeps the text and the caret, but blocks the UI thread for 230–409 ms per 3 KB slice, 12.6 s in all. Span-by-span TOM takes 35 s.

### 6. Accessibility

- **UI Automation exposes the source, markup included** (finding 11). This is accepted for P2:
  - what is heard is exactly what is edited, and caret offsets agree;
  - hiding it would mean a custom `ITextProvider`, a full reimplementation of text accessibility, which R2 forbids.
  - Narrator listening in the RC campaign decides whether this ships; the reversal path is under "Future reconsideration".
- **Spell check stays on** (`IsSpellCheckEnabled`); whether hidden markup disturbs it is an RC visual check.
- **IME and `Win+.`**: RichEdit is TSF-native, and §4's composition rule keeps Noto out of the way. Not exercised here: no IME is installed (RC).
- `Esc`, `Tab`, `Ctrl+Alt+Up/Down` and the other leave and navigation keys are the host's, exactly as in P1 (ADR-016 §5).

### 7. Order of the remaining Phase C work (implementation plan §8, unchanged)

```
P2.1 continuous persistence ──▶ P2.2 MarkdownDocument ──▶ P2.3 formatting commands ──▶ P2.4 structural editing
  (needs only the seam)            (Core, pure)              (on the plain editor)        (indent, move line, tasks)
                                                                                                │
P2.9 quick formatting ◀── P2.8 modes, spell ◀── P2.7 paste ◀── P2.6 hidden markup ◀── P2.5 RichEditBox host,
                                                                   + toggle              rendering, EditHistory, links
```

P2.3 and P2.4 run on today's plain editor through the same `TextEdit` contract, so they are usable, and fully unit-testable, before the rich host exists.

## Rationale

- **Option A is the only one where the source never stops being the text.** Fidelity, offsets, caret and typing all come from RichEdit itself (spike findings 1–6). B needs a second text kept consistent through every IME composition, autocorrect, paste and UI Automation read; one mapping slip is data loss. A cannot slip, because there is nothing to map.
- **Pure transforms make the host replaceable** (R2). If `RichEditBox` ever fails a gate, only the host is rewritten; commands, history, parser and tests stay.
- **The limits are measured, not guessed.** Each number in §4 and §5 is in the spike's raw output, and §5's limit is a named constant re-measured on the RC reference machine.

## Consequences

**Positive:**
- markdown stays the truth in storage, in the editor and in exports;
- every editing rule is a Core unit test;
- typing stays at RichEdit's own speed at any size;
- the seam and its callers are untouched.

**Negative:**
- Noto writes and owns undo and redo;
- every restyle is deliberate, with no free "style everything" pass;
- long notes are not live-styled: a visible, honest limit, and an exception to ADR-004's "parity requirement" bounded by measurement;
- screen readers hear markup until the RC decides otherwise;
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
| **E. Plain `TextBox` forever** | ADR-004 rejects it as the end state. It stays as §5's fallback, which is the same editor with styling off |
| RichEdit's own undo, made to skip restyles | WinUI's `RichEditTextDocument` has no way to suspend undo recording; its only lever, `ClearUndoRedoHistory`, would erase the user's history with each restyle |
| Progressive styling of long notes | Measured: 230–409 ms of UI-thread blocking per slice (§5) |

## Verification

**Each later Phase C PR: cheap gates, plus:**

| PR | Its own gate |
|---|---|
| P2.2 | Parser round-trip and span tests over SideNotes' subset and the spike's fidelity corpus; parse of 100 KB ≤ 10 ms |
| P2.3, P2.4 | One pure test per command, including a "not applicable" case; mutation testing |
| P2.5 | A focused runtime check that re-measures §4's numbers, and §5's at the limit, on a frozen build; the guards above; mutation testing of `EditHistory` and `TextDiff` |

**RC campaign:**
- Narrator listening over styled notes (decides §6);
- a Japanese IME and `Win+.` in a styled note;
- spell check with markup hidden;
- High Contrast styling;
- §5's limit re-measured on the reference machine.

## Future reconsideration criteria

Revisit if:
- Narrator listening finds markup noise unacceptable. The path is an automation peer that reports the visible text, or Show markup on whenever a screen reader is active (SPI_GETSCREENREADER);
- a Windows App SDK update changes formatting-call costs. The spike reruns in minutes, and §5's limit moves with it;
- the RC reference machine misses §4's or §5's budgets. The limit drops, it does not disappear;
- P3's images need embedded objects. That needs its own ADR (P3.0) and must keep §1's "nothing not in the source" or replace it explicitly.

Do **not** revisit to make the control the model, or to hold a second, visible-only text. Both are what this ADR exists to prevent.
