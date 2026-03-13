# Birko.BackgroundJobs.RavenDB

RavenDB-based persistent job queue for the Birko Background Jobs framework. Built on Birko.Data.RavenDB for seamless integration with the framework's data access layer.

## Features

- **Persistent storage** — Jobs stored as RavenDB documents via `AsyncRavenDBStore`
- **Auto-database setup** — Database initialized automatically on first use
- **Expression-based queries** — Uses Birko.Data lambda expressions for filtering
- **Transaction support** — Integrates with RavenDB document sessions for atomic operations
- **Retry with backoff** — Failed jobs are re-scheduled with configurable delay
- **Convention-based mapping** — No special attributes needed on model properties

## Dependencies

- Birko.BackgroundJobs (core interfaces)
- Birko.Data (AbstractModel, stores, RemoteSettings)
- Birko.Data.RavenDB (AsyncRavenDBStore, RavenDB.Client)

## Usage

```csharp
using Birko.BackgroundJobs;
using Birko.BackgroundJobs.RavenDB;
using Birko.BackgroundJobs.Processing;
using Birko.Data.Stores;

var settings = new RemoteSettings
{
    Location = "http://localhost:8080",
    Name = "BackgroundJobs",
    UserName = "admin",
    Password = "secret"
};

var queue = new RavenDBJobQueue(settings);

var dispatcher = new JobDispatcher(queue);
await dispatcher.EnqueueAsync<MyJob>();

var executor = new JobExecutor(type => serviceProvider.GetRequiredService(type));
var processor = new BackgroundJobProcessor(queue, executor);
await processor.RunAsync(cancellationToken);
```

## API Reference

| Type | Description |
|------|-------------|
| `RavenDBJobQueue` | `IJobQueue` implementation using `AsyncRavenDBStore` |
| `RavenJobDescriptorModel` | `AbstractModel` with convention-based mapping |
| `RavenDBJobQueueSchema` | Database creation/drop utilities |

## License

Part of the Birko Framework.
