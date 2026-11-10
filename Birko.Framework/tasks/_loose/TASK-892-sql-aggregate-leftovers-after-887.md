---
id: TASK-892
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P2
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-09
depends-on: [TASK-887]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# SQL aggregates: NULL-only SUM, SQLite decimal views, store-level AVG truncation

## Context

Found by TASK-887 (2026-11-09):
- **The store-level `SelectAggregate`** (`AbstractConnectorBase.BuildAggregateQueryParts`) has SQL Server's integer AVG
  truncation (2 where LINQ says 2.5, silently) and returns raw provider types (NUMERIC vs bigint) with no conversion.
- **SUM over a group of only NULLs** answers NULL on all four providers; LINQ's `Sum(int?)` answers 0. Recorded as a
  deviation in TASK-887's contract, not asserted. Related to TASK-885/886.
- **SQLite view decimal SUM/AVG** use SQLite's REAL arithmetic; the registered `birko_decimal_sum` / `birko_decimal_avg`
  aggregates are never used by views.
- **AVG of an integral column into a decimal property** goes through float (about 15 significant digits); LINQ's
  `Average` into decimal is exact.

## Acceptance criteria

- [x] Reproduce each (rule 54) per provider, against LINQ.
      → Measured 2026-11-10, store `Aggregate` (41 aggregates × grouped/ungrouped × sync/async = 164 per provider)
        against `AggregateHelper.LinqAggregate`: SQLite 80 differ, PostgreSQL 106, MySQL 75, SQL Server 54.
        - **PostgreSQL returned the group key as `kind`, not `Kind`: every grouped query read a null key.**
        - **SQL Server truncated integral AVG** (1 for 1,1,2), MySQL kept 4 decimals; decimal AVG 6–10 decimals;
          SQLite decimal SUM/AVG were REAL (wrong digits) and decimal/DateTime MIN/MAX came back as strings; result types
          were provider types (Int64, NUMERIC, decimal) instead of AggregateHelper's.
        - Views (17 cases × both modes, all four): SUM over only NULLs NULL (LINQ 0); decimal AVG and integral AVG into
          decimal inexact; SQLite decimal SUM REAL; PostgreSQL long AVG into double 1 ulp off.
- [x] Store-level aggregates share TASK-887's reading rule and AVG operand (one producer); the NULL-only SUM gets one
      recorded answer; SQLite views use the decimal aggregates; AVG precision matches or is refused.
      → One producer for aggregate SQL, `SqlValueFragmentDialect.Aggregate` (store `Aggregate`, view SELECT, persistent
        DDL, on-the-fly ORDER BY): integral AVG `AVG(x * 1.0E0)` (PostgreSQL `CAST(x AS DOUBLE PRECISION)`); SQLite
        decimal SUM/AVG through `birko_decimal_sum` / `birko_decimal_avg` with `COLLATE BIRKO_DECIMAL` (without it 11 of 14
        order tests were red). One reading rule, `Fields.AggregateValue.Convert` (TASK-887's, moved), with
        `AggregateMath.ResultType` pinned to `ComputeAggregate`; store group keys and non-numeric MIN/MAX read by the
        column's own reader; store SUM/AVG of a non-numeric column refused as LINQ does.
      → Store decimal AVG on servers reads SUM and COUNT and divides in C# decimal: exact, identical to LINQ. View decimal
        AVG on servers has no exact one-column form and is refused by name (**breaking**, also for terminating values);
        SQLite computes it exactly.
      → **SUM over only NULLs: two answers, each matching its oracle** — views `COALESCE(SUM(x), 0)` as LINQ `Sum`; the
        store keeps NULL as `AggregateHelper` (and Elasticsearch, TASK-878). One answer means changing `AggregateHelper`
        for every store: moved to TASK-886 for the user's decision.
- [x] Tests, red before each fix.
      → `Task892AggregateTests` (215 per provider, 860), `Task892AggregateRenderingTests`,
        `Task892AggregateResultTypeTests`; TASK-887's tests updated on purpose (Sum int? now 0, decimal AVG refused on
        servers, rendering via `GetSelectName`), `AggregateViewDdlTests` registers `SqLiteDecimal` on its raw connection
        (rule 64). Red with the production hunks reversed: 368 of 860. Green 860/860.
      → Live: Views 3317, SQLite 2280, SQL Server 1756, MySQL 1722, PostgreSQL 1700, SQL.Tests 1112, Data.Tests 320, and
        the other 28 importers. No consumer calls the store aggregate or defines a view aggregate.
      → Recorded, not fixed: migrations' `ViewSqlGenerator` renders the portable form (no provider); double SUM/AVG depend
        on row order; integral AVG past 2^53 sums as double; exact decimal AVG in server views would need a two-column view
        field (TASK-898).

## Out of scope

- None recorded.

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
