using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.Health;
using FluentAssertions;
using Xunit;

namespace Birko.Health.Azure.Tests.Azure;

/// <summary>
/// CR-L264/L265: the shared MeasureAsync helper carries the timing/threshold/result logic that used to be
/// duplicated in both Azure checks. These exercise the Healthy and Degraded branches directly (the >2s
/// slow path was previously untestable without a live Azure dependency), plus the failure and
/// cancellation-rethrow paths, without any Azure client.
/// </summary>
public class AzureHealthCheckHelperTests
{
    [Fact]
    public async Task MeasureAsync_FastProbe_ReturnsHealthyWithLatency()
    {
        var result = await AzureHealthCheckHelper.MeasureAsync(
            "Test Service",
            _ => Task.CompletedTask,
            CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("Test Service OK");
        result.Data.Should().ContainKey("latencyMs");
    }

    [Fact]
    public async Task MeasureAsync_ProbeSlowerThanThreshold_ReturnsDegraded()
    {
        // A real (small) awaited delay with a zero slow-threshold deterministically trips the Degraded
        // branch without waiting the production 2s.
        var result = await AzureHealthCheckHelper.MeasureAsync(
            "Test Service",
            async _ => await Task.Delay(20),
            CancellationToken.None,
            slowThreshold: TimeSpan.Zero);

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("Test Service responding slowly");
        result.Data.Should().ContainKey("latencyMs");
    }

    [Fact]
    public async Task MeasureAsync_ProbeThrows_ReturnsUnhealthy()
    {
        var result = await AzureHealthCheckHelper.MeasureAsync(
            "Test Service",
            _ => throw new InvalidOperationException("boom"),
            CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("Test Service failed");
        result.Exception.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task MeasureAsync_ProbeCancelled_RethrowsOperationCanceled()
    {
        // CR-M191: cancellation must bubble so the runner's timeout handling applies, not be masked.
        var act = async () => await AzureHealthCheckHelper.MeasureAsync(
            "Test Service",
            _ => throw new OperationCanceledException(),
            CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
