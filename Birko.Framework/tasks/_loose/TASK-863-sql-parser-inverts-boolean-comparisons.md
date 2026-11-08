---
id: TASK-863
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P1
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-07
depends-on: [TASK-859]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# The SQL filter parser inverts or mangles a boolean compared with a constant

## Context

Found by the TASK-859 review (2026-11-07), on SQLite against the C# oracle. These shapes already failed on HEAD,
before TASK-859:
- `x.Score.HasValue == false` renders `Score IS NOT NULL`: inverted.
- `(x.Score == x.Peer) == false` renders the equality itself: inverted.
- `x.Flag.HasValue && x.Flag.Value` renders `Flag = NULL`: matches nothing.

An inverted filter on a delete removes exactly the rows the caller meant to keep. Rules 41–44 apply.

## Acceptance criteria

- [x] Reproduced per SQL provider against the C# oracle: each shape above, and the `== true`, `!= true`, `!= false`
      variants over `HasValue`, a comparison, a `bool` and a `bool?` column, and `.Value` on a `bool?`. Record each.
      → Measured 2026-11-08: 34 of 75 shapes wrong on SQLite, PostgreSQL, MySQL and SQL Server (live).
        - `HasValue == false`, `!= false`, a closure `false`: inverted.
        - A comparison or a group compared with `false` (`(Score == Peer) == false`, `(a && b) == false`): the bare
          comparison, so inverted. `(Score < Peer) == false` gave 0 rows where C# matched 6.
        - `StartsWith` / `Contains` / `{5}.Contains(x)` compared with a bool: the bool was bound as the LIKE or IN value,
          so 0 or 6 rows.
        - `Opt.HasValue && Opt.Value` and 5 variants rendered `Opt = NULL` and matched nothing.
        - `(Opt ?? false) == false`: inverted.
        - `Flag == (Amount > 2)`: refused, with a misleading message.
        - Already correct: the `== true` forms, plain `bool` columns, lifted `bool?` (`Opt != true` includes NULL),
          `Opt ?? c`, `Opt == Flag`, `Opt.Value == c`.
- [x] Each one is translated exactly, or refused by name with `NotSupportedException` before the statement runs.
      → All translated, none refused. One producer: `RewriteBooleanComparisons`, a new file
        `DataBase_BooleanPredicate.cs`. It is called once, right after `ExpressionNormalizer.Normalize`, and it
        rewrites only boolean operands in predicate positions.
      → `e == true` becomes `e`; `e == false` and `e != true` become `!e`, on either side. A predicate compared with a
        predicate becomes `(a && b) || (!a && !b)`. `x.Opt.Value` becomes the lifted `Opt == true`. TASK-859's NOT and
        NULL handling does the rest.
      → A plain column compared with a constant or with another column renders unchanged.
      → **Residual, accepted:** an unguarded `x.Opt.Value` renders `Opt = 1` where C# would throw. `!x.Name.StartsWith`
        on a NULL `Name` has no IS NULL leg. C# throws on that row, so there is no answer to match.
- [x] Regression tests per shape and provider (add them to TASK-859's `NullFilterContract` or a sibling contract),
      red before the fix. Every SQL importer suite stays green.
      → New shared `tests/Shared/StoreContract/BoolComparisonContract.cs`, 83 shapes, each checked on read and count
        and on delete and update against `Compile()`, in the four SQL provider suites.
      → Red proof: with the hook line commented out, 80 of 166 failed per provider; 166 of 166 pass with it.
      → 35 SQL importer suites green live, apart from the MySQL TASK295 flake (passed on both reruns) and PostgreSQL
        `DeadlockRetryLiveTests.With_retry_off…`, which fails identically on a clean HEAD worktree.
      → `GetValueOrDefault()` on a `bool?` renders `Opt = NULL`, and `GetValueOrDefault(true)` drops its default.
        That is a dropped call, already in TASK-860.

## Out of scope

- Calls and casts that are dropped (TASK-860).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
