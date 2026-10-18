---
id: TASK-662
parent: STORY-053
feature: FEATURE-014
status: todo
priority: P2
assignee: ai
created: 2026-10-18
depends-on: []
blocks: [TASK-175]
findings: [SH-M003]
pr: null
github-issue: null
jira-key: null
---

# JobDescriptor.MaxRetries becomes nullable: null = the queue's RetryPolicy, 0 = no retries

## Context

Spawned by [[TASK-158]] (SH-M003, confirmed). Six backends compare `AttemptCount < MaxRetries` with no fallback (SQL,
including its reclaim; MongoDB, Elasticsearch, XML, Cosmos DB, Redis). Three use `MaxRetries > 0 ? MaxRetries :
policy.MaxRetries` (InMemory, JSON, RavenDB). So a descriptor with `MaxRetries = 0` is dead on its first failure on six
backends and follows the policy on three.

**Decided by the user (2026-10-18): make it nullable.** `null` means "use the queue's RetryPolicy", and `0` means "no
retries". A signature change on `JobDescriptor` and on every backend's model / column.

## Acceptance criteria

- [ ] `JobDescriptor.MaxRetries` is `int?`; the default value is decided and recorded (keeping 3 changes nothing for
      existing callers; `null` hands every default job to the policy)
- [ ] One producer of "how many attempts does this job get" (e.g. on `RetryPolicy`), used by all nine backends, the
      SQL reclaim included
- [ ] Every backend model and column accepts null; the SQL column change is reported as drift and its repair stated
- [ ] CHANGELOG breaking entry with the old and new signature
- [ ] Tests: null follows the policy, 0 dies on the first failure, n gives n attempts, on every backend

## Human test plan

N/A — library behaviour, covered by unit and live tests.
