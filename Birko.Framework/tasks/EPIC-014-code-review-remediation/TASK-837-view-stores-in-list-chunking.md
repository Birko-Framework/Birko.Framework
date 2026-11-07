---
id: TASK-837
parent: EPIC-014
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-07
depends-on: [TASK-833, TASK-834]
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: [FIELD-051]
pr: null
github-issue: null
jira-key: null
---

# View stores get the same IN-list chunking as their base stores

## Context

TASK-833 (SQL) and TASK-834 (Cosmos DB) chunk an `ids.Contains(x.Col)` list past the provider's limit in the stores.
Both left the view stores unwired, as a stated limitation:
- **`Birko.Data.SQL.View`** has its own select path. Past `MaxCommandParameters` the driver's own error surfaces, for
  example SQL Server Msg 8003.
- **`Birko.Data.CosmosDB.Views/CosmosViewStore.cs`** builds its own LINQ query. Past 13,105 Guids, Cosmos DB answers
  400 SC3020.

## Acceptance criteria

- [ ] A view read or count with a list past the limit is split under the same rules as its base store, through the
      same planner (`InListChunkPlanner` for SQL, `CosmosInListChunking` for Cosmos DB), or refused with
      `NotSupportedException` before any request. There is no second copy of the rules (rule 16).
- [ ] Tests on SQLite (a lowered limit) and the live Cosmos DB emulator.

## Out of scope

- The base stores (TASK-833, TASK-834).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
