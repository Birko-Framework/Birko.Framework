---
id: TASK-156
parent: STORY-053
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P1
assignee: ai
created: 2026-08-09
depends-on: []
blocks: []
# findings: ids this task remediates, from a review/audit/spec-harvest pass (CR-* SEC-* SH-* VC-*)
findings: [SH-M355, SH-M356, SH-M357, SH-M358, SH-M359, SH-M360, SH-M361, SH-M362, SH-M363, SH-M364, SH-M365, SH-M366, SH-M367, SH-M368, SH-M369, SH-M370, SH-M371, SH-M372, SH-M373, SH-M374, SH-M375, SH-M376]
pr: null
github-issue: null
jira-key: null
---

# Triage the 22 medium spec-harvest findings in `validation-and-rules`

## Context

Filed by `/tasks intake --epic EPIC-014` on 2026-08-09, decomposing [[STORY-053]] — which held all
421 medium findings as an unscheduled checklist. A checklist line is
filed, not scheduled: only `status: todo` **tasks** are ranked by `/tasks pick`, by the `Next up` snapshot,
or by [[fix-next]], so the whole backlog was invisible to every picker. This task owns the **22** findings
that fall in the `validation-and-rules` area.

**Findings:** `SH-M355`–`SH-M376` (contiguous), detailed in
[`SPEC-HARVEST-FINDINGS-2026-07-30.md`](../SPEC-HARVEST-FINDINGS-2026-07-30.md) § Medium severity →
`### area: validation-and-rules`, lines 2614–2747.

**Closed outside this task:** `SH-M358` (an unresolvable rule field is dropped; a lone one makes `ToExpression` return null = match-all) — fixed by [[TASK-558]] (2026-10-11). `SH-M367` was **narrowed, not closed** there: both negation routes now exclude null-parent rows; whether they should is still this task's question.

**The contract under review** is specced in [`docs/specs/validation-and-rules.md`](../../../docs/specs/validation-and-rules.md), harvested from
2 source globs — `../Birko.Validation/**/*.cs`, `../Birko.Rules/**/*.cs`. Every one of
them points into a **sibling repo**, so a fix here normally lands as three commits in three repos (production,
regression suite, this file) per CLAUDE.md § Integration model.

**These are unverified harvester claims, not confirmed defects.** Confirm each against the code before fixing.
The prior to carry in comes from the only findings anyone has checked by hand: of 15 high findings verified at
harvest time, **12 held exactly and 3 needed their scope corrected**, 0 were outright refuted — so expect
roughly a quarter to be imprecise rather than wrong. Refuting on the record is a valid close; a finding
silently dropped is one the next sweep re-raises and someone re-litigates from scratch.

**Ordering constraint — the spec currently documents these defects as shipped behaviour.** The harvest specced
what the code *does*, defects included, which is exactly what let it find them. So a behavioural fix leaves
`docs/specs/validation-and-rules.md` lying until `/specs regen validation-and-rules` runs, and **that spec diff is the fix's evidence**.

## Acceptance criteria

- [x] All 22 findings are marked **confirmed**, **confirmed-narrower**, or **refuted** against the code,
      with the verdict and its evidence (`file:line` + the mechanism, not just the rule) written back into
      `SPEC-HARVEST-FINDINGS-2026-07-30.md`. Every `Verdict:` line names the specific code it traced
- [x] Confirmed findings are fixed with a regression test, or explicitly waived with a recorded reason.
      Findings sharing a root cause are fixed **together**, not one edit each
- [x] Each regression test is **red-verified**: reverting the fix fails it. Report the split as numbers, and
      name any test that passes either way as a contract pin rather than as evidence
- [ ] Any behavioural fix is followed by `/specs regen validation-and-rules`, with the spec diff reviewed as the change's
      evidence
- [x] A confirmed finding too large for this task is spawned via `/tasks spawn` — never left as a ticked box
      with the work undone
- [x] [[STORY-053]]'s **Progress** line and `finding-count` reflect this area's closed count

## Out of scope

- The other 399 medium findings — they belong to the other 21 per-area tasks under [[STORY-053]].
- High-severity findings in this area — [[STORY-051]] owns those.
- Low-severity findings in this area — [[TASK-181]] owns them. Where a fix closes both, do it once and
  cross-reference; don't split one edit across two tasks.
- **Test gaps.** Test coverage was explicitly out of scope for the harvest sweep, so a missing test is not a
  finding here — only a test a confirmed fix needs.
- Per-sub-repo spec trees, which would make this area's staleness measurable from its own repo — that's
  [[TASK-131]].

## Resolution (2026-10-15)

**22 findings triaged: 21 confirmed, 1 refuted.** Fixed here: SH-M359–M362 and M369–M376 (12). Fixed earlier and verified
by reading: SH-M355, M357, M358 (TASK-558), M363, M365 (TASK-116), M368 (TASK-144). Resolved by TASK-144's decisions:
SH-M364 (the translator no longer throws; string ordering stays evaluator-only, documented), M366, M367. Refuted by
design: SH-M356 (an absent field matches nothing, negated or not: TASK-116's invariant, on both engines).

- **Rules engine** (`ComparisonHelper`, contexts): exact comparison of integers and decimals (no double collisions); a
  string read as a number only against a numeric side; `Like` honours `%` only at the ends (a lone `%` is "any value",
  an interior `%` matches nothing, `_` is literal), as the translator does; `DictionaryRuleContext` resolves fields
  case-insensitively. **Also closed:** a negated `Contains null` / `In null`, and `NotIn null`, matched everything in
  memory while the store matched nothing; `CanEvaluate` now covers them.
- **SH-M370, measured worse than filed:** editing a `RuleSet` during evaluation threw, and so did merely copying the
  `List` mid-edit ("Destination array was not long enough", or a cleared slot). `RuleSet.Rules` and `RuleGroup.Rules` are
  now `RuleList`, copy-on-write: edits publish a new array under a lock, reads enumerate the current one. **Source-compatible
  for every caller found** (no consumer uses `List`-only members); assigning a list now copies it.
- **Validation:** `RangeRule` compares numeric bounds by value (the doc example `Range(-50, 150)` on a `double` threw);
  `PropertyRule` is null-safe on nested paths and names them by the whole path (DraCode's two `...MaxParallel` rules
  shared one key); the bulk validating wrapper validates both filtered updates (the `PropertyUpdate` one by preview, so
  `Increment` stays atomic) and materialises batches once; `In` passes null and takes a message; `RegexRule` has a match
  timeout and is no longer `Compiled` by default.

**Red-verified, as numbers.** `ValidationAndRulesTriageTests` (Rules) 7 of 7. `ValidationTriageTests` (Validation) 6 of 6
of those that compile against the old code; the three that use new API (`In` with a message, the regex timeout) cannot.

**Regression:** the 92 suites importing `Birko.Rules` or `Birko.Validation` (5,049 tests), live: 91 green on the first
run. One failure, `ElasticSearchJobQueueLiveTests.Racing_dequeuers_hand_each_job_out_once_and_never_throw`, passed 5 of 5
when re-run alone: an intermittent racing test under the load of the full run, in code this task does not touch
(recorded as a known flake beside the MySQL escape and leader-election ones).

**Spec regen pending**, batched: `validation-and-rules`.

## Human test plan

N/A: fully covered by automated tests; a library contract with no UI surface.

## Implementation plan

_Populated by `/tasks plan TASK-156` — leave empty until then._
