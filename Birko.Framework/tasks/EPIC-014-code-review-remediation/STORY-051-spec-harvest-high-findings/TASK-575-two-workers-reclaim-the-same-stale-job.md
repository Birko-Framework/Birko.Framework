---
id: TASK-575
parent: STORY-051
feature: FEATURE-014
status: done
priority: P1
assignee: ai
created: 2026-10-12
depends-on: []
blocks: []
findings: [SH-H059]
pr: null
github-issue: null
jira-key: null
---

# Two SQL job workers can both reclaim the same stale Processing job and run it twice

## Context

Found by the 2026-10-12 `/specs regen` of all 25 areas at `c50ed580` (`background-jobs`); traced 2026-10-12.

TASK-451 made `SqlJobQueue.DequeueAsync` (`Birko.BackgroundJobs.SQL/SqlJobQueue.cs:95-180`) reclaim a row held in
`Processing` past `claimTimeout`. The claim is `UPDATE … WHERE Guid = id AND Status = originalStatus`, and for a reclaim
`originalStatus` **is** `Processing` — the status the claim writes. So after worker A's UPDATE, worker B's identical
UPDATE still matches; each sets its own `ClaimToken`, and the read-back only tells a worker who wrote **last**. If A reads
its token back before B's UPDATE lands, A runs the job, then B overwrites the token, reads its own back and runs it too.
CR-H011's guarantee holds for Pending/Scheduled rows (the claim changes their status) and not for a reclaim.

The Dead-burial branch beside it is a full read-modify-write `UpdateAsync(candidate)` with no guard at all, so it can
also bury a row another worker has just legitimately reclaimed.

## Acceptance criteria

- [x] A reclaim's WHERE also requires the row to still be stale (`LastAttemptAt < staleBefore`, or the `ClaimToken` read with the candidate), so at most one concurrent reclaim matches
- [x] The Dead burial is guarded the same way, so it cannot overwrite a row another worker reclaimed meanwhile
- [x] A concurrency test with two workers on one stale row asserts exactly one claim
- [x] A regression test fails on the current code and passes with the fix
- [x] `/specs regen` of the area, with the spec diff reviewed, is the fix's evidence (EPIC-014's standing constraint) — done 2026-10-16 (batched regen of 17 areas, diffs reviewed and confirmed)

## Out of scope

- Pending/Scheduled claims (already atomic, CR-H011)
- The other job-queue backends

## Human test plan

N/A — the behaviour is asserted by the regression test; nothing here is visual.

## Progress log

- 2026-10-15 — Both races forced deterministically (`StaleReclaimRaceTests`): a store hook runs the other party's whole
  step just before this worker's write. Red on the old code: two workers both got the reclaimed job, and a job its slow
  worker completed was buried as Dead (status 5, not 3). Fix: the reclaim and the burial both update only
  `WHERE Guid = id AND Status = Processing AND LastAttemptAt < staleBefore` (the winner's write moves `LastAttemptAt` to
  now). The staleness guard was chosen over comparing the old `ClaimToken`, which is null on a fresh row, the
  null-comparison shape TASK-574 shows can render as `= NULL`. Green: BackgroundJobs.SQL 27/27.
- **Open: `/specs regen background-jobs`** (EPIC-014's standing evidence rule) has not been run. It is batched with
  the regens TASK-567, 620, 622 and 625 also owe (`store-crud-contract`, `schema-index-and-ddl`). The box stays
  unticked until it is done.
