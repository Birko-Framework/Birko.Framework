---
id: TASK-574
parent: STORY-051
feature: FEATURE-014
status: done
priority: P1
assignee: ai
created: 2026-10-12
depends-on: []
blocks: []
findings: [SH-H058]
pr: null
github-issue: null
jira-key: null
---

# `x.A.Date == x.B.Date` renders `DATE(B) = NULL` and matches no row

## Context

Found by the 2026-10-12 `/specs regen` of all 25 areas at `c50ed580` (`filter-expression-translation`); traced 2026-10-12.

`TryBuildDateTruncatedComparison` (`Birko.Data.SQL/SQL/DataBase.cs:567`) rewrites `x.Col.Date <op> value` to a range,
but only when **one** side is a constant; its own comment says column-vs-column "keeps the old DATE() path". That path
parses both operands into **one** `basecondition` (`DataBase.cs:597-598`). The `.Date` member branch
(`DataBase.cs:834-851`) sets `parent.Name = $"DATE({inner.Name})"` for each side, so the right operand overwrites the
left's name, and no value is bound. The rendered predicate is `DATE(B) = NULL`: always UNKNOWN, zero rows, no error.
`DATE()` is also not T-SQL.

The harvester suggested any plain column-vs-column comparison may share the mechanism. Not traced: check it while
reproducing.

## Acceptance criteria

- [x] `x.A.Date == x.B.Date` (and `!=`, `<`, `>`) translates to a comparison of both columns' dates on every SQL provider, or is refused with `NotSupportedException` — never a silent match-nothing
- [x] Plain `x.A == x.B` checked for the same overwrite; fixed here if it shares the cause, spawned otherwise
- [x] A regression test fails on the current code and passes with the fix
- [x] `/specs regen` of the area, with the spec diff reviewed, is the fix's evidence (EPIC-014's standing constraint) — done 2026-10-16 (batched regen of 17 areas, diffs reviewed and confirmed)

## Resolution (2026-10-15)

**Plain column-vs-column shared the cause and is fixed here.** Measured before the fix on SQLite, PostgreSQL and SQL
Server: `x.A == x.B`, `x.A != x.B`, `x.A < x.B` and `x.Opened < x.Closed` all returned an empty collection. The
fall-through parse writes both operands into one condition, so the right operand overwrote the left's name and no
value was bound. Any comparison whose two operands both read the row now goes through `BuildColumnComparison`
(`Birko.Data.SQL/SQL/DataBase.cs`): both sides rendered by `RenderValueFragment`, the right one `IsField`.

- **Null-safety.** `==` / `!=` between operands that can be null render
  `A = B OR (A IS NULL AND B IS NULL)` and `A <> B OR (A IS NULL AND B IS NOT NULL) OR (A IS NOT NULL AND B IS NULL)`,
  so they answer what C# (and the InMemory store) answers, as the constant `== null` path already did. Orderings
  are left plain: a lifted `<` with a null is false in C#, and UNKNOWN filters the row out in SQL.
- **`.Date` vs `.Date` is refused**, the criterion's second branch. The parser is static and dialect-free, and no
  date truncation is portable: `DATE()` is not T-SQL (the old code failed outright on SQL Server), and
  `CAST(x AS DATE)` reads SQLite's stored `yyyy-MM-dd HH:mm:ss` text as the number 2026. A `.Date` against a value
  keeps its range rewrite (control test, green before and after). Should a consumer need it, the honest form is a
  per-dialect hook on the connector, which is a feature, not this fix.

Tests: `tests/Birko.Data.SQL.SqLite.Tests/ColumnVersusColumnTests.cs` (13; 12 red before, the constant-side
`.Date` control green) and `ColumnVersusColumnLiveTests` on PostgreSQL, SQL Server and MySQL (3 each, red before on
PostgreSQL and SQL Server, all green after). The first live run tripped on the test model's own `End` column, a
reserved word in PostgreSQL and T-SQL, since column names are emitted bare (rule 17); renamed `Opened` / `Closed`.

## Out of scope

- The constant-side `.Date` range rewrite (correct today)

## Human test plan

N/A — the behaviour is asserted by the regression test; nothing here is visual.
