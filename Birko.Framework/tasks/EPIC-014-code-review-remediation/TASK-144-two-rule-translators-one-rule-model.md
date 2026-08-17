---
id: TASK-144
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P3
assignee: ai
created: 2026-08-08
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# `RuleSpecification` and `RuleExpressionConverter` are two translators of one rule model

## Context

Filed from [[TASK-116]]'s Outcome, where unifying them was out of scope for a correctness fix.

`Birko.Rules`' `RuleExpressionConverter` and `Birko.Data.Patterns`' `RuleSpecification` both turn an
`IRule` tree into a LINQ `Expression`. They are independent implementations of the same mapping, and
TASK-116 made the maintenance cost concrete rather than theoretical:

**`Like`, `In` and `NotIn` were missing entirely from `RuleSpecification`** — falling through to
`Expression.Constant(true)`, i.e. match-all on a filter that also drives bulk `Update` and `Delete` —
while `RuleExpressionConverter` had implemented all three correctly the whole time. The fix was to **port
the arms across by hand**.

So one translator silently lacked three *declared* operators for as long as they had existed, and the
only reason the fix was cheap is that the other one was right. Nothing stops the next operator landing in
one and not the other.

TASK-116 also closed a *behavioural* divergence between `RuleSpecification.ToExpression()` and the
in-memory `RuleEvaluator`, pinned afterwards by a parameterised both-engines test. That test is the model
for how this task should end — except the two engines here are both *expression* translators, so they can
be compared structurally and not only by results.

## Acceptance criteria

- [ ] Establish whether the two can become one — including whether `Birko.Data.Patterns` may depend on
      `Birko.Rules` for it (it already references it), and what `RuleSpecification` adds beyond
      translation (the `ISpecification<T>` surface, `IsSatisfiedBy` routed through the evaluator)
- [ ] If unified: every behaviour TASK-116 fixed still holds, proven by re-running its suite **unchanged**
      — that suite is this refactor's regression contract and must not be edited to fit the result
- [ ] If not unified: a test asserts the two translators agree operator-by-operator over a shared table,
      so a future divergence fails loudly instead of waiting to be found by a match-all defect
- [ ] Either way, the decision and its reasoning live in the surviving translator's own docs
- [ ] The degradation invariant TASK-116 introduced (`Leaf.Unevaluable`, never negated into match-all) is
      preserved or deliberately re-derived — it is the property that makes the translator safe, not an
      implementation detail to drop in a merge

## Out of scope

- The four match-all findings themselves ([[TASK-116]], closed).
- `RuleConditionConverter` — the SQL-**text** rule path, a third and genuinely different translator with
  its own outstanding defect ([[TASK-111]]).

## Human test plan

N/A — fully covered by automated tests. The acceptance is either a behaviour-preserving refactor or a
divergence test; both are mechanically checkable.

## Implementation plan

_Populated by `/tasks plan TASK-144` — leave empty until then._
