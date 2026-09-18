using System;
using System.Collections.Generic;
using Birko.Health;
using FluentAssertions;
using Xunit;

namespace Birko.Health.Tests;

public class HealthCheckResultTests
{
    [Fact]
    public void Healthy_DefaultValues()
    {
        var result = HealthCheckResult.Healthy();

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().BeNull();
        result.Exception.Should().BeNull();
        result.Data.Should().BeNull();
        result.Duration.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Healthy_WithDescription()
    {
        var result = HealthCheckResult.Healthy("All good");

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Be("All good");
    }

    [Fact]
    public void Healthy_WithData()
    {
        var data = new Dictionary<string, object> { ["key"] = "value" };
        var result = HealthCheckResult.Healthy("OK", data);

        result.Data.Should().ContainKey("key");
    }

    [Fact]
    public void Degraded_HasCorrectStatus()
    {
        var result = HealthCheckResult.Degraded("Slow");

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Be("Slow");
    }

    [Fact]
    public void Degraded_WithException()
    {
        var ex = new TimeoutException("timeout");
        var result = HealthCheckResult.Degraded("Slow", ex);

        result.Exception.Should().Be(ex);
    }

    [Fact]
    public void Unhealthy_HasCorrectStatus()
    {
        var result = HealthCheckResult.Unhealthy("Down");

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Be("Down");
    }

    [Fact]
    public void Unhealthy_WithException()
    {
        var ex = new InvalidOperationException("connection refused");
        var result = HealthCheckResult.Unhealthy("Failed", ex);

        result.Exception.Should().Be(ex);
    }

    [Fact]
    public void WithDuration_SetsDuration()
    {
        var result = HealthCheckResult.Healthy("OK");
        var withDuration = result.WithDuration(TimeSpan.FromMilliseconds(42));

        withDuration.Duration.Should().Be(TimeSpan.FromMilliseconds(42));
        withDuration.Status.Should().Be(HealthStatus.Healthy);
        withDuration.Description.Should().Be("OK");
    }
}
