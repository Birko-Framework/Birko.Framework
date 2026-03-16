# Birko.Telemetry.OpenTelemetry.Tests

Unit tests for the Birko.Telemetry.OpenTelemetry integration.

## Test Coverage

- **BirkoOpenTelemetryOptionsTests** — Default values, property mutability
- **OpenTelemetryServiceExtensionsTests** — DI registration, TracerProvider/MeterProvider resolution, console/OTLP exporters, additional sources, service resource, metrics interval, chaining

## Running Tests

```bash
dotnet test Birko.Telemetry.OpenTelemetry.Tests/
```

## Test Framework

- xUnit 2.9.3
- FluentAssertions 7.0.0

## License

MIT License - see [License.md](License.md)
