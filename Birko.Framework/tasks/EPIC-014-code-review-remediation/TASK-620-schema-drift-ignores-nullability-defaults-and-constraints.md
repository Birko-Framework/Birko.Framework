---
id: TASK-620
parent: EPIC-014
feature: null
status: done
priority: P2
assignee: ai
created: 2026-10-15
depends-on: []
blocks: []
related: [TASK-269, TASK-610, TASK-611]
findings: []
pr: null
github-issue: null
jira-key: null
---

# Schema drift compares column types only: nullability, defaults and key constraints that disagree read clean

## Context

Found 2026-10-15 reviewing what `DetectDrift` covers, after TASK-610/611 (asked: "all schema detection errors are
handled too?"). Read in source, **not measured**.

`AbstractConnector.DetectDrift` (`AbstractConnector_SchemaDrift.cs`) reads each stored column as a `StoredColumn` with
only `Name`, `TypeName`, `Size`, `Precision` and `Scale`, and compares `ConvertType(field)` against the rendered stored
type. So none of these is reported:

- **Nullability.** A column the model declares `NOT NULL` (`[RequiredField]`, a non-nullable value type) stored as
  `NULL`, or the reverse. The first lets rows in that the model says cannot exist. The second makes every INSERT that
  omits the value fail on a table the health check calls clean.
- **Defaults** the DDL emits (`AddColumnDefinition`'s back-fill default, a declared default) and the stored column
  lacks.
- **Primary key / unique.** A `[UniqueField]` or key column whose constraint is absent (TASK-567 is the "no declared
  key" sibling).

`SchemaDriftHealthCheck` then reports Healthy over all of them.

## Acceptance criteria

- [x] Measured first, per provider (PostgreSQL, MSSQL, MySQL, SQLite): a table whose column differs from the model
      only in nullability reports clean today. Record it.
      → Clean (an empty drift list) on all four, in both directions (`int` stored NULL, `int?` stored NOT NULL).
- [x] Nullability drift is reported, on every provider, with a kind of its own or as `TypeMismatch` carrying both
      sides. Decide and record which. The declared side comes from the method that emits the DDL (rule 63), never
      restated.
      → A kind of its own, `NullabilityMismatch`: the type is the same, and a consumer filtering by kind must be able to
      tell them apart. The declared side is `IsNotNull`, the flag every `FieldDefinition` emits `NOT NULL` from, with an
      offline test pinning that agreement. **Key columns are skipped**: the framework always writes a key, providers
      disagree on what a key implies (SQLite reports a framework-created key as nullable), and whether a key is
      declared at all is TASK-567's question. The key is `DataBase.GetPrimaryFields`, including its `Guid` fallback.
- [x] Defaults and unique / key constraints are each either reported, with a live test, or recorded here as
      deliberately not compared, with the reason.
      → **Not compared, deliberately.** *Defaults*: the framework writes every column on every INSERT, so a missing
      default never changes what a framework write stores; it matters only to other writers. *Unique / key*: a missing
      declared key is TASK-567's, and a missing unique index or constraint belongs with index drift (TASK-622), where
      the catalogue reader for indexes already lives.
- [x] `EnsureColumns` stays additive: none of these is acted on.

## Out of scope

- Index drift (TASK-622). Tables outside the default schema (TASK-272).

## Human test plan

N/A — covered by the live provider tests.

## Implementation plan

1. **Measure** (per provider, live): a table created by the framework, then `ALTER` a declared `NOT NULL` column to
   nullable (and the reverse), and run `DetectDrift`. Expected: clean. Record it.
2. **Read the stored side** in the existing catalogue queries rather than adding new ones: PostgreSQL `a.attnotnull`,
   MSSQL `c.is_nullable`, MySQL `IS_NULLABLE`, SQLite `pragma_table_info.notnull`. `StoredColumn` gains `IsNullable`
   (null where a provider cannot say, which then reports nothing rather than guessing).
3. **Declared side, one producer** (rule 63): extract `DeclaresNotNull(field)` (`IsNotNull || IsPrimary`) as the
   predicate every `FieldDefinition` already applies, and use it in both the DDL and the drift comparison, so they
   cannot disagree.
4. **Report** as a new `ColumnDriftKind.NullabilityMismatch` with both sides. Not `TypeMismatch`: the type is the
   same, and a consumer filtering on kind must be able to tell them apart. `EnsureColumns` ignores it (additive only).
5. **Defaults and unique/key**: measured but **recorded as not compared**, unless the measurement shows a silent
   failure. A default only matters for an omitted column (and this framework always writes every column), and key
   drift is TASK-567's and TASK-622's ground. Record the reasoning in the task.
6. Tests: a live test per provider (both polarities, proven red), a clean control per provider, and an offline test
   that `DeclaresNotNull` and `FieldDefinition` agree for every field shape.

## Progress log

- 2026-10-15 — Live PostgreSQL, MSSQL and MySQL (Docker), plus SQLite. Red: all four reported the nullability-flipped
  table clean. Green: PostgreSQL 149, MSSql 184, MySQL 168, SQLite 506, SQL core 710, Health.SQL 20, TimescaleDB 86
  (offline; no container). Deviation from plan step 3: no extracted `DeclaresNotNull`. The check reads `IsNotNull`
  directly, and an offline test pins `FieldDefinition` to it. A first cut implied NOT NULL for keys per provider; it
  produced false positives on SQLite and on keyless `AbstractModel` tables, so keys are skipped instead.
  Seven hand-written fixtures (PostgreSQL, MySQL, Health.SQL) had omitted the `NOT NULL` their models declare. They
  now declare it, so they stay about type.
