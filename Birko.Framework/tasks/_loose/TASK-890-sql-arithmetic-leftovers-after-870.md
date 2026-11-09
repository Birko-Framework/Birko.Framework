---
id: TASK-890
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-09
depends-on: [TASK-870]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# SQL arithmetic leftovers after TASK-870

## Context

Found by TASK-870 (2026-11-09):
- **SQLite overflow type:** `birko_checked_int` surfaces as `SqliteException` (Error 1); the read-modify-write fallback
  throws `OverflowException`, and the servers throw their drivers' exceptions. Callers cannot catch one type (rule 9).
- **SQLite narrowing SET** (`(short)(Sh + 1)`), refused since TASK-860, could translate through `birko_checked_int`
  with the target type's range.
- **MySQL computes integer expressions in BIGINT**, so an overflow mid-expression (`Hits * 2 / 2`) is likely silent.
  Not measured.
- **A fold over a persistent view column** is refused; it could be supported by renaming the column inside the fold.
- Reads with integer arithmetic on SQLite now fail the whole statement when any row overflows, as PostgreSQL and SQL
  Server do; MySQL widens and answers.

## Acceptance criteria

- [ ] Measure each (rule 54) and record.
- [ ] Fix or record each; one exception type for an arithmetic overflow across providers if decided worthwhile.
- [ ] Tests, red before any fix.

## Out of scope

- The items TASK-870 fixed.

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
