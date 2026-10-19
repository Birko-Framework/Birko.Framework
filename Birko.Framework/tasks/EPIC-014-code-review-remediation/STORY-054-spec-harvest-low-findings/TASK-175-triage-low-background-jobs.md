---
id: TASK-175
parent: STORY-054
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P2
assignee: ai
created: 2026-08-09
depends-on: []
blocks: []
# findings: ids this task remediates, from a review/audit/spec-harvest pass (CR-* SEC-* SH-* VC-*)
findings: [SH-L001, SH-L002, SH-L003, SH-L004, SH-L005, SH-L006, SH-L007, SH-L008, SH-L009, SH-L010, SH-L011, SH-L012, SH-L013, SH-L014, SH-L015, SH-L016, SH-L017, SH-L018, SH-L019, SH-L020, SH-L021, SH-L022, SH-L023, SH-L024]
pr: null
github-issue: null
jira-key: null
---

# Triage the 24 low spec-harvest findings in `background-jobs`

## Context

Filed by `/tasks intake --epic EPIC-014` on 2026-08-09, decomposing [[STORY-054]] — which held all
387 low findings as an unscheduled checklist. A checklist line is
filed, not scheduled: only `status: todo` **tasks** are ranked by `/tasks pick`, by the `Next up` snapshot,
or by [[fix-next]], so the whole backlog was invisible to every picker. This task owns the **24** findings
that fall in the `background-jobs` area.

**Findings:** `SH-L001`–`SH-L024` (contiguous), detailed in
[`SPEC-HARVEST-FINDINGS-2026-07-30.md`](../SPEC-HARVEST-FINDINGS-2026-07-30.md) § Low severity →
`### area: background-jobs`, lines 3024–3169.

**The contract under review** is specced in [`docs/specs/background-jobs.md`](../../../docs/specs/background-jobs.md), harvested from
10 source globs — `../Birko.BackgroundJobs/**/*.cs`, `../Birko.BackgroundJobs.SQL/**/*.cs`, `../Birko.BackgroundJobs.ElasticSearch/**/*.cs`, and 7 more. Every one of
them points into a **sibling repo**, so a fix here normally lands as three commits in three repos (production,
regression suite, this file) per CLAUDE.md § Integration model.

**These are unverified harvester claims, not confirmed defects.** Confirm each against the code before fixing.
The prior to carry in comes from the only findings anyone has checked by hand: of 15 high findings verified at
harvest time, **12 held exactly and 3 needed their scope corrected**, 0 were outright refuted — so expect
roughly a quarter to be imprecise rather than wrong. Refuting on the record is a valid close; a finding
silently dropped is one the next sweep re-raises and someone re-litigates from scratch.

**Ordering constraint — the spec currently documents these defects as shipped behaviour.** The harvest specced
what the code *does*, defects included, which is exactly what let it find them. So a behavioural fix leaves
`docs/specs/background-jobs.md` lying until `/specs regen background-jobs` runs, and **that spec diff is the fix's evidence**.

## Acceptance criteria

- [x] All 24 findings are marked **confirmed**, **confirmed-narrower**, or **refuted** against the code,
      with the verdict and its evidence (`file:line` + the mechanism, not just the rule) written back into
      `SPEC-HARVEST-FINDINGS-2026-07-30.md`. Every `Verdict:` line names the specific code it traced
- [x] Confirmed findings are fixed with a regression test, or explicitly waived with a recorded reason.
      Findings sharing a root cause are fixed **together**, not one edit each
- [x] Each regression test is **red-verified**: reverting the fix fails it. Report the split as numbers, and
      name any test that passes either way as a contract pin rather than as evidence
- [x] Any behavioural fix is followed by `/specs regen background-jobs`, with the spec diff reviewed as the change's
      evidence
- [x] A confirmed finding too large for this task is spawned via `/tasks spawn` — never left as a ticked box
      with the work undone
- [x] [[STORY-054]]'s **Progress** line and `finding-count` reflect this area's closed count

## Out of scope

- The other 363 low findings — they belong to the other 21 per-area tasks under [[STORY-054]].
- High-severity findings in this area — [[STORY-051]] owns those.
- Medium-severity findings in this area — [[TASK-158]] owns them. Where a fix closes both, do it once and
  cross-reference; don't split one edit across two tasks.
- **Test gaps.** Test coverage was explicitly out of scope for the harvest sweep, so a missing test is not a
  finding here — only a test a confirmed fix needs.
- Per-sub-repo spec trees, which would make this area's staleness measurable from its own repo — that's
  [[TASK-131]].

## Human test plan

N/A — fully covered by automated tests: a library contract with no UI surface; the backends are exercised live.

## Implementation plan

_Populated by `/tasks plan TASK-175` — leave empty until then._

## Outcome (2026-10-18)

**24 findings: 19 confirmed, 3 confirmed-narrower (SH-L014, SH-L015, SH-L017), 2 refuted (SH-L008, SH-L019).**
Verdicts with `file:line` and the mechanism are in the findings doc.
- **Fixed, grouped by root cause:**
  - Processor: L001 (one loop at a time) and L010 (opt-in purge, `PurgeInterval`; user decision).
  - Executor: L002 (type checked before it is built) and L003 (only reflection's wrapper unwrapped).
  - Scheduler: L004 (interval validated, adaptive tick) and L005 (the grid kept).
  - In-memory queue: L006 (duplicate refused) and L007 (copies).
  - Hosting: L009 (the options' `RetryPolicy` used).
  - Retry policies: L011 (`Random.Shared`) and L012 (attempt clamped to 1, doc fixed).
  - Enqueue: L013 and L023 (`JobEnqueue.ResolveDelay` on all nine queues; JSON/XML stamp `EnqueuedAt`), and
    L014 (null input refused).
  - Redis: L015 (the missing field named) and L017 (reads pipelined).
  - Purge: L018 (by filter).
  - SQL lock: L020 (PostgreSQL waits via `lock_timeout`) and L022 (base-type dialect).
  - Comment: L024.
- **Found fixed / waived:** L016 fixed by TASK-663 and pinned. L017 still reads every member (waived: an ordered index
  would change stored keys). L021 waived (a rolling deploy would lose exclusion).
- **Spawned and fixed first: [[TASK-667]].** The live Elasticsearch purge test showed a job completed right after its
  claim was never recorded: a regression from TASK-158's `update_by_query` transitions.
- **Red check.** Production was reverted to HEAD, with the two new members the tests call (`PurgeInterval`,
  `NextOccurrence`) stubbed to HEAD behaviour:
  - Red: 13 core, 9 per-backend `Delay` (InMemory counted in core), 3 PostgreSQL lock, 1 Redis L015, 1 Hosting L009,
    and the L014 permissive-serializer test.
  - Hung on HEAD: the L001 concurrent-run test (a second loop started); its test host alone was stopped.
  - Pins that pass either way: L001 stop-without-run, L014 default serializer, L016, L017, and the MongoDB and
    Elasticsearch live purge tests (L018).
- Tests needing doubles: the MongoDB and Elasticsearch doubles gained `DeleteAsync(filter)` (the stores delete natively).
- Found on the way: the spec scenario "Stop … faults the caller" was stale since TASK-158 (SH-M010), and
  `docs/background-jobs.md` used a non-existent `DefaultTimeout`; both corrected.

**Regression, live:** all 102 test projects importing a changed project (Birko.Contracts, every BackgroundJobs project, the
MongoDB / Elasticsearch / RavenDB / Cosmos DB data stores) green. Sandbox builds.
