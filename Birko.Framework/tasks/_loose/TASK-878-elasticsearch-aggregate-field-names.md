---
id: TASK-878
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

# Elasticsearch store aggregation: GROUP BY by property name returns nothing; Count reads 0

## Context

Found by TASK-868 (2026-11-09), measured live but not fully diagnosed: `GroupByFields = ["Bucket"]` (the C# property
name) returned no groups; the camelCase `"bucket"` returned the right groups, but a `Count` metric read 0 with source
`"A"` or `"a"`. There is no live Elasticsearch aggregate test.

## Acceptance criteria

- [x] Reproduce (rule 54) live, each aggregate kind, property-name and field-name forms, against C#.
      → Measured 2026-11-09 on ES 7.17.25, sync and async, against `AggregateHelper.LinqAggregate` over the same rows:
        93 of 99 cases wrong on HEAD, silently except where noted. Ungrouped Count read 0; Sum/Avg/Min/Max by property
        name read 0 or null; GROUP BY a property name returned no groups, by a text field's stored name a 400; two GROUP
        BY fields threw; null group keys were dropped; a time bucket on the property name returned no buckets; Count in
        a group read 0; unknown names, a half-specified or unparsable interval ran silently (the interval as 1 hour).
      → Root cause, three defects: (1) property names were sent verbatim, and ES answers an aggregation over an
        unmapped field with nothing rather than an error (a string also needs `.keyword`); (2) grouped metrics were
        never sent: NEST only carries sub-aggregations when the aggregation converts to its container implicitly
        (confirmed from the request body; views already used that form); (3) Count was a value count of its source
        field, not a row count.
- [x] Names resolve through the store's mapping (one producer), or an unknown name is refused by name; metrics match C#.
      → Names are property names, as SQL and the in-memory path read them; the stored field name is accepted too. One
        lookup: `ElasticSearch.SortMember` generalised into `ResolveMember(type, key, role)`, used by sorts and
        aggregates, then `StoredFieldName` / `ExactFieldName`.
      → New internal `StoreAggregatePlan` for both stores: refuses bad names before any request, builds one composite
        aggregation (groups and the time bucket, `missing_bucket` for null keys), reads every page, counts rows
        (`doc_count`, or `hits.total`), returns a sum of no values as null, converts keys and metrics to the member's
        C# type. Refused by name: GROUP BY a `[Text]` or object member, Min/Max of a string or Guid, Sum/Avg of a non-
        number, a non-`DateTime` time column, an interval that does not divide year 1 to the epoch (a week). A group
        whose `.keyword` skipped a value past `ignore_above` throws rather than counting it as null.
      → The old builder, single-page parser, `ParseToTime` (silently 1 hour) and `TruncationError` were removed as
        unreachable (rule 53). An ungrouped query over no rows now returns no rows, as LINQ, MongoDB and RavenDB
        (SQL and Cosmos DB return one row: TASK-886).
- [x] Live tests, red before the fix.
      → `Task878AggregateLiveTests`: 34 shapes × sync/async against LINQ, 8 × 2 refusals matching LINQ's, the by-name
        refusals, paging (7 groups through pages of 2), the `ignore_above` refusal. Red with HEAD's production files: 93
        of 99. Green 99/99.
      → All 8 importers of `Birko.Data.ElasticSearch.projitems` live: green (ElasticSearch 603, Views 32, ViewModel 6,
        BackgroundJobs 22, Migrations 40, Sync 13, Health 6, Workflow 9).
      → Consumers: none calls the store aggregate (Affiliate builds raw NEST aggregations).
      → **Found, filed:** TASK-886 (ES view-store Count is a non-null count; an empty `AggregateQuery`; one answer for an
        empty ungrouped aggregate across providers). `docs/specs/views-and-aggregation.md` still names the removed
        methods; a scoped `/specs regen` will show it.

## Out of scope

- Null orderings (TASK-868, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
