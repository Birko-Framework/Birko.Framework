# Birko.BackgroundJobs.MongoDB

MongoDB-based persistent job queue for the Birko Background Jobs framework. Built on Birko.Data.MongoDB for seamless integration with the framework's data access layer.

## Features

- **Persistent storage** — Jobs stored as MongoDB documents via `AsyncMongoDBStore`
- **Auto-collection setup** — Collection initialized automatically on first use
- **Expression-based queries** — Uses Birko.Data.Stores lambda expressions for filtering
- **Transaction support** — Integrates with MongoDB sessions for atomic operations
- **Retry with backoff** — Failed jobs are re-scheduled with configurable delay

## Dependencies

- Birko.BackgroundJobs (core interfaces)
- Birko.Data.Core (AbstractModel)
- Birko.Data.Stores (store interfaces, Settings)
- Birko.Data.MongoDB (AsyncMongoDBStore, MongoDB.Driver)

## Usage

```csharp
using Birko.BackgroundJobs;
using Birko.BackgroundJobs.MongoDB;
using Birko.BackgroundJobs.Processing;

var settings = new Birko.Data.MongoDB.Stores.Settings
{
    Location = "localhost",
    Name = "mydb",
    UserName = "admin",
    Password = "secret"
};

var queue = new MongoDBJobQueue(settings);

var dispatcher = new JobDispatcher(queue);
await dispatcher.EnqueueAsync<MyJob>();

var executor = new JobExecutor(type => serviceProvider.GetRequiredService(type));
var processor = new BackgroundJobProcessor(queue, executor);
await processor.RunAsync(cancellationToken);
```

## API Reference

| Type | Description |
|------|-------------|
| `MongoDBJobQueue` | `IJobQueue` implementation using `AsyncMongoDBStore` |
| `MongoJobDescriptorModel` | `AbstractModel` with BSON attributes for document mapping |
| `MongoDBJobQueueSchema` | Collection creation/drop utilities |

## License

Part of the Birko Framework.
