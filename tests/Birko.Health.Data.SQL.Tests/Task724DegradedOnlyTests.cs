using System;
using System.Threading.Tasks;
using Birko.Health;
using Birko.Health.Data.SQL;
using FluentAssertions;
using Xunit;

namespace Birko.Health.Data.SQL.Tests;

/// <summary>
/// TASK-724. Measured before the fix: <c>SchemaDriftHealthCheck</c>, documented "Degraded, never Unhealthy" (reachability
/// is <c>SqlHealthCheck</c>'s question), answered Unhealthy when its connector factory returned null and when the check
/// itself threw — so a drift diagnostic could pull an instance out of a load balancer.
/// </summary>
public class Task724DegradedOnlyTests
{
    [Fact]
    public async Task No_connector_is_Degraded()
    {
        var result = await new SchemaDriftHealthCheck(() => null!, new[] { typeof(object) }).CheckAsync();

        result.Status.Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public async Task A_check_that_throws_is_Degraded()
    {
        var result = await new SchemaDriftHealthCheck(() => throw new InvalidOperationException("database unreachable"), new[] { typeof(object) }).CheckAsync();

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("database unreachable");
    }
}
