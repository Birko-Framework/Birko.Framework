---
id: TASK-324
parent: STORY-053
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P1
assignee: ai
created: 2026-09-08
depends-on: []
blocks: []
# findings: ids this task remediates, from a review/audit/spec-harvest pass (CR-* SEC-* SH-* VC-*)
findings: [SH-M425]
pr: null
github-issue: null
jira-key: null
---

# Fix the 1 verified medium finding in `store-lazy-initialization`

## Context

Filed by [[TASK-195]] on 2026-09-08, which rated, ID'd and folded in the 16 findings recovered from the
harvest's lost first-schema pass. `store-lazy-initialization` is one of the **three areas that had no per-area task at all**,
because the 44 tasks under [[STORY-053]]/[[STORY-054]] cover only the 22 areas that had rated findings —
and these three are precisely the ones that did not. Folding ids in without creating this task would have
left them exactly as they were: filed, and scheduled by nothing.

⚠ **These are not triage tasks, and that is the one way they differ from their 44 siblings.** Every
finding below was **hand-verified against the code on 2026-09-08** as part of the rating pass, so the
verdict is already in the findings doc. There is no confirm-or-refute step: the work is the fix.

| Finding | Verdict | Claim |
|---|---|---|
| `SH-M425` | CONFIRMED | One-time async init runs under whichever concurrent caller's cancellation token wins the lock race |

Detail — including each verdict and what was measured — in
[`SPEC-HARVEST-FINDINGS-2026-07-30.md`](../SPEC-HARVEST-FINDINGS-2026-07-30.md) § Medium severity →
`### area: store-lazy-initialization`. The recovered originals are in
[`STORY-055/RECOVERED-FINDINGS.md`](../STORY-055-spec-harvest-unrated-areas/RECOVERED-FINDINGS.md).

**The contract under review** is specced in [`docs/specs/store-lazy-initialization.md`](../../../docs/specs/store-lazy-initialization.md). Its
sources point into **sibling repos**, so a fix here normally lands as three commits in three repos
(production, regression suite, this file) per CLAUDE.md § Integration model.

**Why P1:** it sits on `AbstractAsyncStore`, which **every** async store in the framework inherits, and the failure is a shared one-time initialization aborted mid-flight by an unrelated caller's timeout — possibly leaving the backend half-built. Reach is structural rather than by name: only 1 consumer `.cs` file names the type, but every async store is one.

⚠ **The framework already contains the remedy this finding asks for, in a different class.** The finding says an *"initializing latch or an explicit throw would make this diagnosable"*, and [[TASK-270]] built exactly that for `DataBase.IsInitializing` — an `AsyncLocal<bool>` entered through a scope, per call flow **and** per instance, restoring on exception. Reuse that shape rather than inventing a second one; § Conventions records repeatedly that a rule with one statement and two implementations is one that will be got wrong again.

**The token question is the substance:** one-time setup governed by an arbitrary caller's token is the defect, so the fix is `CancellationToken.None` or a token linked across queued callers — decide which, and say why, because the two differ in whether a genuinely cancelled *store* can ever stop initialising.

⚠ Its five siblings from the same recovered area were **duplicates** already owned by [[TASK-163]] and [[TASK-182]] — see § *Coverage gaps* in the findings doc. Do not re-file them here.

**Ordering constraint — the spec documents these defects as shipped behaviour.** The harvest specced what
the code *does*, defects included. So a behavioural fix leaves `docs/specs/store-lazy-initialization.md` lying until
`/specs regen store-lazy-initialization` runs, and **that spec diff is the fix's evidence**.

## Acceptance criteria

- [ ] The finding is fixed with a regression test, or explicitly waived with a recorded reason. Findings sharing a
      root cause are fixed **together**, not one edit each
- [ ] Each regression test is **red-verified**: reverting the fix fails it. Report the split as numbers,
      and name any test that passes either way as a contract pin rather than as evidence
- [ ] ⚠ The assertion is the **observed state**, never that no exception was thrown. § Conventions records
      several defects hidden by exactly that assertion, including one in this epic that hid a live MSSql
      failure for weeks
- [ ] Any public-surface change (a signature, a namespace, a nullable annotation) is preceded by a
      **measured** consumer blast radius across all 16 consumer repos, and the count is written into the
      task — not asserted
- [ ] `/specs regen store-lazy-initialization` run after any behavioural fix, with the spec diff reviewed as the evidence
- [ ] Anything too large for this task is spawned via `/tasks spawn` — never left as a ticked box with the
      work undone, and never as an `## Out of scope` sentence describing work
- [ ] [[STORY-053]]'s task table and `finding-count` reflect this area's closed count

## Out of scope

- **Re-verifying the findings.** Done on 2026-09-08; the verdicts are in the findings doc. If the code has
  moved since and a verdict no longer holds, that is a regression — say so and re-measure rather than
  silently fixing something else.
- The other severity tier of this same area (this area produced no low finding that was not a duplicate).
- **The five recovered findings that were duplicates.** `SLI-1`→`SH-M307`, `SLI-2`/`SLI-3`→`SH-M316`
  ([[TASK-163]]), `SLI-4`→`SH-L297`, `SLI-6`→`SH-L298` ([[TASK-182]]). They are cross-referenced in the
  findings doc, not re-filed here.
- **Test gaps.** Test coverage was out of scope for the harvest sweep, so a missing test is not a finding
  here — only a test a fix needs.

## Human test plan

N/A — library contracts with no UI surface; the proof is the regression tests and their red-verification.

## Implementation plan

_Populated by `/tasks plan TASK-324` — leave empty until then._
