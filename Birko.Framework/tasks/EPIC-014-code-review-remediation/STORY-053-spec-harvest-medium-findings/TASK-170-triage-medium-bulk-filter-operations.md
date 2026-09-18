---
id: TASK-170
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
findings: [SH-M022, SH-M023, SH-M024, SH-M025, SH-M026, SH-M027, SH-M028, SH-M029, SH-M030, SH-M031, SH-M032, SH-M033, SH-M034]
pr: null
github-issue: null
jira-key: null
---

# Triage the 13 medium spec-harvest findings in `bulk-filter-operations`

## Context

Filed by `/tasks intake --epic EPIC-014` on 2026-08-09, decomposing [[STORY-053]] — which held all
421 medium findings as an unscheduled checklist. A checklist line is
filed, not scheduled: only `status: todo` **tasks** are ranked by `/tasks pick`, by the `Next up` snapshot,
or by [[fix-next]], so the whole backlog was invisible to every picker. This task owns the **13** findings
that fall in the `bulk-filter-operations` area.

**Findings:** `SH-M022`–`SH-M034` (contiguous), detailed in
[`SPEC-HARVEST-FINDINGS-2026-07-30.md`](../SPEC-HARVEST-FINDINGS-2026-07-30.md) § Medium severity →
`### area: bulk-filter-operations`, lines 576–659.

**The contract under review** is specced in [`docs/specs/bulk-filter-operations.md`](../../../docs/specs/bulk-filter-operations.md), harvested from
18 source globs — `../Birko.Data.Stores/IBulkStore.cs`, `../Birko.Data.Stores/IAsyncBulkStore.cs`, `../Birko.Data.Stores/AbstractBulkStore.cs`, and 15 more. Every one of
them points into a **sibling repo**, so a fix here normally lands as three commits in three repos (production,
regression suite, this file) per CLAUDE.md § Integration model.

**These are unverified harvester claims, not confirmed defects.** Confirm each against the code before fixing.
The prior to carry in comes from the only findings anyone has checked by hand: of 15 high findings verified at
harvest time, **12 held exactly and 3 needed their scope corrected**, 0 were outright refuted — so expect
roughly a quarter to be imprecise rather than wrong. Refuting on the record is a valid close; a finding
silently dropped is one the next sweep re-raises and someone re-litigates from scratch.

**Ordering constraint — the spec currently documents these defects as shipped behaviour.** The harvest specced
what the code *does*, defects included, which is exactly what let it find them. So a behavioural fix leaves
`docs/specs/bulk-filter-operations.md` lying until `/specs regen bulk-filter-operations` runs, and **that spec diff is the fix's evidence**.

**Three of these 13 already have history — do not re-file them.**

| Finding | State |
|---|---|
| `SH-M022` | Folded into [[TASK-110]] (`done`) — same call site and root cause as `SH-H003`: the ORDER BY key never resolved through `GetField().GetSelectName()`, so a `[NamedField]`-remapped property emitted ORDER BY on a nonexistent column and `RunReaderCommand` swallowed it, returning **empty rather than throwing**. Close it as remediated, don't re-triage. |
| `SH-M023` | Fixed by [[TASK-109]] (`done`); its MongoDB regression coverage is still open as [[TASK-141]]. Leave both alone. |
| `SH-M025` | Carries a **`Verdict: CONFIRMED` line that belongs to `SH-H003`** — pasted on by mistake. The finding is actually about `ReadCore` handing out a lazy iterator holding an open `DbConnection`/`DbDataReader` outside the store, which has nothing to do with ORDER BY. **Treat it as unverified**, and strip the stray verdict from the findings doc as part of this task. A copied verdict block is worse than no verdict — it launders an unchecked claim as a checked one. |

## Acceptance criteria

- [ ] All 13 findings are marked **confirmed**, **confirmed-narrower**, or **refuted** against the code,
      with the verdict and its evidence (`file:line` + the mechanism, not just the rule) written back into
      `SPEC-HARVEST-FINDINGS-2026-07-30.md`. Every `Verdict:` line names the specific code it traced
- [ ] Confirmed findings are fixed with a regression test, or explicitly waived with a recorded reason.
      Findings sharing a root cause are fixed **together**, not one edit each
- [ ] Each regression test is **red-verified**: reverting the fix fails it. Report the split as numbers, and
      name any test that passes either way as a contract pin rather than as evidence
- [ ] Any behavioural fix is followed by `/specs regen bulk-filter-operations`, with the spec diff reviewed as the change's
      evidence
- [ ] A confirmed finding too large for this task is spawned via `/tasks spawn` — never left as a ticked box
      with the work undone
- [ ] [[STORY-053]]'s **Progress** line and `finding-count` reflect this area's closed count

## Out of scope

- The other 408 medium findings — they belong to the other 21 per-area tasks under [[STORY-053]].
- High-severity findings in this area — [[STORY-051]] owns those.
- Low-severity findings in this area — [[TASK-190]] owns them. Where a fix closes both, do it once and
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

_Populated by `/tasks plan TASK-170` — leave empty until then._
