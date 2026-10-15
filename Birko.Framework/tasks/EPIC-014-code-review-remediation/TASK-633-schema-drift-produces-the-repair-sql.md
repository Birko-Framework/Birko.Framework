---
id: TASK-633
parent: EPIC-014
feature: null
status: todo
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

- [ ] A provider-aware way to turn a drift report into statements (shape is this task's call — e.g. a repair plan
      per table: the statements, plus the drifts it will not action and why).
- [ ] `Missing` → `ADD COLUMN` with the `ConvertType` type, followed by the backfill `UPDATE … WHERE col IS NULL` for
      a non-nullable value type, per the rules above. Correct syntax per SQL provider (SQLite, PostgreSQL, MSSQL,
      MySQL — e.g. MSSQL has no `COLUMN` keyword in `ADD` and renames through `sp_rename`).
- [ ] `CaseMismatch` → a rename from `StoredColumn` that keeps the values.
- [ ] `TypeMismatch`, `NullabilityMismatch`, key and index drifts, and `Unexpected` produce **no** statement — they
      are listed as the operator's (a type or key change can lose data or fail on existing rows); the plan says which.
- [ ] Tests, live on every SQL provider the suites run: a table created before a model gained a non-nullable column
      is repaired by the plan, a re-run of drift detection reports nothing, and **existing rows read back** (the
      NULL-read failure above is the regression to prove); a wrong-case column keeps its values after the rename.
      Mutation-prove the backfill and the rename.
- [ ] CHANGELOG; Symbio TASK-932 is told (it consumes this).

## Out of scope

- Applying the statements automatically — the framework produces them; when to run them is the consumer's (Symbio's
  deploy routine rehearses them on a restored copy first).
- Non-SQL providers (Mongo, RavenDB, Cosmos, ElasticSearch mapping drift — TASK-621/624).

## Human test plan

N/A — statements and their effect on a real table are asserted by the live provider suites; nothing is rendered.

## Implementation plan

_Drafted when picked._
