# Birko.Data.Processors

## Overview
Generic stream processor framework for composable data pipelines. Event-driven processors for XML, CSV, HTTP, and ZIP sources with decorator-based composition.

## Project Location
`C:\Source\Birko.Data.Processors\` (shared project, .shproj)

## Components

### Core/
- **IProcessor.cs** — `IProcessor` (ProcessAsync) and `IStreamProcessor` (ProcessStreamAsync) interfaces
- **AbstractProcessor.cs** — Generic base `AbstractProcessor<T>` with item lifecycle (`InitItem`, `PostProcessItemAsync`), event delegates (`OnItemProcessed`, `OnElementStart/Value/End`, `OnProcessFinished`), and `ILogger` support
- **ProcessorException.cs** — `ProcessorException`, `ProcessorDownloadException` (URL), `ProcessorParseException` (element)

### Formats/
- **XmlProcessor.cs** — `XmlProcessor<T> : AbstractProcessor<T>, IStreamProcessor` — XmlReader-based sequential parser, fires element events, async with DtdProcessing.Ignore
- **CsvProcessor.cs** — `CsvProcessor<T> : AbstractProcessor<T>, IStreamProcessor` — Row/column parser, configurable delimiter/enclosure/encoding, `SkipFirst` header flag. Uses `Birko.Helpers.CsvParser` for parsing.

### Transport/
- **HttpProcessor.cs** — `HttpProcessor<TProcessor, TModel> : AbstractProcessor<TModel>, IDisposable` — HTTP download decorator, `HttpCompletionOption.ResponseHeadersRead` for streaming, file cleanup in finally block, exposes `Inner` property
- **ZipProcessor.cs** — `ZipProcessor<TProcessor, TModel> : AbstractProcessor<TModel>, IStreamProcessor, IDisposable` — ZIP extraction decorator, configurable `EntryIndex`, file cleanup in finally block, exposes `Inner` property

## Architecture

### Decorator Composition
Processors compose via generic type parameters:
```
HttpProcessor<ZipProcessor<XmlProcessor<T>, T>, T>
```
Events wire from inner → outer via constructor, allowing single subscription on outermost processor.

### Event Pipeline
- `OnItemProcessed: Func<T, CancellationToken, Task>` — item complete
- `OnProcessFinished: Func<CancellationToken, Task>` — all done
- `OnElementStart/Value/End: Action<string>` or `Action<string, string>` — element lifecycle

### Key Design Decisions
- **Async-first** — No sync methods, all CancellationToken-aware
- **`new()` constraint** — AOT-friendly item creation, no Activator.CreateInstance
- **ILogger** — Microsoft.Extensions.Logging, not NLog
- **Zero Birko dependencies** — Standalone, no coupling to stores/models
- **Delegates over events** — Simpler wiring, no reflection-based invocation

## Dependencies
- `Microsoft.Extensions.Logging.Abstractions` (ILogger)
- `Birko.Helpers` (CsvParser — used by CsvProcessor)

## Extracted From
Originally `Affiliate.Import.Processors\` — base/generic files only. Domain-specific processors (Common/, Custom/) remain in Affiliate.Import.

## Maintenance
- When adding new format processors, implement `IStreamProcessor` and extend `AbstractProcessor<T>`
- When adding new transport decorators, follow `HttpProcessor`/`ZipProcessor` pattern (wire inner events in constructor, cleanup in finally)
- Keep namespace flat: `Birko.Data.Processors` for all public types
