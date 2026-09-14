<div align="center">

# Noto

**Notes that live where you work.**

A Windows-native contextual notes application. Local-first, keyboard-first,
and quiet.

[![CI](https://github.com/mdsaif45/Noto/actions/workflows/ci.yml/badge.svg)](https://github.com/mdsaif45/Noto/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Status](https://img.shields.io/badge/status-pre--alpha-orange.svg)](docs/roadmap/roadmap.md)

</div>

---

> **Status: pre-alpha. There is nothing to install yet.**
>
> Noto is in its research and architecture phase. The documentation is real;
> the application is not. Follow the [roadmap](docs/roadmap/roadmap.md).

---

## What it is

The information you need while working is almost never in the application
where you need it. So it ends up in a scratch file, a browser tab, or a sticky
note — and then it has to be found again.

Noto keeps notes next to the work they belong to.

```
  what you are doing        ->   what Noto shows you
  ------------------             -------------------
  VS Code, repo "api"            API notes, TODO for this branch
  Chrome, a Jira ticket          that ticket's working notes
  Terminal                       the commands you always forget
```

Four ways a note can be present:

| Mode | What it is |
| ---- | ---------- |
| **Workspace** | An edge drawer you pull out to browse and find |
| **Floating** | A note as a desktop object, pinned above your work |
| **Contextual** | Notes that appear with the application they belong to |
| **Capture** | A hotkey from anywhere, straight into a note |

---

## Why it exists

Existing tools sit at one of two extremes:

```
        sticky notes                          Notion / Evernote / OneNote
        no structure, no search               not present while you work
                    |                                      |
                    +------------------+-------------------+
                                       |
                                 Noto is here
                        structured enough to find things,
                        present enough that you don't have to
```

On Windows specifically, research found the gap is real: the best contextual
notes apps ([SideNotes](https://www.apptorium.com/sidenotes/),
[Noticky](https://www.noticky.app/)) are macOS-only, and **no Windows product
in the category supports markdown or an edge-docked sidebar.**

Full analysis: [docs/research/competitive-analysis.md](docs/research/competitive-analysis.md)

---

## Principles

- **Local-first.** No account, no cloud, no telemetry. Works on a plane.
- **Fast.** Under a second to usable. Effectively zero idle CPU.
- **Quiet.** Invisible until you ask for it.
- **Keyboard-first.** Every action has a keyboard path. Every trigger can be
  turned off.
- **Native.** Real Windows APIs, real Windows behavior.
- **Small on purpose.** Noto is not becoming Notion.

The full set, with the trade-off each one commits us to:
[docs/product/principles.md](docs/product/principles.md)

---

## Built with

```
  C# / .NET  +  WinUI 3  +  Windows App SDK  +  Win32 interop
  SQLite (Microsoft.Data.Sqlite + Dapper), FTS5 for search
  Markdown as the note format
```

Two choices worth explaining, because both depart from the obvious answer:

- **Not EF Core** — it cannot model FTS5, and full-text search is a core
  feature, not an add-on. ([ADR-003](docs/decisions/ADR-003-sqlite-data-access.md))
- **WinUI 3 is provisional** — it is weak at translucency and click-through,
  so the decision is gated on three validation spikes with WPF as the
  fallback. ([ADR-001](docs/decisions/ADR-001-native-windows-stack.md))

---

## Documentation

| | |
| --- | --- |
| [Vision](docs/product/vision.md) | What Noto is, and what it is not |
| [Principles](docs/product/principles.md) | The rules that decide what gets built |
| [MVP](docs/product/mvp.md) | What ships first, and what does not |
| [Roadmap](docs/roadmap/roadmap.md) | Milestones as capabilities |
| [Architecture](docs/architecture/architecture-overview.md) | How it is put together |
| [Data model](docs/architecture/data-model.md) | Schema and storage layout |
| [Decisions (ADRs)](docs/decisions/) | Why it is shaped this way |
| [Research](docs/research/) | Competitive and technical findings |

---

## Development

**Prerequisites:** Windows 11 (10 20H1+ should work), Visual Studio 2022 with
the .NET Desktop Development and Windows App SDK workloads.

```bash
git clone https://github.com/mdsaif45/Noto.git
cd Noto
dotnet build
```

```bash
dotnet test
```

Noto is developed with heavy AI assistance, which makes architectural drift the
main long-term risk. The defense is
[ADRs](docs/decisions/): significant decisions are recorded before they are
implemented, and a change that contradicts one requires the record to be
reviewed first.

If you are contributing — including via an AI assistant — read
[CONTRIBUTING.md](CONTRIBUTING.md) first.

---

## Contributing

Issues and pull requests are welcome. The bar is about **fit** as much as code
quality: a good feature that does not belong in Noto is still a no. Read the
[principles](docs/product/principles.md) before proposing anything substantial.

- 🐛 [Report a bug](https://github.com/mdsaif45/Noto/issues/new?template=bug.yml)
- ✨ [Request a feature](https://github.com/mdsaif45/Noto/issues/new?template=feature.yml)
- 🔒 [Report a vulnerability](https://github.com/mdsaif45/Noto/security/policy) — privately, never as a public issue

---

## License

[MIT](LICENSE)

---

<div align="center">
<sub><i>Noto is a working name and may change before v1.0.</i></sub>
</div>
