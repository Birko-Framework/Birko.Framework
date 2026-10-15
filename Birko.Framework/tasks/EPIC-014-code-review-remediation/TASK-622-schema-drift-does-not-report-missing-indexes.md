---
id: TASK-622
parent: EPIC-014
feature: null
status: todo
priority: P3
assignee: ai
created: 2026-10-15
depends-on: []
blocks: []
related: [TASK-595, TASK-204, TASK-269]
findings: []
pr: null
github-issue: null
jira-key: null
---

# Schema drift does not report a declared index that the table lacks

## Context

Found 2026-10-15 reviewing schema-drift coverage. Index problems surface only through
`AbstractConnector.IndexCreationFailures`, which lists indexes schema-ensure **tried** to build and could not (TASK-204).
Schema-ensure runs `CREATE TABLE IF NOT EXISTS` once. An index the model gained after the table existed is created only
if something ensures indexes again, and an index dropped by hand is never noticed. `DetectDrift` compares columns
only, so a table with none of its declared indexes reports clean. TASK-595 measured that state for real:
`Birko.Models.Users.SQL` had zero indexes, and nothing said so.

## Acceptance criteria

- [ ] Measured first: a table created before an index was declared (or with the index dropped) reports clean in
      `DetectDrift` and `SchemaDriftHealthCheck`. Record it, per provider.
- [ ] A declared index that is absent is reported, by name and columns, read from the provider's catalogue: the same
      `ListIndexesSql` the index managers use, so there is one reader. A present index with different columns or
      uniqueness is reported too.
- [ ] Undeclared indexes are not reported (DBAs add their own). Record the reason.
- [ ] Live tests per SQL provider, proven red.

## Out of scope

- Creating the missing index. That is `EnsureIndexes`' (or schema-ensure's) job, not a report's.

## Human test plan

N/A — covered by the live provider tests.

## Implementation plan

_Drafted when picked._
