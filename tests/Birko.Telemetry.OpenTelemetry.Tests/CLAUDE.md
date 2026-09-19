# Birko.Telemetry.OpenTelemetry.Tests

## Overview
Unit tests for Birko.Telemetry.OpenTelemetry integration.

## Project Location
`tests/Birko.Telemetry.OpenTelemetry.Tests/` (xUnit test project, .csproj)

## Test Classes
- **BirkoOpenTelemetryOptionsTests** — Default values, property mutability
- **OpenTelemetryServiceExtensionsTests** — DI registration, TracerProvider/MeterProvider resolution, console/OTLP exporters, additional sources, service resource, chaining

## Dependencies
- Birko.Telemetry, Birko.Telemetry.OpenTelemetry (shared project imports)
- OpenTelemetry 1.15.0, OpenTelemetry.Extensions.Hosting, OTLP/Console/InMemory exporters
- xUnit 2.9.3, FluentAssertions 7.0.0

## Running Tests
```bash
dotnet test Birko.Telemetry.OpenTelemetry.Tests/
```
