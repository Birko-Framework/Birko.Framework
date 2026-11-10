---
id: TASK-890
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-09
depends-on: [TASK-870]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# SQL arithmetic leftovers after TASK-870

## Context

Found by TASK-870 (2026-11-09):
- **SQLite overflow type:** `birko_checked_int` surfaces as `SqliteException` (Error 1); the read-modify-write fallback
  throws `OverflowException`, and the servers throw their drivers' exceptions. Callers cannot catch one type (rule 9).
- **SQLite narrowing SET** (`(short)(Sh + 1)`), refused since TASK-860, could translate through `birko_checked_int`
  with the target type's range.
- **MySQL computes integer expressions in BIGINT**, so an overflow mid-expression (`Hits * 2 / 2`) is likely silent.
  Not measured.
- **A fold over a persistent view column** is refused; it could be supported by renaming the column inside the fold.
- Reads with integer arithmetic on SQLite now fail the whole statement when any row overflows, as PostgreSQL and SQL
  Server do; MySQL widens and answers.

## Acceptance criteria

- [x] Measure each (rule 54) and record.
      → Measured 2026-11-10 live on all four providers (reads, counts, Delete, both Update forms, SET, on-the-fly views).
        Oracle chosen: **checked C#** — C#'s default `int` arithmetic wraps, which no provider can compute; PostgreSQL and
        SQL Server already fail, and TASK-870 made SQLite fail, so "fail where C# would wrap, never answer differently".
        - **MySQL silently wrong:** every `int` overflow answered (BIGINT arithmetic): `Hits * 2 / 2 == Hits` matched the
          int.MaxValue row; `SET Hits = Hits * 2 / 2` stored int.MaxValue.
        - **New: PostgreSQL and SQL Server failed where C# succeeds**: `x.Sh * x.Sh > 1000`, `(long)x.Hits * 2 > 0` — the
          explicit widening cast was dropped, so they computed in the column's type (22003 / 8115).
        - **New: `Math.Abs` at the type's minimum** answered on MySQL, SQLite and SQL Server (`short`); C# throws.
        - Exception types: each driver's `DbException` (PostgreSQL 22003, SQL Server 8115/220, MySQL 1690/1264, SQLite
          error 1 carrying `birko_checked_int`'s text).
- [x] Fix or record each; one exception type for an arithmetic overflow across providers if decided worthwhile.
      → (a) No rewrap (rule 9): all four are `DbException` with driver codes the retry filter and callers select on;
        consistent with TASK-888. Documented.
      → MySQL: `MySqlValueFragmentDialect` `Arithmetic` / `Negate` / `Abs` wrap `int`/`short`/`sbyte` results as
        `((v * 2^(64-n)) DIV 2^(64-n))`, which raises 1690 exactly when `v` leaves n bits (unsigned recorded, TASK-904).
      → PostgreSQL / SQL Server: new `IntegerWidening` hook renders `CAST(… AS INTEGER/BIGINT)` for a transparent
        widening (MySQL and SQLite render nothing); base `Abs` of a `short` is `CAST(ABS(x) AS SMALLINT)`; SQLite `Abs`
        through `birko_checked_int`.
      → (b) `NarrowedInteger(value, target)` replaces `StoresIntegerOverflowLoudly` (**breaking**: the public virtual is
        removed); SQLite wraps it in `birko_checked_int` with the target range, so `Sh = (short)(Sh + 1)` translates and
        fails only on overflow; the refusal branch was unreachable and is removed (rule 53).
      → (d) A fold of a plain column on a persistent view is supported: `Condition.CaseFold`, one producer
        `DataBase.FoldColumn`, `ResolveViewConditions` renames inside the fold; a fold of a computed value stays refused.
      → (e) Recorded: a read with integer arithmetic fails the whole statement when any row overflows — now on all four.
- [x] Tests, red before any fix.
      → `Task890ArithmeticOverflowTests` (15 filter shapes × 7 paths, 13 SET cases, 3 view shapes per provider);
        `Task870ViewCaseFoldTests` persistent refusal inverted (rule 56); `CallCastFilterContract` narrowing test now
        `Contract_A_narrowing_SET_writes_what_CSharp_writes`. Red with production reversed on a HEAD snapshot: 186 of 620
        (MySQL 77), SQLite narrowing 3 of 3. Green on HEAD + patch: Views 3889, SQL.Tests 1112, SQLite 2280, and the other
        importers; full provider suites green apart from TASK-895's in-progress shape.
      → Consumers: no dialect subclass or `StoresIntegerOverflowLoudly` user; Symbio on PostgreSQL only gains `CAST` in
        integer-widening fragments.

## Out of scope

- The items TASK-870 fixed.

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
