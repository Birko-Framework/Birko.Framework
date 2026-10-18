---
id: TASK-661
parent: STORY-053
feature: FEATURE-014
status: todo
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

- [ ] A conditional write in the Cosmos store: replace-if-ETag-matches, or a patch with a filter predicate
- [ ] Dequeue claims with it and retries on a lost race; Cancel guards Pending | Scheduled; Complete / Fail guard Processing
- [ ] Tests: two concurrent dequeuers on the offline double and live, each job claimed once; cancel of a running job is false

## Human test plan

N/A — library behaviour, covered by unit and live tests.
