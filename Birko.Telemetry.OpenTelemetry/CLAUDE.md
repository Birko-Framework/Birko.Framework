# Birko.Telemetry.OpenTelemetry

## Overview
OpenTelemetry SDK integration for Birko.Telemetry. Auto-wires Birko meters and activity sources to OTLP and Console exporters via a single `AddBirkoOpenTelemetry()` DI call.

## Project Location
`C:\Source\Birko.Telemetry.OpenTelemetry\` (shared project, .shproj)

## Components

- **BirkoOpenTelemetryOptions.cs** — Configuration: OTLP endpoint, exporter toggles (OTLP/Console for traces/metrics), service name/version, additional meter/source names, ASP.NET Core instrumentation toggle, metrics export interval
- **OpenTelemetryServiceExtensions.cs** — `AddBirkoOpenTelemetry()` extension on `IServiceCollection`. Configures `TracerProvider` + `MeterProvider` with Birko convention names, optional ASP.NET Core instrumentation, OTLP and Console exporters

## How It Works

The OpenTelemetry .NET SDK subscribes to `System.Diagnostics.Metrics.Meter` and `System.Diagnostics.ActivitySource` instances by name. This project calls `AddMeter("Birko.Data.Store")` and `AddSource("Birko.Data.Store")` — the names defined in `BirkoTelemetryConventions` — so all Birko store instrumentation flows to the configured exporters.

## Dependencies
- `Birko.Telemetry` — `BirkoTelemetryConventions` (meter/activity source names)
- NuGet packages (added by consuming project, NOT by .shproj):
  - `OpenTelemetry` (1.15.0+)
  - `OpenTelemetry.Extensions.Hosting`
  - `OpenTelemetry.Exporter.OpenTelemetryProtocol`
  - `OpenTelemetry.Exporter.Console` (optional, for dev)
  - `OpenTelemetry.Instrumentation.AspNetCore` (optional, for HTTP tracing)

## Maintenance
- If `BirkoTelemetryConventions` adds new meter/source names, add corresponding `AddMeter`/`AddSource` calls in `OpenTelemetryServiceExtensions`
- Keep OpenTelemetry package versions aligned across framework and test projects
