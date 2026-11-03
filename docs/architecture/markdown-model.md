# The Markdown model (P2.2)

**Status:** Design gate, 2026-11-03
**Related:** [ADR-004](../decisions/ADR-004-markdown-content.md) (the format), [ADR-017](../decisions/ADR-017-editor-model.md) (the editor model; this is its `MarkdownDocument`), `NoteTitle` (the heading rules it must agree with)

`Noto.Core.Markdown` turns a note's source into a description of it: blocks and formatting spans with offsets into that source. It is pure: no WinUI, no RichEdit, no UI Automation, no threading, no HTML, no RTF. The Windows editor host (P2.5) will turn spans into formatting; nothing here knows how.

```
Noto.Core.Markdown                         Noto.Windows (P2.5)
  MarkdownDocument.Parse(source)   ──▶      NoteEditorHost: spans → design tokens → RichEdit formats
    Blocks: kind, range, attributes
    Spans:  kind, range
  MarkdownDocument.ChangedRegion(before, after) ──▶ restyle only that range
```

---

## 1. Invariants

| # | Invariant | How it is held |
|---|---|---|
| I1 | **The source is the only text.** Parsing never changes, normalises or re-emits it | The model has no text output at all: no `ToMarkdown`, no renderer. `Source` is the exact string passed in |
| I2 | **Offsets are into the source as given.** Every range is `[Start, Start + Length)` of UTF-16 code units of that string | No pre-processing step exists to shift them |
| I3 | **Deterministic.** The same string always gives the same blocks and spans, in the same order | No hashing, no culture, no clock, no randomness. Spans are ordered by start, then longest first, then kind |
| I4 | **Total.** Every string parses. Malformed or unfinished markup is text, never an error | No exception path; adversarial tests over random input |
| I5 | **Spans are well formed.** In bounds, non-negative length, never across a line break, never splitting a surrogate pair | Every delimiter is ASCII and every span ends at a delimiter or a line end. Invariant tests |
| I6 | **RichEdit-compatible offsets.** RichEdit holds one character per line break (spike finding 2) | For a source whose breaks are LF or lone CR, offsets are RichEdit's 1:1. A CRLF source is normalised before it reaches the editor (ADR-017 §1); that is an explicit load step of the host, never the parser's. Tests prove that LF, CR and CRLF sources give the same structure |
| I7 | **Local.** Inline markup never crosses a line; only a code fence carries state from one line to the next | So an edit restyles its own line, plus, only when it opens or closes a fence, the lines up to where the fence state agrees again. `ChangedRegion` tells the host which range |
| I8 | **Platform-free.** No type, member or reference of WinUI, RichEdit, `TextRange`, UI Automation, dispatcher or HTML | The existing Core boundary guards, plus a Markdown-specific name guard |

**Losslessness:**

```
source ──parse──▶ model (read-only description) ──▶ host styles it
   ▲                                                    │
   └────────────── the user's edit (one TextEdit, P2.3) ┘
```

Parsing writes nothing, so the only change to the source is the user's own edit. No whitespace is canonicalised, no line ending is rewritten, no markup is "fixed".

## 2. Syntax: what v0.1 recognises

The agreed editor scope: basic-release.md "Editor" and ADR-004's in-scope list. CommonMark where it defines the construct, so exported notes render the same elsewhere; Noto's line-local rule (I7) where CommonMark would reach across lines.

### Blocks: one per line, except a fenced code block

Decided per line, in this order of precedence. "Indent ≤ 3" means at most three leading spaces, a CommonMark rule.

| Block | Recognised when | Spans |
|---|---|---|
| **Code fence** | indent ≤ 3, then ≥ 3 backticks (with no backtick in the info string) or ≥ 3 tildes. It runs to a line of the same character, at least as long, with indent ≤ 3 and nothing else; **unclosed, it runs to the end of the note** (CommonMark) | Each fence line is `Markup`. Each line between is `CodeBlock`, never parsed further |
| **Separator** | indent ≤ 3, then ≥ 3 of one of `-` `*` `_`, with spaces or tabs between allowed, and nothing else | `Separator` over the marks |
| **Heading** | indent ≤ 3, then 1–6 `#`, then a space, a tab or the line's end. An optional closing `#` run is decoration only when a space or tab precedes it (`# C#` keeps its `#`). Same rules as `NoteTitle` | `Markup` over the indent, hashes and following blanks, and over any closing run with its blanks. `Heading` (level 1–6) over the text. The text is inline-parsed |
| **Quote** | indent ≤ 3, then `>` and one optional space | `Markup` over the marker. `Quote` over the rest, inline-parsed |
| **Task item** | a list marker (below), then `[ ]`, `[x]` or `[X]`, then a space, a tab or the line's end | `ListMarker`; `TaskBox` over the three box characters (checked or not); `TaskDone` over a checked item's text; the text inline-parsed |
| **Bullet item** | any indent, then `-`, `*` or `+`, then a space, a tab or the line's end | `ListMarker` over the marker and its following blanks; the text inline-parsed |
| **Ordered item** | any indent, then 1–9 digits, `.` or `)`, then a space, a tab or the line's end | as a bullet item |
| **Blank** | only spaces and tabs | none |
| **Paragraph** | anything else | the line inline-parsed |

- A list item's **`Indent`** is its leading whitespace in columns, a tab counting to the next multiple of four. The host uses it for nesting; there is no list tree to keep consistent.
- A separator outranks a list item (`- - -`), as in CommonMark.
- With 4 or more spaces of indent, a heading, fence, separator or quote marker is plain text. That matches `NoteTitle`, which follows CommonMark's indented-code rule.

### Inline: within one line's text, never inside a code block

| Construct | Syntax | Rule | Spans |
|---|---|---|---|
| **Escape** | `\` before ASCII punctuation | The character is literal. Not inside a code span | `Markup` over the backslash |
| **Code span** | a run of n backticks … the next run of exactly n | Binds first. Its text is raw: no emphasis, link or escape inside. With no matching run, the backticks are text | `Markup` over both runs; `Code` over the text |
| **Link** | `[text](destination)` | Non-empty text, which may hold emphasis and code. Destination: no whitespace, with balanced parentheses allowed (`…/Foo_(bar)`). Delimiters inside the text pair only with each other. No link inside a link. `![…](…)` is not a link: image syntax is P3's | `Markup` over `[`, `](` and `)`; `Link` over the text; `LinkTarget` over the destination |
| **Strong** | `**text**`, `__text__` | CommonMark's delimiter-run algorithm: left- and right-flanking, the rule of 3, `_` never opens or closes inside a word | `Markup` over each delimiter; `Strong` over the text between |
| **Emphasis** | `*text*`, `_text_` | as Strong; `***x***` is both | `Markup`; `Emphasis` |
| **Strikethrough** | `~~text~~` | exactly two tildes on each side; flanking rules as `*` | `Markup`; `Strikethrough` |
| **Underline** | `==text==` | Noto's and SideNotes' syntax, not CommonMark (ADR-004). Exactly two `=`; flanking as `*` | `Markup`; `Underline` |

Nesting and overlap follow from the algorithm, deterministically:
- `**a *b* c**` gives Strong over `a *b* c` and Emphasis over `b`.
- `**foo**bar**` gives Strong over `foo`, then `bar**` as text.
- `*a **b* c**` gives three nested Emphasis spans, as CommonMark does.

Each case in the tests was checked against a CommonMark reference implementation (markdown-it in CommonMark mode), run locally and not a dependency.

Noto departs from CommonMark in three places, each deliberately:
- **Empty link text is not a link.** `[](u)` would be invisible once its markup is hidden.
- **`![…](…)` stays text** until P3.
- **Inline markup never crosses a line** (I7).

### Deferred, and how each reads meanwhile

| Construct | Status in v0.1 | How it parses now |
|---|---|---|
| Setext headings (`Title` over `===`) | Not recognised (line-local; `NoteTitle` agrees) | a paragraph, then a paragraph or separator |
| Indented code blocks (4 spaces) | Not recognised: they conflict with nested lists | lists or paragraphs |
| Blocks inside quotes (`> - item`), lazy quote continuation | Deferred | a quote whose text is `- item` |
| Tables | Deferred (ADR-004) | paragraphs |
| Images `![alt](src)` | P3.0 (attachments) | text |
| Reference links, autolinks `<…>`, bare URLs | Bare URLs are P3.5; the others are deferred | text |
| Link titles `[t](u "title")` | Deferred | text (the space ends the destination) |
| `::mark::` highlight | Deferred; not in the agreed v0.1 editor list | text |
| `#rrggbb` colour swatches | P3.6 | text |
| HTML, entities (`&amp;`) | Rejected (ADR-004: no HTML) | text |
| Hard line breaks (two trailing spaces) | Deferred: Shift+Enter (D52) writes a plain break | text |

## 3. The model

```csharp
namespace Noto.Core.Markdown;

MarkdownDocument.Parse(string source) → MarkdownDocument
  .Source  : string                       // the same instance passed in (I1)
  .Blocks  : IReadOnlyList<MarkdownBlock> // in source order, covering every line
  .Spans   : IReadOnlyList<MarkdownSpan>  // every block's spans, ordered (I3)
MarkdownDocument.ChangedRegion(before, after) → (int Start, int Length)  // in after's source

MarkdownBlock(Kind, Start, Length, Level, Indent, IsChecked, Spans)
  // Start/Length: the block's lines without their final line break;
  // a fence block spans its lines
MarkdownSpan(Kind, Start, Length, Level)
MarkdownBlockKind: Paragraph, Blank, Heading, Quote, BulletItem, OrderedItem, TaskItem, CodeFence, Separator
MarkdownSpanKind:  Markup, Heading, Quote, ListMarker, TaskBox, TaskDone, CodeBlock, Separator,
                   Strong, Emphasis, Strikethrough, Underline, Code, Link, LinkTarget
```

- `Markup` is what the host hides, or dims with Show markup.
- `LinkTarget` is hidden too, but is kept separate so P3.5 can open the destination without re-parsing.
- Content spans (`Strong`, `Heading`, …) cover the text between their markers, so a span's own markers are never inside it. An outer span may contain an inner span's markers.

**Incremental use.** The host parses the whole note after an edit. That is cheap (§6) and keeps one code path, so nothing incremental can drift from a full parse. `ChangedRegion` compares the two parses:
- matching blocks are skipped from the front and from the back, aligned by the length change;
- what remains is the only range whose styling can differ, and the host restyles exactly that.

## 4. Editing semantics

The source is the truth, so every edit's effect on formatting is: **re-parse, and style what the parse says.** Each row is a tested outcome.

| Situation | Outcome |
|---|---|
| Partially typed syntax: `**bol` | No Strong yet; the `**` is text and shown. Typing the closing `**` makes it Strong, and the markers become `Markup` |
| Caret just inside a closing marker: `**bold|**`, typing `x` | `x` is inside Strong (`boldx`) |
| Caret just after a closing marker: `**bold**|`, typing `x` | `x` is plain text |
| Caret just after an opening marker: `**|bold**` | inside, at Strong's start |
| Inserting inside a formatted range | The span grows by the insertion. Nothing outside the line changes (I7) |
| Deleting visible characters inside a span | The span shrinks. Deleting all of them leaves `****`, which is a separator line on its own and text inside a line. **That hazard is P2.3's to prevent**: deleting a span's whole text removes its markers in the same edit (ADR-017, the "emptied span" rule). The parse of what is left is tested |
| Deleting one marker (`**bold**` → `*bold**`) | CommonMark's result: Emphasis over `bold`, then a `*` as text. Deterministic, never an error |
| A selection across formatted and plain text, deleted | Whatever remains is parsed afresh. Markers whose partner was deleted become text |
| Nested formatting | As the algorithm (§2) |
| Overlapping markup | As the algorithm. The later run is text |
| Unclosed constructs | Text: `**`, `` ` ``, `[x](` and `~~` with no partner are shown, never hidden. **An unclosed fence is the exception: CommonMark runs it to the end of the note**, so typing ```` ``` ```` turns the rest into code until it is closed. Tested, and noted for the host (P2.5 may show the open fence distinctly) |
| Code spans and fences | Nothing inside them is formatting: `` `**x**` `` is Code over `**x**`, and a fenced `# x` is a code line |

## 5. Tests

- **Construct tables:** every block and inline rule above, its boundaries (1 vs 6 vs 7 `#`; indent 3 vs 4; `--` vs `---`; `~` vs `~~` vs `~~~`; a fence closed short vs closed long), and every deferred construct reading as text.
- **CommonMark agreement** on the emphasis cases that decide nesting, overlap and the rule of 3.
- **Text:** emoji with modifiers and ZWJ, CJK next to markers, combining marks, tabs, trailing spaces, LF/CRLF/CR, long lines, the empty note.
- **Corpus:** realistic notes (lists, meeting notes, long prose, code-heavy, checklists, pasted documents) and the spike's dense synthetic corpus.
- **Invariants over seeded random input and random edits** (I1–I5, I7):
  - the source is unchanged;
  - spans are in bounds, non-negative, line-local and never split a surrogate pair;
  - a repeated parse is identical;
  - nothing throws;
  - after an edit, everything outside `ChangedRegion` styles exactly as before (shifted by the edit). An edit on a line with no fence marker gives a region inside that line.
- **Mutation testing** of the rules that matter: delimiter lengths, off-by-one ranges, the flanking and rule-of-3 conditions, precedence, code exclusion, list and task detection, fence closing, line splitting and surrogate handling.

## 6. Performance

Measured in Core alone, independent of RichEdit; numbers in the PR. The gate:
- a parse of the 100 KB dense corpus in **≤ 10 ms** (ADR-017 verification);
- a realistic note in well under a millisecond;
- allocation proportional to spans, not to repeated copies of the text.

A test holds a generous ceiling so a slow CI runner cannot fail it spuriously; the measured numbers are recorded here and in the PR.

**Measured (2026-11-03; Release; .NET 9; the 20-thread desktop of the spike; median of 15 after 3 warm-up parses):**

| Note | Size | Spans | Parse, median | Allocated per parse |
|---|---|---|---|---|
| Meeting notes (realistic) | 563 chars | 37 | 0.027 ms | 9 KB |
| Large realistic (the realistic notes repeated) | 104,980 chars | 2,130 | 3.9 ms | 525 KB |
| Dense synthetic (the spike's corpus) | 100,661 chars | 8,395 | 5.0 ms | 1.5 MB |

Within the 10 ms gate, so the host can re-parse the whole note on every keystroke and restyle only `ChangedRegion`. A repeated parse costs the same as the first; nothing is cached, so nothing can go stale. Allocation follows the span count (about 180 bytes per span, including the per-line lists), not the text length.

**Span density, for ADR-017 §5:**

| Corpus | Spans per 1,000 characters |
|---|---|
| Dense synthetic | 83 |
| Realistic notes | 4–140 by note; 20 for the large realistic note |
| Long-form prose | 5.4 |
| Pasted document | 4.1 |

At 20 spans per 1,000 characters, ADR-017 §5's provisional 400-span limit is reached at about 20 KB of realistic text, and at about 74 KB of prose. Whether that limit, and the fallback, survive is P2.5's question, answered with the real host.
