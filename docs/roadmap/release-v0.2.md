# v0.2 plan: owner feedback after v0.1

**Status:** proposed, for the owner to order. **Recorded:** 2026-11-06.
**Related:** [basic-release.md](basic-release.md) (the five rows moved to v0.2), [release-v0.1.md](release-v0.1.md), [roadmap.md](roadmap.md) (M5, M6), [sidenotes-parity.md](../product/sidenotes-parity.md)

The owner used v0.1 next to SideNotes and the new Sticky Notes (OneNote) and listed what Noto should do next. Each item below says what v0.1 does today, what is missing, how it would be built, and which decision records it touches. The priorities at the end are a proposal: the owner orders them.

The v0.1 rule still holds: one user-visible slice per PR, with the same gates.

---

## The items

### F1. Settings page: scroll bar and drop-downs (fix)

**Today:**
- The Settings scroll bar is drawn over the right edge of the cards (`SettingsScroll` in `SettingsPage.xaml`). WinUI's scroll bar overlays the content, and the cards leave it no room.
- The drop-downs (`SettingComboBoxStyle`) keep WinUI's small corner radius, while the cards around them have Noto's larger radius.
- The open list does not line up with the field. WinUI places the list so the selected item sits over the field, and a list wider than a right-aligned field grows to the left.

**Build:**
- Give the scrolled content a right margin as wide as the scroll bar.
- Set the drop-down's corner radius from the same token as the cards.
- Open the list under the field, at the field's width or wider to the right, never shifted.

**Size:** S. **Decision records:** none.

### F2. Opening animation

**Today:** the window appears in place, and only its content moves: a 24 px slide and a fade over 250 ms (`MotionPlan`, `ContentMotion`, the `MotionDrawerEnter*` tokens). Windows' *Animation effects* is on, so it runs, but it is too small to see. The empty window shows up first, so opening feels like a jump, not a slide.

**Build:**
- The whole drawer slides in from the screen edge, its full width, in about 150–200 ms.
- The motion starts fast and slows at the end, so it never feels like loading.
- The window's background moves with it: no empty frame first.
- Closing is the same in reverse, a little faster.
- With *Animation effects* off, the drawer appears at once, as it does now.
- **Check:** the drawer accepts typing on its first frame; the startup and show budgets in [quality-gates.md](../architecture/quality-gates.md) still hold.

**Size:** M. **Decision records:** ADR-007 says "slide-in and auto-hide implemented by Noto". This follows it; the tokens change.

### F3. A shortcut for *Keep Noto open*

**Today:** the pin toggle in the header (`PinToggle`) has no keyboard shortcut. The global hotkey only shows and hides Noto.

**Build:** an in-app shortcut that toggles it and announces the new state. **Ctrl+Shift+K** is proposed. It is free: the shortcuts in use are Ctrl+Shift+C, E, H, L, M, R and V.

**Open question:** should it also work while Noto is hidden, as a second global hotkey? Principle 7 wants every global trigger to be one you can turn off.

**Size:** S. **Decision records:** none.

### F4. The number of notes in each folder

**Today:** a folder row shows the icon, the name and the pin marker. The count is shown only inside the folder (`NoteCountText`).

**Build:**
- The count, right-aligned and quiet, as SideNotes shows it.
- Notes in the Recycle Bin are not counted.
- Screen readers hear it in the row's help text.
- It comes from the folder query, not one query per row.

**Size:** S. **Decision records:** none.

### F5. Folder list layout: *New folder* and *Search*

**Today:**
- The *New folder name* box and its button take the top row of the folder list all the time.
- Search has no visible entry: it opens only with Ctrl+F (`MainWindow.Search.cs`).

**Build:**
- **New folder:** a **+** button, in the header or at the foot of the list next to the Recycle Bin. It opens the name box in place. Ctrl+N stays.
- **Search:** a search button in the header, beside Settings and Pin. It opens the existing search surface, as Ctrl+F does. A full search box was considered; a button keeps the header quiet, and Search is already its own surface.

**Size:** M: layout, focus order and the welcome tour's steps. **Decision records:** [ADR-016](../decisions/ADR-016-card-stack-workspace.md) (the workspace surfaces) and [ADR-018](../decisions/ADR-018-search.md) (how Search opens): check both, no change expected.

### F6. Fold: show what is hidden, and toggle in one click

**Today:**
- A note can be folded, from its context menu.
- A folded card shows only its title and a marker (`NoteCard.ReadText`, `FoldedMarkerVisibility`).

**Missing, compared with SideNotes:**
- a **"+ N lines"** pill on the folded card that says how much is hidden;
- clicking the pill unfolds the note;
- a fold button on the card being edited;
- a shortcut.

**Size:** S–M. **Decision records:** none. The engine already stores `IsFolded`.

### F7. Note colours

Moved here from v0.1 (see [basic-release.md](basic-release.md)).

**Today:** the engine has `NoteColor`; nothing in the interface uses it.

**Build:**
- A colour chosen from the card's menu and its bar.
- The card's background is tinted with the colour.
- A small set of named colours, each with a light and a dark value.
- High Contrast ignores the colours, and the colour is never the only signal.

**Size:** M. **Decision records:** the design tokens. The colours must pass contrast checks in both themes.

### F8. A quick-formatting toolbar on selection

**Today:**
- Formatting comes from shortcuts and from the **Format** button on the card's bar (a `MenuFlyout` that lists every command with its key).
- Nothing appears when text is selected.

**Build:**
- A small toolbar above the selection, as in SideNotes: bold, italic, highlight, strikethrough, underline, link, code, list.
- It uses the same commands as the menu, so there is no second formatting path in the model.
- It is quiet, and can be turned off.
- With the keyboard, it never takes focus away from the text.

**Size:** M–L. **Decision records:** [ADR-017](../decisions/ADR-017-editor-model.md): the toolbar only calls the editor's commands, and the WinUI control is still not the model. Highlight is a new mark, and needs a Markdown form decided in ADR-017.

### F9. Drop text files and pictures into Noto to import them

**Today:**
- Settings › Data imports Markdown and text files through a file picker.
- Dropping a picture into a note being edited adds it to that note.
- Dropping any other file into a note adds a link to it.
- The folder list and the note stack accept only Noto's own drags, for reordering.

**Build:**
- Drop `.txt` or `.md` files on a folder, or on the note stack: each becomes a new note in that folder. This is the importer that already exists, reached by a drop.
- Drop a picture on the stack: a new note that holds the picture.
- Several files at once give one result, with an undo.
- Dropped files are copied, never moved, and the original is never changed.

**Size:** M. **Decision records:** none. It reuses the importer and the picture store.

### F10. Screenshot with its source

**What Sticky Notes does:**
- It takes a screenshot of the page or file in front of you.
- It saves the screenshot with a link back to where it came from.
- One click on the note opens the source again.

**Today:** Noto takes pictures from the clipboard or a drop. It records no source.

**Build (later):**
- Capture a region or a window: `Windows.Graphics.Capture`, or Windows' own snipping tool.
- Record the source:
  - the foreground app and the window title;
  - for a browser, the page address, read through UI Automation;
  - for a document, its path.
- Show the source on the card as a safe link (v0.1's link rules).
- Everything stays on this computer.

**Size:** L. **Milestone:** M5, *screenshot capture* (see [roadmap.md](roadmap.md)). It is also the groundwork for F11.

### F11. Notes that come back when you return to a site or a document

**What it means:**
- Sticky Notes remembers where a note was taken: a web page in Edge, or a document.
- When you open that same page or document again, it shows you the notes you took there.
- You do not search for them: they come back with the work they belong to.

**Today:** nothing. This is Noto's **M6, Contextual Notes**: notes bound to an app, shown when that app comes to the front. Sticky Notes binds to a page or a document. Noto's M6 begins with whole applications, and could extend to pages and documents.

**Before it starts:** M6's own preconditions in [roadmap.md](roadmap.md):
- re-validate [ADR-005](../decisions/ADR-005-context-engine.md) and [ADR-006](../decisions/ADR-006-window-binding-identity.md);
- detect the foreground app from events, never by polling;
- never steal focus.

**Size:** XL. **Milestone:** M6.

### F12. Dock that pushes other windows aside (test first)

**What Sticky Notes does:** *Dock to desktop* reserves a strip at the side of the screen. Other windows shrink to fit beside it. *Undock* gives the space back.

**Today:**
- Noto floats over other windows: a maximised window slides underneath it.
- *Keep Noto open* stops it hiding; it does not reserve space.

**Why not yet:**
- [ADR-007 §4](../decisions/ADR-007-window-presentation.md) rejected the Windows API that reserves space (`SHAppBarMessage`).
- If Noto crashes while docked, the reserved strip may stay until you sign out, and the API has DPI defects.
- Users also report that Sticky Notes' own dock fails with a second monitor and does not survive a restart.

**Build (when reached):**
1. A spike: dock with that API, then force-kill, crash and upgrade Noto while it is docked, on one monitor and on two.
2. **If Windows gives the space back by itself:** amend ADR-007 and offer Dock as a mode beside *Keep Noto open*, off by default.
3. **If not:** keep floating.

**Size:** spike S, feature M. **Priority:** the owner's lowest.

---

## Also in v0.2: moved from v0.1

These were moved on 2026-11-06 (see [basic-release.md](basic-release.md)):

- **F7**, note colours (above);
- **F13**, font and text-size settings;
- **F14**, colour chips for `#rrggbb` codes;
- **F15**, automatic backups and their retention (#15);
- **F16**, detecting and stopping a crash loop (#10).

---

## Proposed order

Fixes and small wins first, then the features you see every day, then the larger ones. The owner reorders as they like.

| # | Item | Size | Why here |
|---|---|---|---|
| 1 | F1 Settings scroll bar and drop-downs | S | A visible defect in a shipped surface |
| 2 | F2 Opening animation | M | You see it every time you open Noto |
| 3 | F3 Shortcut for *Keep Noto open* | S | Small; keyboard-first |
| 4 | F4 Note counts on folders | S | Small; SideNotes parity |
| 5 | F5 *New folder* and *Search* layout | M | The first screen, every day |
| 6 | F6 Fold: "+ N lines" and one-click toggle | S–M | Builds on the existing fold |
| 7 | F7 Note colours | M | Already v0.2; the engine is ready |
| 8 | F9 Drop files to import | M | Reuses the importer |
| 9 | F8 Quick-formatting toolbar | M–L | Needs an ADR-017 note for highlight |
| 10 | F13 Fonts and text size | M | Moved from v0.1 |
| 11 | F14 `#rrggbb` chips | S | Moved from v0.1 |
| 12 | F15 Automatic backups (#15) | M | Moved from v0.1; safety |
| 13 | F16 Crash-loop detection (#10) | M | Moved from v0.1; safety |
| later | F10 Screenshot with its source | L | M5 |
| later | F12 Dock: the spike, then a decision | S + M | The owner's lowest priority; ADR-007 |
| later | F11 Notes that come back with their source | XL | M6, after F10 |

F15 and F16 protect the notes themselves. If the owner prefers safety before polish, they move up to 3 and 4.
