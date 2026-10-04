---
id: TASK-514
parent: null
feature: null
status: todo
priority: P3
assignee: ai
created: 2026-10-04
depends-on: [TASK-513]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# SQLite views aggregate a `decimal` column with the built-in `SUM` / `AVG`, which go through a double

## Context

Spawned from TASK-513, which made a SQLite `decimal` column `TEXT COLLATE BIRKO_DECIMAL` and registered exact
aggregates `birko_decimal_sum` / `birko_decimal_avg` (`SqLiteDecimal`) on every connector connection — but did not
make views emit them.

**What a view does today.** `FunctionField.CreateFunctionField` names the aggregate `SUM` / `AVG` / `MIN` / `MAX`
provider-blind, and `AbstractField.GetSelectName` renders `NAME(column)` with no connector in reach. On a
`TEXT COLLATE BIRKO_DECIMAL` column:

- `MIN` / `MAX` are **correct** — they compare through the column's collation (measured at TASK-513).
- `SUM` / `AVG` behave **exactly as before TASK-513**: SQLite converts the text to doubles, so a sum carries binary
  floating-point error (measured: `SUM` of ten `0.1` returns a REAL). No worse than the old `REAL` column, not exact.

**Why it was not done in TASK-513.** The aggregate name reaches SQL through four connector-free paths: the runtime
`SELECT` list (`Table.GetSelectFields` / `View.GetSelectFields`), the persistent-view DDL (`ViewSelectSqlBuilder`),
the persistent read, and the view `ORDER BY` (`DataBase_ViewOrderBy`, `DataBase_View`). Threading a provider hook
through all four is its own unit of work.

## Acceptance criteria

- [ ] A SQLite view with `[SumField]` / `[AvgField]` over a `decimal` column returns the exact sum / average
      (e.g. ten `0.1` → `1.0`, `1234567890123456.123456 + 1` exact), through every path above that can render it
- [ ] Integer `SUM` / `AVG`, and every other provider, render unchanged (`SUM(col)`)
- [ ] The provider choice has one producer (a connector hook beside `IncrementExpression`), not a per-path check
- [ ] Each path proven by a test that fails with the hook removed

## Out of scope

- `MIN` / `MAX` — already correct through the collation
- `GROUP BY` / `HAVING` on decimal columns — the collation already groups `10.0` with `10.00`

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan

_Populated by `/tasks plan TASK-514` — leave empty until then._
