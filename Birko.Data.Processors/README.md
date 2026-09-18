# Birko.Data.Processors

Generic stream processor framework for composable data pipelines. Provides event-driven processors for XML, CSV, HTTP, and ZIP sources with decorator-based composition.

## Features

- **XML Processing** — XmlReader-based sequential parsing with element events
- **CSV Processing** — RFC 4180-compliant parser with quoted field support
- **HTTP Transport** — Download-and-process decorator with automatic cleanup
- **ZIP Transport** — Extract-and-process decorator with automatic cleanup
- **Decorator Composition** — Chain processors: `HttpProcessor<ZipProcessor<XmlProcessor<T>, T>, T>`
- **Event Pipeline** — `OnItemProcessed`, `OnElementStart/Value/End`, `OnProcessFinished`
- **Async-first** — All processors use `async/await` with `CancellationToken` support
- **Zero framework coupling** — Only depends on `Microsoft.Extensions.Logging.Abstractions`

## Components

| Component | Description |
|-----------|-------------|
| `IProcessor` | Base interface: `ProcessAsync(CancellationToken)` |
| `IStreamProcessor` | Extends IProcessor with `ProcessStreamAsync(Stream, CancellationToken)` |
| `AbstractProcessor<T>` | Generic base with item lifecycle and event delegates |
| `XmlProcessor<T>` | XML element-by-element stream parser |
| `CsvProcessor<T>` | CSV row/column parser with configurable delimiter and header skip |
| `CsvParser` | Low-level RFC 4180 state-machine CSV parser (lazy `IEnumerable`) |
| `HttpProcessor<P,T>` | HTTP download decorator, delegates to inner `IStreamProcessor` |
| `ZipProcessor<P,T>` | ZIP extraction decorator, delegates to inner `IStreamProcessor` |
| `ProcessorException` | Base exception with `ProcessorDownloadException` and `ProcessorParseException` |

## Usage

### Simple CSV Processing

```csharp
var csv = new CsvProcessor<Product>(delimiter: ';');
csv.OnElementValue = (col, value) =>
{
    switch (col)
    {
        case "0": csv._item.Name = value; break;
        case "1": csv._item.Price = decimal.Parse(value); break;
    }
};
csv.OnItemProcessed = async (product, ct) => await store.CreateAsync(product);
await csv.ProcessStreamAsync(fileStream);
```

### Remote XML Feed

```csharp
using var processor = new HttpProcessor<XmlProcessor<Product>, Product>(
    new XmlProcessor<Product>(),
    url: "https://example.com/feed.xml",
    downloadPath: "temp",
    fileName: "feed.xml");

processor.OnItemProcessed = async (p, ct) => await store.CreateAsync(p);
await processor.ProcessAsync(cancellationToken);
```

### Remote ZIP containing XML (3-layer Composition)

```csharp
using var processor = new HttpProcessor<ZipProcessor<XmlProcessor<Product>, Product>, Product>(
    new ZipProcessor<XmlProcessor<Product>, Product>(
        new XmlProcessor<Product>(),
        extractPath: "temp"),
    url: "https://example.com/feed.zip",
    downloadPath: "temp",
    fileName: "feed.zip");

processor.OnItemProcessed = async (p, ct) => await store.CreateAsync(p);
await processor.ProcessAsync(cancellationToken);
```

### Integration with Birko.BackgroundJobs

```csharp
public class ImportFeedJob : IJob<FeedInput>
{
    private readonly IAsyncStore<Product> _store;

    public ImportFeedJob(IAsyncStore<Product> store) => _store = store;

    public async Task ExecuteAsync(FeedInput input, JobContext context, CancellationToken ct)
    {
        using var processor = new HttpProcessor<XmlProcessor<Product>, Product>(
            new XmlProcessor<Product>(),
            input.Url, "temp", input.FileName);

        processor.OnItemProcessed = async (p, token) => await _store.CreateAsync(p);
        await processor.ProcessAsync(ct);
    }
}
```

## Composition Pattern

Processors compose via generic type parameters (decorator pattern):

```
HttpProcessor<ZipProcessor<XmlProcessor<T>, T>, T>
     │              │              │
     │              │              └─ Innermost: parses XML elements
     │              └─ Middle: extracts first file from ZIP
     └─ Outermost: downloads file via HTTP

Event flow: Inner → Middle → Outer → Your handlers
```

## Dependencies

- `Microsoft.Extensions.Logging.Abstractions` — ILogger interface (no concrete logger required)

## License

MIT License - see [License.md](License.md)
