# Birko.Telemetry.OpenTelemetry

OpenTelemetry SDK integration for Birko.Telemetry. Auto-wires Birko store metrics and distributed tracing to OTLP collectors, Grafana, Jaeger, or any OpenTelemetry-compatible backend.

## Features

- One-line DI setup via `AddBirkoOpenTelemetry()`
- Auto-subscribes to all Birko.Telemetry meters and activity sources
- OTLP exporter (gRPC or HTTP/protobuf) for traces and metrics
- Console exporter for development/debugging
- ASP.NET Core HTTP request instrumentation (optional)
- Configurable service name/version for resource identification
- Extensible: add your own meters and activity sources alongside Birko's

## Usage

```csharp
// In Program.cs or Startup.cs
services.AddBirkoTelemetry();           // Birko instrumentation layer
services.AddBirkoOpenTelemetry(opts =>  // OpenTelemetry export
{
    opts.ServiceName = "MyApp";
    opts.ServiceVersion = "1.0.0";
    opts.OtlpEndpoint = "http://otel-collector:4317";
});
```

### Development (Console output)

```csharp
services.AddBirkoOpenTelemetry(opts =>
{
    opts.ServiceName = "MyApp.Dev";
    opts.EnableOtlpTraceExporter = false;
    opts.EnableOtlpMetricsExporter = false;
    opts.EnableConsoleTraceExporter = true;
    opts.EnableConsoleMetricsExporter = true;
});
```

### Custom meters

```csharp
services.AddBirkoOpenTelemetry(opts =>
{
    opts.AdditionalMeterNames.Add("MyApp.CustomMeter");
    opts.AdditionalActivitySourceNames.Add("MyApp.CustomTracing");
});
```

## Required NuGet Packages

The consuming project must add these packages (shared projects cannot declare NuGet dependencies):

```xml
<PackageReference Include="OpenTelemetry" Version="1.15.0" />
<PackageReference Include="OpenTelemetry.Extensions.Hosting" Version="1.15.0" />
<PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.15.0" />
<PackageReference Include="OpenTelemetry.Exporter.Console" Version="1.15.0" />
<PackageReference Include="OpenTelemetry.Instrumentation.AspNetCore" Version="1.15.1" />
```

## What Gets Exported

| Signal | Source | Instruments |
|--------|--------|-------------|
| **Metrics** | `Birko.Data.Store` meter | `birko.store.operation.duration` (histogram), `birko.store.operation.count` (counter), `birko.store.operation.errors` (counter) |
| **Traces** | `Birko.Data.Store` activity source | Per-operation spans with store type, entity type, operation, tenant tags |
| **Traces** | ASP.NET Core (optional) | HTTP request spans |
| **Metrics** | ASP.NET Core (optional) | HTTP request duration, active requests |

## License

MIT License - see [License.md](License.md)
