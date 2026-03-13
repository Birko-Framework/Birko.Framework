# Birko.BackgroundJobs.ElasticSearch

Elasticsearch-based persistent job queue for the Birko Background Jobs framework. Built on Birko.Data.ElasticSearch for seamless integration with the framework's data access layer.

## Features

- **Persistent storage** — Jobs stored as Elasticsearch documents via `AsyncElasticSearchStore`
- **Auto-index creation** — Index created automatically on first use
- **Expression-based queries** — Uses Birko.Data lambda expressions for filtering
- **Retry with backoff** — Failed jobs are re-scheduled with configurable delay
- **Scroll-based reads** — Leverages ES scroll API for large result sets

## Dependencies

- Birko.BackgroundJobs (core interfaces)
- Birko.Data (AbstractModel, stores, settings)
- Birko.Data.ElasticSearch (AsyncElasticSearchStore, NEST)

## Usage

```csharp
using Birko.BackgroundJobs;
using Birko.BackgroundJobs.ElasticSearch;
using Birko.BackgroundJobs.Processing;

var settings = new Birko.Data.ElasticSearch.Stores.Settings
{
    Location = "http://localhost:9200",
    Name = "background-jobs"
};

var queue = new ElasticSearchJobQueue(settings);

var dispatcher = new JobDispatcher(queue);
await dispatcher.EnqueueAsync<MyJob>();

var executor = new JobExecutor(type => serviceProvider.GetRequiredService(type));
var processor = new BackgroundJobProcessor(queue, executor);
await processor.RunAsync(cancellationToken);
```

## API Reference

| Type | Description |
|------|-------------|
| `ElasticSearchJobQueue` | `IJobQueue` implementation using `AsyncElasticSearchStore` |
| `ElasticJobDescriptorModel` | `AbstractModel` with NEST attributes for index mapping |
| `ElasticSearchJobQueueSchema` | Index creation/drop utilities |

## License

Part of the Birko Framework.
