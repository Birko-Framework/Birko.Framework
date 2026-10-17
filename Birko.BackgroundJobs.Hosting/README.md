# Birko.BackgroundJobs.Hosting

DI registration and hosted services for Birko.BackgroundJobs.

## Features
- `AddBackgroundJobs()`: queue, executor, dispatcher, processor and recurring scheduler, idempotent across modules
- `AddRecurringJob<TJob>(name, interval)`: applied to the running scheduler at startup, in any order
- Each job resolved from its own DI scope; leader election through `IJobLockProvider`, with followers taking over
- Shutdown waits for running jobs only as long as the host allows

## Usage

```csharp
builder.Services.AddBackgroundJobs(o => o.RecurringLockName = "myapp:recurring");
builder.Services.AddRecurringJob<CleanupJob>("cleanup", TimeSpan.FromHours(1));
```

## Running tests

```
dotnet test tests/Birko.BackgroundJobs.Hosting.Tests
```

## License
See the repository's root `LICENSE`.
