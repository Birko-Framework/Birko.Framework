---
id: TASK-303
parent: EPIC-014
feature: FEATURE-014
status: done
priority: P2
assignee: ai
created: 2026-09-08
depends-on: []
blocks: []
related: [TASK-252, TASK-254, TASK-247]
findings: []
pr: "Birko.Data.SQL 04431ec + SqLite 58e8235 + PostgreSQL 6eef58a + MySQL 4e018b0 + MSSql 6583fe4"
github-issue: null
jira-key: null
affects: [Birko.Data.SQL, Birko.Data.SQL.TimescaleDB]
---

# A composite `PRIMARY KEY (a, b)` cannot be declared at all, and TimescaleDB needs one

Split out of [[TASK-252]] on pick, as that task's own criterion 2 required: it was filed there as one of
six latent per-provider gaps, and it is the only one that acquired a measurement, a consumer-visible
consequence and a dependent task ([[TASK-254]]). The other five are resolved or declined in TASK-252.

## The defect

`AbstractConnector.CreateTable` emits `PRIMARY KEY` **per column**, from each field's flag. Two
`HasPrimary` calls therefore emit two clauses — measured on PostgreSQL 16 as
`42P16 multiple primary keys for table "T" are not allowed`. So a composite key is not merely unsupported
through the schema builder: it **cannot be expressed at all** through `ModelMap` or the attributes.

The raw-SQL fallback that could emit a composite clause was deleted with the rest of the fallbacks at
TASK-247, which recorded the loss deliberately.

## Why it bites — three facts compose

1. A composite key cannot be declared (above).
2. Bulk update and delete key on `Table.GetPrimaryFields()` and **do nothing without a primary key**, so
   "declare no key" is not a workaround for a store that needs those verbs.
3. TimescaleDB **refuses a unique index that omits the partitioning column** (`TS103`).

Together these force the time column to carry the primary key on any Birko hypertable, and make
`(Guid, Ts)` — the natural shape for telemetry, and the first thing a consumer reaches for —
inexpressible. That is the root cause underneath [[TASK-254]]: a Guid-keyed entity cannot be a hypertable,
and since TASK-472's identifier fix it throws `TS103` out of lazy schema-ensure rather than silently
degrading to a plain table.

## What is NOT claimed

No consumer is blocked today — Symbio has `TimeSeriesRecord` and no subclass of it — and the per-column
rendering is correct for every non-hypertable entity. The gap is that the framework cannot express a shape
one of **its own providers** requires.

## The decision to make

TASK-252 framed it precisely, and it should be answered rather than assumed:

- **A table-level `PRIMARY KEY (a, b)` clause in `CreateTable`**, rendered when more than one field carries
  the flag — the capability fix; or
- **an explicit refusal of a second `HasPrimary`** at the mapper — today's `42P16` is the *DDL layer*
  refusing, which is a wrong-answer-late rather than a clear one.

The first is the one the TimescaleDB case needs. The second is cheap and merely makes the existing failure
legible. They are not exclusive: a mapper-level refusal is the right behaviour on the day the renderer
still cannot do it.

## Acceptance criteria

- [x] **Measure first**: how many declarations across the framework, its tests and all 16 consumer repos
      carry more than one `HasPrimary` / `[PrimaryField]` today, and what each does. TASK-248's veto is the
      precedent — an honest-looking fix there was measured and rejected because it would have broken seven
      working entities.
- [x] The chosen shape is implemented on **all four SQL providers**, with the emitted DDL asserted per
      provider (composite `PRIMARY KEY` syntax is standard, but the per-provider suites are where this
      framework catches the exceptions).
- [x] A **live** test on TimescaleDB proving a `(Guid, Ts)` hypertable can now be created and that
      `TS103` no longer arises for it — that is the case this exists for, and an offline assertion cannot
      show it.
- [x] Bulk update/delete are shown to work against a composite key, since `GetPrimaryFields()` is what
      makes the key load-bearing rather than decorative.
- [x] [[TASK-254]]'s degrade path is re-checked: with a composite key expressible, does the hypertable
      conversion still need to degrade, and does its recorded-failure channel still fire for the cases
      that remain?
- [x] Proven able to fail.

## Out of scope

- The other five gaps from TASK-252 — resolved or declined there.
- Whether a hypertable should be declarable per entity rather than per connector setting — TASK-254
  records that nothing declares it, and changing that is its own feature.

## Human test plan

- [x] N/A — the proof is a live `(Guid, Ts)` hypertable and a bulk update keyed on both columns.

---

## Worked 2026-09-08

### Step 0 — the veto measurement said go, and the premise checked out on five servers

Criterion 1's precedent is TASK-248, where the same instinct was measured and **rejected**. Here it says
proceed: **0** classes across the framework, its tests and all 16 consumer repos declare more than one
primary. So the change is purely additive — no emitted DDL changes for any entity that exists today.

| probe | result |
|---|---|
| Guid-only PK + `create_hypertable` | `cannot create a unique index without the column "ts" (used in partitioning)`, hint: *"ensure the partitioning column is part of the primary or composite key"* |
| `PRIMARY KEY (Guid, Ts)` + `create_hypertable` | **hypertable created** |
| composite PK syntax on PostgreSQL / MySQL / SQL Server / SQLite | accepted on all four |
| two inline `PRIMARY KEY`s (today's emission) | PostgreSQL `42P16`; **SQLite `Error 1: table has more than one primary key`** |
| SQLite `AUTOINCREMENT` + table-level clause | **rejected** — the form is single-column by construction |

Two of those correct this file as written: it cited only PostgreSQL's `42P16`, and **SQLite fails too**;
and the SQLite autoincrement constraint was not anticipated at all.

### The decision, answered rather than assumed

This file offered two shapes. **The table-level clause** was implemented — the capability the TimescaleDB
case needs. The mapper-level refusal was *not* added, because with the renderer able to express a
composite key there is nothing left to refuse; the one genuinely inexpressible combination (SQLite
autoincrement) is refused at the point it is detected, with a message naming autoincrement, which the
server's own error does not.

`AbstractField.UsesInlinePrimaryConstraint` mirrors `UsesInlineUniqueConstraint` (TASK-275) — one
producer, all four providers consult it. It is computed from the **table** where its sibling is computed
from the field, because uniqueness is a property of a column alone and being one of several key columns is
not; `AbstractField` already carries its `Table`, so it needs no wiring at `LoadTable`.

### ⚠ A mutation exposed a hole in my own tests, and that is the part worth carrying

With the suppression tests alone, **deleting the table-level clause left `Birko.Data.SQL.Tests` entirely
green (683 passed)** — only the live TimescaleDB tests caught it. Suppression without emission is *worse
than the original defect*: the table is created with **no key at all**, silently, and bulk update and
delete key on `GetPrimaryFields()` and would quietly do nothing. Tests that assert a flag are not tests
that assert a statement.

Mutations, all disjoint, after the gap was closed:

| mutation | reds |
|---|---|
| flag ignores the table (old behaviour) | 1 offline + 2 live |
| drop the table-level clause | **1 offline** (was 0) + 2 live |
| drop the autoincrement refusal | 1 offline |

### Verified

`BIRKO_REQUIRE_LIVE` set against live TimescaleDB 2.29.2, PostgreSQL 16, MySQL 8.4, SQL Server 2022 and
on-disk SQLite: **1,635 tests, 0 failed** across nine suites (SQL 686, SQLite 344, PostgreSQL 107,
MySQL 119, MSSql 132, Migrations.SQL 87, Migrations.TimescaleDB 88, TimescaleDB 56, Health.Data.SQL 16).
10 new.

### ⚠ Left open deliberately

- **Bulk update/delete is now demonstrated** — `CompositeKeyRoundTripTests` on SQLite. The fixture is two
  rows that **share the first key column**, because one with distinct Guids would pass even if only the
  first column were used, which is precisely the failure a composite key prevents. Update rewrites one row
  and leaves its sibling; delete removes one; a duplicate of the whole key is refused.
- **⚠ Writing that test found a limitation this fix does not cover, and it is filed as [[TASK-304]].**
  `AbstractDatabaseModel` puts `[UniqueField]` *and* `[PrimaryField]` on `Guid`, so a subclass carries a
  standalone `UNIQUE (Guid)` that forbids two rows sharing a Guid whatever the primary key says. Measured:
  every second insert failed with `SQLite Error 19: 'UNIQUE constraint failed: Readings.Guid'`. So the
  capability is real and **the framework's own base model still cannot use it** — the shape this task
  exists for is reachable only by deriving from `Birko.Data.Models.AbstractModel` and declaring the key
  yourself. That qualification belongs on this task rather than being left implied by a passing suite.
- **The sweep flake was caught on the next attempt, with a trx logger, and diagnosed.** It is
  `QualifiedNameEmitterLiveTests.Two_schemas_holding_the_same_table_name_get_their_own_answers` failing
  with `42P01: relation "_timescaledb_internal._materialized_hypertable_1048" does not exist` — a
  continuous aggregate's internal table, dropped by a parallel sibling class while this test's catalogue
  read was resolving it. Fixed by putting the five live classes that share the database into one xUnit
  collection (not by disabling parallelism, which TASK-276 forbids). 6 consecutive clean runs after.
  Two of the participating classes are ones I added yesterday and today, so this was plausibly my own
  regression.
