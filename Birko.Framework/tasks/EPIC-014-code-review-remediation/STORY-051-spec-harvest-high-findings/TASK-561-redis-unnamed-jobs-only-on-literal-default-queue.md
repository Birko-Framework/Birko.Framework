---
id: TASK-561
parent: STORY-051
feature: FEATURE-014
status: todo
priority: P2
assignee: ai
created: 2026-10-11
depends-on: []
blocks: []
findings: [FIELD-028]
pr: null
github-issue: null
jira-key: null
---

# Redis serves an unnamed job only to a queue literally named `"default"`

## Context

Found while fixing [[TASK-317]] (SH-H001, 2026-10-11), the same defect class on a different backend.
`RedisJobQueue` (`Birko.BackgroundJobs.Redis/RedisJobQueue.cs`) routes a job by key: `GetQueueKey(null)`
(:365) resolves to the literal `{prefix}:queue:default`, and `DequeueAsync(queueName)` (:118) reads only
`GetQueueKey(queueName)`. `JobDispatcher.EnqueueAsync` / `ScheduleAsync` / `EnqueueWithPriorityAsync` leave
`QueueName` null, and `BackgroundJobProcessor` always dequeues `JobQueueOptions.DefaultQueueName`.

With the shipped default (`"default"`) the two keys coincide and everything runs. **Set
`DefaultQueueName = "work"` and no dispatcher-enqueued job ever runs on Redis**: it sits in the
`queue:default` sorted set with `Status = Pending`, silently, while every other backend (InMemory, SQL,
MongoDB, Elasticsearch, RavenDB, Cosmos DB since TASK-317, JSON, XML) admits an unnamed job on any named
dequeue. `docs/specs/background-jobs.md` § *Queue-name scoping on dequeue* records the Redis isolation as
shipped behaviour.

Unverified by a run: traced by reading the code only. Reach: config-dependent (a non-default
`DefaultQueueName`) and no consumer selects the Redis job backend today. Hence P2, not P1.

## Acceptance criteria

- [ ] Confirm by a test against the Redis queue (the live suite, or whatever double the Redis tests use)
      that an unnamed job is not served by `DequeueAsync("work")`
- [ ] A named `DequeueAsync` on Redis also serves unnamed jobs, as on every sibling. Likely shape: the claim
      script reads both the named key and the unnamed key and takes the better score — stamping the
      processor's queue at enqueue is not available, because the queue never sees `JobQueueOptions`
- [ ] Regression test asserting the observed state (the job dequeued, its stored status), red-verified
- [ ] `/specs regen background-jobs` — the Redis scenario and the requirement text change

## Out of scope

- Cosmos DB — fixed by [[TASK-317]].
- A named job on another queue staying invisible to the default processor — that is the documented
  contract on every backend.

## Human test plan

N/A — library code with no UI surface; fully covered by automated tests.

## Implementation plan

_Populated by `/tasks plan TASK-561` — leave empty until then._
