---
id: TASK-116
parent: STORY-051
feature: FEATURE-014
status: done
priority: P0
assignee: ai
picked-by: fix-next
created: 2026-07-30
depends-on: []
blocks: []
# One fix, two production repos + one test repo (polyrepo — see CLAUDE.md § Integration model).
# None resolve under `git show` from this aggregator.
pr: >-
  Birko.Data.Patterns@87c0ed3, Birko.Rules@839d712, Birko.Data.Patterns.Tests@27f5be6
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

- [x] `Like`, `In` and `NotIn` leaves produce correct filters — matching `RuleExpressionConverter`'s
      semantics — for `Read`, `Count`, `Update(filter, …)` and `Delete(filter)`
- [x] `Status In [1,2]` on a populated store deletes **only** the matching rows, asserted end-to-end
- [x] A negated `Contains` / `NotContains` on a **non-string** member does not match every row (SH-H043 /
      SH-H044's real trigger)
- [x] A **disabled** rule does not translate to match-all; `ToExpression()` and `IsSatisfiedBy` agree
      (SH-H042)
- [x] An unresolved field stays match-none — the case SH-H044's verdict says is already correct is asserted,
      so the fix cannot regress it
- [x] No degraded leaf can be negated into match-all; a leaf the translator cannot express throws on the
      destructive paths
- [x] `ToExpression()` and `IsSatisfiedBy` agree for **every** case in this task — one parameterised test
      over both
- [x] Regression tests in `Birko.Data.Patterns.Tests`, plus an end-to-end delete case proving the
      destructive path narrows
- [x] `/specs regen` for `specifications-and-paging`, spec diff reviewed

## Out of scope

- `SH-H023` — the SQL-**text** rule path (`RuleConditionConverter`), a different translator. [[TASK-111]].
- `SH-H021`/`SH-H022`/`SH-H026`/`SH-H027` — the same match-all species in the SQL and ElasticSearch
  expression translators. Unverified; separate tasks, but worth verifying as a set since the fix rationale
  here transfers directly.
- Unifying `RuleSpecification` and `RuleExpressionConverter` into one converter. Worth doing, but it is a
  refactor with its own risk and should not ride along on a correctness fix — file it if the port makes the
  duplication obvious.

## Outcome

**What the fix is.** A rule specification could turn an ordinary filter into "every row". `Like`, `In` and
`NotIn` — all three declared `ComparisonOperator` values — had no arm in the expression translator and fell
to `Expression.Constant(true)`, so `Status In [1,2]` matched everything; a disabled root rule did the same;
and any leaf that degraded to a match-none constant (a string operator on a non-string member, a value that
would not convert, unconvertible `Between` bounds) inverted to match-all the moment it was negated. Since
`ToExpression()` is the filter for bulk `Update`/`Delete`, each of those emptied tables.

**Why it outranked the rest of the pool.** `x => true` is exactly the node TASK-109's `IsExplicitAllRows`
whitelists as a *deliberate* whole-table request. So this did not merely slip past the guard shipped the day
before — it was classified as an explicit all-rows instruction and routed through the clean
`DeleteAll()` door, with no exception and a success return.

**The shape of the fix.** Not per-site constants but a tracked degradation flag: `Leaf.Unevaluable()` marks
a leaf that *could not be evaluated*, distinct from one that is *legitimately false*, and negation is
applied only to the latter. That is what makes the invariant hold for degradation sites nobody has written
yet — two sites beyond the four filed findings had already forgotten it. `Like`/`In`/`NotIn` were ported
from `Birko.Rules`' `RuleExpressionConverter`, and an operator with no arm now throws instead of degrading.

**Step-6 split — run twice, because the fix landed in two projects.**

- Reverting `Birko.Data.Patterns`: **16 of 50 failed.** Fix-dependent: the four `In`/`NotIn`/empty-set
  cases, four `Like_translates_its_anchors` cases, `Like_with_an_interior_wildcard_…`,
  `NotContains_on_a_non_string_member_…`, `Negated_Contains_on_a_non_string_member_…`,
  `A_negated_comparison_with_an_unconvertible_value_…`, `A_negated_Between_with_unconvertible_bounds_…`,
  `A_disabled_root_rule_matches_nothing_…`, `An_untranslatable_operator_throws_…`, and both end-to-end
  delete cases.
- Reverting `Birko.Rules`: **5 of 63 failed**, all `ToExpression_and_IsSatisfiedBy_agree` cases.
- **Contract pins, NOT evidence (2):** `A_disabled_child_inside_a_group_is_still_skipped_…` and
  `An_unresolved_field_matches_nothing_even_when_negated`. Both were already correct before this task; both
  were labelled pins **in the test source before the revert ran**.
- Final: `Birko.Data.Patterns.Tests` 63/63, `Birko.Rules.Tests` 138/138.

**Judgement calls, and the stricter option rejected.**

- **An unknown operator throws rather than degrading.** The alternative — degrade to match-none — is safer
  in isolation and was rejected: an operator with no arm is a gap in *code*, not a property of the data, and
  silently filtering nothing is how `In` sat broken long enough to be specced as intended behaviour.
- **The empty `In` set is a real predicate, not a degradation.** It must stay invertible, so `NotIn` over an
  empty set correctly matches every row. Marking it degraded would have been the blanket-safe choice and
  would have made `NotIn []` wrongly match nothing — mirrors the SQL empty-`IN`/`NOT IN` decision.
- **A null string member keeps a real (negatable) predicate.** Only the degraded cases are frozen; treating
  the null guard as degraded would have broken `NotContains` over a null string, which has a passing test.
- **The evaluator moved, not the expression path** (user decision, 1 of 3 offered). The expression path runs
  against a database and no portable translation of `column.ToString().Contains(…)` exists across four SQL
  providers and ElasticSearch, so it could not follow. Cost: `21 Contains "1"` no longer answers true.
  All 138 `Birko.Rules` tests still passed, which is the strongest available evidence that behaviour was a
  `ToString()` artefact rather than a contract.

**Flagged, not fixed.**

- **A pre-existing build break, not mine, blocked the whole task.** `Birko.Data.Patterns.Tests` did not
  compile on `main`: `IAsyncBulkRepository<T>` gained `ReadFirstAsync` in `Birko.Data.Repositories` and a
  hand-rolled test double was never updated. Verified pre-existing by stashing this task's change. The
  interface, its base and the double live in **three separate repos**, so no single build covers them —
  the same polyrepo blind spot as the token-parity drift. Fixed minimally (one line) because nothing could
  be tested otherwise; it is not part of this defect.
- **A narrow residual divergence remains, documented in code.** A null-valued *non-string* member (e.g.
  `int? Qty = null`) still answers `true` for `NotContains` in the evaluator while the expression path says
  match-none — only the typed side can distinguish a null `string` from a null `int?`. Closing it needs the
  declared property type plumbed into the rule context, which is larger than this correctness fix.
- **`RuleSpecification` and `RuleExpressionConverter` remain two translators of one rule model**, now
  provably divergent in maintenance cost (the `Like`/`In`/`NotIn` arms were ported by hand). Unifying them
  was already out of scope here and is worth its own task.
- **EPIC-017 (`tenant-isolation-hardening`) is stamped `review-intake` but contains zero tasks** — its
  findings were filed and never scheduled, which is exactly roadmap DV12. Surfaced, not acted on.

## Progress log

- 2026-08-07 — **step 2 — picked.** Ranked above [[TASK-126]] (cross-tenant tagging assertion), which sits a
  tier higher on the severity ladder but has the pool's weakest reachability: the framework ships **no**
  implementation of `TagServiceBase` (only the abstract base and `ITagService`), so its leak needs a consumer
  to write a hook that omits the filter — hardening against a future third-party error, not a live defect.
  This one is confirmed, fires on **documented** API usage (`In` is a declared `ComparisonOperator`), is
  maximally silent, and is one file. Also ranked above [[TASK-112]] (named as "next" by the previous session
  before the full pool was enumerated): silent data loss, but a four-provider type-mapping build carrying an
  unresolved design question, so it fails the self-containment key and risks stalling mid-session.
- 2026-08-07 — **it defeats the guard TASK-109 shipped yesterday, verified both sides before picking.**
  `ToExpression()` is `Expression.Lambda<Func<T,bool>>(body, param)` (`:31-36`), so a single leaf degrading
  to `Constant(true)` produces literally `x => true`. TASK-109's `IsExplicitAllRows` whitelists exactly that
  node as a *deliberate* all-rows request and routes `Delete(filter)` to `Connector.DeleteAll(typeof(T))`.
  So an ordinary `In` rule passed to bulk delete does not merely slip past the new guard — it is classified
  as an explicit whole-table request and cleanly empties the table. Recorded here because it raises the
  severity above what the filed finding claims.
- 2026-08-07 — **step 3 — verified: all four hold, one NARROWER than filed, and a fifth site found.**
  SH-H041 holds exactly (`_ => Constant(true)` catches `Like`/`In`/`NotIn`, all three declared operators).
  SH-H043 holds (`BuildStringMethod` → `Constant(false)` for a non-string member, `NotContains` wraps it in
  `Expression.Not` → every row; the evaluator's `ToString()`-based `!ContainsString` genuinely filters, so the
  two engines disagree). SH-H044 holds exactly as the task's own correction already narrowed it — the
  `property is null` branch returns before both the switch and the negation, so it is fine.
  **SH-H042 is narrower than filed:** `BuildGroupExpression` already filters disabled children
  (`group.Rules.Where(r => r.IsEnabled)`) and returns `Constant(false)` for an all-disabled group, agreeing
  with `RuleEvaluator.EvaluateGroup`. So the `Constant(true)` is reachable **only for a disabled ROOT rule**
  handed straight to `ToExpression()`, not for a disabled rule inside a group as the finding's wording
  implies. Still severe — a disabled root is literally `x => true` and TASK-109's guard waves it through.
- 2026-08-07 — **a fifth site of the same species, not among the four findings.** `BuildComparison` (`:113`)
  and `BuildBetween` (`:126`) also degrade to `Constant(false)` on an unconvertible value, and both reach
  `if (rule.IsNegated) expr = Expression.Not(expr)` — so a **negated** comparison with a mistyped value is
  match-all too. Same function, same root cause, and criterion 6 ("no degraded leaf can be negated into
  match-all") already covers it as written, so it is pulled in rather than spawned.
- 2026-08-07 — **step 4 — layer: local** to `Birko.Data.Patterns`. `Birko.Rules`' `RuleExpressionConverter`
  is the reference for the missing operators, not a dependency to change for them.
- 2026-08-07 — **step 5 — fix in `Birko.Data.Patterns/Specification/RuleSpecification.cs`; tests in
  `Birko.Data.Patterns.Tests/RuleSpecificationMatchAllTests.cs` (18 new); suite 50/50 green.** The fix is a
  tracked degradation flag (`Leaf.Unevaluable()`), not per-site constants: a leaf that could not be evaluated
  matches nothing and **stays** matching nothing under negation, so the invariant holds for degradation sites
  nobody has written yet. Added `Like`/`In`/`NotIn` ported from `RuleExpressionConverter`; an operator with no
  arm now **throws** instead of falling to a constant.
- 2026-08-07 — **a pre-existing build break had to be cleared first, and it was not mine.**
  `Birko.Data.Patterns.Tests` did **not compile on main**: `IAsyncBulkRepository<T>` gained `ReadFirstAsync`
  in `Birko.Data.Repositories` and the hand-rolled `ConcurrencyProbeRepository` double was never updated.
  Verified pre-existing by stashing this task's change and rebuilding. The interface, its abstract base and
  the double live in **three separate repos**, which is why no single build caught it — the same polyrepo
  blind spot as the token-parity drift. One line added to the double.
- 2026-08-07 — **step 6 — reverted fix: 16 of 50 failed.** Fix-dependent (16): `In_filters_instead_of_matching_every_row`,
  `NotIn_is_the_complement_of_In`, `In_over_an_empty_set_…`, `Like_translates_its_anchors` (×4 cases),
  `Like_with_an_interior_wildcard_…`, `NotContains_on_a_non_string_member_…`,
  `Negated_Contains_on_a_non_string_member_…`, `A_negated_comparison_with_an_unconvertible_value_…`,
  `A_negated_Between_with_unconvertible_bounds_…`, `A_disabled_root_rule_matches_nothing_…`,
  `An_untranslatable_operator_throws_…`, `Delete_with_an_In_specification_…`,
  `Delete_with_a_disabled_specification_…`.
  **Contract pins, NOT evidence (2):** `A_disabled_child_inside_a_group_is_still_skipped_…` and
  `An_unresolved_field_matches_nothing_even_when_negated`. Both pass against the pre-fix code because both
  were already correct — the group path already filtered disabled children, and the unresolved-field return
  already preceded the negation. Both were labelled as pins **in the test source before the revert ran**, not
  reclassified afterwards.
- 2026-08-07 — **criterion 7 is blocked on a decision that spans a second project — asked, not guessed.**
  With this fix a non-string `Contains` is match-none in the expression path, while
  `ComparisonHelper.ContainsString` does `actual.ToString()!.Contains(...)`, so the evaluator answers **true**
  for `Quantity Contains "1"` on `Quantity = 21`. The two engines still disagree on the *positive* form (the
  negated form, which was the dangerous one, now agrees at match-none). Closing it fully means changing
  `Birko.Rules`' evaluator semantics, so it is not folded into a correctness fix unasked.
- 2026-08-07 — **decision taken: the evaluator adopts match-none** (asked, option 1 of 3). A string operator
  on a non-string member is now false in **both** polarities in `ComparisonHelper`. The expression path could
  not move to meet the evaluator: it runs against a database and no portable translation of
  `column.ToString().Contains(...)` exists across four SQL providers and ElasticSearch. **All 138
  `Birko.Rules` tests still pass** — nothing depended on the stringify behaviour, which is the strongest
  evidence available that the given-up `21 Contains "1"` -> true was an artefact rather than a contract.
- 2026-08-07 — **the agreement test immediately found the SAME defect on the evaluator side.**
  `EvaluateLeaf` did `match = rule.IsNegated ? !match : match`, negating a `false` that meant *"this
  operator does not apply to this member"* into a match — SH-H043's exact species, in the engine nobody had
  filed a finding against. Fixed by mirroring the expression side: `ComparisonHelper.CanEvaluate(actual, op)`
  is the evaluator's `Leaf.Degraded`, and negation now consults it. The two engines enforce the invariant the
  same way rather than agreeing by coincidence. **This is what criterion 7 was for** — the parameterised
  both-engines test was not paperwork, it was the thing that found it.
- 2026-08-07 — **step 6 (second half) — reverted the `Birko.Rules` change: 5 of 63 failed**, all
  `ToExpression_and_IsSatisfiedBy_agree` cases (`Quantity` x `Contains` positive / `Contains` negated /
  `NotContains` / `StartsWith` / `Like`). Restored: **63/63 green**, and `Birko.Rules.Tests` 138/138.
- 2026-08-07 — **step 7 — respecced TWO areas, not the one criterion 9 named.** The fix spans
  `Birko.Data.Patterns` *and* `Birko.Rules`, so `validation-and-rules` needed it as well as
  `specifications-and-paging`; the criterion was written before the evaluator change existed. No map gap
  this time — both areas' globs already reached every changed file. **Two requirement TITLES asserted the
  defect and were rewritten:** *"Leaf negation is applied after operator translation"* (it specified
  negating `Constant(false)`/`Constant(true)` results — the bug itself) and *"A disabled root rule
  translates to match-all while in-memory evaluation reports no match"*. Also rewrote the operator table
  (it specified `_ => Constant(true)`), the non-convertible and string-method requirements, and the
  evaluator's string-operator requirement. Diff reviewed: every change traces to an acceptance row,
  **nothing unexplained, so no findings spawned**.
- 2026-08-07 — **step 8 — closed `done`; Birko.Data.Patterns@87c0ed3, Birko.Rules@839d712, tests@27f5be6.**

## Human test plan

N/A — covered by automated tests. The end-to-end delete assertions cover the real consequence.
