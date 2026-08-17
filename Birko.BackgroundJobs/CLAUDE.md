# Birko.BackgroundJobs

## Overview

Core background job processing framework providing interfaces for defining, enqueuing, executing, and scheduling background work. Includes an in-memory queue implementation suitable for single-process apps and testing.

## Structure

```
Birko.BackgroundJobs/
├── Core/
│   ├── IJob.cs                    - IJob (parameterless) and IJob<TInput> (typed) interfaces
│   ├── IJobExecutor.cs            - Resolves and executes job instances from descriptors
│   ├── IJobQueue.cs               - Job storage: enqueue, dequeue, complete, fail, cancel, purge
│   ├── JobContext.cs              - Runtime context (JobId, AttemptNumber, EnqueuedAt, Metadata)
│   ├── JobDescriptor.cs          - Full job description (type, input, status, retries, priority)
│   ├── JobQueueOptions.cs        - Processor config (concurrency, polling, timeout, retention); overrides RetryPolicy defaults to 30s base / 1h max
│   ├── JobResult.cs              - Execution result (Success/Failed, Duration, Error)
│   └── JobStatus.cs              - Lifecycle enum: Pending→Scheduled→Processing→Completed/Failed/Dead/Cancelled
├── Serialization/
│   ├── IJobSerializer.cs         - Serialize/deserialize job inputs
│   └── JsonJobSerializer.cs      - System.Text.Json implementation
└── Processing/
    ├── BackgroundJobProcessor.cs  - Concurrent polling processor with semaphore-based concurrency
    ├── InMemoryJobQueue.cs       - ConcurrentDictionary-based IJobQueue (non-persistent)
    ├── JobDispatcher.cs          - High-level fluent API for enqueue/schedule/cancel
    ├── JobExecutor.cs            - Default executor with DI factory and reflection-based invocation
    └── RecurringJobScheduler.cs  - Interval-based recurring job registration and scheduling
```

## Dependencies

- Birko.Contracts — imported via projitems, provides RetryPolicy (namespace `Birko`)
- Birko.Serialization — JsonJobSerializer delegates to ISerializer internally, accepts ISerializer in constructor

## Key Design Decisions

- **Shared project (.shproj)** — No NuGet dependencies, consumed via .projitems reference by host project
- **IJobQueue is the extension point** — SQL/Redis backends implement this interface. Everything else (dispatcher, processor, scheduler) is queue-agnostic
- **JobDescriptor is the persistence model** — Contains all state needed to serialize, store, and resume a job across restarts (for persistent backends)
- **DI via factory function** — `JobExecutor` takes `Func<Type, object>` instead of depending on `IServiceProvider` directly, keeping the core DI-container-agnostic
- **Typed and untyped jobs** — `IJob` for simple parameterless work, `IJob<TInput>` for jobs with serialized input data
- **In-memory queue for testing** — `InMemoryJobQueue` allows unit testing without external dependencies; jobs are lost on restart
- **`IJobLockProvider` is a separate extension point, and only 2 of 8 backends implement it** — a durable
  queue makes *claiming* a job safe, because dequeue is atomic. It does not make *deciding to enqueue*
  safe: `RecurringJobScheduler` keeps `NextRunAt` in process memory, so every worker independently
  concludes a job is due and enqueues its own copy. The lock is what prevents that.

  | Backend | Locking | Semantics |
  |---|---|---|
  | **SQL** (PostgreSQL / MSSql / MySQL) | ✅ `SqlJobLockProvider<DB>` | **session** — advisory lock on a dedicated connection; the server releases it when the holder dies |
  | **JSON · XML** (and any single-host deployment) | ✅ `FileJobLockProvider` | **session** — an exclusive file handle; the kernel releases it when the process dies |
  | **Redis** | ✅ `RedisJobLockProvider` | **lease**, renewed on a heartbeat — `IsLeaseBased == true` |
  | SQL (**SQLite**) | ❌ returns `false` | no portable cross-connection advisory lock; callers must fall back deliberately |
  | CosmosDB · ElasticSearch · MongoDB · RavenDB | ❌ none, deliberately | a lease each could express, and none is worth the machinery — see below (TASK-236) |

  **The file stores were the inverted case, and the earlier guess was backwards** (TASK-236). TASK-232
  sketched JSON/XML as "probably no locking" because file locking is unportable. Measured on Windows and
  Linux/.NET 9 instead: a second process is refused while the first holds the handle, and after the holder
  is killed outright — `taskkill /F`, `kill -9` — the next caller acquires immediately. That is a **session
  lock**, the *stronger* guarantee, and the only one besides SQL in this family. Its scope is processes
  sharing a filesystem, which is exactly what the JSON and XML queues already assume.

  ⚠ **A lock does not make the file-backed queues cross-process safe.** `JsonJobQueue` serializes its
  read-claim-update with an in-process semaphore and says so: the file store has no compare-and-swap, so
  two processes can still claim the same job. `FileJobLockProvider` buys coordination *around* the queue —
  leader election, where one process is entitled to enqueue.

  **The four document stores are a recorded "no", not a gap.** Each could express a lock — Cosmos via etag
  or TTL, ElasticSearch via `if_seq_no`, MongoDB via `findAndModify` + a TTL index, RavenDB via
  compare-exchange, which is *designed* for distributed locks. None is implemented, on purpose:

  - **Every one of them could offer only a lease**, because none has a server-side notion of "release this
    when that client disappears". So each would need the renewal heartbeat Redis has, and each would carry
    the failure mode a lease brings — expiring while its holder is still working.
  - **A consumer that needs a lock on one of these backends already has a better one available.** Nothing
    ties the lock provider to the queue's backend: a CosmosDB queue can elect its leader with
    `SqlJobLockProvider` or `FileJobLockProvider`, both session-scoped. Adding four lease-based providers
    would mean four new renewal loops to get right in order to offer something *weaker* than what is
    already there.
  - **The one that would be worth revisiting is RavenDB**, whose compare-exchange is purpose-built for
    this. It is a "no" today because the demand is hypothetical, not because the primitive is unsuitable.

  **Two durations, not one.** `TryAcquireAsync(name, acquireTimeout, leaseDuration?, ct)`. The first
  version had a single `timeout` and the two implementations read it as different things — SQL as the wait,
  Redis as the key's expiry, PostgreSQL not at all — so one call meant three things. Splitting them is
  TASK-232.

  **`IsLeaseBased` is on the interface on purpose.** A session lock's failure mode is a *stuck* lock nobody
  can take over; a lease's is **releasing while the holder is still working**, which is what mutual
  exclusion exists to prevent. Those need different caller behaviour, so the distinction is exposed rather
  than smoothed over. On a lease-based provider, work that must not run twice has to be idempotent.

- **`RecurringJobScheduler` consumes it as leader election, and the provider is optional** (TASK-237).
  Pass an `IJobLockProvider` and only the holder of the named lock runs the schedule; pass nothing — the
  default — and behaviour is bit-for-bit what it was, because six of the eight backends cannot supply a
  provider at all. Four rules, each of which is a way to get this wrong:

  - **A new leader re-baselines the schedule** (`NextRunAt = now + interval` for every definition) instead
    of firing every occurrence that elapsed while it was a follower. It has no idea what the previous
    leader enqueued, and replaying is the same duplication in a different coat. This is also why a
    **follower touches `NextRunAt` not at all**: advancing it is a scheduling decision a follower is not
    entitled to make, and code that advances is one edit away from code that enqueues.
  - **Leadership is re-attempted, never decided once**, or the death of a leader leaves nothing scheduling
    until every worker restarts. Rate-limited by `leadershipRetryInterval` (default 15s) because
    `SqlJobLockProvider` opens a real connection per attempt. A failed attempt is swallowed rather than
    propagated — a follower that faults out of the loop stops re-attempting, which is worse than the
    duplication it was there to prevent.
  - **Leadership is re-read every tick, never cached.** On an `IsLeaseBased` provider `IsLocked` goes
    false on its own, and a scheduler that trusted its earlier answer would carry on as the second leader
    the lease exists to prevent.
  - **The release on exit passes `CancellationToken.None`.** The loop exits precisely *because* its token
    was cancelled and both providers' `ReleaseAsync` open with `ThrowIfCancellationRequested`, so
    forwarding the loop's token would skip the release on the only path that ever runs — leaving a session
    lock held until the connection drops and a lease held for its full duration.

  **The idempotency answer is the better one and is deliberately not this.** Locking each individual
  decision does *not* work: every process releases right after enqueueing, so one whose clock or loop lags
  arrives later, finds the lock free and enqueues a duplicate. Closing that means holding until the *next*
  due instant, which is not a lock but a record saying "10:00 was already enqueued". **"Has this occurrence
  already been enqueued?" is an idempotency question, not a mutual-exclusion one** — its right answer is a
  unique key on the queue (job name + due instant), which every durable backend can enforce and which would
  cover all eight rather than the two that can express a lock. Recorded as the long-term shape; it is a
  queue-contract change across eight backends, so it is its own decision.

## Maintenance

### README Updates
When adding new features or changing the API, update README.md with new types, usage examples, and API reference entries.

### CLAUDE.md Updates
When adding/removing files or changing architecture, update the structure tree and design decisions.

### Test Requirements
Tests should be created in `Birko.BackgroundJobs.Tests` covering:
- Job enqueue/dequeue lifecycle
- Retry policy calculations
- Concurrent processing
- Recurring job scheduling
- Serialization round-trips
