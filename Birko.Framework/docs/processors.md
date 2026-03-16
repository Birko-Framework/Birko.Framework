# Data Processors Guide

## Overview

Birko.Data.Processors is a generic stream processing framework for composable data pipelines. It provides event-driven processors for XML, CSV, HTTP, and ZIP sources with a decorator-based composition pattern.

Unlike Birko.BackgroundJobs (which manages job lifecycle, retries, and scheduling), Birko.Data.Processors focuses on **parsing and transforming data streams** — the two compose naturally: processors provide the parsing engine, jobs provide the execution envelope.

## Core Interfaces

### IProcessor

Base contract for all processors:

```csharp
public interface IProcessor
{
    void Process();
    Task ProcessAsync(CancellationToken cancellationToken = default);
}
```

### IStreamProcessor

Extends `IProcessor` with stream acceptance, enabling decorator composition:

```csharp
public interface IStreamProcessor : IProcessor
{
    void ProcessStream(Stream stream);
    Task ProcessStreamAsync(Stream stream, CancellationToken cancellationToken = default);
}
```

## AbstractProcessor\<T\>

Generic base class with item lifecycle management and event pipeline. Subclasses populate `_item` during parsing, then call `PostProcessItem()` or `PostProcessItemAsync()` to emit the completed item.

### Type Constraint

```csharp
public abstract class AbstractProcessor<T> : IProcessor where T : new()
```

The `new()` constraint enables AOT-friendly item creation without `Activator.CreateInstance()`.

### Event Delegates

| Delegate | Signature | Description |
|----------|-----------|-------------|
| `OnItemProcessed` | `Func<T, CancellationToken, Task>` | Fires after each item (async) |
| `OnItemProcessedSync` | `Action<T>` | Fires after each item (sync) |
| `OnProcessFinished` | `Func<CancellationToken, Task>` | Fires when all processing completes (async) |
| `OnProcessFinishedSync` | `Action` | Fires when all processing completes (sync) |
| `OnElementStart` | `Action<string>` | Element opened (XML tag, CSV column index) |
| `OnElementValue` | `Action<string, string>` | Element name + text value |
| `OnElementEnd` | `Action<string>` | Element closed |

### Item Lifecycle

```csharp
// Inside a processor subclass:
InitItem();                          // Reset _item to new T()
_item.Name = value;                  // Populate during parsing
await PostProcessItemAsync(ct);      // Fire OnItemProcessed, then InitItem()
```

## Format Processors

### XmlProcessor\<T\>

Reads XML elements sequentially via `XmlReader`, firing events for each element:

```csharp
var processor = new XmlProcessor<Product>(
    sourceFile: "feed.xml",          // Optional: for file-based Process()
    encoding: Encoding.UTF8);        // Default: UTF-8

processor.OnElementValue = (name, value) =>
{
    switch (name)
    {
        case "name":  processor._item.Name = value; break;
        case "price": processor._item.Price = decimal.Parse(value); break;
    }
};
processor.OnElementEnd = name =>
{
    if (name == "product")
    {
        // Item boundary — emit and reset
    }
};

await processor.ProcessStreamAsync(stream, cancellationToken);
```

**Features:**
- `DtdProcessing.Ignore` for security
- `IgnoreWhitespace = true` for clean parsing
- `Async = true` in async path for non-blocking I/O
- Override `ProcessNode()` to customize element handling

### CsvProcessor\<T\>

Parses CSV rows and columns, firing events per column:

```csharp
var processor = new CsvProcessor<Product>(
    delimiter: ';',                  // Default: ','
    enclosure: '"',                  // Default: '"' (null to disable)
    encoding: Encoding.UTF8);        // Default: UTF-8

processor.SkipFirst = true;          // Skip header row (default: true)

processor.OnElementValue = (col, value) =>
{
    switch (col)
    {
        case "0": /* first column */  break;
        case "1": /* second column */ break;
    }
};
processor.OnItemProcessed = async (product, ct) =>
{
    await store.CreateAsync(product);
};

await processor.ProcessStreamAsync(stream, cancellationToken);
```

**Column events:** Column indices are passed as strings (`"0"`, `"1"`, etc.) to match the `Action<string>` element event signature.

### CsvParser (Birko.Helpers)

Low-level RFC 4180-compliant parser, available as a standalone utility in `Birko.Helpers`:

```csharp
using Birko.Helpers;

var parser = new CsvParser(stream, delimiter: ',', enclosure: '"');

foreach (var row in parser.Parse())  // Lazy IEnumerable<IList<string>>
{
    Console.WriteLine($"Line {parser.Line}: {string.Join(" | ", row)}");
}
```

**Features:**
- State machine with 4 states: `NewLine`, `CurrentLine`, `QuoteText`, `PotentialQuote`
- Handles escaped quotes (doubled `""`)
- Multiline quoted fields
- BOM-aware encoding detection
- Lazy evaluation via `yield return`

## Transport Decorators

### HttpProcessor\<TProcessor, TModel\>

Downloads a file via HTTP, then delegates to an inner `IStreamProcessor`. Cleans up the downloaded file after processing.

```csharp
using var processor = new HttpProcessor<XmlProcessor<Product>, Product>(
    inner: new XmlProcessor<Product>(),
    url: "https://example.com/feed.xml",
    downloadPath: "temp",
    fileName: "feed.xml",
    httpClient: httpClient);         // Optional: inject shared HttpClient

processor.OnItemProcessed = async (p, ct) => await store.CreateAsync(p);
await processor.ProcessAsync(cancellationToken);
```

**Features:**
- `HttpCompletionOption.ResponseHeadersRead` for streaming downloads
- Automatic file cleanup in `finally` block
- Filename sanitization (replaces `/\` with `_`)
- Throws `ProcessorDownloadException` on HTTP errors
- `Inner` property for direct access to the wrapped processor
- Owns `HttpClient` if none injected; disposes correctly

### ZipProcessor\<TProcessor, TModel\>

Extracts a file from a ZIP archive, then delegates to an inner `IStreamProcessor`:

```csharp
var zipProcessor = new ZipProcessor<CsvProcessor<Product>, Product>(
    inner: new CsvProcessor<Product>(delimiter: ';'),
    sourceFile: "data.zip",          // Optional: for file-based Process()
    extractPath: "temp",
    encoding: Encoding.UTF8);

zipProcessor.EntryIndex = 0;         // Which ZIP entry (default: first)
```

**Features:**
- Extracts to disk, processes, then cleans up
- Configurable `EntryIndex` for multi-file archives
- Throws `ProcessorException` on empty archives or invalid index

## Decorator Composition

Processors compose via generic type parameters to create multi-layer pipelines:

```
HttpProcessor<ZipProcessor<XmlProcessor<T>, T>, T>
     │              │              │
     │              │              └─ Innermost: parses XML elements
     │              └─ Middle: extracts first file from ZIP
     └─ Outermost: downloads file via HTTP
```

### Event Flow

Events wire from inner to outer in the constructor. You subscribe only on the outermost processor:

```csharp
// 3-layer: HTTP → ZIP → XML
using var processor = new HttpProcessor<
    ZipProcessor<XmlProcessor<Product>, Product>,
    Product>(
    new ZipProcessor<XmlProcessor<Product>, Product>(
        new XmlProcessor<Product>(),
        extractPath: "temp"),
    url: "https://example.com/feed.zip",
    downloadPath: "temp",
    fileName: "feed.zip");

// Single subscription on outermost processor
processor.OnItemProcessed = async (product, ct) =>
{
    await store.CreateAsync(product);
};

await processor.ProcessAsync(cancellationToken);
```

### Accessing Inner Processors

Use the `Inner` property to configure nested processors:

```csharp
var http = new HttpProcessor<ZipProcessor<CsvProcessor<Product>, Product>, Product>(
    new ZipProcessor<CsvProcessor<Product>, Product>(
        new CsvProcessor<Product>(delimiter: ';'),
        extractPath: "temp"),
    url, "temp", "feed.zip");

// Configure the CSV processor deep in the chain
http.Inner.Inner.SkipFirst = false;
```

## Integration with BackgroundJobs

Processors compose naturally with `Birko.BackgroundJobs`:

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

        processor.OnItemProcessed = async (product, token) =>
            await _store.CreateAsync(product);

        await processor.ProcessAsync(ct);
    }
}

// Schedule
await dispatcher.EnqueueAsync<ImportFeedJob, FeedInput>(
    new FeedInput { Url = "https://...", FileName = "feed.xml" });
```

## Exceptions

| Exception | When |
|-----------|------|
| `ProcessorException` | Base: missing source file, empty ZIP, invalid entry index |
| `ProcessorDownloadException` | HTTP download failure (includes `Url` property) |
| `ProcessorParseException` | Parse error (includes `Element` property) |

## Sync vs Async

All processors provide both sync and async methods:

| Sync | Async |
|------|-------|
| `Process()` | `ProcessAsync(ct)` |
| `ProcessStream(stream)` | `ProcessStreamAsync(stream, ct)` |
| `OnItemProcessedSync` | `OnItemProcessed` |
| `OnProcessFinishedSync` | `OnProcessFinished` |

Sync methods use `OnItemProcessedSync`/`OnProcessFinishedSync` delegates. Async methods use `OnItemProcessed`/`OnProcessFinished`.

## Dependencies

- `Microsoft.Extensions.Logging.Abstractions` — `ILogger` (optional, for logging in processors)
- `Birko.Helpers` — `CsvParser` (used by `CsvProcessor`)

## Related Resources

- [Background Jobs](background-jobs.md) — Job queue, scheduling, retry
- [Store Implementation](store-implementation.md) — Data stores for persisting processed items
- [Birko.Data.Processors CLAUDE.md](../../Birko.Data.Processors/CLAUDE.md) — Project details
