using Birko.Telemetry;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Birko.Telemetry.Tests;

/// <summary>
/// CR-L379: the DI/middleware registration extensions (AddBirkoTelemetry / UseBirkoCorrelationId) had no
/// tests. They are the primary way consumers wire telemetry into an app.
/// </summary>
public class TelemetryServiceExtensionsTests
{
    [Fact]
    public void AddBirkoTelemetry_RegistersOptions_WithDefaults()
    {
        var services = new ServiceCollection();

        services.AddBirkoTelemetry();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<BirkoTelemetryOptions>>().Value;
        options.EnableCorrelationId.Should().BeTrue(); // default
        options.CorrelationIdHeaderName.Should().Be(BirkoTelemetryConventions.DefaultCorrelationIdHeader);
    }

    [Fact]
    public void AddBirkoTelemetry_HonorsConfigureDelegate()
    {
        var services = new ServiceCollection();

        services.AddBirkoTelemetry(o =>
        {
            o.EnableCorrelationId = false;
            o.CorrelationIdHeaderName = "X-Trace-Id";
        });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<BirkoTelemetryOptions>>().Value;
        options.EnableCorrelationId.Should().BeFalse();
        options.CorrelationIdHeaderName.Should().Be("X-Trace-Id");
    }

    [Fact]
    public void AddBirkoTelemetry_ReturnsSameServiceCollection()
    {
        var services = new ServiceCollection();

        var result = services.AddBirkoTelemetry();

        result.Should().BeSameAs(services);
    }

    [Fact]
    public void UseBirkoCorrelationId_RegistersMiddleware_AndReturnsSameBuilder()
    {
        var services = new ServiceCollection();
        services.AddBirkoTelemetry();
        using var provider = services.BuildServiceProvider();
        var app = new ApplicationBuilder(provider);

        var result = app.UseBirkoCorrelationId();

        result.Should().BeSameAs(app);
        // The built pipeline resolves without throwing (the middleware was registered).
        var pipeline = app.Build();
        pipeline.Should().NotBeNull();
    }
}
