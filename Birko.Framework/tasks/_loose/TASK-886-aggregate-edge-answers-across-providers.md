---
id: TASK-886
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
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

- [ ] Measure each per provider (rule 54).
- [ ] One answer for the empty ungrouped aggregate and the empty query, on every provider, recorded; ES view Count
      counts rows, or is refused where it cannot.
- [ ] Tests, red before any fix.

## Out of scope

- Nullable SUM/MIN on Cosmos DB (TASK-885).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
