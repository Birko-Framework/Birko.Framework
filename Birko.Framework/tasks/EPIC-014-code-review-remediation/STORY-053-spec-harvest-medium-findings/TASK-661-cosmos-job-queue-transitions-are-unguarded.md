---
id: TASK-661
parent: STORY-053
feature: FEATURE-014
status: done
priority: P2
assignee: ai
created: 2026-10-18
depends-on: []
blocks: [TASK-175]
findings: [SH-M001, SH-M002]
pr: null
github-issue: null
jira-key: null
---

# The Cosmos DB job queue claims and transitions with unguarded read-then-write

## Context

Spawned by [[TASK-158]], which confirmed both findings against the current code. They are too large to fix inside the
triage, because they need a conditional write that `Birko.Data.CosmosDB` does not offer yet.

- **SH-M001:** `CosmosDBJobQueue.DequeueAsync` reads the top candidate, mutates it in memory and calls
  `UpdateAsync(model)`. It has no claim token, no status guard and no retry loop, so two workers get the same job.
- **SH-M002:** `CancelAsync` reads by id only, so it cancels a Completed, Processing or Dead job and returns `true`.
- Complete / Fail are the same read-modify-write; TASK-158 fixed that shape on SQL / Mongo / ES / Raven (SH-M013).

`Birko.Data.CosmosDB` stores have no `PropertyUpdate` override and no `IfMatchEtag` on writes, so the SQL fix (a guarded
native update) cannot be copied: the base filter update is itself a read-modify-write. The fix needs an ETag (`_etag`,
`ItemRequestOptions.IfMatchEtag`) or a conditional patch path.

## Acceptance criteria

- [x] A conditional write in the Cosmos store: replace-if-ETag-matches, or a patch with a filter predicate
- [x] Dequeue claims with it and retries on a lost race; Cancel guards Pending | Scheduled; Complete / Fail guard Processing
- [x] Tests: two concurrent dequeuers on the offline double and live, each job claimed once; cancel of a running job is false

## Human test plan

N/A — library behaviour, covered by unit and live tests.

## Outcome (2026-10-18)

- **Reproduced live on the emulator first:** 6 concurrent dequeuers over 6 jobs made 24 claims of 4 distinct jobs;
  `FailAsync` on an unclaimed job rescheduled it; `CancelAsync` on a running or completed job answered true.
- **Store:** `AsyncCosmosDBStore.UpdateIfAsync(guid, condition, update)` — read with the ETag, check the condition on
  the stored item, replace with `IfMatchEtag`; a 412 re-reads, up to 16 times, then `StoreConcurrencyException`;
  refused inside a transactional batch. Chosen over a patch with a filter predicate because the job's retry decision
  needs the stored values, which a patch cannot compute. Async store only (the queue is async).
- **Queue:** the claim re-checks "due" on the stored item and moves to the next of up to 8 candidates on a lost race
  (4 rounds); Complete / Fail require Processing; Cancel requires Pending | Scheduled. No claim token is needed: the
  ETag is the version check.
- **Tests:** live queue (concurrent dequeuers, transitions, attempt budget) and live store (condition, missing item,
  8 concurrent increments, batch refusal); offline queue on the double (which overrides `UpdateIfAsync` under a lock).
  Red on HEAD: 2 live + 3 offline. Mutation (no `IfMatchEtag`): the increment test (8 -> 1) and the live dequeuer test
  red. One purge test completed an unclaimed job and now claims it first.

**Regression, live:** BackgroundJobs.CosmosDB 21, Data.CosmosDB 129, ViewModel 5, Views 40, Migrations.CosmosDB 41,
Sync.CosmosDB 4, Workflow.CosmosDB 8. Sandbox builds.
