---
id: TASK-510
parent: null
feature: null
status: todo
priority: P2
assignee: ai
created: 2026-10-03
depends-on: []
blocks: []
findings: [FIELD-019]
pr: null
github-issue: null
jira-key: null
---

# A table created before a type was mapped never gets that column — schema-ensure is create-only, and `AlterTableAdd` cannot add a NOT NULL column to a table with rows

## Context

Found in consumer DraCode (its TASK-082, fixed locally 2026-10-03). DraCode's `UsageRecordEntity.EstimatedCostUsd`
(`double`) existed from the start, but until SH-H037's fix (34928514, 2026-08-08) `double` mapped to no column,
so `CREATE TABLE` silently left it out. After the mapping landed, every INSERT named a column the table did not
have; the consumer swallowed the exception, and two months of usage records were lost.

Two framework gaps made this possible, and any consumer whose database predates a mapping fix has them:

1. **Schema-ensure is create-only.** `CreateSchemaAsync` / store init runs `CREATE TABLE IF NOT EXISTS` and never
   adds a column the model declares and the table lacks. `DetectDrift` (TASK-269) *reports* exactly this as
   `ColumnDriftKind.Missing`, but is deliberately on-demand only, and nothing acts on it.
2. **`AlterTableAdd` cannot add a value-type column to a populated table on SQLite.** The SQLite
   `FieldDefinition` renders `NOT NULL` with no `DEFAULT`, and SQLite refuses
   `ADD COLUMN … NOT NULL` without a default on a table that has rows ("Cannot add a NOT NULL column with default
   value NULL"). So even a consumer that detects the gap cannot use the framework to close it.

DraCode's workaround (`SqlUsageRepository.AddMissingColumnsAsync`): run `DetectDrift`, then
`ALTER TABLE "<t>" ADD COLUMN "<c>" <declared> DEFAULT 0|''` for each `Missing` column. That should become one framework
call.

## Acceptance criteria

- [ ] An opt-in, additive "ensure columns" on SQL stores/connectors: adds every `Missing` column `DetectDrift` reports; never drops or retypes anything; idempotent
- [ ] `AlterTableAdd` (or the new path) gives a NOT NULL value-type column a type default (`0`, `''`, …) so it succeeds on a populated table, on every provider that needs it (at least SQLite; check PostgreSQL/MSSQL/MySQL behaviour and state it)
- [ ] Tests per provider available locally: an old-shape table with rows gains the column, existing rows read back with the default, a second run changes nothing
- [ ] Documented where schema-ensure is described, including that it stays opt-in (TASK-204/254: a diagnostic must not stop a store starting)

## Out of scope

- Type drift (`ColumnDriftKind` other than `Missing`) — reported by `DetectDrift`, not auto-fixed here
- TASK-150 (the remaining unmapped types) — separate; this task is about tables that predate a mapping, whatever the type
- Removing DraCode's local helper — done in DraCode once this ships

## Human test plan

N/A — the upgrade path is asserted per provider by automated tests against real database files/instances.

## Implementation plan

_Populated by `/tasks plan TASK-510` — leave empty until then._
