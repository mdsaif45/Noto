# Noto — Product Principles

**Status:** Draft
**Last updated:** 2026-09-14

These are decision rules, not values. Each one is written so that it can
actually *reject* a proposal. A principle that never says no is decoration.

When two principles conflict, the one earlier in this document wins.

---

## 1. Proximity over capability

**Noto's job is to have the right information present, not to be able to do
everything with information.**

A feature that makes Noto more capable but less present is a bad trade. Being a
layer means the value is in *where* the information is, not *what else* you can
do to it.

- ✅ A note appears next to the work it belongs to
- ❌ A note can be transformed into a database view

> **Test:** does this help the user *see the right thing sooner*, or does it
> give them *more to do once they get there*? Only the first qualifies.

---

## 2. Disappear when not needed

**The default state of Noto is invisible.**

Noto is a background utility on someone's work machine. It competes for screen
space and attention with the work itself, and the work always wins.

This forbids: splash screens, onboarding tours, badge counts, upsell prompts,
"tips", update nags, and anything that appears without being asked for.

- ✅ Summoned by a keystroke, gone when dismissed
- ❌ A persistent panel that must be closed

> **Test:** if the user never invoked this, would they still see it? If yes,
> justify it or cut it.

---

## 3. Speed is a feature, and it is measured

**Noto is fast enough that the user never decides whether it is worth opening.**

The moment a user weighs the cost of opening Noto, Noto has failed — they will
use a scratch file instead.

| Budget | Target |
| ------ | ------ |
| Cold start to usable | under 1 second |
| Sidebar open | perceptually instant |
| Search results | as fast as typing |
| Idle CPU | effectively zero |
| Idle memory | small enough to forget about |
| Window follow | no perceptible lag |

These are **requirements, not aspirations**. Performance claims in a PR must be
measured and the numbers stated. "It feels fine" is not a measurement.

> **Test:** would this feature survive if it cost 200ms of startup? If not, it
> does not get 200ms of startup.

---

## 4. Local-first, and that is not negotiable

**Noto works completely offline, forever, with no account.**

Core functionality must never require: an account, a network connection, a
subscription, or a remote backend.

The user's notes are on their disk, in a format they can find, back up and
read. If Noto disappears tomorrow, their data does not.

Any future sync is **optional, additive, and bring-your-own-storage**. It never
becomes the path of least resistance.

> **Test:** does this work on a plane, on day one, with no sign-in? If not, it
> is not core.

---

## 5. Context should be automatic, or it is not context

**If the user has to configure the context, we have moved work to the user
rather than removing it.**

This is the principle that separates Noto from Notezilla and Zhorn Stickies,
which make users author window-title patterns with wildcards. That is
configuration wearing a context costume.

Ranked by preference:

```
  best   automatic and deterministic   (resolve the real document/file/URL)
         automatic with confirmation   (we detect, user confirms once)
         one-gesture manual binding    ("bind this note to this")
  worst  user authors a match pattern  (Notezilla's model)
```

Noto may ship the middle options as stepping stones. It should never ship the
bottom one as the primary model.

> **Test:** after the first use, does the user have to think about the binding
> again? If yes, it is configuration, not context.

---

## 6. Deterministic over clever

**Prefer a mechanism that is explainable and repeatable over one that is
usually right.**

A notes app that shows the wrong notes is worse than one that shows none,
because the user stops trusting it — and trust is the whole product.

This means: when Noto cannot determine context confidently, it shows nothing and
says nothing, rather than guessing.

It also means AI is not the answer to context resolution. Reading a window to
infer what the user is looking at is a workaround for not knowing. Noto should
know.

> **Test:** can you explain to a user, in one sentence, why this note appeared?
> If not, it should not have appeared.

---

## 7. Keyboard-first, mouse-friendly

**Every primary action has a keyboard path.**

Capture, search, open, switch, dismiss, pin — all reachable without the mouse.
The mouse is fully supported, but the keyboard is the design target, because
the user is already typing.

**Every activation surface is independently disableable.** This comes directly
from research: SideNotes users have complained for years about an edge trigger
that conflicts with other gestures and cannot be turned off. Do not repeat it.

> **Test:** can a user who never touches the trackpad use this? Can a user who
> hates it turn it off?

---

## 8. Native, not wrapped

**Noto is a Windows application, built with Windows APIs, behaving like Windows
software.**

The genuinely hard problems here — window tracking, edge docking, always-on-top,
global hotkeys, per-monitor DPI, screen-capture exclusion, virtual desktops —
are exactly the problems cross-platform wrappers are worst at. There is no
portability benefit to trade against, because Noto is Windows-only by design.

This also means respecting platform conventions: system theme, Mica, DPI
scaling, accessibility, standard shortcuts.

> **Test:** would a Windows user notice this is not a real Windows app?

---

## 9. Small on purpose

**Every feature has an ongoing cost. Most features are not worth it.**

Noto is maintained by very few people over several years. The constraint is not
what can be built; it is what can be kept working across Windows updates, DPI
edge cases and monitor configurations.

Defaults to no:

- plugin systems and extensibility surfaces
- collaboration and multi-user anything
- a second organizational paradigm alongside folders and tags
- a feature that exists because a competitor has it
- configuration options added instead of making a decision

Preferences are not free. Each one is a branch in behavior, a test matrix entry,
and a thing to explain.

> **Test:** who maintains this in three years, and what breaks it? If the answer
> is "unclear" and "Windows", do not build it.

---

## 10. Notes are sensitive by default

**Assume every note contains something the user would not want exposed.**

People put credentials, medical details, salary numbers, and half-formed
thoughts about their colleagues in notes. Design as if that is always true.

Commitments:

- no telemetry by default; any future telemetry is opt-in and documented
- never log note content or user file paths
- never execute commands or code from note content without an explicit,
  documented permission model
- validate every path, URL scheme and attachment
- screen-capture exclusion, so notes do not leak into shared screens and
  recordings

> **Test:** if this note content appeared in a log, a crash report, or a shared
> screen, how bad would it be? Design for the bad answer.

---

## 11. Architecture is decided before it is written

**Noto is built with heavy AI assistance, which makes architectural drift the
primary long-term risk.**

The failure mode is specific and well known: the application works, the tests
pass, and nobody can explain why it is shaped the way it is. Changing anything
then becomes archaeology.

The defense is [ADRs](../decisions/). Significant decisions are recorded with
their context, alternatives and consequences *before* implementation.

"The AI suggested it" is not a rationale. Neither is "it was faster to
generate." If a proposed change contradicts an ADR, the ADR is reviewed first —
it is not silently overridden.

> **Test:** in two years, will someone be able to find out why this is the way
> it is? If not, write it down first.

---

## Applying these

When a feature is proposed, the useful question is not "is this good?" — most
proposed features are good. The question is:

```
  Does it make the right information more present?          (1)
  Does it stay out of the way?                              (2)
  Does it survive the performance budget?                   (3)
  Does it work offline with no account?                     (4)
  Does it remove user work rather than add configuration?   (5)
  Can the user predict and explain its behavior?            (6)
  Is it reachable from the keyboard, and disableable?       (7)
  Does it behave like Windows software?                     (8)
  Is it worth maintaining for three years?                  (9)
  Is it safe if the note contains a password?              (10)
  Has the architectural decision been recorded?            (11)
```

A feature that fails principle 9 but passes the rest is usually still a no.
That is the point.
