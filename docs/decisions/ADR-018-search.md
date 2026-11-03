# ADR-018 — Search: one FTS5 index, one keyboard-first surface

**Status:** Accepted
**Date:** 2026-11-03
**Related:**
- ADR-003 (FTS5 over Microsoft.Data.Sqlite; `MATCH` input is escaped), ADR-012 (FTS5 external content on the implicit `rowid`), ADR-016 (card-stack workspace), ADR-017 §10 (caret placement after an edit)
- Parity E1, E3, E8, E9, G45–G47; NFR N5 (search under 100 ms at 10k notes); implementation plan P4

---

## Context

Search is the cross-folder access path in SideNotes (parity C18 → E1) and a core feature in ADR-003. No second persistence system: the index lives in the note database and is derived from it.

## Decision

### 1. The index (schema 4)

```sql
CREATE VIRTUAL TABLE NotesFts USING fts5(
    Content,
    content = 'Notes',
    content_rowid = 'rowid',
    tokenize = 'unicode61 remove_diacritics 2'
);
```

- **External content, keyed by the implicit `rowid`** (ADR-012). Nothing is stored twice: the index holds tokens, the text stays in `Notes`.
- **Triggers keep it exact:** after insert, after delete, and after an update **of `Content` only**. Moving, pinning, folding or reordering a note does not touch the index.
- **Every note is indexed, deleted or not.** Whether a result is shown is decided by the query (an active note in an active folder), so binning and restoring never rewrite the index.
- Migration 4 creates the table and triggers and builds the index from the existing notes (`'rebuild'`).
- **The `rowid` hazard (ADR-012) is a rule with a guard:** nothing in Noto runs `VACUUM` or rebuilds `Notes` without rebuilding `NotesFts` in the same transaction. Today nothing does either. A guard test fails if `VACUUM` appears without `'rebuild'`.

### 2. What a query means

| Rule | Detail |
|---|---|
| **Terms** | The text is split on white space. Each term matches **the start of a word**, ignoring case and diacritics: `caf` finds "Café", `meet` finds "meeting". All terms must match (AND), in any order |
| **Escaping** | Each term reaches `MATCH` as a quoted FTS5 string (`"term"*`, a `"` doubled). No user text is ever FTS5 syntax: `'`, `"`, `*`, `-`, `NEAR`, `AND` are just characters (ADR-003) |
| **What is searched** | A note's whole markdown source (its title is its first line). Folder **names** are matched the same way, in memory: there are few |
| **Shown** | Active notes in active folders. Folders first, then notes |
| **Order** | Notes whose title matches every term first, then the rest; within each group FTS5's `bm25` rank, then the most recently updated. At most 100 notes |
| **Scope** (E9) | All folders by default (SideNotes 1.4.16). Inside a folder, the scope can be narrowed to that folder; the choice is the session's, not stored |

The meaning lives in Core (`SearchQuery`, `SearchMatch`): the escaping, the word-start matching used for highlighting, and the folder-name match. Infrastructure runs the SQL. One rule, tested once, used for both the index and the highlight.

### 3. The surface (E1, E8, G45)

- **Ctrl+F** opens Search from the folder list, the note stack or the editor. It is a workspace surface like Settings and the Recycle Bin: one at a time, entered with the surface motion, left with **Escape** or Back, returning focus where it was.
- **Opening Search saves the card being edited** (the same save as every other leave). Escape returns to the stack still editing the same card.
- **Results update as you type** (E8). No Enter is needed to search.
- **The keyboard never leaves the box:** Up/Down move the selection through the results, Enter opens the selected one, Home/End stay in the text.
- **Opening a note** enters its folder and edits its card, with the caret at the start of the first match: you continue where your search found it. Opening a folder enters it.
- **Quick copy (E3):** `Ctrl+C` copies the selected note's text, `Ctrl+Shift+C` without its first line. Search then closes and Noto hides, so focus returns to the app the user came from and a paste lands there (the snippet workflow, D36).
- **Highlighting:** in each result's title and snippet, the matched word starts are marked with a text highlighter. They use the design system's tokens; High Contrast uses the system's Highlight and HighlightText.
- **States:**
  - empty box: a short hint;
  - no results: "No notes or folders match";
  - the database unavailable: the error, with the text kept.
- **Accessibility:**
  - the box is named;
  - each result's name reads its title, its folder and its snippet;
  - the result count is announced politely.

### 4. Budget

N5: **a query under 100 ms at 10,000 notes.** A query runs on the UI thread while it measures well inside that budget. It is measured by a benchmark test over 10,000 realistic notes, and on a frozen build by the runtime harness (`Invoke-Search.ps1`). The migration's index build is measured on the same corpus.

## Not in this ADR

These are left for later:
- **E2** (a global search hotkey): P6.
- **E4** (launching the URL on a note's second line): the rich-content slice, which brings scheme validation.
- **E5** (Ctrl+Click opens the result's folder): the modifier is unknown.
- **In-note find, filters and tags:** not documented in SideNotes (E8 note).

## Consequences

- **Positive:**
  - one store;
  - no indexing step to forget;
  - the index can't drift from the text except through the `rowid` rule, and that rule is guarded.
- **Negative:**
  - word-start matching only: `ing` does not find "meeting". It is the common desktop-search behaviour and keeps FTS5's index usable; substring search (`trigram`) would double the index and need ≥ 3 characters.
  - The index costs disk space roughly proportional to the text.

## Verification

- **Infrastructure tests:**
  - triggers on insert, update, delete and purge;
  - the migration on a database with notes;
  - escaping: an apostrophe, a quote, `*`, `NEAR`, `AND`, unicode and diacritics;
  - scope and deleted-note filtering;
  - the 10k-note benchmark.
- **Core tests:** query parsing, word-start matching and highlight ranges.
- **Guards:**
  - no `VACUUM` without a rebuild;
  - Ctrl+F reaches Search from the editor;
  - opening Search saves first.
- **Runtime, on a frozen build** (`Invoke-Search.ps1`):
  - Ctrl+F from every surface;
  - typing, highlighting, keyboard selection, opening a note with the caret at the match, opening a folder;
  - scope; quick copy; empty and no-results states;
  - Escape returning to the editor still editing;
  - timings.
