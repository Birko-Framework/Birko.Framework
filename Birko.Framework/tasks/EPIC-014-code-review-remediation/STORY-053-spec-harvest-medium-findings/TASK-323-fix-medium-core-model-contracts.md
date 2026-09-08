---
id: TASK-323
parent: STORY-053
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P2
assignee: ai
created: 2026-09-08
depends-on: []
blocks: []
# findings: ids this task remediates, from a review/audit/spec-harvest pass (CR-* SEC-* SH-* VC-*)
findings: [SH-M422, SH-M423, SH-M424]
pr: null
github-issue: null
jira-key: null
---

# Fix the 3 verified medium findings in `core-model-contracts`

## Context

Filed by [[TASK-195]] on 2026-09-08, which rated, ID'd and folded in the 16 findings recovered from the
harvest's lost first-schema pass. `core-model-contracts` is one of the **three areas that had no per-area task at all**,
because the 44 tasks under [[STORY-053]]/[[STORY-054]] cover only the 22 areas that had rated findings —
and these three are precisely the ones that did not. Folding ids in without creating this task would have
left them exactly as they were: filed, and scheduled by nothing.

⚠ **These are not triage tasks, and that is the one way they differ from their 44 siblings.** Every
finding below was **hand-verified against the code on 2026-09-08** as part of the rating pass, so the
verdict is already in the findings doc. There is no confirm-or-refute step: the work is the fix.

| Finding | Verdict | Claim |
|---|---|---|
| `SH-M422` | CONFIRMED-NARROWER | `AbstractLogModel.CopyTo` is an overload, not an override, so timestamps are dropped via a base-typed reference |
| `SH-M423` | CONFIRMED-NARROWER | `AbstractLogModel.LoadFrom` is an overload, not an override, so `ILoadable<IGuidEntity>` dispatch discards the timestamps the argument carries |
| `SH-M424` | CONFIRMED | `CopyTo` with a null or omitted target returns `this`, so the "clone" aliases the source |

Detail — including each verdict and what was measured — in
[`SPEC-HARVEST-FINDINGS-2026-07-30.md`](../SPEC-HARVEST-FINDINGS-2026-07-30.md) § Medium severity →
`### area: core-model-contracts`. The recovered originals are in
[`STORY-055/RECOVERED-FINDINGS.md`](../STORY-055-spec-harvest-unrated-areas/RECOVERED-FINDINGS.md).

**The contract under review** is specced in [`docs/specs/core-model-contracts.md`](../../../docs/specs/core-model-contracts.md). Its
sources point into **sibling repos**, so a fix here normally lands as three commits in three repos
(production, regression suite, this file) per CLAUDE.md § Integration model.

**Why P2:** all three are latent: measured 2026-09-08, **0** `.CopyTo(` call sites in the framework's non-test code and no `ICopyable<AbstractModel>`-typed variable anywhere, and every `LoadFrom` call site is on a concrete model type so the overload resolves statically to the most-derived one. Real API defects on a base every model inherits, with no shipped path taking the broken dispatch today.

⚠ **These three are one decision, not three edits.** `SH-M422` and `SH-M423` are the same mistake (a derived method that looks like an override and is not) and `SH-M424` is the reason the base cannot simply be made overridable — `AbstractModel` is abstract, so a `CopyTo` with no target has nothing to allocate. Fixing the two dispatch findings without settling the null-target contract leaves `ICopyable`'s XML doc still false. Decide the contract first: **throw** on a null target, or **drop** the optional parameter and the doc claim together.

⚠ **And a signature change here is not free.** `CopyTo`/`LoadFrom` are public surface on the base of every Birko model. Measure the consumer blast radius before changing an arity or a parameter type — § TASK-255 records that a same-typed inserted parameter let two call sites **silently rebind** while the rest failed loudly, and § TASK-260 records that changing the *type* is the change the compiler cannot miss.

**Ordering constraint — the spec documents these defects as shipped behaviour.** The harvest specced what
the code *does*, defects included. So a behavioural fix leaves `docs/specs/core-model-contracts.md` lying until
`/specs regen core-model-contracts` runs, and **that spec diff is the fix's evidence**.

## Acceptance criteria

- [ ] All 3 findings are fixed with a regression test, or explicitly waived with a recorded reason. Findings sharing a
      root cause are fixed **together**, not one edit each
- [ ] Each regression test is **red-verified**: reverting the fix fails it. Report the split as numbers,
      and name any test that passes either way as a contract pin rather than as evidence
- [ ] ⚠ The assertion is the **observed state**, never that no exception was thrown. § Conventions records
      several defects hidden by exactly that assertion, including one in this epic that hid a live MSSql
      failure for weeks
- [ ] Any public-surface change (a signature, a namespace, a nullable annotation) is preceded by a
      **measured** consumer blast radius across all 16 consumer repos, and the count is written into the
      task — not asserted
- [ ] `/specs regen core-model-contracts` run after any behavioural fix, with the spec diff reviewed as the evidence
- [ ] Anything too large for this task is spawned via `/tasks spawn` — never left as a ticked box with the
      work undone, and never as an `## Out of scope` sentence describing work
- [ ] [[STORY-053]]'s task table and `finding-count` reflect this area's closed count

## Out of scope

- **Re-verifying the findings.** Done on 2026-09-08; the verdicts are in the findings doc. If the code has
  moved since and a verdict no longer holds, that is a regression — say so and re-measure rather than
  silently fixing something else.
- The other severity tier of this same area — its low finding is [[TASK-326]].
- **The five recovered findings that were duplicates.** `SLI-1`→`SH-M307`, `SLI-2`/`SLI-3`→`SH-M316`
  ([[TASK-163]]), `SLI-4`→`SH-L297`, `SLI-6`→`SH-L298` ([[TASK-182]]). They are cross-referenced in the
  findings doc, not re-filed here.
- **Test gaps.** Test coverage was out of scope for the harvest sweep, so a missing test is not a finding
  here — only a test a fix needs.

## Human test plan

N/A — library contracts with no UI surface; the proof is the regression tests and their red-verification.

## Implementation plan

_Populated by `/tasks plan TASK-323` — leave empty until then._
