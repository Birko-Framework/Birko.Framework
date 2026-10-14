---
id: TASK-611
parent: EPIC-014
feature: null
status: todo
priority: P3
assignee: ai
created: 2026-10-14
depends-on: []
blocks: []
related: [TASK-610, TASK-269]
findings: []
pr: null
github-issue: null
jira-key: null
---

# On a case-sensitive MSSQL database, DetectDrift / EnsureColumns accept a column that differs only in case

## Context

Found 2026-10-14 while fixing TASK-610 (asked: "check other providers too"). TASK-610 made the schema-drift match
follow `AbstractConnectorBase.StoredColumnNameComparer`: `Ordinal` where the provider folds (PostgreSQL), else
`OrdinalIgnoreCase`. MSSQL keeps `OrdinalIgnoreCase`.

SQL Server resolves a column reference by the **database collation**, not by the dialect. Under a `_CS_` collation
(e.g. `Latin1_General_CS_AS`) the bare `SupplierVatin` the connector emits does not reach a column stored as
`suppliervatin`. So on such a database the TASK-610 defect is present: drift reports clean, every query on the
column fails, and `EnsureColumns` adds nothing. Not measured. This is from SQL Server's documented identifier
resolution, so the first step is to reproduce it.

`StoredColumnNameComparer` is virtual for exactly this case. The MSSQL connector can answer it from the database:
for example, `SELECT CASE WHEN 'a' = 'A' THEN 0 ELSE 1 END` evaluates under the database default collation. Read it
once per connection string. It must not be a per-call round-trip on a process-wide cached object (rule 36/62).

## Acceptance criteria

- [ ] Live MSSQL test: on a database created `COLLATE Latin1_General_CS_AS`, a column renamed to differ only in
      case is reported `CaseMismatch` and `EnsureColumns` adds nothing. Red before.
- [ ] On the default (CI) collation the same table stays clean. That is the control.
- [ ] The comparer is resolved from the database, not configured. A wrong setting is the silent option rule 55 forbids.

## Out of scope

- PostgreSQL (TASK-610). MySQL column names are case-insensitive regardless of collation; SQLite always is.

## Human test plan

N/A — covered by the live provider test.

## Implementation plan

_Drafted when picked._
