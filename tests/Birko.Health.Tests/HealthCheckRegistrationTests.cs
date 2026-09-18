using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.Health;
using FluentAssertions;
using Xunit;

namespace Birko.Health.Tests;

public class HealthCheckRegistrationTests
{
    [Fact]
    public void Constructor_WithFactory_SetsProperties()
    {
        var reg = new HealthCheckRegistration("test", () => new StubCheck(), new[] { "db", "live" }, TimeSpan.FromSeconds(5));

        reg.Name.Should().Be("test");
        reg.Tags.Should().Contain("db").And.Contain("live");
        reg.Timeout.Should().Be(TimeSpan.FromSeconds(5));
        reg.TimeoutStatus.Should().Be(HealthStatus.Unhealthy);
        reg.Factory.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithInstance_SetsFactory()
    {
        var check = new StubCheck();
        var reg = new HealthCheckRegistration("test", check);

        reg.Factory().Should().BeSameAs(check);
    }

    [Fact]
    public void Constructor_NullName_ThrowsArgumentException()
    {
        var act = () => new HealthCheckRegistration("", () => new StubCheck());

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_NullFactory_ThrowsArgumentNullException()
    {
        var act = () => new HealthCheckRegistration("test", (Func<IHealthCheck>)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullInstance_ThrowsArgumentNullException()
    {
        var act = () => new HealthCheckRegistration("test", (IHealthCheck)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_DefaultTags_IsEmpty()
    {
        var reg = new HealthCheckRegistration("test", () => new StubCheck());

        reg.Tags.Should().BeEmpty();
    }

    private class StubCheck : IHealthCheck
    {
        public Task<HealthCheckResult> CheckAsync(CancellationToken ct = default)
            => Task.FromResult(HealthCheckResult.Healthy());
    }
}
