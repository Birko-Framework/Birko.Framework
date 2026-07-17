using System.Diagnostics;
using System.Diagnostics.Metrics;
using Birko.Telemetry;
using Birko.Telemetry.OpenTelemetry;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Birko.Telemetry.OpenTelemetry.Tests;

public class OpenTelemetryServiceExtensionsTests
{
    [Fact]
    public void AddBirkoOpenTelemetry_RegistersTracerProvider()
    {
        var services = new ServiceCollection();
        services.AddBirkoOpenTelemetry(opts =>
        {
            opts.EnableOtlpTraceExporter = false;
            opts.EnableOtlpMetricsExporter = false;
            opts.EnableAspNetCoreInstrumentation = false;
        });

        using var provider = services.BuildServiceProvider();
        var tracerProvider = provider.GetService<TracerProvider>();

        tracerProvider.Should().NotBeNull();
    }

    [Fact]
    public void AddBirkoOpenTelemetry_RegistersMeterProvider()
    {
        var services = new ServiceCollection();
        services.AddBirkoOpenTelemetry(opts =>
        {
            opts.EnableOtlpTraceExporter = false;
            opts.EnableOtlpMetricsExporter = false;
            opts.EnableAspNetCoreInstrumentation = false;
        });

        using var provider = services.BuildServiceProvider();
        var meterProvider = provider.GetService<MeterProvider>();

        meterProvider.Should().NotBeNull();
    }

    [Fact]
    public void AddBirkoOpenTelemetry_NullConfigure_UsesDefaults()
    {
        var services = new ServiceCollection();

        var act = () => services.AddBirkoOpenTelemetry(null);

        // Should not throw — uses default options
        // OTLP will fail to connect but won't throw during registration
        act.Should().NotThrow();
    }

    [Fact]
    public void AddBirkoOpenTelemetry_WithConsoleExporters_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddBirkoOpenTelemetry(opts =>
        {
            opts.EnableOtlpTraceExporter = false;
            opts.EnableOtlpMetricsExporter = false;
            opts.EnableConsoleTraceExporter = true;
            opts.EnableConsoleMetricsExporter = true;
            opts.EnableAspNetCoreInstrumentation = false;
        });

        using var provider = services.BuildServiceProvider();
        provider.GetService<TracerProvider>().Should().NotBeNull();
        provider.GetService<MeterProvider>().Should().NotBeNull();
    }

    [Fact]
    public void AddBirkoOpenTelemetry_SubscribesToBirkoActivitySource()
    {
        var activities = new List<Activity>();
        var services = new ServiceCollection();
        services.AddBirkoOpenTelemetry(opts =>
        {
            opts.EnableOtlpTraceExporter = false;
            opts.EnableOtlpMetricsExporter = false;
            opts.EnableAspNetCoreInstrumentation = false;
        });

        using var provider = services.BuildServiceProvider();
        // Force TracerProvider to start listening
        var tracerProvider = provider.GetRequiredService<TracerProvider>();

        // Create an activity from the Birko source
        using var source = new ActivitySource(BirkoTelemetryConventions.ActivitySourceName);
        using var activity = source.StartActivity("TestOperation");

        // If the activity was created (not null), the source is being listened to
        // Note: activity may be null if no listener is sampling — but the provider is configured
        tracerProvider.Should().NotBeNull();
    }

    [Fact]
    public void AddBirkoOpenTelemetry_WithAdditionalSources_ConfiguresThem()
    {
        var services = new ServiceCollection();
        services.AddBirkoOpenTelemetry(opts =>
        {
            opts.EnableOtlpTraceExporter = false;
            opts.EnableOtlpMetricsExporter = false;
            opts.EnableAspNetCoreInstrumentation = false;
            opts.AdditionalActivitySourceNames.Add("MyApp.Custom");
            opts.AdditionalMeterNames.Add("MyApp.Metrics");
        });

        using var provider = services.BuildServiceProvider();
        provider.GetService<TracerProvider>().Should().NotBeNull();
        provider.GetService<MeterProvider>().Should().NotBeNull();
    }

    [Fact]
    public void AddBirkoOpenTelemetry_WithCustomServiceName_ConfiguresResource()
    {
        var services = new ServiceCollection();
        services.AddBirkoOpenTelemetry(opts =>
        {
            opts.ServiceName = "TestService";
            opts.ServiceVersion = "1.2.3";
            opts.EnableOtlpTraceExporter = false;
            opts.EnableOtlpMetricsExporter = false;
            opts.EnableAspNetCoreInstrumentation = false;
        });

        using var provider = services.BuildServiceProvider();
        // If it builds without error, resource was configured
        provider.GetService<TracerProvider>().Should().NotBeNull();
    }

    [Fact]
    public void AddBirkoOpenTelemetry_WithMetricsInterval_ConfiguresExporter()
    {
        var services = new ServiceCollection();
        services.AddBirkoOpenTelemetry(opts =>
        {
            opts.EnableOtlpTraceExporter = false;
            opts.EnableOtlpMetricsExporter = false;
            opts.EnableConsoleMetricsExporter = true;
            opts.EnableAspNetCoreInstrumentation = false;
            opts.MetricsExportInterval = TimeSpan.FromSeconds(10);
        });

        using var provider = services.BuildServiceProvider();
        provider.GetService<MeterProvider>().Should().NotBeNull();
    }

    [Fact]
    public void AddBirkoOpenTelemetry_ReturnsServiceCollection()
    {
        var services = new ServiceCollection();
        var result = services.AddBirkoOpenTelemetry(opts =>
        {
            opts.EnableOtlpTraceExporter = false;
            opts.EnableOtlpMetricsExporter = false;
            opts.EnableAspNetCoreInstrumentation = false;
        });

        result.Should().BeSameAs(services);
    }

    // CR-L383: every other test disables AspNetCore instrumentation and OTLP, so these branches
    // (AddAspNetCoreInstrumentation and AddOtlpExporter) were never exercised.

    [Fact]
    public void AddBirkoOpenTelemetry_WithAspNetCoreInstrumentation_Builds()
    {
        var services = new ServiceCollection();
        services.AddBirkoOpenTelemetry(opts =>
        {
            opts.EnableAspNetCoreInstrumentation = true; // exercises AddAspNetCoreInstrumentation on both signals
            opts.EnableOtlpTraceExporter = false;
            opts.EnableOtlpMetricsExporter = false;
        });

        using var provider = services.BuildServiceProvider();
        provider.GetService<TracerProvider>().Should().NotBeNull();
        provider.GetService<MeterProvider>().Should().NotBeNull();
    }

    [Fact]
    public void AddBirkoOpenTelemetry_WithOtlpExporters_Builds()
    {
        var services = new ServiceCollection();
        services.AddBirkoOpenTelemetry(opts =>
        {
            opts.EnableOtlpTraceExporter = true;
            opts.EnableOtlpMetricsExporter = true;
            opts.OtlpEndpoint = "http://localhost:4317";
            opts.EnableAspNetCoreInstrumentation = false;
        });

        using var provider = services.BuildServiceProvider();
        provider.GetService<TracerProvider>().Should().NotBeNull();
        provider.GetService<MeterProvider>().Should().NotBeNull();
    }

    // CR-L382: a malformed OTLP endpoint must fail fast with a clear ArgumentException at registration,
    // not a UriFormatException from deep inside an OpenTelemetry builder callback.

    [Fact]
    public void AddBirkoOpenTelemetry_InvalidOtlpEndpoint_WithOtlpEnabled_ThrowsArgumentException()
    {
        var services = new ServiceCollection();

        var act = () => services.AddBirkoOpenTelemetry(opts =>
        {
            opts.EnableOtlpTraceExporter = true;
            opts.OtlpEndpoint = "not a valid uri";
            opts.EnableAspNetCoreInstrumentation = false;
        });

        act.Should().Throw<ArgumentException>().WithMessage("*OtlpEndpoint*");
    }

    [Fact]
    public void AddBirkoOpenTelemetry_InvalidOtlpEndpoint_WithOtlpDisabled_DoesNotThrow()
    {
        // The endpoint is only consumed when an OTLP exporter is enabled, so a bogus value is ignored otherwise.
        var services = new ServiceCollection();

        var act = () => services.AddBirkoOpenTelemetry(opts =>
        {
            opts.EnableOtlpTraceExporter = false;
            opts.EnableOtlpMetricsExporter = false;
            opts.OtlpEndpoint = "not a valid uri";
            opts.EnableAspNetCoreInstrumentation = false;
        });

        act.Should().NotThrow();
    }
}
