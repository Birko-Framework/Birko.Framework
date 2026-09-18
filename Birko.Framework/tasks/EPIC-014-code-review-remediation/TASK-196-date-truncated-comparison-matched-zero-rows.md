---
id: TASK-196
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P0
assignee: ai
created: 2026-08-09
completed: 2026-08-08
depends-on: []
blocks: []
findings: []
pr: f3cdf99 (Birko.Data.SQL), c2d3ba0 (Birko.Data.SQL.Tests)
github-issue: null
jira-key: null
---

# `x.Col.Date == value` matched zero rows on every input, every column, every day

## Context

`../Birko.Data.SQL/SQL/DataBase.cs` — `ParseConditionExpression`. **Filed retroactively:** the fix
landed on 2026-08-08 with its production and test commits, but the third commit this family's
integration model requires — the aggregator's task + spec + dashboard — was never made. It was found
and driven from the **consumer** side (Symbio TASK-355), so it carries a Symbio task number and no
framework-side tracking existed at all.

`ParseConditionExpression` rendered a `DateTime` column's `.Date` truncation as `DATE(col)` and bound
the other operand as a `DateTime`. `Microsoft.Data.Sqlite` stores a `DateTime` column as the text
`yyyy-MM-dd HH:mm:ss.FFFFFFF`, so `DATE(col)` evaluates to the 10-character `yyyy-MM-dd` while the
parameter serialises to the full `yyyy-MM-dd 00:00:00`. Those two strings are never equal:

- **equality matched NOTHING** — for every column, every value, every day;
- **inequality failed the other way** — the shorter prefix sorts first, so `DATE(col) < @today` also
  matched the target day.

Measured against the Symbio Testing DB: **0 rows where 4 matched, and 14 rows where 4 should have.**

Both failures are silent. The query runs, returns 200, and reports a plausible wrong answer. In the
consumer that meant a restaurant's daily takings reading as zero, every hotel guest tab numbered H001
off an always-empty count, and invoices due today swept overdue a day early.

Two things generalise past this expression:

- **An in-memory store cannot catch this.** It COMPILES the lambda instead of translating it, so a
  test on such a store passes over a predicate whose SQL is broken. Same lesson as the enum-`IN` and
  empty-`IN ()` defects, and the same reason those survived: the suite that would have caught them
  was running against a backend that never produced the SQL.
- **`DATE(x)` is not a T-SQL function at all**, so the old form was a hard syntax error on MSSql —
  the same works-on-SQLite-only trap as `IN ()`. A defect that reads as "wrong rows" on the reference
  provider is a 500 on another.

## Approach

Rewritten to a half-open range over the RAW column (`col >= d AND col < d+1`), which drops `DATE()`
from the emitted SQL entirely. That matters beyond correctness: a function on the column defeats an
index.

The rewrite fires only when exactly one side is a column `.Date` and the other evaluates to a
constant; column-vs-column and `.Date` in value / `ORDER BY` position keep the previous rendering.
The `!=` arm nests rather than merging into the parent condition — `ReturnSingleSubCondition`
overwrites the child's `IsOr` with the enclosing node's flag, which would turn `col < d OR col >= d+1`
into an unsatisfiable AND.

## Acceptance criteria

- [x] `x.Col.Date == value` matches the rows falling on that day
- [x] `<` / `<=` / `>` / `>=` / `!=` against a truncated date are set-faithful
- [x] No `DATE()` appears in the emitted SQL, so the predicate stays sargable and valid on MSSql
- [x] Column-vs-column and value-position `.Date` are unchanged
- [x] Regression tests that fail without the fix — `DateTruncatedComparisonTests`, 10 tests,
      **all 10 red on revert**, asserting on the parsed `Condition` tree so they hold independently
      of any one dialect
- [x] End-to-end proof against real SQLite — lives in Symbio's `DatePredicateTranslationTests`
- [ ] `/specs regen` for `filter-expression-translation` — **still outstanding**, see below

## Outcome

Landed 2026-08-08 as `f3cdf99` (Birko.Data.SQL) + `c2d3ba0` (Birko.Data.SQL.Tests).

**The spec regen has not been run.** `docs/specs/filter-expression-translation` was generated from
code that rendered `DATE(col)`, so it currently documents the broken translation as shipped
behaviour. That is exactly the ordering constraint the spec harvest warned about — a fix must be
followed by a regen of its area, and the spec diff is the fix's evidence. Tracked as the one open
criterion above rather than silently closed.

## Out of scope

- `TimeSpan` / `DateTimeOffset` truncation — no mapping exists for either, see [[TASK-150]].
- The `.Date` rendering in value and `ORDER BY` position, deliberately left as it was.

## Human test plan

N/A — covered by automated tests at both layers.
