# Spike — Hidden markdown over RichEditBox

**Phase:** Basic Release Phase C, design gate (implementation plan P2.0)
**Date:** 2026-11-03
**Status:** Complete
**Verdict:** **GO for the model.** The document text can stay the markdown source, character for character, with the markup hidden. Large-note performance and screen-reader output are open questions, not settled limits: ADR-017 §5 and §6.
**Decision:** [ADR-017](../../decisions/ADR-017-editor-model.md)
**Code:** [`spikes/HiddenMarkdown/`](../../../spikes/HiddenMarkdown/) · raw output: [`spike-results.sample.txt`](../../../spikes/HiddenMarkdown/spike-results.sample.txt)

---

## Objective

ADR-004 makes markdown the truth and hidden markup ("invisible markdown") a parity requirement. The implementation plan names the editor as the largest single risk (R2): hidden markdown over `RichEditBox` must keep the markdown as the truth, keep IME, UI Automation and spell check working, keep a sane undo while text is restyled from code, and stay fast.

> **Can a WinUI 3 `RichEditBox` show a note with its markup hidden while its text stays the markdown source, within the performance budgets, with sane caret, undo and UI Automation behaviour?**

## Environment

| | |
|---|---|
| OS | Windows 11, build 26300 |
| Runtime | .NET 9.0.6, 64-bit, 20 logical CPUs |
| Windows App SDK | 2.5.1 (the app's pinned version), unpackaged, self-contained |
| Display | 96 DPI; window 440 × 900, the width of the side panel |
| Input languages | en-US and en-IN keyboards only: **no IME installed** |

> **One fast machine.** Every number below is from a 20-thread desktop. A low-end laptop will be slower, so the limits in ADR-017 keep a margin and are re-measured on the RC reference machine.

## Method

The spike is the app's shape: the same framework, deployment and package versions. It holds a spike-grade parser for SideNotes' subset (line blocks, regex inlines; P2.2 writes the real one) and styles the editor two ways:

- **TOM:** span by span, `ITextRange.CharacterFormat`, batched with `BatchDisplayUpdates`;
- **RTF:** the spans written as one RTF string, markup as `\v` (hidden) text, loaded with one `SetText(FormatRtf)`.

Two layouts are measured:
- **grow:** the editor sized to its text inside an outer `ScrollViewer`. This is a card today (ADR-016), so the whole note is laid out.
- **fixed:** the editor sized to the window, scrolling inside itself.

Real keys are sent only while the spike's own window is in the foreground; the run refuses otherwise. The desktop preflight was clean before every run. UI Automation is read from a separate process (`Probe-Uia.ps1`).

## Results: behaviour

| # | Question | Result | Evidence |
|---|---|---|---|
| 1 | Is the document text the source? | **Yes** | All 9 samples, through TOM and through RTF: `GetText(UseLf)` equals the LF-normalised source. Samples cover every construct, emoji with modifiers and ZWJ, CJK, a combining mark, a tab, trailing spaces, unclosed markup, CRLF, lone CR and the empty note |
| 2 | Do offsets map 1:1? | **Yes** | Every span's range in the document holds exactly the source's substring: 0 misses in 9 samples. A CRLF or lone CR is one document character, so the LF source and the document agree |
| 3 | Is markup hidden, and does it stay in the text? | **Yes** | Every markup span reads `Hidden = On`; `GetText(NoHidden)` is exactly the source without its markup |
| 4 | Caret over hidden markup | **Skips it** | `a **bold** b`: Right from after `bold` lands after the space (8 → 11); Left from before `bold` lands before the space (4 → 1); a caret placed inside hidden markup snaps forward (3 → 4, 9 → 10). Home and End go to the line's real ends |
| 5 | Backspace beside hidden markup | **Deletes the visible character** | At 4 (`a **|bold** b`) it deletes the space, leaving `a**bold** b` |
| 6 | What a typed character inherits | **The visible format** | `x` typed before or after `bold`, next to hidden `**`, is bold and **not** hidden, before any restyle |
| 7 | `Ctrl+B` with `DisabledFormattingAccelerators = All` | **Inert** | No bold, no text change: Noto owns the chord map |
| 8 | Does a style applied from code enter RichEdit's undo? | **Yes: unusable as is** | Type `x`, apply bold from code, `Ctrl+Z`: the **bold** is undone and the `x` stays. TOM's `Undo` behaves the same |
| 9 | Can RichEdit's undo be switched off? | **Yes** | `UndoLimit = 0`: `CanUndo` is false, `Ctrl+Z` changes nothing, typing still raises its change |
| 10 | Can a restyle be told from an edit? | **Yes** | `TextChanging.IsContentChanging` is false for a format-only change and true for typing |
| 11 | What UI Automation reads | **The source, markup included** | `TextPattern.DocumentRange` text equals the source (191 characters), not the visible text (123) |
| 12 | IME / TSF, `Win+.` | **NOT RUN** | No IME is installed, and installing an input language is a system setting the spike does not change |
| 13 | Spell check over hidden markup | **NOT RUN** | Visual only; it belongs to the RC visual pass |

## Results: performance

Styled load means the RTF path to the first rendered frame. Typing means one character inserted mid-note. The line restyle is the edited line reset and restyled through TOM. Medians, and max where it matters.

**Everyday sizes (grow, the card's layout), spike corpus (dense: about one span per 12 characters):**

| Note | Spans | Styled load | Typing | Line restyle (median / max) |
|---|---|---|---|---|
| 541 chars | 50 | 6.8 ms | 0.35 ms | 1.7 / 5.4 ms |
| 1 KB | 90 | 17 ms | 0.40 ms | 2.8 / 6.3 ms |
| 2.6 KB | 216 | 34 ms | 0.41 ms | 3.3 / 6.2 ms |
| 5 KB | 423 | 79 ms | 0.47 ms | 4.7 / 7.7 ms |
| 10 KB | 828 | 174 ms | 0.56 ms | 6.0 / 10.7 ms |
| 25 KB | 2,052 | 512 ms | 0.66 ms | 7.8 / 18.6 ms |
| 50 KB | 4,086 | 1,196 ms | 0.99 ms | 13.7 / 37.0 ms |
| 100 KB | 8,114 | 2,807 ms | 1.31 ms | 15.9 / 58.6 ms |

Fixed layout is 20–30% faster at every size (10 KB: 141 ms; 100 KB: 2,016 ms). Showing markup instead of hiding it costs the same or slightly more: hidden text is not the cost.

**What styling costs, and why:**

| Measure | Result |
|---|---|
| One formatting call (`CharacterFormat.Bold = …`) | 0.20 ms on a 1 KB note, 0.40 ms on 100 KB; a whole-format clone is no cheaper |
| `GetRange` alone | 0.005 ms |
| TOM styling of a whole note | super-linear: 2.6 KB 80 ms, 10 KB 429 ms, 20 KB 1,367 ms, **100 KB 35 s** |
| TOM, unbatched | 2.4–3.3 × slower than batched |
| Changed one at a time, 10 KB: spell check off, markup shown, no font changes, no `TextChanging` handler, not in the visual tree | each within about 10% of the baseline (250 ms): none of them is the cost |
| RTF load | close to linear in spans, 0.15–0.35 ms per span, rising with the note; 100 KB of plain RTF with no styling at all still takes 584 ms |
| Restyling slices of a 100 KB note through RTF range replacement | text and caret unchanged, but **230 ms median, 409 ms max per 3 KB slice**, 12.6 s in all: unusable on the UI thread |

**The platform's own cost (no Noto styling):**

| Measure | Result |
|---|---|
| A character typed into 100 KB, `TextBox` (today's editor) | 0.49 ms |
| The same, unstyled `RichEditBox` | 0.36 ms (fixed); 1.09 ms (grow) |
| The whole 100 KB note as plain text, to first frame | 140 ms (fixed), 324 ms (grow); the app's `TextBox` baseline is 479 ms (quality-gates.md) |
| Reading the whole 100 KB back, `GetText(UseLf)` | 0.43 ms |
| Parsing the whole 100 KB (spike parser) | 2.6–4.2 ms |

**Read view (`RichTextBlock` built from the same spans):** short note 0.4–0.6 ms to build, 7–16 ms to first frame; 100 KB 75–103 ms to build, 480–530 ms to first frame.

## Findings

1. **Same text, hidden markup works.** Fidelity, 1:1 offsets, caret, Backspace and typed-character inheritance are all right with no code of Noto's. Hidden-markup editing needs no second text and no offset map (rejected option B in ADR-017).
2. **RichEdit's undo cannot be used.** Coded restyles enter its stack, between the user's edits. Noto must own undo and switch RichEdit's off: proven possible (#9).
3. **Styling cost is the number of formatting calls, and each call costs more as the note grows.** No setting changes this (the 10 KB matrix). So:
   - load with as few calls as possible (one RTF string);
   - on a keystroke, restyle nothing unless the edited block's structure changed;
   - above a size, do not style at all.
4. **Typing is never the problem.** An insert costs 0.3–1.3 ms (median) at every size, styled or not.
5. **The two whole-note strategies measured here do not scale to very large notes.** On this dense synthetic corpus, styled load passes the 200 ms "large note" budget only up to about 10 KB. Progressive styling in TOM calls or RTF slices blocks the UI thread for hundreds of milliseconds per slice. Plain source loads 100 KB in 140–324 ms and types at about 1 ms. Realistic notes and other incremental strategies are not measured yet (ADR-017 §5).
6. **UI Automation exposes the source.** A screen reader gets the markup. Whether that is usable is a release-candidate gate: Narrator listening decides, and a noisy result is a v0.1 defect (ADR-017 §6).

## Limitations

- One fast machine; no low-end or ARM64 numbers.
- The spike's parser is regex-based. Its own cost is negligible (2.6 ms per 100 KB) and the real parser's is measured in P2.2.
- The corpus is dense by design: the worst case. Everyday notes hold much more prose per span.
- No IME, `Win+.`, spell-check, Narrator listening, High Contrast or mixed-DPI evidence. All belong to the RC campaign (ADR-017, "Verification").
- Paste and clipboard were not probed: they are P2.7's, and the spike leaves the user's clipboard alone.
