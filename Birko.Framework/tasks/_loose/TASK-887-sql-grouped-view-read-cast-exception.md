---
id: TASK-887
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P1
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

# A grouped `SqlViewStore` read throws a cast exception on every SQL provider

## Context

Found by TASK-880 (2026-11-09), measured on SQLite, PostgreSQL, MySQL and SQL Server: a view
`Select(o => o.Kind, v => v.Kind).GroupBy(o => o.Kind).Sum(o => o.Big, v => v.Total)` over one table (no join), read with
`QueryAsync(v => v.Kind == "a")`, on-the-fly and persistent, throws `InvalidCastException` (`ArgumentException` on
SQLite). The transform appears to read `Kind` from the SUM column: the `GetTableFields()` order does not match the
SELECT order. `AggregateViewDdlTests`, which use a join, pass. Loud, but grouped views over one table do not work.

## Acceptance criteria

- [ ] Reproduce (rule 54) per provider: grouped views with and without a join, different property declaration orders,
      several aggregates, on-the-fly and persistent.
- [ ] Column order comes from one producer for the SELECT and the reader transform.
- [ ] Tests, red before the fix.

## Out of scope

- Read-error propagation (TASK-880, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
