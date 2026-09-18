---
id: TASK-173
parent: STORY-054
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P2
assignee: ai
created: 2026-08-09
depends-on: []
blocks: []
# findings: ids this task remediates, from a review/audit/spec-harvest pass (CR-* SEC-* SH-* VC-*)
findings: [SH-L144, SH-L145, SH-L146, SH-L147, SH-L148, SH-L149, SH-L150, SH-L151, SH-L152, SH-L153, SH-L154, SH-L155, SH-L156, SH-L157, SH-L158, SH-L159, SH-L160, SH-L161, SH-L162, SH-L163, SH-L164, SH-L165, SH-L166, SH-L167, SH-L168, SH-L169, SH-L170, SH-L171, SH-L172, SH-L173, SH-L174]
pr: null
github-issue: null
jira-key: null
---

# Triage the 31 low spec-harvest findings in `llm-provider-and-agents`

## Context

Filed by `/tasks intake --epic EPIC-014` on 2026-08-09, decomposing [[STORY-054]] — which held all
387 low findings as an unscheduled checklist. A checklist line is
filed, not scheduled: only `status: todo` **tasks** are ranked by `/tasks pick`, by the `Next up` snapshot,
or by [[fix-next]], so the whole backlog was invisible to every picker. This task owns the **31** findings
that fall in the `llm-provider-and-agents` area.

**Findings:** `SH-L144`–`SH-L174` (contiguous), detailed in
[`SPEC-HARVEST-FINDINGS-2026-07-30.md`](../SPEC-HARVEST-FINDINGS-2026-07-30.md) § Low severity →
`### area: llm-provider-and-agents`, lines 3898–4085.

**The contract under review** is specced in [`docs/specs/llm-provider-and-agents.md`](../../../docs/specs/llm-provider-and-agents.md), harvested from
2 source globs — `../Birko.AI.Contracts/**/*.cs`, `../Birko.AI/**/*.cs`. Every one of
them points into a **sibling repo**, so a fix here normally lands as three commits in three repos (production,
regression suite, this file) per CLAUDE.md § Integration model.

**These are unverified harvester claims, not confirmed defects.** Confirm each against the code before fixing.
The prior to carry in comes from the only findings anyone has checked by hand: of 15 high findings verified at
harvest time, **12 held exactly and 3 needed their scope corrected**, 0 were outright refuted — so expect
roughly a quarter to be imprecise rather than wrong. Refuting on the record is a valid close; a finding
silently dropped is one the next sweep re-raises and someone re-litigates from scratch.

**Ordering constraint — the spec currently documents these defects as shipped behaviour.** The harvest specced
what the code *does*, defects included, which is exactly what let it find them. So a behavioural fix leaves
`docs/specs/llm-provider-and-agents.md` lying until `/specs regen llm-provider-and-agents` runs, and **that spec diff is the fix's evidence**.

## Acceptance criteria

- [ ] All 31 findings are marked **confirmed**, **confirmed-narrower**, or **refuted** against the code,
      with the verdict and its evidence (`file:line` + the mechanism, not just the rule) written back into
      `SPEC-HARVEST-FINDINGS-2026-07-30.md`. Every `Verdict:` line names the specific code it traced
- [ ] Confirmed findings are fixed with a regression test, or explicitly waived with a recorded reason.
      Findings sharing a root cause are fixed **together**, not one edit each
- [ ] Each regression test is **red-verified**: reverting the fix fails it. Report the split as numbers, and
      name any test that passes either way as a contract pin rather than as evidence
- [ ] Any behavioural fix is followed by `/specs regen llm-provider-and-agents`, with the spec diff reviewed as the change's
      evidence
- [ ] A confirmed finding too large for this task is spawned via `/tasks spawn` — never left as a ticked box
      with the work undone
- [ ] [[STORY-054]]'s **Progress** line and `finding-count` reflect this area's closed count

## Out of scope

- The other 356 low findings — they belong to the other 21 per-area tasks under [[STORY-054]].
- High-severity findings in this area — [[STORY-051]] owns those.
- Medium-severity findings in this area — [[TASK-160]] owns them. Where a fix closes both, do it once and
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

_Populated by `/tasks plan TASK-173` — leave empty until then._
