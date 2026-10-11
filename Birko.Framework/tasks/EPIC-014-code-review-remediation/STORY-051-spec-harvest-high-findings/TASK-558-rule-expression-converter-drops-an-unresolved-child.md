---
id: TASK-558
parent: STORY-051
feature: FEATURE-014
status: todo
priority: P2
assignee: ai
created: 2026-10-11
depends-on: []
blocks: []
findings: [FIELD-025]
pr: null
github-issue: null
jira-key: null
---

# `RuleExpressionConverter` drops a child it cannot resolve, so an AND group widens — and a negated group around it widens again

## Context

Found while fixing [[TASK-320]] (SH-H045, 2026-10-11), which made `RuleSpecification` and `RuleEvaluator`
honour `RuleGroup.IsNegated` by De Morgan. The third translator of the same model,
`Birko.Rules/Expressions/RuleExpressionConverter.cs`, was **not** changed there, and it has a gap of its own:

- `BuildLeafExpression` (:108-110) returns `null` for a `Field` that does not resolve, and `BuildGroupExpression`
  (:145-176) **skips** a `null` child. So `AND(Status == "Open", NonExistent == "x")` becomes `Status == "Open"` —
  wider than the rule — where `RuleSpecification` makes the unresolved leaf an unevaluable match-none and
  `RuleEvaluator` answers `NoMatch`. Pinned as intended today by `RuleExpressionConverterTests.UnknownProperty_ReturnsNull`
  (:280), at the root.
- It negates a group with `Expression.Not(combined)` (:173), which is right only while no child degrades. With the
  child dropped, `NOT(a AND unresolved)` becomes `NOT a` — wider again.
- At the root, a rule that produces nothing makes `ToExpression<T>` return `null`; what a caller does with `null`
  ("no filter" → every row?) decides whether this reaches a destructive call.

Reach, measured 2026-10-11: **no production caller** of `RuleExpressionConverter.ToExpression` outside `Birko.Rules`
itself (tests only). Latent — so P2, not P1.

## Acceptance criteria

- [ ] Decide, in the converter's docs, what an unresolved field means — match-none like the other two engines
      (recommended), or a refusal (throw)
- [ ] A child that cannot be translated never disappears from an AND group; a negated group cannot widen through
      a degraded child (De Morgan, as TASK-320)
- [ ] Tests over a shared table assert the converter agrees with `RuleSpecification` and `RuleEvaluator` on
      unresolved fields and negated groups — the cross-engine check [[TASK-144]] asks for, started here for the
      cases this defect covers; `UnknownProperty_ReturnsNull` is inverted, not deleted, if the decision changes it
- [ ] `/specs regen validation-and-rules`

## Out of scope

- Unifying the translators — [[TASK-144]].

## Human test plan

N/A — library code with no UI surface; fully covered by automated tests.

## Implementation plan

_Populated by `/tasks plan TASK-558` — leave empty until then._
