---
id: TASK-901
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-10
depends-on: [TASK-891]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# SQL views and bulk paths: leftovers after TASK-891

## Context

Found by TASK-891 (2026-11-10):
- **`AbstractConnector.ViewExists` throws instead of answering false** when a view is missing and the provider's View
  package is not loaded (seen on SQL Server without MSSql.View), so Auto mode fails rather than falling back:
  `EnsureSchemaAndReport` rewraps the error in a bare `Exception`, which the probe's `catch (DbException)` misses
  (rule 9).
- **Providers' `BulkInsert` / `BulkUpdate` / `BulkDelete(Type, …)`** (PostgreSQL, MySQL, MSSql) resolve through
  `DataBase.LoadTable`, not TASK-872's `LoadUsableTable`. Not measured; their WHERE names only Guid.
- **Auto mode caches a positive view-existence probe**, so a view dropped outside the connector keeps being read (now
  reported, since TASK-891) until `InvalidateViewExistsCache` is called.

## Acceptance criteria

- [ ] Reproduce each (rule 54).
- [ ] Fix or record each with its reason.
- [ ] Tests, red before each fix.

## Out of scope

- The items TASK-891 fixed.

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
