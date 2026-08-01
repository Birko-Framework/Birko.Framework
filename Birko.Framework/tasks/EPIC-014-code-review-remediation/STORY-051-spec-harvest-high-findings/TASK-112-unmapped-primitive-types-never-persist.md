---
id: TASK-112
parent: STORY-051
feature: FEATURE-014
status: todo
priority: P0
assignee: ai
created: 2026-07-30
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
findings: [SH-H037]
---

# `long` / `double` / `float` / `short` / `byte[]` map to no column and never persist

## Context

`../Birko.Data.SQL/SQL/Fields/AbstractField.cs:235` — **CONFIRMED**.

`CreateAbstractField`'s type dispatch handles only `bool`, `DateTime`, `decimal`, `Guid`, `int`, `char`,
`string` (plus nullable variants) and `enum → int`. Every other CLR type falls to `return null`, commented
*"Unsupported type — skip, filtered by LoadField"*. `LoadField` (`DataBase_Field.cs:110`) turns that null
into `Array.Empty`.

So a `[Table]` model with `public long Ticks`, `public double Ratio` or `public byte[] Blob` gets a
`CREATE TABLE` **without those columns**. `Write()` never emits them and `Read()` never restores them —
**silent write-side data loss with no exception and no log entry.**

Two things make this worse than a missing feature:

- **`decimal` *is* mapped, so money is safe** — which is precisely why this has survived. The types that
  vanish are identifiers (`long`), measurements (`double`/`float`) and blobs (`byte[]`): exactly the
  properties whose loss you notice late.
- **The portable `FieldType` enum already names `Long`, `Double` and `Binary`.** Callers reading that enum
  reasonably expect support; the framework advertises the capability it drops.

## Approach

Add the missing type arms to `CreateAbstractField` and their provider column types. This spans all four
providers, so the per-provider type mapping is the bulk of the work — `long` → `BIGINT`/`INTEGER`,
`double`/`float` → `DOUBLE PRECISION`/`REAL`/`FLOAT`, `byte[]` → `BLOB`/`BYTEA`/`VARBINARY(MAX)` — with
parameter binding and reader materialisation for each.

**Decide explicitly what happens to a type that is still unsupported after this.** The current silent
`return null` is the actual defect: it means any future unmapped type repeats this bug. A model carrying a
property the mapper cannot express should **fail at table load**, the same way the
`[CompositeIndex]` work chose to fail fast on an unmapped property name. If a genuine opt-out is needed,
that is what an explicit `[NotMapped]`-style attribute is for — silence is not a design.

Check what `Birko.Models.SQL`'s `ModelMap<T>` does with these types too; if it can already map them, the two
paths disagree and that divergence should be closed in the same pass.

## Acceptance criteria

- [ ] `long`, `long?`, `double`, `double?`, `float`, `float?`, `short`, `short?` and `byte[]` map to columns
      and round-trip through `Write()` / `Read()` end-to-end
- [ ] Boundary values round-trip exactly: `long.MinValue` / `MaxValue`, `double` precision at the extremes,
      an empty `byte[]`, and a null `byte[]`
- [ ] `CREATE TABLE` emits the correct column type on **all four** providers (SQLite, PostgreSQL, MySQL,
      MSSQL) — DDL asserted per provider
- [ ] A CLR type still unsupported after this change **throws at table load**, naming the property and its
      type, instead of silently producing no column
- [ ] `decimal`, `int`, `Guid`, `DateTime`, `bool`, `string` and enum mappings are unchanged — asserted, so
      the type-dispatch rewrite cannot regress what worked
- [ ] `FieldType.Long` / `.Double` / `.Binary` now correspond to something real
- [ ] Regression tests in `Birko.Data.SQL.Tests` (DDL per provider) and `Birko.Data.SQL.SqLite.Tests`
      (round-trip, boundary values)
- [ ] `/specs regen` for `schema-index-and-ddl`, spec diff reviewed

## Out of scope

- Migrating existing consumer tables to add the newly-mapped columns. A consumer whose model has a `long`
  today has a table with no such column; adding the mapping means their DDL and their live schema diverge.
  **This needs to be called out in the fix's `Recent Updates` entry** — it is the one part of this task that
  can break a running deployment, and it is a consumer decision, not a framework one.
- `SH-H038` (ES reindex reporting success with per-document failures) — same area, unverified, separate.
- Non-primitive complex types, collections, and `TimeSpan`/`DateTimeOffset` unless they fall out for free.

## Human test plan

N/A — covered by automated tests. Round-trip and DDL assertions cover it per provider; there is no visual
surface.
