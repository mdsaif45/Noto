# Noto — Keyboard Shortcuts

**Status:** Decided (v0.4, #266)
**Last updated:** 2026-11-24
**Parity rows:** [G52](sidenotes-parity.md#76-customization) (rebinding in-app shortcuts), [G26](sidenotes-parity.md#73-note-management) (a chord for pinning a note)

---

## What this document is

Every chord Noto has, where it lives in the code, and two decisions about them:

| Row | Decision |
| --- | -------- |
| **G52** | **Not now.** In-app chords stay fixed: one documented map, with no conflicts in it. Rebinding is reconsidered only on the [criteria below](#when-to-reconsider). The six global shortcuts stay rebindable, as they are today (G50). |
| **G26** | **`Alt+P`** pins or unpins the current note: the card, or the card being edited. On the folder list it does the same for the focused folder. Decided here and built in its own feature issue. |

The map below was read from the code on `main` at `5e9c1bc`, not from the parity table or the implementation plan.
Three open PRs add chords, and the map includes them, marked *(PR)*: `Ctrl+P` print (#262), `Ctrl+Enter` hide
(#261, off by default) and `Ctrl+Shift+D` duplicate (#237).

**The code is the source of truth, and this document follows it.** A PR that adds, moves or removes an in-app chord
updates these tables in the same PR, and checks the new chord against the [rules](#rules-for-a-new-chord).

---

## The map

### Scopes

A key goes to the narrowest scope that owns it. The scopes nest like this:

```
  Windows (RegisterHotKey)          global        works while Noto is hidden; rebindable (G50)
    └─ WorkspaceRoot                window        every surface
         ├─ PaneRoot                folder list
         ├─ NoteRoot                note stack    a focused card, and the card's editor
         │    └─ NoteEditorHost     editor        only the card being edited
         ├─ SearchPage              Search
         ├─ SettingsPage            Settings      (the recorders)
         ├─ RecycleBinPage, WelcomePage
         └─ PicturePreview          preview       over the whole drawer while open; every key is its own (#268)
```

Key handling uses five mechanisms. The order they run in is what keeps the map conflict-free, and it matters again in
the G52 decision below:

```
  key down
     │
     ├─ PreviewKeyDown tunnels root -> leaf   the stack's handler sees a key before the editor's
     │      (OnNoteListPreviewKeyDown, then NoteEditorHost.OnPreviewKeyDown and its Chord() table)
     │
     ├─ the control's own KeyDown             RichEdit keeps Ctrl+, Ctrl+F and the text-size chords,
     │                                        so HandedOn() passes them to the root by hand
     │
     └─ KeyboardAccelerators bubble leaf -> root   NoteRoot / PaneRoot first, then WorkspaceRoot
```

### Global (Settings › Shortcuts)

| Chord | Command | Default | Row | Source |
| ----- | ------- | ------- | --- | ------ |
| `Ctrl+Alt+Win+Space` | Show or hide Noto | on | G1 | `SettingKeys.HotkeyBinding` |
| `Ctrl+Shift+Win+N` | New note | off | G2 | `SettingKeys.NewNoteHotkeyBinding` |
| `Ctrl+Shift+Win+V` | New note from the clipboard | off | G4 | `SettingKeys.ClipboardNoteHotkeyBinding` |
| `Ctrl+Shift+Win+F` | Search | off | G3 | `SettingKeys.SearchHotkeyBinding` |
| `Ctrl+Shift+Win+S` | Change side | off | G5 | `SettingKeys.ChangeSideHotkeyBinding` |
| `Ctrl+Shift+Win+K` | Keep Noto open | off | G6 | `SettingKeys.KeepOpenHotkeyBinding` |

Each one can be recorded again in Settings (`SettingsPage` recorders, `App.RebindHotkey`). The recorder refuses a chord
another application holds, and refuses another of these six by name.

### Window (every surface)

| Chord | Command | Row | Source |
| ----- | ------- | --- | ------ |
| `Ctrl+W` | Hide Noto | G7 | `MainWindow.xaml`, WorkspaceRoot |
| `Ctrl+D` | Keep Noto open, on or off | G6 | `MainWindow.xaml`, WorkspaceRoot |
| `Ctrl+,` | Settings | — | `MainWindow.xaml.cs` constructor |
| `Ctrl+F` | Search | G45 | `MainWindow.xaml.cs` constructor |
| `Ctrl+=`, `Ctrl+Shift+=` (`Ctrl++`), `Ctrl+Num +` | Larger note text | G41 | `MainWindow.xaml.cs` constructor |
| `Ctrl+-`, `Ctrl+Num -` | Smaller note text | G42 | `MainWindow.xaml.cs` constructor |
| `Esc` | Back, or hide, as Settings › Escape says | G8, G51 | each surface's KeyDown, `OnEscape` |
| `Ctrl+Enter` *(PR #261)* | Hide Noto, off by default. Some places keep the chord for themselves: the editor on a link, Search with a result selected, a recorder that is listening, and the folder name boxes | G13 | `OnWorkspacePreviewKeyDown` |

### Folder list

| Chord | Command | Row | Source |
| ----- | ------- | --- | ------ |
| `Ctrl+N` | New folder (focuses the name box) | C2, G14 | `MainWindow.xaml`, PaneRoot |
| `Ctrl+Shift+O` | The folder open before this one | G10 | `MainWindow.xaml`, PaneRoot |
| `Ctrl+Down` | Enter the folder | G9 | `OnFolderListPreviewKeyDown` |
| `Ctrl+Alt+Shift+Up` / `Down` | Move the folder one place | C9 | `OnFolderListPreviewKeyDown` |
| `F2` | Rename | — | `OnFolderListKeyDown` |
| `Delete` | Move the folder to the Recycle Bin, after asking | C11 | `OnFolderListKeyDown` |
| `Shift+F10`, Menu key | The folder's menu | — | `ShowMenu` |

### Note stack (a focused card, and the card's editor)

| Chord | Command | Row | Source |
| ----- | ------- | --- | ------ |
| `Ctrl+N` | New note, edited | G14 | `MainWindow.xaml`, NoteRoot |
| `Ctrl+Shift+M` | Move to another folder (menu) | G16 | `MainWindow.xaml`, NoteRoot |
| `Ctrl+Shift+O` | The folder open before this one | G10 | `MainWindow.xaml`, NoteRoot |
| `Ctrl+Shift+D` *(PR #237)* | Duplicate the note | B22 | `MainWindow.xaml`, NoteRoot |
| `Ctrl+P` *(PR #262)* | Print the note | G25 | `MainWindow.xaml`, NoteRoot |
| `Ctrl+Alt+Shift+Up` / `Down` | Move the card one place | G17, G18 | `OnNoteListPreviewKeyDown` |
| `Ctrl+1` … `Ctrl+6`, `Ctrl+0` (number row or keypad) | Colour the card, or clear its colour | G23, G24 | `OnNoteListPreviewKeyDown`, `TryColorKey` |
| `Ctrl+Alt+Left` / `Right`, `Alt+Shift+Left` / `Right` | Fold or unfold the card | G19, G20 | `OnNoteListPreviewKeyDown` |
| `Ctrl+Alt+Shift+Left` / `Right` | Fold or unfold every card | G21, G22 | `OnNoteListPreviewKeyDown` |
| `Ctrl+Alt+Backspace` | Move the note to the Recycle Bin | G15 | `OnNoteRootKeyDown` |
| `Enter`, `F2` (on a card, not in the editor) | Edit the card | — | `OnNoteListPreviewKeyDown` |
| `Space` (on a card with pictures, not in the editor) | Preview the note's pictures, from the first. On a card without, Space stays the list's own: it selects the focused card, which focus has already selected | D35, G44 | `OnNoteListPreviewKeyDown` |
| `Shift+F10`, Menu key | The card's menu | — | `ShowMenu` |

Pin and unpin are on the card's menu and the folder's menu today, with no chord. Their key column is empty
(`MainWindow.xaml.cs`, the `"Pin"` and `"Unpin"` items). G26 below gives them one.

### Editor (the card being edited)

| Chord | Command | Row | Source |
| ----- | ------- | --- | ------ |
| `Esc` | Leave the editor | ADR-016 §5 | `OnPreviewKeyDown` |
| `Ctrl+Alt+Up` / `Down`, `Alt+Shift+Up` / `Down` | Edit the previous or next card | G11, G12 | `OnPreviewKeyDown` |
| `Alt+Up` / `Down` | Move the selected lines | G38, G39 | `OnPreviewKeyDown` |
| `Ctrl+Z` | Undo | — | `Chord()` |
| `Ctrl+Y`, `Ctrl+Shift+Z` | Redo | — | `Chord()` |
| `Ctrl+B`, `Ctrl+I`, `Ctrl+U` | Bold, italic, underline | G27, G28, D7 | `Chord()` |
| `Ctrl+Shift+E` | Strikethrough | D5 | `Chord()` |
| `Ctrl+E` | Code | — | `Chord()` |
| `Ctrl+K` | Link | — | `Chord()` |
| `Ctrl+Shift+H` | Heading | G29 | `Chord()` |
| `Ctrl+L`, `Ctrl+Shift+L` | Bulleted list, numbered list | G30, G31 | `Chord()` |
| `Ctrl+T`, `Ctrl+Shift+T` | Checklist, clear checked tasks | G32, G34 | `Chord()` |
| `Ctrl+.` | Check or uncheck | G33 | `Chord()` |
| `Ctrl+]`, `Ctrl+[` | Indent, outdent list items | G36, G37 | `Chord()` |
| `Ctrl+Shift+P` | Insert picture | G35 | `Chord()` |
| `Ctrl+Shift+R` | Show or hide markup | G40 | `Chord()` |
| `Ctrl+V` | Paste; HTML arrives as Markdown | — | `OnPaste` |
| `Ctrl+Shift+V` | Paste as plain text | — | `OnPreviewKeyDown` |
| `Ctrl+Enter`, `Ctrl+Click` | Open the link at the caret, or under the pointer | ADR-019 §2 | `OnPreviewKeyDown`, `OnPointerReleased` |
| Double-click a file link | Open it | D34 | `OnDoubleTapped` |
| `Enter` | New line, continuing a list | — | `OnPreviewKeyDown` |
| `Shift+Enter` | Line break | — | `OnPreviewKeyDown` |
| `Tab`, `Shift+Tab` | Nest or un-nest a list item; otherwise move focus. In Code, `Tab` indents | G36, G37, #195 | `OnPreviewKeyDown` |
| `Space` on a picture | Preview it. Only with the picture selected alone (as `Ctrl+C` copies it) or the caret inside its hidden markup; anywhere else, with any modifier, or while an IME composes, a space is typed | D35, G44 | `OnPreviewKeyDown`, `NotePictures.At` |

`Ctrl+C`, `Ctrl+X` and `Ctrl+A` are the edit control's own. None of RichEdit's formatting chords act
(`DisabledFormattingAccelerators="All"`), so `Ctrl+1`, `Ctrl+E`, `Ctrl+R`, `Ctrl+J` and the rest mean only what this map
says. The stack's chords above work in the editor too.

### Picture preview (while open)

| Chord | Command | Row | Source |
| ----- | ------- | --- | ------ |
| `Esc`, `Space` | Close it, focus back on the card or the editor's caret (on the key's release, so its character is never typed) | D35, G44 | `OnPictureKeyDown`, `OnPictureKeyUp` |
| `Left` / `Right` | The note's previous or next picture; stops at the first and the last | D35, G44 | `OnPictureKeyDown` |
| any other key | Nothing: `Tab` stays in the preview, and none of Noto's chords act behind it | — | `OnPictureKeyDown`, `OnPictureAccelerators` |

### Search

| Chord | Command | Row | Source |
| ----- | ------- | --- | ------ |
| `Up` / `Down` | Choose a result | E1 | `OnBoxPreviewKeyDown` |
| `Enter` | Open it | E1 | `OnBoxPreviewKeyDown`, `OnListPreviewKeyDown` |
| `Ctrl+Enter`, `Ctrl+Click` | Open the note's folder | G49 | `Open` |
| `Ctrl+C` | Copy the note (in the box, selected text is copied first) | G46 | `TryCopy` |
| `Ctrl+Shift+C` | Copy the note without its first line | G47 | `TryCopy` |
| `Ctrl+R` | Open the note's first link | G48 | `TryLaunch` |
| `Esc` | Back, as Settings › Escape says | G8 | `OnSearchKeyDown` |

### Settings, Recycle Bin, Welcome

| Where | Key | Command | Source |
| ----- | --- | ------- | ------ |
| Settings, a recorder listening | any | The key is the recorder's. None of Noto's own chords act; `Esc` cancels; losing activation cancels | `OnRecorderKeyDown`, `OnRecorderAccelerators` |
| Recycle Bin | `Enter` | Restore | `RecycleBinPage.OnListPreviewKeyDown` |
| Recycle Bin | `Delete` | Delete permanently, after asking | `RecycleBinPage.OnListPreviewKeyDown` |
| Welcome | `Enter`, `Esc` | Continue, close | `WelcomePage`, `MainWindow.Welcome` |

---

## Conflicts

### Inside the map: none

**Each chord has one meaning in each scope.** Five chords are used in more than one scope. Each time it is on purpose,
and the scope decides:

| Chord | Meanings | Why it is safe |
| ----- | -------- | -------------- |
| `Ctrl+N` | New folder; new note | "New" in whichever list has focus (G14) |
| `Ctrl+Shift+O` | The last folder, from either list | The same command in both |
| `Ctrl+Enter` | Open a link (editor); open the folder (Search); hide (#261, opt-in) | #261 asks the editor and Search first (`KeepsCtrlEnter`), so a link still opens |
| `Ctrl+C` | Copy text; copy a search result | Search copies the note only when no text is selected in its box |
| `Space` | Type a space; preview pictures (#268); press a button or a check box | The stack takes it only on a card with pictures, the editor only on a picture; buttons, check boxes, toggles, Search and Settings keep theirs. Plain `Space` is no IME chord (`Ctrl+Space` is), and a composing IME gets it first |

The three PRs add no conflict either:

- `Ctrl+P` is free; only `Ctrl+Shift+P` was taken.
- `Ctrl+Shift+D` is not `Ctrl+D`, because an accelerator matches its modifiers exactly.
- `Ctrl+Enter`'s owners are settled, as the table above says.

**No in-app chord is `Ctrl+Alt+<character key>`**, so §12a's rule 1 holds. The editor's `Chord()` runs only when Alt is
not held. The stack's `Ctrl+Alt` chords use arrows and Backspace, and AltGr types nothing with those keys
([implementation plan §10.2](../roadmap/implementation-plan.md#102-in-app)).

### Found by this inventory

1. **The global recorder accepts a chord Noto uses inside its own window.** `App.RebindHotkey` refuses only the other
   five global chords, and the `HotkeyChord` grammar accepts any `Ctrl+<letter>`. Suppose a user records `Ctrl+B` as
   show and hide. It registers, because no other application holds it, and from then on Windows delivers `Ctrl+B` to
   the hotkey: bold stops working in Noto's editor, and in every other application. The same goes for `Ctrl+N`,
   `Ctrl+Shift+P`, and `Alt+P` once G26 is built. **Fix:** refuse, by name, any chord in the window, list or editor
   tables above, as another global chord is refused today. A bug issue follows.
2. **With Narrator on, moving a card or a folder one place has no keyboard path.** Narrator takes every
   `Ctrl+Alt+arrow` and `Ctrl+Alt+Shift+arrow` for itself (#202, recorded in `NoteStackGuardTests`). #202 gave fold and
   unfold and previous and next an `Alt+Shift` chord. Fold all has the list bar's menu. But G17, G18 and C9 (move one
   place) have neither: the menus offer only Move to top and Move to bottom. **Fix:** Move up and Move down in the card
   and folder menus, with the existing chords shown beside them. A bug issue follows.
3. **`Ctrl+.` (G33) against the Microsoft Pinyin IME**, which uses `Ctrl+.` to switch its punctuation. This is known
   and accepted ([plan §10.3](../roadmap/implementation-plan.md#103-conflict-matrix)): while that IME is on, it wins, and
   Check or uncheck in the format menu still works. It is listed here so it is not found again.

To verify, not yet a finding: Windows switches input language on `Alt+Shift`. The `Alt+Shift+arrow` chords from #202 rely
on Windows switching only when `Alt+Shift` is pressed alone. Check this on a machine with two input languages, in the
RC accessibility checklist's K rows.

---

## G52 — rebinding in-app shortcuts: not now

### The options

| Option | What it costs | What it buys | Verdict |
| ------ | ------------- | ------------ | ------- |
| **1. None:** a fixed, documented map | This document, kept current | A map that is the same on every machine, which a guard test and a harness can pin | **Chosen** |
| **2. All, with conflict detection** | One command table that all five mechanisms read, in place of the work they do now; every label made from it; conflict detection that knows things Windows cannot report | A user can move a chord that collides with something on their machine | No, not now |
| **3. A curated subset** (create, pin, search, hide) | The same table and precedence work, for fewer chords | Little: see below | No |

### Why not now

**There is no cheap seam.** In-app keys go through five mechanisms, and the precedence between them is deliberate. The
stack sees a key before the editor does, so `Ctrl+Alt+Shift+Up` reorders before `Ctrl+Alt+Up` leaves. The editor hands
three chords up to the root by hand, because the edit control keeps them. If a chord could be rebound, it could move
from one mechanism's territory into another's. Each chord's label is also written out in up to three places: the menus'
`KeyboardAcceleratorTextOverride`, the tooltips, and `AutomationProperties.AcceleratorKey`, which is what Narrator reads.
Rebinding means building one table that drives all of this. That is a rewrite of the key handling, not a settings page.

**The global recorder cannot be reused as it is.** Its grammar allows letters, digits, F1–F24 and Space. The in-app map
also uses arrows, Backspace, Enter, `.`, `[`, `]`, `,`, `=` and `-`.

**The tests pin the chords, and they are meant to.** Seven guard-test files assert the chord tables as source text
(`EditorHostGuardTests` lists every `Chord()` line). The validation harnesses send about 350 keystrokes to Noto across 20
scripts. With rebinding, each of these would test a default, not the chord a user has, and the harness matrix would
grow with every binding.

**Honest conflict detection cannot be built.** `RegisterHotKey` can say whether a global chord is held. Nothing can say
whether Narrator, an IME, or the layout's AltGr will take an in-app chord. #202 found Narrator's `Ctrl+Alt+arrow` only by
running Narrator. If users rebind chords, each of them discovers these collisions alone, with no warning, and blames
Noto.

**AltGr no longer needs it.** §12a called rebinding "the mitigation of last resort" for `Ctrl+Alt+<letter>` chords. No
in-app chord is one now (see above), so there is nothing left to mitigate. Open rebinding would bring the risk back: a
user on a French layout could bind `Ctrl+Alt+E` and lose `€`.

**The parity row is weak.** G52 is SHOULD and provisional. Whether SideNotes can rebind in-app shortcuts at all is open
(Q15). Principle 9 defaults to no on "configuration options added instead of making a decision". Here, Noto cannot even
say the competitor has the feature.

**Principle 7 is met without it.** Principle 7 asks for a keyboard path to every primary action. The fixed map gives
one, and G26 below closes the last gap (pin). Its "independently disableable" clause is about activation surfaces: the
edge, the global hotkey and the tray. All three can already be turned off.

**The curated subset buys nothing on its own.** Create, search and hide already have rebindable global chords (G2, G3,
G1). Inside Noto they are `Ctrl+N`, `Ctrl+F` and `Ctrl+W`, Windows conventions nobody needs to move. Pin gets its chord
below. The subset would still need the table and the precedence work for whichever chords it included.

### What this means

- **G52 stays `[ ]`**, SHOULD, and is listed as a known gap at the M4 gate, as the parity doc's rule requires. It is not
  dropped.
- **This document is the map.** It is what G52 delivers instead of rebinding, and it is kept current by the rule at the
  top.
- **G50 is untouched.** The six global shortcuts stay rebindable.

### When to reconsider

G52 reopens if **any** of these becomes true:

1. **SideNotes can rebind in-app shortcuts.** Q15 is answered yes by hands-on testing. G52's parity weight then changes,
   and Q15 decides whether it is MUST or SHOULD.
2. **A chord here is unusable for a group of users,** on a layout, with an IME or with assistive technology, **and no
   fixed chord serves everyone.** If a fixed chord can solve it, the map changes and G52 stays closed.
3. **The key handling becomes one command table for another reason** (a command palette, for example). The main cost is
   then already paid, and rebinding becomes a settings page.

### Rules for a new chord

A new in-app chord must pass all of these. They are what keeps the map conflict-free without rebinding:

1. **Not `Ctrl+Alt+<character key>`.** AltGr is `Ctrl+Alt` (§12a). `Ctrl+Alt` with an arrow or Backspace is allowed.
2. **Not a Windows, IME or layout chord:** no `Win+…`, nothing on `Ctrl+Space`, `Ctrl+Shift` alone, `Alt+Shift` alone or
   `Ctrl+Shift+<digit>` (Windows' input-language hotkeys), and nothing on `Alt+` or `Ctrl+` with a backtick.
3. **One meaning per scope.** It is checked against every table above, the open PRs included.
4. **Narrator-safe, or with a second path.** If Narrator takes the chord (`Ctrl+Alt+arrow` and its `Shift` forms do),
   give the command a second chord or a menu item.
5. **Labelled wherever it shows:** the menu item's key text, the tooltip, and `AutomationProperties.AcceleratorKey` on a
   button.
6. **Listed here, in the same PR,** with its scope, command, row and source.

---

## G26 — a chord for pinning a note: `Alt+P`

### What exists today

Pinning works: the card's menu has Pin and Unpin (`MainWindow.PinCard`, B14), and the folder's menu has the same for
folders (`MainWindow.PinFolder`, C8). Neither has a chord, so their key column is empty. The header's pin glyph is a
different command, Keep Noto open (`Ctrl+D`, G6). The implementation plan's decision 3 gave `Ctrl+Shift+P` to insert
picture and left pin with no chord. This decision supersedes that, for G26 only.

### The candidates

| Chord | Why not |
| ----- | ------- |
| `Ctrl+Shift+P` | Insert picture (G35). It maps SideNotes' `⇧⌘P` literally and is CONFIRMED; pin has no SideNotes chord at all |
| `Ctrl+P` | Print (G25, #262), which maps `⌘P` literally |
| `Ctrl+Alt+P` | An AltGr chord (§12a rule 1) |
| `Alt+Shift+P` | `Alt+Shift` is Windows' input-language switch |
| `Ctrl+S`, `Ctrl+Shift+S` | Save, by reflex. Noto saves by itself, so a habitual `Ctrl+S` would move a note to the pinned group |
| `Ctrl+Shift+K` | Free, but K is already "keep Noto open" (`Ctrl+Shift+Win+K`). Two kinds of pin on one letter would be confused |
| `Ctrl+Shift+I`, `Ctrl+Shift+U`, other free letters | Free, but nothing about them says "pin" |
| **`Alt+P`** | **Chosen** |

### Why `Alt+P`

- **P for pin**, the first letter of the menu item it stands for.
- **Free in every scope** of the map above, the three PRs included. Nothing in Noto uses `Alt` with a letter.
- **Not AltGr.** AltGr arrives as `Ctrl+Alt`. An accelerator for exactly `Alt` does not match `AltGr+P`, so a character
  typed with AltGr is never taken.
- **Not a Windows, IME or layout chord.** It is not `Alt+Shift`, not `Ctrl+Space`, and not `Win`. It passes the rules
  above.
- **Windows does this too.** Where there is no menu bar, Windows itself uses `Alt+<letter>` as a command: Explorer's
  `Alt+P` shows the preview pane. Noto has no menu bar and no access keys, so `Alt+P` opens no menu and shows no key
  tip. If Noto ever adds access keys, P is Pin's.
- **A slip costs little.** `Ctrl+P` is next to it. If one is pressed for the other, print opens a dialog that `Esc`
  closes, and pin is a toggle that the same chord undoes and that is announced.

**On the folder list it pins folders.** `Ctrl+N` means "new" in both lists, so `Alt+P` means "pin" in both. It is one
more accelerator, and it gives C8 a keyboard path too.

### Acceptance criteria (for the feature issue)

1. **The note stack.** `Alt+P` pins the current card's note if it is unpinned and unpins it if it is pinned. It works
   from a focused card and from the card's editor. It is the same command as the card menu's Pin and Unpin
   (`PinCard`):
   - the card moves into or out of the pinned group, and focus stays on it;
   - a card being edited keeps editing, with its unsaved text;
   - "Pinned" or "Unpinned" is announced.
2. **The folder list.** `Alt+P` pins or unpins the focused folder. It is the same command as the folder menu's Pin and
   Unpin (`PinFolder`).
3. **Nothing to pin.** In an empty folder, or with nothing focused, `Alt+P` does nothing and says nothing. It is still
   handled, so there is no beep and no menu mode.
4. **Exactly Alt.** `Ctrl+Alt+P` (AltGr+P) and `Alt+Shift+P` are not the chord. On a layout where `AltGr+P` types a
   character, the character is typed.
5. **Only where it belongs.** `Alt+P` does nothing in Search, Settings, the Recycle Bin or Welcome. A listening recorder
   takes it like any other key.
6. **Labelled.** The card and folder menus show `Alt+P` beside Pin and Unpin. The accelerators sit on NoteRoot and
   PaneRoot, which hide key tips (`KeyboardAcceleratorPlacementMode="Hidden"`).
7. **Pinned by tests.**
   - A guard test asserts the two accelerators (`Key="P"`, `Modifiers="Menu"`) and the menu labels.
   - An `Invoke-NoteStack` case pins and unpins with `Alt+P`, from a card and from its editor, and reads the pin back
     from the store.
   - An `Invoke-NoteStack` folder case does the same for a folder.
   - The RC accessibility checklist gains a Narrator row: with Narrator on, `Alt+P` reaches Noto and "Pinned" is spoken.
8. **Documented.** This document's stack and folder tables gain the row. G26 is scored. The CHANGELOG says it.
