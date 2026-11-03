---
id: TASK-706
parent: STORY-063
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — add this line while the task is blocked, keeping its status; /tasks unblock removes it
priority: P1
assignee: ai
created: 2026-10-21
depends-on: []
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: [SH-T696-01]
pr: null
github-issue: null
jira-key: null
---

# Aggregate queries put caller-supplied names and intervals into SQL text

## Context

Suspected by the TASK-696 spec harvest (22a2bb77), read from the code and **not measured**. `AbstractConnectorBase.ResolveSqlName` falls back to the raw name when it cannot resolve a property, so a caller's group-by, time-column or source string reaches the SQL text unquoted; `ToSqlInterval` puts unparseable interval text inside `time_bucket('…')` without escaping. Rules 15, 33 and 45: an identifier is resolved against metadata or refused, never interpolated. `time_bucket` also exists only on TimescaleDB, so a time-bucketed aggregate fails on every other provider.

Evidence: Birko.Data.SQL/SQL/Connectors/AbstractConnectorBase.cs:1730-1801 (SelectAggregate path).

## Acceptance criteria

- [x] Reproduce each claim (a test or a live measurement) and record the result; a claim that does not reproduce is
      closed with its evidence, never quietly dropped (rule 54).
- [x] Each reproduced defect fixed, with a regression test that fails without the fix.
- [x] The spec area that describes the behaviour updated to the fixed behaviour.

## Out of scope

- Findings of other tasks in EPIC-023.

## Human test plan

N/A — covered by automated tests once reproduced.

## Measured (2026-11-03, before the fix)

Every claim reproduced; one more sink found on the same path.

| Claim | Where | Measured |
|---|---|---|
| A source that is no property is interpolated | SQLite | `Sum` of `"length(Name)"` ran, no error |
| A group-by field that is no property is interpolated | SQLite | `"Name \|\| 'x'"` ran, no error |
| A time column that is no property is interpolated | TimescaleDB, live | `"now()"` ran, no error |
| An unparseable interval goes into `time_bucket('…')` as written | TimescaleDB, live | `"1 hour', Ts) AS b, (SELECT 1) AS z, time_bucket('1 hour"` ran, no error (with `"Ts"` quoted the database answered `column "Ts" does not exist`: the text was parsed as SQL either way) |
| `time_bucket` exists only on TimescaleDB | SQLite | `SqliteException: no such function: time_bucket`, for every bucketed query, a bad interval or time column included |
| **Found here:** `SelectAggregate` passed `query.OrderBy` to `CreateSelectCommand` unresolved, the sink TASK-110 closed for ordinary reads | SQLite | `ORDER BY random()` ran, no error |

Also: `ToSqlInterval` truncated (`(int)TotalHours`: 90 minutes became "1 hours"), and one of `TimeBucketInterval` /
`TimeColumn` alone skipped bucketing silently, where `AggregateHelper` refuses.

## Implementation plan

- `ResolveSqlName(fields, name, role)` matches a property name, then a column name, and otherwise throws
  `ArgumentException` naming the role and the name (rule 15: the metadata is the whitelist).
- `BuildAggregateQueryParts` also resolves every ORDER BY key: a group-by field to its column, an alias to the quoted
  alias, `bucket_time`; anything else is refused. It returns the resolved order, which both connectors pass on.
- Time bucket: both fields or `ArgumentException`; `TimeIntervalParser.Parse` must give a positive `TimeSpan`, else
  `ArgumentException`; the SELECT expression is the virtual `TimeBucketExpression(TimeSpan, column)`, which refuses with
  `NotSupportedException` in the base (rule 41) and is `time_bucket(INTERVAL '<seconds> seconds', column)` on
  `TimescaleDBConnector`, built from the parsed number only (`IntervalSecondsLiteral`), exact to the second.
- Not done: a bucket function for PostgreSQL (`date_bin`, 14+) or SQL Server (`DATE_BUCKET`, 2022). They refuse now,
  where they failed at the database; adding one is a feature for when a consumer asks.

## Built (2026-11-03)

`tests/Birko.Data.SQL.SqLite.Tests/Task706AggregateIdentifierTests.cs` (9) and
`tests/Birko.Data.TimescaleDB.Tests/Task706TimeBucketLiveTests.cs` (4, live on `timescale/timescaledb:latest-pg16`):
8 of 9 and 2 of 4 failed before the fix (the rest are what must keep working), all pass after. No consumer under
`Birko\Consumers` builds an `AggregateQuery`. Spec `views-and-aggregation.md` § SQL native aggregation regenerated
for the requirement (scoped; the file's `generated-at` header is left as it was).
