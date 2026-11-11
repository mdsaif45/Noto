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

ADR-007 already says "slide-in and auto-hide implemented by Noto".

**Built (#145):** the window still never moves. It is see-through, and the whole drawer, background and all, slides its own width inside the docked rectangle: in over 167 ms (`ControlFastAnimationDuration`), out over 83 ms (`ControlFasterAnimationDuration`), both on WinUI's fast-then-settling curve. The window's content is pinned Dark with no fill, because WinUI fills a Light content with opaque white under everything (measured); the chosen theme is set on the drawer below it, and *System* is read from Windows and followed live.

**Fixed after the owner's recording (2026-11-07):** a resizable window draws its top sizing border (a light strip) and an outline under transparent pixels, so both showed before the drawer arrived and as it left. The docked window now has no frame at all (no sizing border, caption or border), and the inner edge resizes from a 6 DIP grip inside the drawer (a WinUI non-client region) rather than from Windows' frame just outside it.

**Owner's choice (2026-11-10):** look B, flush to the edge with the two inner corners rounded (`RadiusMedium`, 8) and a 1 px `BorderSubtleBrush` border on the inner edge, top and bottom, all drawn on the drawer so they slide with it. A start is kept off the screen (cloaked) until the drawer is docked and drawn, then slides in as a shortcut show does; before, it showed a black window with a frame line for about half a second (the owner's recording, 2026-11-10).

### F3. A shortcut for *Keep Noto open* (#146)

**Today:** the pin toggle in the header (`PinToggle`) has no keyboard shortcut. The global hotkey only shows and hides Noto.

**Build:** an in-app shortcut, **Ctrl+D**, that toggles it and announces the new state.

**Decided (owner, 2026-11-06):** inside Noto only, never global. In-app shortcuts stay short; only global hotkeys need long chords.

Ctrl+K was the owner's first choice, but it is the editor's *link* shortcut. Ctrl+P is reserved for printing (parity B21, G25). Ctrl+D is free in the chord map ([implementation-plan.md](implementation-plan.md)), and the owner chose it on 2026-11-06. It is not the note pin, which has no chord (decision 3).

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

**Built (#148):**
- The **+** is at the foot, right of the Recycle Bin: the header is shared by every surface, and a new folder belongs to the folder list.
- The name box is shown when there are no folders yet, when asked for (+, Ctrl+N, the menu's *New folder*) or while it holds a name. It closes once the folder is made, on Escape, or when focus leaves it empty.
- The search button sits first in the header, so Tab reaches Search, Settings, then Pin.
- The welcome tour names both.
- ADR-016 and ADR-018 are unchanged: Search is still its own surface, opened the same way.

### F6. Fold: "+ N lines", and unfold in one click (#149)

**Today:**
- A note can be folded, from its context menu.
- A folded card shows only its title and a marker (`NoteCard.ReadText`, `FoldedMarkerVisibility`).

**Build:**
- A **"+ N lines"** pill on the folded card, as SideNotes shows, saying how much is hidden.
- Clicking the pill unfolds the note.
- A fold button on the card being edited, and a shortcut.

The engine already stores `IsFolded`.

**Built (#149):**
- A folded card that hides anything shows a **"+ N lines"** pill under its title. N counts the lines below the title that hold anything; blank lines hide nothing (`NoteTitle.LinesBelow`). A folded one-line note shows no pill.
- Clicking the pill unfolds the note, focus on its card. It is not a tab stop: a card stays one stop, and Ctrl+Alt+Right unfolds it from the keyboard.
- The card being edited has a **Fold** button on its bar: it saves, folds and leaves editing, as Ctrl+Alt+Left does there. The shortcut already existed (G19–G22).
- Screen readers hear the card as "Folded, N lines hidden", and the pill as "Unfold, N lines hidden".

### F7. Note colours (#150)

**Today:** the engine has `NoteColor`; nothing in the interface uses it.

**Build:**
- A colour chosen from the card's menu, its bar and the keyboard.
- The card's background is tinted with the colour.
- A small set of named colours, each with a light and a dark value that pass contrast.
- High Contrast ignores the colours, and the colour is never the only signal.

**Built (#150):**
- `note1`–`note6` are named Yellow, Green, Blue, Purple, Pink and Grey (`NotePalette`).
- They are chosen from the card's menu (*Colour*, with *No colour*), the Colour button on the card bar, or Ctrl+1 to Ctrl+6, with Ctrl+0 to clear (G23, G24). The keys work in the editor too, and editing keeps its text.
- A coloured card gets a translucent tint over its fill, so hover and selection still show, and a 4 px marker along its leading edge. Both come from the card template's `ColorStates`, so they follow the drawer's theme.
- Contrast: every tint keeps text at 4.5:1 or better on the card, hover and selected fills, and every marker is 3:1 or better against the card (`DesignSystemGuardTests`). Dark tints are fainter (8–13%) than light ones (25%) to keep that.
- High Contrast: the tint has opacity 0 and the marker is drawn in the window text colour. The colour is named in the card's help text and announced when it changes.
### F8. A quick-formatting toolbar (#151)

**Today:**
- Formatting comes from shortcuts and from the **Format** button on the card's bar (a `MenuFlyout` that lists every command with its key).
- Nothing appears when text is selected.

**Build:**
- A small toolbar above the selection, as in SideNotes: bold, italic, highlight, strikethrough, underline, link, code, list.
- It calls the same editor commands as the menu ([ADR-017](../decisions/ADR-017-editor-model.md)), so there is no second formatting path, and the WinUI control is still not the model.
- **Highlight is a new mark.** Its Markdown form is decided in ADR-017 before it is built.
- It never takes keyboard focus from the text, and it can be turned off.

**Built (#151):**
- **Highlight is `::text::`**, SideNotes' syntax, recorded in ADR-017's amendment (F8) and the Markdown model. `::` never opens or closes inside a word, so `std::vector` and `fe80::1` stay text. It is drawn from the theme (`MarkBrush`, `MarkTextBrush`) in the editor and on the card, and in the user's Highlight colours in High Contrast. Toggled by `MarkdownCommands.ToggleHighlight`, from the toolbar and the Format menu; no key yet.
- **The toolbar**: bold, italic, highlight, strikethrough, underline, link, code and bulleted list, above the selection (below it when there is no room above). Each button runs the editor's own command, as its key and the Format menu do. Its buttons are not tab stops and take no focus on a click.
- It shows once a selection is made (the pointer released, or the keys), and goes with a caret, a click, or the editor losing focus.
- Settings › Appearance › *Formatting toolbar* turns it off (`editor.format-toolbar`, default on).

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

**Built (#152):**
- Files dropped on a folder's row, or on its note stack, are imported into that folder (`MarkdownImporter.ImportFiles`): each `.md`, `.markdown` or `.txt` file a new note with its text exactly, the pictures it refers to beside or below it copied in, as a folder import does; each picture a new note titled by its name and holding a copy. They go first in the folder, in the order dropped, in one transaction.
- Anything else (a folder, a document, a program) is skipped and named on the notice line, with why.
- One drop is one result on the status line ("Imported 3 notes.") and one **Undo**, which moves those notes to the Recycle Bin. Undo is offered until anything else is said there, or the surface changes.
- The files are only read: copied, never moved or changed (their bytes and times are checked).
- A drop on the card being edited is still the editor's: the files are added to that note.
- A real OLE file drag, the data Explorer and the desktop give, is verified by `Invoke-DropImport`. Explorer, the desktop and a browser as the source remain the owner's check under #31: a harness cannot drive their windows.

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

**Built (#154):**
- Two settings, `appearance.note-font` and `appearance.note-size`. Their allowed values are in Core (`NoteText`): the design system's family (stored as empty) plus Segoe UI, Calibri, Cambria, Georgia, Verdana and Consolas; and sizes 10–16, 18, 20, 22 and 24, default 14.
- A closed list of families Windows ships keeps the setting testable. Listing every installed font needs DirectWrite enumeration; it can come later if wanted.
- The family and size are set on each card's container. The read view and the editor inherit them, and scale headings by the design system's ratios (20, 17 and 15 over 14).
- A chosen family is always followed by the design system's (`"Georgia, Segoe UI Variable Text, Segoe UI"`), so a family no longer installed falls back (D51).
- Ctrl+=, Ctrl++ and keypad + go up; Ctrl+- and keypad - go down. They work from every surface, the editor included (it hands them on, as it does Ctrl+,), and the new size is announced.
### F14. Colour chips for `#rrggbb` (#155)

A `#rrggbb` code in a note shows a small swatch beside it. The text is unchanged, and screen readers are not given the swatch as extra text.

**Built (#155):**
- Codes are found by `ColorCode.Find` in Core: exactly six hex digits after `#`, standing on their own, so not part of a word, an eight-digit code, a character reference such as `&#123456;`, or a path fragment.
- On a card, the read view splits the run at each code, keeping its formatting, and places a swatch after it. The swatch is sized to the note text and edged in the markup colour (the row's own colour in high contrast), and marked `Raw` for UI Automation. The card's text reads exactly as written, with no object character (measured).
- A code inside a link gets no swatch. The editor shows the code as typed: its RichEdit cannot hold inline elements.
- Not built: the parity SHOULDs, a setting to turn swatches off (F14, J18) and a copy format (J17).

### F15. Automatic backups and retention (#15)

v0.1 has manual backup and restore. v0.2 adds scheduled backups and keeps a bounded number of them.

**Built (#15):**
- Once a day, into the data root's `backups`, as ordinary Noto backups (ADR-020): the same online snapshot, written whole or not at all, restored like any other.
- The seven newest automatic backups are kept (`AutomaticBackups.Keep`).
- Only Noto's own `auto-yyyy-MM-dd HHmmss.notobackup` files are counted and pruned. The before-restore backup and any backup the user saved are never touched.
- The time is in the name, so a copy or sync tool changing file times doesn't affect the schedule. A newest backup dated in the future (the clock moved back) makes one due.
- The first check runs 30 s after a start, never on its path, then hourly. It is skipped while another backup, restore, export or import runs.
- A failure is traced and tried again at the next check, and never interrupts.
- Settings › Data: *Back up automatically*, on by default.
- The old issue's Markdown export and manual `.db` backup scope shipped in v0.1 (ADR-020).

### F16. Crash-loop detection (#10)

Noto notices that it is failing at every start, and stops the loop instead of crashing again.

**Built (#10):**
- As Noto ends after a failure, it records the time and how soon after its start (`failures.txt` in the data root, the last 10, times only).
- A failure within 60 s of a start is a failure to start; three of them within half an hour is a loop (`CrashLoop` in Core).
- A start that finds a loop stops before anything opens, once it owns the data root:
  - a normal start asks "Start Noto anyway?", with **No** the default; **Yes** clears the record and starts;
  - a sign-in start (`--login`) asks nothing and shows nothing, and ends quietly.
- The log records `CrashLoopStopped` with booleans only.
- Noto still does not restart itself after a failure; v0.1 ends it cleanly once the open note is saved. The loop this stops is the one a user, or Windows at sign-in, makes by starting it again.

### F17–F21. Gaps found while checking (#156–#159)

These SideNotes MUST rows are not built, and none of them is in a later milestone:

| Item | What | Parity rows |
|---|---|---|
| F17 | A global hotkey that opens Noto with a new note ready | B2 |
| F18 | A new note from the clipboard's text or picture | B3, D43, G4 |
| F19 | Remove the checked tasks of a note, as one undo step | D22, G34 |
| F21 | A global hotkey that opens Noto with Search | E2 |

New global hotkeys are rebindable and off by default (principle 7).

**Built, F17 (#156):**
- A second global hotkey, **New note**, off by default, `Ctrl+Shift+Win+N` (the implementation plan's §10.1 default for G2), in Settings › Shortcuts with its own switch and recorder (`activation.new-note.enabled`, `activation.new-note.binding`).
- A press from any app shows Noto (never hides it, as a second launch does) and makes a note as Ctrl+N does: in the open folder, else the selected folder on the list, else the first; its editor has the keyboard. Settings, Search and the Recycle Bin are left first; the welcome tour is left alone. With no folder, Noto says to make one and focuses the folder name box.
- The hotkey code is per action (`GlobalAction`): one registration path, one rebind procedure, one recorder, so F21's search shortcut adds an action, not a copy.
- Recording another of Noto's own chords is refused by name ("is already Noto's shortcut to show and hide Noto"). A chord pressed while recording that arrives just after recording ends is still the recorder's: measured, recording the show/hide chord as the new-note shortcut hid Settings without this.

**Built, F18 (#157):**
- *New note from clipboard*: a new note made as the new-note shortcut makes it, then the clipboard pasted into it by the editor's own Ctrl+V (`NoteEditorHost.PasteClipboard`): a page's HTML as Markdown, plain text, copied files as links and pictures kept, a picture alone kept and shown. No second paste path.
- From the tray icon's menu (*New note from &clipboard*), and from a third global hotkey, `Ctrl+Shift+Win+V` (the implementation plan's §10.1 default for G4), off by default, in Settings › Shortcuts with its own switch and recorder. A third `GlobalAction` on F17's per-action hotkey code.
- An empty clipboard gives an empty new note, as Ctrl+N does.

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
