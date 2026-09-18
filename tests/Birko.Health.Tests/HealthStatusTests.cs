using Birko.Health;
using FluentAssertions;
using Xunit;

namespace Birko.Health.Tests;

public class HealthStatusTests
{
    [Fact]
    public void HealthStatus_Ordering_HealthyLessThanDegraded()
    {
        ((int)HealthStatus.Healthy).Should().BeLessThan((int)HealthStatus.Degraded);
    }

    [Fact]
    public void HealthStatus_Ordering_DegradedLessThanUnhealthy()
    {
        ((int)HealthStatus.Degraded).Should().BeLessThan((int)HealthStatus.Unhealthy);
    }

    [Fact]
    public void HealthStatus_Values()
    {
        HealthStatus.Healthy.Should().Be((HealthStatus)0);
        HealthStatus.Degraded.Should().Be((HealthStatus)1);
        HealthStatus.Unhealthy.Should().Be((HealthStatus)2);
    }
}
