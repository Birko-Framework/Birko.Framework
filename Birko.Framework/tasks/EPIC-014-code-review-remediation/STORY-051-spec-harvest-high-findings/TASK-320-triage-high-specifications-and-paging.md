---
id: TASK-320
parent: STORY-051
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
picked-by: fix-next
priority: P1
assignee: ai
created: 2026-09-08
depends-on: []
blocks: []
# findings: ids this task remediates, from a review/audit/spec-harvest pass (CR-* SEC-* SH-* VC-*)
findings: [SH-H045]
pr: null
github-issue: null
jira-key: null
---

# Triage the 1 remaining high spec-harvest finding in `specifications-and-paging`

## Context

Filed by `/tasks intake --epic EPIC-014 --story STORY-051` on 2026-09-08. [[STORY-051]] had **31 task
files covering 18 of its 57 findings**, and the other **39 had no task at all** — so they were
invisible to `/tasks pick`, to the `Next up` snapshot and to [[fix-next]]. *A checklist line is filed,
not scheduled.* This is the same decomposition [[STORY-053]] received on 2026-08-09 for the medium
findings, applied a month later to the tier that outranks it: **high** means silent data loss,
cross-tenant leakage, auth bypass, or a destructive operation on the wrong rows.

This task owns the **1** open finding in the `specifications-and-paging` area.

| Finding | Claim | Named location |
|---|---|---|
| `SH-H045` | RuleGroup.IsNegated is silently dropped, so NOT(a AND b) is translated as (a AND b) | `Birko.Data.Patterns/Specification/RuleSpecification.cs:210` |

Detailed in [`SPEC-HARVEST-FINDINGS-2026-07-30.md`](../SPEC-HARVEST-FINDINGS-2026-07-30.md)
§ High severity -> `### area: specifications-and-paging`, lines 308-342.

**The contract under review** is specced in [`docs/specs/specifications-and-paging.md`](../../../docs/specs/specifications-and-paging.md),
harvested from 10 source files — `../Birko.Data.Patterns/Paging/AsyncPagedRepositoryWrapper.cs`, `../Birko.Data.Patterns/Paging/IPagedRepository.cs`, `../Birko.Data.Patterns/Paging/PagedRepositoryWrapper.cs`, and more. Every one points into a **sibling repo**, so a
fix here normally lands as three commits in three repos (production, regression suite, this file) per
CLAUDE.md § Integration model.

**These are unverified harvester claims, not confirmed defects.** Confirm each against the code before
fixing. The prior to carry in comes from the 15 high findings that *were* checked by hand at harvest
time: **13 CONFIRMED** (one of them re-verified **wider** than filed), **2 CONFIRMED-NARROWER**,
**0 refuted**. So expect most to hold and a meaningful minority to need their scope corrected — and
note that "wider" has happened, so a claim is not a ceiling. Refuting on the record is a valid close; a
finding silently dropped is one the next sweep re-raises and someone re-litigates from scratch.

**Measured consumer reach, 2026-09-08** (so the fix is priced on what it protects, not on the claim's
wording): **Latent for this finding.** `Birko.Data.Patterns` is imported by **8** consumer aggregators, but `RuleSpecification` itself has **0** consumer `.cs` references.

⚠ Latent is **not** a reason to skip or downweight a finding — the framework's recent history is
largely defects that stayed latent until a consumer selected the backend, and § TASK-219/256 record
that such a window *"closes the moment one does"*. It is a reason not to overstate urgency in a report.

**Why P1:** a dropped negation is predicate mistranslation — [[TASK-116]]'s family — but in `RuleSpecification`, which has no injection or match-all dimension.

**Ordering constraint — the spec currently documents these defects as shipped behaviour.** The
harvest specced what the code *does*, defects included, which is exactly what let it find them. So a
behavioural fix leaves `docs/specs/specifications-and-paging.md` lying until `/specs regen specifications-and-paging` runs, and **that spec
diff is the fix's evidence**.

## Acceptance criteria

- [x] The finding is marked **confirmed**, **confirmed-narrower**, **confirmed-wider** or **refuted** against
      the code, with the verdict and its evidence (`file:line` + the mechanism, not just the rule)
      written back into `SPEC-HARVEST-FINDINGS-2026-07-30.md`. Every `Verdict:` line names the specific
      code it traced
- [x] Confirmed findings are fixed with a regression test, or explicitly waived with a recorded reason.
      Findings sharing a root cause are fixed **together**, not one edit each
- [x] Each regression test is **red-verified**: reverting the fix fails it. Report the split as numbers,
      and name any test that passes either way as a contract pin rather than as evidence
- [x] ⚠ For a claim of *silent* loss, corruption or leakage, the assertion is the **observed
      state** — rows counted, the value read back, the tenant that could see it — never that no
      exception was thrown. § Conventions records several defects that a "did not throw" assertion
      hid, including one in this epic that hid a live failure for weeks
- [x] Any behavioural fix is followed by `/specs regen specifications-and-paging`, with the spec diff reviewed as the
      change's evidence
- [x] A confirmed finding too large for this task is spawned via `/tasks spawn` — never left as a
      ticked box with the work undone, and never as an `## Out of scope` sentence describing work
- [x] [[STORY-051]]'s **Progress** line and its task table reflect this area's closed count

## Out of scope

- The other 38 open high findings — they belong to the other 14 per-area tasks under
  [[STORY-051]].
- The 18 high findings already covered by [[STORY-051]]'s existing 31 task files. If triage shows one of
  those fixes did **not** hold, that is a regression: file it fresh and say so, per `/tasks intake`
  § *Re-running a pass*.
- Medium-severity findings in this area — [[TASK-171]] owns those.
- `Birko.Rules.RuleExpressionConverter`, the third translator of the rule model: it negates a group with a plain
  `Not` and drops an unresolved child from an AND group — deferred to [[TASK-558]]. Unifying the translators is
  [[TASK-144]].
- The SQL `RuleConditionConverter`'s empty-group / disabled-rule divergence — deferred to [[TASK-559]].
- Low-severity findings in this area — [[TASK-188]] owns them. Where a fix closes findings across
  tiers, do it once and cross-reference; do not split one edit across two tasks.
- **Test gaps.** Test coverage was explicitly out of scope for the harvest sweep, so a missing test is
  not a finding here — only a test a confirmed fix needs.

## Human test plan

N/A — fully covered by automated tests: the fix is a pure translation of a rule tree into an expression and an
in-memory verdict, with no UI surface. Every case is asserted on the rows selected (and, for the destructive path,
on the rows left in a store after `Delete`), through both engines, and red-verified.

## Outcome

**What was broken.** A rule group marked "negate this whole group" (`RuleGroup.IsNegated`) was treated as if the
flag were not there, by both the query translator (`RuleSpecification.ToExpression()`) and the in-memory evaluator
(`RuleEvaluator`). So a filter meaning "everything except these" selected exactly "these" — and passed to
`Delete(filter)`, deleted the rows it was meant to keep.

**The fix.** Both engines now honour the flag by pushing the negation down to the individual conditions
(De Morgan: NOT(a AND b) → NOT a OR NOT b), in `RuleSpecification.BuildGroupExpression` and
`RuleEvaluator.EvaluateGroup`.

**Step-6 split.** `RuleGroupNegationTests` (Birko.Data.Patterns.Tests, 7) and `RuleEvaluatorGroupNegationTests`
(Birko.Rules.Tests, 8 cases):
- with the flag ignored again (the defect): **5/7** and **6/8** red — fix-dependent: `Negated_and_group_is_its_complement`,
  `Negated_or_group_is_its_complement`, `A_negated_leaf_inside_a_negated_group_cancels_out`,
  `Nested_negated_groups_compose`, `Delete_with_a_negated_group_keeps_the_rows_the_group_selects` (and the evaluator's
  two theories, 3 + 3 cases);
- with a naive `Not` around the whole group (the tempting fix): **2/7** and **2/8** red — fix-dependent:
  `An_unevaluable_leaf_in_a_negated_group_does_not_widen_to_every_row` / `…_is_not_inverted_into_a_match` and
  `A_negated_empty_group_matches_nothing`.
- No test passes under both mutations, so none is only a contract pin. Suites: Patterns 75/75, Rules 146/146,
  Validation 130, EventBus 108, SQL 700, solution build 0 errors.

**Judgement calls.**
- **De Morgan rather than `Not(combined)`.** The obvious one-line fix — wrap the group in `Expression.Not` / invert
  the evaluator's result, exactly what `RuleExpressionConverter` does — was rejected because it reopens TASK-116: an
  unevaluable leaf is a match-none `false`, and `Not` turns `a AND <unevaluable>` into match-all. Pushing negation to
  the leaves reuses the existing rule that a degraded leaf is never inverted. The mutation run above is what shows
  the difference is real (2 tests per engine go red only under the naive fix).
- **A negated empty / all-disabled group stays match-none**, not match-all. Logically NOT(nothing) is everything,
  but an empty group already means "no constraint, match nothing" here, and inverting it would hand a destructive
  call a whole-table predicate built from a group with no conditions in it — the safer reading wins.
- **A negated group whose only condition is an empty `In` set selects every row**, and through
  `ExpressionNormalizer` it folds to `x => true`, which the scope guard reads as a deliberate all-rows request
  (found by the merge-gate review). Kept: NOT(in the empty set) *is* every row — the same set-faithful answer
  TASK-116 chose for a root `NotIn []`, which already reaches that path — so this is a second door to an existing
  decision, not a new widening. With any sibling condition present (`NOT(a AND In [])` → `!a || true`) the guard
  still refuses. Rejected: making an empty-set leaf unevaluable, which would make `NotIn []` match nothing and
  contradict TASK-116's recorded decision.
- **Both engines in one fix**, because `IsSatisfiedBy` routes through the evaluator: fixing only the translator
  would have made the two disagree, which the both-engines tests forbid.

**Flagged, not fixed.**
- [[TASK-558]] — `RuleExpressionConverter` (the third translator) negates groups with a plain `Not` and drops an
  unresolved child from an AND group, widening it; latent (no production caller).
- Measured reach: no consumer constructs a negated group in code; Symbio's IoT alarm/automation rules run through
  `RuleEvaluator` from stored rule data, so a stored negated group there now evaluates correctly (it never did).
- [[TASK-559]] — `Birko.Data.SQL`'s `RuleConditionConverter` (a fourth translator) negates groups soundly through
  SQL `NOT` and refuses unresolved fields, so it does not share this defect; but it turns a disabled rule or an
  empty group into **no condition** (every row) where the other engines match nothing. Latent (no production caller).

## Progress log

- step 2 — picked; ranked above [[TASK-319]] (SH-H038, ES reindex reports success over per-document failures and can delete the source) because both are silent destructive defects of the same severity tier, all four remaining high findings are latent (0 consumer references), and reachability breaks the tie: a rule group is data — often user-authored filter definitions — so a negated group reaching `Delete(filter)` deletes exactly the rows it meant to keep, whereas the reindex loss needs an admin-invoked reindex with `deleteOldIndex: true` and per-document failures. TASK-317 (Cosmos jobs never dequeued) and TASK-318 (Redis non-group reads from `$`) lose work that stays recoverable in storage, so they rank lower on key 1.
- step 3 — verified: held, and wider by one site. `RuleSpecification.BuildGroupExpression` (`Birko.Data.Patterns/Specification/RuleSpecification.cs:333-350`) combines the enabled children and returns without ever reading `group.IsNegated`; `RuleEvaluator.EvaluateGroup` (`Birko.Rules/Evaluation/RuleEvaluator.cs:72-110`) ignores it too, so `IsSatisfiedBy` and `ToExpression` agree with each other — on the wrong answer. A naive `Expression.Not(combined)` would reopen TASK-116's hole: a degraded leaf is `false`, so `NOT(a AND unevaluable)` would become match-all. Fix shape: push negation to the leaves (De Morgan), where the degradation rule already keeps an unevaluable leaf match-none; an empty / all-disabled group stays match-none under negation for the same reason. Sibling `Birko.Rules.RuleExpressionConverter` *does* negate groups (:173) but naively, and drops an unresolved-field child from an AND (widening it) — a separate translator, spawned at close.
- step 4 — layer: local (both sites are framework code; `Birko.Data.Patterns` already references `Birko.Rules`)
- step 5 — fix in `Birko.Data.Patterns/Specification/RuleSpecification.cs` (`BuildExpression` / `BuildLeafExpression` / `BuildGroupExpression` take `negate`) and `Birko.Rules/Evaluation/RuleEvaluator.cs` (private `Evaluate(rule, ctx, negate)`, leaf and group); tests in `tests/Birko.Data.Patterns.Tests/RuleGroupNegationTests.cs` (7) and `tests/Birko.Rules.Tests/RuleEvaluatorGroupNegationTests.cs` (8); suites Patterns 75/75, Rules 146/146, Validation 130/130, EventBus 108/108, SQL 700/700; solution 0 errors
- step 6 — reverted fix (flag ignored): 5/7 + 6/8 failed; naive `Not` around the group: 2/7 + 2/8 failed; fix-dependent = all 15 (each red under one mutation, named in Outcome); contract pins = none
- step 7 — respecced by hand (not `/specs regen`, as TASK-163): `specifications-and-paging` (requirement retitled "…and negates a group by De Morgan"; "Negated group is silently un-negated" → "Negated group is its complement"; + unevaluable-leaf and destructive-call scenarios) and `validation-and-rules` (group-evaluation requirement; "Group-level negation is ignored in memory" → "…is applied in memory"; + inapplicable-leaf scenario)
- step 8 — handed to /tasks close --unattended; outcome in status:, commit in git log

## Implementation plan

_Populated by `/tasks plan TASK-320` — leave empty until then._
