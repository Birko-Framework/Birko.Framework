# Birko.BackgroundJobs.Tests

## Overview
Unit tests for the Birko.BackgroundJobs core framework (in-memory queue, dispatcher, executor, processor, scheduler, serialization, retry policies).

## Project Location
- **Directory:** `C:\Source\Birko.BackgroundJobs.Tests\`
- **Project File:** `Birko.BackgroundJobs.Tests.csproj` (.NET 10.0 test project)

## Components

### Test Classes
- `Core/RetryPolicyTests.cs` - RetryPolicy delay calculation, exponential backoff, presets
- `Core/JobDescriptorTests.cs` - JobDescriptor defaults, property assignment
- `Core/JobResultTests.cs` - JobResult factory methods
- `Core/JobContextTests.cs` - JobContext construction, metadata
- `Serialization/JsonJobSerializerTests.cs` - Serialize/deserialize round-trips
- `Processing/InMemoryJobQueueTests.cs` - Full queue operations (enqueue, dequeue, complete, fail, cancel, purge, status queries)
- `Processing/JobDispatcherTests.cs` - Fluent enqueue API (immediate, scheduled, priority, queue routing, cancel)
- `Processing/JobExecutorTests.cs` - Job execution (parameterless, typed input, error handling)
- `Processing/BackgroundJobProcessorTests.cs` - Processor lifecycle (process jobs, cancellation, timeout)
- `Processing/RecurringJobSchedulerTests.cs` - Register, remove, fire on interval

### Test Helpers
- `TestJobs.cs` - Test IJob and IJob<TInput> implementations

## Dependencies
- Birko.BackgroundJobs (shared project via .projitems)
- xUnit 2.9.3
- FluentAssertions 7.0.0
- Microsoft.NET.Test.Sdk 18.0.1

## Maintenance
- Update tests when BackgroundJobs interfaces or behavior changes
- Follow xUnit + FluentAssertions patterns consistent with other Birko test projects
