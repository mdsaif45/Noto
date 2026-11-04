# ADR-021 — The welcome: a four-step tour for a new workspace

**Status:** Accepted
**Date:** 2026-11-04
**Related:**
- ADR-002 (local-first), ADR-007 (the drawer and its Escape behaviour), ADR-009 (settings are typed keys), ADR-011 (design system: tokens only, one button role), ADR-016 (the note stack), ADR-020 (backup and restore)
- Basic Release plan: the welcome comes after data portability and before the final polish

---

## Context

A new user opens Noto to an empty folder list and a name box. Nothing says that Noto is summoned by a global shortcut, that it lives at the screen's edge, that a folder holds notes, that notes are Markdown cards saved as they are typed, or that everything stays on this PC and can be backed up. The features are in place (ADR-016 to ADR-020); a new user cannot find them.

## Problem

Tell a new user, once, what Noto is and how to use it, without:

- showing it to anyone who already has a workspace: upgrades, restores and existing data roots;
- interrupting startup again after the user has finished or skipped it;
- costing anything on an ordinary start;
- a new navigation framework, a wizard engine, new visuals or animation.

## Options considered

1. **A dialog (ContentDialog) at first start.** Modal over a 360 DIP drawer, it covers the window the user has not seen yet, and cannot be reopened as a surface of its own.
2. **Empty-state hints only.** Already there ("No folders yet…"); they cannot explain the shortcut, Markdown or backups, which are not visible from the empty list.
3. **A surface of the drawer, like Settings**, opened at a new workspace's first start and from Settings > Help.

## Decision

Option 3.

### 1. When it shows: only a genuinely new workspace

The rule lives in the settings layer: one typed key, `onboarding.welcome` (`WelcomeState`: `None`, `Due`, `Completed`, `Skipped`; default `None`), and `Onboarding.ShowAtStart`, called once per start after settings load and before the window exists.

```
start ─► PendingRestore.ApplyIfPending ─► created = NotoDatabase.Initialize()
                 │ Applied?                     │ true only for an empty file (no schema before this call)
                 ▼                              ▼
          restored ──yes──► Due? write None ─► no welcome
                 │no
                 ▼
          created ──yes──► write Due
                 ▼
          (created or Due) ─► holds nothing ─► welcome
                          holds data ───► write None, no welcome (no longer new)
                          cannot say ───► no welcome now, still owed
```

- **Owed only by creation.** `Due` is written only when this start created the database. A data root opened by an older Noto (no row) reads `None`, empty or not: an upgrade never shows it.
- **A restore never shows it.** A restored workspace is an existing one; a `Due` it carries (a backup taken while its own welcome was open) is settled to `None`.
- **Nothing of the user's.** Shown only while no folder or note exists, in the Recycle Bin or out of it (`Onboarding.HoldsAnything`; three reads, made only when the welcome is owed). A workspace that cannot answer is treated as holding data.
- **Owed until it ends.** Only Get started (`Completed`) and Skip or Escape (`Skipped`) record an end. A start that ends or crashes with the welcome open leaves it owed, and the next start shows it again. A workspace still owed it that has come to hold data (an import while it was open, then a crash) is settled to `None`: it is no longer new, emptying it later does not bring the welcome back, and its data is never read at start again.
- **Never in the way.** A setting that cannot be written changes nothing for this start and throws nothing (#9: settings never stop startup). At worst the welcome shows once more, or not at all.

### 2. What it is: one surface, four steps

`WelcomePage`, created when it opens and released when it is left, beside the other surfaces in the drawer (as `SettingsPage`), opened after the folder list's first load. The steps and their words are `WelcomeTour` (Core), so they are tested with the navigation:

| Step | Title | Says |
|---|---|---|
| 1 | Welcome to Noto | the show/hide shortcut as it is set (or how to turn it on), the notification-area icon, Ctrl+W |
| 2 | Organize | making and opening folders, Ctrl+N for a note, pin/fold/move from the menu, drag or Ctrl+Alt+Shift+Up/Down to order |
| 3 | Write | cards, saving as you type, Markdown, the formatting keys, pictures, Ctrl+F |
| 4 | Your data | local-first, the data folder, Settings > Data's backup, restore, export and import; Settings > Help reopens the tour |

Moving is bounded by the tour: Back on the first step and Next on the last do nothing.

### 3. How it looks and behaves

- One column, centred, no wider than `ReadingWidth` (440 DIP, a new token: a measure of text); inside the 360 DIP drawer it is never reached.
- **Where the reader is, in words:** "Step 2 of 4" in the caption style, and a native ProgressBar that shows the same by its length (raw to UI Automation, decoration). Never by colour alone.
- Title in `SurfaceTitleStyle` (heading level 1), one secondary line under it, the lines in a `SettingsGroupStyle` card: the existing card language.
- **Button hierarchy without a new role:** the primary action (Next, then Get started) is the only button with a fill and border (`FolderPaneButtonStyle`); Back and Skip are subtle (`SubtleButtonStyle`). Skip sits apart on the left. ADR-011 rule 4 holds: no new button style.
- **Back is absent on the first step**, not disabled: a disabled subtle button gains a border it has nowhere else, a control that does nothing.
- **Keys:** Enter is the primary action unless a focused button takes it (Enter on Back goes back); Escape skips; Tab follows reading order (Skip, Back, Next) and moves on to the header's buttons, so there is no trap; focus opens on the primary action and returns to it when Back disappears.
- **Screen readers:** the page is one element, `WelcomeRoot`, "Welcome to Noto"; every control is named; a step change is announced once ("Step 2 of 4. Organize. Folders hold your notes.") while focus stays on the button that moved.
- **Reopening:** Settings > Help > Show tour opens it over Settings; leaving it goes back to Settings with focus on Show tour. Opened from Settings, Settings can be opened from it and left again without nesting.
- No motion beyond the existing surface transition.

## Rationale

- The database's creation is the one fact that tells a new workspace from an old one without guessing from data: an upgraded empty workspace and a fresh one look the same in their tables.
- Keeping the rule in a typed setting means it is backed up, restored and inspected like every other preference, with no new table and no new file.
- A drawer surface reuses the lifecycle, focus return and Escape handling Settings already proved.

## Consequences

- One new setting key, one Core enum and model, one use case, one surface. `NotoDatabase.Initialize` now says whether it created the database.
- **Performance.** An ordinary start adds one read of the in-memory settings cache; the three table reads happen only while the welcome is owed, and a workspace that turns out to hold data stops owing it, so they never repeat on a workspace with notes. Measured in the PR.
- The validation harness skips the welcome when it starts Noto on a new data root (`Start-Noto`; `-KeepWelcome` keeps it), and stops a run if a new data root does not show it.

## Alternatives rejected

- **A first-run flag file beside the database.** A second source of truth that a restore or a copied data root would disagree with.
- **"No data" alone as the rule.** An upgraded workspace whose user emptied it would be welcomed as new.
- **Recording completion when the welcome opens.** A crash in the middle would lose it for good.

## Future reconsideration criteria

- A localized build: the words move out of `WelcomeTour` to resources.
- A feature the tour does not mention becomes central to first use.
- Users report the tour reappearing after an interrupted first start as a nuisance rather than a help.
