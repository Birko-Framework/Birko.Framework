---
id: TASK-232
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P2
assignee: ai
created: 2026-08-17
depends-on: []
blocks: []
related: [TASK-231]
findings: []
pr: null
github-issue: null
jira-key: null
---

# DECISION: six of eight job backends cannot supply a lock, and nothing consumes the new interface yet

## Context

Audited after `Birko.BackgroundJobs@9b1395d` introduced `IJobLockProvider`, and
`Birko.BackgroundJobs.SQL@1f7bc2a` / `.Redis@1ba1f91` declared it on the two existing providers. **That
change is complete** — there are exactly two `*JobLockProvider` classes in the family and both now
implement the interface, so nothing was left half-declared. This task is about what the audit found *next
to* it.

**The capability is present in 2 of 8 backends:**

| Backend | Job queue | Lock provider |
|---|---|---|
| SQL | `SqlJobQueue` | **`SqlJobLockProvider<DB>`** |
| Redis | `RedisJobQueue` | **`RedisJobLockProvider`** |
| CosmosDB | `CosmosDBJobQueue` | — |
| ElasticSearch | `ElasticSearchJobQueue` | — |
| MongoDB | `MongoDBJobQueue` | — |
| RavenDB | `RavenDBJobQueue` | — |
| JSON | `JsonJobQueue` | — |
| XML | `XmlJobQueue` | — |

**Why that matters, stated in the interface's own words.** A durable queue makes *claiming* a job safe
because dequeue is atomic; it does not make *deciding to enqueue* safe. `RecurringJobScheduler` lives in
each process's memory, so *"every worker independently concludes that a job is due and enqueues its own
copy"*. The lock is what prevents that. So on six of the eight backends a consumer running more than one
worker has **no framework-provided way to avoid duplicate scheduled jobs** and must hand-roll the
coordination — which is what the interface was declared to make unnecessary.

**But nothing consumes the interface yet, and that bounds the severity.** Grepped family-wide:
`IJobLockProvider` appears in exactly three files — its own declaration and the two implementations.
Neither `RecurringJobScheduler` nor `BackgroundJobProcessor` takes one. So **no framework code path
silently duplicates work today**; the gap is that a consumer who wants the coordination can only get it on
SQL or Redis. That is a real limitation, not a live defect, which is why this is P2 and a decision rather
than a fix.

## The decision this task exists to take

**Which of the six should get a lock provider, and which should record that they cannot?** The answer is
not uniform, and the analysis has to come before any code:

- **Plausible** — each has a primitive that could carry a session-scoped lock: MongoDB
  (`findAndModify` / a TTL-indexed lock document), CosmosDB (etag / lease), ElasticSearch (optimistic
  concurrency), RavenDB (compare-exchange, which is designed for exactly this).
- **Probably not** — `JSON` and `XML` are single-process file stores. A cross-process lock over them would
  mean OS file locking, which does not match the interface's *"the backend releases it if the holder
  dies"* contract in any portable way. The honest outcome for these two may be a recorded
  "not supported, and here is why", which is a legitimate deliverable and not a gap.

The interface's contract is the constraint to judge against, and it is strict on one point: locks are
**session-scoped, not lease-scoped** — the backend must release on holder death, *"a SQL advisory lock on
a dedicated connection drops when the connection does"*. A TTL-based lock is a lease, not a session, so any
TTL implementation either diverges from the documented contract or forces the contract to widen. **Decide
that before implementing**, because widening the contract retroactively would weaken the guarantee SQL and
Redis currently make.

## Acceptance criteria

- [ ] A per-backend verdict for all six, each with its reason: *implement* (naming the primitive) or
      *record as unsupported* (naming what the backend cannot provide). A glob count is not a verdict
- [ ] The session-vs-lease question is settled explicitly. If any candidate can only offer a TTL lease,
      state whether the interface widens (and what SQL/Redis then stop guaranteeing) or whether that
      backend is recorded unsupported instead
- [ ] Each implemented provider has tests proving the two behaviours the interface singles out: a loser
      gets `false` rather than an exception, and releasing an unheld lock is not an error
- [ ] **Decide whether anything in `Birko.BackgroundJobs` should consume `IJobLockProvider`.** Today
      nothing does, so `RecurringJobScheduler` duplicates on every backend including SQL and Redis. Either
      wire it (optional dependency, absent = current behaviour) or record that coordination is deliberately
      the consumer's job — leaving it unconsumed by accident is the outcome to avoid
- [ ] `Birko.BackgroundJobs`' own `CLAUDE.md` states which backends support locking, so a consumer chooses
      a backend knowing this rather than discovering it
- [ ] Unsupported backends are recorded where a consumer will look — not only in a task file

## Out of scope

- Changing `IJobLockProvider`'s members. `9b1395d` declared the shape the two existing providers already
  had; re-litigating it is separate work.
- `Birko.EventBus.Outbox.SQL`'s registration gaps — [[TASK-231]].
- Making `RecurringJobScheduler` idempotent in general. The interface is explicit that a lock *"reduces
  duplication, it does not abolish it"* and that work which must not run twice needs to be idempotent
  regardless; that is a broader design question than this task.

## Human test plan

N/A — lock acquisition, contention and release are observable by automated tests against each backend,
gated on a live service where one is needed.

## Implementation plan

_Populated by `/tasks plan TASK-232` — leave empty until then._
