# Changelog

All notable changes to Noto are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

While Noto is on `0.x`, minor versions may include breaking changes to data
format or behavior. Breaking changes are always called out explicitly.

## [Unreleased]

### Added

- Settings › Appearance › *Theme*: install a `.nototheme` colour theme file (checked for readable contrast first),
  choose it, show it in Explorer, or remove it to the Windows Recycle Bin. The Light/Dark row is now called *Mode*
  (#269, ADR-023).
- Space shows a picture full size inside Noto: on a card it opens the note's first picture, and in a note being
  edited the selected picture. Left and Right move between the note's pictures; Escape or Space closes it (#268).
- Settings › Importing › *Move imported files to the Windows Recycle Bin* (off by default): files dropped on Noto
  go to Windows' Recycle Bin once their notes are saved. Files on a network or removable drive, read-only files and
  files in use stay where they are, and the status says how many moved and how many stayed (#267).
- The Open Bar: a small handle at the screen edge, shown while Noto is hidden, that shows Noto when clicked and imports
  files dropped on it. Settings › Sidebar chooses Off, Always shown or Hide when the mouse is idle; it is off by default,
  and dropping files on it has its own switch (#185).
- Each folder can sort its notes by name, date created or date modified, or keep your own order, from the note list's
  *More* menu. Pinned notes stay first, and Manual brings back the order you made (#242).
- Settings › General › *Ask which folder for a note from a shortcut*: the global new-note and clipboard shortcuts
  first ask where the note goes. Recent folders come first; type to filter, Enter to choose, Escape to cancel (#243).
- Settings › *Move checked tasks to the bottom*: when on, checking a task moves it, with its nested items, below the
  open tasks in its list, and unchecking moves it back. The check and the move are one undo step (#244).
- Noto can show itself when the pointer rests at the screen edge it docks to, or when you drag something to that edge
  to drop it in. Both are off by default, and each is turned on separately in Settings › Sidebar (#185).
- Duplicate a note from its card menu or with Ctrl+Shift+D: the copy appears directly below it with the same text,
  colour and mode. Duplicate a folder from its menu to get "<name> copy" holding copies of all its notes, in order (#232).
- Settings › Data › *Backup folder*: automatic backups can go to a folder you choose, such as one in OneDrive. If
  it is missing or can't be written to, the backup goes to Noto's data folder and Settings says so (#233).
- Settings › Appearance › *Line height* and *Paragraph spacing*: Compact, Normal or Relaxed, applied at once to the
  note being edited and to every card. Normal keeps the look notes had before (#234).
- Format › *Clear formatting* (Ctrl+\) removes bold, italic, strikethrough, underline, highlight, code and links
  (keeping their text) from the selection in one undo step; a picture's menu can save it to a file with *Save picture
  as…* (#235).
- Settings › General › *Where new notes go*: at the bottom, at the top, or above or below the selected note. Ctrl+N,
  the + button and the new-note shortcuts all follow it, at once (#188).
- Settings › Data › *How often*: Noto backs itself up every day (still the default), or every 45, 30 or 15 minutes.
  The last seven automatic backups are kept, as before (#199).
- Ctrl+Shift+P inserts a picture chosen from your files; Ctrl+] and Ctrl+[ indent and outdent list items; the Format
  menu inserts a separator line, and a picture (#194).
- In Search, Ctrl+R opens the chosen note's first link (only the kinds of link Noto already opens), and Ctrl+Click or
  Ctrl+Enter on a note opens the folder it is in (#197).
- Move to (Ctrl+Shift+M, or the card menu) can create a folder: choose *New folder…*, type a name and press Enter, and
  the note moves into it (#189).
- Two more global shortcuts, off by default and rebindable in Settings › Shortcuts: *change side*
  (Ctrl+Shift+Win+S) moves Noto to the other edge, and *keep open* (Ctrl+Shift+Win+K) turns Keep Noto open on or off.
  Neither shows or hides Noto; Ctrl+D inside Noto is unchanged (#198).
- The note list's bar has a *More* menu with Fold all and Unfold all, and a double-click on a note that isn't being
  edited folds or unfolds it; inside a note being edited, a double-click still selects a word (#190).
- Settings › Appearance › *Note colour style*: Background tints a coloured note and marks its edge, as before; Bar
  shows only the coloured edge (#191).
- The note list's **+** button has a menu (right-click or Shift+F10) with *New note from clipboard*, and a picture in a
  note can be copied as a picture from the editor's menu, or with Ctrl+C when it alone is selected (#187).
- Noto remembers the folders you open: Ctrl+Shift+O switches back to the folder you were in, Back's menu and its
  chevron list your recent folders, and Settings can open the last folder when Noto starts (#193).
- Settings › *Paste code as a code block*: lines of code pasted with Ctrl+V go in a fenced code block. Off by default;
  Ctrl+Shift+V always pastes plain text (#187).
- A file or folder dropped or pasted into a note shows as a shortcut with its icon, in a Standard or Compact style
  chosen in Settings, and a double-click opens it. Settings › Importing also chooses whether a dropped picture is kept
  in the note or linked, and whether a dropped text file is linked or its text inserted (#196).
- Noto can be driven by `noto://` links and from the command line: make a note with text (in a folder if named),
  open a folder, show the folder list, make a folder, or open Search with a query. A folder's menu has *Copy link*, and
  the installed Noto registers the link for the current user (#192).
- A note can be Markdown, Code or Plain text, from the card's menu under *Mode*. Code shows the text exactly as typed,
  in a monospace font with spell check off. Settings › *New notes are* sets the mode for new notes (#195).

### Fixed

- With Narrator on, Alt+Shift+Left and Alt+Shift+Right fold and unfold a note, and Alt+Shift+Up and Alt+Shift+Down
  move to the previous and next note while editing. Narrator keeps Ctrl+Alt+arrows for itself; without Narrator they
  still work (#202).
- Narrator says a folder's or a note's state with its title, such as "Work, Pinned" or "Weekly sync, Folded, 3 lines
  hidden". It read only the title before (#201).

## [0.2.0] - 2026-11-14

### Added

- A shortcut to search your notes from any app: Noto opens with Search ready to type in. Turn it on in Settings ›
  Shortcuts › *Search*; it is Ctrl+Shift+Win+F and can be changed there. Off by default (#159).

- Clear checked tasks: Ctrl+Shift+T in a note, or *Clear checked tasks* in its Format menu, removes every checked task
  and keeps everything else. Ctrl+Z brings them all back in one step (#158).

- A new note from the clipboard: the tray icon's menu has *New note from clipboard*, and Settings › Shortcuts can turn
  on a shortcut for it (Ctrl+Shift+Win+V, off by default). Copied text, a page's formatting as Markdown, or a picture
  becomes a new note, pasted exactly as Ctrl+V pastes (#157).

- A shortcut to make a new note from any app: Noto opens with a new note ready to type in, in the folder you have
  open (or the selected one). Turn it on in Settings › Shortcuts › *New note*; it is Ctrl+Shift+Win+N and can be
  changed there. Off by default (#156).

- A small formatting toolbar above selected text in a note: bold, italic, highlight, strikethrough, underline, link,
  code and bulleted list. It never takes the keyboard from your text, and can be turned off in Settings › Appearance ›
  *Formatting toolbar* (#151).

- Highlight: `::text::` is drawn with a highlighter's colour, in the editor and on the card, from the toolbar or the
  Format menu. It is SideNotes' syntax; in High Contrast it uses your Highlight colours (#151).

- Drop files on a folder, or on its notes, to import them: each Markdown or text file becomes a new note, and each
  picture a new note holding it, first in that folder. A drop says what it imported in one line, with an **Undo** that
  moves those notes to the Recycle Bin. The files are copied, never moved or changed. A file dropped on the note being
  edited is still added to that note (#152).

- A folded note says how much it hides: its card shows "+ N lines" under the title, and clicking it unfolds the note.
  The card being edited has a Fold button; Ctrl+Alt+Left and Ctrl+Alt+Right still fold and unfold. Screen readers
  hear "Folded, N lines hidden" (#149).

- If Noto stops unexpectedly three times in a row, each time soon after starting, it no longer starts again on its own:
  it asks whether to start anyway, and at sign-in it stays closed without a message. Your notes are untouched (#10).

- Automatic backups: once a day Noto backs itself up into the data folder's `backups`, keeping the last 7. Turn it off
  in Settings › Data › *Back up automatically*. A backup you saved yourself, or the one taken before a restore, is
  never removed (#15).

- A `#rrggbb` colour code in a note shows a small swatch of its colour beside it on the card. The text is unchanged,
  and screen readers read only the code (#155).

- A font and a text size for notes, in Settings › Appearance: Default (Segoe UI Variable), Segoe UI, Calibri, Cambria,
  Georgia, Verdana or Consolas, from 10 to 24. Ctrl+= and Ctrl+- make the text larger and smaller from anywhere in
  Noto, the editor included; headings stay a step above it. A font Windows no longer has falls back to the default
  (#154).

- Note colours: Yellow, Green, Blue, Purple, Pink and Grey, from the card's menu (*Colour*), the Colour button on the
  card being edited, or Ctrl+1 to Ctrl+6; Ctrl+0 clears it. A coloured card is tinted and marked along its edge, and
  its colour is named for screen readers; High Contrast leaves cards untinted (#150).

- `Ctrl+D` turns *Keep Noto open* on and off, from anywhere inside Noto (#146).
- Each folder in the list shows how many notes it holds (#147).

- A search button in the header, beside Settings and *Keep Noto open*, opens Search as Ctrl+F does (#148).
- A **+** button at the foot of the folder list, beside the Recycle Bin, makes a new folder, as Ctrl+N does (#148).

### Changed

- A hidden Noto now gives back the memory it is not using: after it has been hidden for a few seconds, Windows takes
  back its unused pages (idle and hidden: about 7 MB of working set instead of about 160). The first show after that
  is in front just as fast; its slide-in takes a little longer, once (#160).

- The *New folder name* box no longer takes the top of the folder list all the time: it opens from **+**, Ctrl+N or
  the menu's *New folder*, and closes once the folder is made, on Escape, or when you leave it empty. With no folders
  yet, it stays open (#148).

- The drawer slides in from its screen edge when it opens, background and all, and slides back out when it closes, a
  little faster. Nothing is drawn before it arrives, not even an outline: until it has, you see your desktop. With
  Windows' *Animation effects* off, it appears and goes at once, as before (#145).
- The drawer resizes from a narrow grip just inside its inner edge, rather than from just outside it (#145).
- The drawer's inner corners are rounded and its inner edge, top and bottom have a thin border, drawn on the drawer
  so they slide with it (#145).
- Starting Noto no longer shows a black window for a moment: it appears once drawn, sliding in as it does from the
  shortcut (#145).

### Fixed

- The automatic backup checked a second time 30 seconds after the first, instead of an hour later (#160).

- A picture dragged from a web page onto Noto is imported; before, nothing was. Browsers give a picture as a file with
  no path, which Noto now copies in first (#31).
- Text dragged from another app, such as a web page, onto the note being edited is added where it is dropped, as Ctrl+V
  adds it (#31).
- Narrator now hears Noto's messages as they change: notices, results lines and Search's count were never announced
  (#161).
- Ctrl+C in Search hides Noto every time; about one copy in two, Noto came straight back.

### Known limitations

- Narrator does not say a row's state (Pinned, Folded, a colour) (#201).
- With Narrator on, Ctrl+Alt+arrows are Narrator's own keys: fold and unfold from the card's menu instead (#202).
- High Contrast, display scaling, input methods, two monitors and Windows 10 are not yet checked by hand for this
  release (#161, #30, #32).
- The installer is still not signed: Windows shows *Windows protected your PC* the first time (choose *More info*, then
  *Run anyway*), and Smart App Control blocks it.
- Noto still never uses the network, so v0.1 does not update itself: run the v0.2 installer over it. Your notes, in
  `%LOCALAPPDATA%\Noto`, are kept.

## [0.1.0] - 2026-11-06

The Basic Release. Noto is a notes drawer for Windows: quick to reach, usable
entirely from the keyboard, and it keeps everything on this computer.

### Added

**The drawer**
- A window docked to the left or right edge of the screen, on top while shown.
- Resizable from its inner edge, with its width remembered per display.
- Shown and hidden by a global hotkey (rebindable) or the tray icon.
- Put away by Escape, `Ctrl+W` or a click elsewhere, unless pinned for the session.
- One Noto per data folder.

**Start with Windows**, turned on from the tray menu:
- A hidden start: nothing is shown until you ask.
- Off by default, and Windows' own Startup apps setting is respected.

**Folders and notes**
- Create and rename.
- Move a note to another folder; recent folders come first.
- Pin and fold.
- Reorder from the keyboard or by drag and drop.
- Delete to the Recycle Bin, and restore.
- Notes are cards in a stack, edited in place.

**The editor**
- Markdown kept as plain text, shown formatted with the markup hidden: headings,
  bold, italic, underline, strikethrough, lists, tasks with clickable checkboxes,
  links, code and separators.
- Formatting shortcuts, a format menu, list indent, line moves, formatted paste,
  undo and redo.
- Saved as you type.

**Search** (`Ctrl+F`)
- Across every note or only the current folder, as you type.
- Highlighted results, navigated entirely from the keyboard.

**Links** open safely
- Only web, mail and local file links open.
- Bare addresses, pasted links and copied files become links.

**Pictures**
- Paste, drop or copy images into a note.
- They are drawn in the editor and on the cards.

**Back up, restore, export and import**, in Settings > Data
- A full backup you can restore.
- An export to Markdown files, pictures included.
- An import of Markdown and text files.

**Settings** (`Ctrl+,`)
- Theme: light, dark or the same as Windows.
- The edge and the hotkey.
- What Escape and a click elsewhere do.
- Where new notes go, and opening a note with a single click.

**A welcome tour** for a new workspace: four steps, which you can skip.

**Accessibility**
- Every surface works from the keyboard.
- Screen readers get names and announcements through UI Automation.
- High Contrast draws in its own colours.

**An installer** for 64-bit Windows 11
- Per user, with no administrator rights and no wizard.
- Uninstalling never deletes your notes.

**Project**
- Governance: issue forms, a pull request template, CODEOWNERS, a Dependabot
  configuration, and CI, build, release and CodeQL workflows.
- Competitive research, the product definition, architecture documentation and
  the architecture decision records.

### Known limitations

**The installer is not signed.**
- The first time you run it, Windows shows *Windows protected your PC*: choose
  *More info*, then *Run anyway*.
- On a PC with Smart App Control turned on, Windows blocks it.

**Noto never uses the network**, so it does not check for updates. To install a
newer version, run its installer; your notes are kept.

**No automatic backups yet.** Back up from Settings > Data.

**Planned for v0.2:**
- note colours;
- font and text size settings;
- colour chips for `#rrggbb`;
- automatic backups;
- detecting and stopping a crash loop (Noto failing at every start).

**Not planned yet:** tags in the interface, sync, nested folders.

**Not yet checked by hand:**
- Narrator, High Contrast, display scaling, multiple monitors and input
  methods have not been checked by a person. The automated checks hold the
  names, focus order and High Contrast colours.
- Windows 10 22H2 has not been tested.

Your notes are stored in `%LOCALAPPDATA%\Noto`.

[Unreleased]: https://github.com/mdsaif45/Noto/compare/v0.2.0...main
[0.2.0]: https://github.com/mdsaif45/Noto/releases/tag/v0.2.0
[0.1.0]: https://github.com/mdsaif45/Noto/releases/tag/v0.1.0
