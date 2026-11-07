---
id: TASK-837
parent: EPIC-014
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
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

- [x] A view read or count with a list past the limit is split under the same rules as its base store, through the
      same planner (`InListChunkPlanner` for SQL, `CosmosInListChunking` for Cosmos DB), or refused with
      `NotSupportedException` before any request. There is no second copy of the rules (rule 16).
      → **SQL, measured before:**
        - The 4 view funnels (`Select(View)` and `SelectCount(View)`, sync and async) ran no planner.
        - With a limit of 40, 16 of 17 tests were red.
        - On a normal SQLite connector, a view read with 33,000 ids threw "too many SQL variables".
      → **SQL, now:** `AbstractConnector_ViewInListChunking.cs` plans through the existing `PlanInListChunks`, probing
        with the same command builder the statement uses.
        - Reads are streamed and counts summed.
        - Sort or paging past the limit refuses.
        - An on-the-fly aggregate view refuses past the limit, because its WHERE runs before GROUP BY.
        - A persistent view filters its own output rows, so it splits even when grouped.
      → **Cosmos DB, measured on the emulator:**
        - `CosmosViewStore` does not use LINQ (the brief and TASK-834 were wrong). It renders SQL through
          `CosmosFilterTranslator`, with values as parameters.
        - A collection `Contains` was already refused, with an unrelated message, so the 13,105-Guid query-text
          ceiling cannot be reached through a view. `CosmosInListChunking` is not needed there.
        - `v.Tags.Contains(x)` rendered `CONTAINS()` over an array and **silently matched nothing**.
      → **Cosmos DB, now:** every collection `Contains` refuses by name before any request. This is a behaviour change:
        a filter that returned nothing now throws (rules 26 and 41). The stale "uses LINQ" class comment is
        corrected.
- [x] Tests on SQLite (a lowered limit) and the live Cosmos DB emulator.
      → `Task837ViewInListChunkingTests` (SQL.Views) and `Task837CosmosViewInListTests` (live). One no-request test
        points the client at port 1.
      → Red proof: the aggregate guard off and the count sum turned into an overwrite gave 4 red; the Cosmos tests on
        the old message, 14 of 15 red.
      → All 36 importer suites of SQL.View and CosmosDB.Views are green live (PostgreSQL, MySQL, SQL Server,
        TimescaleDB, Cosmos emulator, `BIRKO_REQUIRE_LIVE=1`), run in a temporary worktree while TASK-836's edits were
        in flight: SQL.Views 142, CosmosDB.Views 55, SqLite 661.

## Out of scope

- The base stores (TASK-833, TASK-834).
- IN support in Cosmos DB view filters is new capability, not a fix. It is the user's call whether to add it.

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
