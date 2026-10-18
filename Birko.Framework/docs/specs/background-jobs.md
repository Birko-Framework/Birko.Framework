---
area: background-jobs
generated-at: 4b5bf7e6a33b66aa46da5e6adc40ff88e547da43
generated-on: 2026-10-16
sources:
  - ../Birko.BackgroundJobs.CosmosDB/CosmosDBJobQueue.cs
  - ../Birko.BackgroundJobs.CosmosDB/CosmosDBJobQueueSchema.cs
  - ../Birko.BackgroundJobs.CosmosDB/Models/CosmosJobDescriptorModel.cs
  - ../Birko.BackgroundJobs.ElasticSearch/ElasticSearchJobQueue.cs
  - ../Birko.BackgroundJobs.ElasticSearch/ElasticSearchJobQueueSchema.cs
  - ../Birko.BackgroundJobs.ElasticSearch/Models/ElasticJobDescriptorModel.cs
  - ../Birko.BackgroundJobs.Hosting/BackgroundJobsHostingExtensions.cs
  - ../Birko.BackgroundJobs.Hosting/HostedServices.cs
  - ../Birko.BackgroundJobs.JSON/JsonJobQueue.cs
  - ../Birko.BackgroundJobs.JSON/JsonJobQueueSchema.cs
  - ../Birko.BackgroundJobs.JSON/Models/JsonJobDescriptorModel.cs
  - ../Birko.BackgroundJobs.MongoDB/Models/MongoJobDescriptorModel.cs
  - ../Birko.BackgroundJobs.MongoDB/MongoDBJobQueue.cs
  - ../Birko.BackgroundJobs.MongoDB/MongoDBJobQueueSchema.cs
  - ../Birko.BackgroundJobs.RavenDB/Models/RavenJobDescriptorModel.cs
  - ../Birko.BackgroundJobs.RavenDB/RavenDBJobQueue.cs
  - ../Birko.BackgroundJobs.RavenDB/RavenDBJobQueueSchema.cs
  - ../Birko.BackgroundJobs.Redis/RedisJobLockProvider.cs
  - ../Birko.BackgroundJobs.Redis/RedisJobQueue.cs
  - ../Birko.BackgroundJobs.SQL/Models/JobDescriptorModel.cs
  - ../Birko.BackgroundJobs.SQL/SqlJobLockProvider.cs
  - ../Birko.BackgroundJobs.SQL/SqlJobQueue.cs
  - ../Birko.BackgroundJobs.SQL/SqlJobQueueSchema.cs
  - ../Birko.BackgroundJobs.XML/Models/XmlJobDescriptorModel.cs
  - ../Birko.BackgroundJobs.XML/XmlJobQueue.cs
  - ../Birko.BackgroundJobs.XML/XmlJobQueueSchema.cs
  - ../Birko.BackgroundJobs/Core/IJob.cs
  - ../Birko.BackgroundJobs/Core/IJobExecutor.cs
  - ../Birko.BackgroundJobs/Core/IJobLockProvider.cs
  - ../Birko.BackgroundJobs/Core/IJobQueue.cs
  - ../Birko.BackgroundJobs/Core/IJobQueueCounts.cs
  - ../Birko.BackgroundJobs/Core/JobContext.cs
  - ../Birko.BackgroundJobs/Core/JobDescriptor.cs
  - ../Birko.BackgroundJobs/Core/JobQueueOptions.cs
  - ../Birko.BackgroundJobs/Core/JobResult.cs
  - ../Birko.BackgroundJobs/Core/JobStatus.cs
  - ../Birko.BackgroundJobs/Core/RetryPolicy.cs
  - ../Birko.BackgroundJobs/Processing/BackgroundJobProcessor.cs
  - ../Birko.BackgroundJobs/Processing/FileJobLockProvider.cs
  - ../Birko.BackgroundJobs/Processing/InMemoryJobQueue.cs
  - ../Birko.BackgroundJobs/Processing/JobDispatcher.cs
  - ../Birko.BackgroundJobs/Processing/JobExecutor.cs
  - ../Birko.BackgroundJobs/Processing/RecurringJobScheduler.cs
  - ../Birko.BackgroundJobs/Serialization/IJobSerializer.cs
  - ../Birko.BackgroundJobs/Serialization/JobSerializationHelper.cs
  - ../Birko.BackgroundJobs/Serialization/JsonJobSerializer.cs
  - ../Birko.Contracts/Retry/RetryPolicy.cs
# source-commits: omitted — since the monorepo migration (TASK-457) every source above is inside this
# repo, so `generated-at` covers them (regen step 5c). The old per-sibling shas no longer resolve here.
shaped-by: [FEATURE-014]
# Evidence pass ran 2026-10-12 over 325 feature-linked tasks: 59 resolved, 266 did not —
# polyrepo-era `pr:` shas the history import rewrote, or no reachable commit whose subject leads with the
# task id; 9 of those are refused by the state gate (todo/cancelled). Earlier entries kept append-only.
shaped-by-derived: true
shaped-by-unresolved: 266
---
# Job queue, retry policy, processing and scheduling

## Purpose

This capability lets an application hand work off to be run later, out of band from the request
that created it. A caller describes a unit of work as a `JobDescriptor` (a job type name, an
optional serialized input payload, a queue name, a priority, a delay and a retry budget) and puts
it into an `IJobQueue`. A `BackgroundJobProcessor` polls that queue, hands each claimed descriptor
to a `JobExecutor` which reflectively resolves and runs the corresponding `IJob` / `IJob<TInput>`
implementation, and then reports the outcome back to the queue — success completes the job, failure
either reschedules it with backoff or buries it as dead.

The queue itself is an interface with nine shipped implementations: an in-process
`InMemoryJobQueue` (the reference behaviour), and persistent backends over SQL, MongoDB,
Elasticsearch, RavenDB, Cosmos DB, Redis, a JSON file and an XML file. They agree on the descriptor
model and the status lifecycle but **differ materially** in how they claim a job under concurrency,
whether they honour the queue-level retry budget, whether cancellation is status-guarded, and where
they read "now" from. Those divergences are recorded per backend below. Lock providers
(`SqlJobLockProvider`, `RedisJobLockProvider`, `FileJobLockProvider`) share the `IJobLockProvider`
contract and are consumed by the recurring scheduler's leader election as well as by a consumer
serialising its own
worker coordination; nothing in this area uses them internally.

## Requirements

### Requirement: Job descriptor shape and defaults

The system SHALL represent an enqueued unit of work as a `JobDescriptor` whose `Guid` defaults to a
new `Guid`, whose `EnqueuedAt` defaults to `DateTime.UtcNow` at construction, whose `Status`
defaults to `JobStatus.Pending`, whose `MaxRetries` (an `int?`) defaults to `3`, whose `Priority` defaults to
`0`, and whose `Metadata` defaults to an empty `Dictionary<string, string>`.

#### Scenario: Constructing a bare descriptor

- **Given** no property is assigned
- **When** `new JobDescriptor()` is evaluated
- **Then** `Guid` is a fresh non-empty `Guid`, `Status` is `JobStatus.Pending`, `MaxRetries` is `3`, `Priority` is `0`, `JobType` is `string.Empty`, `Metadata` is an empty dictionary, and `EnqueuedAt` is the UTC clock reading at construction time

#### Scenario: Nullable fields default to null

- **Given** a freshly constructed `JobDescriptor`
- **When** its optional fields are inspected
- **Then** `SerializedInput`, `InputType`, `QueueName`, `Delay`, `ScheduledAt`, `LastAttemptAt`, `CompletedAt` and `LastError` are all `null`, and `AttemptCount` is `0`

### Requirement: Job status lifecycle values

The system SHALL define the job lifecycle as the `JobStatus` enum `Pending = 0`, `Scheduled = 1`,
`Processing = 2`, `Completed = 3`, `Failed = 4`, `Dead = 5`, `Cancelled = 6`, and SHALL leave
`Failed` unused by every shipped queue implementation — a retry-eligible failure is recorded as
`Scheduled` and an exhausted one as `Dead`.

#### Scenario: A retry-eligible failure never lands in Failed

- **Given** a job with `MaxRetries = 3` and `AttemptCount = 1`
- **When** `FailAsync(jobId, "boom")` is called on any shipped backend
- **Then** the stored status is `JobStatus.Scheduled` with a future `ScheduledAt`, and is never `JobStatus.Failed`

#### Scenario: A job parked in Failed can never be dequeued again

- **Given** a stored job whose status was set to `JobStatus.Failed` by external means
- **When** `DequeueAsync` runs on any shipped backend
- **Then** the job is not returned, because every eligibility predicate admits only `Pending`, (`Scheduled` and due) or — on SQL only — (`Processing` and abandoned past the claim timeout)

### Requirement: BackgroundJobs retry-delay computation

The system SHALL compute the delay before the next attempt from
`Birko.BackgroundJobs.RetryPolicy.GetDelay(attemptNumber)` as `BaseDelay` when
`UseExponentialBackoff` is `false`, and otherwise as `BaseDelay.Ticks * 2^(attemptNumber - 1)`
saturated at `MaxDelay`, with defaults `MaxRetries = 3`, `BaseDelay = 30s`, `MaxDelay = 1h`,
`UseExponentialBackoff = true`.

#### Scenario: Exponential growth across attempts

- **Given** `new RetryPolicy()` with default `BaseDelay = 30s` and `MaxDelay = 1h`
- **When** `GetDelay(1)`, `GetDelay(2)` and `GetDelay(3)` are called
- **Then** they return 30 seconds, 60 seconds and 120 seconds respectively

#### Scenario: Saturation at MaxDelay instead of tick overflow

- **Given** a policy whose `BaseDelay` and `attemptNumber` are large enough that `BaseDelay.Ticks * 2^(attemptNumber-1)` exceeds `MaxDelay.Ticks`
- **When** `GetDelay` is called
- **Then** it returns exactly `MaxDelay`, because the scaled value is compared as a `double` before any `TimeSpan` is allocated — no negative `TimeSpan` is produced

#### Scenario: Fixed delay when backoff is disabled

- **Given** `new RetryPolicy { UseExponentialBackoff = false, BaseDelay = TimeSpan.FromMinutes(5) }`
- **When** `GetDelay(7)` is called
- **Then** it returns 5 minutes

#### Scenario: A zero attempt number yields a fraction of BaseDelay

- **Given** `new RetryPolicy()` with `BaseDelay = 30s`
- **When** `GetDelay(0)` is called
- **Then** it returns 15 seconds, because the exponent `attemptNumber - 1` is `-1` and `2^-1 = 0.5`; this class does **not** clamp `attemptNumber` to a minimum of 1

#### Scenario: RetryPolicy.None disables retries

- **Given** `RetryPolicy.None`
- **When** its `MaxRetries` is read
- **Then** it is `0`

### Requirement: A job's attempt budget is its own or the queue's policy

The system SHALL give a job `RetryPolicy.MaxRetriesFor(job.MaxRetries)` attempts — its own `MaxRetries` when it is
set, otherwise the queue's `RetryPolicy.MaxRetries` — on every shipped backend (InMemory, JSON, XML, SQL including its
reclaim, MongoDB, Elasticsearch, RavenDB, Cosmos DB, Redis). `null` means "the queue's policy", `0` means no retry (the
job is `Dead` on its first failure), and `n` means `n` attempts. Every backend SHALL store a null `MaxRetries` as null
and read it back as null. A SQL table created before `MaxRetries` became nullable has a `NOT NULL` column: it refuses a
null budget, and `DetectDrift` reports it as `NullabilityMismatch`; making the column nullable is the operator's step.
(TASK-662, decided 2026-10-18: `0` meant "the policy" on three backends and "no retries" on six.)

#### Scenario: A job that sets no budget follows the queue's policy

- **Given** a queue whose `RetryPolicy.MaxRetries` is `2` and a job enqueued with `MaxRetries = null`
- **When** every claim of it fails
- **Then** it is claimed twice and is then `Dead`, and `GetAsync` reads `MaxRetries` back as `null`

#### Scenario: A budget of zero means no retry

- **Given** the same queue and a job enqueued with `MaxRetries = 0`
- **When** its first claim fails
- **Then** it is `Dead` after that one attempt, on every backend

#### Scenario: An old SQL column is reported, not silently relied on

- **Given** a `__BackgroundJobs` table whose `MaxRetries` column is `NOT NULL`
- **When** `DetectDrift(typeof(JobDescriptorModel))` runs
- **Then** it reports `MaxRetries` as `NullabilityMismatch`, and enqueueing a job with `MaxRetries = null` fails

### Requirement: Shared contracts retry policy is a distinct type with different defaults

The system SHALL also expose `Birko.RetryPolicy` in `Birko.Contracts`, which differs from
`Birko.BackgroundJobs.RetryPolicy`: its defaults are `BaseDelay = 5s` and `MaxDelay = 5min`, it
exposes a configurable `BackoffMultiplier` (default `2.0`) and an opt-in `AddJitter` (default
`false`) applying a ±25% factor, and it clamps `attemptNumber` to a minimum of `1`.

#### Scenario: Contracts policy clamps a non-positive attempt number

- **Given** `new Birko.RetryPolicy()` with `BaseDelay = 5s`
- **When** `GetDelay(0)` or `GetDelay(-3)` is called
- **Then** it returns 5 seconds, because `attemptNumber` is clamped to `1` before the exponent is applied

#### Scenario: Jitter widens the delay band

- **Given** `new Birko.RetryPolicy { AddJitter = true, UseExponentialBackoff = false, BaseDelay = TimeSpan.FromSeconds(100) }`
- **When** `GetDelay(1)` is called repeatedly
- **Then** each result lies in `[75s, 125s)` because the delay ticks are multiplied by `0.75 + random * 0.5`

#### Scenario: NaN scaling saturates rather than throwing

- **Given** a contracts policy whose `BackoffMultiplier` produces a `NaN` scaled tick count
- **When** `GetDelay` is called
- **Then** it returns `MaxDelay`, because `double.IsNaN(scaled)` is checked alongside the saturation comparison

#### Scenario: The job queues bind to the BackgroundJobs policy, not the contracts one

- **Given** every shipped `IJobQueue` constructor takes a `RetryPolicy?` parameter and `JobQueueOptions.RetryPolicy` is declared as `RetryPolicy`
- **When** the type is resolved from within namespace `Birko.BackgroundJobs`
- **Then** it is `Birko.BackgroundJobs.RetryPolicy` (30s/1h, no multiplier, no jitter), so `AddJitter` and `BackoffMultiplier` are unreachable for job retries

### Requirement: Enqueue persists the descriptor and returns its identity

The system SHALL accept a `JobDescriptor` through `IJobQueue.EnqueueAsync` and return the `Guid`
identifying the stored job, persisting the descriptor's job type, input type, serialized input,
queue name, priority, retry budget, status, attempt count, timestamps and serialized metadata.

#### Scenario: Store-backed enqueue inserts a new row

- **Given** a `SqlJobQueue<DB>` and a descriptor with `JobType = "MyJob, MyAsm"`
- **When** `EnqueueAsync(descriptor)` is called
- **Then** `JobDescriptorModel.FromDescriptor` maps the descriptor onto the `__BackgroundJobs` table model, `AsyncDataBaseBulkStore.CreateAsync` inserts it, and the store-assigned `Guid` is returned

#### Scenario: Redis enqueue writes three keys in one script

- **Given** a `RedisJobQueue` with `KeyPrefix = "birko:jobs"` and a descriptor with `Guid = g`, `QueueName = null`, `Status = Pending`
- **When** `EnqueueAsync(descriptor)` is called
- **Then** one Lua script replaces the hash `birko:jobs:job:{g}` with the descriptor fields, adds `{g}` to `birko:jobs:status:0` (removing it from the status set of a job stored under the same id before), and adds `{g}` to the delayed set `birko:jobs:queue:default` scored `(ScheduledAt ?? EnqueuedAt).Ticks / 1e4`; a descriptor whose status is not `Pending` or `Scheduled` is stored but never queued (TASK-663, SH-M014 / SH-M016: it was three round trips, and a Cancelled descriptor was queued and run)

#### Scenario: InMemory enqueue restamps EnqueuedAt from the injected clock

- **Given** an `InMemoryJobQueue` built with a `TestDateTimeProvider` whose `UtcNow` is `2026-01-01T00:00:00Z`, and a descriptor whose `EnqueuedAt` was defaulted to the real wall clock
- **When** `EnqueueAsync(descriptor)` is called
- **Then** `descriptor.EnqueuedAt` is overwritten with `2026-01-01T00:00:00Z` before storage, so dequeue's `ThenBy(EnqueuedAt)` ordering is deterministic

#### Scenario: No other backend restamps EnqueuedAt

- **Given** a `SqlJobQueue`, `MongoDBJobQueue`, `ElasticSearchJobQueue`, `RavenDBJobQueue`, `CosmosDBJobQueue`, `RedisJobQueue`, `JsonJobQueue` or `XmlJobQueue`
- **When** `EnqueueAsync(descriptor)` is called
- **Then** the descriptor's existing `EnqueuedAt` is copied through unchanged (`LoadFrom` / `SerializeDescriptor`), so the persisted enqueue time is whatever the caller's `JobDescriptor` constructor captured from `DateTime.UtcNow`

### Requirement: Dequeue eligibility and ordering

The system SHALL return from `DequeueAsync` the single highest-priority, then oldest-enqueued job
that is either `Pending`, or `Scheduled` with a non-null `ScheduledAt` at or before the current
time — or, on SQL only, `Processing` past the claim timeout (see the SQL reclaim requirement) — and SHALL
return `null` when no such job exists.

#### Scenario: Priority wins over enqueue order

- **Given** a pending job A with `Priority = 0` enqueued first and a pending job B with `Priority = 5` enqueued second
- **When** `DequeueAsync()` is called on any store-backed or in-memory backend
- **Then** B is returned, because the order is `ByDescending(Priority).ThenBy(EnqueuedAt)`

#### Scenario: Equal priority dequeues FIFO

- **Given** two pending jobs with equal `Priority` and distinct `EnqueuedAt`
- **When** `DequeueAsync()` is called
- **Then** the older one is returned

#### Scenario: A future-scheduled job is not eligible

- **Given** a job with `Status = Scheduled` and `ScheduledAt` one hour in the future
- **When** `DequeueAsync()` is called
- **Then** `null` is returned

#### Scenario: A Scheduled job with a null ScheduledAt is never eligible

- **Given** a job with `Status = Scheduled` and `ScheduledAt = null`
- **When** `DequeueAsync()` is called on any store-backed backend
- **Then** it is not returned, because every filter requires `j.ScheduledAt != null && j.ScheduledAt <= now`

#### Scenario: A Processing job is never re-offered

- **Given** a job left in `Status = Processing` (for example by a worker that crashed)
- **When** `DequeueAsync()` is called on any backend other than SQL
- **Then** it is not returned; no backend other than SQL implements a visibility timeout or lease expiry, and `PurgeAsync` does not remove `Processing` jobs either, so the job is stranded permanently

#### Scenario: Dequeue marks the job Processing and consumes an attempt

- **Given** an eligible pending job with `AttemptCount = 0`
- **When** `DequeueAsync()` returns it
- **Then** the stored status is `Processing`, `AttemptCount` is `1`, and `LastAttemptAt` is the current time — so the returned `JobDescriptor.AttemptCount` is 1-based for the attempt about to run

#### Scenario: Redis drains the highest priority first among due jobs

- **Given** a `RedisJobQueue`, a pending job B with `Priority = 0` enqueued at T and a pending job A with `Priority = 5` enqueued at T+30s
- **When** both are due and `DequeueAsync()` is called
- **Then** A is returned first, as on every backend: eligibility and order are decoupled. `{prefix}:queue:{name}` holds jobs by due time; the dequeue script moves due members (up to 100 per key per call) to `{prefix}:ready:{name}`, ranked `(999 - priority) * 1e12 + EnqueuedAt in ms since 2020`, and claims the lowest rank (TASK-663, decided by the user; priority used to span under 1 ms of score, so a mixed-priority queue drained FIFO)

#### Scenario: Redis keeps a future high-priority job waiting

- **Given** a job with `Priority = 999` scheduled 5 minutes ahead and a due job with `Priority = 0`
- **When** `DequeueAsync()` is called twice
- **Then** the due job is returned, then `null`: priority is not part of the due-time score

#### Scenario: Redis priority beyond ±999 is clamped

- **Given** a descriptor with `Priority = 100000` (or `-100000`)
- **When** it is ranked in the ready set
- **Then** the priority is clamped to `999` (or `-999`); within that range a negative priority runs after the default `0`

### Requirement: Queue-name scoping on dequeue

The system SHALL scope `DequeueAsync(queueName)` to the named queue, and on every backend SHALL additionally
admit jobs whose `QueueName` is `null`. On Redis a null `QueueName` is stored under the `default` key (shared with
jobs explicitly named `"default"`), and a named dequeue reads both keys in one script, claiming the better-scored
candidate; from the `default` key it takes only a member whose hash carries no `QueueName`, inspecting at most 100
due members (FIELD-028, fixed by TASK-561 — Redis used to serve an unnamed job only to a queue named `"default"`).

#### Scenario: A null-queue job is served to a named-queue request on most backends

- **Given** a pending job with `QueueName = null`
- **When** `DequeueAsync("reports")` is called on the in-memory, SQL, MongoDB, Elasticsearch, RavenDB, Cosmos DB, JSON or XML backend
- **Then** the job is returned, because the predicate is `queueName == null || j.QueueName == null || j.QueueName == queueName`

#### Scenario: Cosmos DB serves a dispatcher-enqueued job to the processor's default queue

- **Given** a job enqueued on Cosmos DB through `JobDispatcher.EnqueueAsync<TJob>()`, which leaves `QueueName = null`
- **When** `DequeueAsync(new JobQueueOptions().DefaultQueueName)` is called, as `BackgroundJobProcessor` does
- **Then** the job is returned and stored as `Processing`; the Cosmos serializer writes `"QueueName": null` explicitly, so the predicate's `j.QueueName == null` term matches the stored document (SH-H001, fixed by TASK-317 — the predicate used to omit that term, so no dispatcher-enqueued job ever ran on Cosmos)

#### Scenario: Redis routes a null-queue job to the "default" key

- **Given** a `RedisJobQueue` and a descriptor with `QueueName = null`
- **When** it is enqueued and then `DequeueAsync("default")` is called
- **Then** the job is returned, because `GetQueueKey(null)` and `GetQueueKey("default")` both resolve to `{prefix}:queue:default`

#### Scenario: Redis serves a null-queue job to any named queue

- **Given** a `RedisJobQueue`, a job with `QueueName = null` and `JobQueueOptions.DefaultQueueName = "work"`
- **When** `DequeueAsync("work")` is called
- **Then** the job is returned and stored as `Processing`; with a `"work"` job also due, the lower score (due time,
  then priority) is claimed first (`RedisUnnamedJobDequeueTests`, live)

#### Scenario: Redis keeps an explicitly "default" job on its queue

- **Given** a job enqueued with `QueueName = "default"`, sharing the key unnamed jobs use
- **When** `DequeueAsync("work")` is called
- **Then** it is not returned and stays `Pending`; an unnamed job queued behind it in that key is still found

#### Scenario: A job on a non-default queue is invisible to the shipped processor

- **Given** a job enqueued via `JobDispatcher.EnqueueOnAsync<TJob>("reports")` and a `BackgroundJobProcessor` with default `JobQueueOptions`
- **When** the processor loop polls
- **Then** it calls `DequeueAsync("default")` — `_options.DefaultQueueName` is passed unconditionally — and never claims the "reports" job; draining a non-default queue requires a separate processor configured with that `DefaultQueueName`

### Requirement: Concurrent dequeue claiming differs per backend

The system SHALL prevent two workers from claiming the same job, using a token-verified,
status-guarded conditional update on SQL, MongoDB and Elasticsearch; a single atomic Lua
script on Redis; an in-process `SemaphoreSlim` on the in-memory, JSON and XML backends; and a
compare-and-set that re-checks eligibility on the stored document on Cosmos DB (the item's ETag,
`AsyncCosmosDBStore.UpdateIfAsync`, TASK-661) and RavenDB (load by id and optimistic concurrency,
`AsyncRavenDBStore.UpdateIfAsync`, TASK-665).

#### Scenario: SQL claims via ClaimToken and verifies by re-read

- **Given** two `SqlJobQueue<DB>` workers racing for the same eligible row
- **When** each issues `UpdateAsync(filter: j => j.Guid == id && j.Status == originalStatus, updates: Set(Status, Processing).Set(ClaimToken, myToken).Set(AttemptCount, n+1).Set(LastAttemptAt, now))` and then re-reads the row
- **Then** only the worker whose `ClaimToken` matches the stored value returns the descriptor; the loser skips to the next candidate, retrying up to `MaxClaimAttempts = 32` times before returning `null`

#### Scenario: The claim guard is status-only, so a rescheduled job is re-claimed ahead of its ScheduledAt

- **Given** a SQL, MongoDB, Elasticsearch or RavenDB worker B that has read a due `Scheduled` candidate and then stalls, while worker A claims that job, runs it, and `FailAsync` puts it back to `Scheduled` with a future `ScheduledAt`
- **When** B finally issues its conditional update, whose only guard is `j.Guid == claimId && j.Status == originalStatus`
- **Then** the filter matches again, B's `ClaimToken` verifies on the re-read, and B returns the descriptor — the job runs before its `ScheduledAt`, and `AttemptCount` is overwritten with B's stale `candidate.AttemptCount + 1`, so the retry budget is rewound; the guard compares status only, never `ScheduledAt`, `AttemptCount` or a version

#### Scenario: A crowded queue exhausts the claim attempt budget

- **Given** a SQL, MongoDB, Elasticsearch or RavenDB queue where the caller loses 32 consecutive claim races
- **When** `DequeueAsync` finishes its loop
- **Then** it returns `null` even though eligible jobs exist

#### Scenario: Elasticsearch treats a conflicted claim as a lost race

- **Given** an `ElasticSearchJobQueue` and two racing workers that read the same eligible candidate
- **When** worker A's conditional `update_by_query` lands first and worker B's then matches the version A replaced
- **Then** B's `UpdateAsync` throws `StoreConcurrencyException`, which the dequeue loop catches and treats as a lost race, moving on to the next candidate within the same `MaxClaimAttempts` budget; any other store failure propagates

#### Scenario: Elasticsearch verifies its claim with a realtime read

- **Given** an `ElasticSearchJobQueue` holding two pending jobs on one queue
- **When** `DequeueAsync` performs its conditional update and reads the claimed document back
- **Then** the read-back is `ReadRealtimeAsync(claimId)` — a realtime GET by document id that sees the claim's own write before the index refreshes — so the `ClaimToken` comparison succeeds and the call returns that one job, while the other stays `Pending`; a search-based re-read used to miss the claim until refresh, so one call claimed both jobs, left them in `Processing` and returned `null` (TASK-616)

#### Scenario: Redis performs the whole claim in one script

- **Given** a `RedisJobQueue`
- **When** `DequeueAsync` runs
- **Then** a single `ScriptEvaluateAsync` promotes due members to the ready set, takes the best-ranked member whose hash `Status` is still `Pending` or `Scheduled` (dropping any other member it meets), removes it, moves status-set membership, increments `AttemptCount` and sets `Status = Processing` and `LastAttemptAt` atomically, returning the job id or nil

#### Scenario: Redis discards the queue entry before the job is done

- **Given** a job claimed by the Redis dequeue script (its member has been `ZREM`ed from the queue sorted set)
- **When** the worker dies before calling `CompleteAsync` or `FailAsync`
- **Then** the job hash remains in `Status = Processing` with no queue-set membership, and nothing re-adds it (reclaim on every backend is [[TASK-664]])

#### Scenario: JSON and XML serialise dequeue in-process only

- **Given** two worker tasks in one process sharing a `JsonJobQueue` (or `XmlJobQueue`)
- **When** both call `DequeueAsync` concurrently
- **Then** the private `_dequeueLock` `SemaphoreSlim(1,1)` serialises read-claim-update so only one gets the job; two separate *processes* over the same file have no compare-and-swap and can both claim it

#### Scenario: Cosmos DB claims by ETag

- **Given** several `CosmosDBJobQueue` workers polling the same eligible documents
- **When** they call `DequeueAsync` concurrently
- **Then** each job is returned to one worker with `AttemptCount = 1`: a claim replaces the item only if its ETag is unchanged, a lost race (412) re-reads it and finds it no longer due, and the worker moves on to the next of up to 8 candidates (measured on the emulator before TASK-661: 6 workers, 6 jobs, 24 claims of 4 jobs)

### Requirement: SQL reclaims a job abandoned in Processing, bounded by its retry budget

The system SHALL, on `SqlJobQueue<DB>` only, additionally treat as a dequeue candidate a job in
`Processing` whose `LastAttemptAt` is non-null and earlier than `now - claimTimeout`, where
`claimTimeout` is a constructor argument defaulting to 30 minutes (the default `JobQueueOptions.JobTimeout`),
and SHALL claim it through the same `ClaimToken` conditional update as any other candidate, except that a
reclaim's filter requires the row to be still stale — `j.Guid == claimId && j.Status == Processing &&
j.LastAttemptAt != null && j.LastAttemptAt < staleBefore` — rather than merely still `Processing`. When such
a stale candidate's `AttemptCount` is already `>= RetryPolicy.MaxRetriesFor(MaxRetries)` (the row's own value, or the
queue's policy when it is null), the system SHALL instead mark it `Dead`, stamp `CompletedAt`, set `LastError` to an
`"Abandoned: held in Processing for longer than {claimTimeout} after {AttemptCount} attempt(s), and the attempt budget is spent."`
message, and move on to the next candidate within the same `MaxClaimAttempts` budget; that burial is a
conditional `PropertyUpdate` under the same still-stale filter, not a write of the whole row. No other backend
reclaims a `Processing` job.

#### Scenario: A job held in Processing past the claim timeout is re-offered

- **Given** a `SqlJobQueue<DB>` with the default 30-minute claim timeout and a job in `Processing` with `AttemptCount = 1`, `MaxRetries = 3` and `LastAttemptAt` 31 minutes ago
- **When** `DequeueAsync()` is called
- **Then** the job is returned with a new `ClaimToken`, `AttemptCount = 2` and `LastAttemptAt = now`

#### Scenario: A recently claimed Processing job is not reclaimed

- **Given** a `SqlJobQueue<DB>` and a job in `Processing` whose `LastAttemptAt` is 5 minutes ago
- **When** `DequeueAsync()` is called
- **Then** the job is not returned

#### Scenario: An abandoned job with no attempts left is buried

- **Given** a `SqlJobQueue<DB>` and a job in `Processing` past the claim timeout with `AttemptCount = 3` and `MaxRetries = 3`
- **When** `DequeueAsync()` reaches it
- **Then** it is updated to `Dead` with `CompletedAt` stamped and an `"Abandoned: …"` `LastError`, is not returned, and the loop continues to the next candidate

#### Scenario: A job still running past the claim timeout is handed to a second worker

- **Given** a host that raised `JobQueueOptions.JobTimeout` above the queue's `claimTimeout`, and a job still genuinely running after `claimTimeout`
- **When** another worker calls `DequeueAsync()`
- **Then** the job is claimed a second time — nothing distinguishes a dead holder from a slow one, so reclaiming is safe only for idempotent jobs

#### Scenario: Two workers that read the same stale job reclaim it once

- **Given** two `SqlJobQueue<DB>` workers that both read the same stale `Processing` candidate before either writes
- **When** worker A's conditional update and re-read complete, and then worker B issues its own update filtered on the still-stale condition
- **Then** B's filter no longer matches — A's write set `LastAttemptAt` to its `now`, which is not earlier than `staleBefore` — so B's `ClaimToken` does not verify on its re-read, B moves on to the next candidate, and only A returns the job (TASK-575; the filter used to be `j.Status == Processing` alone, which a reclaim does not change, so both workers returned the job)

#### Scenario: A job completed by its slow worker is not buried Dead

- **Given** a `SqlJobQueue<DB>` worker that read a stale `Processing` candidate with its attempt budget spent, and the job's original (slow, not dead) worker then calls `CompleteAsync`
- **When** the reclaiming worker issues its `Dead` burial
- **Then** the still-stale filter no longer matches the `Completed` row, so it stays `Completed`; the burial used to be a read-modify-write of the whole row, which overwrote the completed job as `Dead` (TASK-575)

### Requirement: Completing a job

The system SHALL, on `CompleteAsync(jobId)`, set the job's status to `Completed` and its
`CompletedAt` to the current time, and SHALL do nothing when the job does not exist.

#### Scenario: A processing job is completed

- **Given** a job in `Status = Processing`
- **When** `CompleteAsync(jobId)` is called
- **Then** the stored status becomes `Completed` and `CompletedAt` is stamped

#### Scenario: Completing an unknown id is a no-op

- **Given** a `jobId` that is not stored
- **When** `CompleteAsync(jobId)` is called
- **Then** the call returns without error and without creating anything (store-backed backends' guarded update matches nothing; Redis's script finds no `Processing` status; in-memory returns early on `TryGetValue == false`)

#### Scenario: Completion acts only on a claimed job

- **Given** a job in `Status = Pending`, `Scheduled`, `Cancelled` or `Dead`
- **When** `CompleteAsync(jobId)` or `FailAsync(jobId, error)` is called
- **Then** nothing changes, on every backend: both act only on a `Processing` job (TASK-158; Cosmos DB since TASK-661, Redis since TASK-663, where the check and the write are one script)

### Requirement: Failure escalates to a scheduled retry or to Dead

The system SHALL, on `FailAsync(jobId, error)`, record `error` as `LastError`, and then either set
`Status = Scheduled` with `ScheduledAt = now + retryPolicy.GetDelay(attemptCount)` when attempts
remain, or set `Status = Dead` with `CompletedAt` stamped when they do not.

#### Scenario: Retries remain

- **Given** a job with `MaxRetries = 3` and `AttemptCount = 1`
- **When** `FailAsync(jobId, "timeout")` is called with the default policy
- **Then** `LastError` is `"timeout"`, `Status` is `Scheduled`, and `ScheduledAt` is 30 seconds ahead (`GetDelay(1)`)

#### Scenario: Retries exhausted

- **Given** a job with `MaxRetries = 3` and `AttemptCount = 3`
- **When** `FailAsync(jobId, "boom")` is called
- **Then** `Status` is `Dead` and `CompletedAt` is stamped

#### Scenario: Failing an unknown id is a no-op

- **Given** a `jobId` that is not stored
- **When** `FailAsync(jobId, "boom")` is called
- **Then** the call returns without error

### Requirement: Cancellation is status-guarded on every backend

The system SHALL cancel a job through `CancelAsync(jobId)` only when its current status is `Pending`
or `Scheduled`, returning `true` on success and `false` otherwise, on every backend (Cosmos DB since
TASK-661; it applied no status guard and returned `true` for any existing job).

#### Scenario: Cancelling a pending job succeeds

- **Given** a job in `Status = Pending`
- **When** `CancelAsync(jobId)` is called
- **Then** the status becomes `Cancelled`, `CompletedAt` is stamped, and `true` is returned

#### Scenario: Cancelling a processing job is refused

- **Given** a job in `Status = Processing`
- **When** `CancelAsync(jobId)` is called on any backend
- **Then** `false` is returned and the status is unchanged

#### Scenario: Cancelling an unknown job returns false

- **Given** a `jobId` that is not stored
- **When** `CancelAsync(jobId)` is called
- **Then** `false` is returned

#### Scenario: Cosmos DB refuses to cancel a completed job

- **Given** a Cosmos DB job in `Status = Completed` (or `Processing`, or `Dead`)
- **When** `CancelAsync(jobId)` is called
- **Then** `false` is returned and the item is unchanged: the cancel is a compare-and-set whose condition is `Pending | Scheduled`

#### Scenario: Redis cancellation also detaches the queue entry

- **Given** a Redis job in `Status = Scheduled` on queue `reports`
- **When** `CancelAsync(jobId)` is called
- **Then** one script checks the status and, only when it is `Pending` or `Scheduled`, removes the id from `{prefix}:status:1`, `{prefix}:queue:reports` and `{prefix}:ready:reports` (the queue name is read from the job hash), flips the hash to `Cancelled` with `CompletedAt` and adds the id to `{prefix}:status:6`; a claim cannot land between the check and the flip (TASK-663, SH-M018: measured 5 of 5 runs, a job both claimed and cancelled)

#### Scenario: Redis status sets stay disjoint under concurrent transitions

- **Given** claimed Redis jobs on which `CompleteAsync` and `FailAsync` race
- **When** both have returned
- **Then** each job is in exactly one `{prefix}:status:*` set, matching its hash, and a fail that lost writes no `LastError` (SH-M017: each transition moved set membership in four commands and Fail rewrote the whole hash)

### Requirement: Reading a single job and listing by status

The system SHALL return the current `JobDescriptor` for a `jobId` via `GetAsync` (or `null` when
absent), and SHALL return via `GetByStatusAsync(status, limit)` the jobs in that status ordered by
`EnqueuedAt` descending, truncated to `limit` (default `100`).

#### Scenario: Get returns the live state

- **Given** a job that has been dequeued
- **When** `GetAsync(jobId)` is called
- **Then** the returned descriptor has `Status = Processing` and `AttemptCount = 1`

#### Scenario: Get on an unknown id returns null

- **Given** an id with no stored job
- **When** `GetAsync(id)` is called
- **Then** `null` is returned

#### Scenario: Listing by status is newest-first

- **Given** five `Dead` jobs with distinct `EnqueuedAt`
- **When** `GetByStatusAsync(JobStatus.Dead, limit: 2)` is called
- **Then** the two most recently enqueued are returned

#### Scenario: Redis materialises the whole status set before truncating

- **Given** a Redis `{prefix}:status:3` set with 1000 members and `limit = 10`
- **When** `GetByStatusAsync(JobStatus.Completed, 10)` is called
- **Then** every member's hash is fetched (Redis sets are unordered), the results are then ordered by `EnqueuedAt` descending and only then truncated to 10 — so the call costs 1000 round trips

#### Scenario: The in-memory queue hands back live references

- **Given** an `InMemoryJobQueue`
- **When** `GetAsync` or `GetByStatusAsync` or `DequeueAsync` returns a descriptor
- **Then** it is the same `JobDescriptor` instance held in the internal `ConcurrentDictionary`, so a caller mutating it mutates the queue's state

### Requirement: Purge removes only terminal jobs older than the retention window

The system SHALL, on `PurgeAsync(olderThan)`, delete jobs whose status is `Completed`, `Dead` or
`Cancelled` and whose `CompletedAt` is non-null and strictly earlier than `now - olderThan`, and
SHALL return the number of jobs removed.

#### Scenario: An old completed job is purged

- **Given** a job with `Status = Completed` and `CompletedAt` 8 days ago
- **When** `PurgeAsync(TimeSpan.FromDays(7))` is called
- **Then** the job is deleted and the returned count includes it

#### Scenario: A recent dead job survives

- **Given** a job with `Status = Dead` and `CompletedAt` 1 hour ago
- **When** `PurgeAsync(TimeSpan.FromDays(7))` is called
- **Then** the job is retained

#### Scenario: A terminal job with no CompletedAt is never purged

- **Given** a job with `Status = Cancelled` and `CompletedAt = null`
- **When** `PurgeAsync` is called with any window
- **Then** it is retained, because the predicate requires `CompletedAt != null`

#### Scenario: Pending, Scheduled and Processing jobs are never purged

- **Given** jobs in `Pending`, `Scheduled` and `Processing`
- **When** `PurgeAsync(TimeSpan.Zero)` is called
- **Then** none of them is deleted

#### Scenario: Redis purge scans each terminal status set

- **Given** a `RedisJobQueue`
- **When** `PurgeAsync(olderThan)` is called
- **Then** it iterates the `Completed`, `Dead` and `Cancelled` status sets, reads each member's `CompletedAt` tick field, and for each expired one deletes the job hash and removes the id from that status set

#### Scenario: Purge is never invoked automatically

- **Given** a `BackgroundJobProcessor` configured with `JobQueueOptions.RetentionPeriod = 7 days`
- **When** the processor runs indefinitely
- **Then** `PurgeAsync` is never called; `RetentionPeriod` is not read anywhere in this capability and purging is entirely the consumer's responsibility

### Requirement: Recurring scheduling is coordinated by leader election, and is opt-in

The system SHALL keep each recurring definition's `NextRunAt` in the scheduler's own memory, and SHALL
therefore, when given an `IJobLockProvider`, enqueue occurrences only while it holds a named leadership
lock. Without a provider it SHALL behave exactly as before — every scheduler enqueues — so the feature is
opt-in and existing call sites are unaffected.

#### Scenario: Without a lock provider every worker still schedules

- **Given** a `RecurringJobScheduler` constructed with no `lockProvider`
- **When** a definition becomes due in several processes at once
- **Then** each enqueues its own occurrence, bit-for-bit the prior behaviour, and `IsLeader` reports true
  — because `NextRunAt` is per-process memory, so N workers independently conclude the job is due

#### Scenario: Only the lock holder enqueues

- **Given** several schedulers sharing one `lockName` and a lock provider
- **When** a definition becomes due
- **Then** only the holder enqueues; a follower makes **no scheduling decision at all** rather than
  skipping the enqueue while advancing `NextRunAt`

#### Scenario: A new leader re-baselines rather than firing immediately

- **Given** leadership passes to another scheduler
- **When** it takes the lock
- **Then** it sets `NextRunAt = now + interval` for each definition, because it cannot know what the
  previous leader already enqueued — so it neither fires immediately nor inherits a stale overdue instant

#### Scenario: A leadership lock needs a name and a positive retry interval

- **Given** a lock provider with a blank `lockName`, or a non-positive `leadershipRetryInterval`
- **When** the scheduler is constructed
- **Then** `ArgumentException` is thrown

#### Scenario: Leadership is re-checked, not assumed once

- **Given** a scheduler that acquired leadership and then lost the lock
- **When** the loop next considers a due definition
- **Then** `IsLeader` is `_isLeader && _lockProvider.IsLocked`, so a lost lock stops it enqueueing, and
  the death of a leader is recovered from without restarting every worker

### Requirement: A lock's two durations are distinct, and lease-vs-session is advertised

The system SHALL expose `TryAcquireAsync(lockName, acquireTimeout, leaseDuration?, ct)` on
`IJobLockProvider`, separating how long to wait for the lock from how long it is held, and SHALL declare
`IsLeaseBased` so a caller can tell a lease that expires from a session that dies with its owner.

#### Scenario: One argument had meant three different things

- **Given** the earlier single-`timeout` signature
- **When** it reached each implementation
- **Then** SQL/PostgreSQL ignored it, SQL/MSSql and MySQL waited on it, and Redis used it as the key's
  expiry — releasing the lock while the holder was still working. Splitting the durations is what makes
  the providers substitutable behind one interface

#### Scenario: A session lock refuses a lease duration

- **Given** `FileJobLockProvider`, whose `IsLeaseBased` is `false`
- **When** `TryAcquireAsync` is called with a non-null `leaseDuration`
- **Then** it throws, stating that a file lock is session-scoped and cannot expire, and naming both the
  null-lease option and the lease-based alternative
- **And** the lock itself is a `FileStream` opened `FileShare.None`, so the OS releases it if the process
  dies — genuine session semantics no document store in this family can offer

### Requirement: Counting jobs is an optional capability, not a queue-contract member

The system SHALL expose `CountByStatusAsync` on a separate `IJobQueueCounts` interface that a queue may
implement, rather than adding a member to the queue contract.

#### Scenario: A capability only some backends can answer efficiently

- **Given** nine queue implementations
- **When** a counting capability is needed
- **Then** it is advertised by implementing `IJobQueueCounts`; adding the member to the shared contract
  would break every implementation for a capability only some can serve efficiently, so a caller feature-
  detects instead

#### Scenario: Only the in-memory and SQL queues advertise counts

- **Given** the nine shipped queues
- **When** each is tested for `IJobQueueCounts`
- **Then** only `InMemoryJobQueue` (grouping its dictionary by status) and `SqlJobQueue<DB>` (one `COUNT` per `JobStatus` value) implement it, and both leave statuses with no jobs out of the result

### Requirement: Clock source differs per backend

The system SHALL derive "now" from an injected `IDateTimeProvider` in the in-memory, JSON and XML
queues, the `JobDispatcher` and the `RecurringJobScheduler`, and SHALL read `DateTime.UtcNow`
directly in the SQL, MongoDB, Elasticsearch, RavenDB, Cosmos DB and Redis queues.

#### Scenario: A test clock controls the in-memory queue end to end

- **Given** an `InMemoryJobQueue(new TestDateTimeProvider(t0))` holding a job scheduled for `t0 + 1h`
- **When** the test clock is advanced past `t0 + 1h` and `DequeueAsync()` is called
- **Then** the job is returned, because eligibility, `EnqueuedAt`, `LastAttemptAt`, `CompletedAt`, retry `ScheduledAt` and the purge cutoff all read the injected provider

#### Scenario: A test clock cannot influence the SQL queue

- **Given** a `SqlJobQueue<DB>` (which takes no `IDateTimeProvider`)
- **When** `DequeueAsync`, `CompleteAsync`, `FailAsync`, `CancelAsync` or `PurgeAsync` run
- **Then** they compare against and stamp the real `DateTime.UtcNow`; the same holds for MongoDB, Elasticsearch, RavenDB, Cosmos DB and Redis

#### Scenario: A queue constructor rejects a null clock

- **Given** `JsonJobQueue`, `XmlJobQueue`, `InMemoryJobQueue`, `JobDispatcher` or `RecurringJobScheduler`
- **When** it is constructed with a null `IDateTimeProvider`
- **Then** an `ArgumentNullException` is thrown

### Requirement: Job resolution and invocation

The system SHALL, in `JobExecutor.ExecuteAsync`, resolve the job type by
`Type.GetType(descriptor.JobType)`, instantiate it through the supplied `Func<Type, object>`
factory, build a `JobContext` from the descriptor's id, attempt count, enqueue time and metadata,
and invoke either `IJob<TInput>.ExecuteAsync(input, context, ct)` reflectively when both `InputType`
and `SerializedInput` are non-null, or `IJob.ExecuteAsync(context, ct)` otherwise — returning
`JobResult.Succeeded(elapsed)` or `JobResult.Failed(elapsed, message, exception)`.

#### Scenario: A parameterless job runs

- **Given** a descriptor whose `JobType` resolves to a class implementing `IJob`, with `InputType = null`
- **When** `ExecuteAsync(descriptor)` is called
- **Then** `IJob.ExecuteAsync(context, ct)` is awaited and `JobResult.Succeeded` is returned with the measured duration

#### Scenario: A typed job runs via reflection

- **Given** a descriptor with `InputType` and `SerializedInput` set, whose job class exposes `ExecuteAsync(TInput, JobContext, CancellationToken)`
- **When** `ExecuteAsync(descriptor)` is called
- **Then** the input is deserialized to `TInput`, the method is located by exact parameter types, invoked, and its returned `Task` awaited

#### Scenario: Unresolvable job type

- **Given** a descriptor whose `JobType` string cannot be loaded
- **When** `ExecuteAsync(descriptor)` is called
- **Then** `JobResult.Failed` is returned with error `"Job type not found: {JobType}"` and no factory call is made

#### Scenario: Unresolvable input type

- **Given** a descriptor with a non-null `SerializedInput` and an `InputType` string that cannot be loaded
- **When** `ExecuteAsync(descriptor)` is called
- **Then** `JobResult.Failed` is returned with error `"Input type not found: {InputType}"`

#### Scenario: Input deserializes to null

- **Given** a descriptor whose `SerializedInput` deserializes to `null` for the resolved input type
- **When** `ExecuteAsync(descriptor)` is called
- **Then** `JobResult.Failed` is returned with error `"Failed to deserialize job input"`

#### Scenario: The typed entry point is missing

- **Given** a job class that does not expose `ExecuteAsync(TInput, JobContext, CancellationToken)`
- **When** a typed descriptor for it is executed
- **Then** `JobResult.Failed` is returned with error `"ExecuteAsync method not found on {typeName}"`

#### Scenario: The matched method returns no awaitable

- **Given** a job class whose matched `ExecuteAsync` returns `null` instead of a `Task`
- **When** it is invoked
- **Then** `JobResult.Failed` is returned with `"ExecuteAsync on {typeName} did not return a Task"` rather than falling through to success

#### Scenario: A parameterless descriptor for a non-IJob class

- **Given** a descriptor with `InputType = null` whose resolved type does not implement `IJob`
- **When** `ExecuteAsync(descriptor)` is called
- **Then** `JobResult.Failed` is returned with `"Job {typeName} does not implement IJob"`

#### Scenario: A thrown exception is unwrapped one level

- **Given** a job whose body throws `InvalidOperationException("bad")`, surfaced through reflection as a `TargetInvocationException`
- **When** `ExecuteAsync(descriptor)` is called
- **Then** `JobResult.Failed` carries the *inner* exception (`ex.InnerException ?? ex`) and its `Message` as the error text

#### Scenario: Cancellation is reported as a failure, not propagated

- **Given** a job that observes its `CancellationToken` and throws `OperationCanceledException`
- **When** `ExecuteAsync(descriptor, ct)` is called with that token cancelled
- **Then** the catch-all converts it to `JobResult.Failed` — `JobExecutor` never rethrows cancellation

#### Scenario: JobContext exposes the attempt and metadata

- **Given** a descriptor with `AttemptCount = 2` and `Metadata { ["recurring.name"] = "nightly" }`
- **When** the executor builds the `JobContext`
- **Then** `context.AttemptNumber == 2`, `context.JobGuid == descriptor.Guid`, `context.EnqueuedAt == descriptor.EnqueuedAt`, and `context.Metadata["recurring.name"] == "nightly"`; a null metadata argument yields an empty dictionary rather than null

### Requirement: Job input serialization

The system SHALL serialize and deserialize job input through `IJobSerializer`, defaulting to
`JsonJobSerializer` which uses `SystemJsonSerializer` with `JsonNamingPolicy.CamelCase` and
`WriteIndented = false`, and SHALL allow a custom `ISerializer` to be supplied instead.

#### Scenario: Default camelCase serialization

- **Given** a `JsonJobSerializer()` with no options
- **When** an input object with a `FirstName` property is serialized
- **Then** the JSON uses `firstName` and is not indented

#### Scenario: A custom serializer is honoured

- **Given** `new JsonJobSerializer(customSerializer)`
- **When** `Serialize`/`Deserialize` are called
- **Then** they delegate to `customSerializer`; passing a null `ISerializer` throws `ArgumentNullException`

#### Scenario: Dispatcher and executor must agree on the serializer

- **Given** a `JobDispatcher` constructed with serializer A and a `JobExecutor` constructed with serializer B
- **When** a typed job is enqueued through the dispatcher and executed
- **Then** the payload written by A is read by B; each component independently defaults to `new JsonJobSerializer()` when its serializer argument is omitted

### Requirement: Metadata persistence round-trip

The system SHALL persist `JobDescriptor.Metadata` as a serialized string alongside the job,
producing `null` when the dictionary is empty, and SHALL restore it on read; the SQL, MongoDB,
Elasticsearch, RavenDB and Cosmos DB models SHALL use the shared camelCase
`JobSerializationHelper`, the JSON model its own default-options `SystemJsonSerializer`, the XML
model a `SerializableMetadata`/`MetadataEntry` element wrapper, and Redis the queue's injected
`ISerializer`.

#### Scenario: Empty metadata stores null

- **Given** a descriptor with an empty `Metadata` dictionary
- **When** it is mapped to any backing model
- **Then** the metadata column/field is `null` (`SerializeMetadata` returns null for a null-or-empty dictionary; the JSON/XML models guard on `Metadata.Count > 0`; Redis omits the hash entry)

#### Scenario: Metadata survives a round trip

- **Given** a descriptor with `Metadata { ["a"] = "1", ["b"] = "2" }`
- **When** it is enqueued and later returned by `GetAsync`
- **Then** the restored descriptor's `Metadata` contains both pairs

#### Scenario: XML metadata uses an element wrapper because dictionaries are unsupported

- **Given** an `XmlJobDescriptorModel`
- **When** metadata is serialized
- **Then** it becomes a `<Metadata>` root with one `<Entry key="...">value</Entry>` per pair, since `System.Xml.Serialization` cannot serialize `Dictionary<TKey,TValue>` directly

#### Scenario: Redis requires the core hash fields to be present

- **Given** a Redis job hash missing the `MaxRetries` field
- **When** `DeserializeDescriptor` runs
- **Then** it throws `KeyNotFoundException`, because `Id`, `JobType`, `Status`, `Priority`, `MaxRetries`, `AttemptCount` and `EnqueuedAt` are read with the indexer rather than `TryGetValue`

### Requirement: Persisted status is trusted as-is and a null stored identity is replaced

The system SHALL, in every backing model's `ToDescriptor`, cast the stored integer status directly to
`JobStatus` without range validation, and SHALL substitute a freshly generated `Guid` when the
stored `Guid` is null.

#### Scenario: An out-of-range stored status is passed through

- **Given** a stored row with `Status = 99`
- **When** `ToDescriptor()` is called
- **Then** the returned descriptor's `Status` is `(JobStatus)99`, which matches no eligibility, cancellation or purge predicate

#### Scenario: A null stored Guid yields a descriptor that cannot be addressed

- **Given** a stored job row whose `Guid` is null
- **When** `ToDescriptor()` is called
- **Then** `descriptor.Guid` is a brand-new random `Guid`, so a subsequent `CompleteAsync(descriptor.Guid)` or `FailAsync(descriptor.Guid, …)` finds no job and silently no-ops

### Requirement: Descriptor Delay is advisory and never persisted

The system SHALL treat `JobDescriptor.Delay` as an input to the enqueue caller only: no backing
model persists it, and no queue derives `ScheduledAt` or `Status` from it.

#### Scenario: A Delay-only descriptor runs immediately

- **Given** a descriptor with `Delay = TimeSpan.FromHours(1)`, `ScheduledAt = null` and `Status = Pending`
- **When** it is enqueued and `DequeueAsync()` is called
- **Then** the job is returned at once, because eligibility looks only at `Status` and `ScheduledAt`

#### Scenario: The dispatcher resolves Delay for the caller

- **Given** `JobDispatcher.ScheduleAsync<TJob>(TimeSpan.FromHours(1))`
- **When** the descriptor is built
- **Then** it carries `Delay`, `ScheduledAt = clock.UtcNow + delay` **and** `Status = JobStatus.Scheduled`, so the persisted `ScheduledAt`/`Status` pair is what defers the job

### Requirement: Processor polling and concurrency

The system SHALL run a `BackgroundJobProcessor` loop that, per iteration, prunes completed task
handles, waits on a `SemaphoreSlim` sized to `JobQueueOptions.MaxConcurrency` (default `4`), calls
`DequeueAsync(options.DefaultQueueName)`, and — when nothing is available — releases the semaphore
and sleeps `PollingInterval` (default 1 second) before polling again.

#### Scenario: Concurrency is capped

- **Given** `JobQueueOptions { MaxConcurrency = 2 }` and four eligible jobs
- **When** the processor runs
- **Then** at most two `ProcessJobAsync` tasks are in flight at any time; each releases its permit in a `finally`

#### Scenario: An empty queue backs off

- **Given** an empty queue and `PollingInterval = 1s`
- **When** the loop iterates
- **Then** the acquired permit is released and the loop awaits `Task.Delay(1s)` before the next `DequeueAsync`

#### Scenario: A non-positive concurrency is rejected at construction

- **Given** `JobQueueOptions { MaxConcurrency = 0 }`
- **When** `new BackgroundJobProcessor(queue, executor, options)` is evaluated
- **Then** `SemaphoreSlim`'s constructor throws `ArgumentOutOfRangeException`

#### Scenario: Null dependencies are rejected

- **Given** a null `IJobQueue` or null `IJobExecutor`
- **When** the processor is constructed
- **Then** `ArgumentNullException` is thrown; a null `options` is replaced with `new JobQueueOptions()`

### Requirement: Processor outcome reporting, timeout and shutdown

The system SHALL, for each claimed job, execute it under a linked `CancellationTokenSource` cancelled
after `JobQueueOptions.JobTimeout` (default 30 minutes), call `CompleteAsync` when
`JobResult.Success` is true and `FailAsync(id, result.Error ?? "Unknown error")` otherwise, and SHALL
treat a shutdown cancellation as a **failed attempt** recorded via
`FailAsync(id, "Job cancelled due to processor shutdown", CancellationToken.None)` — a path reached
only when one of those queue calls itself throws on the cancelled token, since `JobExecutor` never
rethrows cancellation.

#### Scenario: A successful job is completed

- **Given** an executor returning `JobResult.Succeeded`
- **When** the processor handles the job
- **Then** `CompleteAsync(descriptor.Guid, ct)` is called

#### Scenario: A failed job is reported with its error text

- **Given** an executor returning `JobResult.Failed(elapsed, "bad input")`
- **When** the processor handles the job
- **Then** `FailAsync(descriptor.Guid, "bad input", ct)` is called; a failure with a null `Error` reports `"Unknown error"`

#### Scenario: A job exceeding JobTimeout is failed

- **Given** `JobQueueOptions { JobTimeout = TimeSpan.FromSeconds(5) }` and a job that ignores nothing but runs 10 seconds
- **When** the timeout fires
- **Then** the job's token is cancelled, `JobExecutor` converts the resulting `OperationCanceledException` into `JobResult.Failed`, and the processor calls `FailAsync` — consuming a retry

#### Scenario: Shutdown consumes a retry and can kill a last-attempt job

- **Given** a store-backed queue, a job on its final permitted attempt, and a processor whose outer `cancellationToken` is cancelled mid-execution
- **When** `JobExecutor` swallows the `OperationCanceledException` and returns `JobResult.Failed`, and the reporting `FailAsync(id, result.Error, cancellationToken)` then throws on the cancelled token (every store-backed queue funnels through `EnsureInitializedAsync`, which calls `ct.ThrowIfCancellationRequested()`), so the shutdown catch runs its own `FailAsync`
- **Then** the job is marked `Dead`; there is no requeue-without-consuming-an-attempt path (`IJobQueue` has no `RequeueAsync`, and `EnqueueAsync` is an insert that would conflict on the existing id)

#### Scenario: A job that succeeded is recorded as a failed attempt when the processor stops mid-completion

- **Given** a store-backed queue and a job whose body has just returned `JobResult.Succeeded`
- **When** `Stop()` fires so that `CompleteAsync(descriptor.Guid, cancellationToken)` throws `OperationCanceledException` before writing anything
- **Then** the shutdown catch calls `FailAsync(descriptor.Guid, "Job cancelled due to processor shutdown", CancellationToken.None)`, so a job that succeeded is rescheduled for a re-run — or marked `Dead` when it was on its last attempt; an `InMemoryJobQueue`, which ignores the token, completes normally and never enters that path

#### Scenario: Stop cancels the loop and in-flight work is awaited

- **Given** a running processor
- **When** `Stop()` is called
- **Then** the linked `CancellationTokenSource` is cancelled and the `finally` block awaits `Task.WhenAll` over the recorded job tasks; `RunAsync` returns normally only when the cancellation is observed by `DequeueAsync` or by the polling `Task.Delay`, which each catch `OperationCanceledException` and `break` — when it is observed by `await _concurrencySemaphore.WaitAsync(_cts.Token)`, which sits outside every `catch` (the normal state at `MaxConcurrency`), that exception propagates out of `RunAsync` after the in-flight wait and faults the caller's task

#### Scenario: A queue-reporting failure escapes the job task

- **Given** an `IJobQueue` whose `FailAsync` itself throws
- **When** `ProcessJobAsync` reaches its `catch (Exception)` handler and that `FailAsync` throws again
- **Then** the exception faults the job task; if the loop's `tasks.RemoveAll(t => t.IsCompleted)` has already dropped the handle, the exception is never observed

### Requirement: Fluent enqueue API

The system SHALL expose a `JobDispatcher` that builds descriptors from generic type parameters —
using `typeof(TJob).AssemblyQualifiedName` as `JobType` — for immediate enqueue, delayed schedule,
named-queue enqueue and prioritised enqueue, and SHALL forward cancellation and status queries to
the underlying queue.

#### Scenario: Immediate parameterless enqueue

- **Given** `TJob : IJob`
- **When** `EnqueueAsync<TJob>()` is called
- **Then** a descriptor with only `JobType` set (status `Pending`, no delay) is enqueued and its id returned

#### Scenario: Immediate typed enqueue

- **Given** `TJob : IJob<TInput>` and an input instance
- **When** `EnqueueAsync<TJob, TInput>(input)` is called
- **Then** the descriptor carries `JobType`, `InputType = typeof(TInput).AssemblyQualifiedName` and `SerializedInput` from the dispatcher's serializer

#### Scenario: Delayed schedule

- **Given** a dispatcher on a clock reading `t0`
- **When** `ScheduleAsync<TJob>(TimeSpan.FromMinutes(10))` is called
- **Then** the descriptor has `Delay = 10min`, `ScheduledAt = t0 + 10min` and `Status = Scheduled`

#### Scenario: Named queue and priority

- **Given** a dispatcher
- **When** `EnqueueOnAsync<TJob>("reports")` and `EnqueueWithPriorityAsync<TJob>(9)` are called
- **Then** the first descriptor sets `QueueName = "reports"` and the second sets `Priority = 9`; neither sets both

#### Scenario: Status query on an unknown job

- **Given** an id with no stored job
- **When** `GetStatusAsync(id)` is called
- **Then** `null` is returned, because it projects `descriptor?.Status`

### Requirement: Recurring job scheduling

The system SHALL let a caller register named recurring jobs with a fixed interval via
`RecurringJobScheduler.Register<TJob>(name, interval, queueName)`, set the first run to
`clock.UtcNow + interval`, and — in `RunAsync` — once per second enqueue every definition whose
`NextRunAt` has passed, tagging each enqueued descriptor with `Metadata["recurring.name"]` and
advancing `NextRunAt` to the loop's observed `now + interval`.

#### Scenario: A registered job first fires one interval later

- **Given** `Register<CleanupJob>("cleanup", TimeSpan.FromMinutes(5))` at `t0`
- **When** the scheduler loop runs
- **Then** nothing is enqueued until the clock reaches `t0 + 5min`; registration does not fire an immediate run

#### Scenario: The enqueued descriptor is tagged with its schedule name

- **Given** a due definition named `"cleanup"`
- **When** the loop enqueues it
- **Then** the descriptor's `JobType` is the registered type name, its `QueueName` is the registered queue, and `Metadata["recurring.name"] == "cleanup"`

#### Scenario: Re-registering the same name replaces the definition

- **Given** `"cleanup"` already registered with a 5-minute interval
- **When** `Register<OtherJob>("cleanup", TimeSpan.FromHours(1))` is called
- **Then** the existing definition is replaced via `AddOrUpdate`, resetting `NextRunAt` to `now + 1h`

#### Scenario: Removing an unknown name returns false

- **Given** no definition named `"missing"`
- **When** `Remove("missing")` is called
- **Then** `false` is returned

#### Scenario: Sub-second intervals cannot fire faster than the tick

- **Given** a definition registered with `interval = TimeSpan.FromMilliseconds(100)`
- **When** the loop runs
- **Then** it enqueues at most once per iteration and each iteration sleeps a hard-coded 1 second, so the job fires roughly once per second, not ten times

#### Scenario: A missed window produces one run, not a catch-up burst

- **Given** a definition with a 1-minute interval whose `NextRunAt` was 10 minutes ago
- **When** the loop next observes it
- **Then** exactly one descriptor is enqueued and `NextRunAt` is set to the observed `now + 1min`; the nine skipped occurrences are dropped

#### Scenario: An enqueue failure terminates the scheduler

- **Given** an `IJobQueue` whose `EnqueueAsync` throws
- **When** a definition becomes due
- **Then** the exception propagates out of `RunAsync` (after the `finally` releases any held leadership lock) and the loop stops; the enqueue is not wrapped in any `catch` — only the leadership check and `Task.Delay` catch `OperationCanceledException`

#### Scenario: Cancellation exits the loop cleanly

- **Given** a running scheduler
- **When** the cancellation token is triggered during the 1-second delay
- **Then** the `OperationCanceledException` is caught and the loop breaks

### Requirement: SQL advisory-lock provider

The system SHALL provide `SqlJobLockProvider<DB>` which acquires a named advisory lock on a
dedicated connection, choosing the statement from the connector type *name* —
`pg_try_advisory_lock` for PostgreSQL, `sp_getapplock` (session-owned, exclusive) for MSSql,
`GET_LOCK` for MySQL — and SHALL return `false` without opening a connection for any other
connector.

#### Scenario: SQLite cannot take a cross-connection lock

- **Given** `SqlJobLockProvider<SqLiteConnector>`
- **When** `TryAcquireAsync("jobs", TimeSpan.FromSeconds(5))` is called
- **Then** `false` is returned and no connection is opened, because the dialect resolves to `Other`

#### Scenario: PostgreSQL uses a hashed 64-bit lock key

- **Given** `SqlJobLockProvider<PostgreSQLConnector>` and lock name `"jobs"`
- **When** the lock is taken
- **Then** the statement is `SELECT pg_try_advisory_lock(<djb2("jobs") & 0x7FFFFFFFFFFFFFFF>)`, and two distinct lock names that collide under that hash share one lock

#### Scenario: The PostgreSQL path ignores the timeout

- **Given** a PostgreSQL provider called with `acquireTimeout = TimeSpan.FromMinutes(1)`
- **When** another session already holds the lock
- **Then** `pg_try_advisory_lock` returns immediately with `false` — the timeout only shapes `cmd.CommandTimeout`, it does not make the acquisition wait

#### Scenario: MSSql grant is read from the return value

- **Given** an MSSql provider
- **When** `sp_getapplock` is executed via `DECLARE @r int; EXEC @r = sp_getapplock …; SELECT @r;`
- **Then** the lock is considered held when the scalar is non-null, not `DBNull`, and `>= 0`

#### Scenario: MySQL grant requires exactly 1

- **Given** a MySQL provider
- **When** `SELECT GET_LOCK(@res, @to)` returns `0` (timeout) or `NULL` (error)
- **Then** `IsLocked` stays `false` and the dedicated connection is closed and disposed

#### Scenario: A failure never leaks the connection

- **Given** a provider whose `OpenAsync` or lock statement throws
- **When** `TryAcquireAsync` runs
- **Then** `IsLocked` is set to `false`, the connection is closed, disposed and nulled, and the exception is rethrown

#### Scenario: Requesting a second lock name while holding one succeeds without locking

- **Given** a provider that already holds `"queue-a"` (`IsLocked == true`)
- **When** `TryAcquireAsync("queue-b", timeout)` is called
- **Then** `true` is returned immediately — the early-out does not compare the requested name against the held one, and no lock on `"queue-b"` is taken

#### Scenario: Release is a no-op when nothing is held

- **Given** `IsLocked == false` or a null lock connection
- **When** `ReleaseAsync(lockName)` is called
- **Then** it returns without issuing a statement; a `DbException` during a real release is swallowed (the lock ends when the connection closes) and the connection is disposed regardless

#### Scenario: A SQL advisory lock refuses a lease duration

- **Given** `SqlJobLockProvider<DB>`, whose `IsLeaseBased` is `false`
- **When** `TryAcquireAsync` is called with a non-null `leaseDuration`
- **Then** an `ArgumentException` is thrown stating that SQL advisory locks are session-scoped and cannot expire, before any connection is opened

### Requirement: Redis distributed-lock provider

The system SHALL provide `RedisJobLockProvider`, whose `IsLeaseBased` is `true`, which takes a lock as
`SET {prefix}:lock:{name} {token} NX` with an expiry equal to the lease — the caller's `leaseDuration`,
or `DefaultLeaseDuration` (30 seconds) when it is null, with a non-positive lease rejected by
`ArgumentException` — retrying after a backoff that starts at 50 ms and grows by 50 ms up to 500 ms until
`acquireTimeout` has elapsed. While the lock is held the system SHALL renew the lease every half lease
with a script that re-applies the expiry only while the key still holds the caller's token, and SHALL
clear `IsLocked` when a renewal finds the key gone or holding another token. The system SHALL release the
lock only when the stored value still equals the caller's token, via a check-and-delete Lua script shared
by `ReleaseAsync`, `DisposeAsync` and `Dispose`.

#### Scenario: The lock is taken only when the key is absent

- **Given** no existing `birko:jobs:lock:jobs` key
- **When** `TryAcquireAsync("jobs", TimeSpan.Zero, TimeSpan.FromMinutes(1))` is called
- **Then** `StringSetAsync(..., When.NotExists)` succeeds, `IsLocked` becomes `true`, the key carries a fresh random token that expires after one minute, and a renewal loop starts

#### Scenario: A null lease uses the provider default

- **Given** no existing lock key
- **When** `TryAcquireAsync("jobs", TimeSpan.Zero)` is called with no `leaseDuration`
- **Then** the key is set with the 30-second `DefaultLeaseDuration` rather than without an expiry

#### Scenario: A held lock blocks a second holder

- **Given** the key already exists
- **When** another provider calls `TryAcquireAsync`
- **Then** it keeps retrying with backoff until `acquireTimeout` elapses, then returns `false`, `IsLocked` stays `false`, and its key and token are cleared

#### Scenario: The lease is renewed while the holder works

- **Given** a provider holding a lock with a 30-second lease
- **When** 15 seconds pass and the key still holds its token
- **Then** the renewal script re-applies a 30-second expiry, so the lock outlives its original lease for as long as the holder is alive; a renewal that throws is swallowed and retried on the next tick

#### Scenario: A lost lease clears IsLocked

- **Given** a provider whose key expired or now holds another provider's token
- **When** the next renewal runs
- **Then** the script returns `0`, the provider sets `IsLocked = false` and stops renewing — a subsequent `ReleaseAsync` then returns at its `!IsLocked` guard without running the release script

#### Scenario: Release never deletes another holder's lock

- **Given** provider A's lock expired and provider B re-acquired the same key with a new token, before A's renewal noticed
- **When** provider A calls `ReleaseAsync`
- **Then** the script's `GET == ARGV[1]` comparison fails, `DEL` is not issued, and B keeps its lock; A still clears its own `IsLocked`, `_lockKey` and `_lockToken`

#### Scenario: Dispose releases best-effort

- **Given** a provider holding a lock
- **When** `Dispose()` or `DisposeAsync()` runs
- **Then** the safe-release script is attempted inside a `try`/`catch` that swallows failures, `IsLocked` is cleared, and the connection manager is disposed only when the provider created it (`_ownsConnection`)

#### Scenario: Requesting a second lock name while holding one is refused

- **Given** a provider with `IsLocked == true` for `"queue-a"`
- **When** `TryAcquireAsync("queue-b", timeout)` is called
- **Then** `InvalidOperationException` is thrown; a provider holds one lock name (TASK-158, SH-M006 — it returned `true` without locking `"queue-b"`)

### Requirement: Explicit schema helpers are optional and separate from the runtime path

The system SHALL expose a per-backend `*JobQueueSchema` static utility with `EnsureCreatedAsync`
(constructing a store, applying settings and calling `InitAsync`) and `DropAsync` (calling
`DestroyAsync`), and SHALL NOT call `EnsureCreatedAsync` from any queue implementation — storage is
otherwise created by the underlying store's lazy initialization on first CRUD.

#### Scenario: A queue works without ever calling the schema helper

- **Given** a `SqlJobQueue<DB>` on a database with no `__BackgroundJobs` table
- **When** `EnqueueAsync` is called first
- **Then** the store's lazy `InitAsync` creates the table; `SqlJobQueueSchema.EnsureCreatedAsync` was never invoked by the queue

#### Scenario: Drop destroys all job data

- **Given** `ElasticSearchJobQueueSchema.DropAsync(settings)` (or the SQL / MongoDB / RavenDB / Cosmos DB / JSON / XML equivalent)
- **When** it is awaited
- **Then** `DestroyAsync` removes the backing index, table, collection, container or file together with every stored job

#### Scenario: Redis has no schema helper

- **Given** the Redis backend
- **When** the shipped file set is inspected
- **Then** there is no `RedisJobQueueSchema` — keys are created on demand by `EnqueueAsync` — and instead a `RedisJobLockProvider` ships alongside the queue

### Requirement: The hosting bridges register once, reach the running scheduler, and run on every node

The system SHALL provide `AddBackgroundJobs(configure)` (Birko.BackgroundJobs.Hosting, TASK-538), registering the clock,
an in-memory queue, an executor that resolves each job from its own DI scope (`ScopedJobExecutor`), the dispatcher, the
processor, the recurring scheduler (with the registered `IJobLockProvider`, `RecurringLockName` and
`LeadershipRetryInterval`), and two hosted services. A service registered before the call is kept. A second call
SHALL add nothing: the guard keys on `BackgroundJobProcessor`, never on `IJobQueue` (Symbio TASK-383), because a host
registers its durable queue first. `AddRecurringJob<TJob>(name, interval, queueName)` SHALL register the job and a
`RecurringJobRegistration`, applied to the running scheduler when the hosted service starts, whatever the call order
(Symbio TASK-457). The scheduler's hosted service SHALL always start the loop, follower or not; leadership belongs to
the scheduler. `StopAsync` SHALL stop the loop and wait for it only until the host's shutdown token fires, disposing its
token source only after the loop ends.

#### Scenario: Durable queue registered first

- **Given** an `IJobQueue` registered before two `AddBackgroundJobs` calls
- **When** the provider is built
- **Then** that queue is used, and there is one processor and two hosted services

#### Scenario: A follower takes over

- **Given** a lock provider that refuses the lock, then grants it
- **When** the hosted services have started
- **Then** nothing is enqueued while the node follows, and the recurring job runs once it leads

#### Scenario: A job that ignores cancellation at shutdown

- **Given** a running job that ignores its token, and a shutdown token that fires after 300 ms
- **When** the hosted services stop
- **Then** `StopAsync` returns within the shutdown timeout instead of waiting for the job

### Requirement: Queue transitions are guarded, loops survive their failures, and a lock provider holds one name

The system SHALL (TASK-158):
- complete or fail only a job that is Processing (a no-op otherwise, on every backend). On SQL, MongoDB, Elasticsearch
  and RavenDB each transition is one guarded native update of the fields it changes: Fail also requires the attempt
  count it read, and Cancel requires Pending | Scheduled and returns `true` only when its own stamp reads back.
- claim a job only if its Status and its AttemptCount are still those the worker read (SH-M012: Status alone is ABA).
- write a job's outcome with `CancellationToken.None`, raise `JobOutcomeNotRecorded` when the write fails, and stop
  cleanly when stopped while waiting for a slot.
- keep the recurring scheduler running when an enqueue fails, raising `EnqueueFailed` and retrying on the next tick.
- make every queue on one JSON or XML file in a process share one store and one gate.
- have a lock provider hold one lock name. Re-acquiring it is idempotent, another name throws, and releasing a name it
  does not hold keeps its lock. The SQL provider unlocks before closing its connection on dispose. The Redis provider's
  `IsLocked` ends at a local deadline (the last successful renewal plus the lease).

#### Scenario: A stale claim after a reschedule

- **Given** worker B read a due Scheduled job, and worker A then claimed, ran and failed it
- **When** B's claim is written
- **Then** it matches nothing (the attempt count moved on), and the job stays Scheduled inside its backoff

#### Scenario: Dispose on a pooled MySQL connection

- **Given** a provider holding `GET_LOCK` that is disposed
- **When** a provider on another connection pool asks for the same lock
- **Then** it gets it (an idle pooled session used to keep the lock)

