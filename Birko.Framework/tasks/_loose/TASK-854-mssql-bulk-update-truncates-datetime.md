---
id: TASK-854
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P2
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-07
depends-on: [TASK-852]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# SQL Server's BulkUpdate and BulkDelete truncate DateTime to datetime precision

## Context

Found and measured live by TASK-852 (2026-11-07). The user asked for the day's suspected bugs to be fixed.
- `MSSqlConnector.BulkUpdate` / `BulkDelete` (Birko.Data.SQL.MSSql, around lines 837 and 860) build
  `new SqlParameter(name, DBNull.Value)` and assign `.Value` directly, bypassing `CreateParameter`, which is where
  SQL Server sets `DateTime2` (TASK-656).
- Measured: a `BulkUpdate` with `…56.1234567` stored `…56.1233333`, a silently wrong value.
- It is the same class of defect as TASK-852 (rule 28: a value the driver infers a type for fails quietly).

## Acceptance criteria

- [x] Reproduced live first: `BulkUpdate` and `BulkDelete` with a `DateTime` carrying 100 ns precision, including a
      row whose `DateTime` is null and a later row with a value. Record what is stored or matched.
      → **Before the fix (live, 6 of 6 new tests red):**
        - `BulkUpdate(Async)` sent `…56.1234567` and stored `…56.1233333`; also for value-null-value and null-value
          sequences, and `…56.1234568`.
        - `BulkDelete(Async)` on a composite key including a `DateTime` **deleted nothing**: all 3 rows remained, and
          nothing was raised.
- [x] Every parameter these paths bind is created and typed through the connector's one producer (`CreateParameter`),
      including on re-bind (TASK-852). No direct `new SqlParameter` with inferred typing remains in the connector
      (grep, and list any other site found).
      → All four methods (update and delete, sync and async) drop the `new SqlParameter(name, DBNull.Value)`
        placeholders and bind each row through `AddParameter`, so `CreateParameter` (`DATETIME2`) types it, and
        TASK-852 re-creates a re-bound name.
      → A grep of the project finds no `new SqlParameter`, `AddWithValue` or direct `.Value =` left. `BuildDataTable`
        (for `SqlBulkCopy`) fills `DataTable` rows, not parameters, and is unchanged; its `DateTime` precision was not
        checked.
- [x] Regression tests red before the fix, sync and async; the SQL Server live suite stays green.
      → `Task854BulkDateTimePrecisionLiveTests` (three theories, sync and async): 6 red before, green after.
      → MSSql.Tests 249, MSSql.View.Tests 22 (live) and SQL.Tests 777 green.

## Out of scope

- `AbstractConnectorBase.AddParameter` re-binding (TASK-852).

## Human test plan

N/A — fully covered by automated tests against the live SQL Server.

## Implementation plan
