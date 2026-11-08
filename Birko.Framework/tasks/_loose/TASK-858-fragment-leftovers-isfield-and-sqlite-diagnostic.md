---
id: TASK-858
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
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

# Two leftovers from the value-fragment work: a dropped `IsField`, and SQLite's diagnostic text

## Context

Found by TASK-856 (2026-11-07). Grouped, because both come from the same thread and are small.
- **`DataBase.ReturnSingleSubCondition`** does not copy `IsField` when it collapses a group. This predates TASK-856 and
  no current path exercises it. A collapsed column-vs-column comparison would render its right side as a value instead
  of a column.
- **On SQLite**, `GetGeneratedQuery`'s diagnostic text shows the portable fragment, not the SQL TASK-856's dialect
  actually sent. A reader debugging a SQLite failure sees a statement that never ran.
- **Added from the TASK-856 review:**
  - `DataBase.ComparedFragment` (around line 1793): on SQLite, constants inside a fragment are evaluated again at
    render time, and once per IN chunk. A side-effecting call (`x.Amount + Next()`) runs several times, and a captured
    variable that changes between parsing and rendering makes the executed SQL differ from the `Name` the planner
    checked. Low real-world impact.
  - `Birko.Data.SQL.View/SQL/DataBase_ViewConditions.cs` (around line 71): `ResolveViewCondition` builds a new
    `Condition` and drops `NameFragment` / `FieldFragment`, so persistent SQLite views keep the portable form. Most
    likely loud ("no such column"), since the fragment's columns are not remapped there.
  - "Other providers render byte-identically" is pinned by one shape only
    (`ThePortableFragment_OtherProvidersRun_IsUnchanged`). It should also cover CASE, COALESCE, a Convert, and column
    vs column.

## Acceptance criteria

- [x] `ReturnSingleSubCondition` carries every field of the condition it collapses, preferably through
      `Condition.Copy` (one producer). A test collapses a column-vs-column condition and asserts it still renders as
      columns. If no path can collapse such a condition, say so and pin it.
      → Re-measured 2026-11-08: not reachable. The parser nests every column comparison under the condition it is
        filling, so the node that survives a collapse is always a group, and its IsField leaf moves with it. 6
        collapse shapes matched C# on SQLite. Pinned in `Task858FragmentLeftoverTests`. The code is unchanged:
        switching to `Copy` would change nothing today (rule 53).
- [x] The diagnostic text equals the command text actually executed on SQLite, with the values substituted. Test:
      the diagnostic of a decimal-fragment filter on SQLite contains the dialect's rendering.
      → Already correct: `GetGeneratedQuery` reads the command text that runs. Measured, it contains
        `birko_decimal_add(...) COLLATE BIRKO_DECIMAL = 10.10`. Pinned.
- [x] Fragment constants are evaluated once, at parse time, and the rendering reuses that value: a test with a
      side-effecting captured call shows one evaluation across chunks.
      → Gone: `ExpressionNormalizer` folds the constant parts of a filter before parsing, so a side-effecting
        captured call ran once, chunked or not. Pinned.
      → **The re-render at render time caused a regression, found and fixed here.** It resolved the view's columns
        again after `SqlViewStore`'s lookup scope had closed. So every on-the-fly view filter using arithmetic,
        COALESCE, CASE, Round or a column-vs-column comparison threw "Cannot translate operand 'v.Amount'", on all
        four providers at HEAD. TASK-857 introduced it: PostgreSQL ran these filters at cf6bee27.
      → Fix: a new `FragmentResolution` in `DataBase.cs`. The parse records columns, constants and nullability once,
        and each provider's re-render reads them.
- [x] Persistent SQLite views carry the fragments through `ResolveViewCondition`, or refuse a decimal fragment by
      name. Test on a persistent view.
      → Reproduced: all four providers failed loudly, but with an error naming something else ("no such column",
        42P01, "BIRKO_ROUND_TOEVEN not recognized").
      → `ResolveViewCondition` (`DataBase_ViewConditions.cs`) now copies conditions through `Condition.Copy`. A
        computed fragment on a persistent view is refused by name with `NotSupportedException` before anything runs.
        Plain and column-vs-column filters run as before.
- [x] The byte-identity test for the other providers covers CASE, COALESCE, a Convert, and column vs column.
      → `Task858FragmentByteIdentityTests`: 10 shapes on PostgreSQL, MySQL and SQL Server, all green.
      → Red proof:
        - With both production files reverted, 92 of 100 view tests fail: 40 on-the-fly computed, 40 persistent
          refusal, 12 column-vs-column on-the-fly. With only the view resolver reverted, 40 fail.
        - The pins for items 1–3 and 5 pass either way.
      → 35 SQL importer suites live. All green, except TimescaleDB and Migrations.TimescaleDB, which have no container.
        The known failures also showed: PostgreSQL `With_retry_off`, and the TASK295 flake on MySQL and SQL Server,
        which passed on rerun.
      → **Found, filed:** TASK-871. Reader paths pass `OnExecute` the raw command text, without the values substituted.

## Out of scope

- Parser translation of `Math.*` (TASK-857).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
