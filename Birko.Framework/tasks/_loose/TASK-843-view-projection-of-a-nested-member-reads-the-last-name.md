---
id: TASK-843
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P2
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-07
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# A view that projects a nested member reads the field named by the last segment only

## Context

Found by TASK-840 (2026-11-07), from reading the code; not yet reproduced.
- `ViewDefinitionBuilder.GetMemberName` (Birko.Data.Views) keeps only the last member name. A projection
  `Select(r => r.Sub.X, ...)` is recorded as source field `X`, so Cosmos DB projects `c["X"]`, a different field or
  none.
- It is in the shared view definition, so the SQL, MongoDB and Elasticsearch view stores probably read the wrong
  column or field the same way.
- The answer is wrong and nothing is raised. This is the projection-side twin of TASK-840, which fixed the
  filter side for Cosmos DB.

## Acceptance criteria

- [ ] Reproduced first, per provider that has a view store (Cosmos DB live, SQLite in-process, and others where a
      live suite exists): a view projecting a nested member returns the wrong value today. Record it.
- [ ] A nested projection reads the full path where the provider can express it, or is refused by name when the view
      is defined (never silently flattened). One producer for the path (rule 16), shared by every provider.
- [ ] Regression tests red before the fix, one per provider covered.

## Out of scope

- Filters on nested view properties: TASK-840 fixed Cosmos DB's.

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
