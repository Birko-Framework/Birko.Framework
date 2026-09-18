---
id: TASK-327
parent: STORY-054
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P2
assignee: ai
created: 2026-09-08
depends-on: []
blocks: []
# findings: ids this task remediates, from a review/audit/spec-harvest pass (CR-* SEC-* SH-* VC-*)
findings: [SH-L389, SH-L390, SH-L391]
pr: null
github-issue: null
jira-key: null
---

# Fix the 3 verified low findings in `unit-of-work-and-transactions`

## Context

Filed by [[TASK-195]] on 2026-09-08, which rated, ID'd and folded in the 16 findings recovered from the
harvest's lost first-schema pass. `unit-of-work-and-transactions` is one of the **three areas that had no per-area task at all**,
because the 44 tasks under [[STORY-053]]/[[STORY-054]] cover only the 22 areas that had rated findings —
and these three are precisely the ones that did not. Folding ids in without creating this task would have
left them exactly as they were: filed, and scheduled by nothing.

⚠ **These are not triage tasks, and that is the one way they differ from their 44 siblings.** Every
finding below was **hand-verified against the code on 2026-09-08** as part of the rating pass, so the
verdict is already in the findings doc. There is no confirm-or-refute step: the work is the fix.

| Finding | Verdict | Claim |
|---|---|---|
| `SH-L389` | CONFIRMED | `SqlUnitOfWork` is declared in the global namespace |
| `SH-L390` | CONFIRMED | `SqlTransactionContext`'s constructor performs no null validation |
| `SH-L391` | CONFIRMED | `response.OriginalException!` is null-suppressed but can be null on a ServerError-only response |

Detail — including each verdict and what was measured — in
[`SPEC-HARVEST-FINDINGS-2026-07-30.md`](../SPEC-HARVEST-FINDINGS-2026-07-30.md) § Low severity →
`### area: unit-of-work-and-transactions`. The recovered originals are in
[`STORY-055/RECOVERED-FINDINGS.md`](../STORY-055-spec-harvest-unrated-areas/RECOVERED-FINDINGS.md).

**The contract under review** is specced in [`docs/specs/unit-of-work-and-transactions.md`](../../../docs/specs/unit-of-work-and-transactions.md). Its
sources point into **sibling repos**, so a fix here normally lands as three commits in three repos
(production, regression suite, this file) per CLAUDE.md § Integration model.

**Why P2:** none of the three changes behaviour today. `SH-L389` is reachable as it stands (a global-namespace type needs no using, and `SqlUnitOfWork.FromStore` is in live use), `SH-L390` turns a bad argument into an NRE later instead of an `ArgumentNullException` now, and `SH-L391` is a misleading null-suppression that is legal at runtime.

⚠ **`SH-L389` is the one with a blast radius, and it is the opposite of what it looks like.** Moving `SqlUnitOfWork` into `Birko.Data.SQL.UnitOfWork` is the obviously-correct fix **and it breaks every existing caller**, which currently resolves the type with no using at all. Measure the consumer count before doing it (3 `.cs` files name it today) and land the using in the same change, per the polyrepo three-commit rule.

`SH-L390` should follow whatever [[TASK-323]] decides about guard-versus-throw, so the framework has one answer to *"what does a null argument do"* rather than two.

**Ordering constraint — the spec documents these defects as shipped behaviour.** The harvest specced what
the code *does*, defects included. So a behavioural fix leaves `docs/specs/unit-of-work-and-transactions.md` lying until
`/specs regen unit-of-work-and-transactions` runs, and **that spec diff is the fix's evidence**.

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
- [ ] `/specs regen unit-of-work-and-transactions` run after any behavioural fix, with the spec diff reviewed as the evidence
- [ ] Anything too large for this task is spawned via `/tasks spawn` — never left as a ticked box with the
      work undone, and never as an `## Out of scope` sentence describing work
- [ ] [[STORY-054]]'s task table and `finding-count` reflect this area's closed count

## Out of scope

- **Re-verifying the findings.** Done on 2026-09-08; the verdicts are in the findings doc. If the code has
  moved since and a verdict no longer holds, that is a regression — say so and re-measure rather than
  silently fixing something else.
- The other severity tier of this same area — its medium findings are [[TASK-325]].
- **The five recovered findings that were duplicates.** `SLI-1`→`SH-M307`, `SLI-2`/`SLI-3`→`SH-M316`
  ([[TASK-163]]), `SLI-4`→`SH-L297`, `SLI-6`→`SH-L298` ([[TASK-182]]). They are cross-referenced in the
  findings doc, not re-filed here.
- **Test gaps.** Test coverage was out of scope for the harvest sweep, so a missing test is not a finding
  here — only a test a fix needs.

## Human test plan

N/A — library contracts with no UI surface; the proof is the regression tests and their red-verification.

## Implementation plan

_Populated by `/tasks plan TASK-327` — leave empty until then._
