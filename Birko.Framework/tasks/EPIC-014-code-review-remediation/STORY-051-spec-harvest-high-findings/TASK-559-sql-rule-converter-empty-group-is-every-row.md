---
id: TASK-559
parent: STORY-051
feature: FEATURE-014
status: todo
priority: P2
assignee: ai
created: 2026-10-11
depends-on: []
blocks: []
findings: [FIELD-026]
pr: null
github-issue: null
jira-key: null
---

# The SQL `RuleConditionConverter` turns a disabled rule or an empty group into no condition — every row — where the other engines match nothing

## Context

Found while closing [[TASK-320]] (SH-H045, 2026-10-11), checking whether the fourth translator of the rule model,
`Birko.Data.SQL/SQL/Conditions/RuleConditionConverter.cs`, shares the negated-group defect. **It does not** — it
negates a group with SQL `NOT` around a sub-condition, and refuses an unresolved field (SH-H023), so it has no
degraded constant to invert. It has a different divergence:

- `Convert` (:99-110) returns `[]` for a disabled rule, and `ConvertGroup` (:126-152) returns `[]` for an empty or
  all-disabled group, negated or not. An empty condition list is **no `WHERE`** — every row — whereas
  `RuleSpecification` answers match-none for the same input (SH-H042, fixed at TASK-116 precisely because a disabled
  root became `x => true`) and `RuleEvaluator` answers `NoMatch`.
- Pinned as intended today by `DisabledRule_ReturnsEmpty` / `DisabledRuleSet_ReturnsEmpty`
  (`tests/Birko.Data.SQL.Tests`, :164-176).
- A nested empty group inside a non-empty AND contributes nothing, which is harmless there; the root case is the one
  that widens. Whether a destructive call can receive it depends on the SQL scope guard (CLAUDE-conventions rules
  1-3), which should refuse an unconditioned `DELETE` / `UPDATE` — measure that rather than assume it.

Reach, measured 2026-10-11: **no production caller** of `RuleConditionConverter` outside tests. Latent — P2.

## Acceptance criteria

- [ ] Decide what a disabled root / empty group means to the SQL converter — match-none like the other engines
      (an always-false condition), or a refusal — and record it in the converter's docs
- [ ] Measure whether a `Delete` / bulk `Update` fed an empty condition list from this converter is refused by the
      scope guard today; record the answer either way
- [ ] Tests pin the decision; `DisabledRule_ReturnsEmpty` / `DisabledRuleSet_ReturnsEmpty` inverted, not deleted, if
      it changes; and the converter agrees with `RuleSpecification` / `RuleEvaluator` on the cases this covers
- [ ] `/specs regen filter-expression-translation`

## Out of scope

- Unifying the translators — [[TASK-144]]; `RuleExpressionConverter`'s dropped child — [[TASK-558]].

## Human test plan

N/A — library code with no UI surface; fully covered by automated tests.

## Implementation plan

_Populated by `/tasks plan TASK-559` — leave empty until then._
