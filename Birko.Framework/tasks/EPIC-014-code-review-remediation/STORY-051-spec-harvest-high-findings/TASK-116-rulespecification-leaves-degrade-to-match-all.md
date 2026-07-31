---
id: TASK-116
parent: STORY-051
feature: null
status: todo
priority: P0
assignee: ai
created: 2026-07-30
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
findings: [SH-H041, SH-H044, SH-H042, SH-H043]
---

# `RuleSpecification` leaves degrade to match-all — on the destructive paths

## Context

Four findings, all in `../Birko.Data.Patterns/Specification/RuleSpecification.cs`, all the same species: **a
leaf degrades to a constant, and the constant then widens to match every row.** `ToExpression()` is the store
filter for `Read`/`Count` **and** for bulk `Update(filter, …)` / `Delete(filter)`, so match-all on this path
empties tables.

Two are hand-verified:

**SH-H041 (`:97`) — CONFIRMED.** `ComparisonOperator` declares `Like`, `In` and `NotIn`;
`BuildLeafExpression` has **no arm for any of them**, so all three fall to `_ => Expression.Constant(true)`.
`Status In [1,2]` becomes an unconditional match, and `Delete(spec.ToExpression())` empties the table. Every
*other* degradation in this file deliberately chose `Constant(false)`, which is what marks the `true` as
unintended.

**SH-H044 (`:100`) — CONFIRMED-NARROWER, and read the correction before fixing.** The claim named the
unresolved-field branch, but `if (property is null) return Expression.Constant(false)` returns **before**
both the switch and the `IsNegated` check — so that case is plain match-none and is fine. The mechanism is
real only for the `Constant(false)` that `BuildStringMethod` returns for a **non-string member**, which does
reach the negation. That makes a negated `Contains` on a non-string property match everything.

Two more are unverified but **cannot be fixed independently**, which is why they are in this task:

**SH-H043 (`:94`)** is the trigger SH-H044's verdict points at. `BuildStringMethod` returns
`Constant(false)` for a non-string member (lines 141–142, commented *"a non-string field is never a
match"*), and the `NotContains` arm wraps it in `Expression.Not` — so `Qty NotContains "1"` on an `int` is
`x => !false`, every row, with no `IsNegated` needed. In memory, `ComparisonHelper.NotContains` does
`ToString().Contains` and gives a genuinely filtered result, so the two halves of the framework disagree too.

**SH-H042 (`:62`)** — `BuildExpression` returns `Constant(true)` when `!rule.IsEnabled`, while
`RuleEvaluator.Evaluate` (`RuleEvaluator.cs:13-14`) returns NoMatch for the same condition. So a disabled
rule reads/updates/**deletes every row** while `IsSatisfiedBy` returns false for every entity.
`WrapRuleSet` deliberately avoids handing a disabled rule to this path — evidence the `true` arm is
unintended.

## The reference implementation already exists

`Birko.Rules`' own `RuleExpressionConverter` (`Expressions/RuleExpressionConverter.cs:130-132`) implements
`Like`, `In` and `NotIn` **correctly**, and returns `null` (no filter) rather than `Constant(true)` for a
disabled rule. Port from there rather than inventing; then consider whether the two converters should be one.

## Approach

Two decisions, and the second matters more than the missing arms:

1. **Implement the missing operators** (`Like`, `In`, `NotIn`) by porting `RuleExpressionConverter`'s arms.
2. **Make a degradation impossible to widen.** The bug is not any single wrong constant — it is that a
   fallback constant sits where the caller cannot tell degradation from a real predicate, and negation then
   inverts it. An untranslatable leaf should **throw** on the destructive paths, mirroring the decision in
   [[TASK-109]] and the ElasticSearch `ParseRequiredFilterQuery` split. If a fallback constant must survive
   for the read path, negation must not be applied to a degraded leaf.

Also close the in-memory / expression divergence: `IsSatisfiedBy` and `ToExpression()` must agree for every
case fixed here. Two answers from one specification is its own defect, and it is what allowed these to sit
unnoticed.

## Acceptance criteria

- [ ] `Like`, `In` and `NotIn` leaves produce correct filters — matching `RuleExpressionConverter`'s
      semantics — for `Read`, `Count`, `Update(filter, …)` and `Delete(filter)`
- [ ] `Status In [1,2]` on a populated store deletes **only** the matching rows, asserted end-to-end
- [ ] A negated `Contains` / `NotContains` on a **non-string** member does not match every row (SH-H043 /
      SH-H044's real trigger)
- [ ] A **disabled** rule does not translate to match-all; `ToExpression()` and `IsSatisfiedBy` agree
      (SH-H042)
- [ ] An unresolved field stays match-none — the case SH-H044's verdict says is already correct is asserted,
      so the fix cannot regress it
- [ ] No degraded leaf can be negated into match-all; a leaf the translator cannot express throws on the
      destructive paths
- [ ] `ToExpression()` and `IsSatisfiedBy` agree for **every** case in this task — one parameterised test
      over both
- [ ] Regression tests in `Birko.Data.Patterns.Tests`, plus an end-to-end delete case proving the
      destructive path narrows
- [ ] `/specs regen` for `specifications-and-paging`, spec diff reviewed

## Out of scope

- `SH-H023` — the SQL-**text** rule path (`RuleConditionConverter`), a different translator. [[TASK-111]].
- `SH-H021`/`SH-H022`/`SH-H026`/`SH-H027` — the same match-all species in the SQL and ElasticSearch
  expression translators. Unverified; separate tasks, but worth verifying as a set since the fix rationale
  here transfers directly.
- Unifying `RuleSpecification` and `RuleExpressionConverter` into one converter. Worth doing, but it is a
  refactor with its own risk and should not ride along on a correctness fix — file it if the port makes the
  duplication obvious.

## Human test plan

N/A — covered by automated tests. The end-to-end delete assertions cover the real consequence.
