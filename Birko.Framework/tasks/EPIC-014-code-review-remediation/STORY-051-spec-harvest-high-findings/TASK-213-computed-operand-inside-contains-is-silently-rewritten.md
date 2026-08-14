---
id: TASK-213
parent: STORY-051
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P1
assignee: ai
created: 2026-08-14
depends-on: []
blocks: []
related: [TASK-137]
pr: null
github-issue: null
jira-key: null
findings: []
---

# A COMPUTED operand inside `Contains` is silently discarded and replaced by a different predicate

## Context

Found by [[TASK-137]] (2026-08-14) — specifically by adding its shapes to `SqlExpressionParityTests`, the
compiled-delegate oracle suite. **This is pre-existing and independent of TASK-137**: it reproduces with
non-empty sets and with a non-negated `Contains`, neither of which that task touched.

`ids.Contains(<expr>)` translates correctly when `<expr>` is a plain mapped property. When `<expr>` is
**computed** — arithmetic, or a coalesce — the operand is **dropped** and the `In` condition is built with
`Name = null` plus a subcondition holding a fabricated `Equal`. Measured against real SQLite (seed
`Amount = 1, 5, 5, 9`, `Score = 10, null, 20, 30`; `SomeIds = {1, 5}`, `NoIds` empty), oracle =
`expr.Compile()` over the same rows:

| predicate | oracle | SQL | parsed condition |
|---|---|---|---|
| `SomeIds.Contains(x.Amount + 1)` | **0** | **1** | `Name=null Type=In Values=[1,5] Subs=1` → child `Amount Equal [1]` |
| `!SomeIds.Contains(x.Amount + 1)` | 4 | 3 | same, negated |
| `!SomeIds.Contains(x.Score ?? 0)` | 4 | 3 | `Name=null Type=In Values=[1,5] Subs=1` → child `Score Equal [0]` |
| `!NoIds.Contains(x.Score ?? 0)` | 4 | 3 | `Name=null Type=In Values=[] Subs=1` → child `Score Equal [0]` |
| `SomeIds.Contains(x.Amount)` (plain, control) | 3 | 3 | `Name=ProbeRows.Amount Type=In Values=[1,5]` ✔ |
| `(x.Score ?? 0) > 4` (coalesce OUTSIDE Contains, control) | 3 | 3 | `Name=COALESCE(ProbeRows.Score, 0) Type=Greather` ✔ |

Two things the table shows:

- **Wrong answers in both directions**, not just under-matching: the non-negated row returns a row that does
  not satisfy the predicate. `x.Amount + 1` appears to be consumed as though `+` were a comparison, leaving
  `Amount = 1`, which matches the `Amount = 1` row while the true answer is no rows at all.
- **The coalesce machinery works — it just isn't reached from the `Contains` operand path.** The last control
  row renders a proper `COALESCE(...)` as the condition's `Name`; inside `Contains` the same expression
  becomes a subcondition instead. So this is a wiring gap in the `In` operand resolution, not a missing
  translation.

Silent throughout: no exception, no log entry, and the malformed shape (`Name = null` with children) is
exactly the "non-empty collection that renders oddly" case `AddRequiredWhere`'s doc comment anticipates
without naming.

**Interaction with TASK-137 — nil, and verified so.** That task's reduction inspects `SubConditions` first, so
a malformed `In`-with-children is treated as a group, its child is not always-true, and the term renders
unchanged. `!NoIds.Contains(x.Amount) && !NoIds.Contains(x.Amount)` (plain operands) is 4/4 correct. The
malformed rows above answer the same before and after that fix.

## Approach

Start at `DataBase.ParseConditionExpression`'s `Contains` arm (`../Birko.Data.SQL/SQL/DataBase.cs`, the
`case "Contains":` around line 606) and establish **why** a computed operand becomes a subcondition — most
likely the operand is recursed into as though it were a nested predicate, so a `BinaryExpression` operand
takes the ordinary binary-comparison path and its right side is read as a value.

The fix is presumably to resolve the operand through the same expression-to-SQL-name path the working control
row uses (whatever produces `COALESCE(ProbeRows.Score, 0)` as a condition `Name`) and use its result as the
`In` condition's `Name`.

**Decide explicitly what happens to an operand that cannot be expressed** — the § SH-H037 rule applies: a
mapper that cannot express something refuses, it never drops it quietly. Today's silent rewrite is the worst
option available. Refusing needs a measured blast radius first, since consumer models may contain such
predicates that currently "work" (i.e. return wrong rows).

## Acceptance criteria

- [ ] `ids.Contains(<computed expr>)` either translates correctly or **refuses**; it never silently
      substitutes a different predicate. Whichever is chosen is recorded with its reason
- [ ] Every row of the § Context table agrees with its oracle (or refuses), including the two controls, which
      must not regress
- [ ] The negated and non-negated forms are both covered, and the empty-set forms too — TASK-137 owns the
      *reduction* of an empty negated set, not the operand resolution, and the two must compose
- [ ] The malformed shape (`In` condition with `Name = null` and subconditions) is no longer producible from
      the parser; if it remains representable, what the renderer does with it is asserted rather than left to
      `BuildSingleCondition`'s empty-string fallback
- [ ] New cases live in `Birko.Data.SQL.SqLite.Tests/SqlExpressionParityTests.cs` — the compiled-delegate
      oracle is what found this and is the right home; hand-computed expectations are not
- [ ] Red-verified with the split reported as numbers; contract pins named as pins
- [ ] Blast radius measured before any refusal ships — if a computed operand starts throwing, a consumer
      model carrying one breaks at parse time (the TASK-112 precedent: cleared against 19 suites)
- [ ] `/specs regen filter-expression-translation` — the area globs `DataBase*.cs`, so the diff is real
      evidence. Note that its § *SQL IN translation and empty sets* requirement currently says nothing about
      operand resolution, so this will likely ADD a requirement rather than change one

## Out of scope

- TASK-137's always-true reduction. Verified not to interact (§ Context).
- `String.Contains` (the `LIKE` path) — a different arm with a different operand story. Check it while you are
  there, and file separately if it shares the defect.
- Non-SQL backends. ElasticSearch and MongoDB translate `Contains` themselves; whether they mishandle a
  computed operand is the same question in a different translator and needs its own measurement.

## Human test plan

`N/A — fully covered by automated tests.` The compiled-delegate oracle compares SQL results against C#
semantics over a real database, which is a stronger check than any manual step, and there is no UI surface.

## Implementation plan

_Populated by `/tasks plan TASK-213` — leave empty until then._
