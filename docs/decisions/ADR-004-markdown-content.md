# ADR-004 — Markdown as the note content format

**Status:** Accepted
**Date:** 2026-09-14

---

## Context

Noto must choose how note content is stored and edited. Research found a
striking gap:

> **No mainstream Windows product in the researched set supports markdown.**
> OneNote still has "zero native markdown support" as of February 2026, despite
> years of requests. CONFIRMED.
>
> One qualification: **TSNotes** is a $10 Windows/Mac/Linux tool whose listing
> mentions hashtags and wikilinks, so it may be markdown-adjacent. Both of its
> official domains returned HTTP 403 to research, so this is **UNVERIFIED**. It
> is also absent from every 2026 roundup found. The claim above holds for every
> product a Windows user is likely to encounter; it should not be stated as
> absolute until TSNotes is checked by hand.

Meanwhile Noto's target users — developers, designers, analysts, technical
staff — already write markdown daily, in GitHub, Slack, Obsidian, and their
editors.

ADR-002 also makes the storage format a public contract: a user's notes must
outlive the application.

---

## Problem

What format stores note content, and what editing model does the user get?

---

## Options considered

| Option | Storage | Editing |
| ------ | ------- | ------- |
| **A. Plain text** | `.txt` equivalent | Trivial |
| **B. Rich text (RTF / HTML)** | Markup blob | WYSIWYG toolbar |
| **C. Markdown, plain editing** | Markdown source | Type the source, see the source |
| **D. Markdown, rendered preview** | Markdown source | Source, with a separate preview |
| **E. Markdown, live-styled** | Markdown source | Type source; styling applied inline as you type |
| **F. Block-based model** | Structured JSON | Notion-style blocks |

---

## Decision

**Markdown as the storage format (CommonMark plus GitHub-flavoured task lists),
with live-styled editing (Option E) — "invisible markdown" — as a parity
requirement rather than a later refinement.**

> **Revised 2026-09-14 after the SideNotes feature inventory.** This ADR
> originally treated live-styled editing as an M8 refinement and plain-source
> editing as acceptable for the first release. That is wrong.
>
> SideNotes 1.5 shipped **"Invisible Markdown"** — markup hidden by default,
> toggled with `⇧⌘R` — and rebuilt its entire editor around it. It is the
> editor's defining characteristic, not a display option. A Windows SideNotes
> that shows raw `**asterisks**` by default is not at parity, however good the
> rest is.
>
> Live styling therefore moves into **M3 (SideNotes Parity)**, with a
> show/hide-markup toggle. The storage format is unchanged, so this is a
> change of schedule and editor ambition — not of data.

Plain-source editing remains valid as an *intermediate* state during M2 while
the editor is built. It is not an acceptable end state for parity.

### Scope of supported syntax

**In scope, M1:**

```
  headings          # ## ###
  bold / italic     ** *
  inline code       `
  code blocks       ``` with language
  lists             - 1.
  task lists        - [ ] - [x]
  links             [text](url)
  quotes            >
  rules             ---
```

**Deferred, and each needs its own justification:**

- tables — genuinely useful, genuinely fiddly to edit well
- images — depends on the attachment model
- footnotes, definition lists, math — no evidence of need

**Explicitly rejected:**

- arbitrary embedded HTML — a security surface (principle 10) with no
  proportionate benefit
- wiki-links and backlinks — that is a knowledge graph, which the vision
  explicitly excludes

### Storage

Note content is stored as **markdown source text** in the database. Not
rendered HTML, not an AST, not a proprietary blob.

Export is therefore lossless by construction: a note is already a markdown
file, and the export is a copy.

---

## Rationale

**It is the clearest uncontested opening in the market.** Every competitor
lacks it, and the target users already write it. This is rare: a table-stakes
feature for the audience, with zero competition on it.

**It satisfies the data-outlives-the-app requirement.** ADR-002 promises that
if Noto disappears, the user's notes remain usable. Markdown in a SQLite column
is readable by any tool, forever. RTF, HTML or a block-JSON model would all be,
in practice, a proprietary format.

**It is the right shape for the content.** Noto's notes are short reference
material: a command, a checklist, an API shape, three things to remember.
Markdown's syntax covers that almost exactly, and its ceiling — no page
layout, no complex nesting — is a *feature* given principle 1. A format that
cannot become a document helps keep Noto from becoming a document editor.

**Task lists come free.** Checklists are table stakes in this category (every
competitor has them) and `- [ ]` is already markdown. One syntax, not a
separate feature.

**Code blocks matter for the primary audience** and are absent everywhere else.

### Why live-styled rather than a preview pane

A preview pane splits a small note in half, doubles the screen cost, and adds a
mode. Live styling — where `**bold**` renders bold as you type while the
source remains editable — avoids the mode entirely and suits short notes.

It is harder to build, and SideNotes needed a full editor rebuild (1.5) to get
there — which is a realistic signal of the cost. Shipping C then moving to E is
still not a rewrite of *data*, because **the storage format does not change**;
that is the point of deciding storage separately from editing. But it is a
substantial editor rebuild, and the parity inventory makes clear it cannot be
skipped.

**Scope note from the inventory:** SideNotes' markdown is a deliberate subset —
five heading levels, bold/italic/bold-italic, strikethrough, a non-standard
`::mark::` highlight, quotes, both list types, tasks, inline and block code,
`#rrggbb` colour swatches, inline links, and `---` separators. **Tables are
absent from its documentation.** Noto's in-scope list should match this subset
for parity; tables remain deferred and are not a parity gap.

### Why not rich text

A formatting toolbar is a mouse-first interaction in a keyboard-first product
(principle 7). RTF and HTML are worse archival formats. And rich text invites
scope creep toward document editing, which the vision forbids.

### Why not blocks

A block model is the right choice for Notion and the wrong one here. It is a
large implementation, a proprietary format, and it pulls directly toward the
product Noto has committed not to become.

---

## Consequences

### Positive

- a real competitive differentiator, cheap to obtain
- lossless export by construction
- checklists and code blocks arrive with the format
- users already know the syntax
- the format's low ceiling helps enforce product scope
- plain-text storage means FTS5 indexes note content directly, with no
  extraction step (ADR-003)

### Negative

- **users who expect a formatting toolbar will find it unfamiliar.** Mitigated
  by live styling and a small formatting shortcut set (`Ctrl+B` inserting `**`).
- live-styled editing is a substantial piece of work; the MVP ships without it
- tables and images are deferred, and both will be asked for
- markdown rendering is a dependency, and dependencies must be justified
  (principle 9) — the renderer must be small and well-maintained

### Security requirements

- **no arbitrary HTML rendering.** Markdown is rendered to native controls, not
  to a web view.
- link handling validates the URL scheme before opening. `http`, `https`,
  `mailto` and `file` only, with `file` requiring confirmation.
- no `javascript:` or other executable schemes, ever (principle 10)
- rendering never executes anything from note content

---

## Alternatives rejected

| Option | Why |
| ------ | --- |
| **Plain text (A)** | Forgoes checklists and code blocks for no real saving — markdown *is* plain text with conventions. |
| **Rich text (B)** | Mouse-first, worse archival format, invites document-editor scope creep. |
| **Preview pane (D)** | Adds a mode and halves the space in a surface that is already small. |
| **Blocks (F)** | Large, proprietary, and pulls toward the product we have committed not to build. |

---

## Future reconsideration criteria

Revisit if:

- tables prove to be a common, repeated request — this is a **scope extension**
  within markdown, not a format change
- the chosen renderer becomes unmaintained
- live-styled editing proves unachievable at acceptable performance, in which
  case plain source editing becomes permanent rather than transitional

Do **not** revisit to add a block model or a WYSIWYG rich-text editor. Both are
foreclosed by the vision, and the storage-format commitment in ADR-002 means
either would break the promise that a user's notes outlive the application.
