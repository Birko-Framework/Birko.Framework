---
id: TASK-561
parent: STORY-051
feature: FEATURE-014
status: done
priority: P2
assignee: ai
picked-by: fix-next
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

- [x] Confirm by a test against the Redis queue (the live suite, or whatever double the Redis tests use)
      that an unnamed job is not served by `DequeueAsync("work")`
- [x] A named `DequeueAsync` on Redis also serves unnamed jobs, as on every sibling. Likely shape: the claim
      script reads both the named key and the unnamed key and takes the better score — stamping the
      processor's queue at enqueue is not available, because the queue never sees `JobQueueOptions`
- [x] Regression test asserting the observed state (the job dequeued, its stored status), red-verified
- [x] `/specs regen background-jobs` — the Redis scenario and the requirement text change

## Out of scope

- Cosmos DB — fixed by [[TASK-317]].
- A named job on another queue staying invisible to the default processor — that is the documented
  contract on every backend.

## Human test plan

N/A — library code with no UI surface; fully covered by automated tests.

## Implementation plan

_Populated by `/tasks plan TASK-561` — leave empty until then._

## Outcome

**Fixed.** On Redis, a background job queued without a queue name — which is every job `JobDispatcher` queues — now
runs on a processor configured with any queue name, not only one named `"default"`. Before, renaming the processor's
queue silently stranded every such job in `Pending` forever.

- **Confirmed by a run (acceptance 1):** against a live `redis:7` (throwaway container, removed), the original code
  failed `A_named_dequeue_serves_an_unnamed_job` — `DequeueAsync("work")` returned null for an unnamed job.
- **Shape:** the claim script takes both keys and claims the lower-scored candidate. The unnamed key is shared with jobs
  explicitly named `"default"`, which must not leak to another queue, so it takes only a member whose hash has no
  `QueueName`, stepping past up to 100 due ones (`UnnamedScanLimit`). Rejected: moving unnamed jobs to their own key —
  it would strand every already-pending unnamed job in the old key.
- **Step-6 split (`RedisUnnamedJobDequeueTests`, 8, live):** full revert → **4/8** red
  (`A_named_dequeue_serves_an_unnamed_job`, `A_named_dequeue_takes_the_better_scored_of_its_own_and_an_unnamed_job`,
  `An_unnamed_job_behind_explicitly_default_named_jobs_is_still_served`,
  `An_older_named_job_is_taken_before_a_newer_unnamed_one`). The unnamed-only filter removed → **2/8**
  (`A_job_explicitly_named_default_is_not_served_to_another_queue`, `An_unnamed_job_behind_…`), so the guard that keeps a
  "default" job on its queue is tested too. **Contract pins:** `A_job_named_for_another_queue_stays_invisible`,
  `The_default_queue_still_serves_unnamed_jobs`, `A_future_unnamed_job_is_not_served_early`.
- **Correction during the work:** the first priority test assumed priority beats age; Redis scores by due time with
  priority as a bounded tie-break (CR-C02), so the test now uses one enqueue time, and a separate test pins that an
  older named job beats a newer unnamed one.
- **Flagged, not fixed:** `DequeueAsync(null)` on Redis reads only the `default` key, while the sibling predicate
  (`queueName == null || …`) serves any queue. Not this finding and no caller passes null (`BackgroundJobProcessor`
  always passes `DefaultQueueName`); recorded here.
- Suites: Birko.BackgroundJobs.Redis.Tests 28/28 live (without `BIRKO_REDIS_HOST` the new tests report a skip).
- **Merge gate:** inline review; no public surface change; no nullable warnings.

## Progress log

- step 2 — picked; ranked above TASK-560 (ES reindex without waiting reports Successful, P3) on key 1: silent permanent non-execution of every dispatcher job under a non-default queue name vs a premature success report; TASK-555/556/557 excluded as decision tasks
- step 3 — verified: held as written; confirmed by a live run (unnamed job not served to DequeueAsync("work"), 3 of the first 7 tests red on the original)
- step 4 — layer: local (Birko.BackgroundJobs.Redis dequeue script)
- step 5 — fix in `Birko.BackgroundJobs.Redis/RedisJobQueue.cs` (script reads the named and unnamed keys, unnamed-only filter, scan limit); tests in `tests/Birko.BackgroundJobs.Redis.Tests/RedisUnnamedJobDequeueTests.cs` (8, live-gated); suite 28/28 live
- step 6 — full revert 4/8 red; unnamed-only filter removed 2/8; pins named in Outcome
- step 7 — respecced background-jobs: *Queue-name scoping on dequeue* requirement now covers Redis; scenarios *Redis serves a null-queue job to any named queue* and *Redis keeps an explicitly "default" job on its queue* added
- step 8 — handed to /tasks close --unattended; outcome in status:, commit in git log
