---
id: TASK-558
parent: STORY-051
feature: FEATURE-014
status: done
priority: P2
assignee: ai
picked-by: fix-next
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

- [x] Decide, in the converter's docs, what an unresolved field means — match-none like the other two engines
      (recommended), or a refusal (throw)
- [x] A child that cannot be translated never disappears from an AND group; a negated group cannot widen through
      a degraded child (De Morgan, as TASK-320)
- [x] Tests over a shared table assert the converter agrees with `RuleSpecification` and `RuleEvaluator` on
      unresolved fields and negated groups — the cross-engine check [[TASK-144]] asks for, started here for the
      cases this defect covers; `UnknownProperty_ReturnsNull` is inverted, not deleted, if the decision changes it
- [x] `/specs regen validation-and-rules`

## Out of scope

- Unifying the translators — [[TASK-144]].

## Human test plan

N/A — library code with no UI surface; fully covered by automated tests.

## Implementation plan

_Populated by `/tasks plan TASK-558` — leave empty until then._

## Outcome

**Fixed.** The LINQ rule converter now selects the same rows as the other two rule engines. A rule it cannot
translate — a misspelled field, a disabled rule, an empty group — used to vanish from its group (an AND got wider) or,
alone, made the converter return `null`, which a store reads as "every row". It now matches nothing, and a negated
group can no longer turn that into "everything".

- **Decision (acceptance 1) — match-nothing, not a throw.** It is what `RuleSpecification` (TASK-116) and
  `RuleEvaluator` answer for an unresolved field, so the three engines agree; a throw would make this one engine
  disagree in the other direction. Recorded in the class doc. Consequence: the entry points **never return null**
  — the return type lost its `?` — because `null` was itself the widening (`SH-M358`).
- **Negation (acceptance 2):** pushed down to the leaves by De Morgan, as `RuleSpecification` / `RuleEvaluator` do
  (TASK-320); `Expression.Not` around a combined group is gone. An unknown operator is match-nothing too, not a
  `false` a negation could invert.
- **Step-6 split (164 tests in Birko.Rules.Tests; 18 new in `RuleExpressionConverterAgreementTests` + 1 pin):** full revert
  → **18/164** red: 12 agreement shapes/facts (`unresolved leaf`, `negated unresolved leaf`, `A AND unresolved`,
  `NOT (A OR unresolved)`, `NOT ((A OR unresolved) AND B)`, `disabled leaf`, `empty group`, `A AND empty`,
  `negated empty group`, `A_rule_set_holding_an_unresolved_rule_matches_nothing`,
  `A_rule_list_holding_an_unresolved_rule_matches_nothing`, `An_empty_rule_list_matches_nothing`), the 5 inverted
  tests, and `A_negated_group_over_a_nested_field_treats_a_null_parent_like_a_negated_leaf_does`. Negation push-down
  reverted alone → **3** (`NOT (A AND unresolved)`, `NOT (A OR unresolved)`, `NOT ((A OR unresolved) AND B)`).
  Dropped-child behaviour restored alone → **6**. **Contract pins** (green on the full revert): `A OR unresolved`,
  `NOT (A AND unresolved)` (the old drop-then-negate happens to give the right rows there), `NOT (A OR B)`,
  `NOT (A AND B)`, `An_enabled_rule_set_skips_its_disabled_members`. Each theory row first asserts
  `RuleSpecification`'s expression and its evaluator agree on the reference answer.
- **Inverted, not deleted (rule 56):** `DisabledRule_ReturnsNull`, `UnknownProperty_ReturnsNull`,
  `EmptyGroup_ReturnsNull`, `Group_AllDisabled_ReturnsNull`, `RuleSet_Disabled_ReturnsNull` → `*_MatchesNothing`.
- **Judgement call — nested fields under group negation.** Push-down puts a group's negation inside the null guard,
  where the leaf's own `IsNegated` already sat, so a negated group over `Address.City` no longer includes rows with a
  null `Address`. That makes the two negation routes agree; whether a negated nested rule *should* include them is
  `SH-M367`, recorded as narrowed (not closed) and left to [[TASK-156]]. Pinned as agreement only.
- **Closed alongside:** `SH-M358` (same defect, filed by the harvest); verdict recorded and noted in [[TASK-156]].
  `RuleEvaluator`'s comment that the converter "still negates naively — TASK-558" updated.
- **Not in scope, recorded:** the converter still **throws** where `RuleSpecification` degrades (string operator on a
  non-string, uncoercible value, null against a non-nullable) — loud, not silent; [[TASK-144]] owns unifying them.
- **Reach:** 0 callers of `RuleExpressionConverter.ToExpression` outside the framework's tests (Framework + Consumers
  grep, 2026-10-11). Solution builds with 0 errors; Birko.Rules.Tests 164/164.
- **Merge gate:** inline review. Public change is the return annotation only (`?` dropped); no nullable warnings.

## Progress log

- step 2 — picked; ranked above TASK-561 (Redis unnamed jobs on a non-"default" queue, P2) on key 1: a dropped child silently widens a filter that can reach Delete(expr) (wrong rows, potentially destructive) while TASK-561 strands jobs on a configuration; TASK-555/556/557 excluded as decision tasks
- step 3 — verified: held as written (BuildLeafExpression returns null for an unresolved field, BuildGroupExpression skips it, group negation is Expression.Not around the survivors, root returns null); harvest twin SH-M358 pulled in
- step 4 — layer: local (Birko.Rules RuleExpressionConverter); decision: match-nothing like the other engines, never null
- step 5 — fix in `Birko.Rules/Expressions/RuleExpressionConverter.cs` (match-nothing as a null node, De Morgan push-down, non-null entry points), comment in `Birko.Rules/Evaluation/RuleEvaluator.cs`; tests in `tests/Birko.Rules.Tests/RuleExpressionConverterAgreementTests.cs` (17) + 1 pin and 5 inverted in `RuleExpressionConverterTests.cs`; Birko.Rules.Tests 164/164, solution builds
- step 6 — full revert 18/164 red; push-down alone 3; dropped-child alone 6; pins named in Outcome
- step 7 — respecced validation-and-rules: *Rule tree to LINQ predicate conversion* and *property resolution* requirements rewritten; scenarios *Disabled input matches nothing*, *Group negation agrees*, *The converter agrees with RuleSpecification and RuleEvaluator*, *An unresolvable field matches nothing*, *An unresolvable child narrows or vanishes like the other engines*, *Both negation routes agree on a null parent* replace the defect-describing ones; docs/rules.md and Birko.Rules/README.md updated
- step 8 — handed to /tasks close --unattended; outcome in status:, commit in git log
