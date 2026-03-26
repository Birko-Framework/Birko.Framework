# Birko.Telemetry

Thin instrumentation layer for the Birko framework, providing metrics, distributed tracing, and correlation ID support for store operations. Built on .NET built-in APIs (`System.Diagnostics.Metrics`, `System.Diagnostics.Activity`) with zero external NuGet dependencies.

## Features

- **Store Metrics** — Histogram for operation duration, counters for operation count and errors, with tags for store type, entity type, operation name, and bulk flag
- **Distributed Tracing** — Automatic `Activity` creation for every store operation with proper status codes
- **Store Wrappers** — Decorator wrappers for `IStore<T>`, `IBulkStore<T>`, `IAsyncStore<T>`, and `IAsyncBulkStore<T>`
- **Correlation ID Middleware** — ASP.NET Core middleware that reads/generates `X-Correlation-Id` header and propagates via `Activity` baggage
- **Fluent API** — Extension methods to easily wrap any store with instrumentation

## Usage

### Wrapping a Store

```csharp
using Birko.Telemetry;

// Wrap a sync store
var instrumented = myStore.WithInstrumentation<MyStoreType, MyEntity>();

// Wrap a bulk store
var instrumentedBulk = myBulkStore.WithBulkInstrumentation<MyBulkStoreType, MyEntity>();

// Wrap an async store
var instrumentedAsync = myAsyncStore.WithAsyncInstrumentation<MyAsyncStoreType, MyEntity>();

// Wrap an async bulk store
var instrumentedAsyncBulk = myAsyncBulkStore.WithAsyncBulkInstrumentation<MyAsyncBulkStoreType, MyEntity>();
```

### ASP.NET Core Integration

```csharp
var builder = WebApplication.CreateBuilder(args);

// Register telemetry options
builder.Services.AddBirkoTelemetry(options =>
{
    options.EnableCorrelationId = true;
    options.CorrelationIdHeaderName = "X-Correlation-Id";
});

var app = builder.Build();

// Add correlation ID middleware
app.UseBirkoCorrelationId();
```

### Consuming Metrics

Use `MeterListener` or an OpenTelemetry exporter to consume metrics from the `Birko.Data.Store` meter:

- `birko.store.operation.duration` — Histogram (ms)
- `birko.store.operation.count` — Counter
- `birko.store.operation.errors` — Counter

## Conventions

All telemetry names are defined in `BirkoTelemetryConventions`:

| Constant | Value |
|----------|-------|
| MeterName | `Birko.Data.Store` |
| ActivitySourceName | `Birko.Data.Store` |
| OperationDurationMetric | `birko.store.operation.duration` |
| OperationCountMetric | `birko.store.operation.count` |
| OperationErrorMetric | `birko.store.operation.errors` |

## Dependencies

- Birko.Data.Core (AbstractModel)
- Birko.Data.Stores (IStore, IAsyncStore, IBulkStore, IAsyncBulkStore, IStoreWrapper)
- Microsoft.AspNetCore.Http (FrameworkReference, for middleware)
- No external NuGet packages

## Filter-Based Bulk Operations

All filter-based bulk operations (`Update(filter, PropertyUpdate)`, `Update(filter, Action)`, `Delete(filter)`) are instrumented with metrics and tracing, tagged as bulk operations.

## License

This project is licensed under the MIT License - see the [License.md](License.md) file for details.
