---
id: TASK-888
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-09
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Divide by zero in a SQL filter is NULL on SQLite and MySQL; C# throws

## Context

Found by TASK-880 (2026-11-09), measured: `x.Hits / (x.Hits - x.Hits) == 0` gives NULL on SQLite and MySQL, so the
read returns no rows and the count is 0, silently. PostgreSQL and SQL Server throw (SQL Server's read did not before
TASK-880). C# throws `DivideByZeroException`. Same "SQL vs C#" family as TASK-859 and TASK-860.

## Acceptance criteria

- [ ] **Decide with the user:** refuse a division whose divisor can be zero (not decidable in general), guard it
      (`NULLIF` / `CASE` raising an error) so every provider throws, or document the NULL answer.
- [ ] Implement and test per provider, red before the fix.

## Out of scope

- Other arithmetic (TASK-860, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
