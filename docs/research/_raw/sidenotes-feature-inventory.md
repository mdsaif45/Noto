# SideNotes (Apptorium, macOS) — Exhaustive Feature Inventory

> **Purpose:** Authoritative parity specification source for the Windows app (Noto).
> **Subject:** Apptorium SideNotes for macOS
> **Version observed:** **1.6.5** (released 2026-09-07; Mac App Store listing confirmed 1.6.5)
> **Date observed:** 2026-09-14
> **Platform requirement:** macOS 13 or later; Apple Silicon + Intel
> **Price:** $19.99 one-time per major version (Mac App Store / Apptorium direct); also on Setapp; SideNotes Mobile (iOS/iPadOS 17+) sold separately

## Confidence labels

| Label | Meaning |
|---|---|
| **CONFIRMED** | Stated on an official Apptorium page (sidenotes marketing, features, tips, articles, release notes) or the Mac App Store / Setapp listing |
| **INFERRED** | Derived from reviews, third-party listings, or reasonable reading of official screenshots/wording — not stated outright |
| **UNKNOWN** | Behavior not documented in any source found; must be resolved by hands-on testing or left as a design decision |

## macOS-specificity flag

- 🍎 = macOS-specific mechanism. Windows app needs a different mechanism or must drop the feature.
- (no flag) = platform-neutral, portable concept.

## Source key

| Ref | URL |
|---|---|
| S1 | https://www.apptorium.com/sidenotes |
| S2 | https://www.apptorium.com/sidenotes/features |
| S3 | https://www.apptorium.com/sidenotes/tips |
| S4 | https://www.apptorium.com/sidenotes/articles |
| S5 | https://www.apptorium.com/sidenotes/articles/keyboard-shortcuts |
| S6 | https://www.apptorium.com/sidenotes/articles/markdown |
| S7 | https://www.apptorium.com/sidenotes/articles/apple-script-api |
| S8 | https://www.apptorium.com/sidenotes/articles/url-api |
| S9 | https://www.apptorium.com/sidenotes/articles/shortcut-actions |
| S10 | https://www.apptorium.com/sidenotes/articles/data |
| S11 | https://www.apptorium.com/sidenotes/articles/icloud |
| S12 | https://www.apptorium.com/sidenotes/articles/creating-themes |
| S13 | https://www.apptorium.com/sidenotes/articles/using-tasks |
| S14 | https://www.apptorium.com/sidenotes/articles/five-tricks-to-get-more-of-sidenotes |
| S15 | https://www.apptorium.com/sidenotes/release-notes (index) |
| S16 | https://www.apptorium.com/sidenotes/release-notes/1.6 |
| S17 | https://www.apptorium.com/sidenotes/release-notes/1.5 |
| S18 | https://www.apptorium.com/sidenotes/release-notes/1.4 |
| S19 | https://www.apptorium.com/sidenotes/release-notes/1.3 |
| S20 | https://www.apptorium.com/sidenotes/release-notes/1.1 |
| S21 | https://www.apptorium.com/sidenotes/release-notes/1.6.3 |
| S22 | https://www.apptorium.com/sidenotes/release-notes/1.6.2 |
| S23 | https://www.apptorium.com/sidenotes/release-notes/1.6.1 |
| S24 | https://apps.apple.com/us/app/sidenotes-screen-edge-notes/id1441958036?mt=12 |
| S25 | https://setapp.com/apps/sidenotes |
| S26 | https://macsources.com/sidenotes-macos-app-review/ (review; secondary) |
| T-* | Individual tip pages under https://www.apptorium.com/sidenotes/tips/<slug> |

---

## A. Sidebar / Workspace

| # | Feature (SideNotes name) | Behavior | Settings | Shortcut | macOS-specific | Status | Source |
|---|---|---|---|---|---|---|---|
| A1 | **Screen edge window** | App occupies one vertical side of the screen as an overlay panel rather than a normal floating window. "Covers one side of your Mac's screen with notes." | — | — | No (concept portable) | CONFIRMED | S1, S25 |
| A2 | **Side Changing** | Move the app between the **left** and **right** screen edge. Default is **right**. | Preferences → (General) side selection; also right-click the Open Bar → choose side | Custom assignable shortcut for "Change Side" (added in 1.6) | No | CONFIRMED | S2, S16, T-how-to-change-side |
| A3 | **Open Bar** | A small vertical, semi-transparent button on the right (or left) screen edge. Click it to reveal the notes panel. | Settings → General → **Hide Open Bar**: `never` (default) / `when mouse is inactive` / `always` (the `always` option is only available in Hot Side and Menubar Icon modes) | Click | No | CONFIRMED | S2, T-how-to-hide-open-bar, T-how-to-show-menubar-icon |
| A4 | **Hot Side mode** | Hover activation: move the mouse cursor to the screen edge and the panel appears. No visible button required. | Preferences → **Show or Hide Notes** = `Hot Side` | Hover at edge | No | CONFIRMED | S1, S24, T-how-to-show-menubar-icon |
| A5 | **Menubar Icon mode** | Click the menu bar icon to show the app; click again to hide. | Preferences → **Show or Hide Notes** = `Menubar Icon` | Click | 🍎 (macOS menu bar; Windows = system tray) | CONFIRMED | S2, S20, T-how-to-show-menubar-icon |
| A6 | **Right-click menu on menu bar icon** | Context actions available directly from the menu bar icon (added 1.6). | — | Right-click | 🍎 | CONFIRMED | S16 |
| A7 | **Open with keyboard shortcut** | Global shortcut shows/hides the window even when SideNotes is not frontmost. | Preferences → Shortcuts → Global Shortcuts | `⌃⌥⌘␣` (default, configurable). The features page also cites `⌃⌥⌘` as the display chord. | No | CONFIRMED | S2, S5 |
| A8 | **Always on top / full-screen compatibility** | The window displays over other apps, including full-screen apps, and is Stage Manager compatible. | — | — | 🍎 (Stage Manager is macOS-only; "always on top over full-screen apps" needs a Windows-specific window-level approach) | CONFIRMED | S1, S24 |
| A9 | **Pin the window open** | Keyboard shortcut pins the app window so it stays visible. | Configurable shortcut | Configurable (no default documented) | No | CONFIRMED | S5, S20 |
| A10 | **Close window** | Closes/hides the SideNotes window. | — | `⌘W` | No | CONFIRMED | S5 |
| A11 | **Hide with Command-Return** | Optional binding making `⌘↩` hide SideNotes. (Note: `⌘↩` is otherwise Share menu — this is a configurable alternative.) | Documented as a settable behavior | `⌘↩` | No | CONFIRMED | S3 (T-how-to-hide-sidenotes-using-command-return) |
| A12 | **Escape key behavior** | Four configurable modes: (1) *Leave current folder or hide SideNotes* (default — first press returns to folder list, second press hides app); (2) *Leave current folder* only; (3) *Hide SideNotes* immediately; (4) *None* (disabled). | Settings → Shortcuts → ⎋ (Escape) | `⎋` | No | CONFIRMED | T-how-to-change-behaviour-of-escape-key |
| A13 | **Close when clicking outside SideNotes** | Option causing the panel to hide when the user clicks elsewhere. | Named setting "Close when clicking outside SideNotes" | — | No | CONFIRMED | S23 |
| A14 | **Window width resize** | Hover the window edge to reveal a guideline, then click-drag to resize width like a normal window. No preset size values; free drag. | Drag handle on window edge | — | No | CONFIRMED | T-how-to-change-window-size |
| A15 | **Drop on screen edge (drag-to-reveal)** | Dragging content toward the screen edge reveals SideNotes so you can drop into it. Can be disabled. | Preferences → Importing → disable drag-and-drop on screen edge; also a separate "disable showing SideNotes while dragging things across screen side" toggle | — | No | CONFIRMED | S18, S3 (T-how-to-disable-dropping-on-screen-side, T-how-to-disable-dnd-on-sceen-edge) |
| A16 | **Launch on startup** | App can auto-launch at login. | Preference "Launch on Startup" | — | No (Windows equivalent: run at login) | CONFIRMED | S20, S23 |
| A17 | **Show window if already running** | Launching SideNotes when already running brings the window forward instead of showing an "already running" warning (1.6). | — | — | No | CONFIRMED | S16 |
| A18 | **Multi-display behavior** | Which display the panel attaches to on multi-monitor setups; whether it follows the active display. | — | — | — | **UNKNOWN** — not documented on any Apptorium page; must be decided/tested | S26 (absence noted) |
| A19 | **Spaces / virtual desktop behavior** | Whether the panel appears on all Spaces or a single Space. | — | — | 🍎 (Spaces); Windows equivalent = Virtual Desktops | **UNKNOWN** — not documented | — |
| A20 | **Show/hide animation** | Panel slides out from the side of the screen. | — | — | No | INFERRED (review describes "slide out from the side of your screen") | S26 |
| A21 | **Two-finger edge gesture** | A trackpad gesture at the screen edge can trigger the panel; reviewers note it can conflict with other gestures. | — | Trackpad gesture | 🍎 (macOS trackpad gestures) | INFERRED | S26 |

---

## B. Note management

| # | Feature (SideNotes name) | Behavior | Settings | Shortcut | macOS-specific | Status | Source |
|---|---|---|---|---|---|---|---|
| B1 | **Create note (in app)** | Creates a new note in the current folder. Same key creates a folder when in the folder list. | Preferences → Creating Notes | `⌘N` | No | CONFIRMED | S5 |
| B2 | **Create note (global)** | Creates a new note in the current folder from anywhere; window pops up with the new note already active. | Configurable in Preferences → Shortcuts → Global Shortcuts | `⌃⌥⌘N` (default) | No | CONFIRMED | S2, S5, S14 |
| B3 | **Note from Pasteboard** | Creates a new note whose content is the current clipboard contents. Available via long-press on the **+** button, or a dedicated global shortcut. | Preferences → Shortcuts → *Create Note from Pasteboard* (global shortcut definable) | Configurable global shortcut | No | CONFIRMED | S2, S5, S14, S19, T-how-to-add-note-from-pasteboard |
| B4 | **Create note by Drag & Drop** | Drag content (text, files, images) into the app to create a note. | — | — | No | CONFIRMED | S2 |
| B5 | **New note placement** | Configurable where new notes/folders are inserted: **top**, **bottom**, **over current one**, **under current one**. | Preferences → Creating Notes | — | No | CONFIRMED | S19 |
| B6 | **Ask for folder on global create** | Option controlling whether the app asks which folder to use when creating a note via the global shortcut. | Preferences → Creating Notes | — | No | CONFIRMED | S19 |
| B7 | **Delete note** | Deletes a note; since 1.6 a **delete note confirmation popover** is shown. | — | `⌥⌘⌫` | No | CONFIRMED | S5, S16 |
| B8 | **Move note up / down** | Reorder a note within the current folder by one position. | — | `⇧⌥⌘↑` / `⇧⌥⌘↓` | No | CONFIRMED | S5, S20, T-how-to-move-notes-up-and-down |
| B9 | **Move note to top / bottom** | Quickly jump a note to the very top or bottom of the list. | — | Documented as "quick moving note to top and bottom" (added 1.3); exact chord not published | No | CONFIRMED (feature) / **UNKNOWN** (exact chord) | S19, T-how-to-move-note-top-or-bottom-quickly |
| B10 | **Drag & Drop reordering** | Rearrange notes *and* folders by dragging them up or down. Dropping a note onto another note is explicitly disabled. | — | — | No | CONFIRMED | S2, S18 |
| B11 | **Move note to another folder** | Move a note into a different folder. Redesigned menu in 1.6 including **Move to Recent Folders**. Allows creating a new destination folder inline during the move. Also invoked from the arrow button at the note's bottom. | — | `⇧⌘M` | No | CONFIRMED | S5, S16, S18, S14 |
| B12 | **Fold / Unfold note** | Collapses a note down to its **first line** to reduce clutter; expand to see full content. Invoked via gear icon → Fold/Unfold, double-clicking the note's bottom bar, pinch gesture, or keyboard. | — | Fold: `⌥⌘←` · Unfold: `⌥⌘→` · Fold All: `⇧⌥⌘←` · Unfold All: `⇧⌥⌘→` | Pinch gesture is 🍎-ish (trackpad); rest portable | CONFIRMED | S1, S2, S16, T-how-to-fold-note |
| B13 | **Fold All / Unfold All** | Collapse or expand every note in the current folder; also available from the **…** button at the bottom left. | — | `⇧⌥⌘←` / `⇧⌥⌘→` | No | CONFIRMED | T-how-to-fold-note |
| B14 | **Pin Notes** | Pin important notes so they always stay at the top of the list. Added in 1.6. | — | — | No | CONFIRMED | S1, S16, T-how-to-pin-note |
| B15 | **Note colors** | Assign one of **six** colors to a note, plus an "empty"/no-color state. | Preferences → Appearance → **Note Colors**: `Background` (whole note tinted, default since 1.3) or `Bar` (colored left bar only). Default color for new notes set in Preferences. Improved color picker in Note Settings (1.6). | `⌘0` = empty color; `⌘1`–`⌘6` = set note color | No | CONFIRMED | S1, S5, S19, T-how-to-set-color-bar, T-how-to-set-default-note-color, T-how-to-mark-notes-with-colors |
| B16 | **Note title behavior** | The **first line** of a note acts as its title: it is what remains visible when a note is folded, it is the line excluded by `⌘⌥C` quick-copy, and note URLs now include the note title (1.6.3). | — | — | No | CONFIRMED | S21, S14, T-how-to-fold-note |
| B17 | **Note bottom bar** | Per-note action bar at the bottom of each note (gear/format/share/move actions). Redesigned in 1.6 ("New Bottom Bar in Note View"). | — | — | No | CONFIRMED | S16 |
| B18 | **Notes-list bottom bar** | List-level action bar at the bottom of the notes list, including the **…** menu and a note count. Added/redesigned in 1.6. | — | — | No | CONFIRMED | S16, S23 |
| B19 | **Auto-save** | Notes persist continuously to a SQLite database; no explicit save action documented anywhere. | — | — | No | INFERRED (no save command exists in the documented shortcut set; storage is a live SQLite DB) | S10 |
| B20 | **Switch between notes** | Move selection between notes with the keyboard. | — | `⌘⌥↓` / `⌘⌥↑` | No | CONFIRMED | S5 |
| B21 | **Print a note** | Print notes. Added in 1.6.3 (previously a known limitation complained about in reviews). | — | `⌘P` | No | CONFIRMED | S21, S26 |
| B22 | **Duplicate note** | Duplicating an individual note. | — | — | — | **UNKNOWN** — only **folder** duplication is documented (1.6.3). Note-level duplication is not documented. | S21 |
| B23 | **Trash / restore deleted note** | A per-note trash or undo-delete feature. | — | — | — | **UNKNOWN** — no trash/restore documented. Recovery is only via **backups** (see I). Deletion shows a confirmation popover instead. | S16 |
| B24 | **Note templates** | — | — | — | — | **UNKNOWN** — no template feature documented anywhere. Do not assume it exists. | — |
| B25 | **Note width / size** | Notes fill the panel width; the panel width itself is draggable (A14). No per-note width setting documented. | — | — | No | INFERRED | T-how-to-change-window-size |
| B26 | **Empty note placeholder** | Placeholder UI designs shown for empty notes. | — | — | No | CONFIRMED | S20 |

---

## C. Folders / organization

| # | Feature (SideNotes name) | Behavior | Settings | Shortcut | macOS-specific | Status | Source |
|---|---|---|---|---|---|---|---|
| C1 | **Folders** | Notes are organized into folders. The folder list is the top-level view; entering a folder shows its notes. | — | `⌘N` creates a folder while in the folder list | No | CONFIRMED | S1, S2 |
| C2 | **Create folder** | Create a new folder from the folder list, from the note-move flow, via AppleScript, Shortcuts, and the Alfred workflow. | — | `⌘N` (in folder list) | No | CONFIRMED | S2, S7, S9, S18 |
| C3 | **Nesting depth** | Whether folders can contain sub-folders. | — | — | — | **UNKNOWN** — every source describes a flat, single-level folder → notes hierarchy. No sub-folder feature is documented. Treat as **flat (1 level)** unless disproven. | S1–S25 (absence) |
| C4 | **Enter folder** | Open the selected folder. | Optional **single-click folder opening** (Settings → Folders, added 1.6); otherwise double-click. | `⌘↓` | No | CONFIRMED | S5, S16 |
| C5 | **Leave folder / back** | Return to the folder list. Long-press the back (`<`) button to pick from **recently visited folders**. | Escape behavior configurable (A12) | `⎋` | No | CONFIRMED | S5, S14 |
| C6 | **Switch to last folder** | Toggle back to the previously opened folder without repeatedly using the back button. | — | `⌘⌥O` | No | CONFIRMED | S5, S14, S19, T-how-to-switch-quickly-to-last-folder |
| C7 | **Show all folders** | Display the full folder list (also exposed as an AppleScript command). | — | — | No | CONFIRMED | S7 |
| C8 | **Pin Folders** | Pin frequently used folders so they stay at the top of the folder list. Added 1.6. | — | — | No | CONFIRMED | S1, S16, T-how-to-pin-folder |
| C9 | **Folder reordering** | Move folders up/down via drag & drop. | — | — | No | CONFIRMED | S2, S18 |
| C10 | **Folder sorting** | Folder sorting capability added in 1.3. Exact sort keys (name/date/manual) not enumerated. | — | — | No | CONFIRMED (exists) / **UNKNOWN** (options) | S19 |
| C11 | **Delete folder** | Deletes a folder; since 1.6 shows a **delete folder confirmation popover**. | — | — | No | CONFIRMED | S16 |
| C12 | **Duplicate folder** | "Create a copy of any folder without rebuilding it from scratch." Added 1.6.3. | — | — | No | CONFIRMED | S21 |
| C13 | **Rename folder** | Folders have a settable `name`; rename is inline and Escape-during-rename behavior was bug-fixed in 1.1. | — | — | No | CONFIRMED | S7, S20 |
| C14 | **Open last folder on restart** | Option to reopen the last-used folder when the app restarts. | Preference "Opening last folder on app restart" | — | No | CONFIRMED | S19 |
| C15 | **Empty folder guidance message** | Helper text in an empty folder explaining how to add the first note. Added 1.6. | — | — | No | CONFIRMED | S16 |
| C16 | **Folder URL** | Each folder has a URL, copyable via contextual menu → **Copy URL**. | — | — | 🍎-adjacent (custom URL scheme; Windows needs its own protocol handler) | CONFIRMED | S8 |
| C17 | **Folder colors** | — | — | — | — | **UNKNOWN** — note colors are documented; folder colors are not. Theme JSON has a `folders`/`folder` section but that is theme-level styling, not per-folder color. | S12 |
| C18 | **"All notes" view / favorites / archiving** | — | — | — | — | **UNKNOWN** — no cross-folder "All Notes" view, favorites, or archive feature is documented. Pinning (B14/C8) is the nearest analogue. Search (E) is the cross-folder access path. | — |
| C19 | **Note count** | The notes-list bottom bar displays a note count. | — | — | No | CONFIRMED | S23 |

---

## D. Editor & content

### D.1 Markdown / formatting

SideNotes supports a **subset of Markdown**, rendered with **invisible markup** (markup characters hidden by default) since 1.5. Prior to 1.5 markup was shown greyed-out (1.3).

| # | Element | Syntax | Shortcut | Status | Source |
|---|---|---|---|---|---|
| D1 | **Headers** | `#` … `#####` + space — **5 levels** | `⇧⌘H` creates a header | CONFIRMED | S6, S5 |
| D2 | **Bold** | `**text**` | `⌘B` | CONFIRMED | S6, S5 |
| D3 | **Italic** | `*text*` | `⌘I` | CONFIRMED | S6, S5 |
| D4 | **Bold Italic** | `***text***` | — | CONFIRMED | S6 |
| D5 | **Strikethrough ("Strike")** | `~~text~~` | — | CONFIRMED | S6 |
| D6 | **Marked / highlight** | `::text::` (non-standard, SideNotes-specific) | — | CONFIRMED | S6 |
| D7 | **Underline** | Added 1.6 as a text-formatting option | — | CONFIRMED (feature) / **UNKNOWN** (syntax + shortcut) | S16 |
| D8 | **Quote** | `> quoted text` | — | CONFIRMED | S6 |
| D9 | **Bullet list** | `* item` | `⌘L` | CONFIRMED | S6, S5 |
| D10 | **Ordered list** | `1. item` | `⇧⌘L` | CONFIRMED | S6, S5 |
| D11 | **Task (unchecked)** | `[ ]` | `⌘T` toggles a line to/from a task | CONFIRMED | S6, S13, S5 |
| D12 | **Task (checked)** | `[x]` | `⌘.` toggles checked state | CONFIRMED | S6, S13, S5 |
| D13 | **Inline code** | `` `code` `` | — | CONFIRMED | S6 |
| D14 | **Code block** | ` ``` ` (triple backticks) | — | CONFIRMED | S6 |
| D15 | **Color code preview** | `#rrggbb` renders as a color swatch | — | CONFIRMED | S1, S6 |
| D16 | **Horizontal separator** | Type `---` on a new line, or Aa button → **Separator**. Added 1.6. | — | CONFIRMED | S16, T-how-to-insert-separator |
| D17 | **Markdown inline links** | Inline links supported since 1.3. Link editing while markup visible was bug-fixed in 1.6. | — | CONFIRMED | S19, S16 |
| D18 | **Tables** | — | — | **UNKNOWN → treat as NOT supported.** Tables are absent from the official Markdown article's supported list and from all release notes. Do not assume support. | S6 |
| D19 | **Indent / outdent text** | Shift text right / left | `⌘]` / `⌘[` | CONFIRMED | S5, S18 (1.4.4 added text indentation), T-how-to-indent-text |
| D20 | **Move lines up / down** | Reorder lines within a note | `⌘⌥[` / `⌘⌥]` (added 1.6.2) | CONFIRMED | S22, T-how-to-move-lines-up-and-down |
| D21 | **Clear formatting of selected text** | Strip formatting from a selection | — | CONFIRMED (feature) / **UNKNOWN** (shortcut) | S3 (T-how-to-clear-formatting-of-selected-text) |
| D22 | **Clear Completed Tasks** | Deletes all checked tasks from the note. Aa button → *Clear Completed Tasks*. Added 1.6. | `⇧⌥⌘T` | CONFIRMED | S16, T-how-to-clear-completed-tasks |
| D23 | **Completed tasks move to bottom** | Finished tasks automatically move to the bottom of the task list. Added 1.6.2. | — | CONFIRMED | S22 |
| D24 | **Show / Hide Markdown markup** | Toggles visibility of the raw markup characters. Default = hidden ("invisible Markdown"). Aa button → Show/Hide Markdown. | `⇧⌘R` | CONFIRMED | S17, T-how-to-show-or-hide-markdown-markup |
| D25 | **Quick Formatting Toolbar** | Floating toolbar for formatting selected text without opening the Aa menu. Added 1.6. Settings → Notes → Quick Formatting Toolbar: position **above** / **below** cursor, or **disabled**. | — | CONFIRMED | S1, S16, S24 |
| D26 | **Aa button / formatting menu** | Per-note bottom-bar menu holding formatting commands (Separator, Show/Hide Markdown, Clear Completed Tasks, etc.). | — | CONFIRMED | S3 (multiple tips) |

### D.2 Note modes

| # | Feature | Behavior | Settings | Status | Source |
|---|---|---|---|---|---|
| D27 | **Code mode** ("Code-friendly mode") | Switches a note from Markdown rendering to a **fixed-width (monospace) font**, suited to code snippets. Spell checking is disabled in Code mode (fixed in 1.4). | Per-note: note gear icon → *Code mode*. Global default: Preferences → Text → **Default Text Formatting** | CONFIRMED | S1, S18, S25, T-how-to-set-fixed-width-font |
| D28 | **Note modes: code / plain text / markdown** | Setapp lists three note modes. | Preferences → Text → Default Text Formatting | CONFIRMED (Setapp listing) / INFERRED (exact three-way naming) | S25 |

### D.3 Content types

| # | Feature | Behavior | Settings | Shortcut | macOS-specific | Status | Source |
|---|---|---|---|---|---|---|---|
| D29 | **Pictures / images** | Embed images inline in notes. Insert via shortcut, drag & drop, or paste. | Preferences → Importing (behavior for imported images) | `⇧⌘P` inserts a picture | No | CONFIRMED | S2, S5, S18 |
| D30 | **Screenshots** | Screenshots supported as note content via drag-and-drop. | — | — | No | CONFIRMED | S24 |
| D31 | **Add image from iPhone/iPad (Continuity Camera)** | Insert an image captured on a nearby iOS device. | — | — | 🍎 **Continuity Camera — macOS/iOS only. No Windows equivalent; drop or replace.** | CONFIRMED | S3 (T-how-to-add-image-from-iphone) |
| D32 | **Copy an image quickly** | Quick copy of an embedded image to the clipboard. | — | — | No | CONFIRMED | S3 (T-how-to-copy-image-quickly) |
| D33 | **Save an image to a file** | Export an embedded image out to a file. | — | — | No | CONFIRMED | S3 (T-how-to-save-image-to-file) |
| D34 | **File & Folder Shortcuts** | Drag a file or folder into SideNotes (as a new note or into an existing note) to create a shortcut with a **file preview**. Double-click the shortcut to open the file. | Preferences → Text → **File Links**: standard (full-width preview, default) or **compact** style | — | No (paths differ) | CONFIRMED | S2, S20, T-how-to-add-shortcut-to-file |
| D35 | **Quick Look for images and files** | Click an image or file shortcut and press **Space** to preview it. Added 1.4. | — | `Space` | 🍎 **Quick Look is a macOS system service. Windows needs a custom preview implementation.** | CONFIRMED | S18 |
| D36 | **Text snippets** | Notes used as reusable snippets (addresses, signatures, boilerplate), retrieved through search and copied with quick-copy shortcuts. | — | `⌘C` / `⌘⌥C` (see E) | No | CONFIRMED | S1, S14, T-how-to-use-text-snippets |
| D37 | **URL handling / links** | URLs in notes are clickable/launchable; `⌘R` launches a URL from the second line via search. URL parsing for addresses containing dashes was fixed in 1.1. Link titles are used when available on paste (1.6.3). URL pasting into selected text fixed in 1.6.5. | — | `⌘R` (in search) | No | CONFIRMED | S5, S20, S21, S15 |
| D38 | **Save a website address quickly** | Fast capture of the current/clipboard web address into a note. Supported by the Share extension for web addresses. | — | — | 🍎 (Share extension mechanism) | CONFIRMED | S1, T-how-to-save-website-address-quickly |
| D39 | **Code detection on paste** | Detects code pasted from editors and automatically pastes it as a **code block**; a setting chooses between code-block format and Code-note format. Added 1.6.2. | Setting for code block vs Code note | — | No | CONFIRMED | S22 |
| D40 | **Better pasting from ChatGPT** | Improved formatting cleanup and structure when pasting from ChatGPT. Added 1.6. | — | — | No | CONFIRMED | S16 |
| D41 | **Prevent automatic spaces on paste** | Option preventing automatic spaces being added to pasted text. Added 1.6.1. | Named option | — | No | CONFIRMED | S23 |
| D42 | **Smarter pasted links** | Link titles are used when accessible during pasting. Added 1.6.3. | — | — | No | CONFIRMED | S21 |
| D43 | **Paste Clipboard Content** | Generate a note directly from clipboard contents (see B3). | — | — | No | CONFIRMED | S2 |
| D44 | **Text replacement** | macOS system text replacement works in notes (fixed in 1.4). | System-level | — | 🍎 (macOS Text Replacement service) | CONFIRMED | S18 |
| D45 | **Spell check** | Spell checking active in normal notes; explicitly **disabled in Code mode**. | — | — | 🍎 (uses macOS spell-check services; Windows needs its own) | CONFIRMED | S18 |
| D46 | **Word count** | — | — | — | — | **UNKNOWN** — no word-count feature documented. A note **count** exists at list level (C19), but not a word count. | — |

### D.4 Typography

| # | Feature | Behavior | Settings | Shortcut | Status | Source |
|---|---|---|---|---|---|---|
| D47 | **Font family selection** | Choose the font family used for notes. As of 1.6, **all fonts can now be set** (expanded font customization across UI elements). | Preferences → Text / Text Settings | — | CONFIRMED | S2, S16, S19 |
| D48 | **Font size** | Increase / decrease text size. | Preferences → Text Settings | `⌘+` / `⌘−` | CONFIRMED | S5, S2 |
| D49 | **Line height** | Three presets: **Tight**, **Normal** (default), **Relaxed**. | Preferences → Line Height | — | CONFIRMED | T-how-to-change-line-height |
| D50 | **Paragraph spacing** | Three presets: **Tight**, **Normal**, **Relaxed**. | Preferences → Paragraph Spacing | — | CONFIRMED | T-how-to-change-line-height |
| D51 | **Crash fix: missing font** | A crash could occur when a selected font was unavailable (fixed 1.6.1) — implies font fallback handling is needed. | — | — | CONFIRMED | S23 |

---

## E. Search

| # | Feature | Behavior | Settings | Shortcut | macOS-specific | Status | Source |
|---|---|---|---|---|---|---|---|
| E1 | **Search window** | Searches **notes and folders** across the whole app (cross-folder scope). Introduced in 1.1. | — | `⌘F` (app must be open and visible) | No | CONFIRMED | S20, T-how-to-access-search-quickly |
| E2 | **Global search shortcut** | Opens search from any app, including while another app is full-screen. Recommended default `⌃⌥⌘F`, user-configurable. | Preferences → Shortcuts → Global Shortcuts → **Search** → "Record Shortcut" | `⌃⌥⌘F` (recommended; not a hard default) | No | CONFIRMED | S19, T-how-to-access-search-quickly |
| E3 | **Quick Copy in Search** | Copy a found note's text straight from search results. `⌘C` copies all content **including** the first line; `⌘⌥C` copies content **excluding** the first line (title). After copying, the Search window closes and the previously frontmost app returns to focus. Improved in 1.6.3 to encompass **headers and list items**. | — | `⌘C` / `⌘⌥C` | No | CONFIRMED | S5, S14, S21, S15 (1.3.2) |
| E4 | **Launch website from search results** | If a note has a title on line 1 and a URL on line 2, `⌘R` launches that URL from the Search window. Added 1.4. | — | `⌘R` | No | CONFIRMED | S5, S14, S18 |
| E5 | **Open containing folder from search** | Hold **Control** (docs also cite `⌘ + Click`) while clicking a search result to open the folder containing it. Added 1.4. | — | `⌃ + Click` / `⌘ + Click` (sources differ — verify) | No | CONFIRMED (feature) / **UNKNOWN** (exact modifier) | S5, S18 |
| E6 | **Search via AppleScript** | `search`, `search notes`, `search folders`, `search themes` commands. | — | — | 🍎 | CONFIRMED | S7 |
| E7 | **Search via Shortcuts** | *Find Notes* and *Find Folders* actions take a search phrase. | — | — | 🍎 | CONFIRMED | S9 |
| E8 | **Live search / filters / in-note search** | Incremental results as you type; filter by folder/color/type; find-within-a-single-note. | — | — | — | **UNKNOWN** — not documented. Search is described only as global across notes and folders. | — |

---

## F. Appearance & themes

| # | Feature | Behavior | Settings | Status | Source |
|---|---|---|---|---|---|
| F1 | **Light / Dark mode** | Follows the system appearance, or can be set manually to light or dark (manual selection added in 1.1). | Preferences → Appearance | CONFIRMED | S1, S2, S20, T-how-to-set-light-or-dark-mode |
| F2 | **User Interface Themes** | Installable color themes changing the whole app's appearance. Introduced in 1.3. New built-in themes added in 1.6; built-in themes refreshed in 1.5 for the new editor. | Preferences → Appearance → **Manage** | CONFIRMED | S1, S2, S16, S17, S19 |
| F3 | **Custom theme creation** | Themes are `.sntheme` files — actually **JSON**, with an optional schema definition. Editable in any text editor; VS Code recommended (`"*.sntheme": "json"` in `files.associations`) because it honors the schema for completion and color rendering. | — | CONFIRMED | S12 |
| F4 | **Theme structure** | Three top-level appearance modes: **`light`**, **`dark`**, **`any`** (common settings inherited by both). Branches into sections including `folders`, `folder`, `search`, and `notes` — where `notes` contains `clean` (the default note style) and `colors` (the **six** color variations). | — | CONFIRMED | S12 |
| F5 | **Theme color properties** | `backgroundColor`, `backgroundGradient`, `textColor`, `secondaryTextColor`, `tertiaryTextColor`, `linkColor`, `buttonColor`, plus specialized properties for `quote`, `mark`, `codeBlock`, `code`, `checkMark`, `filePreview`. Text styles are also settable. | — | CONFIRMED | S12 |
| F6 | **Theme color formats** | Three hex formats accepted: `#rrggbb`, `#rrggbbaa` (with alpha), `#rgb` shorthand. | — | CONFIRMED | S12 |
| F7 | **Theme installation** | Double-click a `.sntheme` file and SideNotes copies it into its directory. Manage installed themes via Preferences → Appearance → Manage → contextual menu → **Show in Finder**. | — | CONFIRMED | S12, T-how-to-install-themes |
| F8 | **Theme gallery** | Apptorium hosts a downloadable themes gallery. | — | CONFIRMED | S1 |
| F9 | **Theme via automation** | `set theme` / `search themes` (AppleScript); *Find Theme* (with optional partial-name matching) and *Set Theme* (Shortcuts). | — | 🍎 | CONFIRMED | S7, S9 |
| F10 | **Note color display style** | `Background` (whole note tinted — default since 1.3) or `Bar` (colored left bar). | Preferences → Appearance → Note Colors | CONFIRMED | T-how-to-set-color-bar |
| F11 | **Transparency** | The **Open Bar** is explicitly described as semi-transparent. Theme colors support alpha (`#rrggbbaa`), enabling translucency. | — | CONFIRMED (Open Bar + alpha support) / **UNKNOWN** (a dedicated window-transparency/opacity setting) | S12, T-how-to-show-menubar-icon |
| F12 | **Compact / comfortable density** | The nearest documented analogue is the **compact** style for file-link previews (D34) and the Tight/Normal/Relaxed line-height and paragraph-spacing presets (D49/D50). No global density setting is documented. | Preferences → Text → File Links | INFERRED | T-how-to-add-shortcut-to-file, T-how-to-change-line-height |
| F13 | **Refreshed icons / menu icons** | Interface icons refreshed and icons added in menus (1.5); Settings graphics refreshed (1.6). | — | CONFIRMED | S16, S17 |
| F14 | **Disable #rrggbb color rendering** | Option to hide the inline color swatch for hex codes. Added 1.6. | Settings → Text → Colors | CONFIRMED | S16 |

---

## G. Keyboard & shortcuts — complete documented list

### G.1 Global shortcuts (work when the app is hidden) — all configurable

| Action | Default | Status |
|---|---|---|
| Show / hide the app | `⌃⌥⌘␣` | CONFIRMED (S5) |
| Create a new note in current folder | `⌃⌥⌘N` | CONFIRMED (S5) |
| Search | `⌃⌥⌘F` (recommended, user-set) | CONFIRMED (S19, T-how-to-access-search-quickly) |
| Create note from Pasteboard | Configurable, no default | CONFIRMED (S19, S14) |
| Change Side | Configurable, added 1.6 | CONFIRMED (S16) |
| Pin the app window | Configurable | CONFIRMED (S5) |

### G.2 Window / navigation

| Action | Shortcut | Status |
|---|---|---|
| Close the app window | `⌘W` | CONFIRMED (S5) |
| Back to folder list / hide (configurable, 4 modes) | `⎋` | CONFIRMED (S5, T-how-to-change-behaviour-of-escape-key) |
| Enter a folder | `⌘↓` | CONFIRMED (S5) |
| Switch to last-open folder | `⌘⌥O` | CONFIRMED (S5) |
| Switch between notes | `⌘⌥↓` / `⌘⌥↑` | CONFIRMED (S5) |
| Hide SideNotes (optional binding) | `⌘↩` | CONFIRMED (S3) |

### G.3 Note management

| Action | Shortcut | Status |
|---|---|---|
| New folder / new note (context-dependent) | `⌘N` | CONFIRMED (S5) |
| Delete note | `⌥⌘⌫` | CONFIRMED (S5) |
| Move note to another folder | `⇧⌘M` | CONFIRMED (S5) |
| Move note up | `⇧⌥⌘↑` | CONFIRMED (S5) |
| Move note down | `⇧⌥⌘↓` | CONFIRMED (S5) |
| Fold note | `⌥⌘←` | CONFIRMED (T-how-to-fold-note, S16) |
| Unfold note | `⌥⌘→` | CONFIRMED (T-how-to-fold-note) |
| Fold all notes | `⇧⌥⌘←` | CONFIRMED (T-how-to-fold-note) |
| Unfold all notes | `⇧⌥⌘→` | CONFIRMED (T-how-to-fold-note) |
| Set note color (1–6) | `⌘1` … `⌘6` | CONFIRMED (S5, S19) |
| Clear note color | `⌘0` | CONFIRMED (S5) |
| Print note | `⌘P` | CONFIRMED (S21) |

### G.4 Editing

| Action | Shortcut | Status |
|---|---|---|
| Bold | `⌘B` | CONFIRMED (S5) |
| Italic | `⌘I` | CONFIRMED (S5) |
| Header | `⇧⌘H` | CONFIRMED (S5) |
| Bullet list | `⌘L` | CONFIRMED (S5) |
| Ordered list | `⇧⌘L` | CONFIRMED (S5) |
| Create / remove task | `⌘T` | CONFIRMED (S5, S13) |
| Toggle task state | `⌘.` | CONFIRMED (S5, S13, S19) |
| Clear completed tasks | `⇧⌥⌘T` | CONFIRMED (T-how-to-clear-completed-tasks) |
| Insert picture | `⇧⌘P` | CONFIRMED (S5) |
| Indent right | `⌘]` | CONFIRMED (S5) |
| Indent left | `⌘[` | CONFIRMED (S5) |
| Move line up | `⌘⌥[` | CONFIRMED (S22) |
| Move line down | `⌘⌥]` | CONFIRMED (S22) |
| Show / hide Markdown markup | `⇧⌘R` | CONFIRMED (T-how-to-show-or-hide-markdown-markup) |
| Increase font size | `⌘+` | CONFIRMED (S5) |
| Decrease font size | `⌘−` | CONFIRMED (S5) |
| Open Share menu | `⌘↩` | CONFIRMED (S5) |
| Quick Look preview of image/file | `Space` | CONFIRMED (S18) |

### G.5 Search window

| Action | Shortcut | Status |
|---|---|---|
| Open search | `⌘F` | CONFIRMED (T-how-to-access-search-quickly) |
| Copy note content (incl. first line) | `⌘C` | CONFIRMED (S5, S14) |
| Copy note content (excl. first line) | `⌘⌥C` | CONFIRMED (S5, S14) |
| Launch website from second line | `⌘R` | CONFIRMED (S5) |
| Open containing folder | `⌃ + Click` / `⌘ + Click` (sources differ) | CONFIRMED (feature) / UNKNOWN (modifier) |

### G.6 Customization

- **Shortcuts preference pane** exists with a **Global Shortcuts** group and a "Record Shortcut" input for rebinding. CONFIRMED (T-how-to-access-search-quickly, S14).
- The Escape key has a dedicated four-option behavior setting in Settings → Shortcuts. CONFIRMED.
- Whether *every* in-app (non-global) shortcut is rebindable is **UNKNOWN**; only global shortcuts, Escape, and Change Side are documented as configurable.

---

## H. Integration & automation

| # | Feature | Behavior | macOS-specific | Status | Source |
|---|---|---|---|---|---|
| H1 | **Menu bar item** | Menu bar icon toggles app visibility; right-click gives context actions (1.6). | 🍎 **macOS menu bar → Windows system tray** | CONFIRMED | S2, S16, S20 |
| H2 | **Share Extension** | Send note content out to Messages, Notes, Reminders, Dropshare and more via the system share sheet. Also a share extension for **web addresses** (capture URLs into SideNotes from other apps). Invoked with `⌘↩`. | 🍎 **macOS Share Sheet / NSSharingService. Windows has no equivalent; needs custom "Send to" targets.** | CONFIRMED | S1, S2, S20, S25 |
| H3 | **AppleScript API** | Commands: `open folder`, `show all folders`, `search`, `search notes`, `search folders`, `search themes`, `set theme`, `show preferences`. Classes: **Folder** (`name`; `get every folder`, `get name of every folder`, `make new folder`, `set name of`, `delete`) and **Note** (`text`, plus `pinned`, `folded`, `url` added in 1.6.3; `get notes in`, `make new note`, `set text of`, `delete`). Can create notes honoring the global "Creating Notes" settings. | 🍎 **AppleScript / OSA is macOS-only. Windows equivalent: COM automation, PowerShell module, or a local CLI/HTTP API.** | CONFIRMED | S7, S18, S19, S21 |
| H4 | **Apple Shortcuts actions** | Actions: **Create Note**, **Append to Note** (text, file shortcuts, images, URLs, file contents, or tasks), **Create Folder**, **Show Folder**, **Show Note**, **Find Notes**, **Find Folders**, **Export Note to Image**, **Find Theme**, **Set Theme**. 1.6.3 added actions to **pin** and **fold** notes and **retrieve note URLs**. 1.5 added new create-folder/open-note actions for SideNotes Mobile compatibility; pre-1.5 actions marked **Legacy** and discouraged. Requires macOS 12+. | 🍎 **Apple Shortcuts is macOS/iOS-only. Windows equivalent: Power Automate, a CLI, or a scriptable local API.** | CONFIRMED | S9, S17, S18, S21 |
| H5 | **URL scheme (`sidenotes://`)** | `sidenotes://open/<UUID>` opens a note or folder by ID. `sidenotes://add-note-with-text/<url-encoded-text>` creates a note with the given text, placed per Preferences → Creating Notes. Folder URLs copied via contextual menu → **Copy URL**; note URLs via Share button → **Copy Note URL**. Note URLs include the note title since 1.6.3. | Portable concept — Windows needs its own registered protocol handler | CONFIRMED | S8, S21 |
| H6 | **Alfred workflow** | Downloadable workflow to quickly add notes, use snippets, create folders, change theme, and search notes. | 🍎 **Alfred is macOS-only. Windows analogue: PowerToys Run / Flow Launcher plugin.** | CONFIRMED | S1, S2, S4 |
| H7 | **Hookmark integration** | Hookmark links files, apps and websites; documented integration with SideNotes. | 🍎 **Hookmark is macOS-only. No Windows equivalent.** | CONFIRMED | S1, S4 |
| H8 | **PopClip integration** | Listed as an integrated third-party app. | 🍎 **PopClip is macOS-only.** | CONFIRMED | S1 |
| H9 | **Dropzone integration** | Listed as an integrated third-party app. | 🍎 **Dropzone is macOS-only.** | CONFIRMED | S1 |
| H10 | **Raycast integration** | Listed as an integrated third-party app. | 🍎 **Raycast is primarily macOS (Windows version emerging).** | CONFIRMED | S1 |
| H11 | **Workspaces integration** | Open a SideNotes folder from Apptorium's own Workspaces app. | 🍎 **Workspaces is an Apptorium macOS app.** | CONFIRMED | S3 (T-how-to-open-folder-by-workspaces) |
| H12 | **MindNode integration** | Create mind maps from notes and quickly open them. | 🍎 **MindNode is macOS/iOS-only.** | CONFIRMED | S4 |
| H13 | **Things integration** | Create tasks in Things from notes, via Apple Shortcuts. | 🍎 **Things is Apple-platform-only; relies on Shortcuts.** | CONFIRMED | S4 |
| H14 | **Drag from other apps** | Drag text, files, folders, images from any app into SideNotes; dragging toward the screen edge reveals the panel to accept the drop. Improved drop feedback (background overlay) in 1.6.2. | No | CONFIRMED | S2, S18, S22 |
| H15 | **Clipboard behaviors** | Note-from-pasteboard (global shortcut + long-press **+**); quick-copy from search; copy image quickly; code detection on paste; link-title resolution on paste; suppress auto-spaces on paste. | No | CONFIRMED | S2, S14, S21, S22, S23 |
| H16 | **Dropshare sharing target** | Listed as a share destination. | 🍎 (Mac app) | CONFIRMED | S25 |
| H17 | **Handoff / Continuity** | — | 🍎 | **UNKNOWN** — not documented. Only Continuity Camera image insert (D31) is referenced. | — |
| H18 | **Services menu** | — | 🍎 | **UNKNOWN** — no macOS Services integration documented. | — |

---

## I. Import / export / backup / sync

| # | Feature | Behavior | Settings | macOS-specific | Status | Source |
|---|---|---|---|---|---|---|
| I1 | **Import text files** | Drag & drop text files into SideNotes to create notes. Whole **folders** of files can be dragged in for bulk import. | Preferences → Importing — choose import behavior for images and text files; option to automatically move imported files to Trash | No | CONFIRMED | S2, S18, T-how-to-import-text-files |
| I2 | **Import images** | Images dropped in are embedded; import behavior configurable. | Preferences → Importing | No | CONFIRMED | S18 |
| I3 | **Import files as shortcuts** | Files/folders dropped in become shortcuts with previews rather than embedded content. | Preferences → Text → File Links (standard / compact) | No | CONFIRMED | S20, T-how-to-add-shortcut-to-file |
| I4 | **Export Note to Image** | Export any note to an image, to share or post. Invoked via **Share button → Save as Image / Copy Image**, or by drag-and-drop. Added 1.4. | Preferences → **Exporting**: background (color or transparent), margins, aspect/size ratio, show or hide the bottom bar | No | CONFIRMED | S2, S18, T-how-to-export-a-note-to-image |
| I5 | **Export via Shortcuts** | *Export Note to Image* Shortcuts action; result saveable as a file. | — | 🍎 | CONFIRMED | S9 |
| I6 | **Print** | Print notes with `⌘P`. Added 1.6.3. | — | No | CONFIRMED | S21 |
| I7 | **Text export / Markdown file export** | — | — | — | **UNKNOWN** — no documented export to `.md`, `.txt`, PDF, or a bulk/all-notes export. Only **image** export and **print** are documented. This is a notable data-portability gap. | — |
| I8 | **Automatic backups** | SideNotes automatically backs up notes into the local SideNotes data folder. Frequency options **every 15, 30 and 45 minutes** (1.3). Old backups are cleaned up automatically. | Settings → Backups; custom backup folder selectable | No | CONFIRMED | S1, S2, S15 (1.2), S19, S10 |
| I9 | **Manual backup** | Settings → Backups → **Browse Backups…** → **Make backup**. Manual backups are **locked by default** so automatic cleanup will not delete them. | Settings → Backups | No | CONFIRMED | T-how-to-manually-make-backup |
| I10 | **Restore a backup** | Restore from a chosen backup (added with automatic backups in 1.2). | Settings → Backups → Browse Backups… | No | CONFIRMED | S15 (1.2), T-how-to-restore-backup |
| I11 | **Store backups in iCloud Drive** | Backups can optionally be stored in iCloud Drive rather than locally. | Settings → Backups → backup folder | 🍎 **iCloud Drive. Windows equivalent: OneDrive or a user-chosen sync folder.** | CONFIRMED | S1, S2, T-how-to-store-backups-in-cloud |
| I12 | **iCloud synchronization** | Notes sync automatically across Mac, iPhone and iPad. iCloud holds the canonical copy; each device keeps a local copy and syncs with the server. **Conflict resolution: last-saved-wins** — the most recently saved version takes precedence. If local files are accidentally deleted, SideNotes restores them from iCloud. Requires macOS 11+ and an enabled iCloud account. Disabling sync does **not** delete data already in iCloud. Since 1.3 a single unified iCloud database is used across all distribution channels. | Preferences → **Data** tab; also offered during first-run setup | 🍎 **CloudKit/iCloud. Windows needs an entirely different sync backend.** | CONFIRMED | S1, S11, S19, S10 |
| I13 | **Local storage location** | Primary DB + images: `~/Library/Application Support/SideNotes` (SQLite; "Do not modify these files manually"). Secondary location (older data, themes, automatic backups) varies by channel: App Store `~/Library/Containers/com.apptorium.SideNotes/Data/Library/Application Support/com.apptorium.SideNotes`; website `~/Library/Application Support/com.apptorium.SideNotes-paddle`; Setapp `~/Library/Application Support/com.apptorium.SideNotes-setapp`. Both locations reachable from Preferences → Data. | Preferences → Data | 🍎 (paths); **SQLite storage model is portable** | CONFIRMED | S10 |
| I14 | **Legacy storage format** | Up to 1.3, notes were stored as **JSON** (`data.json`) with images in a separate `images` folder; preferences in User Defaults. | — | 🍎 (User Defaults) | CONFIRMED | S10 |
| I15 | **Data portability** | No documented bulk export; portability is effectively via the SQLite database and backups. | — | — | INFERRED | S10 |

---

## J. Preferences / Settings — enumerated

The Settings window was **completely redesigned and rebuilt from scratch in 1.6** with improved structure and UX, and carries an **update-available badge** on the Settings button. Panes below are aggregated from all documented references; some tips cite older "Preferences" naming and some newer "Settings" naming.

| Pane | Setting | Options / notes | Status |
|---|---|---|---|
| **General** | Show or Hide Notes | `Open Bar` / `Hot Side` / `Menubar Icon` | CONFIRMED |
| General | Hide Open Bar | `never` (default) / `when mouse is inactive` / `always` (only in Hot Side & Menubar Icon modes) | CONFIRMED |
| General | Side | Left / Right (default Right) | CONFIRMED |
| General | Launch on Startup | On / off | CONFIRMED |
| General | Close when clicking outside SideNotes | On / off | CONFIRMED |
| **Notes** | Quick Formatting Toolbar | Above cursor / Below cursor / Disabled | CONFIRMED |
| **Folders** | Single-click folder opening | On / off (added 1.6) | CONFIRMED |
| **Creating Notes** | New note/folder placement | top / bottom / over current one / under current one | CONFIRMED |
| Creating Notes | Ask for folder when creating notes via global shortcut | On / off | CONFIRMED |
| Creating Notes | Open last folder on app restart | On / off | CONFIRMED |
| **Text** | Default Text Formatting | Markdown / Code mode (Setapp also implies plain text) | CONFIRMED |
| Text | Font family | All fonts settable (expanded 1.6) | CONFIRMED |
| Text | Font size | Adjustable (`⌘+` / `⌘−`) | CONFIRMED |
| Text | Line Height | Tight / Normal / Relaxed | CONFIRMED |
| Text | Paragraph Spacing | Tight / Normal / Relaxed | CONFIRMED |
| Text → **File Links** | File preview style | Standard (full width, default) / Compact | CONFIRMED |
| Text → **Colors** | Custom color copy format | Choose output format when copying `#rrggbb` colors (1.6) | CONFIRMED |
| Text → Colors | Disable `#rrggbb` color display | On / off (1.6) | CONFIRMED |
| Text | Prevent automatic spaces on pasted text | On / off (1.6.1) | CONFIRMED |
| Text | Code detection on paste | Paste as code block / as Code note (1.6.2) | CONFIRMED |
| **Appearance** | Light / Dark / System | Manual or follow system | CONFIRMED |
| Appearance | Themes → Manage | Install, list, Show in Finder | CONFIRMED |
| Appearance | Note Colors | `Background` (default) / `Bar` | CONFIRMED |
| Appearance | Default color for a new note | One of the six colors or empty | CONFIRMED |
| **Shortcuts** | Global Shortcuts | Show/Hide, Create Note, Search, Create Note from Pasteboard, Change Side, Pin window — each "Record Shortcut" | CONFIRMED |
| Shortcuts | ⎋ (Escape) behavior | Leave folder or hide (default) / Leave folder / Hide / None | CONFIRMED |
| **Importing** | Drag-and-drop on screen edge | Enable / disable | CONFIRMED |
| Importing | Show SideNotes while dragging across screen side | Enable / disable | CONFIRMED |
| Importing | Move imported files to Trash | On / off | CONFIRMED |
| Importing | Import behavior for images and text files | Selectable | CONFIRMED |
| **Exporting** | Image export background | Color or transparent | CONFIRMED |
| Exporting | Image export margins | Adjustable | CONFIRMED |
| Exporting | Image export aspect/size ratio | Selectable | CONFIRMED |
| Exporting | Show/hide bottom bar in exported image | On / off | CONFIRMED |
| **Data** | iCloud synchronization | Enable / disable (also offered at first run) | CONFIRMED |
| Data | Data locations | Reveal primary and secondary data folders | CONFIRMED |
| **Backups** | Automatic backups | Enable / disable | CONFIRMED |
| Backups | Backup frequency | Every 15 / 30 / 45 minutes | CONFIRMED |
| Backups | Backup folder | Custom location, incl. iCloud Drive | CONFIRMED |
| Backups | Browse Backups… | Make backup / restore / locked manual backups | CONFIRMED |

---

## K. Anything else

### K.1 Pricing & licensing

| Item | Detail | Status |
|---|---|---|
| Mac App Store / direct | **$19.99** one-time purchase, **per major version**; no subscription. Free upgrade for existing SideNotes 1.x users to 1.5. | CONFIRMED (S1, S17, S24) |
| Setapp | Included in Setapp membership (from $14.99/mo); Setapp also lists a standalone option from $9.99/yr; 7-day free trial | CONFIRMED (S25) |
| Trial | Free trial available from Apptorium's site | CONFIRMED (S1) |
| Student discount | Via StudentAppCentre | CONFIRMED (S1) |
| License deactivation | License can be deactivated to move it to another computer | CONFIRMED (S4) |
| Distribution channels | App Store, Setapp, Apptorium website (Paddle) — each with distinct data folder suffixes | CONFIRMED (S4, S10) |
| App size | 15 MB (App Store) / 35.5 MB (Setapp listing) | CONFIRMED (S24, S25) |
| Ratings | 4.5/5 from ~170 App Store ratings; 97% positive from ~1,470 Setapp reviews | CONFIRMED (S1, S24, S25) |
| Localization | 9 languages: English, French, German, Italian, Japanese, Korean, Polish, Simplified Chinese, Spanish (added 1.6) | CONFIRMED (S16, S24) |
| Maturity | 7 years of active development; 40+ updates since 2019 | CONFIRMED (S1) |

### K.2 Privacy

- "SideNotes is private by design. We do not collect, use, store, or have access to any of your notes" or personal data. Notes stored locally; iCloud sync uses Apple's private infrastructure. App Store privacy label: **developer does not collect any data**. CONFIRMED (S1, S24)

### K.3 Version history highlights

| Version | Date | Highlight |
|---|---|---|
| 1.1 | 2020-03-18 | Search, Share Extension, file shortcuts, menubar icon, move notes between folders, launch at startup, pin window, manual dark/light, AppleScript |
| 1.2 | 2020-09-23 | Automatic backups with restore |
| 1.2.5 | 2020-11-13 | UI refined for macOS Big Sur |
| 1.3 | 2021-07-28 | **iCloud sync**, note background colors, UI themes, font family choice, greyed-out markdown, inline links, color shortcuts, backup frequency options |
| 1.4 | 2021-10-22 | **Apple Shortcuts**, export note to image, expanded AppleScript, **Quick Look**, folder creation during move, quick folder access in search, launch URL from search |
| 1.4.3 / 1.4.4 | 2021-12 / 2022-01 | Enhanced AppleScript API, new shortcuts, text indentation |
| 1.4.8 | 2022-10-13 | macOS Ventura ready |
| 1.5 | 2025-06-16 | **Invisible Markdown** — all-new editor with hidden markup; new Shortcuts actions (old ones become Legacy); shared icon with SideNotes Mobile |
| 1.6 | 2026-05-11 | **Pinning notes & folders**, **folding notes**, clear completed tasks, underline, Change Side shortcut, horizontal separator, menu-bar right-click menu, **Quick Formatting Toolbar**, single-click folder opening, custom color copy format, new bottom bars, **rebuilt Settings**, 8 new translations |
| 1.6.1 | 2026-05-20 | No-auto-space paste option; fixes (Launch on Startup, Hot Side, emoji panel, font crash) |
| 1.6.2 | 2026-06-17 | **Code detection on paste**, completed tasks to bottom, move lines up/down, better DnD feedback |
| 1.6.3 | 2026-07-23 | **Folder duplication**, **printing (⌘P)**, more Shortcuts actions (pin/fold/note URL), improved Quick Copy, smarter pasted links, descriptive note URLs, AppleScript `pinned`/`folded`/`url` |
| 1.6.4 | 2026-09-03 | Writing improvements and fixes |
| 1.6.5 | 2026-09-07 | Fixed URLs not pasting into selected text |

CONFIRMED (S15–S23)

### K.4 Notable limitations / user complaints

| Limitation | Status |
|---|---|
| **No printing** — historically the top complaint; note contents had to be copied or exported elsewhere. **Resolved in 1.6.3** (`⌘P`). | CONFIRMED resolved (S21); complaint from S26 |
| **Trackpad edge gesture conflicts** — the two-finger gesture at the screen edge can conflict with other system gestures. | INFERRED (S26) |
| **No keyboard-trigger-only mode** — users dislike having to choose between a visible Open Bar (which can open accidentally) and an extra menu bar icon; a keyboard-only activation mode was requested. *(Partially mitigated by Hide Open Bar = `always` in Hot Side/Menubar modes.)* | INFERRED (S26) |
| **Setup friction** — must create a folder before creating notes; too many clicks for quick capture. | INFERRED (S26) |
| **Requested: shortcut + hot edge combination** — open only when a shortcut is held AND the cursor hits the edge. | INFERRED (S26) |
| **No documented bulk/text export** — image export and print only; a real data-portability gap. | INFERRED from documentation absence |
| **Flat folder hierarchy** — no documented sub-folders. | INFERRED from documentation absence |

### K.5 Companion product

- **SideNotes Mobile** — separate app for iPhone/iPad, iOS/iPadOS 17+, sold separately, syncs via iCloud, shares the app icon since 1.5. 🍎 CONFIRMED (S1, S17, S24)

---

## Windows parity: macOS-specific features requiring replacement

| # | macOS feature | Why macOS-specific | Windows direction |
|---|---|---|---|
| 1 | **iCloud synchronization** (I12) | CloudKit | Needs an entirely different sync backend (own service, OneDrive, Dropbox, or file-based sync) |
| 2 | **Backups in iCloud Drive** (I11) | iCloud Drive | OneDrive or user-chosen sync folder |
| 3 | **AppleScript API** (H3) | OSA/AppleScript | COM automation, PowerShell module, or local CLI/HTTP API |
| 4 | **Apple Shortcuts actions** (H4) | Shortcuts app | Power Automate, CLI, or scriptable local API |
| 5 | **Share Extension / system share sheet** (H2) | NSSharingService | No direct equivalent — custom "Send to" targets, or Windows Share contract |
| 6 | **Menu bar icon + right-click menu** (H1, A6) | macOS menu bar | System tray icon with context menu |
| 7 | **Quick Look preview** (D35) | macOS Quick Look | Custom in-app preview implementation |
| 8 | **Continuity Camera (image from iPhone/iPad)** (D31) | Apple Continuity | No equivalent — drop, or replace with phone-upload/QR flow |
| 9 | **Stage Manager / full-screen always-on-top** (A8) | Stage Manager; macOS window levels | Windows-specific topmost/appbar window handling; no Stage Manager analogue |
| 10 | **Spaces behavior** (A19) | macOS Spaces | Windows Virtual Desktops — behavior must be defined |
| 11 | **macOS Text Replacement** (D44) | System service | Own snippet/expansion feature, or none |
| 12 | **macOS spell check services** (D45) | NSSpellChecker | Windows spell-check API or bundled dictionary |
| 13 | **Alfred workflow** (H6) | macOS-only launcher | PowerToys Run / Flow Launcher plugin |
| 14 | **Hookmark integration** (H7) | macOS-only | No equivalent — drop |
| 15 | **PopClip integration** (H8) | macOS-only | No equivalent — drop |
| 16 | **Dropzone integration** (H9) | macOS-only | No equivalent — drop |
| 17 | **Raycast integration** (H10) | Primarily macOS | Optional; Raycast Windows is emerging |
| 18 | **Workspaces integration** (H11) | Apptorium macOS app | Not applicable |
| 19 | **MindNode integration** (H12) | Apple-platform-only | Not applicable / generic mind-map export |
| 20 | **Things integration** (H13) | Apple-platform-only, via Shortcuts | Not applicable / generic task-app export |
| 21 | **Dropshare share target** (H16) | Mac app | Not applicable |
| 22 | **Data folder paths** (I13) | `~/Library/Application Support/...` | `%APPDATA%` / `%LOCALAPPDATA%` — SQLite model itself is portable |
| 23 | **User Defaults preferences** (I14, legacy) | macOS defaults system | Registry or JSON config file |
| 24 | **Trackpad pinch-to-fold & two-finger edge gesture** (B12, A21) | macOS trackpad gestures | Precision-touchpad gestures where available; keyboard/mouse fallback required |
| 25 | **`sidenotes://` URL scheme** (H5) | Concept portable, registration is OS-specific | Register a custom protocol handler in the Windows registry |
| 26 | **SideNotes Mobile companion** (K.5) | iOS/iPadOS | Out of scope, or a separate Windows-compatible mobile strategy |

---

## Open questions to resolve by hands-on testing

1. Multi-display behavior — which screen, does it follow focus? (A18)
2. Virtual-desktop/Spaces behavior. (A19)
3. Exact chord for "move note to top/bottom". (B9)
4. Whether note-level duplication exists (folder duplication is confirmed). (B22)
5. Whether any trash/undo exists for deleted notes. (B23)
6. Folder nesting — confirm flat hierarchy. (C3)
7. Folder sorting options (name/date/manual). (C10)
8. Underline syntax and shortcut. (D7)
9. Table support — confirm absent. (D18)
10. Clear-formatting shortcut. (D21)
11. Search: live/incremental? Filters? In-note find? (E8)
12. Exact modifier for "open containing folder from search" — `⌃` vs `⌘`. (E5)
13. Window transparency/opacity setting beyond the Open Bar. (F11)
14. Text/Markdown/PDF export — confirm absent. (I7)
15. Whether non-global shortcuts are rebindable. (G.6)
16. macOS Services menu integration — confirm absent. (H18)
17. Handoff support — confirm absent. (H17)
