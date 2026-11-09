---
id: TASK-868
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P2
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-08
depends-on: [TASK-867]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# RavenDB's index manager and aggregation helpers skip the null-ordering fold

## Context

Found by TASK-867 (2026-11-08), by reading the code; not reproduced.
- TASK-867 added `NullOrderingFold` and called it in every store and view translator.
- `RavenDBIndexManager` (around lines 358, 398, 419) and the aggregation helpers pass filters to the driver without
  the fold.
- RavenDB matches `Score > null` against every non-null Score (measured in TASK-867), so those paths would return wrong
  rows silently.
- `RavenViewStore` now runs `RavenFilterRewriter` (which brings `Contains` → `In` and the normalizer), but that side
  effect has no test of its own.

## Acceptance criteria

- [x] Reproduce first (rule 54): each filter-taking path that bypasses `RavenFilterRewriter` (index manager, aggregation
      helpers, and any other found by grep), with `x.N > nul`, `!(x.N > nul) && …`, live against the C# oracle. Do the
      same grep for every other provider; record any path that skips the fold.
      → Measured 2026-11-09 live (RavenDB, MongoDB replica set), 9 shapes against `Compile()`:
        - RavenDB `QueryMapReduceAsync` / `QueryMapReduceFirstAsync` / `CountMapReduceAsync`: 21 of 27 wrong or threw
          (`Score > nul` counted 3, not 0; negations missed rows; `Contains` and `x => true` threw).
        - RavenDB store `Aggregate(Async)`, LINQ fallback: 10 of 18 wrong or threw.
        - **MongoDB, found by the grep:** `Aggregate(AggregateQuery)` and the public `Aggregate().Match(Expression)`:
          15 of 27 wrong (`$gte: null` matched null rows).
        - Elasticsearch `Aggregate` already folds (through `ParseFilterQuery`). Index creation, `FacetAggregationHelper`,
          migrations, sync and localization take no filter expression.
- [x] Each one runs the fold (`NullOrderingFold`), one producer. Where a path is fed an already-rewritten filter,
      record that instead.
      → RavenDB: each path calls `RavenFilterRewriter.Rewrite`, the stores' existing pre-pass, and answers a filter that
        folds to `false` without a query (`MatchesNothing`). The aggregate fallback's in-memory re-check keeps the
        original filter, which is C#. MongoDB: `AggregationPipelineBuilder.Match(Expression)`, the one hand-off to the
        driver for both the store aggregate and the public builder, folds once.
- [x] Tests red before the fix, plus a test for `RavenViewStore`'s `Contains` → `In` rewrite.
      → `Task868NullOrderingHelperLiveTests` (RavenDB): red with the production hunks reverted, green with them.
        `Task868AggregateNullOrderingLiveTests` (MongoDB): 15 red before, green after. `Task868RavenViewContainsLiveTests`
        (RavenDB views): the `Contains` → `In` and `v => true` rewrite on 5 shapes through query, count and first; each
        shape also goes to the driver unrewritten and must throw, which shows the rewrite matters.
      → Inverted gap (rule 56): `Known_gap_the_native_facet_aggregate_is_refused_by_RavenDB_for_any_filter`.
      → 16 importer suites live: green, apart from `Birko.BackgroundJobs.RavenDB`
        `TASK_664_an_abandoned_job_runs_again_after_the_claim_timeout`, which fails identically with the change reverted.
      → **Found, filed:** TASK-876 (Cosmos DB `Aggregate` ignores the filter: P1, silent), TASK-877 (RavenDB native facet
        aggregation is refused for every filter; decision needed), TASK-878 (Elasticsearch aggregation field names and
        Count metric), TASK-879 (`BackgroundJobs.RavenDB` TASK_664 test fails on HEAD).

## Out of scope

- The store and view translators (TASK-867, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
