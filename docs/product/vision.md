# Noto — Product Vision

**Status:** Draft
**Last updated:** 2026-09-14

---

## One sentence

Noto is the contextual information layer for Windows: notes that live where you
work, and disappear when you don't need them.

---

## The problem

Knowledge work happens across many applications at once. The information you
need while doing that work — a credential, an API shape, a decision you made
last week, the three things left to check before shipping — is almost never in
the application where you need it.

So it goes somewhere else: a second monitor, a browser tab, a sticky note, a
scratch file, a chat message to yourself. And then it has to be **found again**,
which means remembering that it exists, remembering where you put it, and
switching away from the work to go get it.

The cost is not storage. It is **retrieval and proximity**.

Existing tools fail at one of two ends:

```
             small, fast,                    organized, searchable,
             always visible                  structured
                   |                                  |
 Windows Sticky ---+                                  +--- Notion
 Zhorn Stickies    |                                  |    Evernote
 Notezilla         |                                  |    OneNote
                   |                                  |    Obsidian
      no structure |                                  | not present while
      no search    |                                  | you work; you must
      clutters the |                                  | leave the work to
      desktop      |                                  | go and use them
```

Nothing sits in the middle: structured enough to find things, present enough
that you don't have to.

---

## The insight

The missing organizing principle is **context**.

A note is rarely about nothing. It is about a project, a codebase, a document, a
customer, a ticket. And that subject almost always has a window on screen.

If Noto knows what you are working on, it can decide what to show you — without
you filing, tagging, searching, or remembering.

```
  what you are doing        ->   what you need to see
  ------------------             --------------------
  VS Code, repo "api"            API notes, TODO for this branch
  Chrome, a Jira ticket          that ticket's working notes
  Figma, "Checkout" file         design decisions for checkout
  Terminal                       the commands you always forget
```

This is not automation for its own sake. It is the removal of a step the user
should never have had to perform.

---

## What Noto is

Four modes over one set of notes:

```
                   +--------------------------+
                   |      one note store      |
                   |    local, fast, yours    |
                   +-------------+------------+
                                 |
      +-------------+------------+------------+-------------+
      |             |            |            |             |
 +----v-----+  +----v-----+  +---v------+  +--v-------+
 |WORKSPACE |  | FLOATING |  |CONTEXTUAL|  | CAPTURE  |
 |          |  |          |  |          |  |          |
 | an edge  |  | a note   |  | notes    |  | a hotkey |
 | drawer   |  | as a     |  | appear   |  | from     |
 | you pull |  | desktop  |  | with the |  | anywhere |
 | out to   |  | object,  |  | work     |  | into a   |
 | browse   |  | pinned   |  | they     |  | note     |
 | and find |  | on top   |  | belong to|  |          |
 +----------+  +----------+  +----------+  +----------+
     find          keep          recall       capture
```

They are not four features. They are four answers to *where should this
information be right now*, and a note moves freely between them.

---

## Who it is for

**Primary: people who work across many applications on one Windows machine.**
Developers, designers, analysts, support engineers, IT staff, students — people
who already keep a scratch file open, already have a notes tab pinned, and
already lose things.

**Specifically, people who:**

- switch context often and pay for it every time
- keep reference material they need *while* doing something, not before or after
- have tried a large notes app and stopped using it because it was somewhere else
- want their data on their own disk

**Not for:**

- teams needing real-time collaboration
- people building a long-term linked knowledge base
- project and task management
- anyone whose primary need is a document editor

---

## What Noto is not

| Not this | Because |
| -------- | ------- |
| Notion / Evernote / OneNote | Those are destinations. Noto is a layer. A destination has to be travelled to. |
| A task manager | Checklists inside notes, yes. Projects, assignees and due-date pipelines, no. |
| A knowledge graph | No backlinks, no graph view, no daily notes. Context replaces manual linking. |
| A collaboration platform | Local-first, single user. Sharing is export. |
| A document editor | Notes are short. If it wants to be a document, it belongs elsewhere. |
| A plugin platform | Extensibility is a large maintenance liability for a utility this small. |

Every one of these is a plausible direction, and every one dilutes the product.
**Saying no is the feature.**

---

## The competitive gap

Research (see [docs/research/](../research/)) found the market cleanly split.

```
  NOTES APP QUALITY
    high |
         |   SideNotes *            * Noticky
         |   (macOS only)             (macOS only)
         |
         |                              +---------------+
         |   Notezilla *---------*      |   Noto aims   |
         |                       |      |     here      |
         |   MS Sticky *         |      |               |
         |   Stickies  *---------*      +---------------+
         |              (title-string
         |               matching)  * @/Anchored
     low |                            (Windows, but no confirmed
         +----------------------------- search / tags / folders) --
           none         app-level        window-level      finer
                       CONTEXT AWARENESS
```

Three findings define the opportunity.

1. **The best contextual notes products are macOS-only.** SideNotes (the best
   sidebar) and Noticky (app-aware notes, screen-capture exclusion) do not ship
   on Windows at all. Windows users have sticky notes.

2. **The one Windows product built around context has no notes app behind it.**
   @/Anchored claims to anchor notes to windows and to follow them live — though
   every one of those claims comes from its own marketing site, with no
   independent verification found. Research could not confirm it has search,
   tags, folders, or even a note list. On the available evidence it is an
   anchoring engine attached to a plain sticky note.

3. **Contextual notes on Windows are not greenfield — but they are shallow.**
   Notezilla and Zhorn Stickies have attached notes to windows for years. This
   is important: it proves the demand is real and the idea is not exotic. But
   Notezilla's documented mechanism is **user-authored window-title matching
   with `*` wildcards**, and Zhorn does not document a mechanism at all. The
   user writes a string pattern and hopes the title keeps matching.

Noto's position is the intersection none of them occupy: **a real notes app,
with context that resolves itself, native on Windows.**

### The specific opening

Every existing Windows implementation binds at or above the level of **one OS
window**. That is coarser than how people actually work:

```
  a browser window = 40 tabs  ->  one anchor
  an editor window = 12 files ->  one anchor
```

The user's real context is the *document inside* the window. The two ways the
market currently copes with that are both unsatisfying:

| Approach | Who | Problem |
| -------- | --- | ------- |
| Make the user write a title pattern | Notezilla, Zhorn | Fragile, manual, breaks when titles change |
| Read the window with an AI and guess | @/Anchored (Atlas) | Probabilistic, heavier, privacy cost |

Noto's opportunity is the third option: resolve context **deterministically and
automatically**, at the level of the document, file or URL, with no pattern for
the user to author and no inference. Doing that well is the hard engineering
problem, and the actual moat.

The related finding is that **no mainstream Windows product in this category
supports markdown**, and none offers an edge-docked sidebar. (TSNotes may be an
exception, but its site could not be reached and it appears in no 2026 roundup.)
The whole category looks like 2010.

---

## Principles in brief

Noto is **fast, quiet, native, keyboard-first, context-aware, local-first and
small**. It should be invisible until wanted, and instant when summoned.

The full set, with the trade-off each one commits us to, is in
[principles.md](principles.md).

---

## What success looks like

**For a user, one year in:**

> "I stopped thinking about where to put things. I hit a key, it goes in. When I
> come back to that project, it is already there."

**Concretely:**

- Noto is still running on their machine weeks later, not uninstalled
- they stopped keeping a scratch file
- they rarely open a note by searching, because it was already showing
- they would be annoyed to lose it

**For the project:**

- someone can read `docs/decisions/` and understand why the app is shaped this way
- a new contributor can ship a change in a day
- startup time and idle memory have not quietly doubled

---

## Open questions

This vision deliberately does not yet settle:

- **How fine can context resolution go** before it becomes fragile or
  privacy-invasive? Window level is tractable; the document inside the window is
  the hard part. → ADR-005, ADR-006
- **What is the durable identity of a window across restarts?** `HWND` is not
  stable. This is the core technical unknown. → ADR-006
- **Is there patent risk** around window-following notes? @/Anchored asserts
  "Patent Pending" on the anchoring concept. Scope is unknown and prior art
  exists, but this needs a real check before it becomes a headline claim.
- **How much editor is enough?** Markdown and checklists, certainly. Tables and
  images are less clear.
- **Does Noto ever sync?** Post-v1 at the earliest, and bring-your-own cloud
  drive if at all.

---

## A note on the name

"Noto" is a working name. Branding, domain and trademark decisions are
deliberately deferred; the name may change before v1.0, and nothing in the
architecture should depend on it.
