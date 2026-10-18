# Background Jobs Guide

## Overview

Birko.BackgroundJobs provides a background job processing framework with pluggable persistent queues. Jobs can be enqueued for immediate processing, scheduled for future execution, or registered as recurring tasks.

## Core Concepts

### Job Interfaces

```csharp
// Parameterless job
public interface IJob
{
    Task ExecuteAsync(JobContext context, CancellationToken ct = default);
}

// Typed job with input
public interface IJob<TInput>
{
    Task ExecuteAsync(TInput input, JobContext context, CancellationToken ct = default);
}
```

### JobContext

Runtime context available during execution:

```csharp
public class JobContext
{
    public Guid JobGuid { get; }
    public int AttemptNumber { get; }
    public DateTime EnqueuedAt { get; }
    public IDictionary<string, string> Metadata { get; }
}
```

### JobDescriptor

Full persistence model tracked in the queue:

```csharp
public class JobDescriptor
{
    public Guid Guid { get; set; }
    public string JobType { get; set; }          // Assembly-qualified type name
    public string? SerializedInput { get; set; } // JSON-serialized input
    public string? InputType { get; set; }
    public string? QueueName { get; set; }
    public int Priority { get; set; }            // Higher is dequeued first
    public int? MaxRetries { get; set; } = 3;    // null = the queue's RetryPolicy, 0 = no retry
    public TimeSpan? Delay { get; set; }
    public JobStatus Status { get; set; }
    public int AttemptCount { get; set; }
    public DateTime EnqueuedAt { get; set; }
    public DateTime? ScheduledAt { get; set; }
    public DateTime? LastAttemptAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? LastError { get; set; }
    public Dictionary<string, string> Metadata { get; set; }
}
```

### Job Lifecycle

```
Pending -> Scheduled -> Processing -> Completed
                                   -> Failed -> (retry) -> Processing
                                             -> Dead (max retries exceeded)
                                   -> Cancelled
```

A job whose worker dies stays `Processing`. Every queue offers it again once it has been held longer than the queue's
`claimTimeout` (a constructor argument, default 30 minutes, the same as `JobQueueOptions.JobTimeout`), and buries it as
`Dead` when its attempts are spent. If you raise `JobTimeout`, raise the queue's `claimTimeout` too, or a slow job is
handed to a second worker. Jobs should be idempotent for that reason.

## Defining Jobs

```csharp
// Simple job
public class SendEmailJob : IJob
{
    public async Task ExecuteAsync(JobContext context, CancellationToken ct)
    {
        var to = context.Metadata["to"];
        await emailService.SendAsync(to, ct);
    }
}

// Typed job with input
public class GenerateReportJob : IJob<ReportRequest>
{
    public async Task ExecuteAsync(ReportRequest input, JobContext context, CancellationToken ct)
    {
        var report = await reportService.GenerateAsync(input.ReportType, input.DateRange, ct);
        await storageService.SaveAsync($"reports/{context.JobGuid}.pdf", report, ct);
    }
}
```

## Dispatching Jobs

```csharp
var queue = new InMemoryJobQueue();  // Or any persistent queue
var serializer = new JsonJobSerializer();
var dispatcher = new JobDispatcher(queue, serializer);

// Enqueue for immediate processing
await dispatcher.EnqueueAsync<SendEmailJob>(
    metadata: new Dictionary<string, string> { ["to"] = "user@example.com" });

// Enqueue typed job
await dispatcher.EnqueueAsync<GenerateReportJob, ReportRequest>(
    new ReportRequest { ReportType = "Monthly", DateRange = "2026-03" });

// Schedule for future execution
await dispatcher.ScheduleAsync<CleanupJob>(
    scheduledAt: DateTime.UtcNow.AddHours(2));

// Cancel a job
await dispatcher.CancelAsync(jobId);
```

## Processing Jobs

```csharp
var options = new JobQueueOptions
{
    MaxConcurrency = 4,
    PollingInterval = TimeSpan.FromSeconds(5),
    JobTimeout = TimeSpan.FromMinutes(30),
    RetentionPeriod = TimeSpan.FromDays(7),
    PurgeInterval = TimeSpan.FromHours(1) // opt-in: null (the default) purges nothing
};

var executor = new JobExecutor(type => serviceProvider.GetRequiredService(type));
var processor = new BackgroundJobProcessor(queue, executor, options);

// Run until cancelled (or processor.Stop()); in-flight jobs are awaited before RunAsync returns
var cts = new CancellationTokenSource();
var run = processor.RunAsync(cts.Token);

cts.Cancel();
await run;
```

In a .NET host use **Birko.BackgroundJobs.Hosting** instead (below), which also resolves each job from its own DI scope.

`BackgroundJobProcessor` uses semaphore-based concurrency control and polls the queue at the configured interval.

## Recurring Jobs

```csharp
var scheduler = new RecurringJobScheduler(queue, clock, lockProvider: null);

scheduler.Register<CleanupJob>("cleanup", TimeSpan.FromHours(1));
scheduler.Register<HealthCheckJob>("healthcheck", TimeSpan.FromMinutes(5));

await scheduler.RunAsync(cts.Token);   // with an IJobLockProvider, only the leader enqueues; followers keep re-trying
```

## Hosting (Birko.BackgroundJobs.Hosting)

`AddBackgroundJobs` registers the queue (in-memory unless one is already registered), the clock, an executor that
resolves each job from its own DI scope, the dispatcher, the processor and the scheduler, plus the two hosted services
that run them (TASK-538). It is idempotent, keyed on the processor, so a host can register a durable queue first and
several modules can call it. `AddRecurringJob<TJob>` declares a schedule that is applied to the running scheduler when
the host starts, in any order relative to `AddBackgroundJobs`.

```csharp
builder.Services.AddSingleton<IJobQueue>(durableQueue);          // optional, before or after
builder.Services.AddSingleton<IJobLockProvider>(lockProvider);    // for more than one node
builder.Services.AddBackgroundJobs(o =>
{
    o.Queue = new JobQueueOptions { MaxConcurrency = 4 };
    o.RecurringLockName = "myapp:recurring";                        // the same on every node, in every version
});
builder.Services.AddRecurringJob<CleanupJob>("cleanup", TimeSpan.FromHours(1));
```

The scheduler's loop runs on every node; a follower enqueues nothing and takes over when the lock frees. On shutdown
the processor stops and running jobs get their cancellation; the host waits for them up to its shutdown timeout.

## Retry Policy

```csharp
var policy = new RetryPolicy
{
    MaxRetries = 5,
    BaseDelay = TimeSpan.FromSeconds(10),
    MaxDelay = TimeSpan.FromHours(1),
    UseExponentialBackoff = true  // 10s, 20s, 40s, 80s
};
```

A job gets `policy.MaxRetriesFor(job.MaxRetries)` attempts: its own `MaxRetries` when set, otherwise the policy's.
Set `MaxRetries = null` on a job to defer to the queue's policy; `0` means no retry (dead on the first failure). A SQL
`__BackgroundJobs` table created before this was nullable keeps a `NOT NULL` column, which `DetectDrift` reports; make
it nullable (`ALTER TABLE … ALTER COLUMN MaxRetries INT NULL` on SQL Server, `… MODIFY MaxRetries INT NULL` on MySQL,
`… ALTER COLUMN "MaxRetries" DROP NOT NULL` on PostgreSQL, a table rebuild on SQLite) before enqueueing a null budget.

## Queue Backends

### In-Memory (Testing)

```csharp
var queue = new InMemoryJobQueue();
```

Non-persistent, ConcurrentDictionary-based. For unit tests and development only.

### SQL (Any Connector)

```csharp
var queue = new SqlJobQueue<PostgreSQLConnector>();
queue.SetSettings(new RemoteSettings("localhost", "jobs_db", "admin", "secret", 5432));

// Create table if needed
await SqlJobQueueSchema.EnsureCreatedAsync(queue);
```

Works with any SQL connector (PostgreSQL, MSSql, MySQL, SQLite).

### Elasticsearch

```csharp
var queue = new ElasticSearchJobQueue();
queue.SetSettings(new RemoteSettings("localhost", "jobs", "", "", 9200));
await ElasticSearchJobQueueSchema.EnsureCreatedAsync(queue);
```

### MongoDB

```csharp
var queue = new MongoDBJobQueue();
queue.SetSettings(new RemoteSettings("localhost", "jobs_db", "admin", "secret", 27017));
await MongoDBJobQueueSchema.EnsureCreatedAsync(queue);
```

### RavenDB

```csharp
var queue = new RavenDBJobQueue();
queue.SetSettings(new RemoteSettings("localhost", "JobsDB", "", "", 8080));
await RavenDBJobQueueSchema.EnsureCreatedAsync(queue);
```

### JSON (Development)

```csharp
var queue = new JsonJobQueue();
queue.SetSettings(new Settings("./data", "jobs"));
await JsonJobQueueSchema.EnsureCreatedAsync(queue);
```

Stores jobs as JSON files on disk. No external database required.

### Redis

Uses `RedisSettings` from `Birko.Redis` (extends the framework's `RemoteSettings` hierarchy):

```csharp
var settings = new RedisSettings
{
    Location = "localhost",
    Port = 6379,
    KeyPrefix = "myapp:jobs",
    Database = 0
};

var queue = new RedisJobQueue(settings);
```

High-performance persistent queue using Redis hashes for job storage and sorted sets for priority-based ordering. Atomic dequeue via Lua scripts prevents race conditions between workers.

**Features:**
- Atomic dequeue (Lua script: `ZRANGEBYSCORE` + `ZREM`)
- Priority ordering (higher priority first, FIFO within same priority)
- Status tracking via Redis sets for fast lookup
- Key prefix isolation for multi-app Redis instances
- No schema creation needed (Redis is schemaless)

**Shared connection:**

```csharp
// Share a connection manager across queue and lock provider
var connectionManager = new RedisConnectionManager(settings);
var queue = new RedisJobQueue(connectionManager, settings);
var lockProvider = new RedisJobLockProvider(connectionManager, settings);
```

**Distributed locking:**

```csharp
await using var lockProvider = new RedisJobLockProvider(settings);

if (await lockProvider.TryAcquireAsync("my-queue", TimeSpan.FromSeconds(30)))
{
    await processor.StartAsync(cancellationToken);
    await lockProvider.ReleaseAsync("my-queue");
}
```

Uses SET NX with expiry and Lua-based safe release (checks lock token before DEL).

**Redis key schema:**

| Key Pattern | Type | Description |
|-------------|------|-------------|
| `{prefix}:job:{id}` | Hash | Job descriptor fields |
| `{prefix}:queue:{name}` | Sorted Set | Pending/scheduled jobs ordered by priority + time |
| `{prefix}:status:{int}` | Set | Job IDs grouped by status |
| `{prefix}:lock:{name}` | String | Distributed lock with TTL |

## Queue Backend Comparison

| Backend | Persistence | Distributed | ACID | Best For |
|---------|------------|-------------|------|----------|
| **InMemory** | No | No | N/A | Unit tests, development |
| **SQL** | Yes | Yes (advisory locks) | Yes | Production, existing SQL infrastructure |
| **Elasticsearch** | Yes | Yes (bulk API) | No | Search-heavy workloads |
| **MongoDB** | Yes | Yes (sessions) | Yes (4.0+) | Document-oriented workloads |
| **RavenDB** | Yes | Yes (built-in) | Yes | RavenDB-based applications |
| **JSON** | Yes (files) | No | No | Development, small deployments |
| **Redis** | Yes | Yes (Redlock) | No | High-throughput, low-latency |
| **CosmosDB** | Yes | Yes (global; claims by ETag) | Yes (batch) | Azure cloud-native workloads |

## See Also

- [Birko.BackgroundJobs](https://github.com/Birko-Framework/Birko.Framework/tree/main/Birko.BackgroundJobs)
- [Birko.BackgroundJobs.SQL](https://github.com/Birko-Framework/Birko.Framework/tree/main/Birko.BackgroundJobs.SQL)
- [Birko.BackgroundJobs.Redis](https://github.com/Birko-Framework/Birko.Framework/tree/main/Birko.BackgroundJobs.Redis)
- [Birko.Redis](https://github.com/Birko-Framework/Birko.Framework/tree/main/Birko.Redis)
