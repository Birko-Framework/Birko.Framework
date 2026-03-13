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
│   ├── JobQueueOptions.cs        - Processor config (concurrency, polling, timeout, retention)
│   ├── JobResult.cs              - Execution result (Success/Failed, Duration, Error)
│   ├── JobStatus.cs              - Lifecycle enum: Pending→Scheduled→Processing→Completed/Failed/Dead/Cancelled
│   └── RetryPolicy.cs            - Exponential backoff retry configuration
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

- None (core only, uses System.Text.Json built-in)

## Key Design Decisions

- **Shared project (.shproj)** — No NuGet dependencies, consumed via .projitems reference by host project
- **IJobQueue is the extension point** — SQL/Redis backends implement this interface. Everything else (dispatcher, processor, scheduler) is queue-agnostic
- **JobDescriptor is the persistence model** — Contains all state needed to serialize, store, and resume a job across restarts (for persistent backends)
- **DI via factory function** — `JobExecutor` takes `Func<Type, object>` instead of depending on `IServiceProvider` directly, keeping the core DI-container-agnostic
- **Typed and untyped jobs** — `IJob` for simple parameterless work, `IJob<TInput>` for jobs with serialized input data
- **In-memory queue for testing** — `InMemoryJobQueue` allows unit testing without external dependencies; jobs are lost on restart

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
