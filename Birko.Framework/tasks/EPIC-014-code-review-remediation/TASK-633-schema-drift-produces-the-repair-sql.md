---
id: TASK-633
parent: EPIC-014
feature: null
status: done
priority: P2
assignee: ai
created: 2026-10-15
depends-on: []
blocks: []
related: [TASK-269, TASK-610, TASK-620, TASK-622, TASK-567]
findings: []
pr: null
github-issue: null
jira-key: null
---

# Schema drift produces the repair SQL — the ADD with its backfill, and the RENAME on a wrong case — not only the report

## Context

Filed 2026-10-15 from Symbio (consumer), as the framework prerequisite of **Symbio TASK-932** ("one drift engine:
Symbio's boot `SchemaDriftCheck` runs on the framework's `DetectDrift`"). Symbio and the framework each have a drift
detector; the user decided (2026-10-14) that Symbio's becomes a thin boot sweep over the framework's, so the
comparison exists once. The framework's detector now covers more than Symbio's (TASK-269 types, TASK-610 case,
TASK-620 nullability, TASK-567 key, TASK-622 indexes) — but it only **reports**. Symbio's check also prints the
statements that **repair** the drift, and its deploy routine (`deploy/godzilla/deploy.sh`) applies them to a restored
copy before production. Moving Symbio onto the framework without this would lose the repair, so every consumer gets
it here instead of only Symbio keeping a second copy (`SqlTypeFor` / `RepairSql` in
`Symbio.DataAccess/Sql/SchemaDriftCheck.cs`, which TASK-932 deletes).

What Symbio's copy does today, and the measured reason behind each rule:

- **Missing column → `ADD COLUMN` + a backfill for a non-nullable value type.** Measured in Symbio: the ADD alone
  leaves existing rows NULL, and the readers call the typed getter with no `IsDBNull` guard
  (`GuidField.Read` → `SqliteDataReader.GetGuid`), so **every read of the entity throws** ("The data is NULL at
  ordinal 0") — a repair that only fixed the writes broke all reads. Backfill = the type's default (`Guid.Empty`, 0,
  false, `default(DateTime)`, enum 0); none for a nullable or reference type, where NULL is legal; none and a named
  type in the message when the type is unknown — the operator decides rather than the check inventing data.
- **Case-only mismatch (PostgreSQL, TASK-610's `CaseMismatch`) → `RENAME COLUMN` from the stored spelling.** Adding
  the column the connector reads would orphan every value in the stored one.
- Column types come from Symbio's own `SqlTypeFor` — the copy that drifted from `ConvertType` and is TASK-881's
  blind spot. Here they come from `ConvertType`, the same source as `CREATE TABLE`.

## Acceptance criteria

- [x] A provider-aware way to turn a drift report into statements (shape is this task's call — e.g. a repair plan
      per table: the statements, plus the drifts it will not action and why).
- [x] `Missing` → `ADD COLUMN` with the `ConvertType` type, followed by the backfill `UPDATE … WHERE col IS NULL` for
      a non-nullable value type, per the rules above. Correct syntax per SQL provider (SQLite, PostgreSQL, MSSQL,
      MySQL — e.g. MSSQL has no `COLUMN` keyword in `ADD` and renames through `sp_rename`).
- [x] `CaseMismatch` → a rename from `StoredColumn` that keeps the values.
- [x] `TypeMismatch`, `NullabilityMismatch`, key and index drifts, and `Unexpected` produce **no** statement — they
      are listed as the operator's (a type or key change can lose data or fail on existing rows); the plan says which.
- [x] Tests, live on every SQL provider the suites run: a table created before a model gained a non-nullable column
      is repaired by the plan, a re-run of drift detection reports nothing, and **existing rows read back** (the
      NULL-read failure above is the regression to prove); a wrong-case column keeps its values after the rename.
      Mutation-prove the backfill and the rename.
- [x] CHANGELOG; Symbio TASK-932 is told (it consumes this).

## Out of scope

- Applying the statements automatically — the framework produces them; when to run them is the consumer's (Symbio's
  deploy routine rehearses them on a restored copy first).
- Non-SQL providers (Mongo, RavenDB, Cosmos, ElasticSearch mapping drift — TASK-621/624).

## Human test plan

N/A — statements and their effect on a real table are asserted by the live provider suites; nothing is rendered.

## Resolution (2026-10-15)

**`AbstractConnector.PlanRepair(Type)` → `SchemaRepairPlan`**: `Statements` (in order), `NotActioned` (each drift the
plan will not write, with the reason), `Requirements` (what the connection running the statements must provide). It
runs nothing.

- **Missing**: the statement is `AddColumnSql`, the producer `EnsureColumns` (TASK-510) already executes, typed by
  `ConvertType`. **One deliberate difference from the task text:** there is no separate `UPDATE … WHERE col IS NULL`.
  A non-nullable value-type property maps to a NOT NULL column (`GuidField` and friends pass `notNull: true`), and the
  ADD carries `DEFAULT <type default>`, which fills the existing rows on all four providers (measured live). Symbio's
  copy needed the UPDATE because its `SqlTypeFor` added the column nullable. A column `EnsureColumns` refuses (key,
  unique, identity, NOT NULL with no value-type default) is listed instead.
- **CaseMismatch**: `RenameColumnSql(table, stored, StoredColumnName(column))`, a new connector producer: `ALTER TABLE …
  RENAME COLUMN` (PostgreSQL, SQLite 3.25+, MySQL 8), `EXEC sp_rename` on SQL Server.
- **Everything else** (type, nullability, key, unexpected column, index drifts) is listed with its reason.
- **Found while testing, and announced in the plan (rule 64):** on SQLite a `decimal` ADD names the `BIRKO_DECIMAL`
  collation, and a connection that did not call `SqLiteDecimal.Register` (the `sqlite3` CLI, a raw `SqliteConnection`)
  refuses it with "no such collation sequence" (measured). The new `StatementRequirement(sql)` connector hook puts that
  in `plan.Requirements`; Symbio TASK-932 is told.

**Tests.** `SchemaRepairPlanTests` (SQLite, 6) and `SchemaRepairPlanLiveTests` (PostgreSQL 3, SQL Server 2, MySQL 2,
live): an old table with a row is repaired by running the statements as text, the old row reads back as defaults, a
re-run of the detection reports nothing, and a second plan is empty; the rename keeps values on every provider; a real
case-only mismatch is repaired on PostgreSQL; unrepairable drifts are listed and written as nothing. **Mutation-proved:**
an ADD without NOT NULL / DEFAULT (Symbio's old shape) fails the read with "The data is NULL at ordinal 1" (SQLite) and
the drift re-check (PostgreSQL); an ADD instead of the rename leaves the stored "Name" as an unexpected column
(PostgreSQL). Regression: the SQL core, SQLite, PostgreSQL, SQL Server, MySQL, TimescaleDB and Health.Data.SQL suites
(1,973 tests), live, green.

**Symbio TASK-932 told** (Symbio `6e2e639c`): the API, no separate UPDATE, and the SQLite requirement.
