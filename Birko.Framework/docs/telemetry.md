# Birko.Telemetry

Thin instrumentation layer for the Birko framework. Built on .NET built-in APIs with zero external NuGet dependencies.

## Design Philosophy

Instead of custom `ILog`/`IMetrics`/`ITracer` abstractions, Birko.Telemetry leverages the .NET platform directly:
- **Metrics:** `System.Diagnostics.Metrics` (Meter, Counter, Histogram)
- **Tracing:** `System.Diagnostics.Activity` / `ActivitySource`
- **Logging:** `ILogger<T>` (already used throughout the framework)

This means any OpenTelemetry exporter, Prometheus scraper, or `MeterListener` works out of the box — no adapter layer needed.

## Architecture

### Conventions

All telemetry identifiers are centralized in `BirkoTelemetryConventions`:

| Constant | Value | Description |
|----------|-------|-------------|
| `MeterName` | `Birko.Data.Store` | Meter name for all store metrics |
| `ActivitySourceName` | `Birko.Data.Store` | ActivitySource for distributed tracing |
| `OperationDurationMetric` | `birko.store.operation.duration` | Histogram (ms) |
| `OperationCountMetric` | `birko.store.operation.count` | Counter |
| `OperationErrorMetric` | `birko.store.operation.errors` | Counter |
| `StoreTypeTag` | `birko.store.type` | Tag: store implementation type |
| `EntityTypeTag` | `birko.store.entity_type` | Tag: entity model type |
| `OperationTag` | `birko.store.operation` | Tag: operation name (Read, Create, etc.) |
| `TenantTag` | `birko.store.tenant` | Tag: tenant identifier |
| `BulkTag` | `birko.store.bulk` | Tag: whether operation is bulk |
| `DefaultCorrelationIdHeader` | `X-Correlation-Id` | HTTP header for correlation |

### StoreInstrumentation (Internal)

Shared helper that holds static `Meter`, `ActivitySource`, and metric instruments. Provides four overloads:

```
Execute(storeType, entityType, operation, isBulk, Action)        — sync void
Execute<TResult>(...)                                             — sync with return
ExecuteAsync(...)                                                 — async void
ExecuteAsync<TResult>(...)                                        — async with return
```

Each overload:
1. Creates a `TagList` with store type, entity type, operation, and bulk flag
2. Starts an `Activity` from the shared `ActivitySource`
3. Starts a `Stopwatch`
4. Delegates to the provided action/func
5. Records duration histogram + operation counter on success
6. Records duration + operation counter + error counter on exception
7. Sets `Activity.Status` to `Ok` or `Error`

### Store Wrappers

Follow the same decorator pattern as `AuditStoreWrapper` and `SoftDeleteStoreWrapper`:

| Wrapper | Wraps | Inherits |
|---------|-------|----------|
| `InstrumentedStoreWrapper<TStore, T>` | `IStore<T>` | — |
| `InstrumentedBulkStoreWrapper<TStore, T>` | `IBulkStore<T>` | `InstrumentedStoreWrapper` |
| `AsyncInstrumentedStoreWrapper<TStore, T>` | `IAsyncStore<T>` | — |
| `AsyncInstrumentedBulkStoreWrapper<TStore, T>` | `IAsyncBulkStore<T>` | `AsyncInstrumentedStoreWrapper` |

All wrappers implement `IStoreWrapper<T>` to allow unwrapping the inner store.

Constructor caches `typeof(TStore).FullName` and `typeof(T).FullName` for metric tags (no per-call reflection).

### Fluent Extensions

```csharp
// Sync
var instrumented = myStore.WithInstrumentation<MyStore, MyEntity>();
var bulkInstrumented = myBulkStore.WithBulkInstrumentation<MyBulkStore, MyEntity>();

// Async
var asyncInstrumented = myAsyncStore.WithAsyncInstrumentation<MyAsyncStore, MyEntity>();
var asyncBulkInstrumented = myAsyncBulkStore.WithAsyncBulkInstrumentation<MyAsyncBulkStore, MyEntity>();
```

### Correlation ID Middleware

ASP.NET Core middleware that:
1. Reads `X-Correlation-Id` from request headers (or generates a GUID)
2. Sets `Activity.Current?.SetBaggage("correlation-id", value)`
3. Writes the correlation ID to the response header

Configuration via `BirkoTelemetryOptions`:
- `EnableCorrelationId` (bool, default: `true`)
- `CorrelationIdHeaderName` (string, default: `"X-Correlation-Id"`)

## Usage

### Basic Store Instrumentation

```csharp
using Birko.Telemetry;

// Wrap any IStore<T>
var store = new MyJsonStore<Product>();
var instrumented = store.WithInstrumentation<MyJsonStore<Product>, Product>();

// All operations are now instrumented
var guid = instrumented.Create(new Product { Name = "Widget" });
var product = instrumented.Read(guid);
instrumented.Update(product);
instrumented.Delete(product);
```

### ASP.NET Core Integration

```csharp
// Program.cs
builder.Services.AddBirkoTelemetry(options =>
{
    options.EnableCorrelationId = true;
    options.CorrelationIdHeaderName = "X-Correlation-Id";
});

var app = builder.Build();
app.UseBirkoCorrelationId();
```

### Consuming Metrics with MeterListener

```csharp
using var listener = new MeterListener();
listener.InstrumentPublished = (instrument, l) =>
{
    if (instrument.Meter.Name == BirkoTelemetryConventions.MeterName)
        l.EnableMeasurementEvents(instrument);
};
listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
{
    Console.WriteLine($"{instrument.Name}: {value:F2}ms");
});
listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
{
    Console.WriteLine($"{instrument.Name}: {value}");
});
listener.Start();
```

### OpenTelemetry Integration

Since Birko.Telemetry uses standard .NET APIs, OpenTelemetry picks up metrics and traces automatically:

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter(BirkoTelemetryConventions.MeterName))
    .WithTracing(tracing => tracing
        .AddSource(BirkoTelemetryConventions.ActivitySourceName));
```

### Composing with Other Wrappers

Instrumentation composes with other store wrappers:

```csharp
// Audit first, then instrument (metrics include audit overhead)
var store = new MyStore<Product>();
var audited = new AuditStoreWrapper<MyStore<Product>, Product>(store, auditContext);
var instrumented = audited.WithInstrumentation<AuditStoreWrapper<MyStore<Product>, Product>, Product>();
```

## Dependencies

- `Birko.Data.Core` — `AbstractModel`
- `Birko.Data.Stores` — `IStore<T>`, `IAsyncStore<T>`, `IBulkStore<T>`, `IAsyncBulkStore<T>`, `IStoreWrapper<T>`, `StoreDataDelegate<T>`, `OrderBy<T>`
- `Microsoft.AspNetCore.Http` — FrameworkReference (for middleware)
- `System.Diagnostics.DiagnosticSource` — BCL built-in

No external NuGet packages required.

## Metrics Reference

### birko.store.operation.duration (Histogram, ms)
Recorded for every store operation. Tags: store type, entity type, operation, bulk.

### birko.store.operation.count (Counter)
Incremented once per operation (success or failure). Same tags as duration.

### birko.store.operation.errors (Counter)
Incremented only on exception. Same tags as duration.
