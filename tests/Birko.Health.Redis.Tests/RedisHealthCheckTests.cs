using System;
using System.Threading.Tasks;
using Birko.Health;
using Birko.Health.Redis;
using FluentAssertions;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace Birko.Health.Redis.Tests;

/// <summary>
/// CR-M194: RedisHealthCheck had no dedicated .Tests sibling. It accepts an IConnectionMultiplexer
/// specifically so the multiplexer (and its IDatabase) can be mocked; these cover the
/// Healthy/Degraded/Unhealthy branching, the >100ms latency threshold, the data dictionary contents,
/// PingAsync faults, and the constructor null guards.
/// </summary>
public class RedisHealthCheckTests
{
    private static RedisHealthCheck BuildCheck(bool connected, TimeSpan? pingLatency = null, Exception? pingThrows = null)
    {
        var db = new Mock<IDatabase>();
        if (pingThrows != null)
        {
            db.Setup(d => d.PingAsync(It.IsAny<CommandFlags>())).ThrowsAsync(pingThrows);
        }
        else
        {
            db.Setup(d => d.PingAsync(It.IsAny<CommandFlags>())).ReturnsAsync(pingLatency ?? TimeSpan.Zero);
        }

        var conn = new Mock<IConnectionMultiplexer>();
        conn.Setup(c => c.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(db.Object);
        conn.SetupGet(c => c.IsConnected).Returns(connected);

        return new RedisHealthCheck(conn.Object);
    }

    [Fact]
    public async Task CheckAsync_ConnectedLowLatency_ReturnsHealthy()
    {
        var check = BuildCheck(connected: true, pingLatency: TimeSpan.FromMilliseconds(5));

        var result = await check.CheckAsync();

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data.Should().ContainKey("latencyMs");
        result.Data!["latencyMs"].Should().Be(5.0);
        result.Data!["isConnected"].Should().Be(true);
    }

    [Fact]
    public async Task CheckAsync_ConnectedHighLatency_ReturnsDegraded()
    {
        var check = BuildCheck(connected: true, pingLatency: TimeSpan.FromMilliseconds(150));

        var result = await check.CheckAsync();

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("slowly");
    }

    [Fact]
    public async Task CheckAsync_LatencyExactly100ms_IsHealthy()
    {
        // Boundary: the threshold is > 100ms, so exactly 100ms stays Healthy.
        var check = BuildCheck(connected: true, pingLatency: TimeSpan.FromMilliseconds(100));

        var result = await check.CheckAsync();

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task CheckAsync_NotConnected_ReturnsUnhealthy()
    {
        var check = BuildCheck(connected: false, pingLatency: TimeSpan.FromMilliseconds(5));

        var result = await check.CheckAsync();

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("not connected");
        result.Data!["isConnected"].Should().Be(false);
    }

    [Fact]
    public async Task CheckAsync_PingThrows_ReturnsUnhealthyWithException()
    {
        var ex = new RedisConnectionException(ConnectionFailureType.UnableToConnect, "boom");
        var check = BuildCheck(connected: true, pingThrows: ex);

        var result = await check.CheckAsync();

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("failed");
        result.Exception.Should().BeSameAs(ex);
    }

    [Fact]
    public void Constructor_NullFactory_ThrowsArgumentNullException()
    {
        var act = () => new RedisHealthCheck((Func<IConnectionMultiplexer>)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullConnection_ThrowsArgumentNullException()
    {
        var act = () => new RedisHealthCheck((IConnectionMultiplexer)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task CheckAsync_UsesFactoryOverload()
    {
        var db = new Mock<IDatabase>();
        db.Setup(d => d.PingAsync(It.IsAny<CommandFlags>())).ReturnsAsync(TimeSpan.FromMilliseconds(1));
        var conn = new Mock<IConnectionMultiplexer>();
        conn.Setup(c => c.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(db.Object);
        conn.SetupGet(c => c.IsConnected).Returns(true);

        var check = new RedisHealthCheck(() => conn.Object);
        var result = await check.CheckAsync();

        result.Status.Should().Be(HealthStatus.Healthy);
    }
}
