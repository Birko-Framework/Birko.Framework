---
id: TASK-303
parent: EPIC-014
feature: FEATURE-014
status: todo
priority: P2
assignee: ai
created: 2026-09-08
depends-on: []
blocks: []
related: [TASK-252, TASK-254, TASK-247]
findings: []
pr: null
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

- [ ] **Measure first**: how many declarations across the framework, its tests and all 16 consumer repos
      carry more than one `HasPrimary` / `[PrimaryField]` today, and what each does. TASK-248's veto is the
      precedent — an honest-looking fix there was measured and rejected because it would have broken seven
      working entities.
- [ ] The chosen shape is implemented on **all four SQL providers**, with the emitted DDL asserted per
      provider (composite `PRIMARY KEY` syntax is standard, but the per-provider suites are where this
      framework catches the exceptions).
- [ ] A **live** test on TimescaleDB proving a `(Guid, Ts)` hypertable can now be created and that
      `TS103` no longer arises for it — that is the case this exists for, and an offline assertion cannot
      show it.
- [ ] Bulk update/delete are shown to work against a composite key, since `GetPrimaryFields()` is what
      makes the key load-bearing rather than decorative.
- [ ] [[TASK-254]]'s degrade path is re-checked: with a composite key expressible, does the hypertable
      conversion still need to degrade, and does its recorded-failure channel still fire for the cases
      that remain?
- [ ] Proven able to fail.

## Out of scope

- The other five gaps from TASK-252 — resolved or declined there.
- Whether a hypertable should be declarable per entity rather than per connector setting — TASK-254
  records that nothing declares it, and changing that is its own feature.

## Human test plan

- [ ] N/A — the proof is a live `(Guid, Ts)` hypertable and a bulk update keyed on both columns.
