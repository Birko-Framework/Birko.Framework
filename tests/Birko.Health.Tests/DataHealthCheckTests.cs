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

    // ── InfluxDB ──

    [Fact]
    public void InfluxDbHealthCheck_EmptyUrl_ThrowsArgumentException()
    {
        var act = () => new InfluxDbHealthCheck("");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task InfluxDbHealthCheck_InvalidUrl_ReturnsUnhealthy()
    {
        var check = new InfluxDbHealthCheck("http://localhost:99999");

        var result = await check.CheckAsync();

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("failed");
    }

    // ── Vault ──

    [Fact]
    public void VaultHealthCheck_EmptyUrl_ThrowsArgumentException()
    {
        var act = () => new VaultHealthCheck("");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task VaultHealthCheck_InvalidUrl_ReturnsUnhealthy()
    {
        var check = new VaultHealthCheck("http://localhost:99999");

        var result = await check.CheckAsync();

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("failed");
    }

    // ── MQTT ──

    [Fact]
    public void MqttHealthCheck_EmptyHost_ThrowsArgumentException()
    {
        var act = () => new MqttHealthCheck("");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void MqttHealthCheck_NullPingFunc_ThrowsArgumentNullException()
    {
        var act = () => new MqttHealthCheck((Func<System.Threading.CancellationToken, Task<bool>>)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task MqttHealthCheck_CustomPingReturnsTrue_ReturnsHealthy()
    {
        var check = new MqttHealthCheck(ct => Task.FromResult(true));

        var result = await check.CheckAsync();

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data.Should().ContainKey("latencyMs");
    }

    [Fact]
    public async Task MqttHealthCheck_CustomPingReturnsFalse_ReturnsUnhealthy()
    {
        var check = new MqttHealthCheck(ct => Task.FromResult(false));

        var result = await check.CheckAsync();

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task MqttHealthCheck_InvalidHost_ReturnsUnhealthy()
    {
        var check = new MqttHealthCheck("invalid-host-that-does-not-exist.local", 1883);

        var result = await check.CheckAsync();

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    // ── WebSocket ──

    [Fact]
    public void WebSocketHealthCheck_EmptyUri_ThrowsArgumentException()
    {
        var act = () => new WebSocketHealthCheck("");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void WebSocketHealthCheck_NullPingFunc_ThrowsArgumentNullException()
    {
        var act = () => new WebSocketHealthCheck((Func<System.Threading.CancellationToken, Task<bool>>)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task WebSocketHealthCheck_CustomPingReturnsTrue_ReturnsHealthy()
    {
        var check = new WebSocketHealthCheck(ct => Task.FromResult(true));

        var result = await check.CheckAsync();

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data.Should().ContainKey("latencyMs");
    }

    [Fact]
    public async Task WebSocketHealthCheck_CustomPingReturnsFalse_ReturnsUnhealthy()
    {
        var check = new WebSocketHealthCheck(ct => Task.FromResult(false));

        var result = await check.CheckAsync();

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task WebSocketHealthCheck_InvalidUri_ReturnsUnhealthy()
    {
        var check = new WebSocketHealthCheck("ws://invalid-host-that-does-not-exist.local:9999");

        var result = await check.CheckAsync();

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("failed");
    }

    // ── TCP ──

    [Fact]
    public void TcpHealthCheck_EmptyHost_ThrowsArgumentException()
    {
        var act = () => new TcpHealthCheck("", 80);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TcpHealthCheck_InvalidPort_ThrowsArgumentOutOfRangeException()
    {
        var act = () => new TcpHealthCheck("localhost", 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void TcpHealthCheck_PortTooHigh_ThrowsArgumentOutOfRangeException()
    {
        var act = () => new TcpHealthCheck("localhost", 70000);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task TcpHealthCheck_InvalidHost_ReturnsUnhealthy()
    {
        var check = new TcpHealthCheck("invalid-host-that-does-not-exist.local", 9999);

        var result = await check.CheckAsync();

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("failed");
    }

    // ── SSE ──

    [Fact]
    public void SseHealthCheck_EmptyUrl_ThrowsArgumentException()
    {
        var act = () => new SseHealthCheck("");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task SseHealthCheck_InvalidUrl_ReturnsUnhealthy()
    {
        var check = new SseHealthCheck("http://invalid-host-that-does-not-exist.local:9999/events");

        var result = await check.CheckAsync();

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("failed");
    }

    // ── SMTP ──

    [Fact]
    public void SmtpHealthCheck_EmptyHost_ThrowsArgumentException()
    {
        var act = () => new SmtpHealthCheck("");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task SmtpHealthCheck_InvalidHost_ReturnsUnhealthy()
    {
        var check = new SmtpHealthCheck("invalid-host-that-does-not-exist.local", 25);

        var result = await check.CheckAsync();

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("failed");
    }
}
