# v0.2 plan

**Status:** planned. **Order set by the owner:** 2026-11-06. **Recorded:** 2026-11-06.
**GitHub:** the [v0.2 milestone](https://github.com/mdsaif45/Noto/milestone/11).
**Related:** [roadmap.md](roadmap.md) (the capability milestones), [basic-release.md](basic-release.md) (the rows moved from v0.1), [release-v0.1.md](release-v0.1.md), [sidenotes-parity.md](../product/sidenotes-parity.md)

v0.2 is three things:
- the v0.1 scope that was not built, moved on 2026-11-06;
- the owner's feedback after using v0.1 next to SideNotes and the new Sticky Notes (OneNote);
- gaps found while checking that feedback against the code.

Work that belongs to a later capability milestone, M5 or M6, is listed at the end and is **not** v0.2.

Each item has one issue in the v0.2 milestone, and a **capability milestone**: the M0–M9 area of [roadmap.md](roadmap.md) it advances. The v0.1 rule still holds: one user-visible slice per PR, with the same gates.

---

## The order

Set by the owner on 2026-11-06: the Settings fix, then the small daily wins, then the moved v0.1 scope, then the rest.

| # | Item | Issue | Capability | Size | Source |
|---|---|---|---|---|---|
| 1 | F1 Settings: the scroll bar and the drop-downs | #144 | M4 | S | feedback |
| 2 | F3 A shortcut for *Keep Noto open* | #146 | M3 | S | feedback |
| 3 | F4 Note counts on folder rows | #147 | M3 | S | feedback |
| 4 | F2 The drawer slides in from its edge | #145 | M2 | M | feedback |
| 5 | F5 A **+** button for new folders, and a search button | #148 | M3 | M | feedback |
| 6 | F7 Note colours | #150 | M3 | M | moved from v0.1, feedback |
| 7 | F13 Font family and text size | #154 | M3 | M | moved from v0.1 |
| 8 | F14 Colour chips for `#rrggbb` | #155 | M3 | S | moved from v0.1 |
| 9 | F15 Automatic backups and retention | #15 | M1 | M | moved from v0.1 |
| 10 | F16 Crash-loop detection | #10 | M0 | M | moved from v0.1 |
| 11 | F6 Fold: "+ N lines", and unfold in one click | #149 | M3 | S–M | feedback |
| 12 | F9 Drop files and pictures to import them | #152 | M3 | M | feedback |
| 13 | F8 A quick-formatting toolbar | #151 | M3 | M–L | feedback |
| 14 | F17 A global hotkey to create a note | #156 | M3 | M | gap |
| 15 | F18 A new note from the clipboard | #157 | M3 | S–M | gap |
| 16 | F19 Clear completed tasks | #158 | M3 | S | gap |
| 17 | F21 A global hotkey to open Search | #159 | M3 | S | gap |
| 18 | F22 Idle memory within 150 MB | #160 | M4 | M | v0.1 budget miss |
| 19 | F12 Dock that pushes other windows aside: a spike | #153 | M2 | S, then M | feedback; the owner's lowest |

**The owner's checks**, in the same milestone and run on the installed v0.1:

| Item | Issue | Capability |
|---|---|---|
| V1 The hand accessibility check | #161 | M4 |
| V2 Mixed-DPI multi-monitor, with the second monitor | #30 | M2 |
| V3 A clean machine, and Windows 10 22H2 | #32 | M4 |
| V4 Score the parity table, update progress | #162 | M3 |

#31, *drag and drop from Explorer*, is verified by F9.

V4 can add work. Every MUST that is still open after scoring gets an issue, and the owner decides whether it joins v0.2.

---

## The items

### F1. Settings: the scroll bar and the drop-downs (#144)

**Today:**
- The Settings scroll bar is drawn over the right edge of the cards (`SettingsScroll` in `SettingsPage.xaml`). WinUI's scroll bar overlays the content, and the cards leave it no room.
- The drop-downs (`SettingComboBoxStyle`) keep WinUI's small corner radius, while the cards around them have Noto's larger radius.
- The open list does not line up with the field. WinUI places the list so the selected item sits over the field, and a list wider than a right-aligned field grows to the left.

**Build:**
- Give the scrolled content a right margin as wide as the scroll bar.
- Set the drop-down's corner radius from the same token as the cards.
- Open the list under the field, at the field's width or wider to the right, never shifted.

### F2. The drawer slides in from its edge (#145)

**Today:** the window appears in place, and only its content moves: a 24 px slide and a fade over 250 ms (`MotionPlan`, `ContentMotion`, the `MotionDrawerEnter*` tokens). Windows' *Animation effects* is on, so it runs, but it is too small to see. The empty window shows up first, so opening feels like a jump, not a slide.

**Build:**
- The whole drawer slides in from the screen edge, its full width, in about 150–200 ms.
- The motion starts fast and slows at the end, so it never feels like loading.
- The window's background moves with it: no empty frame first.
- Closing is the same in reverse, a little faster.
- With *Animation effects* off, the drawer appears at once, as it does now.
- **Check:** the drawer accepts typing on its first frame, and the show and startup budgets in [quality-gates.md](../architecture/quality-gates.md) still hold.

ADR-007 already says "slide-in and auto-hide implemented by Noto"; only the tokens change.

### F3. A shortcut for *Keep Noto open* (#146)

**Today:** the pin toggle in the header (`PinToggle`) has no keyboard shortcut. The global hotkey only shows and hides Noto.

**Build:** an in-app shortcut, **Ctrl+P**, that toggles it and announces the new state.

**Decided (owner, 2026-11-06):** inside Noto only, never global. In-app shortcuts stay short; only global hotkeys need long chords.

Ctrl+K was the owner's first choice, but it is the editor's *link* shortcut. Ctrl+P is free and reads as *pin*: Noto does not print.

### F4. Note counts on folder rows (#147)

**Today:** a folder row shows the icon, the name and the pin marker. The count is shown only inside the folder (`NoteCountText`).

**Build:**
- The count, right-aligned and quiet, as SideNotes shows it.
- Notes in the Recycle Bin are not counted.
- Screen readers hear it in the row's help text.
- It comes from the folder query, not one query per row.

### F5. A **+** button for new folders, and a search button (#148)

**Today:**
- The *New folder name* box and its button take the top row of the folder list all the time.
- Search has no visible entry: it opens only with Ctrl+F (`MainWindow.Search.cs`).

**Build:**
- **New folder:** a **+** button, in the header or at the foot of the list next to the Recycle Bin. It opens the name box in place. Ctrl+N stays.
- **Search:** a search button in the header, beside Settings and Pin. It opens the existing search surface, as Ctrl+F does. A full search box was considered; a button keeps the header quiet, and Search is already its own surface.
- **Update:** the focus order and the welcome tour's steps.
- **Check** [ADR-016](../decisions/ADR-016-card-stack-workspace.md) and [ADR-018](../decisions/ADR-018-search.md): no change is expected.

### F6. Fold: "+ N lines", and unfold in one click (#149)

**Today:**
- A note can be folded, from its context menu.
- A folded card shows only its title and a marker (`NoteCard.ReadText`, `FoldedMarkerVisibility`).

**Build:**
- A **"+ N lines"** pill on the folded card, as SideNotes shows, saying how much is hidden.
- Clicking the pill unfolds the note.
- A fold button on the card being edited, and a shortcut.

The engine already stores `IsFolded`.

### F7. Note colours (#150)

**Today:** the engine has `NoteColor`; nothing in the interface uses it.

**Build:**
- A colour chosen from the card's menu, its bar and the keyboard.
- The card's background is tinted with the colour.
- A small set of named colours, each with a light and a dark value that pass contrast.
- High Contrast ignores the colours, and the colour is never the only signal.

### F8. A quick-formatting toolbar (#151)

**Today:**
- Formatting comes from shortcuts and from the **Format** button on the card's bar (a `MenuFlyout` that lists every command with its key).
- Nothing appears when text is selected.

**Build:**
- A small toolbar above the selection, as in SideNotes: bold, italic, highlight, strikethrough, underline, link, code, list.
- It calls the same editor commands as the menu ([ADR-017](../decisions/ADR-017-editor-model.md)), so there is no second formatting path, and the WinUI control is still not the model.
- **Highlight is a new mark.** Its Markdown form is decided in ADR-017 before it is built.
- It never takes keyboard focus from the text, and it can be turned off.

### F9. Drop files and pictures to import them (#152)

**Today:**
- Settings › Data imports Markdown and text files through a file picker.
- Dropping a picture into a note being edited adds it to that note.
- Dropping any other file into a note adds a link to it.
- The folder list and the note stack accept only Noto's own drags, for reordering.

**Build:**
- Drop `.txt` or `.md` files on a folder, or on the note stack: each becomes a new note in that folder, through the importer that already exists.
- Drop a picture on the stack: a new note that holds it.
- Several files at once give one result, with one undo.
- Dropped files are copied, never moved, and the original is never changed.
- Verified from Explorer, the desktop and a browser, which closes #31.

### F12. Dock that pushes other windows aside: a spike (#153)

**What Sticky Notes does:** *Dock to desktop* reserves a strip at the side of the screen. Other windows shrink to fit beside it. *Undock* gives the space back.

**Today:**
- Noto floats over other windows: a maximised window slides underneath it.
- *Keep Noto open* stops it hiding; it does not reserve space.

**Why a spike first:**
- [ADR-007 §4](../decisions/ADR-007-window-presentation.md) rejected the Windows API that reserves space (`SHAppBarMessage`).
- If Noto crashes while docked, the reserved strip may stay until you sign out, and the API has DPI defects.
- Users also report that Sticky Notes' own dock fails with a second monitor and does not survive a restart.

**The spike:**
1. Dock with that API, then force-kill, crash and upgrade Noto while it is docked, on one monitor and on two.
2. **If Windows gives the space back by itself:** amend ADR-007, and offer Dock as a mode beside *Keep Noto open*, off by default.
3. **If not:** Noto keeps floating.

### F13. Font family and text size (#154)

- A font family and a text size in Settings, applied to the cards and the editor.
- Shortcuts to increase and decrease the size.
- A missing font falls back without breaking the layout.

### F14. Colour chips for `#rrggbb` (#155)

A `#rrggbb` code in a note shows a small swatch beside it. The text is unchanged, and screen readers are not given the swatch as extra text.

### F15. Automatic backups and retention (#15)

v0.1 has manual backup and restore. v0.2 adds scheduled backups and keeps a bounded number of them.

### F16. Crash-loop detection (#10)

Noto notices that it is failing at every start, and stops the loop instead of crashing again.

### F17–F21. Gaps found while checking (#156–#159)

These SideNotes MUST rows are not built, and none of them is in a later milestone:

| Item | What | Parity rows |
|---|---|---|
| F17 | A global hotkey that opens Noto with a new note ready | B2 |
| F18 | A new note from the clipboard's text or picture | B3, D43, G4 |
| F19 | Remove the checked tasks of a note, as one undo step | D22, G34 |
| F21 | A global hotkey that opens Noto with Search | E2 |

New global hotkeys are rebindable and off by default (principle 7).

### F22. Idle memory within 150 MB (#160)

v0.1 measured 152 MB idle against the 150 MB budget. The work was deferred on 2026-11-06 ([quality-gates.md](../architecture/quality-gates.md)).

---

## Not v0.2: later capability milestones

| Item | Issue | Milestone | Why later |
|---|---|---|---|
| F10 A screenshot that keeps its source | #163 | M5 | Screenshot capture is M5 |
| F11 Notes that come back when you return to a site or a document | #164 | M6 | Contextual Notes; ADR-005 and ADR-006 must be re-validated first |

**F10, what Sticky Notes does:**
- It captures the page or file in front of you.
- It saves the picture with a link back to its source.
- One click opens the source again.

Noto would record:
- the app and the window title;
- for a browser, the page address, read through UI Automation;
- for a document, its path.

It keeps the link safe under v0.1's link rules, and everything stays on this computer.

**F11, what it means:**
- Sticky Notes remembers where a note was taken: a page in Edge, or a document.
- When you open that page or document again, it shows you those notes, without a search.
- Noto's M6 binds notes to applications first (#18). F11 extends that binding to pages and documents, and builds on F10's source capture.
