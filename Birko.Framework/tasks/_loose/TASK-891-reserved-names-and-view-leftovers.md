---
id: TASK-891
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P2
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-09
depends-on: [TASK-872]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Reserved names and persistent views: leftovers after TASK-872

## Context

Found by TASK-872 (2026-11-09):
- **A failed `EnsureAsync` followed by a persistent view read silently returns 0 rows** on SQL Server and MySQL, while
  Count throws "Invalid object name": the reader swallows a missing table (rule 26). Not reserved-word specific.
  Measured.
- **`ViewSqlGenerator` (migrations) emits view columns bare with no provider list**: a generated persistent view with a
  reserved view property would read the database function silently on PostgreSQL and SQLite (reasoned from TASK-872's
  measurement of the same shape).
- **Connector overloads taking a resolved `Tables.Table` or a table name** (`Delete(Table, …)`, `Insert(string, …)`,
  `Update(string, …)`, `SelectCount(IEnumerable<string>)`) are unguarded; a direct `Delete` through
  `DataBase.LoadTable(t)` is still silent.
- `if (table == null) yield break;` in `SelectAggregate(Async)` is unreachable (rule 53).

## Acceptance criteria

- [ ] Reproduce each (rule 54).
- [ ] The missing-table read reports (or heals) consistently with Count; the migrations generator uses the provider's
      list; decide whether the `Table` / name overloads are a supported API and guard them through TASK-872's funnel or
      record why not; remove the dead branch.
- [ ] Tests, red before each fix.

## Out of scope

- The typed entry points and view DDL (TASK-872, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
