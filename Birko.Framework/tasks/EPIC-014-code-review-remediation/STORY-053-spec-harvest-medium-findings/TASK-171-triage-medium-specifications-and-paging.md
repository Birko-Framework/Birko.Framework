---
id: TASK-171
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
findings: [SH-M290, SH-M291, SH-M292, SH-M293, SH-M294, SH-M295, SH-M296, SH-M297, SH-M298, SH-M299, SH-M300, SH-M301]
pr: null
github-issue: null
jira-key: null
---

# Triage the 12 medium spec-harvest findings in `specifications-and-paging`

## Context

Filed by `/tasks intake --epic EPIC-014` on 2026-08-09, decomposing [[STORY-053]] — which held all
421 medium findings as an unscheduled checklist. A checklist line is
filed, not scheduled: only `status: todo` **tasks** are ranked by `/tasks pick`, by the `Next up` snapshot,
or by [[fix-next]], so the whole backlog was invisible to every picker. This task owns the **12** findings
that fall in the `specifications-and-paging` area.

**Findings:** `SH-M290`–`SH-M301` (contiguous), detailed in
[`SPEC-HARVEST-FINDINGS-2026-07-30.md`](../SPEC-HARVEST-FINDINGS-2026-07-30.md) § Medium severity →
`### area: specifications-and-paging`, lines 2216–2289.

**The contract under review** is specced in [`docs/specs/specifications-and-paging.md`](../../../docs/specs/specifications-and-paging.md), harvested from
2 source globs — `../Birko.Data.Patterns/Specification/*.cs`, `../Birko.Data.Patterns/Paging/*.cs`. Every one of
them points into a **sibling repo**, so a fix here normally lands as three commits in three repos (production,
regression suite, this file) per CLAUDE.md § Integration model.

**These are unverified harvester claims, not confirmed defects.** Confirm each against the code before fixing.
The prior to carry in comes from the only findings anyone has checked by hand: of 15 high findings verified at
harvest time, **12 held exactly and 3 needed their scope corrected**, 0 were outright refuted — so expect
roughly a quarter to be imprecise rather than wrong. Refuting on the record is a valid close; a finding
silently dropped is one the next sweep re-raises and someone re-litigates from scratch.

**Ordering constraint — the spec currently documents these defects as shipped behaviour.** The harvest specced
what the code *does*, defects included, which is exactly what let it find them. So a behavioural fix leaves
`docs/specs/specifications-and-paging.md` lying until `/specs regen specifications-and-paging` runs, and **that spec diff is the fix's evidence**.

## Acceptance criteria

- [x] All 12 findings are marked **confirmed**, **confirmed-narrower**, or **refuted** against the code,
      with the verdict and its evidence (`file:line` + the mechanism, not just the rule) written back into
      `SPEC-HARVEST-FINDINGS-2026-07-30.md`. Every `Verdict:` line names the specific code it traced
- [x] Confirmed findings are fixed with a regression test, or explicitly waived with a recorded reason.
      Findings sharing a root cause are fixed **together**, not one edit each
- [x] Each regression test is **red-verified**: reverting the fix fails it. Report the split as numbers, and
      name any test that passes either way as a contract pin rather than as evidence
- [x] Any behavioural fix is followed by `/specs regen specifications-and-paging`, with the spec diff reviewed as the change's — done 2026-10-16 (batched regen of 17 areas, diffs reviewed and confirmed)
      evidence
- [x] A confirmed finding too large for this task is spawned via `/tasks spawn` — never left as a ticked box
      with the work undone
- [x] [[STORY-053]]'s **Progress** line and `finding-count` reflect this area's closed count

## Resolution (2026-10-15)

**12 findings triaged: 11 confirmed, 1 confirmed-narrower, 0 refuted.** 9 fixed here (SH-M290, M291, M292, M293,
M294, M296, M298, M300, M301: M291 is the narrower one). 3 owned by the open [[TASK-144]] (two translators, one rule
model): SH-M295, M297 and M299, each with the conflict that makes it a unification decision written into its verdict.

- **Measured as the two engines' selections**: each finding was first a probe comparing `ToExpression().Compile()` with
  `IsSatisfiedBy` over the same entities, under `sk-SK` for the culture findings. 13 of 14 probe shapes disagreed or threw.
- **One fix tried and reverted.** Refusing an unresolvable field at construction (for SH-M297) broke 11 pinned
  tests: TASK-116's three-engine agreement says an unresolved field matches nothing, even negated. Two deliberate
  rules collide there, so SH-M297 went to TASK-144 instead of being forced.
- **Behaviour changes, recorded:** every paged read sorts (by the caller's order, then `Guid`); `NotContains` with a
  null needle matches nothing in memory too.

**Red-verified, as numbers.** `SpecificationPagingTriageTests` 9 of 9.

**Spec regen pending**, batched with the database fixes: `specifications-and-paging`.

## Out of scope

- The other 409 medium findings — they belong to the other 21 per-area tasks under [[STORY-053]].
- High-severity findings in this area — [[STORY-051]] owns those.
- Low-severity findings in this area — [[TASK-188]] owns them. Where a fix closes both, do it once and
  cross-reference; don't split one edit across two tasks.
- **Test gaps.** Test coverage was explicitly out of scope for the harvest sweep, so a missing test is not a
  finding here — only a test a confirmed fix needs.
- Per-sub-repo spec trees, which would make this area's staleness measurable from its own repo — that's
  [[TASK-131]].

## Human test plan

N/A: fully covered by automated tests. A library contract with no UI surface; every fix is asserted by what
both rule engines select over the same entities, and by the order and offset paging hands the repository.

## Implementation plan

_Populated by `/tasks plan TASK-171` — leave empty until then._
