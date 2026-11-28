---
id: TASK-1019
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-28
depends-on: []
blocks: []
related: [TASK-1008, TASK-860]
findings: []
pr: null
github-issue: null
jira-key: null
---

# The SQL WHERE .Date wrapper renders DATE(col) on every provider

## Context

Found by [[TASK-1008]]'s pass 2 (method calls, casts and string matching) on 2026-11-28; the user asked for it to be filed.

The WHERE-side `.Date` wrapper (`Birko.Data.SQL/SQL/DataBase.cs`, about line 1221) renders `DATE(col)` with no
dialect; `DATE` is not T-SQL. It is reached only when a column's `.Date` is compared with a value that does not evaluate
to a `DateTime`, and its constant arm (about line 1227) cannot be reached since TASK-860 refuses a non-column `.Date`
(rule 53).

## Acceptance criteria

- [ ] Show whether the wrapper is reachable (a test, or a measurement that nothing reaches it).
- [ ] Reachable: render it through the dialect as the SET `.Date` does (`CAST(… AS DATE)`, SQLite's form), red first
  on SQL Server. Unreachable: delete it. Either way the dead constant arm goes.

## Human test plan

N/A — fully covered by automated tests.
