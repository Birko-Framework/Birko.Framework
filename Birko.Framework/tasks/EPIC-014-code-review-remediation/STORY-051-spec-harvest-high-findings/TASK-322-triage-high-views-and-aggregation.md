---
id: TASK-322
parent: STORY-051
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P1
assignee: ai
picked-by: fix-next
created: 2026-09-08
depends-on: []
blocks: []
# findings: ids this task remediates, from a review/audit/spec-harvest pass (CR-* SEC-* SH-* VC-*)
findings: [SH-H055]
pr: "Birko.Data.CosmosDB.Views 3fba6e6 · tests cd6868c"
github-issue: null
jira-key: null
---

# Triage the 1 remaining high spec-harvest finding in `views-and-aggregation`

## Context

Filed by `/tasks intake --epic EPIC-014 --story STORY-051` on 2026-09-08. [[STORY-051]] had **31 task
files covering 18 of its 57 findings**, and the other **39 had no task at all** — so they were
invisible to `/tasks pick`, to the `Next up` snapshot and to [[fix-next]]. *A checklist line is filed,
not scheduled.* This is the same decomposition [[STORY-053]] received on 2026-08-09 for the medium
findings, applied a month later to the tier that outranks it: **high** means silent data loss,
cross-tenant leakage, auth bypass, or a destructive operation on the wrong rows.

This task owns the **1** open finding in the `views-and-aggregation` area.

| Finding | Claim | Named location |
|---|---|---|
| `SH-H055` | CosmosFilterTranslator.Translate catches everything and returns "", silently dropping the WHERE clause | `Birko.Data.CosmosDB.Views/CosmosViewStore.cs:358` |

Detailed in [`SPEC-HARVEST-FINDINGS-2026-07-30.md`](../SPEC-HARVEST-FINDINGS-2026-07-30.md)
§ High severity -> `### area: views-and-aggregation`, lines 424-430.

**The contract under review** is specced in [`docs/specs/views-and-aggregation.md`](../../../docs/specs/views-and-aggregation.md),
harvested from 41 source files — `../Birko.Data.CosmosDB.Views/CosmosViewManager.cs`, `../Birko.Data.CosmosDB.Views/CosmosViewStore.cs`, `../Birko.Data.ElasticSearch.Views/ElasticSearchViewIndexResolver.cs`, and more. Every one points into a **sibling repo**, so a
fix here normally lands as three commits in three repos (production, regression suite, this file) per
CLAUDE.md § Integration model.

**These are unverified harvester claims, not confirmed defects.** Confirm each against the code before
fixing. The prior to carry in comes from the 15 high findings that *were* checked by hand at harvest
time: **13 CONFIRMED** (one of them re-verified **wider** than filed), **2 CONFIRMED-NARROWER**,
**0 refuted**. So expect most to hold and a meaningful minority to need their scope corrected — and
note that "wider" has happened, so a claim is not a ceiling. Refuting on the record is a valid close; a
finding silently dropped is one the next sweep re-raises and someone re-litigates from scratch.

**Measured consumer reach, 2026-09-08** (so the fix is priced on what it protects, not on the claim's
wording): **Possibly live.** `CosmosViewStore` is referenced from **1** consumer `.cs` file — check what that reference does before ranking this one purely latent.

⚠ Latent is **not** a reason to skip or downweight a finding — the framework's recent history is
largely defects that stayed latent until a consumer selected the backend, and § TASK-219/256 record
that such a window *"closes the moment one does"*. It is a reason not to overstate urgency in a report.

**Why P1:** a catch-all that returns `""` and drops the WHERE is the match-all family again, on the CosmosDB view path only.

**Ordering constraint — the spec currently documents these defects as shipped behaviour.** The
harvest specced what the code *does*, defects included, which is exactly what let it find them. So a
behavioural fix leaves `docs/specs/views-and-aggregation.md` lying until `/specs regen views-and-aggregation` runs, and **that spec
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
      — met as far as offline allows: every assertion is the **emitted SQL**, which is the observable
      state of a query builder, and none is "it did not throw". `WHERE false` and the WHERE-present /
      WHERE-absent pair are the direct captures. ⚠ Counting actual **rows** would need a live Cosmos
      account; the Human test plan records that as the one step a consumer selecting this backend owes
- [x] Any behavioural fix is followed by `/specs regen views-and-aggregation`, with the spec diff reviewed as the
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
- Medium-severity findings in this area — [[TASK-151]] owns those.
- Low-severity findings in this area — [[TASK-180]] owns them. Where a fix closes findings across
  tiers, do it once and cross-reference; do not split one edit across two tasks.
- **Test gaps.** Test coverage was explicitly out of scope for the harvest sweep, so a missing test is
  not a finding here — only a test a confirmed fix needs.
- The **string-escaping injection** in `TranslateValue` (escapes `'` and not the backslash) —
  [[TASK-447]] owns it. Different root cause from this finding, measured and spawned rather than
  folded in.
- The **`{filter}` interpolation** in `ElasticSearchViewStore` and the contradiction between
  § TASK-308 and § TASK-310 about what a rendered tree exposes — [[TASK-448]] owns both.

## Human test plan

**N/A - fully covered by automated tests.** The change is a query-string builder with no UI and no
network surface, and the assertions are the emitted SQL read back off the builders. The
`Birko.Data.CosmosDB.Views` seam has no deployment reaching it today (Symbio wires it through a
`DataProvider.CosmosDB` switch case and every environment is configured SQLite), so there is nothing
a human could exercise that the 9 tests do not.

⚠ What a human *would* need to do when a consumer selects Cosmos: run one real aggregate query with a
filter and confirm it is scoped. Building the statement is fully offline; only executing it needs a
server, and the refusal path has never been exercised against a live account.

## Implementation plan

_Populated by `/tasks plan TASK-322` — leave empty until then._

## Progress log

- step 2 - picked; ranked above TASK-314 (migrations) on key 1: SH-H055 claims **cross-tenant
  leakage** (a `v => v.TenantGuid == x` filter returning other tenants' rows), where TASK-314's worst
  claim SH-H032 is an unbounded destructive write, one band lower. Also wins key 3 (a bare
  `catch { return ""; }` is total silence) and key 4 (one finding, one file, against five findings
  across four unrelated backend projects). Runner-up for key 2 as well: not purely latent - 1 consumer
  .cs file references CosmosViewStore, against 0 for most of this pool.
- step 3 - verified against source. **CONFIRMED as filed**, and the per-backend audit says it is
  CosmosDB Views **only**: `CosmosFilterTranslator.Translate` (`CosmosViewStore.cs:356-365`) wraps the
  whole recursion in `catch { return string.Empty; }`, and both consumers
  (`BuildAggregateSql:224`, `BuildCountAggregateSql:266`) then test
  `if (!string.IsNullOrEmpty(whereClause))` and emit **no WHERE at all**. Reached by all three public
  methods (`QueryAsync`, `QueryFirstAsync`, `CountAsync`) on the aggregate path only; the non-aggregate
  LINQ path hands the predicate to the driver and is unaffected. The other view backends were checked
  and are clean: ElasticSearch already throws (CR-H047 -> TASK-268), MongoDB renders through
  `Builders<TView>.Filter`, RavenDB uses `.Where(filter)`. A framework-wide sweep for the same
  catch-all-returning-empty shape found exactly one other hit, in a different area with a different
  meaning (`TenantSyncProvider:1006`, a version-hash fallback).
- step 3b - **two things the finding does not name, both found in the same function:**
  (a) `x => true` currently *works by accident* - `ConstantExpression` hits the `_ =>` throw arm, is
  swallowed, and the empty result correctly means "no constraint". So a naive "just throw" is a
  regression on a working call, and CLAUDE.md's own rule is that a false refusal is worse than the hole.
  (b) The mirror case is a silent wrong answer the finding missed: `x => false` takes the identical
  path and therefore matches **everything** instead of nothing. Same root cause - `string.Empty` is
  overloaded across "no filter", "translation failed" and "always true" - so both are in scope here
  (§ TASK-308's *the empty state is OVERLOADED*, arriving in a second translator).
- step 4 - layer: **local**, in `Birko.Data.CosmosDB.Views`. The ES sibling already settled the shape
  for this exact family, so this is a reuse rather than a new decision.
- step 5 - fix in `Birko.Data.CosmosDB.Views/CosmosViewStore.cs` (one file, four edits):
  `Translate` stops swallowing; an explicit `x => true` body gets its own early return so the one
  case that worked keeps working; `TranslateExpression` gains a `ConstantExpression { Value: bool }`
  arm so a constant renders as a real literal in any position; and `TranslateValue`'s compile step
  reports an unevaluatable operand as `NotSupportedException` rather than leaking
  `InvalidOperationException` from `Compile()`. Tests in
  `Birko.Data.CosmosDB.Views.Tests/CosmosViewFilterFailOpenTests.cs` (9). Suite 15/15 green
  (6 pre-existing + 9 new), 0 warnings under `-warnaserror`.
- step 5b - **measured before the fix was written**: the new tests run against unfixed code gave
  **4 of 14 red**. The pair that settles step 3b's two unfiled claims:
  `An_always_true_constant_still_matches_everything` **passed** unfixed (so it really did work by
  accident, and a naive throw would have regressed it) while
  `An_always_false_constant_matches_NOTHING_rather_than_everything` **failed** (so `x => false` really
  did match every document).
- step 6 - four disjoint mutations, all reverted, each attributing a distinct piece:
  **(A) the `catch { return string.Empty; }` restored** -> `3 of 15` red, exactly the fail-open trio
  (`An_untranslatable_filter_is_REFUSED_and_never_silently_dropped`,
  `The_count_path_refuses_the_same_filter`,
  `A_TENANT_filter_that_cannot_be_translated_does_not_return_other_tenants_rows`).
  **(B) the `ConstantExpression` arm removed** -> `2 of 15` red
  (`An_always_false_constant_matches_NOTHING_rather_than_everything`,
  `A_boolean_constant_NESTED_in_a_conjunction_renders_as_a_literal`) and the fail-open trio stays
  green - the two halves are independent.
  **(C) the top-level always-true door removed** -> `1 of 15` red
  (`An_always_true_constant_still_matches_everything`), which is the regression a naive fix ships.
  **(D) the `TranslateValue` wrap removed** -> `1 of 15` red, the tenant test, because the
  column-vs-column operand then surfaces `InvalidOperationException` from `Compile()` instead of the
  family's `NotSupportedException`.
  **Contract pins, not evidence** (green either way, each present for a stated reason):
  `A_translatable_filter_still_emits_its_WHERE` and `A_null_filter_still_emits_no_WHERE` - a fix that
  refused everything, or one that emitted a clause for a null filter, would satisfy every test above.

## Outcome

**What was wrong.** `CosmosFilterTranslator.Translate` wrapped its entire recursion in
`catch { return string.Empty; }`. Both consumers - `BuildAggregateSql` and `BuildCountAggregateSql` -
append the WHERE only when the clause is non-empty, so any predicate the translator could not express
produced **no WHERE at all** and the aggregate ran over every document in the container. A
tenant-scoped filter therefore returned other tenants' rows, silently. It is the same fail-open
CR-H047 closed on the ElasticSearch side and TASK-268 generalised, on the last backend still holding
it - and the per-backend audit confirms it was only this one.

**What was done**, in one file:

1. `Translate` no longer swallows. Every internal `NotSupportedException` now reaches the caller.
2. An explicit `x => true` body returns an empty clause from the **entry point**, so the one case that
   worked keeps working.
3. `TranslateExpression` gained a `ConstantExpression { Value: bool }` arm, so a constant renders as a
   real SQL literal wherever it appears - including nested inside a conjunction, where an empty
   fragment would be joined between the `AND` separators into invalid SQL.
4. `TranslateValue` reports an unevaluatable operand as `NotSupportedException` instead of leaking
   `InvalidOperationException` out of `Compile()`, excluding cancellation from the rewrap.

**Finding.** `SH-H055` **CONFIRMED**; verdict and evidence written into
`SPEC-HARVEST-FINDINGS-2026-07-30.md`. Spec regenerated: the requirement previously titled
*"Cosmos DB filter translation is best-effort and fails open"* is now
*"...refuses what it cannot express"*, its two defect-describing scenarios are inverted, and four
scenarios were added (count path, unevaluatable operand, and the two boolean constants).

**Step-6 split.** Four disjoint mutations, each attributing a distinct piece - see the Progress log
for names. The measurement that matters most was taken *before* the fix existed: 4 of 14 red, with
`x => true` passing and `x => false` failing, which is what established that one accident had to be
preserved and one silent wrong answer had to be fixed.

**Judgement calls, and why the stricter option lost.**

- **"Just stop swallowing" was measured and rejected.** It is the minimal reading of the finding and
  it regresses `x => true`, which worked. CLAUDE.md's own rule is that a false refusal breaks working
  code and is worse than the hole, so the constant got a door instead.
- **The always-true door is at the entry point, not in the recursion.** Putting it in the recursion is
  the smaller diff and produces `(c.Amount > 0 AND )` for a nested constant - § TASK-137's hazard,
  exactly. The nested case has its own test.
- **`x => false` renders `WHERE false` rather than being refused.** Refusing would be defensible
  (nobody writes it deliberately) and it is a silent wrong answer today, so the fix that makes it
  *correct* beats the one that makes it *loud*.
- **The rewrap in `TranslateValue` is narrow.** It catches around `Compile`/`DynamicInvoke` only, and
  excludes `OperationCanceledException` - rewrapping a cancellation is the defect § TASK-291 records.
- **The refusal message carries the node type, never the expression** - and measuring *why* corrected
  a claim in § Conventions. TASK-308 says a rendered tree "interpolates the values a closure
  captured"; TASK-310 says a captured local renders as `value(<>c__DisplayClass…).field`. They
  disagree, so I measured: **the captured local is NOT leaked** (`value(…DisplayClass3_0).secret`)
  while an **inline literal is** (`v.Name == "hunter2-INLINE-LITERAL"`), along with type and member
  names. So TASK-310 is right for a raw tree and TASK-308's wording holds only for a *normalized* one,
  where funcletization has folded the capture to its value. Using `NodeType` is correct either way,
  which is why this fix needed no re-work - but the ElasticSearch sibling does interpolate `{filter}`,
  and the contradiction is now owned by [[TASK-448]] rather than left as a sentence.

**Flagged, and spawned.** [[TASK-447]] (P0) - `TranslateValue` escapes `'` and not the backslash, so
a filter *value* breaks out of its own literal. Measured rather than reasoned: `a\' OR 1=1 --`
renders as `'a\\' OR 1=1 --'`, the literal terminates early, and the remainder is parsed as SQL.
Different root cause from this finding (escaping, not swallowing), so it is its own task. That task
also carries the question of whether these values should be **parameterised** instead - both call
sites already build a `QueryDefinition`, so unlike the identifier family this sink has a real
parameter mechanism available.
- step 7 - respecced `views-and-aggregation` (targeted regen, stable wording). Requirement **renamed and inverted**: *Cosmos DB filter translation is best-effort and fails open* -> *...refuses what it cannot express*; its two defect-describing scenarios inverted (*silently drops the filter* -> *is refused*; *constant on the left is not translated* -> *is refused*); four scenarios added (count path, unevaluatable operand, always-true, always-false, nested constant). Stamp and the `../Birko.Data.CosmosDB.Views` source-commit refreshed. Nothing unintended in the diff.
- step 7b - `CLAUDE.md` gains its `### Recent Updates` entry (convention gate check 9).
- step 8 - closed done; out-of-scope sweep: 5 boundary, 2 spawned ([[TASK-447]] P0, [[TASK-448]] P3), 0 declined.
