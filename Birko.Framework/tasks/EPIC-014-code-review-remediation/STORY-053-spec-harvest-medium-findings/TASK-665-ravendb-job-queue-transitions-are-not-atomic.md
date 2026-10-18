---
id: TASK-665
parent: STORY-053
feature: FEATURE-014
status: done
priority: P2
assignee: ai
created: 2026-10-18
depends-on: []
blocks: [TASK-664, TASK-175]
findings: [SH-M012, SH-M013]
pr: null
github-issue: null
jira-key: null
---

# The RavenDB job queue's guarded claim and transitions are a read-then-write over a stale index

## Context

Found by [[TASK-664]]: on a live RavenDB a job claimed a moment ago was dequeued again. `Birko.Data.RavenDB` does not
override the filtered `UpdateAsync(filter, PropertyUpdate)`, so the base `AbstractAsyncBulkStore` runs it as a query
followed by whole-document writes. The query is served by an index that may be stale, and the write has no
concurrency check. So the "guarded native update" [[TASK-158]] gave the RavenDB queue (SH-M012 / SH-M013) is neither
guarded nor atomic there; TASK-158 recorded it as fixed on RavenDB, and its offline double could not show otherwise.

This is the same defect [[TASK-661]] fixed on Cosmos DB, and the same remedy applies: a compare-and-set on one
document — load it by id (ACID, never from an index), check the condition, write with optimistic concurrency (the
change vector), and re-read on a conflict.

## Acceptance criteria

- [x] `AsyncRavenDBStore.UpdateIfAsync(guid, condition, update)`: load by id, condition checked on the stored document,
      written only if its change vector is unchanged; a conflict re-reads; bounded, then `StoreConcurrencyException`
- [x] The RavenDB queue claims, completes, fails and cancels through it
- [x] Live tests (`BIRKO_RAVEN_URL`): concurrent dequeuers claim each job once; a just-claimed job is not dequeued again;
      transitions act only on their own status; red on HEAD

## Human test plan

N/A — library behaviour, covered by unit and live tests.

## Outcome (2026-10-18)

- **Reproduced live first (red on HEAD, 3 of 3 runs):** 3 dequeuers racing a canceller over 20 jobs — a job both
  claimed and cancelled each run, and in one run a job claimed twice. Six concurrent dequeuers over 6 jobs and a
  re-dequeue right after a claim did not reproduce on HEAD (the index had caught up); they stay as pins.
- **Store:** `AsyncRavenDBStore.UpdateIfAsync` — load by id, condition on the stored document, save with
  `UseOptimisticConcurrency`; a `ConcurrencyException` reloads, up to 16 times, then `StoreConcurrencyException`;
  refused inside a unit of work. The filtered `UpdateAsync` is left as it is (the base read-then-write) and documented.
- **Queue:** claim (up to 8 candidates per round, 4 rounds), Complete, Fail and Cancel go through it, Cosmos-style
  (TASK-661). The candidate query keeps two branches: the RavenDB LINQ provider cannot translate a `queueName == null ||`
  term on a captured value (measured: `InvalidQueryException`).
- **Tests:** live queue (3) and live store (condition, missing, 8 concurrent increments, unit-of-work refusal); the
  offline double overrides `UpdateIfAsync` under a lock. Green 5 of 5 runs. Mutation (`UseOptimisticConcurrency =
  false`): the race and the concurrent-dequeuer tests red, 3 of 3 runs.
- **Corrected:** TASK-158's file and the SH-M012 / SH-M013 verdicts said RavenDB was fixed by the guarded update.

**Regression, live:** BackgroundJobs.RavenDB 18, Data.RavenDB 119, Migrations.RavenDB 40, ViewModel 4, Views 11,
Sync.RavenDB 5, Workflow.RavenDB 8. Sandbox builds.
