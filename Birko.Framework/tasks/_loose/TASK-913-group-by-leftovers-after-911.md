---
id: TASK-913
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-11
depends-on: [TASK-911]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# SQL GROUP BY leftovers after TASK-911

## Context

Found by TASK-911 (2026-11-11):
- **Cost on SQL Server:** an exact string GROUP BY on an indexed key went 17–20 ms → 158–183 ms (100k rows); for columns
  bounded below 4000 characters the form without the `NVARCHAR(MAX)` cast measured 45–58 ms (the cast is needed only when
  a value can reach 4000 characters).
- **`char` group keys** follow the collation on SQL Server and MySQL (`'a'` with `'A'`); only `string` was in scope.
- **TimescaleDB continuous aggregates** (`Birko.Data.Migrations.TimescaleDB`) group by caller-given columns with a plain
  GROUP BY; they merge only under a non-deterministic collation.
- `TimeIntervalParser` refuses `"1h"` with "is not a positive interval", which reads as a value error, not a syntax one.

## Acceptance criteria

- [ ] Measure each (rule 54).
- [ ] Fix, refuse by name or record; tests red before.

## Out of scope

- String GROUP BY on `string` columns (TASK-911, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
