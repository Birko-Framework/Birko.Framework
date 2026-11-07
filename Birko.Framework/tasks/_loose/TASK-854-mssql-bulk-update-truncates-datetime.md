---
id: TASK-854
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: in-progress
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

- [ ] Reproduced live first: `BulkUpdate` and `BulkDelete` with a `DateTime` carrying 100 ns precision, including a
      row whose `DateTime` is null and a later row with a value. Record what is stored or matched.
- [ ] Every parameter these paths bind is created and typed through the connector's one producer (`CreateParameter`),
      including on re-bind (TASK-852). No direct `new SqlParameter` with inferred typing remains in the connector
      (grep, and list any other site found).
- [ ] Regression tests red before the fix, sync and async; the SQL Server live suite stays green.

## Out of scope

- `AbstractConnectorBase.AddParameter` re-binding (TASK-852).

## Human test plan

N/A — fully covered by automated tests against the live SQL Server.

## Implementation plan
