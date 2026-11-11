---
id: TASK-917
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P2
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-11
depends-on: [TASK-901]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# SQL views without the provider View package: base DDL and probe gaps

## Context

Found by TASK-901 (2026-11-11). Every consumer loads `Birko.Data.SQL.View` without the provider View package (BardStudio,
Sandbox, DraCode, Presenter, Symbio, Reps), so the base implementation is what they run:
- **The base `CreateView` renders `CREATE OR REPLACE VIEW`**, which SQLite ("near OR") and SQL Server ("Incorrect syntax
  near 'REPLACE'") reject (measured; recorded in `ViewProbeContract.CreatesViewsWithoutViewPackage`). A persistent view
  cannot be created on those providers without the View package — fails loudly. Affects the SQLite-only consumers and
  Symbio's SQL Server path if they ever use persistent views (none does today).
- **The base probe answers `true` for a table with the view's name** (known from an older SQLite test comment).
- **Not measured (rule 54):** inside an ambient PostgreSQL transaction, the probe's swallowed missing-relation error
  probably aborts the transaction (25P02).

## Acceptance criteria

- [ ] Reproduce each (rule 54).
- [ ] The base DDL works on every provider (dialect-rendered) or refuses by name pointing at the View package; the
      probe distinguishes a table; the transaction case fixed (savepoint) or refused. Tests red before.

## Out of scope

- The items TASK-901 fixed.

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
