# ADR-002 — Local-first, no account, no backend

**Status:** Accepted
**Date:** 2026-09-14

---

## Context

Noto stores notes, which research and common sense agree are sensitive:
credentials, medical details, salary figures, half-formed opinions about
colleagues. Principle 10 requires designing as if every note contains something
the user would not want exposed.

Competitive research found this dimension is **unserved**: across all products
studied, you can have cloud sync *or* local encryption, but never both.
Notezilla gates sync behind a subscription. Microsoft's new Sticky Notes
couples to OneNote, which generated well-documented user backlash.

---

## Problem

Does Noto require an account, a network connection, or a remote backend for
core functionality — and if not, how strictly is that enforced?

---

## Options considered

| Option | Description |
| ------ | ----------- |
| **A. Local-only, permanently** | No sync, ever. Simplest, most private. |
| **B. Local-first, optional BYO-storage sync later** | Everything works offline. Optional sync through the user's own cloud drive, post-v1. |
| **C. Cloud-first with offline cache** | Account required. Rejected on sight. |
| **D. Local-first with a vendor sync service** | Requires running a backend, an account system, and a privacy policy. |

---

## Decision

**Option B. Noto is local-first, permanently and non-negotiably.**

Core functionality must never require an account, a network connection, a
subscription, or a remote backend. This is not a v1 constraint that relaxes
later — it is a permanent property of the product.

Concretely:

- all data lives in a SQLite database and an attachments folder under the
  user's local application data
- no network calls occur unless the user explicitly enables a feature that
  needs one
- **no telemetry by default.** Any future telemetry is opt-in and documented.
- if Noto is uninstalled, the user's data is still there, in a documented
  location, in a readable format

Any future sync is **optional, additive, and bring-your-own-storage** (a folder
the user already syncs with OneDrive, Dropbox or similar). Noto does not
operate a backend. Sync is post-v1 and out of scope for the MVP.

---

## Rationale

**It is the product position.** Research shows local-first *with* encryption is
the unserved corner of this market, and the Microsoft Sticky Notes backlash
demonstrates the cost of forcing users toward a cloud they did not ask for.

**It removes an entire class of obligation.** No backend means no uptime, no
account recovery, no data-breach exposure, no privacy policy to maintain, no
subscription billing, no GDPR data-subject pipeline. For a project maintained
by very few people over years (principle 9), this is decisive.

**It serves speed.** No network round-trip on the critical path of opening the
sidebar or saving a note. Principle 3's budgets are much easier to hold.

**It is a trust position.** The value proposition of a notes app that watches
what application you have focused (ADR-005) depends entirely on users believing
that observation stays on their machine. Local-first is what makes the context
engine acceptable rather than alarming.

### Why not local-only forever (Option A)

Multi-machine users are real, and a folder-based sync model costs little if the
storage design anticipates it. Committing to "never" would be an unnecessary
constraint on a decision that does not need making yet.

### On encryption

Full at-rest encryption (for example SQLCipher) is **not decided here** and is
not in the MVP. Noto ships **per-note locking** as the first privacy mechanism,
because the realistic threat is a shoulder or a shared screen, not an attacker
who already controls the Windows user account.

SECURITY.md states this boundary explicitly: Noto does not defend against a
compromised OS user.

---

## Consequences

### Positive

- works on a plane, on day one, with no sign-in (principle 4's test)
- no backend to operate, secure or pay for
- no account system, no password resets, no billing
- a clear, honest privacy story
- meaningfully faster on the critical path

### Negative

- no cross-device access in v1 — a real limitation versus Notezilla
- no cross-device access at all without the user arranging storage
- backup is the user's responsibility, so **export and backup must be good**,
  not an afterthought
- no server-side search, no web client, no mobile app — all consistent with
  scope, but all genuinely absent

### Implications for design

- the on-disk format is a **public contract**. Document it. Changing it is a
  breaking change requiring migration.
- export must be first-class and lossless. If Noto is the only thing that can
  read a user's notes, local-first has failed in spirit.
- the database must tolerate sitting in a folder that a cloud client syncs
  underneath it — which means being careful about SQLite WAL files and
  concurrent access. This is a known hazard and must be tested before any sync
  feature ships.

---

## Alternatives rejected

| Option | Why |
| ------ | --- |
| **Cloud-first (C)** | Contradicts principles 2, 4 and 10. Would make Noto a destination rather than a layer. |
| **Vendor sync service (D)** | Requires a backend, an account system and ongoing operational cost. Fails principle 9 decisively. |
| **Local-only forever (A)** | Unnecessarily forecloses a cheap future option. |

---

## Future reconsideration criteria

Revisit if:

- multi-device demand proves strong enough to justify BYO-folder sync — this is
  **anticipated**, and would be a new ADR rather than a reversal
- a threat model emerges that per-note locking does not address, justifying
  full at-rest encryption
- the file-sync-under-SQLite hazard proves unmanageable, forcing a different
  on-disk format for synced deployments

Do **not** revisit to add an account system, telemetry-by-default, or a
vendor-operated backend. Those are foreclosed.
