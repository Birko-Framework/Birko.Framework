---
id: TASK-664
parent: STORY-053
feature: FEATURE-014
status: done
priority: P2
assignee: ai
created: 2026-10-18
depends-on: [TASK-665]
blocks: [TASK-175]
findings: [SH-M005]
pr: null
github-issue: null
jira-key: null
---

# A job left in Processing by a dead worker is reclaimed on every backend, not only SQL

## Context

Spawned by [[TASK-158]] (SH-M005, confirmed narrower). The SQL queue reclaims a job held in Processing longer than its
claim timeout (`e66eafcd`, hardened by TASK-575). MongoDB, Elasticsearch, RavenDB, Cosmos DB, JSON, XML, InMemory and
Redis have no such term. There, a worker that dies between dequeue and complete strands the job in Processing forever,
and no purge removes it.

## Acceptance criteria

- [x] Every backend offers a stale Processing job again after its claim timeout, guarded so only one worker wins the
      reclaim (as SQL does since TASK-575), and buries it as Dead once its attempt budget is spent
- [x] One `claimTimeout` option, with the same default on every backend
- [x] Tests per backend: a job claimed by a stopped worker runs again after the timeout; two reclaiming workers run it once

## Human test plan

N/A — library behaviour, covered by unit and live tests.

## Outcome (2026-10-18)

- **One option:** `JobReclaim` in core — `DefaultClaimTimeout` (30 min; `JobQueueOptions.JobTimeout` now defaults to it),
  `Resolve` (a zero or negative timeout is refused), and `AbandonedError` (SQL's message, now the one producer). Every
  queue constructor gains `TimeSpan? claimTimeout = null`. `AddBackgroundJobs` passes the processor's `JobTimeout` to its
  in-memory queue, so the two move together.
- **Per backend:** InMemory, JSON and XML add the stale term under their in-process lock. MongoDB and Elasticsearch take
  SQL's shape (TASK-575): a reclaim's claim and burial require the job to be STILL stale. Cosmos DB and RavenDB re-check
  staleness in their compare-and-set (TASK-661 / TASK-665). Redis keeps claimed jobs in `{prefix}:processing:{queue}`
  scored by claim time; the dequeue script returns a stale one to the ready set as Scheduled, or buries it.
- **Found on the way:** the RavenDB queue's guarded claim was not atomic at all — spawned and fixed first as
  [[TASK-665]]. A first suspicion that RavenDB compared dates wrongly was falsified: with TASK-665 in, the test passes.
- **Tests:** three per backend (re-offered after the timeout and not before; budget spent -> Dead "Abandoned";
  racing reclaimers win once), offline on every backend (test clock for InMemory / JSON / XML, the doubles for the
  store-backed ones, SQLite for SQL) and live on MongoDB, Elasticsearch, RavenDB, Cosmos DB and Redis; plus the timeout
  refusal and the shared default. **Red check:** the constructor parameter does not exist on HEAD, so it was done as a
  mutation turning the stale term off on every backend: every reclaim test red (3 + 3 + 3 + 5 + 5 + 5 + 6 + 3).
  Dropping only the "still stale" guard on MongoDB was caught 1 of 3 runs: the ClaimToken re-read usually still
  resolves one winner, so the race test is a weak pin for that guard (recorded, not strengthened).

**Regression, live, twice:** core 116, Hosting 9, JSON 16, XML 16, SQL 57, MongoDB 19, Elasticsearch 17, RavenDB 23,
Cosmos DB 27, Redis 47 (every importer of the changed projects). Sandbox builds.
