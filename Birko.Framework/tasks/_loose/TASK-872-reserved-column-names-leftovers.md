---
id: TASK-872
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-08
depends-on: [TASK-862]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Reserved-word column names: leftovers after TASK-862

## Context

Found by TASK-862 (2026-11-08):
- **Direct connector calls skip the guard.** `connector.Select/Insert/Update/Delete(type, …)` on a table Birko did not
  build never runs store init, so a reserved column name is not refused. Measured with raw SQL: `WHERE User = 'postgres'`
  on PostgreSQL and `= 'dbo'` on SQL Server compare the session user, and a delete removed 2 of 2 rows; on SQLite
  `CURRENT_DATE` reads as the date. About 30 typed entry points would need the check.
- **MySQL 5.7:** `Birko.Data.SQL.MySQL/CLAUDE.md` declares 5.7 support, but the list is 8.4's, so it refuses `RANK`,
  which 5.7 accepts. Too strict, not unsafe.
- **SQL view stores** over a base entity are not checked on their own. Reads work on PostgreSQL and MySQL (qualified
  columns) and fail loudly on SQLite and SQL Server.

## Acceptance criteria

- [ ] Decide per item whether it is reachable from a supported API and worth a guard (rule 54: reproduce first).
- [ ] Each one fixed is tested red before the fix; each one left is recorded.

## Out of scope

- Quoting column identifiers (decided against in TASK-862).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
