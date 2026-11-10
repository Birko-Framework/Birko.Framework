---
id: TASK-898
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-10
depends-on: [TASK-892]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# SQL aggregates: leftovers after TASK-892

## Context

Found by TASK-892 (2026-11-10):
- **View decimal AVG on PostgreSQL, MySQL and SQL Server is refused**, including averages that terminate. Exact would
  need a view column made of two SQL columns (SUM and COUNT): reader ordinals, persistent DDL and ORDER BY.
- **Migrations' `ViewSqlGenerator` knows no provider**, so it renders the portable aggregate form: no SQLite decimal
  aggregates, no PostgreSQL float8 cast, decimal AVG not refused.
- Recorded: a double SUM/AVG depends on row order (SQLite and SQL Server 0.6 where LINQ gives 0.6000000000000001); an
  integral AVG whose sum exceeds 2^53 is summed as double; ordering by a store decimal AVG on servers uses the
  server's scale; TimescaleDB time buckets not measured.

## Acceptance criteria

- [ ] Reproduce each (rule 54).
- [ ] Exact decimal AVG in server views, or the refusal kept and recorded; the migrations generator renders through the
      provider's dialect (one producer); each recorded item fixed or kept with its reason.
- [ ] Tests, red before each fix.

## Out of scope

- The SUM-of-nothing answer (TASK-886).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
