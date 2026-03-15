# Birko.Telemetry.Tests

Unit tests for the Birko.Telemetry project.

## Test Framework

- **xUnit** 2.9.3
- **FluentAssertions** 7.0.0
- **Target Framework:** .NET 10.0

## Test Classes

- **ConventionsTests** — Verifies all convention constants are non-null and non-empty
- **StoreInstrumentationTests** — Verifies metrics are emitted via `MeterListener` and Activities are created
- **InstrumentedStoreWrapperTests** — Verifies sync store wrapper delegates all methods and emits metrics
- **AsyncInstrumentedStoreWrapperTests** — Verifies async store wrapper delegates all methods and emits metrics
- **CorrelationIdMiddlewareTests** — Verifies correlation ID header read/write and Activity baggage

## Running Tests

```bash
dotnet test Birko.Telemetry.Tests/Birko.Telemetry.Tests.csproj
```

## License

This project is licensed under the MIT License - see the [License.md](License.md) file for details.
