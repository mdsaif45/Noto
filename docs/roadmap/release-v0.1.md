# v0.1 release checklist

**Status:** open. **Owner decisions recorded:** 2026-11-06.
**Related:** [basic-release.md](basic-release.md) (scope and order), [rc-accessibility-checklist.md](rc-accessibility-checklist.md), [ADR-008](../decisions/ADR-008-packaging-and-identity.md), [CHANGELOG](../../CHANGELOG.md)

This is the release path from here to a published v0.1: who does what, in order. A box is ticked in the PR that does it.

---

## Done

- [x] Phases A–G built: the workspace, management, editor, search, rich content, portability and welcome (#115–#138)
- [x] Performance and stability baseline: budgets measured, a crash, freezes and typing latency fixed (#139)
- [x] An unsigned installer that upgrades and uninstalls without touching the notes (#140)
- [x] Release paperwork: this checklist, the CHANGELOG, the README status, and the issues triaged

## Owner to do

### 1. Accessibility check (when the second monitor arrives)

Work through [rc-accessibility-checklist.md](rc-accessibility-checklist.md) on the frozen release candidate. For each item, record **pass**, **fail** (and file an issue) or **n/a**, with the build, the Windows version and the setting under test.

- [ ] §1 Narrator: Windows' screen reader, `Win+Ctrl+Enter`
- [ ] §2 High Contrast
- [ ] §3 Display scaling: 100%, 125%, 150%, 175% and 200%
- [ ] §4 Multiple monitors (needs the second monitor)
- [ ] §5 Keyboard layouts and input methods (IME)
- [ ] §6 Focus and restoration
- [ ] §7 Sign-off

A fail is fixed within the existing design before release; the bar is not lowered.

### 2. First start after installing the release

On 2026-11-06 the owner decided to judge this on the release itself.
- **Measured:** the first start after an install is 3.6 s at the median (3.3–4.2 s). The budget is 4 s, and 2 of 5 runs went over ([quality-gates.md](../architecture/quality-gates.md)).
- **To do:** install the published v0.1 and note how long the first start takes.
- **If it feels slow:** file an issue.

- [ ] First start after installing v0.1 checked

### 3. After the release: clean machines (deferred)

On 2026-11-06 the owner deferred the clean-machine install test until after v0.1 (ADR-008, release prerequisites).

- [ ] After the release, remove every Noto trace from the development PC:
  - Settings › Apps › Noto › Uninstall;
  - `%LOCALAPPDATA%\Noto.App`;
  - `%LOCALAPPDATA%\velopack`;
  - the Start menu `Noto` shortcut.
  - Keep `%LOCALAPPDATA%\Noto` (your notes) unless you mean to start fresh.
- [ ] Install v0.1 on that clean machine, as a new user would: download, then Setup.
- [ ] Test on a clean Windows 10 22H2 machine as well (#32).

## Decided

| Decision | Date | Where recorded |
|---|---|---|
| v0.1 ships **unsigned**. SmartScreen warns on the first run; Smart App Control blocks it. | 2026-11-06 | ADR-008, CHANGELOG known limitations |
| The clean-machine install test waits until after the release. | 2026-11-06 | ADR-008, §3 above |
| The first start after install is judged on the installed release. | 2026-11-06 | quality-gates.md, §2 above |
| Memory-footprint work is deferred: idle memory is 152 MB against a 150 MB budget. | 2026-11-06 | quality-gates.md |

**Still the owner's call before release.** These are v0.1 scope rows in [basic-release.md](basic-release.md) that are not built. Ship v0.1 without them, or build them first:

- [ ] Note colours. The engine has them (`NoteColor`); there is no UI.
- [ ] Font and text size settings.
- [ ] Colour chips for `#rrggbb`.
- [ ] Automatic backups and backup retention (#15). Manual backup and restore exist.
- [ ] Crash-loop detection (#10).

## Release steps (after the owner to-dos above)

In order, following [basic-release.md](basic-release.md), *Release candidate, then v0.1*:

1. [ ] **Freeze** the release candidate: tag-free, from `main`.
2. [ ] **Run the campaign** on it:
   - every harness in `tools/validation` (README);
   - `Invoke-Install.ps1`;
   - the accessibility check (§1).
3. [ ] **Score the parity rows** in [sidenotes-parity.md](../product/sidenotes-parity.md) against the running candidate.
4. [ ] **Update** [progress.md](../project/progress.md) to the candidate's state.
5. [ ] **Date the CHANGELOG:** in `CHANGELOG.md`, change `## [0.1.0] - Unreleased` to the release date.
6. [ ] **Run the release workflow:** Actions › Release › Run, with version `0.1.0` and pre-release off. It:
   - checks the version and the CHANGELOG;
   - builds the installer;
   - creates a **draft** GitHub release with `Noto.App-win-Setup.exe`, the package and the feed attached.
7. [ ] **Publish:** the owner reads the draft release and publishes it.
