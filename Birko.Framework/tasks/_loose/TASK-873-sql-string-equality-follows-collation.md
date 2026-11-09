---
id: TASK-873
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-08
depends-on: [TASK-869]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# A SQL string `==` follows the column collation, not C#'s ordinal equality

## Context

Found by TASK-869 (2026-11-08). `x.Name == "abc"` renders `Name = @p` and follows the collation: case-insensitive on
MySQL and SQL Server, accent-insensitive on MySQL, while C# `==` is ordinal. It is the same gap TASK-869 left on the
no-argument LIKE forms by the user's decision (option A). Since TASK-869, `string.Equals(x.Name, v,
StringComparison.Ordinal)` gives C#'s answer on every provider.

## Acceptance criteria

- [x] Measure per provider (live): `==`, `!=`, `IN` lists of strings, on mixed-case and accented data, against C#.
      → Measured 2026-11-09 with a shared contract of 134 shapes (read, count, filtered update and delete vs C#): SQLite
        0 and PostgreSQL 0 differed; **MySQL 97, SQL Server 113.** Case on both; accents on MySQL; precomposed vs
        decomposed é, `ß`/`ss` and ligatures on both; trailing spaces and `''` = `' '` on SQL Server (padded under every
        collation, BIN2 included); Kelvin `K` and no-break space on MySQL. MySQL `utf8mb4_bin` is PAD SPACE,
        `utf8mb4_0900_bin` is not. Not framework-created but measured: a PostgreSQL nondeterministic ICU column and a
        SQLite NOCASE / RTRIM column are not exact; a citext column bound with Npgsql's text parameter already was.
- [x] **Decide with the user:** keep `==` collation-following and document it (as TASK-869's option A), or make it
      ordinal. Record the decision.
      → **Decided 2026-11-09 by the user: the default is exact.** Plain `==` / `!=` (and string `IN` lists) on a string
        column give C#'s ordinal answer on every provider, not the collation's. Breaking on MySQL and SQL Server.
- [x] Tests per provider, red before any fix.
      → One producer, `SqlValueFragmentDialect.OrdinalTextEquality`: `==` and IN render `(plain AND exact)` so the plain
        term keeps the index seek; `!=` and NOT IN render `NOT (plain AND exact)`; TASK-859's NULL legs unchanged. Exact
        forms per provider: SQL Server `(x + N'|') COLLATE Latin1_General_100_BIN2` (the terminator defeats padding; a
        value of 4000+ characters is also offered uncut, because SQL Server cuts a non-MAX concatenation at 4000; two
        columns compare under an `NVARCHAR(MAX)` cast); MySQL `CONVERT(x USING utf8mb4) COLLATE utf8mb4_0900_bin` (NO
        PAD, any column charset, 8.0+); PostgreSQL `(x)::text COLLATE "C"`; SQLite `(x) COLLATE BINARY`.
        `DataBase.MarkOrdinalText` marks every string Equal/In leaf the parser builds (comparisons, value fragments,
        column vs column, set `Contains`, `IsNullOrEmpty`). Hand-built conditions and rules keep the collation (TASK-895).
      → Index plans measured (100k rows): the exact term alone is a full scan on SQL Server, MySQL and PostgreSQL; the
        `plain AND exact` form seeks everywhere. `Task873IndexPlanTests` pins it (9 of 9 red without the plain term).
      → **Independent review:** correctness held on all four (55 extra shapes incl. NULL legs, TASK-861/863/866 forms,
        column vs column, lists, long values; Symbio's PostgreSQL shapes keep their index plans). It found one blocker:
        the first SQL Server form cast every IN item to `NVARCHAR(MAX)`, so a 1000-value IN took 18.3 s (was 36 ms) and
        2000 values timed out. Fixed by the terminator without the MAX cast: 1000 values 67 ms, 2000 values 232 ms;
        NOT IN within 10% of before. Its doc finding on citext was corrected.
      → Tests: `StringEqualityContract` (143 per provider, incl. a 20,000-row 1000-value IN/NOT IN budget test, red at
        14–17 s on the MAX form, and values of 3999/4000/4001 characters), `Task873ViewStringEqualityTests` (on-the-fly and
        persistent), `Task873IndexPlanTests`, `Task873OrdinalTextEqualityTests` (9). Red on HEAD: MySQL 97, SQL Server 113;
        views SQL Server 13, MySQL 10; SQLite NOCASE/RTRIM 3/3. Green 143/143 on all four.
      → Live: SQL Server 1755, SQL.Tests 1018, SQLite 2279, MySQL 1721, Views 2441, PostgreSQL 1698/1699 (TASK-893).
      → Consumers: Symbio runs PostgreSQL (rows unchanged, index still used); on its MySQL/SQL Server configurations,
        lookups such as `u.UserName == request.Login` become case-sensitive. SQLite consumers unchanged.
      → **Found, filed:** TASK-895.

## Out of scope

- LIKE forms and explicit comparisons (TASK-869, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
