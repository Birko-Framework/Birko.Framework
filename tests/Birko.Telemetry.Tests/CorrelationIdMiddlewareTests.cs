using Birko.Telemetry;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Xunit;

namespace Birko.Telemetry.Tests;

public class CorrelationIdMiddlewareTests
{
    private static IOptions<BirkoTelemetryOptions> CreateOptions(Action<BirkoTelemetryOptions>? configure = null)
    {
        var options = new BirkoTelemetryOptions();
        configure?.Invoke(options);
        return Options.Create(options);
    }

    [Fact]
    public async Task InvokeAsync_GeneratesCorrelationId_WhenNotInRequest()
    {
        var context = new DefaultHttpContext();
        var middleware = new CorrelationIdMiddleware(
            next: _ => Task.CompletedTask,
            options: CreateOptions());

        await middleware.InvokeAsync(context);

        context.Response.Headers.Should().ContainKey("X-Correlation-Id");
        var correlationId = context.Response.Headers["X-Correlation-Id"].ToString();
        Guid.TryParse(correlationId, out _).Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_UsesExistingCorrelationId_WhenInRequest()
    {
        var context = new DefaultHttpContext();
        var expectedId = "my-custom-id-123";
        context.Request.Headers["X-Correlation-Id"] = expectedId;

        var middleware = new CorrelationIdMiddleware(
            next: _ => Task.CompletedTask,
            options: CreateOptions());

        await middleware.InvokeAsync(context);

        context.Response.Headers["X-Correlation-Id"].ToString().Should().Be(expectedId);
    }

    [Fact]
    public async Task InvokeAsync_UsesCustomHeaderName()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Request-Id"] = "req-456";

        var middleware = new CorrelationIdMiddleware(
            next: _ => Task.CompletedTask,
            options: CreateOptions(o => o.CorrelationIdHeaderName = "X-Request-Id"));

        await middleware.InvokeAsync(context);

        context.Response.Headers["X-Request-Id"].ToString().Should().Be("req-456");
    }

    [Fact]
    public async Task InvokeAsync_SkipsWhenDisabled()
    {
        var context = new DefaultHttpContext();
        var middleware = new CorrelationIdMiddleware(
            next: _ => Task.CompletedTask,
            options: CreateOptions(o => o.EnableCorrelationId = false));

        await middleware.InvokeAsync(context);

        context.Response.Headers.Should().NotContainKey("X-Correlation-Id");
    }

    [Fact]
    public async Task InvokeAsync_SetsActivityBaggage()
    {
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(activityListener);

        using var activitySource = new ActivitySource("Test");
        using var activity = activitySource.StartActivity("TestOp");

        var context = new DefaultHttpContext();
        context.Request.Headers["X-Correlation-Id"] = "baggage-test-id";

        var middleware = new CorrelationIdMiddleware(
            next: _ => Task.CompletedTask,
            options: CreateOptions());

        await middleware.InvokeAsync(context);

        activity?.GetBaggageItem("correlation-id").Should().Be("baggage-test-id");
    }

    [Fact]
    public async Task InvokeAsync_CallsNext()
    {
        var nextCalled = false;
        var context = new DefaultHttpContext();
        var middleware = new CorrelationIdMiddleware(
            next: _ => { nextCalled = true; return Task.CompletedTask; },
            options: CreateOptions());

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }
}
