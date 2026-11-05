# Accessibility RC checklist (v0.1)

The manual, interactive half of the release-candidate campaign (implementation plan, phase RC). It runs **once, on the frozen release candidate**, after the performance and stability slice, on a real desktop by a person at the keyboard. Automated guards and harnesses already hold names, focus order, raw decoration and theme brushes (`DesignSystemGuardTests`, `*GuardTests`, `Invoke-*.ps1`); this list covers what only a person with the real assistive technology can judge.

Record each item as **pass**, **fail** (with a defect filed) or **n/a**, with the build SHA, Windows version and the setting under test. A fail is fixed within the existing design; it is not a reason to lower the bar.

## 1. Narrator (Windows' screen reader)

Turn on with `Win+Ctrl+Enter`. Scan mode off unless a row says otherwise.

| # | Where | Do | Pass when Narrator says |
|---|---|---|---|
| N1 | Folder list | Tab into it, arrow through rows | each folder's name; "pinned" for a pinned one; the list's name and count |
| N2 | Folder list | Ctrl+N, type a name, Enter | the name box's name; then the new folder, focused |
| N3 | Card stack | enter a folder (Ctrl+Down), arrow through cards | each card's title; its help text (pinned, folded); the folder's title as the surface's heading |
| N4 | Card stack | create (Ctrl+N), pin, fold, move, reorder, delete a card | "Note created", "Pinned"/"Unpinned", "Folded"/"Unfolded", "Moved to …", "Moved up/down/to top/to bottom", "Note moved to the Recycle Bin": once each, not repeated |
| N5 | Editor | open a note (Enter), read by line and by character | the text as written; hidden markup **not** read as noise (ADR-017 §6); "Note content" as the editor's name |
| N6 | Editor, long note | a note taller than the stack: read to the end with Narrator's reading keys | reading continues inside the card as it scrolls (ADR-017 amendment); the caret stays in view |
| N7 | Editor | format (Ctrl+B, Ctrl+K), lists (Ctrl+L), tasks (Ctrl+T, Ctrl+.) | the changed text; a task's state where it is read |
| N8 | Pictures | a note with pictures | the caption (the source text), never the picture twice |
| N9 | Search | Ctrl+F, type, arrow through results | the count as it changes ("N results"), each result's title and folder; "No notes match" |
| N10 | Settings | Ctrl+, then Tab through every control | every control's name, state and help text; section headings at level 2; a refused shortcut announced |
| N11 | Settings > Data | back up, export, import, a refused restore | the progress line politely; the result; a failure assertively |
| N12 | Recycle Bin | open, arrow, restore, delete permanently | each entry with its state; the confirmation's buttons, Cancel first |
| N13 | Welcome | a new workspace; Next, Back, Skip, Get started | "Step N of 4. Title. Summary." once per step; the buttons' names |
| N14 | Notices | a failed save, a restore that rolled back | the notice, assertively, once |
| N15 | Tray | the tray icon and its menu | "Noto"; each menu item; Keep Noto open's state |

## 2. High Contrast

Settings > Accessibility > Contrast themes. Each of **Aquatic, Desert, Dusk, Night sky**, with Noto set to System, Light and Dark (High Contrast must win over all three).

| # | Check | Pass when |
|---|---|---|
| H1 | Every surface: folders, stack, editor, search, settings, recycle bin, welcome, menus | every text, border, focus ring and icon is drawn in the theme's own colours; nothing in Noto's palette remains |
| H2 | Selection and focus | a selected row and the focused control are told apart by the theme's highlight, not by colour alone |
| H3 | Editor | markup, links, code and task checkboxes in theme colours; the caret visible |
| H4 | Pictures | drawn as pictures; their captions readable |
| H5 | Disabled controls | readable and distinguishable from enabled ones |
| H6 | Switching contrast while Noto is open | Noto follows at once, no restart |

## 3. Display scaling (DPI)

Settings > System > Display > Scale. Restart nothing between changes unless Windows asks.

| # | Scale | Pass when |
|---|---|---|
| D1 | 100% | layout as designed; no clipped text or controls |
| D2 | 125% | same; the drawer's width scales; the switch tracks whole (Invoke-Visual's check) |
| D3 | 150% | same; long folder names end in an ellipsis; card text wraps |
| D4 | 200% | same; the editing card fits the stack (the cap) and scrolls inside |
| D5 | Text size (Accessibility > Text size) 150% and 225% | text grows; nothing overlaps; the editor and cards reflow |
| D6 | Scale changed while Noto is shown | Noto re-docks at the new size without a restart |

## 4. Multiple monitors

| # | Arrangement | Pass when |
|---|---|---|
| M1 | Two monitors side by side, Noto on the right edge | it docks to the right edge of the monitor it is shown on |
| M2 | Second monitor to the **left** of the primary | docking on the left edge and on the right edge of each monitor |
| M3 | Monitors of different scale (100% and 150%) | moving between them re-docks at the right size; text is sharp on both |
| M4 | Monitor stacked above or below | the drawer spans the work area of its own monitor only |
| M5 | Hotkey with focus on the other monitor | Noto appears on the monitor that has the foreground window, docked |
| M6 | Disconnect the monitor Noto is on | Noto moves to a remaining monitor when next shown; nothing is lost |
| M7 | Per-monitor width | each monitor keeps the width chosen there (`workspace.width::<scope>`) |

## 5. Keyboard layouts and input methods

| # | Layout or IME | Pass when |
|---|---|---|
| K1 | US, UK | every documented shortcut works (Ctrl+N, Ctrl+F, Ctrl+, Ctrl+W, Ctrl+B/I/K, Ctrl+Alt+Shift+Up/Down, Ctrl+Down) |
| K2 | German (QWERTZ), French (AZERTY), Polish (programmer) | **AltGr characters type** in the editor and the name box (AltGr arrives as Ctrl+Alt: no shortcut may swallow it, quality-gates.md); `@ { } [ ] \ €` type |
| K3 | Japanese IME (Microsoft) | composition in the editor: no restyle mid-composition, one undo step after it ends; candidates window placed at the caret |
| K4 | Chinese Pinyin, Korean | same as K3 |
| K5 | Emoji panel (`Win+.`) and clipboard history (`Win+V`) | insert into the editor and the name box |
| K6 | The global shortcut under each layout | Ctrl+Alt+Win+Space shows and hides Noto; a chord that collides is refused and announced in Settings |
| K7 | Dictation (`Win+H`) | text arrives in the editor as typed text (UI Automation's SetValue makes only the change) |

## 6. Focus and restoration

| # | Flow | Pass when |
|---|---|---|
| F1 | Hide (hotkey, Ctrl+W, click elsewhere) and show again | focus returns to where it was: the editor at its caret, a card, a folder |
| F2 | Settings, Search, Recycle Bin, Welcome: open and leave | focus returns to the control it was opened from |
| F3 | Delete a folder or a card | focus moves to the next row, never lost |
| F4 | A dialog (restore confirmation, delete permanently) | opens with Cancel focused; Escape cancels; focus returns |
| F5 | Tab through every surface | visual order; no trap; a visible focus ring everywhere (keyboard only) |
| F6 | Reduced motion (Animation effects off) | no slide; every surface still appears and takes focus |

## 7. Sign-off

The RC passes this list when every row is pass or n/a, on the reference machine and on one other Windows 11 machine with a different scale. Re-score the parity rows that depend on it (implementation plan, RC.1) only then.
