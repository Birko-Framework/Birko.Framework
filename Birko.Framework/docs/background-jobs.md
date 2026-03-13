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
    public Guid JobId { get; }
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
    public Guid Id { get; }
    public string JobType { get; }          // Assembly-qualified type name
    public string? SerializedInput { get; } // JSON-serialized input
    public JobStatus Status { get; }
    public int RetryCount { get; }
    public int MaxRetries { get; }
    public JobPriority Priority { get; }
    public DateTime? ScheduledAt { get; }
    public DateTime? StartedAt { get; }
    public DateTime? CompletedAt { get; }
    public string? Error { get; }
    public IDictionary<string, string> Metadata { get; }
}
```

### Job Lifecycle

```
Pending -> Scheduled -> Processing -> Completed
                                   -> Failed -> (retry) -> Processing
                                             -> Dead (max retries exceeded)
                                   -> Cancelled
```

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
        await storageService.SaveAsync($"reports/{context.JobId}.pdf", report, ct);
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
    DefaultTimeout = TimeSpan.FromMinutes(30),
    RetentionPeriod = TimeSpan.FromDays(7)
};

var executor = new JobExecutor(type => serviceProvider.GetRequiredService(type));
var processor = new BackgroundJobProcessor(queue, executor, serializer, options);

// Start processing (runs until cancelled)
var cts = new CancellationTokenSource();
await processor.StartAsync(cts.Token);

// Stop gracefully
cts.Cancel();
```

`BackgroundJobProcessor` uses semaphore-based concurrency control and polls the queue at the configured interval.

## Recurring Jobs

```csharp
var scheduler = new RecurringJobScheduler(dispatcher);

// Register recurring jobs
scheduler.Register<CleanupJob>("cleanup", TimeSpan.FromHours(1));
scheduler.Register<HealthCheckJob>("healthcheck", TimeSpan.FromMinutes(5));

// Start scheduler
await scheduler.StartAsync(cts.Token);
```

## Retry Policy

```csharp
var policy = new RetryPolicy
{
    MaxRetries = 5,
    InitialDelay = TimeSpan.FromSeconds(10),
    BackoffMultiplier = 2.0  // Exponential: 10s, 20s, 40s, 80s, 160s
};
```

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

## See Also

- [Birko.BackgroundJobs CLAUDE.md](../Birko.BackgroundJobs/CLAUDE.md)
- [Birko.BackgroundJobs.SQL CLAUDE.md](../Birko.BackgroundJobs.SQL/CLAUDE.md)
