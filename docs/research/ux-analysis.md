# UX Analysis

**Research date:** 2026-09-14
**Source:** vendor documentation, user reviews, forum and community complaints
**Raw material:** [`_raw/`](_raw/)

---

## Purpose

This document records **interaction design lessons** from the competitive set —
particularly the complaints, which are more informative than the feature lists.

A feature list tells you what a product does. A years-old unresolved complaint
tells you what its architecture made hard to change.

---

## 1. The loudest lesson: undisableable triggers

The most repeated SideNotes complaint, across years:

> The **edge trackpad gesture cannot be disabled**, and it conflicts with other
> gestures. Users also want **keyboard-only activation** and do not have it.

CONFIRMED, repeated across multiple sources and multiple years.

This is the single most actionable finding in the research, because it is
cheap to get right at design time and expensive to retrofit.

```
  ACTIVATION SURFACES          each independently toggleable
  -------------------
  global hotkey        [on ]   <- must always be available
  edge hover           [on ]   <- the classic conflict source
  edge gesture         [off]
  tray click           [on ]
  edge hover delay     [200ms] <- tunable, not fixed
```

**Design rule, now principle 7:** every activation surface is independently
disableable, and every action has a keyboard path.

The deeper point: an ambient utility lives on a desktop shared with the user's
actual tools. Any input it claims is input taken from something else. A trigger
that cannot be surrendered is a trigger that eventually makes the product
uninstallable.

---

## 2. Focus theft is disqualifying

Noticky's app-aware notes are described as appearing when an application
activates **without stealing focus** — and this is called out as a feature,
which means it is a known failure mode elsewhere.

For contextual notes it is existential:

```
  user alt-tabs to VS Code
        |
        v
  Noto notices, shows the bound note
        |
        +--> note takes focus     -> user is now typing into the note
        |                            instead of their code. Uninstalled.
        |
        +--> note does not take   -> user sees their note, keeps typing
             focus                   in VS Code. Correct.
```

A contextual notes app that steals focus is worse than no contextual notes app,
because it actively interferes with the work it is supposed to support.

**Rule:** contextual surfacing never takes focus. Ever. This needs an explicit
test.

---

## 3. Coupling to another product generates backlash

Microsoft rebuilt Sticky Notes inside OneNote. The response was well documented:
slower, lost notes, forced coupling, and **users deliberately reverting to the
legacy app**.

The lesson is not "OneNote is bad". It is that users of a small ambient tool
chose it *because it was small*. Making it a front-end for something larger
removes the reason they chose it.

For Noto: resist every gravitational pull toward becoming a front-end for a
bigger system — a knowledge base, a task manager, a cloud service. That pull is
exactly what principle 9 exists to resist.

---

## 4. The category's interaction model is stale

```
  every Windows competitor:

     desktop                        manager window
    +--------+  +--------+         +------------------+
    | note   |  | note   |         | search           |
    +--------+  +--------+         | [] note          |
        +--------+                 | [] note          |
        | note   |                 | [] note          |
        +--------+                 +------------------+

    notes scattered on the desktop, plus a separate window to find them
```

Two consequences, both visible in reviews:

- **notes accumulate into desktop clutter**, which is why Notezilla invented
  "memoboards" to move them off the desktop again
- **finding a note means leaving your work** to open the manager

The SideNotes edge-drawer model solves both, and **does not exist on Windows**:

```
    edge drawer

    work area                     |  drawer
    +-------------------------+   | +-------+
    |                         |   | | notes |
    |    your actual work     |   | | list  |
    |                         |   | |       |
    +-------------------------+   | +-------+
                                  ^
                          pull out, use, dismiss
                          never covers the work permanently
```

This is a large, uncontested opportunity on Windows, and it is why the
Workspace milestone comes before floating notes.

---

## 5. Ambiguity must be legible, not silent

Every contextual system will sometimes fail to identify context (ADR-006). The
design question is what the user sees when it does.

The failure pattern to avoid:

```
  BAD    note silently does not appear
         -> user assumes it is lost
         -> user stops trusting the feature
         -> feature is disabled

  BAD    note appears on the wrong window
         -> user assumes the feature is broken
         -> worse than the above, because it is actively wrong

  GOOD   note is visible in the sidebar, labelled "was: Code.exe"
         -> user understands what happened
         -> one click to re-attach
```

This is why ADR-006 makes `detached` a first-class, non-alarming state rather
than an error. **The note is never unreachable.** It is always in the sidebar
and always findable by search.

Trust is the whole product. A contextual system that is occasionally
unavailable is tolerable; one that is occasionally *wrong* is not.

---

## 6. What good looks like, per surface

### Sidebar

| Do | Don't |
| -- | ----- |
| Slide in, use, dismiss | Persist and cover the work |
| Remember width per monitor | Reset on every display change |
| Fully keyboard navigable | Require the mouse for navigation |
| Configurable, disableable trigger | A fixed gesture (the SideNotes error) |

### Floating notes

| Do | Don't |
| -- | ----- |
| Restore to the monitor they were on | Restore off-screen when it is gone |
| Provide a non-click exit from ghost mode | Trap the user in an unclickable window |
| Persist position, size, opacity | Reset on restart |

The ghost-mode trap is worth spelling out: a click-through note cannot be
clicked, so the control that exits ghost mode **cannot be on the note**. A
hotkey and a tray item are both required (ADR-007).

### Contextual notes

| Do | Don't |
| -- | ----- |
| Appear without taking focus | Steal focus (section 2) |
| Show nothing when unsure | Guess |
| Explain why a note appeared | Behave inexplicably |
| Make re-binding one gesture | Require editing a pattern |

### Capture

| Do | Don't |
| -- | ----- |
| Hotkey → type → save → return | Require choosing a folder first |
| Return focus to the prior app | Leave the user in Noto |
| Default destination, sort later | Force filing at capture time |

Capture is the surface where friction is least tolerable, because it competes
directly with a scratch file — which has none.

---

## 7. Keyboard model

The target: the whole product is operable without the mouse.

```
  global      summon / dismiss workspace
              quick capture
              exit ghost mode

  workspace   search (type to search, no focus step)
              j/k or arrows      move through notes
              enter              open
              n                  new note
              esc                dismiss

  editor      standard text editing
              ctrl+b / i         formatting shortcuts that insert markdown
              esc                back to the list
```

Two specifics from the research:

- **type-to-search with no separate focus step.** The search box should already
  be focused when the workspace opens.
- **formatting shortcuts insert markdown syntax** rather than applying rich
  text (ADR-004). `Ctrl+B` inserts `**`, which keeps one content model while
  meeting the expectation the shortcut sets.

---

## 8. Performance is a UX property

Principle 3's budgets are interaction requirements, not engineering vanity.

```
  user considers writing something down
        |
        v
  "is it worth opening Noto?"     <-- if this thought occurs, Noto lost
        |
        +-- yes -> uses Noto
        +-- no  -> uses a scratch file, permanently
```

The scratch file's advantage is that it is *already open*. The only way to
compete is for invocation to cost nothing perceptible. A sidebar that takes
400 ms to appear is not a slow sidebar; it is an unused one.

---

## 9. Open UX questions

| Question | Resolved by |
| -------- | ----------- |
| How is a contextual note visually distinguished from an ordinary one? | M5 |
| Where does a contextual note appear — near the window, or in the sidebar? | M5 |
| What happens when several notes bind to the same application? | M5 |
| Is the sidebar a list, or a list plus preview? | M2 |
| How does a user discover that context binding exists at all? | M5 |

The last one matters more than it looks. The differentiating feature is
invisible until used, and a feature nobody finds is a feature that does not
exist.
