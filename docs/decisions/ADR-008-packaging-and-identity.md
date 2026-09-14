# ADR-008 — Packaging: sparse package, self-contained, Velopack updates

**Status:** Accepted
**Date:** 2026-09-14
**Related:** ADR-001 (framework), ADR-002 (local-first)

---

## Context

Several Windows features Noto needs require **package identity** — the OS will
not grant them to a plain unpackaged executable:

```
  app notifications (AppNotificationManager)   requires identity
  StartupTask (run at login)                   requires identity
  protocol activation (noto:// URI)            requires identity
```

But full MSIX packaging brings real costs: installation into a sandboxed
location, a harder debugging story, and Store-style distribution friction.

A further constraint emerged in research: **the `ms-appinstaller:` URI protocol
has been disabled by default since December 2023** (it was being abused for
malware distribution). That removes MSIX's one-click web-install story, which
was its main distribution advantage for a product not shipping through the
Store.

---

## Problem

How is Noto packaged, installed and updated, given that it needs package
identity for some features but wants a conventional desktop installation and a
working auto-update path?

---

## Options considered

| Option | Identity | Install | Updates |
| ------ | -------- | ------- | ------- |
| **A. Unpackaged** | No | Normal | Any updater |
| **B. Full MSIX** | Yes | Sandboxed | App Installer (crippled) or Store |
| **C. Sparse package** | Yes | Normal Program Files | Any updater |
| **D. Microsoft Store only** | Yes | Store | Store |

---

## Decision

**Option C: a sparse package (packaged with external location), with
`WindowsAppSDKSelfContained=true`, distributed as a conventional installer and
updated via Velopack.**

```
  +------------------------------------------+
  |  Conventional install in Program Files   |
  |  normal .exe, normal debugging           |
  |                                          |
  |  + sparse package manifest               |
  |    -> grants package identity            |
  |    -> notifications, StartupTask,        |
  |       protocol activation all work       |
  |                                          |
  |  + WindowsAppSDKSelfContained            |
  |    -> no runtime prerequisite            |
  |                                          |
  |  + Velopack                              |
  |    -> delta updates that actually work   |
  +------------------------------------------+
```

---

## Rationale

**Sparse packaging is the only option that gets both halves.** It grants
package identity while keeping a normal installation location, normal file
system access, and a normal debugging experience. Full MSIX would give identity
at the cost of all three; unpackaged would give the developer experience while
silently removing three user-facing features.

**Self-contained Windows App SDK removes a prerequisite.** Without it, users
must have the correct Windows App SDK runtime installed, which is a support
burden and an install-time failure mode for a small utility. The cost is
distribution size, which is an acceptable trade for a one-time download against
a recurring class of "it will not start" reports.

**Velopack rather than App Installer.** With `ms-appinstaller:` disabled by
default, MSIX auto-update outside the Store requires the user to download and
run a package each time — which is not auto-update. Velopack provides delta
updates and a conventional update flow, and it does not conflict with sparse
packaging.

**Not Store-only.** The Store would handle identity, distribution and updates
in one step, and remains attractive as an *additional* channel. But as the sole
channel it requires certification for every release, constrains the release
cadence, and sits awkwardly with a local-first tool that some users will want
to install without a Microsoft account (ADR-002).

---

## Consequences

### Positive

- notifications, run-at-login and `noto://` protocol activation all work
- conventional install location; users can find and inspect the application
- normal debugging, which matters for a project relying on rapid iteration
- no runtime prerequisite
- working delta auto-update

### Negative

- **sparse packaging is less commonly used**, so there is less community
  material when something goes wrong
- a signing certificate is required — sparse packages must be signed. This is a
  real cost and a release-process prerequisite, not a detail.
- two mechanisms to understand (installer + sparse manifest)
- Velopack is an additional dependency in the release path

### Release prerequisites

Before the first public release:

- a code-signing certificate must be obtained and the signing process
  documented
- the installer must be tested on a clean Windows 10 22H2 and Windows 11 24H2
  machine
- the upgrade path must be tested from a previous version, **with an existing
  notes database present** — an update that loses user data is the worst
  possible first impression
- uninstall must be verified to leave user data intact by default, and to ask
  before removing it

---

## Alternatives rejected

| Option | Why |
| ------ | --- |
| **Unpackaged (A)** | Silently loses notifications, StartupTask and protocol activation. The registry `Run` key could substitute for startup, but the other two have no substitute. |
| **Full MSIX (B)** | Sandboxed install, harder debugging, and its distribution advantage evaporated when `ms-appinstaller:` was disabled by default. |
| **Store only (D)** | Certification on every release, constrained cadence, and a poor fit for a local-first tool. Attractive as a **secondary** channel later. |

---

## Future reconsideration criteria

Revisit if:

- sparse package support is deprecated or proves unreliable in practice
- `ms-appinstaller:` is re-enabled with adequate safeguards, restoring MSIX web
  install
- the Store becomes a meaningful distribution channel for Noto — as an
  **addition**, not a replacement
- the signing certificate cost or process proves prohibitive, which would force
  a reassessment of the whole packaging approach

The signing requirement is the most likely thing to force a rethink, and it
should be confirmed early rather than discovered at release time.
