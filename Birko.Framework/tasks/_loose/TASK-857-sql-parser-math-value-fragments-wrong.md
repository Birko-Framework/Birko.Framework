---
id: TASK-857
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P2
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-07
depends-on: [TASK-856]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# The SQL filter parser mistranslates `Math.Round`, `Math.Abs` and unary minus, on every provider

## Context

Found by TASK-856 (2026-11-07), measured on SQLite, PostgreSQL, MySQL and SQL Server. Grouped, because all three are
the same producer: the SQL filter parser's value-fragment translation (`DataBase.ParseConditionExpression`, TASK-213
fragments).
- **`Math.Round(x.Amount, 1, MidpointRounding.ToEven) == 10.1m`** drops the Round and renders `Amount = 10.1`: 3 rows
  expected, 2 returned on every provider. Silent.
- **`Math.Abs(x.Amount - 10m) < 1m`** returns 0 rows instead of 3. Silent.
- **`-x.Amount == -7m`** throws "Condition name cannot be null". Loud, but only by accident.

A translator that cannot express a node must refuse it (rules 41–44), and the first two violate that.

## Acceptance criteria

- [ ] Each shape reproduced per provider against the C# oracle, together with any other `Math.*` or unary operator
      the parser accepts. Record what each returns.
      → Measured 2026-11-07; the same on SQLite, PostgreSQL, MySQL and SQL Server (live):
        - `Round(Amount,1,ToEven)==10.1`: 1 row instead of 3. With `AwayFromZero`: 1 instead of 4.
          `Round(Amount,0,ToEven)==10`: 0 instead of 8. With `!=`: 13 instead of 11.
        - `Abs(Amount-10)<0.2`: 0 instead of 5. `Abs(Amount)==10.05`: 1 instead of 2.
        - `decimal.Negate(Amount)==-7`: 0 instead of 1. Unary minus threw "Condition name cannot be null".
        - `Floor`, `Ceiling`, `Truncate`, `Min`, `Max`, `Sign`, `Pow`, `Sqrt`, `Round` without a mode, `Round` on a
          double, and `~x.Hits` all ran and returned wrong rows (`~` became `NOT (Hits = -3)`).
- [x] Each is translated exactly (`Round` with an explicit mode as TASK-826's `Compute` does, `ABS`, unary minus), or
      refused by name with `NotSupportedException` before the statement runs. Never a wrong count. On SQLite, decimal
      operands go through TASK-856's fragment dialect.
      → **Translated as value fragments:**
        - a decimal `Round` with explicit `ToEven` or `AwayFromZero`, through each connector's TASK-826 `ComputedRound`
          (one producer; `birko_decimal_round` on SQLite);
        - `Abs` → `ABS` (`birko_decimal_abs` on SQLite);
        - unary minus and `decimal.Negate` → `(-(x))` (`birko_decimal_sub('0.0', x)` on SQLite);
        - unary plus passes through.
      → **Refused by name** (`NotSupportedException`, before the statement runs): every other Math / MathF / decimal
        call, `Round` without a mode or with another mode, digits read from the row, `Round` on a double, and integer
        `~`. Round's rules live in one place, `ComputedValue.ReadRound`, which computed sets also use.
      → Every connector re-renders fragments through its own dialect. The IS NULL legs of a nullable
        column-vs-column comparison carry their rendering.
      → Generated parameter names cap the field stem at 64 characters, keeping the unique `{index}_{count}` suffix.
        SQL Server refused names over 128 characters for long Round fragments.
- [x] Regression tests per shape and provider (live where available), red before the fix.
      → New shared `tests/Shared/StoreContract/MathFilterContract.cs` (55 cases), run in the SQLite, PostgreSQL, MySQL
        and SQL Server suites; plus a parameter-name test in `SqlBuilderContextTests`.
      → Red proof: on the old production code, 49 of 53 failed on every provider (the 2 nullable shapes were added
        after), and the name test failed.
      → All 35 importer suites green live, plus InMemory and JSON (for `ComputedValue`).
      → **Found, filed:** the parser silently drops other calls and casts (`Substring`, `Convert.ToInt32`,
        `(decimal)` on an int, meaning integer division on PostgreSQL and SQL Server) and SET unary minus →
        TASK-860 (P1).

## Out of scope

- The SQLite decimal comparison itself (TASK-856).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
