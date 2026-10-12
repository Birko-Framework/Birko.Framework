---
id: TASK-574
parent: STORY-051
feature: FEATURE-014
status: todo
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

- [ ] `x.A.Date == x.B.Date` (and `!=`, `<`, `>`) translates to a comparison of both columns' dates on every SQL provider, or is refused with `NotSupportedException` — never a silent match-nothing
- [ ] Plain `x.A == x.B` checked for the same overwrite; fixed here if it shares the cause, spawned otherwise
- [ ] A regression test fails on the current code and passes with the fix
- [ ] `/specs regen` of the area, with the spec diff reviewed, is the fix's evidence (EPIC-014's standing constraint)

## Out of scope

- The constant-side `.Date` range rewrite (correct today)

## Human test plan

N/A — the behaviour is asserted by the regression test; nothing here is visual.
