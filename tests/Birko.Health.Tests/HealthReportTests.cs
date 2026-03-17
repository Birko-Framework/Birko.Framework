using System;
using System.Collections.Generic;
using Birko.Health;
using FluentAssertions;
using Xunit;

namespace Birko.Health.Tests;

public class HealthReportTests
{
    [Fact]
    public void Status_AllHealthy_ReturnsHealthy()
    {
        var entries = new Dictionary<string, HealthCheckResult>
        {
            ["a"] = HealthCheckResult.Healthy(),
            ["b"] = HealthCheckResult.Healthy()
        };

        var report = new HealthReport(entries, TimeSpan.FromMilliseconds(10));

        report.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public void Status_OneDegraded_ReturnsDegraded()
    {
        var entries = new Dictionary<string, HealthCheckResult>
        {
            ["a"] = HealthCheckResult.Healthy(),
            ["b"] = HealthCheckResult.Degraded("slow")
        };

        var report = new HealthReport(entries, TimeSpan.FromMilliseconds(10));

        report.Status.Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public void Status_OneUnhealthy_ReturnsUnhealthy()
    {
        var entries = new Dictionary<string, HealthCheckResult>
        {
            ["a"] = HealthCheckResult.Healthy(),
            ["b"] = HealthCheckResult.Degraded("slow"),
            ["c"] = HealthCheckResult.Unhealthy("down")
        };

        var report = new HealthReport(entries, TimeSpan.FromMilliseconds(10));

        report.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public void Status_Empty_ReturnsHealthy()
    {
        var report = new HealthReport(new Dictionary<string, HealthCheckResult>(), TimeSpan.Zero);

        report.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public void TotalDuration_IsSet()
    {
        var report = new HealthReport(new Dictionary<string, HealthCheckResult>(), TimeSpan.FromSeconds(1));

        report.TotalDuration.Should().Be(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Constructor_NullEntries_ThrowsArgumentNullException()
    {
        var act = () => new HealthReport(null!, TimeSpan.Zero);

        act.Should().Throw<ArgumentNullException>();
    }
}
