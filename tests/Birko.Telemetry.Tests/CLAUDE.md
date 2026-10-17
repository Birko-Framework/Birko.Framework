# Birko.Telemetry.Tests

## Overview
Unit tests for Birko.Telemetry — verifies metrics, tracing, store wrappers, and middleware.

## Project Location
`tests/Birko.Telemetry.Tests/` — .csproj (net10.0, xUnit + FluentAssertions)

## Components
- **ConventionsTests.cs** — Convention constant validation
- **StoreInstrumentationTests.cs** — MeterListener-based metric verification, Activity creation
- **InstrumentedStoreWrapperTests.cs** — Sync store wrapper delegation and metrics
- **AsyncInstrumentedStoreWrapperTests.cs** — Async store wrapper delegation and metrics
- **CorrelationIdMiddlewareTests.cs** — HTTP middleware behavior

## Dependencies
- Birko.Data.Core (.projitems)
- Birko.Data.Stores (.projitems)
- Birko.Telemetry (.projitems)
- Microsoft.AspNetCore.App (FrameworkReference)

## Maintenance
When adding new telemetry features, add corresponding tests here.

## The store meter is process-wide (TASK-657)

Every `MeterListener` receives every measurement of the `Birko.Data.Store` meter, from whichever thread made it. A
class that publishes to it or listens on it carries `[Collection(StoreMeterCollection.Name)]`, records into a
thread-safe collection, and asserts only its own measurements where it can (`StoreInstrumentationTests` filters on a
per-instance entity tag). Measured: a `List`-backed listener lost 3-4% of its entries while siblings published, with no
exception, and CI run 39054139288 failed on it.
