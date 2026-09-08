---
id: TASK-314
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
findings: [SH-H029, SH-H030, SH-H031, SH-H032, SH-H033]
pr: null
github-issue: null
jira-key: null
---

# Triage the 5 remaining high spec-harvest findings in `migrations`

## Context

Filed by `/tasks intake --epic EPIC-014 --story STORY-051` on 2026-09-08. [[STORY-051]] had **31 task
files covering 18 of its 57 findings**, and the other **39 had no task at all** — so they were
invisible to `/tasks pick`, to the `Next up` snapshot and to [[fix-next]]. *A checklist line is filed,
not scheduled.* This is the same decomposition [[STORY-053]] received on 2026-08-09 for the medium
findings, applied a month later to the tier that outranks it: **high** means silent data loss,
cross-tenant leakage, auth bypass, or a destructive operation on the wrong rows.

This task owns the **5** open findings in the `migrations` area.

| Finding | Claim | Named location |
|---|---|---|
| `SH-H029` | ElasticSearch GetAppliedVersions returns an empty set on an INVALID search response | `Birko.Data.Migrations.ElasticSearch/ElasticSearchMigrationStore.cs:88` |
| `SH-H030` | InfluxDB GetAppliedVersions swallows every InfluxException, re-running everything | `Birko.Data.Migrations.InfluxDB/InfluxMigrationStore.cs:113` |
| `SH-H031` | MongoDB version rows are written without the session, surviving AbortTransaction | `Birko.Data.Migrations.MongoDB/MongoMigrationRunner.cs:78` |
| `SH-H032` | An empty operator object degrades the filter to match-all on delete/update | `Birko.Data.Migrations.SQL/Context/SqlDataMigrator.cs:152` |
| `SH-H033` | InfluxDB migration bucket has a 365-day expiry, so applied versions expire | `Birko.Data.Migrations.InfluxDB/InfluxMigrationStore.cs:47` |

Detailed in [`SPEC-HARVEST-FINDINGS-2026-07-30.md`](../SPEC-HARVEST-FINDINGS-2026-07-30.md)
§ High severity -> `### area: migrations`, lines 224-254.

**The contract under review** is specced in [`docs/specs/migrations.md`](../../../docs/specs/migrations.md),
harvested from 51 source files — `../Birko.Data.Migrations.CosmosDB/Context/CosmosDBDataMigrator.cs`, `../Birko.Data.Migrations.CosmosDB/Context/CosmosDBMigrationContext.cs`, `../Birko.Data.Migrations.CosmosDB/Context/CosmosDBSchemaBuilder.cs`, and more. Every one points into a **sibling repo**, so a
fix here normally lands as three commits in three repos (production, regression suite, this file) per
CLAUDE.md § Integration model.

**These are unverified harvester claims, not confirmed defects.** Confirm each against the code before
fixing. The prior to carry in comes from the 15 high findings that *were* checked by hand at harvest
time: **13 CONFIRMED** (one of them re-verified **wider** than filed), **2 CONFIRMED-NARROWER**,
**0 refuted**. So expect most to hold and a meaningful minority to need their scope corrected — and
note that "wider" has happened, so a claim is not a ceiling. Refuting on the record is a valid close; a
finding silently dropped is one the next sweep re-raises and someone re-litigates from scratch.

**Measured consumer reach, 2026-09-08** (so the fix is priced on what it protects, not on the claim's
wording): **Partly live.** `Birko.Data.Migrations.SQL` is imported by **3** consumer aggregators, but `SqlDataMigrator` (which `SH-H032` names) has **0** consumer `.cs` references, and no consumer selects the ElasticSearch / InfluxDB / MongoDB stores the other four findings name.

⚠ Latent is **not** a reason to skip or downweight a finding — the framework's recent history is
largely defects that stayed latent until a consumer selected the backend, and § TASK-219/256 record
that such a window *"closes the moment one does"*. It is a reason not to overstate urgency in a report.

**Why P1:** includes `SH-H032`, a match-all degrade on delete/update — [[TASK-109]]'s family, which was P0 — but reachable only from a migration a consumer authors, not from an ordinary read or write. Rank that finding higher than its siblings when draining.

**Ordering constraint — the spec currently documents these defects as shipped behaviour.** The
harvest specced what the code *does*, defects included, which is exactly what let it find them. So a
behavioural fix leaves `docs/specs/migrations.md` lying until `/specs regen migrations` runs, and **that spec
diff is the fix's evidence**.

## Acceptance criteria

- [ ] All 5 findings are marked **confirmed**, **confirmed-narrower**, **confirmed-wider** or **refuted** against
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
- [ ] Any behavioural fix is followed by `/specs regen migrations`, with the spec diff reviewed as the
      change's evidence
- [ ] A confirmed finding too large for this task is spawned via `/tasks spawn` — never left as a
      ticked box with the work undone, and never as an `## Out of scope` sentence describing work
- [ ] [[STORY-051]]'s **Progress** line and its task table reflect this area's closed count

## Out of scope

- The other 34 open high findings — they belong to the other 14 per-area tasks under
  [[STORY-051]].
- The 18 high findings already covered by [[STORY-051]]'s existing 31 task files. If triage shows one of
  those fixes did **not** hold, that is a regression: file it fresh and say so, per `/tasks intake`
  § *Re-running a pass*.
- Medium-severity findings in this area — [[TASK-152]] owns those.
- Low-severity findings in this area — [[TASK-178]] owns them. Where a fix closes findings across
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

_Populated by `/tasks plan TASK-314` — leave empty until then._
