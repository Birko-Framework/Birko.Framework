---
id: TASK-667
parent: STORY-053
feature: FEATURE-014
status: done
priority: P1
assignee: ai
created: 2026-10-18
depends-on: []
blocks: [TASK-175]
findings: [SH-M013]
pr: null
github-issue: null
jira-key: null
---

# Elasticsearch job transitions match a stale search index, so an outcome reported right after the claim is dropped

## Context

Found by TASK-175's live purge test: a job completed right after its claim was still `Processing`, with no
`CompletedAt`. **A regression from [[TASK-158]]**, which made the Elasticsearch Complete / Fail / Cancel guarded
`update_by_query` calls. `update_by_query` matches against the search index, which sees a write only after a refresh
(~1 s). A job claimed under a second ago was still indexed Pending, so the Processing guard matched nothing and the
outcome was silently dropped. Any job shorter than the refresh interval was never recorded, stayed Processing, and since
[[TASK-664]] would be reclaimed after 30 minutes and run again. Cancel, guarded on Pending | Scheduled, cancelled a
running job. `GetAsync`, a search, read the pre-transition copy.

## Acceptance criteria

- [x] `AsyncElasticSearchStore.UpdateIfAsync`: realtime GET, condition on that document, write with `if_seq_no` /
      `if_primary_term`, a 409 re-reads, bounded, then `StoreConcurrencyException`
- [x] The queue claims (with TASK-664's reclaim), completes, fails and cancels through it; `GetAsync` reads realtime
- [x] Live tests red on HEAD; every importer green

## Outcome (2026-10-18)

- **Reproduced live first, red on HEAD 2 of 2 runs (3 tests each):** claim, then at once Complete -> still Pending;
  Fail -> still Pending; Cancel -> true for a running job.
- **Fix:** the store primitive above, Cosmos / RavenDB-style (TASK-661 / TASK-665); the queue's dequeue and transitions
  are RavenDB's shape. The candidate search stays a hint.
- **Tests:** 3 live queue tests (`Task667EsTransitionLiveTests`) and 2 live store tests (unrefreshed write seen,
  condition, missing, 8 concurrent increments); the offline double overrides `UpdateIfAsync`. Green 3 of 3 runs.
  Mutation (no `_seq_no` condition): increments 8 -> 1, and the racing-dequeuers test red 3 of 3.
- **Also explained:** the racing-dequeuers test recorded as a pre-existing 1-in-5 flake at TASK-158 passed 3 of 3
  with the fix; its stale-index claim is the likely cause. Not claimed as proven.
- **Corrected:** TASK-158's file and the SH-M013 verdict.

## Human test plan

N/A — library behaviour, covered by live tests.

**Regression, live:** all 8 Elasticsearch importers green (BackgroundJobs.ElasticSearch 20, Data.ElasticSearch 293, ViewModel 6,
Views 27, Migrations 40, Sync 13, Health 6, Workflow 9). Sandbox builds.
