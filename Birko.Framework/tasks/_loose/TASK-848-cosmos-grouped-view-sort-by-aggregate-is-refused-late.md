---
id: TASK-848
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-07
depends-on: [TASK-844]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# A Cosmos DB grouped view sorted by an aggregate is sent to the server, which refuses it

## Context

Found by TASK-844 (2026-11-07), measured on the emulator. Cosmos DB cannot sort a grouped view by an aggregate
property in any form:
- a bare alias gives SC2001;
- a quoted alias, or `ORDER BY SUM(...)`, gives 2206.

`CosmosViewStore` builds and sends the statement anyway, and the server answers 400. The failure is loud and predates
TASK-844. `CosmosViewAggregateSqlTests` pins the text of a statement that cannot run.

## Acceptance criteria

- [ ] Re-measured: each form the store can render for "sort a grouped view by an aggregate" fails on the emulator.
- [ ] Either the sort is applied correctly another way (for example over the grouped rows in memory, if that keeps
      paging correct) or it is refused by name with `NotSupportedException` before any request. The choice is
      recorded here, with its reason.
- [ ] The test that pins the unrunnable statement is replaced by one that pins the chosen behaviour; red before the
      fix.

## Out of scope

- Reserved-word aliases (TASK-844, done).

## Human test plan

N/A — fully covered by automated tests against the emulator.

## Implementation plan
