using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.Health;
using FluentAssertions;
using Xunit;

namespace Birko.Health.Tests;

public class HealthCheckRunnerTests
{
    [Fact]
    public async Task RunAsync_NoChecks_ReturnsHealthyReport()
    {
        var runner = new HealthCheckRunner();

        var report = await runner.RunAsync();

        report.Status.Should().Be(HealthStatus.Healthy);
        report.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_SingleHealthyCheck_ReturnsHealthy()
    {
        var runner = new HealthCheckRunner();
        runner.Register("test", new LambdaCheck(() => HealthCheckResult.Healthy("OK")));

        var report = await runner.RunAsync();

        report.Status.Should().Be(HealthStatus.Healthy);
        report.Entries.Should().ContainKey("test");
        report.Entries["test"].Description.Should().Be("OK");
    }

    [Fact]
    public async Task RunAsync_MixedResults_ReturnsWorstStatus()
    {
        var runner = new HealthCheckRunner();
        runner.Register("healthy", new LambdaCheck(() => HealthCheckResult.Healthy()));
        runner.Register("degraded", new LambdaCheck(() => HealthCheckResult.Degraded("slow")));

        var report = await runner.RunAsync();

        report.Status.Should().Be(HealthStatus.Degraded);
        report.Entries.Should().HaveCount(2);
    }

    [Fact]
    public async Task RunAsync_CheckThrowsException_ReportsUnhealthy()
    {
        var runner = new HealthCheckRunner();
        runner.Register("failing", new LambdaCheck(() => throw new InvalidOperationException("boom")));

        var report = await runner.RunAsync();

        report.Status.Should().Be(HealthStatus.Unhealthy);
        report.Entries["failing"].Exception.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task RunAsync_CheckTimesOut_ReportsUnhealthy()
    {
        var runner = new HealthCheckRunner(defaultTimeout: TimeSpan.FromMilliseconds(50));
        runner.Register("slow", new LambdaCheck(async ct =>
        {
            await Task.Delay(5000, ct);
            return HealthCheckResult.Healthy();
        }));

        var report = await runner.RunAsync();

        report.Status.Should().Be(HealthStatus.Unhealthy);
        report.Entries["slow"].Description.Should().Contain("Timed out");
    }

    [Fact]
    public async Task RunAsync_TagFilter_OnlyRunsMatchingChecks()
    {
        var runner = new HealthCheckRunner();
        runner.Register("db", new LambdaCheck(() => HealthCheckResult.Healthy()), "db");
        runner.Register("cache", new LambdaCheck(() => HealthCheckResult.Healthy()), "cache");
        runner.Register("disk", new LambdaCheck(() => HealthCheckResult.Healthy()), "system");

        var report = await runner.RunAsync(tag: "db");

        report.Entries.Should().HaveCount(1);
        report.Entries.Should().ContainKey("db");
    }

    [Fact]
    public async Task RunAsync_SetsEntryDuration()
    {
        var runner = new HealthCheckRunner();
        runner.Register("test", new LambdaCheck(async ct =>
        {
            await Task.Delay(10, ct);
            return HealthCheckResult.Healthy();
        }));

        var report = await runner.RunAsync();

        report.Entries["test"].Duration.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task RunAsync_MultipleChecks_RunsConcurrently()
    {
        var runner = new HealthCheckRunner();
        var started = 0;

        for (int i = 0; i < 5; i++)
        {
            var name = $"check-{i}";
            runner.Register(name, new LambdaCheck(async ct =>
            {
                Interlocked.Increment(ref started);
                await Task.Delay(50, ct);
                return HealthCheckResult.Healthy();
            }));
        }

        var report = await runner.RunAsync();

        report.Entries.Should().HaveCount(5);
        // Total time should be much less than 5 * 50ms = 250ms if concurrent
        report.TotalDuration.Should().BeLessThan(TimeSpan.FromMilliseconds(200));
    }

    [Fact]
    public void Register_FluentChaining()
    {
        var runner = new HealthCheckRunner()
            .Register("a", new LambdaCheck(() => HealthCheckResult.Healthy()))
            .Register("b", () => new LambdaCheck(() => HealthCheckResult.Healthy()), "tag1");

        runner.Registrations.Should().HaveCount(2);
    }

    [Fact]
    public void Register_NullRegistration_ThrowsArgumentNullException()
    {
        var runner = new HealthCheckRunner();

        var act = () => runner.Register((HealthCheckRegistration)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private class LambdaCheck : IHealthCheck
    {
        private readonly Func<CancellationToken, Task<HealthCheckResult>> _func;

        public LambdaCheck(Func<HealthCheckResult> func)
        {
            _func = _ => Task.FromResult(func());
        }

        public LambdaCheck(Func<CancellationToken, Task<HealthCheckResult>> func)
        {
            _func = func;
        }

        public Task<HealthCheckResult> CheckAsync(CancellationToken ct = default) => _func(ct);
    }
}
