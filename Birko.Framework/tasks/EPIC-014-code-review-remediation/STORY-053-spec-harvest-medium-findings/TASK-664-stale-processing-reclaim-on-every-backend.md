---
id: TASK-664
parent: STORY-053
feature: FEATURE-014
status: todo
priority: P2
assignee: ai
created: 2026-10-18
depends-on: []
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

- [ ] Every backend offers a stale Processing job again after its claim timeout, guarded so only one worker wins the
      reclaim (as SQL does since TASK-575), and buries it as Dead once its attempt budget is spent
- [ ] One `claimTimeout` option, with the same default on every backend
- [ ] Tests per backend: a job claimed by a stopped worker runs again after the timeout; two reclaiming workers run it once

## Human test plan

N/A — library behaviour, covered by unit and live tests.
