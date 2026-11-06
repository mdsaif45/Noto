# Basic Release (v0.1)

**Status:** Accepted, 2026-11-02 (owner's direction)
**Related:** [roadmap.md](roadmap.md), [implementation-plan.md](implementation-plan.md), [ADR-016](../decisions/ADR-016-card-stack-workspace.md)

The first release Noto ships is the **Basic Release**:

```
core note-taking  +  a clean SideNotes-level workspace  +  reliable local-first persistence
                  +  strong Windows-native behaviour   +  performance-conscious quality
```

It is the first build to call shippable. New ideas, integrations and differentiators come after it, in v0.2+.

Every feature is judged by one question:

> Does this materially improve the core note-taking product, or the SideNotes-level UX and stability bar, for v0.1?
> If not, it waits.

---

## In scope

| Area | v0.1 |
|---|---|
| Workspace | Docked side workspace, left or right; global show/hide; tray; start with Windows; multi-monitor and DPI correctness; keyboard-first |
| Folders | Create, rename, delete and restore, reorder, pin, drag and drop, move notes between them, recent folders |
| Notes | Create, edit, delete and restore, move, reorder, pin, fold, empty states |
| Editor | Markdown-backed storage with hidden markup:<br>• headings, bold, italic, underline, strike<br>• lists, tasks, links, code, separators<br>• undo and redo<br>• paste normalisation<br>• selection-aware and quick formatting<br>SideNotes 1.5–1.6 is the quality bar, not the implementation template. The WinUI control never owns the model. |
| Search | Global and in the current folder; highlighted results; keyboard navigation; quick copy. SQLite-backed |
| Rich content | Images (including clipboard images and drops), file and folder shortcuts, URLs. ~~`#rrggbb` colours~~: moved to v0.2 |
| Personalisation | Light and dark, themes. ~~Fonts, text size, note colours~~: moved to v0.2 |
| Safety | Restore, crash-safe saves, migration safety, corruption detection. ~~Automatic backups~~: moved to v0.2 |
| UX polish | The card stack, clean empty states, context menus, confirmations, focus correctness, tooltips, keyboard hints, reduced motion, High Contrast, UI Automation |

## Deferred to v0.2+

These do not delay v0.1:

- sync and accounts;
- mobile and web;
- AI and MCP;
- plugins;
- an HTTP API;
- advanced shell extensions;
- nested folders and tables;
- localisation;
- contextual and competitor-only features.

**Moved from v0.1 to v0.2 by the owner, 2026-11-06.** These were v0.1 scope that was not built when the release candidate was ready:

- note colours: the engine has them (`NoteColor`), with no UI;
- font and text size settings;
- colour chips for `#rrggbb`;
- automatic backups and their retention (#15);
- crash-loop detection (#10).

---

## Order

Each phase is one or more small PRs on `main`, never a long-lived integration branch. Identifiers are reconciled in [implementation-plan.md §1.0](implementation-plan.md).

| Phase | Work | Rules |
|---|---|---|
| Engine (done) | P1.0 design gate, P1.1 recycle bin, P1.2 create, rename and placement | - |
| A | Workspace and card stack (ADR-016): folders surface, cards edited in place, one editor card behind the `NoteEditorHost` seam, selection and focus, empty states, accessibility, keyboard, context menus | Do not rebuild the editor |
| B | Management: move, recent folders, pin, fold, reorder, drag and drop, delete and restore UI, confirmations | Use the existing engine commands; no domain rules in the UI |
| C | Editor: architecture first, then the formatting, tasks, lists, links, code, separators, paste and quick formatting | Do not let the WinUI control be the domain model |
| D | Search: SQLite-backed, keyboard-first | - |
| E | Rich content: images, file and folder shortcuts, URLs, colours | - |
| F | Backup, restore, text and Markdown import, image export | - |
| G | Release polish: accessibility, UX, multi-monitor and DPI, startup and tray, failure recovery | - |

## Quality gates

- **Every PR** (cheap gates):
  - tests;
  - mutation testing for critical behaviour;
  - Release and Debug with 0 warnings and 0 errors;
  - architecture guards, format, `git diff --check`.

  Plus a focused runtime check only where a PR's mechanics are risky.
- **Not after every PR:** the full desktop campaign. It runs once, on the frozen release candidate.

"Stable and optimised" is a bar of its own, not "tests pass":

| Stability | Performance |
|---|---|
| No data loss | Instant drawer |
| Crash-safe save | Fast note switching |
| Backup and restore | Responsive typing; no visible hitching in normal workflows |
| Migration-safe | Large notes handled |
| Clean startup and shutdown | 1k+ notes, 100+ folders |
| No leaked native hooks or windows | Idle CPU near zero |
| Stable over long sessions | Bounded memory growth |

The #11 measurements are the baseline. Budgets in [quality-gates.md](../architecture/quality-gates.md) do not move casually.

## Release candidate, then v0.1

1. Freeze a Release build.
2. Run the campaign on it:
   - runtime;
   - visual: Light, Dark and High Contrast;
   - keyboard and UI Automation;
   - Narrator listening;
   - performance before and after;
   - long-session soak;
   - crash and recovery;
   - migrations, backup and restore;
   - large datasets.
3. Then, in order: the parity score, the progress update, the release checklist, the installer and signing, and **v0.1**. The checklist, the owner's remaining steps and the decisions taken are in [release-v0.1.md](release-v0.1.md).

v0.2's new-ideas milestone does not start until v0.1 is genuinely shippable.
