# Birko.BackgroundJobs.XML

XML file-based job queue for the Birko Background Jobs framework. Built on Birko.Data.XML for zero-dependency local development, testing, and scenarios where a human-readable XML audit trail is preferred.

## Features

- **File-based storage** — Jobs stored as XML file via `AsyncXmlStore`
- **No external dependencies** — No database server required
- **Auto-file creation** — XML file created automatically on first use
- **Expression-based queries** — Uses Birko.Data.Stores lambda expressions for filtering
- **Retry with backoff** — Failed jobs are re-scheduled with configurable delay
- **XML metadata** — Job metadata stored via an explicit `SerializableMetadata` wrapper (System.Xml.Serialization does not natively support `Dictionary<TKey, TValue>`)

## Dependencies

- Birko.BackgroundJobs (core interfaces)
- Birko.Data.Core (AbstractModel)
- Birko.Data.Stores (store interfaces, Settings)
- Birko.Data.XML (AsyncXmlStore)
- Birko.Serialization (ISerializer, SystemXmlSerializer)
- Birko.Time.Abstractions (IDateTimeProvider)

## Usage

```csharp
using Birko.BackgroundJobs;
using Birko.BackgroundJobs.XML;
using Birko.BackgroundJobs.Processing;
using Birko.Configuration;
using Birko.Time;

var settings = new Settings
{
    Location = "./data",
    Name = "background-jobs"
};

var clock = new SystemDateTimeProvider();
var queue = new XmlJobQueue(settings, clock);

var dispatcher = new JobDispatcher(queue);
await dispatcher.EnqueueAsync<MyJob>();

var executor = new JobExecutor(type => serviceProvider.GetRequiredService(type));
var processor = new BackgroundJobProcessor(queue, executor);
await processor.RunAsync(cancellationToken);
```

## API Reference

| Type | Description |
|------|-------------|
| `XmlJobQueue` | `IJobQueue` implementation using `AsyncXmlStore` |
| `XmlJobDescriptorModel` | `AbstractModel` with `XmlRoot`/`XmlElement` attributes |
| `SerializableMetadata` + `MetadataEntry` | XML-friendly wrapper for the job metadata dictionary |
| `XmlJobQueueSchema` | File creation/deletion utilities |

## License

Part of the Birko Framework.
