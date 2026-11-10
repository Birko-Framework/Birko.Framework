---
id: TASK-906
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P2
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-10
depends-on: [TASK-886]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Aggregates: leftovers after TASK-886

## Context

Found by TASK-886 (2026-11-10):
- **RavenDB view index: MIN of {3, null} gives null and AVG gives 1.5**, where C# gives 3 and 3 (measured; nulls are
  not skipped). Silently wrong.
- **MongoDB store `Aggregate` ignores `OrderBy`, `Offset`, `Limit` and time bucketing**, and leaves non-numeric MIN/MAX
  types as the driver maps them.
- **InfluxDB native Flux path** (async, single aggregate, no filter) not measured: probably no row for an ungrouped query
  over nothing, and drops a group whose field is all null.
- **Mongo view decimal sums:** the empty-row Sum is an `Int32` 0 deserialised into the view property; not measured for a
  `decimal` view property.
- `docs/specs/views-and-aggregation.md` still describes "no aggregates and no grouping returns raw rows"; needs a
  `/specs regen`.

## Acceptance criteria

- [x] Reproduce each (rule 54) against `AggregateHelper.LinqAggregate`.
- [x] Each matches the oracle or is refused by name.
- [x] Tests, red before each fix.
      → Measured 2026-11-10 live against `AggregateHelper.LinqAggregate`:
        (1) RavenDB view index: MIN of {3, null} null, AVG 1.5; with a negative value next to nulls the reduce threw
        "Object must be of type Int64", erroring the index so every view query threw. `RavenViewTranslator` now maps
        `X_Count` as non-null count, AVG as `Sum/Count` (null when no values), MIN/MAX over non-null values (re-reduce safe).
        `Task906RavenViewNullAggregateLiveTests` red 2/2 → green; TASK-886's recorded gap inverted (rule 56).
        (2) MongoDB store `Aggregate`: red 36 of 38 — OrderBy/Offset/Limit and the time bucket ignored, enum keys/MinMax as
        Int32, grouping on `Guid` or a differently-cased name grouped everything as null, unknown fields not refused.
        `StoreAggregationHelper` resolves names like `AggregateHelper` (case-insensitive, same exceptions) at the stored
        element, buckets the stored date (refusing non-DateTime columns, non-UTC-BSON dates, non-millisecond and
        non-aligned intervals, as Cosmos and ES), reads keys and Min/Max with the member's serializer, and pages through
        `AggregateHelper.ApplyOrderingAndPaging`. `Task906MongoStoreAggregateLiveTests` 36/38 red → 38/38.
        (3) **InfluxDB native Flux path: 24 of 27 wrong** (one row per stored point, Count of field points, group-by on a
        field ignored, buckets on write time, no row over nothing, decimal summed as double, unparsable interval as 1 hour).
        The native path is removed with `FormatFluxInterval` (rules 36, 53); every query aggregates through
        `AggregateHelper`. `Task906InfluxAggregateLiveTests` 27/27. Trade-off: no server-side aggregation.
        (4) MongoDB view decimal sums: no defect (12 cases pinned).
        (5) Spec: not hand-edited (the specs skill forbids it); `docs/specs/views-and-aggregation.md` needs a
        `/specs regen` (raw-rows scenarios, Raven AVG, Mongo and Influx store aggregates).
      → All 11 importer suites live green. No consumer uses these aggregates.
      → **Found, filed:** TASK-907 (string MIN/MAX order: MongoDB binary vs the culture-sensitive oracle; enum stored as
        text; exact native Flux aggregation).

## Out of scope

- The decided empty and NULL answers (TASK-886, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
