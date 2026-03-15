# Birko.Telemetry

## Overview
Thin instrumentation layer over .NET built-in telemetry APIs (`System.Diagnostics.Metrics`, `System.Diagnostics.Activity`, `ILogger<T>`). No external NuGet dependencies.

## Project Location
`C:\Source\Birko.Telemetry\` — Shared project (.shproj + .projitems)

## Components

### Conventions
- **BirkoTelemetryConventions.cs** — Static class with standard meter/activity/metric/tag names

### Core Instrumentation
- **StoreInstrumentation.cs** — Internal helper with static `Meter`, `ActivitySource`, `Histogram<double>`, `Counter<long>`. Four overloads: sync void, sync with return, async void, async with return. Each starts Activity, records duration/count/errors.

### Store Wrappers (Decorator Pattern)
- **InstrumentedStoreWrapper.cs** — Wraps `IStore<T>`, implements `IStoreWrapper<T>`
- **InstrumentedBulkStoreWrapper.cs** — Extends above, wraps `IBulkStore<T>` with `isBulk: true`
- **AsyncInstrumentedStoreWrapper.cs** — Wraps `IAsyncStore<T>`
- **AsyncInstrumentedBulkStoreWrapper.cs** — Extends above, wraps `IAsyncBulkStore<T>`

### ASP.NET Core
- **BirkoTelemetryOptions.cs** — Options: `EnableCorrelationId`, `CorrelationIdHeaderName`
- **CorrelationIdMiddleware.cs** — Reads/generates `X-Correlation-Id`, sets Activity baggage, echoes in response
- **TelemetryServiceExtensions.cs** — `AddBirkoTelemetry()` and `UseBirkoCorrelationId()` extensions

### Extensions
- **StoreInstrumentationExtensions.cs** — Fluent `.WithInstrumentation()`, `.WithBulkInstrumentation()`, `.WithAsyncInstrumentation()`, `.WithAsyncBulkInstrumentation()`

## Dependencies
- Birko.Data.Core (AbstractModel)
- Birko.Data.Stores (IStore, IAsyncStore, IBulkStore, IAsyncBulkStore, IStoreWrapper, StoreDataDelegate, OrderBy)
- Microsoft.AspNetCore.Http (FrameworkReference)
- System.Diagnostics.DiagnosticSource (BCL built-in)

## Maintenance
When modifying this project, update:
- This CLAUDE.md if components change
- README.md for API changes
- Root CLAUDE.md project listing if project is renamed/moved
- Birko.Telemetry.projitems if files are added/removed
