---
id: TASK-321
parent: STORY-051
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P1
assignee: ai
created: 2026-09-08
depends-on: []
blocks: []
# findings: ids this task remediates, from a review/audit/spec-harvest pass (CR-* SEC-* SH-* VC-*)
findings: [SH-H046]
pr: null
github-issue: null
jira-key: null
---

# Triage the 1 remaining high spec-harvest finding in `store-crud-contract`

## Context

Filed by `/tasks intake --epic EPIC-014 --story STORY-051` on 2026-09-08. [[STORY-051]] had **31 task
files covering 18 of its 57 findings**, and the other **39 had no task at all** — so they were
invisible to `/tasks pick`, to the `Next up` snapshot and to [[fix-next]]. *A checklist line is filed,
not scheduled.* This is the same decomposition [[STORY-053]] received on 2026-08-09 for the medium
findings, applied a month later to the tier that outranks it: **high** means silent data loss,
cross-tenant leakage, auth bypass, or a destructive operation on the wrong rows.

This task owns the **1** open finding in the `store-crud-contract` area.

| Finding | Claim | Named location |
|---|---|---|
| `SH-H046` | IStore.Destroy() is documented as "releases all resources" but implementations hard-delete all stored data | `Birko.Data.Stores/IStore.cs:32` |

Detailed in [`SPEC-HARVEST-FINDINGS-2026-07-30.md`](../SPEC-HARVEST-FINDINGS-2026-07-30.md)
§ High severity -> `### area: store-crud-contract`, lines 344-350.

**The contract under review** is specced in [`docs/specs/store-crud-contract.md`](../../../docs/specs/store-crud-contract.md),
harvested from 12 source files — `../Birko.Data.Core/Exceptions/StoreException.cs`, `../Birko.Data.InMemory/Stores/AbstractAsyncInMemoryStore.cs`, `../Birko.Data.InMemory/Stores/AbstractInMemoryStore.cs`, and more. Every one points into a **sibling repo**, so a
fix here normally lands as three commits in three repos (production, regression suite, this file) per
CLAUDE.md § Integration model.

**These are unverified harvester claims, not confirmed defects.** Confirm each against the code before
fixing. The prior to carry in comes from the 15 high findings that *were* checked by hand at harvest
time: **13 CONFIRMED** (one of them re-verified **wider** than filed), **2 CONFIRMED-NARROWER**,
**0 refuted**. So expect most to hold and a meaningful minority to need their scope corrected — and
note that "wider" has happened, so a claim is not a ceiling. Refuting on the record is a valid close; a
finding silently dropped is one the next sweep re-raises and someone re-litigates from scratch.

**Measured consumer reach, 2026-09-08** (so the fix is priced on what it protects, not on the claim's
wording): **Contract-wide.** `IStore` is the framework's central store interface, so the *documentation* defect is visible to every consumer; whether any of them calls `Destroy()` is the thing to measure when triaging.

⚠ Latent is **not** a reason to skip or downweight a finding — the framework's recent history is
largely defects that stayed latent until a consumer selected the backend, and § TASK-219/256 record
that such a window *"closes the moment one does"*. It is a reason not to overstate urgency in a report.

**Why P1:** a documentation/behaviour mismatch on a **destructive** method. Not P0 because nothing mistranslates or leaks; the risk is a caller believing the doc comment.

**Ordering constraint — the spec currently documents these defects as shipped behaviour.** The
harvest specced what the code *does*, defects included, which is exactly what let it find them. So a
behavioural fix leaves `docs/specs/store-crud-contract.md` lying until `/specs regen store-crud-contract` runs, and **that spec
diff is the fix's evidence**.

## Acceptance criteria

- [ ] The finding is marked **confirmed**, **confirmed-narrower**, **confirmed-wider** or **refuted** against
      the code, with the verdict and its evidence (`file:line` + the mechanism, not just the rule)
      written back into `SPEC-HARVEST-FINDINGS-2026-07-30.md`. Every `Verdict:` line names the specific
      code it traced
- [ ] Confirmed findings are fixed with a regression test, or explicitly waived with a recorded reason.
      Findings sharing a root cause are fixed **together**, not one edit each
- [ ] Each regression test is **red-verified**: reverting the fix fails it. Report the split as numbers,
      and name any test that passes either way as a contract pin rather than as evidence
- [ ] ⚠ For a claim of *silent* loss, corruption or leakage, the assertion is the **observed
      state** — rows counted, the value read back, the tenant that could see it — never that no
      exception was thrown. § Conventions records several defects that a "did not throw" assertion
      hid, including one in this epic that hid a live failure for weeks
- [ ] Any behavioural fix is followed by `/specs regen store-crud-contract`, with the spec diff reviewed as the
      change's evidence
- [ ] A confirmed finding too large for this task is spawned via `/tasks spawn` — never left as a
      ticked box with the work undone, and never as an `## Out of scope` sentence describing work
- [ ] [[STORY-051]]'s **Progress** line and its task table reflect this area's closed count

## Out of scope

- The other 38 open high findings — they belong to the other 14 per-area tasks under
  [[STORY-051]].
- The 18 high findings already covered by [[STORY-051]]'s existing 31 task files. If triage shows one of
  those fixes did **not** hold, that is a regression: file it fresh and say so, per `/tasks intake`
  § *Re-running a pass*.
- Medium-severity findings in this area — [[TASK-163]] owns those.
- Low-severity findings in this area — [[TASK-182]] owns them. Where a fix closes findings across
  tiers, do it once and cross-reference; do not split one edit across two tasks.
- **Test gaps.** Test coverage was explicitly out of scope for the harvest sweep, so a missing test is
  not a finding here — only a test a confirmed fix needs.

## Human test plan

Cannot be written yet: which steps a human adds depends on which findings survive triage. **Resolve this
section before `/tasks close`** — an absent plan is not an `N/A` one, and defaulting it parks the
task on a step that may not exist (SKILL.md § Lifecycle). Expected outcome for this area is
`N/A — fully covered by automated tests`, since it is a library contract with no UI surface; write
that explicitly with its reason rather than leaving the section as-is.

## Implementation plan

_Populated by `/tasks plan TASK-321` — leave empty until then._
