---
id: TASK-885
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P2
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-09
depends-on: [TASK-876]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Cosmos DB aggregates: nullable SUM/MIN, ignored time bucket, a second translator

## Context

Found by TASK-876 (2026-11-09):
- **SUM and MIN over a nullable field return null** when any value in the group is null, even with no filter
  (measured: `Sum(Score)` null where C# gives 140, `Min(Score)` null where C# gives 10). Silently wrong. MAX was right;
  AVG not measured.
- **`TimeBucketInterval` / `TimeColumn` are ignored** by the store aggregate, with no error (seen in code; rule 41).
- **`CosmosViewStore` aggregates use their own `CosmosFilterTranslator`**, not the LINQ provider the store uses, so the
  two WHERE clauses come from two translators.
- **An empty ungrouped aggregate:** in-memory `LinqAggregate` returns no rows; Cosmos DB and SQL return one row with
  count 0.

## Acceptance criteria

- [x] Reproduce each (rule 54) live, against C#.
      → Measured 2026-11-10 on the emulator against `AggregateHelper.LinqAggregate`: 24 of 26 store cases wrong per mode.
        SUM/AVG over a member holding a null came back undefined (C# skips nulls: 140 / 35); MIN came back null (null
        sorts lowest); grouped view SUM/MIN/AVG over `int?` null for any group holding a null; `TimeBucketInterval` /
        `TimeColumn` ignored (one row instead of 5 buckets). MAX was right. Cosmos facts used: `SUM(IS_NULL(x) ?
        undefined : x)` skips nulls (`??` does not); aggregates cannot be composed in one expression; date functions
        count from 1970 and return undefined before 1601.
- [x] Nullable aggregates match C# (skip nulls, as LINQ does) or are refused by name; the time bucket is translated or
      refused; record whether the view translator should become the store's (one producer) and do it or file it; decide
      one answer for the empty ungrouped aggregate across providers.
      → `BuildFunctionSql` (shared with views) emits `FUNC(IS_NULL(x) ? undefined : x)`. New internal
        `CosmosAggregatePlan`: each SUM carries a hidden non-null `COUNT`, so a SUM of no values is null as in LINQ;
        undefined aggregates and keys come back as null entries.
      → Time bucket grouped on `FLOOR(DateTimeToTimestamp(LEFT(col,19))/ms)*ms` (wall-clock, as LINQ). Refused by name
        before any request: an interval that does not divide 0001→1970 (TASK-878's rule), a non-whole-second interval, a
        non-`DateTime` column; `ArgumentException` as LINQ for half-specified, unparsable or unknown. A group holding a
        pre-1601 time is refused after the query rather than answered as the null bucket.
      → View translator: **kept, on measurement.** 34 shapes against C#: the store's LINQ provider is wrong on
        `x.Score.HasValue` (matches every row) and `string.Compare(..) > 0`, both wrong on `!(Score > 20)` (misses null
        rows); the view translator refuses 11 shapes by name. Switching now would turn two safe refusals into silently
        wrong answers. Filed: TASK-896.
      → Empty ungrouped aggregate: recorded for TASK-886 (one row, count 0, other aggregates null), not decided here.
- [x] Tests, red before the fix.
      → `Task885AggregateLiveTests` (26-case matrix incl. 14 time-bucket cases, refusals with no request sent, pre-1601,
        TASK-886 recording; plus 3 offline SQL-text tests) and `Task885CosmosViewAggregateTests`: red 5/5 and 2/2 with
        the production hunks reverted, green 8/8 and 2/2. `CosmosAggregationHelperTests` and `Task876AggregateQueryTests`
        updated to the new text on purpose.
      → Live: BackgroundJobs 28, Data.CosmosDB 380, ViewModel 5, Views 120, Migrations 44, Sync 4, Workflow 8.
      → Consumers: no store aggregate caller; Symbio's `CosmosViewStore` grouped views now return values for nullable
        members where they returned null.
      → **Found, filed:** TASK-896 (P1: Cosmos store reads through the LINQ provider are silently wrong on `HasValue`,
        negated orderings over null, `string.Compare`), TASK-897 (aggregate result types, DateTime MIN/MAX as text,
        case-sensitive names, non-numeric SUM not refused).

## Out of scope

- The filter (TASK-876, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
