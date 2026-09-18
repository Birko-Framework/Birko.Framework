# Birko.BackgroundJobs.Tests

Unit tests for the Birko.BackgroundJobs framework.

## Test Coverage

- **Core** - RetryPolicy, JobDescriptor, JobResult, JobContext, JobQueueOptions
- **Serialization** - JsonJobSerializer
- **Processing** - InMemoryJobQueue, JobDispatcher, JobExecutor, BackgroundJobProcessor, RecurringJobScheduler

## Test Framework

- [xUnit](https://xunit.net/) 2.9.3
- [FluentAssertions](https://fluentassertions.com/) 7.0.0

## Running Tests

```bash
dotnet test
```

## License

MIT License - see [License.md](License.md)
