---
id: TASK-855
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-07
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# `PropertyUpdate` on a column renamed with fluent `HasColumnName` writes the C# name

## Context

Found by TASK-849 (2026-11-07), measured on SQLite. `DataBase.GetFieldFromLambda` ignores the fluent column name, so
`PropertyUpdate.Set(x => x.Points, …)` on a property mapped `HasColumnName("Pts")` renders `SET Points = …` and fails
with "no such column". It fails loudly. It is the "two producers for one name" shape that rule 48 describes: the
fluent mapping and the lambda resolver each define the column's identity.

## Acceptance criteria

- [x] Reproduced on SQLite in-process: Set, Increment and Compute on a renamed column fail today. Record the error.
      → With `Points` → `Pts`, `Amount` → `Amt` and `Price` → `Prc`:
        - Set, Increment, and the connector's `Update<T,P>` expression update (sync and async) failed with
          `no such column: Points`.
        - Compute (the column its formula reads) and the sync store Set failed with `no such column: Price`.
        - A filter and an ORDER BY already resolved through the table and worked.
      → **Worse than loud:** TASK-849's planner recorded the written column as `Points` while the filter read `Pts`.
        With only the SET fixed, a chunked update of a renamed column would have applied twice.
- [x] The update path resolves the column through the same producer the table build uses, so renamed columns work for
      every `PropertyUpdate` kind (rules 16 and 48).
      → **Cause:** `DataBase.GetFieldFromLambda` and `GetField<T,P>` built a fresh field with `LoadField`, while the
        fluent rename exists only on the built table. Two producers for one column name.
      → **Fix:** a new `LambdaField(property, modelType)` in `DataBase_Field.cs` returns
        `TryLoadTable(modelType)?.GetFieldByPropertyName(name)`, and both resolvers use it first. `LoadField` stays as
        the fallback for types with no table, so the ignored-property `ArgumentException` is unchanged.
      → That one change fixes the SET target, `Compute`'s column reads, `Update<T,P>`, and the planner's written
        column.
- [x] Regression tests red before the fix; the Birko.Data.SQL importer suites stay green.
      → `Task855FluentColumnNameUpdateTests` (9 tests): 8 red before the fix (only the filter / ORDER BY test passed);
        TASK-849's 32 still green.
      → All 35 importer suites green live (SQL 777, SqLite 722, MSSql 249, Models.SQL 73). The first run hit
        TASK-856's in-progress `DataBase.cs` edit, and the rerun was green.

## Out of scope

- Nested members (TASK-845).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
