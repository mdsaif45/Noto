# Changelog

All notable changes to Noto are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

While Noto is on `0.x`, minor versions may include breaking changes to data
format or behavior. Breaking changes are always called out explicitly.

## [Unreleased]

### Added

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

[Unreleased]: https://github.com/mdsaif45/Noto/compare/v0.1.0...main
[0.1.0]: https://github.com/mdsaif45/Noto/releases/tag/v0.1.0
