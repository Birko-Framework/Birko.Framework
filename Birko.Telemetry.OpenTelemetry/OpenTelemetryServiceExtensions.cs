using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Birko.Telemetry.OpenTelemetry;

/// <summary>
/// Extension methods for configuring OpenTelemetry with Birko telemetry instrumentation.
/// </summary>
public static class OpenTelemetryServiceExtensions
{
    /// <summary>
    /// Adds OpenTelemetry tracing and metrics configured to listen to Birko.Telemetry
    /// instrumentation (meters and activity sources).
    /// <para>
    /// The consuming project must reference:
    /// OpenTelemetry, OpenTelemetry.Extensions.Hosting,
    /// OpenTelemetry.Exporter.OpenTelemetryProtocol,
    /// OpenTelemetry.Exporter.Console (optional).
    /// </para>
    /// </summary>
    public static IServiceCollection AddBirkoOpenTelemetry(
        this IServiceCollection services,
        Action<BirkoOpenTelemetryOptions>? configure = null)
    {
        var options = new BirkoOpenTelemetryOptions();
        configure?.Invoke(options);

        // CR-L382: parse the OTLP endpoint up front so a malformed value fails fast with a clear message
        // here, rather than surfacing a UriFormatException from deep inside an OpenTelemetry builder callback
        // at provider-build time. Only relevant when an OTLP exporter is actually enabled.
        Uri? otlpEndpoint = null;
        if (options.EnableOtlpTraceExporter || options.EnableOtlpMetricsExporter)
        {
            if (!Uri.TryCreate(options.OtlpEndpoint, UriKind.Absolute, out otlpEndpoint))
            {
                throw new ArgumentException(
                    $"BirkoOpenTelemetryOptions.OtlpEndpoint '{options.OtlpEndpoint}' is not a valid absolute URI.",
                    nameof(configure));
            }
        }

        services.AddOpenTelemetry()
            .ConfigureResource(resource =>
            {
                resource.AddService(
                    serviceName: options.ServiceName,
                    serviceVersion: options.ServiceVersion);
            })
            .WithTracing(tracing =>
            {
                // Birko activity source
                tracing.AddSource(BirkoTelemetryConventions.ActivitySourceName);

                foreach (var source in options.AdditionalActivitySourceNames)
                {
                    tracing.AddSource(source);
                }

                if (options.EnableAspNetCoreInstrumentation)
                {
                    tracing.AddAspNetCoreInstrumentation();
                }

                if (options.EnableOtlpTraceExporter)
                {
                    tracing.AddOtlpExporter(otlp =>
                    {
                        otlp.Endpoint = otlpEndpoint!; // validated above when EnableOtlpTraceExporter is set
                    });
                }

                if (options.EnableConsoleTraceExporter)
                {
                    tracing.AddConsoleExporter();
                }
            })
            .WithMetrics(metrics =>
            {
                // Birko meter
                metrics.AddMeter(BirkoTelemetryConventions.MeterName);

                foreach (var meter in options.AdditionalMeterNames)
                {
                    metrics.AddMeter(meter);
                }

                if (options.EnableAspNetCoreInstrumentation)
                {
                    metrics.AddAspNetCoreInstrumentation();
                }

                if (options.EnableOtlpMetricsExporter)
                {
                    metrics.AddOtlpExporter((otlp, metricReader) =>
                    {
                        otlp.Endpoint = otlpEndpoint!; // validated above when EnableOtlpMetricsExporter is set
                        if (options.MetricsExportInterval.HasValue)
                        {
                            metricReader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds =
                                (int)options.MetricsExportInterval.Value.TotalMilliseconds;
                        }
                    });
                }

                if (options.EnableConsoleMetricsExporter)
                {
                    metrics.AddConsoleExporter();
                }
            });

        return services;
    }
}
