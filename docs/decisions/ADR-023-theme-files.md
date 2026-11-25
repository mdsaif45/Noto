# ADR-023 — Theme files: their format, and the tokens a theme may change

**Status:** Accepted
**Date:** 2026-11-24
**Related:**
- ADR-011 (the design system: semantic tokens, brushes per theme, the note palette keys, high contrast first-class, the `StaticResource`/`ThemeResource` trap), ADR-002 (local-first; principle 10, every input untrusted), ADR-007 (window opacity is `Opacity` on the root XAML element), ADR-008 (Velopack's install and uninstall hooks), ADR-019 and ADR-020 (how Noto treats a file it did not write: checked by its bytes, capped, named by Noto, written whole or not at all), ADR-022 (per-user registration from the installer's hooks; a request from outside Noto)
- Parity F2–F7, F11, J22 (#265); the feature that builds it is #269

---

## Context

Noto's look is ADR-011's tokens. `Noto.UI/Themes/Colors.xaml` holds three theme dictionaries, Light, Dark and HighContrast, each with every colour **and** the brush views bind to (ADR-011's 2026-10-19 amendment, *brushes per theme*). High contrast maps every token to the `SystemColor*` resources, so the user's own palette wins, and the application turns the framework's automatic adjustment off (`HighContrastAdjustment = None`) so those brushes are what is drawn.

The Light/Dark choice (Settings › Appearance › Theme: System, Light, Dark) is applied at run time by `App.ApplyTheme`, which sets `RequestedTheme` on the drawer's content (`WorkspaceContent`). The window around it is see-through (`TransparentBackdrop`, F2) and its root is pinned Dark, so the drawer paints its own background.

The note palette is six stored keys, `note1`…`note6` (`NoteColor.Keys`). A card's colour state (`NoteRow.xaml`, `ColorStates`) reads two brushes per key, `NoteNTintBrush` (translucent, laid over the card) and `NoteNMarkerBrush`, and `NotePalette` gives each key its spoken name (Yellow, Green, Blue, Purple, Pink, Grey).

Two surfaces read colours outside XAML bindings:
- **The editor** (`NoteEditorHost`) builds an RTF document and TOM formats from an `EditorPalette` snapshot: text, markup, link, code surface, card surface, highlight and its text, read from its brushes when it restyles (on `ActualThemeChanged`). Hidden markup is drawn *in the surface colour*, and RTF colours have no alpha.
- **The read view** (`MarkdownReadView`) assigns its brushes to the runs it builds: markup (quotes, list markers, task boxes), link, highlight. In high contrast it sets no colour of its own.

`DesignSystemGuardTests` already holds the built-in colours to WCAG contrast: text 4.5:1 on every fill it is drawn on, the focus ring 3:1, every note tint keeping a card's text at 4.5:1 and its marker at 3:1.

Data lives in one root (`NotoStoragePaths`, `%LOCALAPPDATA%\Noto`): `noto.db`, `attachments\`, `backups\`, `logs\`.

SideNotes has `.sntheme` files: JSON with `light`, `dark` and `any` sections, colours for every surface and the six note colours, three colour formats, and install by double-click. Nothing user-supplied can change a Noto token today.

## Problem

1. What is a theme file, exactly, and which tokens may it change?
2. What can a theme never change, and how is a theme that would make Noto unreadable stopped?
3. What does alpha mean in a theme, given a see-through window and an editor with no alpha?
4. How is a theme installed, chosen, listed, revealed and removed, without a file association?
5. A theme is a file from anywhere. What is it allowed to contain?
6. How are the colours applied at run time without breaking the Light/Dark switch, high contrast, popups, or the editor?

## Options considered

| Option | Verdict |
|---|---|
| **JSON setting a fixed list of colour properties, per light and dark, applied over the built-in dictionaries** | **Chosen.** |
| XAML `ResourceDictionary` files loaded at run time | A XAML file can name any key, any type and any markup extension: it is code, not data. It could replace a focus visual, a template or a high-contrast brush, and a bad one fails as ADR-011 records, silently or with `0xC0000C04`. |
| Accent-only themes (one colour and the note palette) | Safe but too small for parity F4–F5, which name every surface. |

## Decision

### 1. The format

A theme is a UTF-8 JSON file with the extension **`.nototheme`**:

```json
{
  "$schema": "https://raw.githubusercontent.com/mdsaif45/Noto/main/docs/themes/noto-theme.schema.json",
  "version": 1,
  "name": "Harbour",
  "author": "Noto",
  "any":   { "danger": "#c4314b" },
  "light": { "background": "#eef2f5", "text": "#1c2630", "note1": { "tint": "#f5c40040", "marker": "#a6780a" } },
  "dark":  { "background": "#1b2229", "text": "#e6edf3" }
}
```

| Member | Rule |
|---|---|
| `$schema` | Optional string. For editors; Noto never fetches it. |
| `version` | Required integer, `1`. A larger number is refused by name: *made for a newer Noto*. It changes only when a property's meaning changes; new properties do not change it. |
| `name` | Required, 1–64 characters after trimming. What Settings shows; a theme's identity (§4). `Default` is Noto's own and is refused. |
| `author` | Optional, up to 64 characters. Shown in Settings. |
| `light`, `dark`, `any` | Sections of colour properties (§2). At least one must be present, and the theme must set at least one colour. |

- **`any` applies to both light and dark; `light` or `dark` overrides it, property by property** (for a note colour, part by part: `any.note1.name` and `light.note1.tint` combine). A property set nowhere keeps Noto's own value for that mode.
- **A theme does not choose the mode.** Settings' Light, Dark or System decides which section shows; a theme with only `dark` leaves light as Noto's own.
- **Colours are exactly `#rgb`, `#rrggbb` or `#rrggbbaa`** (F6), hex digits in either case. **Alpha is last, as in CSS** (`#rrggbbaa`), not first as in XAML. `#rgb` doubles each digit; no alpha means opaque. Named colours, `rgb()`, and anything else are not colours.
- Comments (`//`, `/* */`) and trailing commas are accepted, since themes are written by hand in editors that allow them.
- **Unknown properties are ignored with a warning, never an error**, at the top level, in a section and in a note colour, so a file written for a newer Noto still loads. The warning names them, which also catches a typo (`backgound`).

### 2. The properties a theme may set

This is the whole list, in every section. The schema (`docs/themes/noto-theme.schema.json`) names the same properties; they change together.

| Property | ADR-011 tokens it sets | Alpha (§5) |
|---|---|---|
| `background` | `SurfaceSidebar`, `SurfaceWindow`; derives `InteractionHover`, `InteractionPressed`, `InteractionSelected` | opaque |
| `card` | `SurfaceCard`, `SurfaceCardSelected`, `SurfaceInput` (the editor is the card); derives `SurfaceCardHover` | opaque |
| `code` | `SurfaceCode` | flattened over `card` |
| `text` | `TextPrimary`, `InteractionText`, `InteractionSelectedText` | opaque |
| `textSecondary` | `TextSecondary` (also Markdown markers) | opaque |
| `textDisabled` | `TextDisabled` | opaque |
| `link` | `TextLink` | opaque |
| `quote` | `TextQuote` (new, §7) | opaque |
| `checkmark` | `TaskMark` (new, §7) | opaque |
| `danger` | `TextDanger` | opaque |
| `accent` | `SelectionIndicator` (the selected row's pill, the edited card's edge) and the native accent keys Noto maps (§7) | opaque |
| `border` | `BorderSubtle` | kept |
| `highlight` | `Mark` | flattened over `card` |
| `highlightText` | `MarkText` | opaque |
| `note1`…`note6` | an object of three optional parts: | |
| `noteN.tint` | `NoteNTint` | kept |
| `noteN.marker` | `NoteNMarker` | opaque |
| `noteN.name` | the colour's name in menus and help text (`NotePalette`), 1–24 characters | — |

- **The note keys are the stored keys.** `note3` is `Ctrl+3` in every theme (ADR-011); a theme repaints a position and renames it, and never rewrites a note.
- **A note colour may be renamed** because a theme that paints `note1` red would otherwise have a screen reader call it Yellow. The name is plain text, shown and read, never interpreted.
- **States are derived, not set.** Hover, pressed and selected fills are the surface mixed toward the theme's `text`: row hover 4 %, selected 8 %, pressed 10 %, card hover 2.5 % (close to the built-in values, which are themselves not derived). They are recomputed for a mode whenever the theme sets `background`, `card` or `text` in it. A theme that set a dark background but kept Noto's light-grey hover would put white text on light grey; deriving the states makes a partial theme coherent.
- **SideNotes' properties map here:** background → `background`, text ×3 → `text`/`textSecondary`/`textDisabled`, link → `link`, button → `accent`, quote → `quote`, mark → `highlight`, code → `code`, checkmark → `checkmark`, `notes.clean` → `card`, the six note colours → `note1`…`note6`. SideNotes' *preview* has no Noto surface (a card shows the note itself, in `text`). Noto's third text level is Disabled: ADR-011's Tertiary is declared but no surface draws it, so a property for it would paint nothing.

### 3. What a theme can never change

- **High contrast.** While Windows high contrast is on, no theme applies: the HighContrast dictionary is never touched (§6), so the user's palette wins exactly as today. Turning high contrast off shows the theme again.
- **Focus.** `BorderFocus` and `BorderFocusInner`, the two-tone ring, and `SystemControlFocusVisual*` are fixed. A theme is checked against them instead (§4).
- **Search matches** (`SearchMatch`, `SearchMatchText`): a fixed pair, readable on any card.
- Spacing, radius, type, motion, icons and layout. Themes are colours (#265 non-goals).
- Everything not in §2.

### 4. Readability: the contrast rules

Every theme is measured, per mode, on the resolved colours: after `any` is merged, states derived and alpha applied (§5). One table of pairs, in Core, is used by the loader **and** by `DesignSystemGuardTests` (which today hold the built-in colours to a subset of it); the built-in Light and Dark pass it.

| Rule | Pairs |
|---|---|
| **Text, 4.5:1** (WCAG 2.2, 1.4.3) | `text` on background, card, card hover and each row state; `textSecondary` on background and card; `link`, `quote` on card; `danger` on background; `text` on `code`; `highlightText` on `highlight`; `text`, `textSecondary` and `link` on every note tint over the card and over card hover |
| **Non-text, 3:1** (1.4.11) | `checkmark` on card; `accent` on background and on the selected fill; every `noteN.marker` on card; the focus ring on background, card and every row state, measured as its stronger tone (that is what the two tones are for) |
| **Disabled text, 2:1** | `textDisabled` on background. WCAG exempts inactive controls; a command that vanishes is still a broken window. The built-ins are 2.5:1 (Light) and 3.6:1 (Dark). |
| **The mode is honest** | In `light` the background is lighter than `text`; in `dark`, darker. Native controls Noto does not restyle follow the mode, so dark colours in `light` would put them on the wrong ground. |

Secondary text is held to 4.5:1, not 3:1: WCAG makes no exception for it, it is real reading text in Noto (markers, quotes, settings hints), and the built-in themes already meet it.

**How a failure is handled depends on when it is found:**
- **At install: refused, with the reason.** Nothing is copied. The message names the first failures in words, for example *Secondary text on a card is 3.1:1 in dark; it needs 4.5:1*. The author can fix and try again.
- **At load** (a start, or choosing the theme), for an installed file edited since: **fallback per token**. For each failing pair the theme's foreground is dropped (Noto's own value used), the pairs are measured again, and if a pair still fails its background is dropped too. This ends, since Noto's own colours pass every pair; at worst a mode is entirely Default. The window says so once: *Some of Harbour's colours weren't readable, so Noto's own are used for them.*

### 5. Alpha (F11)

| Where | What Noto does | Why |
|---|---|---|
| `background`, `card` | **Opaque**: alpha is set to `ff`, with a warning | The window is see-through (`TransparentBackdrop`): a translucent background shows the desktop, and text over an unknown picture cannot be measured. The card is also the editor's surface, and the editor hides markup by drawing it in the surface colour through RTF, which has no alpha: the hidden markers would show. |
| Text colours, `accent`, `noteN.marker` | **Opaque**, with a warning | The editor's RTF has no alpha, so the editor and the card would disagree; and a translucent colour's contrast depends on what is behind it. |
| `code`, `highlight` | **Flattened over `card`** | They are drawn only inside a note, on the card. Flattening gives the colour the author sees, as one opaque colour the editor can use. |
| `noteN.tint`, `border` | **Kept** | A tint is translucent by design (Noto's own are), so hover and selection show through; it is measured as composited over the card. A border is decoration, measured against nothing. |

**Window translucency is not a theme colour.** F11's translucent drawer and edge handle are ADR-007's `Opacity` on the root element, which fades text and background together and keeps their contrast. That is a separate setting, still provisional in the parity table.

### 6. Applying a theme at run time

**Noto changes the colour of the brushes it already has.** At start, before any theme, Noto records the colour of every brush §2 can reach in Colors.xaml's Light and Dark dictionaries. Applying a theme sets `SolidColorBrush.Color` on those same brush objects, Light's from the light section and Dark's from the dark; choosing Default sets the recorded colours back.

This works with everything that is already true:
- **Light/Dark keeps working.** Each theme dictionary has its own brush objects (ADR-011, *brushes per theme*), so the light ones carry the light section and the drawer's `RequestedTheme` still picks between them.
- **High contrast is untouched by construction.** The HighContrast dictionary's brushes are never written.
- **Every consumer follows,** whatever its kind of reference: a `ThemeResource` in a view, a style setter, a template, a flyout or menu (popups are outside the drawer's element tree), and the aliases inside Colors.xaml (`TextControlForeground` and the others are `StaticResource`s of the same brush object). A brush is a dependency object; setting its colour repaints what uses it.
- **No `StaticResource` trap.** Nothing is added to or removed from a dictionary, nothing is re-resolved, and no dictionary is reparsed. ADR-011's 0xC0000C04 failure is about resolving keys; this resolves none.
- **No flash.** The theme is read and applied before the window is first shown.

What #269 must also do, because they read colours outside bindings:
- **The editor** restyles after a theme is applied, the same path `ActualThemeChanged` takes, since its `EditorPalette` is a snapshot.
- **The read view** renders its cards again, so every run and highlighter takes the new colours.
- **Colour-typed aliases become brush aliases.** A few native-control keys in Colors.xaml alias a *colour* (`ToggleSwitchFillOffPointerOver` → `InteractionHoverColor`); a colour is a value copied when parsed, so it would not follow. They alias the brush instead.

**Theme files are read at start and when chosen in Settings, never watched.** An edit to an installed file shows at the next start, or when the theme is chosen again (choose Default, then it). That is all an author needs, and it needs no watcher, no debounce and no partly-written file.

### 7. Two new tokens and the accent

- **`TextQuote`** and **`TaskMark`** join ADR-011's text group, in all three dictionaries. Light and Dark: the same values as `TextSecondary`, so Noto's own look does not change. High contrast: `SystemColorGrayTextColor`, as `TextSecondary`. The read view's quotes and task boxes, and the editor's quotes and checkbox overlay, use them instead of the markup brush. ADR-011 rule 4 is met: a surface (a theme's quote and checkmark colours) needs them.
- **`accent`**, when a theme sets it, replaces the Windows accent in `SelectionIndicatorBrush`, and #269 maps the native accent keys Noto's surfaces show (a toggle that is on, a focused input's underline) to that brush in Light and Dark, so one accent shows everywhere. Not set, everything is the Windows accent, as today.

### 8. Install, choose and manage (F7, J22)

Settings › Appearance has two rows. The existing *Theme* row (System, Light, Dark) is renamed **Mode**, as Windows' own Settings names it; the new row is **Theme**: a list of *Default* and the installed themes by name (with their author), and three buttons:

- **Install theme…** opens Windows' file picker (`.nototheme`, `.json`). The file is checked completely (§9, §1, §4). Refused, the reason is shown and nothing changes. Accepted, it is copied into **`<data root>\themes\`**, chosen, and applied at once; warnings (unknown properties, alpha made opaque) are shown in the notice line.
- **Show in Explorer** opens Explorer with the chosen theme's file selected.
- **Remove** sends the chosen theme's file to the Windows Recycle Bin, so it can be restored: it may be the only copy, if the author edited it there. Removing the active theme switches to Default at once.

The rules:
- **The installed copy is named by Noto**, from the theme's `name`, by ADR-020's export rules (characters Windows forbids become `_`, reserved device names get `_`, 80 characters at most), plus `.nototheme`. The source file's own name is never used (ADR-019: a dropped name is untrusted). The bytes are copied unchanged, comments included, written to a temporary file in `themes\` and renamed into place: whole or not at all.
- **A theme's identity is its name, ignoring case.** Installing a theme whose name is already installed asks *A theme named Harbour is installed. Replace it?*, defaulting to Cancel; replacing the active theme applies the new one at once. Two different names that give the same file name take ` (2)`, ` (3)`.
- **The choice is a setting**, `appearance.theme-file`: empty for Default, or the installed file's name. The setting refuses anything that is not a bare `*.nototheme` file name (no separator, no `..`), so no value, a restored one included, can point outside `themes\`.
- **The list is the folder.** Settings reads `themes\` each time it opens, off the UI thread, at most 100 files. A file put there by hand is checked as at load (§4); one that cannot be read at all is listed as *can't be read* and cannot be chosen.
- **`themes\` is created at the first install,** not at start: Noto can start without it (unlike `attachments\` and `backups\`).
- **A theme that can't be read at start** (missing, corrupt, too large, a newer version) is not applied: Noto uses Default and says so once in the notice line, *Harbour couldn't be read, so Noto is using its own colours*. The setting is kept, so a file fixed later applies at the next start.

**No file association, no double-click install, in this step.** The package is not needed for one (the `noto://` scheme is registered per user by Velopack's install and update hooks, ADR-022), but it is not small: registry writes in those hooks and their removal on uninstall, a new request from outside Noto through ADR-013's pipe and ADR-022's grammar, and a confirmation, since a double-click on a downloaded file would otherwise change Noto's look unasked. That is its own change to ADR-008 and ADR-022, recorded under *Future reconsideration criteria*.

### 9. A theme is untrusted input

A theme can come from any web page. It is data, and only data:
- **JSON only.** No XAML, no markup extension, no code, no script. A colour is turned into bytes by Noto's own parser of the three formats, and into a brush by code; nothing in the file reaches `XamlReader` or a XAML type converter.
- **No URLs fetched, no images, no fonts, no paths.** `$schema` is a string Noto ignores. The file names no other file.
- **Size: 64 KiB at most** (a complete theme is about 2 KiB), checked before reading, and never more than that read.
- **Parsing** is `System.Text.Json`'s `JsonDocument`, with `MaxDepth = 8` (a theme is three levels deep), comments skipped, trailing commas allowed. Invalid UTF-8 is refused. **A duplicated property is refused**: which one wins would be a guess.
- **Strings** (`name`, `author`, `noteN.name`) are trimmed, length-capped, and refused if they hold control or format characters (Unicode Cc, Cf), so a name cannot hide text or reverse its direction in Settings.
- **Types are strict.** A colour that is not one of the three formats, or a property of the wrong type, is an error: refused at install, that property ignored (Noto's own colour) at load, with the notice.
- **Nothing is executed and nothing is sent.** Local-first holds (ADR-002): installing, choosing and removing a theme touch only `themes\` and one setting.

### 10. Example and schema

- **Noto ships no theme files.** Default is Colors.xaml, as today. A bundled theme would be a second look to hold at contrast in every release, and an example in the install folder would be one users edit and lose on update.
- **One example, `docs/themes/example.nototheme`** ("Harbour": light, dark and `any`, every property, all three colour formats, a kept and a flattened alpha), is the documentation and #269's test fixture. It passes every rule in §4 with no warning.
- **The schema, `docs/themes/noto-theme.schema.json`** (JSON Schema draft 2020-12), is published at the `$schema` URL above, for editors' completion and checking. #269 embeds the same file; Noto's own parser does the checking (no schema validator dependency), and a test holds the parser's property list equal to the schema's.

### 11. Where it lives

- **Core:** the format (parse, colour formats, merge, alpha), the derived states, the contrast table and the fallback; pure and tested. The WCAG contrast arithmetic moves here from the guard test, so there is one copy.
- **Infrastructure:** `NotoStoragePaths.ThemesDirectory`; the theme store (list, install through a temporary file, replace, remove to the Recycle Bin).
- **Windows:** applying colours to the brushes and restoring them, the editor and read-view refresh, Settings' Mode and Theme rows, the notices.

---

## Rationale

**A fixed list is what makes a theme safe.** A theme can only reach the brushes §2 names, so it cannot reach focus, high contrast, templates or layout. A XAML file could reach all of them.

**Every rule is checkable before anything is drawn.** The colours are known numbers, measured against the same table the built-in themes are held to, so "never an unreadable window" is a test, not a hope.

**Refuse at install, fall back at load.** At install the author is there to fix the file, so the reason is worth more than a partial theme. At a start nobody is there, and refusing would mean a theme that worked yesterday vanishes; a per-token fallback keeps what is readable.

**Changing the brushes' colours is the smallest mechanism that reaches everything.** The other ways to override resources miss popups, style setters or the aliases inside Colors.xaml (see *Alternatives rejected*). Setting a colour on a brush every consumer already holds misses none of them, keeps Light/Dark and high contrast as they are, and resolves no key, so ADR-011's trap does not apply.

**The format is Noto's, not SideNotes'.** Parity is "users can install and author themes" (parity table, F2–F7). Property names follow Noto's roles; §2 maps SideNotes' so a port is mechanical.

---

## Consequences

### Positive

- Users can install, author, choose, reveal and remove colour themes (F2–F7 but double-click, J22), applied live.
- A theme cannot make Noto unreadable, hide focus, or override high contrast.
- One contrast table for built-in and installed themes.
- A theme file is small, editable in any editor with completion from the schema, and carries comments.

### Negative

- **No double-click install** until the file association step (§8).
- **Themes are not in backups.** ADR-020's archive holds the database, its settings and images, not `themes\`; a restore elsewhere keeps the setting and falls back to Default, with the notice, until the theme is installed again.
- **Edits are not live.** An author re-chooses the theme or restarts to see an edit.
- **The mutation is process-wide.** Every window uses the same Light and Dark brushes, so there is one theme for all of Noto; per-window themes would need another mechanism (none is planned).
- Two tokens (`TextQuote`, `TaskMark`) and the renamed *Mode* row: small changes to existing views and to the Settings harness's names.
- **Unproven until #269's first test:** that WinUI returns the same brush object to every consumer of a theme dictionary key, so a colour set on it reaches all of them. It is how resource dictionaries are documented to behave and what the aliases already rely on; if it proves false, this ADR is revisited before #269 goes further.

### Rules, binding

1. **A theme sets only the properties in §2.** A new property needs a token, a row in §2, the schema, and its contrast pairs, in one change.
2. **Never written by a theme:** the HighContrast dictionary, the focus tokens, the search-match pair.
3. **Every installed theme passes §4 at install; every loaded one is made to pass it by fallback.** No colour from a file is drawn unmeasured.
4. **A theme file is data.** Nothing from it reaches `XamlReader`, a type converter, a path, a URL or a process.
5. **One contrast table** (Core), used by the loader and `DesignSystemGuardTests`.
6. **Every alias of a token a theme can change names the brush, not the colour.**
7. **Default is the recorded colours, exactly.** Choosing Default after a theme restores every brush §2 reaches.

### Testing requirements

- [ ] **Core:** the three colour formats, alpha last, either case; refusal of `#12`, `#12345`, named colours, `rgb()`, `{ThemeResource …}`; `any` merged with `light`/`dark` per property and per note part; unknown properties warned at every level; duplicates refused; `version` 2 refused; the size and depth caps; comments and trailing commas accepted; name rules (empty, too long, `Default`, control and format characters); alpha made opaque, flattened and kept, each where §5 says; derived states; every pair in §4; fallback ending at Default for the worst theme; the mode rule.
- [ ] **The example** (`docs/themes/example.nototheme`) parses with no warning and passes every pair; it is valid against the schema.
- [ ] **Guard:** the parser's property list equals the schema's; `TextQuote` and `TaskMark` in all three dictionaries; no alias of a settable or derived token names a colour; the built-in Light and Dark pass the full §4 table.
- [ ] **Infrastructure:** install writes the bytes unchanged through a temporary file; Noto's naming, ` (2)`, replace by name; remove to the Recycle Bin; listing skips other files and stops at 100; the setting refuses a path.
- [ ] **Runtime (#269's harness):** install and choose a theme, and every surface shows it, a context menu and the editor included; Mode switches keep it; high contrast on shows high contrast, off shows the theme; Default restores Noto's colours exactly (measured); a corrupt active theme at start gives Default and one notice; removing the active theme gives Default.

---

## Alternatives rejected

| Option | Why |
|---|---|
| **XAML theme files** | Code, not data: any key, any type, focus and high contrast included, and ADR-011's silent failure modes. |
| **Accent-only themes** | Too small for F4–F5. |
| **An override dictionary on the drawer's content** (`WorkspaceContent.Resources.ThemeDictionaries`) | Reaches only elements under the drawer: flyouts, menus and dialogs are outside its tree. Style setters and Colors.xaml's own aliases resolve in their own dictionaries and would keep the old brushes, so each alias would have to be copied into the override. |
| **Replacing Colors.xaml's dictionaries with new brush objects** | Elements already hold the old brushes; they re-resolve only on a theme change, so it needs a Light/Dark flip to show, which flashes. |
| **Refuse a failing theme at load too** | A theme that worked yesterday would vanish after an edit, at a start where nobody chose anything. |
| **Fix failing colours automatically** (lighten or darken until they pass) | Changes the author's colours in ways they did not choose and cannot predict. A fallback to Noto's own is honest and visible. |
| **Allow alpha on the background** | The window is see-through, so text would sit on the desktop. Translucency is ADR-007's root `Opacity`. |
| **Secondary text at 3:1** | WCAG has no such exception for text, and Noto's secondary text is read, not decoration. |
| **A file watcher for live edits** | A watcher, a debounce and half-written files for an authoring convenience; choosing the theme again does the same. |
| **A JSON Schema validator at run time** | A new dependency for checks the parser has to make anyway (alpha, contrast, names). The schema is for editors; a test keeps it and the parser in step. |
| **Built-in example themes in the install** | A second look to maintain at contrast every release; the docs example serves authors and tests. |

---

## Future reconsideration criteria

Revisit if:

- **double-click install** is wanted (parity F7's last clause): add `.nototheme` per-user registration to Velopack's install, update and uninstall hooks as `noto://` is (ADR-008, ADR-022), an install request through ADR-013's pipe, and a confirmation before anything changes;
- #269's first test shows a brush from a theme dictionary is not one shared object (§6);
- users ask for themes in backups (ADR-020's archive gains `themes\`), for live reload while authoring, or to set the theme from the command line (F9);
- a surface needs a token §2 cannot reach, or a theme needs fonts or sizes (a non-goal today);
- WinUI gains supported per-window translucency, which would change §5's answer for the background;
- WCAG's contrast model is replaced (WCAG 3's APCA), which would change §4's numbers, not its structure.

---

## Amendment: as built (#269)

Where #269 could not do exactly what this ADR says, or chose between options it allows:

- **The six switch aliases stay colours** (§6, rule 6). `ToggleSwitchFillOffPointerOver`, `…FillOffPressed`, `…FillOffDisabled`, `…StrokeOffPointerOver`, `…StrokeOffPressed` and `…StrokeOffDisabled` are animated by WinUI's switch template as colours (a `ColorKeyFrame`); given a brush the switch draws no outline (measured, held by `DesignSystemGuardTests`). They stay colour aliases, and the theme's colour is written into those six entries of the Light and Dark dictionaries instead. A switch templated after a theme is applied (Settings is built each time it opens) takes it; one already on screen keeps the old colour until Settings is opened again. Every other alias a theme reaches names the brush.
- **Colors.xaml is merged several times** (each style dictionary merges what it uses), so the brushes of *every* instance of it are recorded and set alike. Only brushes and entries a theme set are written back for Default, so a brush no theme reached (the Windows accent's) keeps its own `ThemeResource`.
- **The shared-brush assumption** (*Consequences*) is checked at run time, not assumed: each apply traces `shared=True|False`, whether the drawer's own background is one of the very brushes set. `Invoke-Visual.ps1` case 19 asserts `shared=True`.
- **The accent** reaches `SelectionIndicatorBrush` and an on switch (`ToggleSwitchFillOn`, and its pointer-over and pressed fills at WinUI's 0.9 and 0.8). **A focused input's underline is not mapped**: WinUI draws it with a gradient brush (`TextControlElevationBorderFocusedBrush`) whose stops cannot follow a brush; it stays the Windows accent.
- **The accent is measured only when a theme sets it** (§4): otherwise it is the user's Windows accent, not a theme's or Noto's to measure, as `DesignSystemGuardTests` already treats it.
- **A property that can't be read at load** (wrong type or format) counts as a fallback: the notice is the fallback one.
- **Note colour names** follow the drawer's mode: a theme's light names in Light, its dark names in Dark.
- **The editor's task checkbox** was drawn in the primary text; it now takes `TaskMark` (the secondary text under Default), as the read view's task boxes always did.
- **The schema is read from `docs/themes` by the test** that holds it equal to the parser's property list, not embedded: nothing at run time reads it.
