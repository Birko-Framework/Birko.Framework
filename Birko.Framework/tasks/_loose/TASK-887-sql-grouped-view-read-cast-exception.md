---
id: TASK-887
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P1
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-09
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# A grouped `SqlViewStore` read throws a cast exception on every SQL provider

## Context

Found by TASK-880 (2026-11-09), measured on SQLite, PostgreSQL, MySQL and SQL Server: a view
`Select(o => o.Kind, v => v.Kind).GroupBy(o => o.Kind).Sum(o => o.Big, v => v.Total)` over one table (no join), read with
`QueryAsync(v => v.Kind == "a")`, on-the-fly and persistent, throws `InvalidCastException` (`ArgumentException` on
SQLite). The transform appears to read `Kind` from the SUM column: the `GetTableFields()` order does not match the
SELECT order. `AggregateViewDdlTests`, which use a join, pass. Loud, but grouped views over one table do not work.

## Acceptance criteria

- [x] Reproduce (rule 54) per provider: grouped views with and without a join, different property declaration orders,
      several aggregates, on-the-fly and persistent.
      → Measured 2026-11-09: 25 aggregate cases × key first/last × with/without join × on-the-fly/persistent, read by
        `QueryAsync`, sync `connector.Select` and `CountAsync`, on all four providers. **The suspected cause was wrong:**
        key order, join and mode changed nothing. What decided it was the aggregate and its source type:
        - SUM/MAX of `long`, `short`, `double` were read with `GetString` (InvalidCast / ArgumentException everywhere);
          AVG into `double` / `long` / `int` was read with `GetDecimal`.
        - **Silent:** COUNT of a group whose key is NULL answered 0 (expected 1) on all four: a portable `Count()`
          counted the first declared column, which was nullable.
        - Correct already: SUM/MIN/MAX of int, decimal, string, DateTime, int?; AVG of decimal; `CountAsync`.
      → Root cause: `FunctionField.CreateFunctionField` chose the reader from the function name and a closed list of
        source classes; anything else fell through to `StringFunction`. The server's result type is not the source's
        anyway (SUM(BIGINT) is NUMERIC on PostgreSQL, SQL Server's AVG of integers is an integer).
- [x] Column order comes from one producer for the SELECT and the reader transform.
      → `View.GetSelectFields`, `GetPersistentViewSelectFields` and `GetTableFields` now enumerate one
        `View.Columns()` → `Table.Columns()` sequence, and `Table.SelectExpression` renders a table's and a view's
        projection (rule 16). Reading by ordinal stays: it was never the defect.
      → One reading rule: `FunctionField.Read` is sealed; a numeric view property gets the server's value converted
        exactly to its type (a fraction into an integral property or NULL into a non-nullable one throws naming the
        aggregate and property; out of range `OverflowException`); a non-numeric MIN/MAX is read by the source column's
        own reader (`AbstractField.ReadingInto`). COUNT/SUM/AVG into a non-numeric property and SUM/AVG of a
        non-numeric column are refused when the view is built (rule 41).
      → AVG of an integral column renders `AVG(col * 1.0E0)`: otherwise SQL Server answers 2 where LINQ answers 2.5,
        and the fix would have turned a loud failure silent. `Count()` without a source counts the primary key (else
        the first NOT NULL column, else it refuses). **Breaking:** the sealed `Read`, the build-time refusals, an
        integral AVG into an integral property refuses a fraction.
- [x] Tests, red before the fix.
      → `Task887GroupedViewReadTests` (live, 200 cases per provider, against LINQ) and `Task887FunctionFieldTests`
        (15 offline). Red with the production hunks reversed: 392 of 800 (98 per provider), 14 of 15. Green 800/800,
        15/15.
      → 35 importers live: green apart from PostgreSQL `DeadlockRetryLiveTests.With_retry_off`, which fails with the
        change reverted too (TASK-893). No consumer defines an aggregate view.
      → **Found, filed:** TASK-892 (SUM over only NULLs is NULL where LINQ says 0; SQLite view decimal SUM/AVG use REAL;
        the store-level `SelectAggregate` truncates integer AVG on SQL Server and returns raw provider types; AVG of an
        integral column into a decimal goes through float).

## Out of scope

- Read-error propagation (TASK-880, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
