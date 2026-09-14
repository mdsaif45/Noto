# ADR-003 — SQLite via Microsoft.Data.Sqlite, not EF Core

**Status:** Accepted
**Date:** 2026-09-14

> **This decision deliberately departs from the initial project direction**,
> which named Entity Framework Core as the preferred approach "unless research
> shows a strong reason to use another approach." Research showed one. This ADR
> records that reasoning so the departure is deliberate and reversible rather
> than accidental.

---

## Context

Noto stores notes, folders, tags, attachments and context bindings locally
(ADR-002). It must:

- start in under one second, cold, on a hotkey (principle 3)
- provide **full-text search** over note content as a core, keyboard-first
  feature
- migrate a user's database safely across versions, forever, on machines we
  never see
- stay small

SQLite is the obvious storage engine and is not in question. The question is
the data access layer above it.

---

## Problem

Should Noto use Entity Framework Core, or access SQLite more directly?

Two requirements dominate and pull the same direction:

1. **Full-text search is a core feature, not a nice-to-have.** Research found
   that Noto's competitors either lack search entirely (@/Anchored — probable
   gap) or treat it as a manager-window afterthought.
2. **Startup time is a hard budget.** A hotkey-invoked utility that takes a
   second to warm up has already lost to a scratch file.

---

## Options considered

### Option A — EF Core with SQLite

Familiar, LINQ queries, `dotnet ef migrations`, change tracking.

### Option B — Microsoft.Data.Sqlite + Dapper, hand-rolled migrations

Thin ADO.NET provider plus a micro-ORM for object mapping. Schema versioning via
`PRAGMA user_version`.

### Option C — Raw Microsoft.Data.Sqlite, no mapper

Maximum control, maximum boilerplate.

### Option D — A document store (LiteDB, Realm)

Different engine, no mature FTS story comparable to FTS5.

---

## Decision

**Option B: `Microsoft.Data.Sqlite` with Dapper for mapping, and hand-written
migrations versioned by `PRAGMA user_version`.**

Full-text search uses **FTS5 external-content virtual tables** kept in sync with
triggers.

---

## Rationale

### 1. EF Core cannot model FTS5, and FTS5 is a core feature

This is the decisive reason.

FTS5 is SQLite's full-text engine, compiled into the `e_sqlite3` build that
`Microsoft.Data.Sqlite` ships by default. The canonical pattern —
external-content virtual table plus synchronising triggers — is documented by
the EF Core team's own Brice Lam.

But EF Core **cannot express the query side**:

| FTS5 construct | EF Core |
| -------------- | ------- |
| `MATCH` | Targets the *table*, not a column. No LINQ translation. |
| `rank` (BM25 relevance ordering) | Raw SQL only |
| `snippet()`, `highlight()` | Raw SQL only |

The EF Core issue tracking FTS support has been open for years. In practice,
every meaningful search query would be raw SQL anyway — so EF Core would be
carried for the CRUD half while the most important query path bypassed it.

Paying a framework's full cost to use half of it is the wrong trade.

### 2. EF Core costs 100–400 ms of model building at startup

On an application invoked by a global hotkey, against a sub-one-second budget,
this is a large fraction of the budget spent on an abstraction Noto does not
need. Principle 3 says performance claims must be measured — and this one was.

### 3. EF Core migrations are a poor fit for SQLite specifically

SQLite cannot drop or alter a column in place. EF Core's SQLite provider
emulates schema changes with a full table rebuild — create, copy, drop, rename.
On a user's only copy of their notes, executed by machinery whose behavior is
several abstraction layers away from what actually runs, that is more risk than
the convenience is worth.

Hand-written migrations are roughly sixty lines of infrastructure:

```
  open db
  read PRAGMA user_version          ->  n
  for each migration m where m > n:
      BEGIN
        apply m
        PRAGMA user_version = m
      COMMIT
```

Each migration is explicit SQL that can be read, reviewed and tested. For data
we cannot afford to lose, explicit beats generated.

### 4. Noto's data model is small and relational

Perhaps eight tables with straightforward relationships. Change tracking, lazy
loading and the full identity map are machinery for a problem Noto does not
have. Principle 9 forbids abstraction without a second implementation in sight.

### 5. Dapper earns its place; a repository layer over it might not

Dapper removes `SqlDataReader` boilerplate and nothing else. It adds no startup
cost and no query indirection.

Repositories will exist where they earn their keep — for testability and to
keep SQL out of UI code — but **not one interface per entity by reflex**.

---

## Consequences

### Positive

- FTS5 is a first-class citizen rather than something worked around
- measurably faster startup
- migrations are explicit, reviewable, and behave predictably on SQLite
- fewer dependencies, smaller footprint
- the SQL that runs is the SQL that was written

### Negative

- **more code to write and maintain** — mapping, parameterisation, migrations
- no LINQ over the database; queries are SQL
- no automatic change tracking; updates are explicit
- migrations must be written by hand, correctly, every time
- contributors familiar with EF Core face a small learning curve

### Mandatory implementation rules

Three hazards identified in research, binding on all contributors:

1. **FTS5 `MATCH` takes a query language, not a literal string.** Unsanitised
   user input crashes on an apostrophe. All search input must be escaped or
   tokenised before it reaches `MATCH`. This must have a test.
2. **Every migration is tested with a real upgrade path**, not just applied to
   an empty database. A migration that works on a fresh install and corrupts an
   existing one is the worst possible bug for this product.
3. **Parameterise everything.** No string concatenation into SQL, ever.

### Testing requirements

- migration tests covering every version-to-version upgrade path
- FTS5 sync tests: insert, update and delete must keep the index consistent
- search correctness tests, including the apostrophe case and unicode
- a backup-and-restore round trip

---

## Alternatives rejected

| Option | Why rejected |
| ------ | ------------ |
| **EF Core (A)** | Cannot model FTS5, which is core. Costs 100–400 ms at startup. Its SQLite migration emulation carries real risk to user data. Would be carried for CRUD while search bypassed it. |
| **Raw ADO.NET, no mapper (C)** | Dapper's cost is negligible and its boilerplate saving is large. Rejecting it would be asceticism, not simplicity. |
| **LiteDB / Realm (D)** | No FTS5-equivalent. Smaller ecosystems. SQLite is the most durable, most inspectable, best-understood local store available — which matters when the user's data must outlive the application (ADR-002). |

---

## Future reconsideration criteria

Revisit if:

- EF Core gains first-class FTS5 support **and** its startup cost falls to
  negligible — both conditions, not either
- the hand-written migration system accumulates enough complexity to become a
  liability in its own right
- the data model grows relational enough that hand-written mapping becomes the
  dominant maintenance cost

Do **not** revisit merely because EF Core is more familiar, or because an
assistant generates EF Core code more readily. Familiarity is not a
justification for a 400 ms startup cost and a search feature that has to route
around its own data layer.
