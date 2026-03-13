# Birko.BackgroundJobs.Redis

## Overview

Redis-based persistent job queue implementing `IJobQueue` from Birko.BackgroundJobs. Uses StackExchange.Redis with Redis hashes for job storage, sorted sets for priority queuing, and Lua scripts for atomic operations.

## Structure

```
Birko.BackgroundJobs.Redis/
├── RedisJobQueue.cs            - IJobQueue using Redis hashes + sorted sets (enqueue, dequeue, complete, fail, cancel, purge)
└── RedisJobLockProvider.cs     - Distributed lock using SET NX + Lua safe release
```

## Dependencies

- Birko.BackgroundJobs (IJobQueue, JobDescriptor, JobStatus, RetryPolicy)
- Birko.Redis (RedisSettings, RedisConnectionManager)
- StackExchange.Redis (NuGet — added by consuming project)

## Key Design Decisions

- **Uses shared Birko.Redis** — `RedisSettings` and `RedisConnectionManager` from `Birko.Redis` (no local duplicates)
- **KeyPrefix from settings** — Uses `RedisSettings.KeyPrefix` with fallback to `"birko:jobs"` default
- **No generic type parameter** — Unlike `SqlJobQueue<DB>`, Redis has a single client library, so no generic connector type needed
- **Hash-based storage** — Each job is a Redis hash (`{prefix}:job:{id}`) with all descriptor fields as hash entries
- **Sorted set queuing** — Jobs ordered by `-(priority * 1e13) + (scheduledAt.Ticks / 1e4)` — priority dominates, FIFO within same priority
- **Atomic dequeue via Lua** — `ZRANGEBYSCORE` + `ZREM` in a single Lua script prevents race conditions between workers
- **Status tracking via sets** — Redis sets (`{prefix}:status:{int}`) index jobs by status for fast `GetByStatusAsync`
- **DateTime as Ticks** — All DateTime values stored as `long` ticks in hash fields for precision
- **Metadata as JSON** — `Dictionary<string, string>` serialized to a single hash field
- **Connection ownership** — Constructor overloads: own connection (disposed with queue) or shared connection (caller manages lifetime)
- **Safe lock release** — Lua script checks lock token before DEL to avoid releasing another worker's lock

## Maintenance

### README Updates
When changing the Redis key schema, adding new operations, or changing connection patterns, update README.md.

### CLAUDE.md Updates
When adding/removing files or changing architecture, update the structure tree and design decisions.

### Test Requirements
Tests should be created in `Birko.BackgroundJobs.Redis.Tests` covering:
- Enqueue/dequeue lifecycle
- Atomic dequeue (multiple workers)
- Retry and dead letter transitions
- Distributed locking (acquire/release)
- Purge operations
- Priority ordering in dequeue
- Key prefix isolation
