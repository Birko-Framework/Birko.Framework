---
id: TASK-876
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P1
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-09
depends-on: [TASK-868]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Cosmos DB `Aggregate` ignores the filter

## Context

Found by TASK-868 (2026-11-09), measured live: `CosmosAggregationHelper.BuildAggregateSql` emits no WHERE clause, so
`Aggregate` / `AggregateAsync` aggregate every document whatever `query.Filter` says. `A > 3` aggregated all 6 rows.
Every filter, silently wrong. There is no live Cosmos DB aggregate test.

## Acceptance criteria

- [x] Reproduce (rule 54) live: plain, negated, `Contains`, null-ordering filters, with each aggregate kind, against C#.
      → Measured 2026-11-09 on the emulator, sync and async, Count/Sum/Avg/Min/Max, grouped and ungrouped, against
        `Compile()` + LINQ over the stored documents: all 22 filtered cases wrong (11 shapes: plain, negated, and/or,
        `== null`, `Contains` array/list/negated, three null orderings, nested member). `A > 3` counted 6, not 3.
        `x => true` and no filter were right. Also: an aggregate as the first call on a new store returned nothing,
        because it did not open the container first.
- [x] The filter is rendered through the store's own translator (the one reads use, `NullOrderingFold` included), or an
      untranslatable filter is refused by name. A filter must never be dropped.
      → Each store's `BuildAggregateQuery` applies the reads' rewrites (`SpanContains`, `DateTruncation`,
        `NullOrderingFold`) and renders through the reads' own LINQ provider (`BuildQuery(..).ToQueryDefinition()`);
        `CosmosAggregationHelper.BuildAggregateQuery` lifts the WHERE and its parameters into
        `SELECT … FROM root WHERE … GROUP BY …`. Any other rendered shape is refused by name (rules 41, 42). A filter
        folding to false sends `WHERE false`, as reads do. A `Contains` list past the query-length limit is refused
        before any request (groups cannot be merged across chunks). Both methods now initialise the container and
        refuse a null query. **Breaking:** the public `BuildAggregateSql<T>`, which dropped the filter, is removed
        (no caller).
- [x] Live tests, red before the fix.
      → `Task876AggregateFilterLiveTests` (13 shapes × grouped/ungrouped, sync and async, first call on a new store, long
        `Contains` list run and refused) and offline `Task876AggregateQueryTests` (WHERE placement, parameters,
        refusals). Red: 22 of 22 filtered cases on the old code; with the filter dropped again, 10 of 12 tests failed.
        Green 12/12.
      → Live, `BIRKO_REQUIRE_LIVE=1`: CosmosDB 372, Views 118, ViewModel 5, BackgroundJobs 28, Migrations 44, Sync 4,
        Workflow 8, all green. No consumer calls a Cosmos aggregate.
      → **Found, filed:** TASK-885 (Cosmos aggregate leftovers: SUM/MIN over a nullable field return null, measured;
        `TimeBucketInterval` ignored; views use another translator; empty ungrouped aggregate row count differs by
        provider).

## Out of scope

- Null orderings (TASK-868, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
