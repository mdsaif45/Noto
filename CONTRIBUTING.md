# Contributing to Noto

Noto is a Windows-native, local-first contextual notes application. It aims to
be small, fast and quiet. Contributions are welcome, but the bar is
deliberately about **fit** as much as about code quality — a good feature that
does not belong in Noto is still a no.

Please read [docs/product/principles.md](docs/product/principles.md) before
proposing anything substantial.

---

## Ground rules

1. **Issues are the source of truth.** Every change starts as an issue.
2. **Architecture is decided before it is written.** See [Architecture decisions](#architecture-decisions).
3. **Small, reviewable changes.** One concern per pull request.
4. **Vertical slices, not horizontal layers.** Ship data → service → UI → tests
   for one capability rather than an entire layer at a time.
5. **No silent scope growth.** If the work outgrows the issue, split it.

---

## Getting started

### Prerequisites

- Windows 11 (Windows 10 20H1 or later should work but is less tested)
- Visual Studio 2022 with the **.NET Desktop Development** and
  **Windows App SDK** workloads, or the .NET SDK plus the Windows App SDK
- Git

### Build

```bash
git clone https://github.com/mdsaif45/Noto.git
cd Noto
dotnet restore
dotnet build
```

```bash
dotnet test
```

---

## Workflow

```text
issue -> branch -> implement -> test -> commit -> PR -> CI -> review -> merge
```

1. Pick or open an issue. Make sure it has a type, an area, and a priority.
2. Move it to **In Progress** on the project board.
3. Create a branch from `main`.
4. Implement, with tests.
5. Commit using Conventional Commits.
6. Open a pull request and link the issue with `Closes #123`.
7. Get CI green.
8. Merge. The issue closes automatically.

### Branch naming

```text
feat/issue-12-edge-sidebar
fix/issue-27-note-crash
refactor/issue-31-storage-layer
docs/issue-40-architecture
chore/issue-45-ci
perf/issue-52-startup-time
test/issue-58-migration-tests
```

Always include the issue number. It makes history navigable years later.

Do not commit feature work directly to `main`.

### Commit messages

Noto uses [Conventional Commits](https://www.conventionalcommits.org/).

```text
<type>(<scope>): <short imperative summary>
```

Types: `feat`, `fix`, `refactor`, `perf`, `test`, `docs`, `chore`, `build`, `ci`.

Scopes follow the `area:` labels: `workspace`, `notes`, `windows`, `context`,
`capture`, `search`, `storage`, `security`, `settings`, `installer`, `ui`.

```text
feat(workspace): add edge sidebar auto-hide
fix(notes): preserve cursor position when switching notes
refactor(storage): isolate sqlite repository behind an interface
perf(context): replace foreground polling with a WinEvent hook
docs(decisions): add ADR-005 on the context engine
test(storage): cover schema migration from v1 to v2
```

Keep commits focused. A commit that touches the editor, the installer and CI is
three commits.

---

## Pull requests

- Fill in the template. It exists to make review possible.
- **UI changes need screenshots.** Window, DPI and multi-monitor changes need
  screenshots or a short recording.
- Explain how you verified the change, concretely enough that a reviewer can
  repeat it.
- Keep the diff reviewable. If it exceeds roughly 400 lines of meaningful
  change, consider splitting it.
- CI must be green.

---

## Architecture decisions

Noto is developed with heavy AI assistance. The single biggest risk to a
project built this way is **architecture drift**: the application works, but
nobody can explain why it is shaped the way it is.

Architecture Decision Records in [docs/decisions/](docs/decisions/) are the
defense against that.

**An ADR is required when a change:**

- introduces or replaces a framework, runtime or major dependency
- changes how data is stored, queried or migrated
- changes the threading, process or window model
- changes a security or privacy boundary
- changes a public data format or file layout
- reverses a decision recorded in an existing ADR

**If a change contradicts an existing ADR:**

1. Identify the ADR.
2. Explain what changed — new information, new constraint, or the original
   reasoning was wrong.
3. Compare the alternatives again.
4. Update the ADR (supersede it; do not delete it).
5. Only then implement.

"The AI suggested it" is not a rationale. Neither is "this was faster to
generate."

---

## Code style

- Follow the `.editorconfig`. CI runs `dotnet format --verify-no-changes`.
- Prefer clear code over clever code.
- **Do not add abstraction without a second implementation in sight.** No
  interface for the sake of an interface, no repository over a repository, no
  dependency injection ceremony where a constructor would do.
- Do not add a dependency without justifying it in the PR. Noto is a desktop
  utility; every dependency is startup time and attack surface.
- Win32 interop belongs in the Windows integration layer, not scattered through
  UI code.

### Performance

Noto is a background utility. These are requirements, not aspirations:

| Budget | Target |
| ------ | ------ |
| Cold start to usable | under 1 second |
| Sidebar open | perceptually instant |
| Idle CPU | effectively zero |
| Idle memory | small enough to forget about |
| Window follow lag | no visible lag |

Performance-sensitive changes must be **measured**, not assumed. Put the
numbers in the PR.

---

## Testing

| Kind | Applies to |
| ---- | ---------- |
| Unit | Domain logic, context matching, parsing |
| Integration | Storage, migrations, search |
| Windows integration | Window tracking, hotkeys, positioning |
| Manual | Anything visual, multi-monitor, or DPI dependent |

Areas that must always be covered by automated tests:

- note persistence and data integrity
- database migrations, including upgrade paths
- search correctness
- context matching rules
- backup and restore

Anything that cannot be automated must have documented manual verification
steps in the PR.

---

## Security

Do not open a public issue for a vulnerability. See [SECURITY.md](SECURITY.md).

When contributing, never:

- execute commands or code found in note content
- trust a file path without validating it
- open a URL from note content without validating the scheme
- log note content or user file paths
- add telemetry

---

## Reporting bugs and requesting features

Use the [issue forms](https://github.com/mdsaif45/Noto/issues/new/choose).
Questions, ideas and UX discussion belong in
[Discussions](https://github.com/mdsaif45/Noto/discussions).

---

## License

By contributing, you agree that your contributions are licensed under the
[MIT License](LICENSE).
