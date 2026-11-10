---
id: TASK-897
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-10
depends-on: [TASK-885]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Cosmos DB aggregates: result types, DateTime MIN/MAX as text, case-sensitive names

## Context

Found by TASK-885 (2026-11-10):
- Results come back as long/double, not the member's C# type (TASK-878 converts to the C# type on Elasticsearch).
- MIN/MAX of a `DateTime` returns its stored text, which also sorts wrongly when fractional seconds are mixed
  (`"…00.5Z"` < `"…00Z"`).
- GROUP BY and aggregate names are sent verbatim and case-sensitively; LINQ matches any case.
- SUM/AVG of a non-numeric member is not refused (LINQ throws `NotSupportedException`).

## Acceptance criteria

- [x] Reproduce each (rule 54) live against LINQ.
      → Measured 2026-11-10 on the emulator against `AggregateHelper.LinqAggregate` (same CLR type and value): 15 of 15
        store shapes wrong in both modes. Numbers came back as long/double, Guid and DateTime as text, enums as numbers;
        enum, Guid and DateTime keys split into wrong groups; DateTime MIN/MAX as text sorting `…00.5Z` before `…00Z`
        (also in grouped views); a group named `grp` read no field; decimal SUM 0.30000000000000004; SUM/AVG of a string
        not refused. Cosmos string MIN orders "B" before "a"; aggregate compositions are rejected (400); `DateTimeToTicks`
        is inexact and converts offsets.
- [x] Each matches LINQ or is refused by name.
      → One name lookup `CosmosAggregationHelper.ResolveMember` (any case, LINQ's exception and message); types from
        TASK-892's `AggregateMath.ResultType`; results read from the `JsonElement` like a read (COUNT `int`, checked
        SUM/AVG conversion, decimal AVG as hidden SUM/COUNT divided in C# decimal, decimal SUM rounded to 15 significant
        digits — the server adds in double).
      → DateTime MIN/MAX compare a `WallClockKey` built from string functions (wall clock, fraction padded to 7 digits,
        suffix): parses back to the stored value, works before 1601. Store DateTime GROUP BY groups the wall clock.
      → Refused by name before any request, as LINQ: SUM/AVG of a non-numeric member (LINQ's message), MIN/MAX of a
        string, `DateTimeOffset`, `TimeSpan`, `char`; non-scalar group keys. **Breaking:** result types change (COUNT
        `int`); `AggregateResult.GetValue<T>` callers are unaffected.
- [x] Tests, red before the fix.
      → `Task897AggregateTypesLiveTests` (async/sync matrices, decimal AVG, live refusals with no request sent, 10
        offline) and `Task897CosmosViewAggregateTests`: red 14/15 and 2/2 with production reverted, green 15/15 and 2/2.
        TASK-885's test updated on purpose (string MIN/MAX now refused).
      → Live: CosmosDB 413, Views 153, ViewModel 5, BackgroundJobs 28, Migrations 44, Sync 4, Workflow 8. No consumer
        aggregates on Cosmos; `StoreUsageRepository` reads through `GetValue`.
      → View-store leftovers (decimal SUM/AVG through the serializer's double; DateTime GROUP BY on stored text) added to
        TASK-899.

## Out of scope

- Nullable aggregates and the time bucket (TASK-885, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
