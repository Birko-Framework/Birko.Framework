---
id: TASK-877
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P2
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

# RavenDB native facet aggregation is refused for every filter

## Context

Found by TASK-868 (2026-11-09), measured live: `Aggregate` with one GROUP BY field and no time bucket sends a facet
query against the collection, and RavenDB refuses it for any filter, plain ones included: `InvalidQueryException: Facet
query must be executed against static index`. Loud, not wrong rows. Pinned by the inverted test
`Known_gap_the_native_facet_aggregate_is_refused_by_RavenDB_for_any_filter`.

## Acceptance criteria

- [x] **Decide with the user:** fall back to the LINQ aggregate (already correct for several GROUP BY fields), or deploy
      a static index for the facet query.
      → **Decided 2026-11-09 by the user: fall back to the LINQ aggregate** (the client-side path already used for
        several GROUP BY fields). Considered and not chosen: facets or map-reduce on a static index (index deployment),
        dynamic group-by (Count and Sum only, filter semantics unmeasured).
- [x] Implement; invert the known-gap test and remove `IsNativeFacetGap` (rule 56).
      → Measured 2026-11-09 live before the change: one GROUP BY field, 11 filters × 4 aggregate sets × sync/async = 88
        cases; 72 refused (`Facet query must be executed against static index`), plain filters and no filter included;
        the other 16 passed only because the filter folded to false and no query was sent.
      → Every GROUP BY shape now takes the LINQ path (`RavenFilterRewriter` → `MatchesNothing` → query →
        `AggregateHelper.LinqAggregate`). `NativeFacetAggregate(Async)` and the public `FacetAggregationHelper` were
        unreachable and are deleted (rule 53; no caller in the framework or consumers). The null group key is `null`, as
        in LINQ (the facet path called it "NULL_VALUE"). An aggregate over no rows returns 0 rows (TASK-886).
      → Known limit, documented in `Birko.Data.RavenDB/CLAUDE.md`: the LINQ path loads every matching document in one
        query (2,600 measured, all counted); it never paged.
      → Tests: `IsNativeFacetGap` removed and the known-gap test inverted into
        `A_single_field_aggregate_with_a_plain_filter_answers_what_CSharp_matches` (rule 56); new
        `Task877SingleFieldAggregateLiveTests` (the 88 cases against LINQ, OrderBy/Offset/Limit over groups, 2,600
        documents). Red with the production hunks reversed: 5 of 6, 72 of 88. Green 6/6, 0 of 88.
      → Live: Data.RavenDB 332/333 (the one failure was the new test's decimal-scale comparison, fixed; Task877/868 6/6
        after), BackgroundJobs 25, Localization 11, Migrations 40, ViewModel 4, Views 15, Sync 5, Workflow 8.
        `docs/specs/views-and-aggregation.md` still lists the deleted helper; a scoped `/specs regen` will show it.

## Out of scope

- Null orderings (TASK-868, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
