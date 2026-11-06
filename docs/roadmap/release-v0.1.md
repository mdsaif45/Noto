# v0.1 release checklist

**Status:** releasing. **Owner decisions recorded:** 2026-11-06.
**Related:** [basic-release.md](basic-release.md) (scope and order), [rc-accessibility-checklist.md](rc-accessibility-checklist.md), [ADR-008](../decisions/ADR-008-packaging-and-identity.md), [CHANGELOG](../../CHANGELOG.md)

This is the release path to a published v0.1, and what follows it. A box is ticked in the PR that does it.

---

## Done

- [x] Phases A–G: the workspace, management, the editor, search, rich content, portability and the welcome (#115–#138)
- [x] Performance and stability baseline: budgets measured; a crash, freezes and typing latency fixed (#139)
- [x] An unsigned installer that upgrades and uninstalls without touching the notes (#140)
- [x] Release paperwork: this checklist, the CHANGELOG, the README status, and the issues triaged (#141)

## Release steps

1. [x] **Freeze the release candidate.**
   - The product code on `main` is byte for byte that of `70f5897`, the build the campaign ran on.
   - Since then, only documentation, CI, one test and one harness have changed.
2. [x] **Run the campaign** on that build, the extracted release package:
   - 15 runtime harnesses: 458 checks, 0 failed, 0 crash events;
   - `Invoke-Install.ps1`: install, upgrade while running, and uninstall, 24/24;
   - the startup comparison (#140).
3. [x] **Date the CHANGELOG:** `[0.1.0] - 2026-11-06`.
4. [ ] **Run the release workflow:** Actions › Release › Run, with version `0.1.0`. It:
   - checks the version and the CHANGELOG;
   - builds the installer from `main`;
   - creates a **draft** GitHub release with `Noto.App-win-Setup.exe`, the package and the feed attached.
5. [ ] **Publish:** the owner reads the draft and publishes it. Publishing creates the tag `v0.1.0`.

## After the release (owner)

These are tracked in the [v0.2 milestone](https://github.com/mdsaif45/Noto/milestone/11), with [release-v0.2.md](release-v0.2.md):

| Section | Issue |
|---|---|
| §1 accessibility check | #161 |
| §1, multiple monitors | #30 |
| §3 clean machines and Windows 10 | #32 |
| §4 the parity score and progress | #162 |

### 1. Accessibility check (when the second monitor arrives)

On 2026-11-06 the owner moved this after the release. Until it is done, v0.1 has not been checked by hand with assistive technology. The CHANGELOG says so.

Work through [rc-accessibility-checklist.md](rc-accessibility-checklist.md) on the installed v0.1. Record each item as:
- **pass**;
- **fail**, and file an issue;
- **n/a**.

Record the build, the Windows version and the setting under test with each.

- [ ] §1 Narrator: Windows' screen reader, `Win+Ctrl+Enter`
- [ ] §2 High Contrast
- [ ] §3 Display scaling: 100%, 125%, 150%, 175% and 200%
- [ ] §4 Multiple monitors (needs the second monitor)
- [ ] §5 Keyboard layouts and input methods (IME)
- [ ] §6 Focus and restoration
- [ ] §7 Sign-off

A fail is fixed in a v0.1.x release, within the existing design. The bar is not lowered.

### 2. First start after installing the release

On 2026-11-06 the owner decided to judge this on the release itself.
- **Measured:** the first start after an install is 3.6 s at the median (3.3–4.2 s).
- **Budget:** 4 s. Two of five runs went over ([quality-gates.md](../architecture/quality-gates.md)).
- **To do:** install the published v0.1 and note how long the first start takes.
- **If it feels slow:** file an issue.

- [ ] First start after installing v0.1 checked

### 3. Clean machines

On 2026-11-06 the owner deferred the clean-machine install test until after v0.1 (ADR-008, release prerequisites).

- [ ] Remove every Noto trace from the development PC:
  - Settings › Apps › Noto › Uninstall;
  - `%LOCALAPPDATA%\Noto.App`;
  - `%LOCALAPPDATA%\velopack`;
  - the Start menu `Noto` shortcut;
  - keep `%LOCALAPPDATA%\Noto` (your notes) unless you mean to start fresh.
- [ ] Install v0.1 there as a new user would: download it, then run Setup.
- [ ] Test on a clean Windows 10 22H2 machine as well (#32).

### 4. Parity score and progress

On 2026-11-06 the owner moved these after the release.

- [ ] Score the parity rows in [sidenotes-parity.md](../product/sidenotes-parity.md) against the installed v0.1.
- [ ] Update [progress.md](../project/progress.md) to v0.1's state.

## Decided

| Decision | Date | Where recorded |
|---|---|---|
| v0.1 ships **unsigned**. SmartScreen warns on the first run; Smart App Control blocks it | 2026-11-06 | ADR-008, CHANGELOG known limitations |
| The clean-machine install test waits until after the release | 2026-11-06 | ADR-008, §3 above |
| The first start after install is judged on the installed release | 2026-11-06 | quality-gates.md, §2 above |
| Memory-footprint work is deferred: idle memory is 152 MB against a 150 MB budget | 2026-11-06 | quality-gates.md |
| **Moved to v0.2:** note colours, font and text size, `#rrggbb` colour chips, automatic backups (#15), crash-loop detection (#10) | 2026-11-06 | [basic-release.md](basic-release.md), *Deferred to v0.2+* |
| The accessibility check, the parity score and the progress update come after the release | 2026-11-06 | *After the release*, above |
