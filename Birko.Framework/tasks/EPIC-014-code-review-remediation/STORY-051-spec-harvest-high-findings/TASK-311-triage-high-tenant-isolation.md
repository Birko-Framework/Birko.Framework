---
id: TASK-311
parent: STORY-051
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P0
assignee: ai
created: 2026-09-08
depends-on: []
blocks: []
# findings: ids this task remediates, from a review/audit/spec-harvest pass (CR-* SEC-* SH-* VC-*)
findings: [SH-H049, SH-H053]
pr: null
github-issue: null
jira-key: null
---

# Triage the 2 remaining high spec-harvest findings in `tenant-isolation`

## Context

Filed by `/tasks intake --epic EPIC-014 --story STORY-051` on 2026-09-08. [[STORY-051]] had **31 task
files covering 18 of its 57 findings**, and the other **39 had no task at all** — so they were
invisible to `/tasks pick`, to the `Next up` snapshot and to [[fix-next]]. *A checklist line is filed,
not scheduled.* This is the same decomposition [[STORY-053]] received on 2026-08-09 for the medium
findings, applied a month later to the tier that outranks it: **high** means silent data loss,
cross-tenant leakage, auth bypass, or a destructive operation on the wrong rows.

This task owns the **2** open findings in the `tenant-isolation` area.

| Finding | Claim | Named location |
|---|---|---|
| `SH-H049` | UseTenantMiddleware binds ITenantContext from the root provider, so a Scoped registration is never observed | `Birko.Data.Tenant/Middleware/TenantMiddleware.cs:222` |
| `SH-H053` | AddEventTenantScope() binds Tenant.Current, which AddTenantContext* never registers, so events lose their tenant | `Birko.EventBus.Tenant/Extensions/EventTenantScopeServiceCollectionExtensions.cs:28` |

Detailed in [`SPEC-HARVEST-FINDINGS-2026-07-30.md`](../SPEC-HARVEST-FINDINGS-2026-07-30.md)
§ High severity -> `### area: tenant-isolation`, lines 352-422.

**The contract under review** is specced in [`docs/specs/tenant-isolation.md`](../../../docs/specs/tenant-isolation.md),
harvested from 32 source files — `../Birko.Data.Sync.Tenant/Models/ITenantSyncKnowledgeItem.cs`, `../Birko.Data.Sync.Tenant/Models/TenantSyncKnowledgeItem.cs`, `../Birko.Data.Sync.Tenant/Models/TenantSyncOptions.cs`, and more. Every one points into a **sibling repo**, so a
fix here normally lands as three commits in three repos (production, regression suite, this file) per
CLAUDE.md § Integration model.

**These are unverified harvester claims, not confirmed defects.** Confirm each against the code before
fixing. The prior to carry in comes from the 15 high findings that *were* checked by hand at harvest
time: **13 CONFIRMED** (one of them re-verified **wider** than filed), **2 CONFIRMED-NARROWER**,
**0 refuted**. So expect most to hold and a meaningful minority to need their scope corrected — and
note that "wider" has happened, so a claim is not a ceiling. Refuting on the record is a valid close; a
finding silently dropped is one the next sweep re-raises and someone re-litigates from scratch.

**Measured consumer reach, 2026-09-08** (so the fix is priced on what it protects, not on the claim's
wording): **Mostly latent.** `UseTenantMiddleware` and `AddTenantContext*`: **0** consumer `.cs` files. `AddEventTenantScope` (`SH-H053`): **1**. So the middleware half cannot currently bite a consumer and the event half can.

⚠ Latent is **not** a reason to skip or downweight a finding — the framework's recent history is
largely defects that stayed latent until a consumer selected the backend, and § TASK-219/256 record
that such a window *"closes the moment one does"*. It is a reason not to overstate urgency in a report.

**Why P0:** both claims are a tenant scope that **fails open** — the class of [[TASK-114]] (P0). And `SH-H049` is the one finding here whose status is already contested (see below).

⚠ **`SH-H049`'s status is contested and must be re-read before it is worked.** Three closed tasks
record it as *"downgraded in [[STORY-051]], not tasked"*, on the grounds that the shipped `TenantContext`
uses `AsyncLocal`. But [[TASK-118]] then wrote the opposite into its own body: the guard *"would **fail
open**: `UseTenantMiddleware` binds its context from the root provider (SH-H049), so under
`AddTenantContextScoped()` the guard's request-scoped instance is a different object, sees no tenant, and
waves the request through silently"* — and it closed with **"SH-H049 is not fixed here. This task
routes around it rather than through it."** So a downgrade and a live exploit path are both on the
record. Per § TASK-283, **re-measure the downgrade's premise before relying on it**; a stale
downgrade is exactly how a P0 stays invisible.

**Ordering constraint — the spec currently documents these defects as shipped behaviour.** The
harvest specced what the code *does*, defects included, which is exactly what let it find them. So a
behavioural fix leaves `docs/specs/tenant-isolation.md` lying until `/specs regen tenant-isolation` runs, and **that spec
diff is the fix's evidence**.

## Acceptance criteria

- [ ] All 2 findings are marked **confirmed**, **confirmed-narrower**, **confirmed-wider** or **refuted** against
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
- [ ] Any behavioural fix is followed by `/specs regen tenant-isolation`, with the spec diff reviewed as the
      change's evidence
- [ ] A confirmed finding too large for this task is spawned via `/tasks spawn` — never left as a
      ticked box with the work undone, and never as an `## Out of scope` sentence describing work
- [ ] [[STORY-051]]'s **Progress** line and its task table reflect this area's closed count

## Out of scope

- The other 37 open high findings — they belong to the other 14 per-area tasks under
  [[STORY-051]].
- The 18 high findings already covered by [[STORY-051]]'s existing 31 task files. If triage shows one of
  those fixes did **not** hold, that is a regression: file it fresh and say so, per `/tasks intake`
  § *Re-running a pass*.
- Medium-severity findings in this area — [[TASK-161]] owns those.
- Low-severity findings in this area — [[TASK-185]] owns them. Where a fix closes findings across
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

_Populated by `/tasks plan TASK-311` — leave empty until then._
