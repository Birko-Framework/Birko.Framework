---
id: TASK-232
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P2
assignee: ai
picked-by: fix-next
created: 2026-08-17
depends-on: []
blocks: []
related: [TASK-231]
findings: []
pr: 7fcb9bb (BackgroundJobs) 01d985f (.SQL) ddf74c8 (.Redis) + tests 24bf980, 62cd34e
github-issue: null
jira-key: null
---

# DECISION: the lock contract meant three different things — split the durations, keep session semantics

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

## Progress log — the premise changed

- **2026-08-17 — picked. Read the two existing implementations first, and they do not agree.**

  This task was filed as "which of the six remaining backends should get a lock provider". That question
  is now secondary, because **the two that already exist do not implement the same contract**, and
  `IJobLockProvider` (`Birko.BackgroundJobs@9b1395d`) was introduced specifically to declare them
  substitutable. Measured:

  | Implementation | what `timeout` actually does | hold semantics |
  |---|---|---|
  | SQL / **PostgreSQL** | **ignored** — `pg_try_advisory_lock` is non-blocking | **session** — releases when the dedicated connection drops |
  | SQL / **MSSql** | waits up to it — `sp_getapplock @LockTimeout` (ms) | **session** — `@LockOwner = 'Session'` |
  | SQL / **MySQL** | waits up to it — `GET_LOCK(@res, @to)` (s) | **session** — connection drop releases |
  | **Redis** | **ignored as a wait** — `When.NotExists` returns immediately | **LEASE** — `StringSetAsync(key, token, timeout, When.NotExists)` passes it as the key's **expiry** |

  So one call, `TryAcquireAsync(name, TimeSpan.FromMinutes(5))`, means three different things:

  - PostgreSQL: do not wait; hold until released or the connection dies.
  - MSSql / MySQL: wait up to 5 minutes; hold until released or the connection dies.
  - Redis: do not wait; **hold for exactly 5 minutes and then release whether or not I am still working.**

  **The interface's own words are contradicted by half its implementations.** Its remarks say *"Locks are
  session-scoped, not lease-scoped … the backend is expected to release it if the holder dies — a SQL
  advisory lock on a dedicated connection drops when the connection does."* True of SQL. Redis is a TTL
  lease and does the opposite of the interesting half: it releases **while the holder is alive**, which is
  the failure mutual exclusion exists to prevent.

  The doc does carry a warning — *"A caller must not assume it still holds a lock it acquired earlier"* —
  but it frames that as a distributed-systems caveat, not as "on Redis this is the routine case". And the
  parameter name `timeout` is doing double duty for *acquisition wait* and *lock lifetime*, which no
  amount of documentation makes safe.

  **Nothing consumes the interface yet** (verified: it appears in exactly three files — its declaration
  and the two implementations), so no framework path is currently harmed. That is the only reason this is
  not a live defect, and it is also the window in which the contract can still be changed cheaply.

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

## Outcome

**The task was filed as "which of the six remaining backends should get a lock provider" and that question
was not the one to answer first.** Reading the two existing providers before writing anything showed they
did not implement the same contract, while `IJobLockProvider` had been introduced specifically to declare
them substitutable. Adding four more implementations on top would have multiplied the inconsistency, and
each new one would silently have picked which of the two meanings of `timeout` it honoured.

### Three decisions, taken by the user 2026-08-17

**1a — two parameters.** `TryAcquireAsync(lockName, acquireTimeout, leaseDuration?, ct)`. One parameter
cannot carry both "how long will I wait for the lock" and "how long may I hold it": the same value meant a
patient caller on MSSql, an ignored argument on PostgreSQL, and a lock that self-destructs mid-work on
Redis. Breaking the signature cost nothing because the interface had **zero consumers** — a window that
closes the moment anyone adopts it.

**2a + 2b — session semantics, and say so when you cannot deliver them.** The contract stays session-scoped,
`RedisJobLockProvider` now **renews its lease on a heartbeat** at half the lease length, and
`IsLeaseBased` is exposed on the interface so a caller can tell. Both halves were taken because they answer
different questions: the heartbeat makes Redis *approximately* honest (renewals stop when the process dies,
the key expires, the lock frees), while the flag admits the approximation is not the real thing. `SqlJobLockProvider`
**throws** on a non-null `leaseDuration` rather than ignoring it — accepting a bound it cannot enforce would
be the same class of lie as the original overloaded parameter.

**3a — leader election**, and *not* wired here. Split to [[TASK-237]].

### What changed

| | |
|---|---|
| `IJobLockProvider` | split durations, `IsLeaseBased`, and remarks that state the session-vs-lease failure modes rather than implying they are the same |
| `SqlJobLockProvider<DB>` | `IsLeaseBased => false`; refuses a lease; `acquireTimeout` drives `@LockTimeout` / `GET_LOCK` / `CommandTimeout` |
| `RedisJobLockProvider` | `IsLeaseBased => true`; 30s default lease **renewed at half-life**; `acquireTimeout` is now a real (polling) wait; renewal stops on release and on both disposes; a renewal that finds the key gone sets `IsLocked = false` rather than renewing something it no longer owns |
| `Birko.BackgroundJobs/CLAUDE.md` | a support table for all 8 backends, and why the flag is on the interface |

### Evidence

**No test in the family touched a lock provider before this.** `Birko.BackgroundJobs.Redis.Tests` had 7
tests, none of them about locking — so the suite was green for the entire life of the expire-mid-work
defect. That is the same "green means nothing" shape this backlog keeps finding, and it is why the split
below matters more than the totals.

- **Redis: 17/17** against a live Redis 7 (7 pre-existing + 10 new). Duration 6s, consistent with the
  renewal test's 5s hold — it genuinely ran rather than no-opping.
- **SQL: 22/22** (17 + 5 new).
- **Whole family green**: BackgroundJobs 77, JSON 7, XML 7, MongoDB 6, CosmosDB 3, ElasticSearch 6,
  RavenDB 7.

**Both new guards proven able to fail, by mutation:**

| Mutation | Result |
|---|---|
| heartbeat disabled (`_renewalTask` not started) | **1 of 17** — `The_heartbeat_keeps_the_lock_past_its_original_lease` |
| SQL lease guard changed to `if (false)` | **1 of 22** — `A_lease_duration_is_refused_rather_than_silently_ignored` |

Both restored by rewriting the exact substitution and re-verified green. The renewal test is the load-bearing
one: a 2s lease held across 5s of work, asserting a second provider is still locked out. Without renewal the
key is gone by t=2s and the intruder walks in — which is precisely what the shipped code did on any run
longer than the caller's `timeout`.

**Deliberately not gated:** the contract tests (`IsLeaseBased` on both providers, both argument guards) need
no server, and the non-positive-lease test points at a dead port on purpose — if the guard ever moves below
`GetDatabase()` it starts failing with a connection error, which is the signal. Gating tests that can run
anywhere is how the lock providers went untested to begin with.

### Judgement calls

- **A failed renewal is swallowed; a lost lease is not.** One failed `PEXPIRE` leaves the lease still valid,
  so tearing the loop down on a single blip would be worse than retrying. But a renewal that finds the key
  **gone or holding another token** sets `IsLocked = false` and stops — continuing to renew a key we no
  longer own would let two providers both believe they hold it, which is the failure the lock exists to
  prevent.
- **The acquire wait is a poll, and that is stated.** `SET NX` does not block. Documenting the cost beats
  hiding it, and beats pretending PostgreSQL blocks by busy-waiting — which would burn a connection and a
  core for nothing.
- **The default lease is 30s, deliberately short.** It is short *because* it is renewed; a long unrenewed
  lease only makes the same failure slower.
- **Six backends still have no provider, and that is now a smaller question** — [[TASK-236]]. It also
  records something worth re-measuring rather than inheriting: this task's first sketch assumed `JSON`/`XML`
  could not support locking, but an OS file lock **is** released by the kernel on process death, which is
  genuine session semantics — the one guarantee none of the document stores can offer. The earlier guess may
  be backwards.

## Human test plan

N/A — lock acquisition, contention and release are observable by automated tests against each backend,
gated on a live service where one is needed.

## Implementation plan

_Populated by `/tasks plan TASK-232` — leave empty until then._
