# Changelog

All notable changes to Noto are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

While Noto is on `0.x`, minor versions may include breaking changes to data
format or behavior. Breaking changes are always called out explicitly.

## [Unreleased]

### Added

- `Ctrl+D` turns *Keep Noto open* on and off, from anywhere inside Noto (#146).
- Each folder in the list shows how many notes it holds (#147).

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
