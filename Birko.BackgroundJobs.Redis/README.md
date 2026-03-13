# Birko.BackgroundJobs.Redis

Redis-based persistent job queue for the Birko Background Jobs framework. Uses StackExchange.Redis for high-performance, distributed job processing.

## Features

- **Persistent storage** — Jobs survive process restarts, stored as Redis hashes
- **Atomic dequeue** — Lua scripts ensure no two workers grab the same job
- **Priority ordering** — Higher priority jobs dequeued first, FIFO within same priority
- **Distributed locking** — `RedisJobLockProvider` using SET NX (Redlock single-instance pattern)
- **Retry with backoff** — Failed jobs re-scheduled with configurable delay
- **Status tracking** — Jobs indexed by status using Redis sets for fast lookup
- **Key prefix isolation** — Multiple apps can share one Redis instance
- **Connection sharing** — Reuse `RedisConnectionManager` across queue and lock provider

## Dependencies

- Birko.BackgroundJobs (core interfaces)
- Birko.Redis (RedisSettings, RedisConnectionManager)
- StackExchange.Redis (NuGet — added by consuming project)

## Usage

### Basic Setup

```csharp
using Birko.BackgroundJobs;
using Birko.BackgroundJobs.Redis;
using Birko.BackgroundJobs.Processing;
using Birko.Redis;

// Connection settings
var settings = new RedisSettings
{
    Location = "localhost",
    Port = 6379,
    KeyPrefix = "myapp:jobs",
    Database = 0
};

// Create Redis job queue
var queue = new RedisJobQueue(settings);

// Use with dispatcher
var dispatcher = new JobDispatcher(queue);
await dispatcher.EnqueueAsync<MyJob>();

// Use with processor
var executor = new JobExecutor(type => serviceProvider.GetRequiredService(type));
var processor = new BackgroundJobProcessor(queue, executor);
await processor.RunAsync(cancellationToken);
```

### Shared Connection

```csharp
// Share a connection manager across queue and lock provider
var connectionManager = new RedisConnectionManager(settings);

var queue = new RedisJobQueue(connectionManager, settings);
var lockProvider = new RedisJobLockProvider(connectionManager, settings);
```

### Distributed Locking

```csharp
await using var lockProvider = new RedisJobLockProvider(settings);

if (await lockProvider.TryAcquireAsync("my-queue", TimeSpan.FromSeconds(30)))
{
    await processor.RunAsync(cancellationToken);
    await lockProvider.ReleaseAsync("my-queue");
}
```

### Custom Retry Policy

```csharp
var retryPolicy = new RetryPolicy
{
    MaxRetries = 5,
    BaseDelay = TimeSpan.FromSeconds(10),
    MaxDelay = TimeSpan.FromMinutes(30),
    UseExponentialBackoff = true
};

var queue = new RedisJobQueue(settings, retryPolicy);
```

## API Reference

| Type | Description |
|------|-------------|
| `RedisJobQueue` | `IJobQueue` implementation using Redis hashes and sorted sets |
| `RedisJobLockProvider` | Distributed lock using Redis SET NX with Lua-based safe release |

## Redis Key Schema

| Key Pattern | Type | Description |
|-------------|------|-------------|
| `{prefix}:job:{id}` | Hash | Job descriptor fields |
| `{prefix}:queue:{name}` | Sorted Set | Pending/scheduled jobs ordered by priority + time |
| `{prefix}:status:{int}` | Set | Job IDs grouped by status for fast lookup |
| `{prefix}:lock:{name}` | String | Distributed lock with TTL |

## Related Projects

- **Birko.BackgroundJobs** — Core interfaces and in-memory implementation
- **Birko.BackgroundJobs.SQL** — SQL-based persistent job queue
- **Birko.Redis** — Shared Redis settings and connection manager
- **Birko.Caching.Redis** — Redis caching (shares Birko.Redis dependency)

## License

Part of the Birko Framework.
