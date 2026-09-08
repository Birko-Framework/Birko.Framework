---
id: TASK-313
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
findings: [SH-H015, SH-H016, SH-H017, SH-H018]
pr: null
github-issue: null
jira-key: null
---

# Triage the 4 remaining high spec-harvest findings in `entity-localization`

## Context

Filed by `/tasks intake --epic EPIC-014 --story STORY-051` on 2026-09-08. [[STORY-051]] had **31 task
files covering 18 of its 57 findings**, and the other **39 had no task at all** — so they were
invisible to `/tasks pick`, to the `Next up` snapshot and to [[fix-next]]. *A checklist line is filed,
not scheduled.* This is the same decomposition [[STORY-053]] received on 2026-08-09 for the medium
findings, applied a month later to the tier that outranks it: **high** means silent data loss,
cross-tenant leakage, auth bypass, or a destructive operation on the wrong rows.

This task owns the **4** open findings in the `entity-localization` area.

| Finding | Claim | Named location |
|---|---|---|
| `SH-H015` | Create/Update on a non-default culture writes the localized text into the default-culture column | `Birko.Data.Localization/Decorators/LocalizedStoreWrapper.cs:83` |
| `SH-H016` | ApplyTranslations mutates the entity in place, corrupting stores that return live instances | `Birko.Data.Localization/Decorators/LocalizedStoreWrapper.cs:218` |
| `SH-H017` | Filter-based Update on a non-default culture overwrites the default-culture base column | `Birko.Data.Localization/Decorators/LocalizedBulkStoreWrapper.cs:174` |
| `SH-H018` | Filter-based Update/Delete/PropertyUpdate never rewrite the filter, so a localized predicate hits the base column | `Birko.Data.Localization/Decorators/AsyncLocalizedBulkStoreWrapper.cs:181` |

Detailed in [`SPEC-HARVEST-FINDINGS-2026-07-30.md`](../SPEC-HARVEST-FINDINGS-2026-07-30.md)
§ High severity -> `### area: entity-localization`, lines 130-154.

**The contract under review** is specced in [`docs/specs/entity-localization.md`](../../../docs/specs/entity-localization.md),
harvested from 12 source files — `../Birko.Data.Localization/Decorators/AsyncLocalizedBulkStoreWrapper.cs`, `../Birko.Data.Localization/Decorators/AsyncLocalizedStoreWrapper.cs`, `../Birko.Data.Localization/Decorators/LocalizedBulkStoreWrapper.cs`, and more. Every one points into a **sibling repo**, so a
fix here normally lands as three commits in three repos (production, regression suite, this file) per
CLAUDE.md § Integration model.

**These are unverified harvester claims, not confirmed defects.** Confirm each against the code before
fixing. The prior to carry in comes from the 15 high findings that *were* checked by hand at harvest
time: **13 CONFIRMED** (one of them re-verified **wider** than filed), **2 CONFIRMED-NARROWER**,
**0 refuted**. So expect most to hold and a meaningful minority to need their scope corrected — and
note that "wider" has happened, so a claim is not a ceiling. Refuting on the record is a valid close; a
finding silently dropped is one the next sweep re-raises and someone re-litigates from scratch.

**Measured consumer reach, 2026-09-08** (so the fix is priced on what it protects, not on the claim's
wording): **Latent.** `LocalizedStoreWrapper`: **0** consumer `.cs` files. `Birko.Data.Localization` is imported by Symbio's aggregator, so it compiles there but nothing constructs the decorator.

⚠ Latent is **not** a reason to skip or downweight a finding — the framework's recent history is
largely defects that stayed latent until a consumer selected the backend, and § TASK-219/256 record
that such a window *"closes the moment one does"*. It is a reason not to overstate urgency in a report.

**Why P1:** data corruption is real but confined to consumers who wrap a store in the localization decorator; no cross-tenant or auth dimension.

**Ordering constraint — the spec currently documents these defects as shipped behaviour.** The
harvest specced what the code *does*, defects included, which is exactly what let it find them. So a
behavioural fix leaves `docs/specs/entity-localization.md` lying until `/specs regen entity-localization` runs, and **that spec
diff is the fix's evidence**.

## Acceptance criteria

- [ ] All 4 findings are marked **confirmed**, **confirmed-narrower**, **confirmed-wider** or **refuted** against
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
- [ ] Any behavioural fix is followed by `/specs regen entity-localization`, with the spec diff reviewed as the
      change's evidence
- [ ] A confirmed finding too large for this task is spawned via `/tasks spawn` — never left as a
      ticked box with the work undone, and never as an `## Out of scope` sentence describing work
- [ ] [[STORY-051]]'s **Progress** line and its task table reflect this area's closed count

## Out of scope

- The other 35 open high findings — they belong to the other 14 per-area tasks under
  [[STORY-051]].
- The 18 high findings already covered by [[STORY-051]]'s existing 31 task files. If triage shows one of
  those fixes did **not** hold, that is a regression: file it fresh and say so, per `/tasks intake`
  § *Re-running a pass*.
- Medium-severity findings in this area — [[TASK-168]] owns those.
- Low-severity findings in this area — [[TASK-194]] owns them. Where a fix closes findings across
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

_Populated by `/tasks plan TASK-313` — leave empty until then._
