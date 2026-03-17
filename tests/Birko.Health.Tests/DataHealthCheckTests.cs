using System;
using System.Threading.Tasks;
using Birko.Health;
using Birko.Health.Data;
using FluentAssertions;
using Xunit;

namespace Birko.Health.Tests;

public class DataHealthCheckTests
{
    [Fact]
    public void SqlHealthCheck_NullFactory_ThrowsArgumentNullException()
    {
        var act = () => new SqlHealthCheck(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ElasticSearchHealthCheck_EmptyUrl_ThrowsArgumentException()
    {
        var act = () => new ElasticSearchHealthCheck("");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task ElasticSearchHealthCheck_InvalidUrl_ReturnsUnhealthy()
    {
        var check = new ElasticSearchHealthCheck("http://localhost:99999");

        var result = await check.CheckAsync();

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("failed");
    }

    [Fact]
    public async Task MongoDbHealthCheck_InvalidHost_ReturnsUnhealthy()
    {
        var check = new MongoDbHealthCheck("invalid-host-that-does-not-exist.local", 27017);

        var result = await check.CheckAsync();

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public void MongoDbHealthCheck_NullPingFunc_ThrowsArgumentNullException()
    {
        var act = () => new MongoDbHealthCheck((Func<System.Threading.CancellationToken, Task<bool>>)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task MongoDbHealthCheck_CustomPingReturnsTrue_ReturnsHealthy()
    {
        var check = new MongoDbHealthCheck(ct => Task.FromResult(true));

        var result = await check.CheckAsync();

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task MongoDbHealthCheck_CustomPingReturnsFalse_ReturnsUnhealthy()
    {
        var check = new MongoDbHealthCheck(ct => Task.FromResult(false));

        var result = await check.CheckAsync();

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public void RavenDbHealthCheck_EmptyUrl_ThrowsArgumentException()
    {
        var act = () => new RavenDbHealthCheck("");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task RavenDbHealthCheck_InvalidUrl_ReturnsUnhealthy()
    {
        var check = new RavenDbHealthCheck("http://localhost:99999");

        var result = await check.CheckAsync();

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }
}
