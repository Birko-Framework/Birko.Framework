---
id: TASK-870
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-08
depends-on: [TASK-860]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# SQL translation leftovers after TASK-860: case folding, decimal division, SET `.Date`

## Context

Found by TASK-860 (2026-11-08). None of these is measured yet, except where noted:
- **`ToLower` / `ToUpper` on SQLite** fold ASCII only, while C# folds Unicode. A non-ASCII name may not match.
- **Decimal `/` in a filter fragment** uses each provider's native division, not TASK-826's `ComputedDivide`, so the
  precision of the result differs by provider.
- **SET `.Date` on SQL Server** renders `DATE()`, which SQL Server rejects. It fails loudly, so no wrong rows.
- **`Replace` on SQL Server** depends on the collation.
- **SQLite integer overflow in SET** (found by TASK-860's fix): `SET Hits = Hits + 1` past `int.MaxValue` stores the
  value silently, and every later read of the row throws. That is plain arithmetic overflow, not a cast. TASK-860
  refuses narrowing SETs on SQLite for the same reason (`StoresIntegerOverflowLoudly`).
- **`ToLower` / `ToUpper` before a comparison** (TASK-869): `x.Col.ToLower() == v` and `x.Col.ToLower().Contains(..)`
  use SQL's `LOWER` / `UPPER`, which group characters differently from .NET (measured by TASK-869: 13 to 72 of 240
  cases off per provider). `StringComparison.OrdinalIgnoreCase` is now the exact form.

## Acceptance criteria

- [x] Measure each item per provider against the C# oracle (rule 54). Record the result.
      → Measured 2026-11-09, shared `TranslationLeftoversContract`, SQLite and PostgreSQL 16, MySQL 8.4, SQL Server 2022
        live, against C#:
        - Folds (`ToLower`/`ToUpper`/`*Invariant`, SET and filters, 34 texts): SQLite 12–35 off (ASCII only); servers
          0–19 off on rare letters (Kelvin, `ſ`, Turkish `İ`/`ı`, `ȿ`); `č`, `ä`, `é` fold correctly everywhere.
        - Decimal `/` in a filter (32 shapes): SQLite exact; PostgreSQL 18, MySQL 19, SQL Server 22 off. A SET into a
          DECIMAL(28,10) column stored wrong values silently (SQL Server and MySQL 10 of 13 rows). `%` exact everywhere.
        - SET `.Date`: SQLite silently wrong (stored text that `x.At == day` never matched); SQL Server failed.
        - `Replace` in SET (11 shapes): a null new value stored NULL (C# removes); an empty old value ran (C# throws);
          SQL Server also 8 collation cases (case-insensitive, `ß` = `ss`).
        - SQLite integer SET overflow: 12 of 12 shapes (incl. `Increment`) stored silently; later reads threw.
- [x] Each one that differs is translated exactly or refused by name. An item that matches C# is recorded and closed.
      → Folds on SQLite: registered C# function `birko_case_fold` (current culture for `ToLower()`, invariant for the
        invariant forms), registered from `SqLiteDecimal.Register` (rule 64). Servers keep `LOWER`/`UPPER`: their gap is
        pinned as per-provider budgets (rule 56) and the decision is TASK-889.
      → Decimal `/`: one decision, `TerminatingReciprocal`, for filters and SET. A constant divisor whose reciprocal
        terminates (±2^i·5^j, ≤ 10 digits: `/ 2m`, `/ 4m`, `/ 2.5m`, `/ 100m`) becomes an exact product on every provider;
        any other decimal division (`/ 3m`, a column) is refused by name on the servers and translated on SQLite. The
        first version refused every division; the independent review measured shapes HEAD answered exactly
        (`x.Amount / 2m == 5m`), and the refusal was narrowed (rule 57).
      → `.Date` in SET: a `DateOf` hook (`CAST(x AS DATE)`; SQLite the text a `DateTime` parameter binds as).
      → `Replace`: an empty, null or row-read old value refused (C# throws); a null new value removes; SQL Server
        compares under `Latin1_General_100_BIN2`.
      → SQLite overflow: `birko_checked_int(value, min, max)` around integer arithmetic, negation and `Increment`;
        plain `Hits + 1` works and fails only on overflow, as on the servers. SQLite SET decimal arithmetic now uses the
        decimal functions (it went through a double: `Amount * 0.4` stored `-2.8000000000000003`).
- [x] Tests per item, red before the fix.
      → `TranslationLeftoversContract` (four providers), `Task870TranslationLeftoverTests` (31),
        `Task870ViewCaseFoldTests` (48). Red with the production hunks reversed: contract SQLite 27/45, servers 3–12,
        views 29/48; division and SET-division tests red on HEAD on all three servers. Green: contract + CallCast 199/199
        per provider. `PropertyUpdateIncrementEndToEndTests` asserted `Hits = Hits +` and now `birko_checked_int(`.
      → **Independent review** (no blocker): measured SET increments at the limits and on NULL, the SQLite fold through
        LIKE escaping and TASK-859/861/863/866 shapes, rule 64 registration on every connection, FisData's `Path.Replace`
        on all four. Its one finding, the blanket division refusal, was narrowed as above.
      → Live: SQLite 2136, MySQL 1578, SQL Server 1612, PostgreSQL 1555/1556 (known `With_retry_off`), SQL.Tests 1009,
        Views 1342.
      → **Breaking:** a non-terminating decimal division in a filter or SET throws on PostgreSQL, MySQL and SQL Server;
        `Replace` with an empty or null old value throws. No consumer filter divides a decimal.
      → **Found, filed:** TASK-889 (server `ToLower`/`ToUpper` vs .NET: decision), TASK-890 (SQLite overflow surfaces as
        `SqliteException`, narrowing SET on SQLite could use `birko_checked_int`, MySQL BIGINT mid-expression overflow,
        fold over a persistent view column).

## Out of scope

- LIKE and `StringComparison` (TASK-869).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
