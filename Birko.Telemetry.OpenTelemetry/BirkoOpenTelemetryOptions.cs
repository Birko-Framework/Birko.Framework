namespace Birko.Telemetry.OpenTelemetry;

/// <summary>
/// Configuration options for Birko OpenTelemetry integration.
/// </summary>
public class BirkoOpenTelemetryOptions
{
    /// <summary>
    /// OTLP endpoint URI. Default: "http://localhost:4317" (gRPC).
    /// Set to "http://localhost:4318" for HTTP/protobuf.
    /// </summary>
    public string OtlpEndpoint { get; set; } = "http://localhost:4317";

    /// <summary>Enable OTLP exporter for traces. Default: true.</summary>
    public bool EnableOtlpTraceExporter { get; set; } = true;

    /// <summary>Enable OTLP exporter for metrics. Default: true.</summary>
    public bool EnableOtlpMetricsExporter { get; set; } = true;

    /// <summary>Enable console exporter for traces (dev/debug). Default: false.</summary>
    public bool EnableConsoleTraceExporter { get; set; }

    /// <summary>Enable console exporter for metrics (dev/debug). Default: false.</summary>
    public bool EnableConsoleMetricsExporter { get; set; }

    /// <summary>
    /// Service name reported to OpenTelemetry resource.
    /// Default: "Birko.Application".
    /// </summary>
    public string ServiceName { get; set; } = "Birko.Application";

    /// <summary>
    /// Service version reported to OpenTelemetry resource.
    /// Default: null (omitted).
    /// </summary>
    public string? ServiceVersion { get; set; }

    /// <summary>
    /// Additional meter names to subscribe to beyond the Birko defaults.
    /// The Birko.Data.Store meter is always included.
    /// </summary>
    public List<string> AdditionalMeterNames { get; set; } = new();

    /// <summary>
    /// Additional activity source names to subscribe to beyond the Birko defaults.
    /// The Birko.Data.Store activity source is always included.
    /// </summary>
    public List<string> AdditionalActivitySourceNames { get; set; } = new();

    /// <summary>
    /// Metrics export interval. Default: null (uses OTel SDK default of 60s).
    /// </summary>
    public TimeSpan? MetricsExportInterval { get; set; }

    /// <summary>
    /// Enable ASP.NET Core instrumentation for HTTP request tracing and metrics.
    /// Default: true.
    /// </summary>
    public bool EnableAspNetCoreInstrumentation { get; set; } = true;
}
