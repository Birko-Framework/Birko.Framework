using Birko.Telemetry.OpenTelemetry;
using FluentAssertions;

namespace Birko.Telemetry.OpenTelemetry.Tests;

public class BirkoOpenTelemetryOptionsTests
{
    [Fact]
    public void Defaults_ShouldBeCorrect()
    {
        var options = new BirkoOpenTelemetryOptions();

        options.OtlpEndpoint.Should().Be("http://localhost:4317");
        options.ServiceName.Should().Be("Birko.Application");
        options.ServiceVersion.Should().BeNull();
        options.EnableOtlpTraceExporter.Should().BeTrue();
        options.EnableOtlpMetricsExporter.Should().BeTrue();
        options.EnableConsoleTraceExporter.Should().BeFalse();
        options.EnableConsoleMetricsExporter.Should().BeFalse();
        options.EnableAspNetCoreInstrumentation.Should().BeFalse("CR-M254: AspNetCore instrumentation is opt-in (requires the optional AspNetCore package), consistent with the console-exporter toggles");
        options.MetricsExportInterval.Should().BeNull();
        options.AdditionalMeterNames.Should().BeEmpty();
        options.AdditionalActivitySourceNames.Should().BeEmpty();
    }

    [Fact]
    public void Properties_ShouldBeMutable()
    {
        var options = new BirkoOpenTelemetryOptions
        {
            OtlpEndpoint = "http://collector:4318",
            ServiceName = "TestService",
            ServiceVersion = "2.0.0",
            EnableOtlpTraceExporter = false,
            EnableOtlpMetricsExporter = false,
            EnableConsoleTraceExporter = true,
            EnableConsoleMetricsExporter = true,
            EnableAspNetCoreInstrumentation = false,
            MetricsExportInterval = TimeSpan.FromSeconds(30),
        };
        options.AdditionalMeterNames.Add("Custom.Meter");
        options.AdditionalActivitySourceNames.Add("Custom.Source");

        options.OtlpEndpoint.Should().Be("http://collector:4318");
        options.ServiceName.Should().Be("TestService");
        options.ServiceVersion.Should().Be("2.0.0");
        options.EnableOtlpTraceExporter.Should().BeFalse();
        options.EnableOtlpMetricsExporter.Should().BeFalse();
        options.EnableConsoleTraceExporter.Should().BeTrue();
        options.EnableConsoleMetricsExporter.Should().BeTrue();
        options.EnableAspNetCoreInstrumentation.Should().BeFalse();
        options.MetricsExportInterval.Should().Be(TimeSpan.FromSeconds(30));
        options.AdditionalMeterNames.Should().Contain("Custom.Meter");
        options.AdditionalActivitySourceNames.Should().Contain("Custom.Source");
    }
}
