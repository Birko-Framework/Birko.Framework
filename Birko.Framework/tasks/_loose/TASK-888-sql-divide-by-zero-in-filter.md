---
id: TASK-888
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
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

# Divide by zero in a SQL filter is NULL on SQLite and MySQL; C# throws

## Context

Found by TASK-880 (2026-11-09), measured: `x.Hits / (x.Hits - x.Hits) == 0` gives NULL on SQLite and MySQL, so the
read returns no rows and the count is 0, silently. PostgreSQL and SQL Server throw (SQL Server's read did not before
TASK-880). C# throws `DivideByZeroException`. Same "SQL vs C#" family as TASK-859 and TASK-860.

## Acceptance criteria

- [x] **Decide with the user:** refuse a division whose divisor can be zero (not decidable in general), guard it
      (`NULLIF` / `CASE` raising an error) so every provider throws, or document the NULL answer.
      → **Decided 2026-11-09 by the user: every provider throws**, as C#, PostgreSQL and SQL Server do. SQLite and MySQL
        guard the divisor so a zero raises an error.
- [x] Implement and test per provider, red before the fix.
      → Measured 2026-11-09 live (reads, counts, Delete, `PropertyUpdate`, the read before `Update(filter, Action)`, SET,
        Compute, on-the-fly views): PostgreSQL (22012) and SQL Server (8134) threw everywhere. **MySQL answered NULL on
        every read path** and threw 1365 on writes only under strict `sql_mode`. **SQLite answered NULL on every path,
        Delete and Update included**, and a SET into a nullable column stored NULL silently. NULL divisors gave NULL
        everywhere, as C#'s lifted operators.
      → One hook, `SqlValueFragmentDialect.Divisor`, inside `Arithmetic` for `/` and `%` (the base is a pass-through,
        so PostgreSQL and SQL Server are unchanged); the SET parser now routes floating `/` and `%` through it too.
        SQLite: registered `birko_divisor` (rule 64) throws on a zero. MySQL has no error expression, so a zero divisor
        evaluates `LENGTH(UUID_TO_BIN('Division by zero …'))`, which raises 1411 naming the cause; non-zero rows are
        unaffected (measured), and `ComputedDivide` uses the same guard, so Compute no longer depends on `sql_mode`.
      → **`double` / `float`:** C# gives Infinity or NaN, not an exception. No provider can return those, and PostgreSQL
        and SQL Server already failed; SQLite and MySQL now fail too, per the decision, rather than answer NULL. Documented.
      → `Task888DivideByZeroTests` (201 cases per provider, against the compiled C#; a failing statement must leave every
        row unchanged). Red with the production hunks reversed: 172 of 804 (MySQL 66, SQLite 106). Green 804/804.
      → All importer suites live green (PostgreSQL `With_retry_off` passed this run). No consumer divides by a column in
        a SQL filter or SET; Symbio's `TaxRateService` Compute runs on PostgreSQL with a non-zero constant divisor.

## Out of scope

- Other arithmetic (TASK-860, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
