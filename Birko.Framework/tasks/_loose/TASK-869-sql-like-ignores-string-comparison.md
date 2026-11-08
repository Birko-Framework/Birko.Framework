---
id: TASK-869
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P2
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

# A SQL `Contains` / `StartsWith` / `EndsWith` follows the column collation, not C#'s comparison

## Context

Found by TASK-860 (2026-11-08). The LIKE family ignores `StringComparison` and follows the column's collation:
- SQLite, MySQL and SQL Server match case-insensitively by default, where C#'s `x.Name.Contains("ab")` is
  case-sensitive (ordinal).
- PostgreSQL is case-sensitive.

So the same filter returns different rows per provider, and differs from C# on three of them. TASK-860 deliberately
left this alone, because Symbio's search depends on case-insensitive matching (85 uses of `ToLower().Contains`, plus
plain `Contains`).

## Acceptance criteria

- [x] Measure per provider (live): `Contains`, `StartsWith` and `EndsWith`, each without a comparison, with `Ordinal`
      and with `OrdinalIgnoreCase`, plus `ToLower().Contains(term.ToLower())`, on mixed-case and non-ASCII data.
      Record each against C#.
      → Measured 2026-11-08 live on SQLite, PostgreSQL 16, MySQL 8.4 and SQL Server 2022: 41 texts × 28 terms × 4
        kinds (Contains, StartsWith, EndsWith, Equals) = 240 cases per form, against `Compile()`. Text types and
        VARCHAR(255) gave the same results. Mismatches, of 240:
        - No comparison (LIKE, collation): SQLite 49 (ASCII-only folding), PostgreSQL 5, MySQL 113 (case- and
          accent-insensitive), SQL Server 104 (case-insensitive).
        - `ToLower().Contains(t.ToLower())`: 72 / 13 / 59 / 45.
        - `OrdinalIgnoreCase` as `UPPER(col) LIKE UPPER(t)`: 54 / 32 / 24 / 33. Over planes 0 and 1, each server's
          `UPPER` groups characters differently from C# (PostgreSQL 16, MySQL 181, SQL Server 625–1101); `ı` and `ſ`
          are the common ones. So `UPPER`, `ILIKE` and `_ci` collations were all rejected.
- [x] **Decide with the user**: make the no-argument form C#-exact (case-sensitive everywhere, which breaks
      Symbio's search unless it moves to `OrdinalIgnoreCase` or `ToLower`), or keep it collation-following and
      document it. Either way, translate the explicit `Ordinal` and `OrdinalIgnoreCase` exactly, or refuse them.
      → **Decided 2026-11-08 by the user: option A.** The no-argument form keeps following the collation, and
        the per-provider behaviour is documented. `StringComparison.Ordinal` translates to an exact, case- and
        accent-sensitive match on every provider; `OrdinalIgnoreCase` to a case-insensitive, accent-sensitive
        match. Any other `StringComparison` is refused by name. Where `OrdinalIgnoreCase` cannot be exact (SQLite
        folds ASCII only), it is fixed or refused, on measurement.
      → Built, one producer `SqlStringMatch` (`Birko.Data.SQL/SQL/Conditions`):
        - `Ordinal`: SQLite `birko_string_match` (a registered C# function, via `SqLiteDecimal.Register`, rule 64);
          PostgreSQL `col COLLATE "C" LIKE`; MySQL `col COLLATE utf8mb4_bin LIKE`; SQL Server
          `col COLLATE Latin1_General_100_BIN2 LIKE`. 0 of 240 off on each.
        - `OrdinalIgnoreCase`: each cased term character is expanded into C#'s own case class, computed once from
          `string.Equals(…, OrdinalIgnoreCase)`, and matched ordinally: PostgreSQL regex under `COLLATE "C"`, MySQL
          `REGEXP_LIKE(…, 'c')`, SQL Server BIN2 LIKE with bracket classes, SQLite the C# function. 0 off. Refused by
          name on SQL Server: a cased letter outside the BMP, and a pattern past 4000 characters (Msg 8152, measured;
          `nvarchar(max)` does not help).
        - `Equals(v, cmp)` and `string.Equals(a, b, cmp)` now translate too (TASK-860 refused them), as
          `ConditionType.TextEqual`, NULL-aware as TASK-859.
        - `CurrentCulture*`, `InvariantCulture*`, the `(string, bool, CultureInfo)` overloads, a null term and a
          comparison read from the row are refused by name (`DataBase_CallTranslation.TranslatableComparison`).
        - The no-argument forms are unchanged and documented per provider (`Birko.Data.SQL/README.md`). MySQL's
          `Ordinal` needs a utf8mb4 column (framework-created tables are); utf8mb3/latin1 fail with ERROR 1253.
- [x] Tests per provider, red before the fix.
      → `tests/Shared/StoreContract/StringComparisonFilterContract.cs`, 293 cases per SQL provider (read, count,
        update and delete against `Compile()`, refusals, and the no-argument forms pinned per collation);
        `Task869ViewStringComparisonTests` (Views, 36, on-the-fly and persistent); `SqlStringMatchTests` (SQL.Tests, 20,
        the case classes against C# over planes 0 and 1).
      → Red on HEAD: 114 / 112 / 141 / 122 of 259 (SQLite / PostgreSQL / MySQL / SQL Server); views 21 of 36.
      → **Independent review** found one defect before commit: `ReturnSingleSubCondition` copied a collapsed group's
        properties by hand and dropped `Comparison`, so `!(x.Text.StartsWith("a", OrdinalIgnoreCase) && T)` fell back
        to collation LIKE; on PostgreSQL its delete removed `Abc` and `ABC`, which C# keeps (measured). Fixed: one
        `Condition.CopyPredicateTo`, used by `Copy` and the new `BecomePredicateOf`. 32 collapse shapes added, red
        20–24 of 32 per provider with the old hunk. The review also measured escaping, empty and long terms, trailing
        spaces, NULL legs and views: sound.
      → Live, `BIRKO_REQUIRE_LIVE=1`: SQL.Tests 835, Views 282, SQLite 1729, MySQL 1171, SQL Server 1202, PostgreSQL
        1147 of 1149 (`DeadlockRetryLiveTests`: the known `With_retry_off`, and a timing flake that passed on rerun).
        The first round ran all 35 importer suites green, apart from TimescaleDB without its host variable.
      → Consumers: one store filter passes a comparison, BardStudio's `SearchTracksAsync` (`OrdinalIgnoreCase`, SQLite):
        `é` now also finds `É`. Symbio's searches use the no-argument form and are unchanged.
      → **Found, filed:** TASK-873 (plain `==` on strings follows the collation too); TASK-874 (SQL Server LIKE past
        4000 characters fails with Msg 8152 on the no-argument form); `ToLower` / `ToUpper` before a comparison added to
        TASK-870.

## Out of scope

- Other calls (TASK-860, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
