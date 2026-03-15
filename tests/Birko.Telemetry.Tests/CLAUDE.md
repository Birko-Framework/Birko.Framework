# Birko.Telemetry.Tests

## Overview
Unit tests for Birko.Telemetry — verifies metrics, tracing, store wrappers, and middleware.

## Project Location
`C:\Source\Birko.Telemetry.Tests\` — .csproj (net10.0, xUnit + FluentAssertions)

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
