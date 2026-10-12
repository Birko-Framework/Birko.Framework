---
id: TASK-575
parent: STORY-051
feature: FEATURE-014
status: todo
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

- [ ] A reclaim's WHERE also requires the row to still be stale (`LastAttemptAt < staleBefore`, or the `ClaimToken` read with the candidate), so at most one concurrent reclaim matches
- [ ] The Dead burial is guarded the same way, so it cannot overwrite a row another worker reclaimed meanwhile
- [ ] A concurrency test with two workers on one stale row asserts exactly one claim
- [ ] A regression test fails on the current code and passes with the fix
- [ ] `/specs regen` of the area, with the spec diff reviewed, is the fix's evidence (EPIC-014's standing constraint)

## Out of scope

- Pending/Scheduled claims (already atomic, CR-H011)
- The other job-queue backends

## Human test plan

N/A — the behaviour is asserted by the regression test; nothing here is visual.
