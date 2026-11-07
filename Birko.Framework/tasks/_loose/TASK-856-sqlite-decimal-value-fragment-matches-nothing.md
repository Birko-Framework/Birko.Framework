---
id: TASK-856
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P2
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-07
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# On SQLite, a decimal value fragment in IN or `=` matches nothing

## Context

Found by TASK-849 (2026-11-07), measured on SQLite. `small.Contains(x.Amount + 0m)` counted 0 rows where 5 match. A
decimal computed operand (a TASK-213 value fragment) compared with decimal parameters silently returns the wrong
answer.
- SQLite stores decimals through Birko's decimal handling (`SqLiteDecimal`), likely as text with a custom comparison.
- An arithmetic fragment yields a REAL, or a differently formatted value, that never equals the bound text.
- The answer is wrong and nothing is raised (rules 26 and 44).

## Acceptance criteria

- [ ] Reproduced first: `Contains` and `==` with decimal value fragments (`x.Amount + 0m`, `x.Amount * 1m`,
      `Math.Round(...)`) against decimal parameters, on SQLite. Record what matches against the C# oracle. Check the
      other providers for the same shapes.
      → **SQLite, before** (9 rows, including two values a double cannot tell apart, a NULL and a 0):
        - `Small.Contains(x.Amount + 0m)` and `* 1m`: 0 rows instead of 3.
        - `== 10.10m`, `+ 0.20m == 10.30m`, `/ 2m`, `% 3m`: 0 rows.
        - `+ 0m < 6m`: every row instead of 3; `> 6m`: 0 instead of 6.
        - `(x.Optional ?? 0m) < 8m` and `CASE … < 8m`: every row.
        - `x.Amount + 0m == x.Amount`: 7 rows instead of 9.
      → PostgreSQL, MySQL and SQL Server (live) were correct for every shape.
      → **Cause:** `(Amount + 0)` evaluates to a REAL, and SQLite ranks every REAL below every TEXT, so it never equals
        a decimal bound as TEXT. `COALESCE` and `CASE` keep the TEXT but drop the column's `BIRKO_DECIMAL` collation, so
        they compare byte-wise. An inlined `0` is an INTEGER.
      → `Math.Round` is dropped by the parser on every provider → TASK-857, not this task.
- [x] Either the comparison is made exact (both sides compared through Birko's decimal comparison), or a decimal value
      fragment that SQLite cannot compare exactly is refused by name before the statement runs. Never a wrong count.
      → **Made exact.** The parser still writes the portable fragment into `Condition.Name`, so other providers run what
        they ran before.
      → Each fragment condition also carries a re-render (`NameFragment`, `FieldFragment`), which `AbstractConnectorBase`
        applies through a virtual `FragmentDialect`. Only SQLite overrides it (`SqLiteValueFragmentDialect`):
        - decimal arithmetic through the `birko_decimal_*` functions (`birko_decimal_mod` added);
        - decimal constants written as text (digits, `-` and `.` only);
        - `COLLATE BIRKO_DECIMAL` on decimal comparisons.
      → That covers IN, `=`, `<>`, `<`, `>`, CASE tests and column vs column. A decimal fragment converted to another
        type is refused by name.
      → `Condition.Copy` carries the re-render, and the IN-list planner and `ReturnSingleSubCondition` use it.
- [x] Regression tests red before the fix; the SQLite and Birko.Data.SQL importer suites stay green.
      → `DecimalValueFragmentComparisonTests` (27 tests): with SQLite back on the portable dialect, 23 of 26 failed.
        Removing the copy from `Condition.Copy` and from `ReturnSingleSubCondition` each failed its own test.
      → All 35 importer suites green live.
      → **Independent review: no blockers, no should-fix.**
        - 38 extra SQLite edge shapes against the oracle: negatives, nullable decimals, mixed int/double, beyond
          double precision, scale, column vs column, collapses, delete, chunked update.
        - Other providers are byte-identical, and there is no injection path.
        - Its 3 nits went to TASK-858.
      → **Found and filed:**
        - Parser `Math.Round` / `Math.Abs` / unary minus → TASK-857 (P2).
        - `IsField` and SQLite diagnostic leftovers → TASK-858.
        - NULL comparisons diverging from C# on every provider, older than this task → TASK-859 (P2).

## Out of scope

- Decimal storage itself (TASK-826 established exact storage).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
