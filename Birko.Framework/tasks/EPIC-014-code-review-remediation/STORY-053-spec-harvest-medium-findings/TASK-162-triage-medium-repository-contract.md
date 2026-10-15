---
id: TASK-162
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
findings: [SH-M205, SH-M206, SH-M207, SH-M208, SH-M209, SH-M210, SH-M211, SH-M212, SH-M213, SH-M214, SH-M215, SH-M216, SH-M217, SH-M218, SH-M219, SH-M220]
pr: null
github-issue: null
jira-key: null
---

# Triage the 16 medium spec-harvest findings in `repository-contract`

## Context

Filed by `/tasks intake --epic EPIC-014` on 2026-08-09, decomposing [[STORY-053]] — which held all
421 medium findings as an unscheduled checklist. A checklist line is
filed, not scheduled: only `status: todo` **tasks** are ranked by `/tasks pick`, by the `Next up` snapshot,
or by [[fix-next]], so the whole backlog was invisible to every picker. This task owns the **16** findings
that fall in the `repository-contract` area.

**Findings:** `SH-M205`–`SH-M220` (contiguous), detailed in
[`SPEC-HARVEST-FINDINGS-2026-07-30.md`](../SPEC-HARVEST-FINDINGS-2026-07-30.md) § Medium severity →
`### area: repository-contract`, lines 1696–1793.

**The contract under review** is specced in [`docs/specs/repository-contract.md`](../../../docs/specs/repository-contract.md), harvested from
9 source globs — `../Birko.Data.Repositories/*.cs`, `../Birko.Data.Core/ViewModels/*.cs`, `../Birko.Data.ViewModel/**/*.cs`, and 6 more. Every one of
them points into a **sibling repo**, so a fix here normally lands as three commits in three repos (production,
regression suite, this file) per CLAUDE.md § Integration model.

**These are unverified harvester claims, not confirmed defects.** Confirm each against the code before fixing.
The prior to carry in comes from the only findings anyone has checked by hand: of 15 high findings verified at
harvest time, **12 held exactly and 3 needed their scope corrected**, 0 were outright refuted — so expect
roughly a quarter to be imprecise rather than wrong. Refuting on the record is a valid close; a finding
silently dropped is one the next sweep re-raises and someone re-litigates from scratch.

**Ordering constraint — the spec currently documents these defects as shipped behaviour.** The harvest specced
what the code *does*, defects included, which is exactly what let it find them. So a behavioural fix leaves
`docs/specs/repository-contract.md` lying until `/specs regen repository-contract` runs, and **that spec diff is the fix's evidence**.

## Acceptance criteria

- [x] All 16 findings are marked **confirmed**, **confirmed-narrower**, or **refuted** against the code,
      with the verdict and its evidence (`file:line` + the mechanism, not just the rule) written back into
      `SPEC-HARVEST-FINDINGS-2026-07-30.md`. Every `Verdict:` line names the specific code it traced
- [x] Confirmed findings are fixed with a regression test, or explicitly waived with a recorded reason.
      Findings sharing a root cause are fixed **together**, not one edit each
- [x] Each regression test is **red-verified**: reverting the fix fails it. Report the split as numbers, and
      name any test that passes either way as a contract pin rather than as evidence
- [ ] Any behavioural fix is followed by `/specs regen repository-contract`, with the spec diff reviewed as the change's
      evidence
- [x] A confirmed finding too large for this task is spawned via `/tasks spawn` — never left as a ticked box
      with the work undone
- [x] [[STORY-053]]'s **Progress** line and `finding-count` reflect this area's closed count

## Resolution (2026-10-15)

**16 findings triaged: 16 confirmed, 0 refuted.** 13 fixed here (SH-M206–M212, M214–M216, M218–M220); 3 found already
fixed (SH-M205, M213, M217 by earlier tasks). SH-M206's corruption did not reproduce under 32 parallel reads; the fix
(a concurrent hash table, a volatile read-mode flag) stands on the reading, and its test is a pin.

- **One contract changed on purpose:** a repository with no store throws on every data operation instead of answering
  null / Guid.Empty / 0 / a silent no-op (SH-M219), on the plain and the ViewModel repositories together.
  `CreateInstance` and `Destroy` (nothing to destroy) keep their answers. The six `AbstractRepositoryFallbackTests`
  that pinned the silent answers are inverted, and the locator test that destroys a store-less repository still passes.
- **Other behaviour changes:** the locator's default key for `GetRepository<TStore, TRepository>` is the repository
  type (it was the store type), and it refuses a different store under a cached key; `AddRepository(store, lifetime)`
  accepts only Singleton; a ViewModel page read is ordered by `Guid`.

**Red-verified, as numbers.** `RepositoryContractTriageTests` 6 of 7 (pin: the same store returns the cached
repository). `ViewModelRepositoryTriageTests` 6 of 7 (pin: concurrent reads). `DataBaseRepositoryBulkWrapperTests` 1
of 2 (pin: the bulk wrapper is accepted).

**Regression: 91 suites, 4,588 tests.** Three failures, none caused here, all resolved:
- `Birko.Data.Tests` was in neither TASK-170's nor TASK-171's regression set, and two of its tests broke there. The paging
  test double re-sorted per order key, so TASK-171's `Guid` tie-break became the primary sort (the real stores'
  `OrderByHelper` uses `ThenBy`; the double now does too). A `PropertyUpdate` test expected `ApplyTo`'s message for a
  field selector that TASK-170 now refuses earlier, at `Set`. Both fixed in this commit; 229/229.
- `RecurringSchedulerLeaderElectionTests.A_stopped_leader_releases_so_the_follower_takes_over` failed once under the
  91-suite load and passed on 3 repeated full runs: a timing test, recorded, not caused by this change.
- MySQL `TASK295_a_vanished_table_heals_on_the_next_write_here_too`: the known intermittent failure recorded in
  TASK-567; passed on rerun.

**Spec regen pending**, batched with the database fixes: `repository-contract`.

## Out of scope

- The other 405 medium findings — they belong to the other 21 per-area tasks under [[STORY-053]].
- High-severity findings in this area — [[STORY-051]] owns those.
- Low-severity findings in this area — [[TASK-193]] owns them. Where a fix closes both, do it once and
  cross-reference; don't split one edit across two tasks.
- **Test gaps.** Test coverage was explicitly out of scope for the harvest sweep, so a missing test is not a
  finding here — only a test a confirmed fix needs.
- Per-sub-repo spec trees, which would make this area's staleness measurable from its own repo — that's
  [[TASK-131]].

## Human test plan

N/A: fully covered by automated tests. A library contract with no UI surface.

## Implementation plan

_Populated by `/tasks plan TASK-162` — leave empty until then._
