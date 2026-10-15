---
id: TASK-623
parent: EPIC-014
feature: null
status: todo
priority: P3
assignee: ai
created: 2026-10-15
depends-on: []
blocks: []
related: [TASK-610, TASK-611, TASK-612]
findings: []
pr: null
github-issue: null
jira-key: null
---

# MySQL catalogue lookups match the table name with a plain `=`, whose case behaviour has not been measured

## Context

Found 2026-10-15 reviewing schema-drift coverage after TASK-610/611. MySQL **column** names are case-insensitive
everywhere, but **table** names follow `lower_case_table_names`. Its default is `0` on Linux, where table names are
case-sensitive, so a quoted `MyTable` and `mytable` are different tables. `MySQLConnector.StoredColumnsSql` filters
`information_schema.COLUMNS` with `TABLE_NAME = '<name>'` (`MySQLConnector.cs:242`), and the index manager does the
same.

**Unmeasured:** whether that comparison is case-sensitive under `lower_case_table_names=0` (it depends on the
`information_schema` column's collation, which changed across 5.7 and 8.x). If it is case-insensitive while the server
is not, drift reads a different table's columns for a name that differs only in case. If it is case-sensitive, nothing
is wrong and this task is cancelled with the measurement.

## Acceptance criteria

- [ ] Measured on `mysql:8.4` (CI's image): two tables `DriftCase` and `driftcase` with different columns. Does
      `DetectDrift` for each read its own columns? Record the answer and the `information_schema.COLUMNS.TABLE_NAME`
      collation.
- [ ] If it does not, the lookup compares exactly (or by the server's `lower_case_table_names`), with a live test
      proven red. Otherwise, cancel with the measurement.

## Out of scope

- Column-name case (MySQL is case-insensitive there, settled at TASK-610).

## Human test plan

N/A — covered by the live provider test.

## Implementation plan

_Drafted when picked._
