---
id: TASK-154
parent: STORY-053
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P1
assignee: ai
created: 2026-08-09
depends-on: []
blocks: []
# findings: ids this task remediates, from a review/audit/spec-harvest pass (CR-* SEC-* SH-* VC-*)
findings: [SH-M221, SH-M222, SH-M223, SH-M224, SH-M225, SH-M226, SH-M227, SH-M228, SH-M229, SH-M230, SH-M231, SH-M232, SH-M233, SH-M234, SH-M235, SH-M236, SH-M237, SH-M238, SH-M239, SH-M240, SH-M241, SH-M242, SH-M243, SH-M244, SH-M245]
pr: null
github-issue: null
jira-key: null
---

# Triage the 25 medium spec-harvest findings in `schema-index-and-ddl`

## Context

Filed by `/tasks intake --epic EPIC-014` on 2026-08-09, decomposing [[STORY-053]] — which held all
421 medium findings as an unscheduled checklist. A checklist line is
filed, not scheduled: only `status: todo` **tasks** are ranked by `/tasks pick`, by the `Next up` snapshot,
or by [[fix-next]], so the whole backlog was invisible to every picker. This task owns the **25** findings
that fall in the `schema-index-and-ddl` area.

**Findings:** `SH-M221`–`SH-M245` (contiguous), detailed in
[`SPEC-HARVEST-FINDINGS-2026-07-30.md`](../SPEC-HARVEST-FINDINGS-2026-07-30.md) § Medium severity →
`### area: schema-index-and-ddl`, lines 1794–1945.

**The contract under review** is specced in [`docs/specs/schema-index-and-ddl.md`](../../../docs/specs/schema-index-and-ddl.md), harvested from
16 source globs — `../Birko.Data.Patterns/Schema/*.cs`, `../Birko.Data.Patterns/IndexManagement/*.cs`, `../Birko.Data.SQL/Attributes/*.cs`, and 13 more. Every one of
them points into a **sibling repo**, so a fix here normally lands as three commits in three repos (production,
regression suite, this file) per CLAUDE.md § Integration model.

**These are unverified harvester claims, not confirmed defects.** Confirm each against the code before fixing.
The prior to carry in comes from the only findings anyone has checked by hand: of 15 high findings verified at
harvest time, **12 held exactly and 3 needed their scope corrected**, 0 were outright refuted — so expect
roughly a quarter to be imprecise rather than wrong. Refuting on the record is a valid close; a finding
silently dropped is one the next sweep re-raises and someone re-litigates from scratch.

**Ordering constraint — the spec currently documents these defects as shipped behaviour.** The harvest specced
what the code *does*, defects included, which is exactly what let it find them. So a behavioural fix leaves
`docs/specs/schema-index-and-ddl.md` lying until `/specs regen schema-index-and-ddl` runs, and **that spec diff is the fix's evidence**.

## Acceptance criteria

- [ ] All 25 findings are marked **confirmed**, **confirmed-narrower**, or **refuted** against the code,
      with the verdict and its evidence (`file:line` + the mechanism, not just the rule) written back into
      `SPEC-HARVEST-FINDINGS-2026-07-30.md`. Every `Verdict:` line names the specific code it traced
- [ ] Confirmed findings are fixed with a regression test, or explicitly waived with a recorded reason.
      Findings sharing a root cause are fixed **together**, not one edit each
- [ ] Each regression test is **red-verified**: reverting the fix fails it. Report the split as numbers, and
      name any test that passes either way as a contract pin rather than as evidence
- [ ] Any behavioural fix is followed by `/specs regen schema-index-and-ddl`, with the spec diff reviewed as the change's
      evidence
- [ ] A confirmed finding too large for this task is spawned via `/tasks spawn` — never left as a ticked box
      with the work undone
- [ ] [[STORY-053]]'s **Progress** line and `finding-count` reflect this area's closed count

## Out of scope

- The other 396 medium findings — they belong to the other 21 per-area tasks under [[STORY-053]].
- High-severity findings in this area — [[STORY-051]] owns those.
- Low-severity findings in this area — [[TASK-192]] owns them. Where a fix closes both, do it once and
  cross-reference; don't split one edit across two tasks.
- **Test gaps.** Test coverage was explicitly out of scope for the harvest sweep, so a missing test is not a
  finding here — only a test a confirmed fix needs.
- Per-sub-repo spec trees, which would make this area's staleness measurable from its own repo — that's
  [[TASK-131]].

## Human test plan

Cannot be written yet: which steps a human adds depends on which findings survive triage. **Resolve this
section before `/tasks close`** — an absent plan is not an `N/A` one, and defaulting it parks the task on a
step that may not exist (SKILL.md § Lifecycle). Expected outcome for this area is
`N/A — fully covered by automated tests`, since it is a library contract with no UI surface; write that
explicitly with its reason rather than leaving the section as-is.

## Implementation plan

_Populated by `/tasks plan TASK-154` — leave empty until then._
