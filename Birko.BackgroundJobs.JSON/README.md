# Birko.BackgroundJobs.JSON

JSON file-based job queue for the Birko Background Jobs framework. Built on Birko.Data.JSON for zero-dependency local development and testing.

## Features

- **File-based storage** — Jobs stored as JSON file via `AsyncJsonStore`
- **No external dependencies** — No database server required
- **Auto-file creation** — JSON file created automatically on first use
- **Expression-based queries** — Uses Birko.Data lambda expressions for filtering
- **Retry with backoff** — Failed jobs are re-scheduled with configurable delay
- **Good for testing** — Drop-in replacement for production queues during development

## Dependencies

- Birko.BackgroundJobs (core interfaces)
- Birko.Data (AbstractModel, stores, Settings)
- Birko.Data.JSON (AsyncJsonStore)

## Usage

```csharp
using Birko.BackgroundJobs;
using Birko.BackgroundJobs.JSON;
using Birko.BackgroundJobs.Processing;
using Birko.Data.Stores;

var settings = new Settings
{
    Location = "./data",
    Name = "background-jobs"
};

var queue = new JsonJobQueue(settings);

var dispatcher = new JobDispatcher(queue);
await dispatcher.EnqueueAsync<MyJob>();

var executor = new JobExecutor(type => serviceProvider.GetRequiredService(type));
var processor = new BackgroundJobProcessor(queue, executor);
await processor.RunAsync(cancellationToken);
```

## API Reference

| Type | Description |
|------|-------------|
| `JsonJobQueue` | `IJobQueue` implementation using `AsyncJsonStore` |
| `JsonJobDescriptorModel` | `AbstractModel` with `JsonPropertyName` attributes |
| `JsonJobQueueSchema` | File creation/deletion utilities |

## License

Part of the Birko Framework.
