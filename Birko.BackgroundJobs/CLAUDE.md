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
  | **Redis** | ✅ `RedisJobLockProvider` | **lease**, renewed on a heartbeat — `IsLeaseBased == true` |
  | SQL (**SQLite**) | ❌ returns `false` | no portable cross-connection advisory lock; callers must fall back deliberately |
  | CosmosDB · ElasticSearch · MongoDB · RavenDB · JSON · XML | ❌ none | see TASK-236 |

  **Two durations, not one.** `TryAcquireAsync(name, acquireTimeout, leaseDuration?, ct)`. The first
  version had a single `timeout` and the two implementations read it as different things — SQL as the wait,
  Redis as the key's expiry, PostgreSQL not at all — so one call meant three things. Splitting them is
  TASK-232.

  **`IsLeaseBased` is on the interface on purpose.** A session lock's failure mode is a *stuck* lock nobody
  can take over; a lease's is **releasing while the holder is still working**, which is what mutual
  exclusion exists to prevent. Those need different caller behaviour, so the distinction is exposed rather
  than smoothed over. On a lease-based provider, work that must not run twice has to be idempotent.

  **Nothing in this project consumes the interface yet** — leader election in `RecurringJobScheduler` is
  the agreed shape (TASK-232 decision 3a) and is TASK-237.

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
