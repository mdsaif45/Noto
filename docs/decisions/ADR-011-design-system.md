# ADR-011 — A token-based design system, defined before the screens

**Status:** Accepted
**Date:** 2026-09-14

---

## Context

SideNotes parity means building many surfaces: sidebar, note list, editor,
folder tree, search, preferences panes, floating notes, dialogs, toasts,
context menus.

If each is built with values chosen in the moment, the result is the familiar
failure:

```
  sidebar      padding 12, radius 8,  #2B2B2B
  note card    padding 13, radius 11, #2C2C2C
  editor       padding 16, radius 8,  #2B2C2B
  dialog       padding 20, radius 12, #2A2A2A
```

Nothing is *wrong*, but nothing is consistent, and the first serious visual
change becomes a sweep through every file. For a product whose stated goal is a
"clean, minimal, modern, distinctly Noto" interface, that is the difference
between a design that can evolve and one that is frozen by its own sprawl.

There is no UI yet. Tokens are nearly free to define now and expensive to
retrofit.

---

## Problem

How is Noto's visual language defined so that it is consistent across many
surfaces, can change without editing every screen, and is distinctly Noto
rather than a SideNotes clone?

---

## Decision

**A token-based design system, defined in `Noto.UI` as XAML resources before
the first production screen is built.**

### Layers

```
  PRIMITIVES     raw values          #1F1F1F, 8px, 14px, 200ms
       │         never used directly by a view
       ▼
  SEMANTIC       role-named tokens   SurfaceSidebar, TextSecondary,
       │                             SpaceM, RadiusCard, MotionFast
       ▼
  COMPONENTS     composed controls   NoteCard, FolderRow, SearchBox,
                                     Toolbar, Toggle, Dialog, Toast
```

**Views consume semantic tokens and components. Never primitives.**

That indirection is the whole point: changing dark-mode surface colour means
editing one primitive, not sixty views. A view that hardcodes `#1F1F1F` has
opted out of that, so the rule is absolute.

### Token groups

| Group | Contents |
| ----- | -------- |
| **Spacing** | A single scale: 2, 4, 8, 12, 16, 24, 32, 48. No value outside it. |
| **Radius** | Small, Medium, Large, Pill — named by role, not number. |
| **Typography** | Display, Heading, Subheading, Body, BodyStrong, Caption, Code. Each fixes family, size, weight, line height. |
| **Color — surfaces** | Window, Sidebar, Card, Floating, Overlay, Dialog, Input |
| **Color — text** | Primary, Secondary, Tertiary, Disabled, OnAccent, Link |
| **Color — accent** | Accent, AccentHover, AccentPressed, AccentMuted |
| **Color — semantic** | Success, Warning, Danger, Info |
| **Color — note palette** | The user-selectable note colours, defined **as palette keys** so they adapt to theme — the data model stores the key, never a hex value |
| **Borders** | Subtle, Default, Strong, Focus |
| **Elevation** | Flat, Raised, Floating, Overlay |
| **Motion** | Instant, Fast (~120ms), Normal (~200ms), Slow (~320ms), plus easing curves |
| **Interaction states** | Rest, Hover, Pressed, Selected, Disabled, **Focused** |

### Theming

Every colour token is defined for **light and dark**, and follows the system
theme by default. No token may exist in only one theme — a missing dark value
is a bug, not a fallback.

High contrast is a first-class theme, not an afterthought, because parity
includes accessibility and retrofitting it is expensive.

### Focus is mandatory

Principle 7 makes Noto keyboard-first. That requires a **visible focus state on
every interactive component**, defined as a token and applied uniformly.

A keyboard-first product where the user cannot see what has focus is not
keyboard-first. This is called out explicitly because it is the most commonly
skipped state.

### Component library before screens

Shared components are built and reviewed in isolation before being assembled
into surfaces:

```
  NoteCard   FolderRow   TagChip    SearchBox    Toolbar
  IconButton Button      Toggle     ContextMenu  Dialog
  Toast      EmptyState  Separator  ScrollHost
```

A screen should be composition, not invention.

### Not a SideNotes clone

Parity is **functional**, not visual. Noto reproduces interaction concepts —
the edge drawer, folders, the note list — in its own visual language, and
replaces macOS mechanisms with Windows ones:

```
  SideNotes (macOS)          Noto (Windows)
  ───────────────────        ──────────────────────────
  menu bar item              system tray
  macOS vibrancy             Mica
  macOS system font          Windows system font
  iCloud                     local-first (ADR-002)
  AppleScript                URI protocol, CLI, PowerShell
```

Noto should look like a well-made Windows application, and like itself.

---

## Rationale

**Cheap now, expensive later.** Tokens before screens cost a day. Tokens after
twenty screens cost a rewrite of twenty screens.

**It is the concrete answer to "easy to change in future."** A design system is
what makes that true for the UI, exactly as ADR-009 makes it true for the
domain.

**It serves parity directly.** SideNotes has themes, font size controls and
note colours. Those are parity features, and each is trivial with tokens and
painful without.

**It keeps the palette honest.** Storing a palette *key* rather than a hex
value (already in the data model) only works if a palette exists to resolve
against. This ADR is what makes that decision coherent.

---

## Consequences

### Positive

- consistent surfaces by construction
- theme changes edit one file
- new screens compose rather than invent
- accessibility and focus handled once, uniformly
- the visual identity can evolve without touching layout code

### Negative

- upfront work before any visible feature
- contributors must look up a token instead of typing a value
- some genuine one-offs will feel constrained by the scale
- an over-elaborate system would itself become overhead — so it stays small
  and grows only when a real surface needs it

### Rules, binding

1. **No hardcoded colour, spacing, radius, font size or duration in any view.**
2. Every colour token is defined for light **and** dark.
3. Every interactive component has a visible focus state.
4. New tokens are added when a surface needs one — not speculatively.
5. A component is built once and reused; a second copy is a bug.

### Testing requirements

- [ ] A check that flags hardcoded colour and spacing literals in XAML
- [ ] Every semantic token resolves in light, dark and high contrast
- [ ] Component gallery page rendering every component in every state, in each
      theme — the cheapest possible visual regression surface
- [ ] Keyboard focus is visible on every interactive component

---

## Alternatives rejected

| Option | Why |
| ------ | --- |
| **Style as you go** | The failure this ADR exists to prevent. |
| **A third-party control library** | A large dependency, a foreign visual identity, and Noto's surfaces are unusual enough that it would be fought as often as used. |
| **WinUI defaults only** | A reasonable starting point, but insufficient: note colours, the drawer, floating notes and density have no default. |
| **A full design-system package with docs site and versioning** | Disproportionate for one desktop application. |

---

## Future reconsideration criteria

Revisit if:

- the token set grows unwieldy and needs restructuring rather than extension
- Noto ships a second surface (a settings app, an installer UI) that should
  share tokens, justifying extraction into its own project
- the ADR-001 gate fails and Noto moves to WPF — tokens survive as a concept,
  but the resource mechanism is rewritten

Do **not** abandon tokens for expedience while building a screen. That is the
moment the system is worth the most.
