---
id: TASK-559
parent: STORY-051
feature: FEATURE-014
status: done
priority: P2
assignee: ai
picked-by: fix-next
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
- **Rescoped at step 3 (2026-10-11): the nested case is not harmless.** `RuleSpecification` answers an empty or
  all-disabled group with `false` at **any** depth, and that `false` is never inverted by an enclosing negation (it
  negates by De Morgan down to the leaves, TASK-320). The SQL converter drops the group instead, so `A AND <empty>`
  selects `A` where the spec selects nothing, and because it negates with SQL `NOT` around a sub-condition, any
  constant it did emit would be inverted by a negated parent. Matching the other engines therefore needs the
  converter to push negation to the leaves too, not just to emit an always-false term at the root. Whether a destructive call can receive it depends on the SQL scope guard (CLAUDE-conventions rules
  1-3), which should refuse an unconditioned `DELETE` / `UPDATE` — measure that rather than assume it.

Reach, measured 2026-10-11: **no production caller** of `RuleConditionConverter` outside tests. Latent — P2.

## Acceptance criteria

- [x] Decide what a disabled root / empty group means to the SQL converter — match-none like the other engines
      (an always-false condition), or a refusal — and record it in the converter's docs
- [x] Measure whether a `Delete` / bulk `Update` fed an empty condition list from this converter is refused by the
      scope guard today; record the answer either way
- [x] Tests pin the decision; `DisabledRule_ReturnsEmpty` / `DisabledRuleSet_ReturnsEmpty` inverted, not deleted, if
      it changes; and the converter agrees with `RuleSpecification` / `RuleEvaluator` on the cases this covers
- [x] `/specs regen filter-expression-translation`

## Out of scope

- Unifying the translators — [[TASK-144]]; `RuleExpressionConverter`'s dropped child — [[TASK-558]].

## Human test plan

N/A — library code with no UI surface; fully covered by automated tests.

## Implementation plan

_Populated by `/tasks plan TASK-559` — leave empty until then._

## Outcome

**Fixed, and wider than filed.** The SQL rule converter now selects the same rows as the other two rule engines. A
disabled rule or an empty group used to mean "no filter" — every row on a read — and now means "nothing". While
proving that, a plain `A OR B` turned out to return only rows matching both: **every OR rule group the SQL converter
ever produced ran as AND** (`SH-M128`, already filed under [[TASK-153]]). Same function, fixed in the same edit.

- **Decision (acceptance 1) — match-none, not refusal.** It is what `RuleSpecification` (TASK-116) and `RuleEvaluator`
  answer, and a disabled rule is a valid, expressible configuration rather than an untranslatable node (rule 42 is
  about the latter). Rendered as an `IN` over no values → `1 = 0`, the existing always-false term, which the
  whole-table guard reads as a constraint. Recorded in the converter's comment.
- **Measured (acceptance 2):** before the fix, `connector.Delete(type, ToConditions(disabledRule))` threw
  `WholeTableWriteException` — the connector's `WouldTargetEveryRow` refuses an empty list — so the destructive half was
  loud, and the live harm was the read. Now it deletes nothing and does not throw
  (`A_delete_through_a_disabled_rule_deletes_nothing_instead_of_being_refused`).
- **Rescoped at step 3:** the nested case was not harmless (`A AND <empty>`), and matching the other engines required
  pushing negation to the leaves — a SQL `NOT` around a sub-condition inverts an always-false member into match-all.
- **Step-6 split (77 tests: `RuleConverterMatchNoneTests` 17 on SQLite + `RuleConditionConverterTests` /
  `RuleFieldResolutionTests` 60):** full revert → **18/77** red (14 + 4). OR fix reverted alone → **5**
  (`A OR B`, `A OR (B AND C)`, `(A OR B) AND (B OR C)`, `A_rule_set_whose_later_member_is_an_or_group_ands_it_with_the_rest`,
  `An_or_group_of_valid_fields_still_resolves_every_leaf`). Match-none reverted alone → **11** (the 6 empty/disabled
  shapes, `A_disabled_rule_set_selects_nothing`, the delete test, the 3 inverted unit tests).
  `NOT ((A OR empty) AND B)` and `NOT (A OR B)` are red only on the full revert (they need the De Morgan push-down).
  **Contract pins** (green throughout): `A OR empty`, `NOT (A AND empty)`,
  `An_enabled_rule_set_still_ands_its_enabled_rules`. Each theory row also asserts `RuleSpecification`'s expression and
  evaluator agree on the reference answer.
- **Inverted, not deleted (rule 56):** `DisabledRule_ReturnsEmpty` / `DisabledRuleSet_ReturnsEmpty` →
  `*_ReturnsMatchNone`; `A_disabled_rule_carrying_a_payload_is_skipped_not_emitted` asserted an empty result too and now
  asserts the match-none (the payload is still never emitted). `An_or_group_of_valid_fields_still_resolves_every_leaf`
  navigates one extra level: a top-level OR group is wrapped in an AND term so its `IsOr` cannot join it to a sibling
  in the flat chain.
- **Wider, same edit:** `SetOr` and `ConvertLeaf`'s dead `isOr` branch removed (`SH-L141`, rule 53); verdicts recorded
  on `SH-M128` / `SH-L141` and noted in [[TASK-153]] / [[TASK-189]].
- **Reach:** 0 callers of `RuleConditionConverter` outside the framework's tests (grep over Framework + Consumers,
  2026-10-11). Latent — both defects shipped and were never exercised.
- Suites: SQL 710/710, SQLite 457/457.
- **Merge gate:** inline review. No public signature change (only private members changed); no nullable warnings.

## Progress log

- step 2 — picked; ranked above TASK-558 (RuleExpressionConverter drops a child) on key 1: a disabled root / empty group becomes no WHERE (every row), a potential unbounded destructive write, while TASK-558 is a wrong result; TASK-555/556/557 excluded as cross-provider decision tasks; TASK-562 (in-progress, not picked-by fix-next) left alone
- step 3 — verified, rescoped: root case holds; nested case also diverges (empty group inside AND is dropped, spec answers false; SQL `NOT` wrapping would invert a constant). Destructive writes fed an empty list are already refused (`WholeTableWriteException`, connector `WouldTargetEveryRow`), so the live harm is a read returning every row. Context corrected; acceptance unchanged
- step 4 — layer: local (Birko.Data.SQL RuleConditionConverter); decision: match-none, as RuleSpecification / RuleEvaluator, rendered as an empty IN (`1 = 0`)
- step 5 — fix in `Birko.Data.SQL/SQL/Conditions/RuleConditionConverter.cs` (match-none term, De Morgan push-down, OR groups as `OrSubCondition`, `SetOr` + dead `isOr` removed); tests in `tests/Birko.Data.SQL.SqLite.Tests/RuleConverterMatchNoneTests.cs` (17), 3 inverted + 1 re-navigated in `tests/Birko.Data.SQL.Tests`; SQL 710/710, SQLite 457/457
- step 6 — full revert 18/77 red; OR fix alone 5; match-none alone 11; pins named in Outcome
- step 7 — respecced filter-expression-translation: *Rules-to-SQL condition conversion* requirement rewritten (match-none, De Morgan, OrSubCondition); scenarios *Disabled rule matches nothing*, *A delete through a disabled rule deletes nothing*, *The SQL converter agrees with the in-memory engines* added; *Empty group*, *OR rule group*, *Negated group* scenarios replaced; docs/rules.md note
- step 8 — handed to /tasks close --unattended; outcome in status:, commit in git log
