---
id: TASK-886
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-09
depends-on: [TASK-878]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Aggregates: edge answers differ across providers; Elasticsearch view Count

## Context

Found by TASK-876 and TASK-878 (2026-11-09):
- **An ungrouped aggregate over no rows:** LINQ, MongoDB, RavenDB and now Elasticsearch return no rows; SQL and Cosmos
  DB return one row with count 0.
- **An empty `AggregateQuery`** (no aggregates, no groups): LINQ returns raw rows; Elasticsearch returns one empty row.
- **`ElasticSearchViewStore` Count** uses `value_count` on the source field, or on `_id` (aggregating on `_id` is
  disabled by default in ES 8): a non-null count, not a row count. Not measured.
- **SUM over only NULLs** (TASK-892): SQL views now answer 0 (`COALESCE`, as LINQ `Sum`); every store `Aggregate`
  answers NULL, as `AggregateHelper.LinqAggregate` does. One answer means changing `AggregateHelper` (every store).
  **Decide with the user.**
- Not measured live on Elasticsearch: `DateTimeOffset` keys and metrics, string-mapped enums.

**Decided 2026-11-10 by the user:** (1) an ungrouped aggregate over no rows returns ONE row with Count 0 (other
aggregates null) on every provider — LINQ/in-memory, MongoDB, RavenDB and Elasticsearch change; (2) SUM over only NULLs
is 0 everywhere, like LINQ's `Sum` (MIN / MAX / AVG stay null) — `AggregateHelper` and every store's `Aggregate` change.

## Acceptance criteria

- [x] Measure each per provider (rule 54).
- [x] One answer for the empty ungrouped aggregate and the empty query, on every provider, recorded; ES view Count
      counts rows, or is refused where it cannot.
- [x] Tests, red before any fix.
      → Measured 2026-11-10 live, 7 cases × sync/async per provider. Before: ungrouped over no rows gave **no row** on
        InMemory/JSON/XML, RavenDB (empty store: `DatabaseDoesNotExistException`), MongoDB and Elasticsearch (empty store:
        `index_not_found`); one row with SUM NULL on SQL and Cosmos. SUM over only NULLs was null everywhere except MongoDB
        (`Int32` 0, decimals as `Decimal128`). An empty query gave five different answers (raw rows, one empty row, a 400,
        a SQL syntax error, one columnless row per table row). Mongo and Raven ungrouped views gave no row; the ES view
        `Count` was `value_count` on `_id`, which fails on ES 8's default (`indices.id_field_data.enabled=false`).
      → The oracle first: `AggregateHelper` treats an ungrouped query as one group of every row; `AggregateMath.ComputeSum`
        returns 0 in `ResultType`; new `SumOfNoValues`, `AggregateQuery.IsGrouped` and `ThrowIfNothingToAggregate`. Then
        each provider through its producer: SQL `COALESCE(SUM, 0)` everywhere (the `sumOfNoValuesIsZero` flag removed, rule
        36; **breaking**: `SqlValueFragmentDialect.Aggregate` signature), Cosmos's hidden count removed, ES ungrouped always
        one row and stores initialised first, RavenDB initialised first, MongoDB results converted through `ResultType` with
        the LINQ empty row as fallback, Mongo views `$facet` + `$ifNull`, Raven views read the single index row or the
        empty row.
      → Decided here (recorded): an **empty `AggregateQuery`** is refused on every provider with `ArgumentException`
        ("has nothing to aggregate") before anything runs — callers wanting rows use `Read`; an **ES view fieldless Count**
        is the row count (`doc_count` / hit total), a Count of a named field stays `value_count` (as SQL `COUNT(col)`).
      → Shared `AggregateEdgeContract` (7 cases × 2 + the refusal × 2) on InMemory, JSON, XML, SQLite, PostgreSQL, MySQL,
        SQL Server, TimescaleDB, MongoDB, RavenDB: red 12–14 of 16, green 16/16; Mongo, Raven, ES and Cosmos view tests.
        Tests that pinned the old answers updated on purpose (rule 56): `SharedHelperTests.ComputeSum_EmptyOrAllNull_*`,
        `Task151AggregateHelperTests.Raw_rows_are_ordered_and_paged` (now the refusal), `Task892AggregateRenderingTests`,
        `Task885AggregateLiveTests`, `Task876AggregateFilterLiveTests`.
      → All 123 importers of changed `.projitems` live green (Redis suites need a Redis host, green without
        `BIRKO_REQUIRE_LIVE`). Consumers: Symbio's `InMemoryTimeSeriesStore` Sum over an all-null bucket is now 0 (was
        null); `StoreUsageRepository` and EventSourcing.SQL unchanged.
      → **Found, filed:** TASK-906.

## Out of scope

- Nullable SUM/MIN on Cosmos DB (TASK-885).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
