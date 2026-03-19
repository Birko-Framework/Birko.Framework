# Birko.BackgroundJobs

Core interfaces and in-memory implementation for background job processing in the Birko Framework.

## Features

- **Job interfaces** — `IJob` (parameterless) and `IJob<TInput>` (typed input) for defining background work
- **Job queue** — `IJobQueue` interface for pluggable storage backends (in-memory included)
- **Job dispatcher** — Fluent API for enqueuing, scheduling, and cancelling jobs
- **Background processor** — Concurrent job processor with configurable concurrency, polling, and timeouts
- **Recurring jobs** — Interval-based recurring job scheduler
- **Retry with backoff** — Configurable retry policy with exponential backoff (RetryPolicy from Birko.Contracts, shared across framework; BackgroundJobs defaults to 30s base delay / 1h max via JobQueueOptions)
- **Job serialization** — Pluggable serialization (JSON default) for job inputs
- **Priority queues** — Jobs can be prioritized (higher priority = processed first)
- **Named queues** — Route jobs to specific queues for workload isolation
- **Job lifecycle** — Full status tracking: Pending, Scheduled, Processing, Completed, Failed, Dead, Cancelled

## Dependencies

- .NET 10.0+
- Birko.Contracts (provides RetryPolicy, shared across framework)
- System.Text.Json (built-in)

## Usage

### Define a Job

```csharp
using Birko.BackgroundJobs;

// Simple job (no input)
public class CleanupJob : IJob
{
    public async Task ExecuteAsync(JobContext context, CancellationToken cancellationToken)
    {
        // Perform cleanup work
    }
}

// Job with typed input
public class SendEmailJob : IJob<SendEmailInput>
{
    public async Task ExecuteAsync(SendEmailInput input, JobContext context, CancellationToken cancellationToken)
    {
        // Send email to input.Recipient
    }
}

public class SendEmailInput
{
    public string Recipient { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
}
```

### Enqueue Jobs

```csharp
using Birko.BackgroundJobs;
using Birko.BackgroundJobs.Processing;

var queue = new InMemoryJobQueue();
var dispatcher = new JobDispatcher(queue);

// Enqueue for immediate processing
var jobId = await dispatcher.EnqueueAsync<CleanupJob>();

// Enqueue with input
await dispatcher.EnqueueAsync<SendEmailJob, SendEmailInput>(new SendEmailInput
{
    Recipient = "user@example.com",
    Subject = "Hello",
    Body = "World"
});

// Schedule for later
await dispatcher.ScheduleAsync<CleanupJob>(TimeSpan.FromMinutes(30));

// Enqueue with priority
await dispatcher.EnqueueWithPriorityAsync<CleanupJob>(priority: 10);

// Check status
var status = await dispatcher.GetStatusAsync(jobId);
```

### Run the Processor

```csharp
using Birko.BackgroundJobs;
using Birko.BackgroundJobs.Processing;

var queue = new InMemoryJobQueue();
var executor = new JobExecutor(type => Activator.CreateInstance(type)!);
var options = new JobQueueOptions
{
    MaxConcurrency = 4,
    PollingInterval = TimeSpan.FromSeconds(1),
    JobTimeout = TimeSpan.FromMinutes(30)
};

var processor = new BackgroundJobProcessor(queue, executor, options);

// Run until cancelled
using var cts = new CancellationTokenSource();
await processor.RunAsync(cts.Token);
```

### Recurring Jobs

```csharp
using Birko.BackgroundJobs.Processing;

var scheduler = new RecurringJobScheduler(queue);
scheduler.Register<CleanupJob>("daily-cleanup", TimeSpan.FromHours(24));

// Run scheduler loop
await scheduler.RunAsync(cancellationToken);
```

## API Reference

### Core
| Type | Description |
|------|-------------|
| `IJob` | Parameterless background job interface |
| `IJob<TInput>` | Background job with typed input |
| `IJobQueue` | Job storage and retrieval interface |
| `IJobExecutor` | Job resolution and execution interface |
| `JobDescriptor` | Full job description (type, input, status, metadata) |
| `JobContext` | Runtime context passed to executing jobs |
| `JobResult` | Execution result (success/failure, duration) |
| `JobStatus` | Job lifecycle enum |
| `JobQueueOptions` | Processor configuration (overrides RetryPolicy defaults to 30s/1h) |
| `RetryPolicy` | Retry strategy with exponential backoff (from Birko.Contracts) |

### Processing
| Type | Description |
|------|-------------|
| `InMemoryJobQueue` | In-memory `IJobQueue` implementation |
| `JobExecutor` | Default `IJobExecutor` with DI factory support |
| `JobDispatcher` | High-level API for enqueuing and scheduling jobs |
| `BackgroundJobProcessor` | Concurrent polling processor |
| `RecurringJobScheduler` | Interval-based recurring job scheduler |

### Serialization
| Type | Description |
|------|-------------|
| `IJobSerializer` | Job input serialization interface |
| `JsonJobSerializer` | System.Text.Json implementation |

## Related Projects

- **Birko.BackgroundJobs.SQL** — SQL-based persistent job queue (planned)
- **Birko.BackgroundJobs.Redis** — Redis-based persistent job queue (planned)

## License

Part of the Birko Framework.
