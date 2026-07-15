using System;
using System.Threading.Tasks;
using Birko.Health;
using Birko.Health.Checks;
using FluentAssertions;
using Xunit;

namespace Birko.Health.Tests;

public class SystemHealthCheckTests
{
    [Fact]
    public async Task DiskSpaceHealthCheck_CurrentDrive_ReturnsResult()
    {
        var check = new DiskSpaceHealthCheck(AppContext.BaseDirectory);

        var result = await check.CheckAsync();

        // Should be either Healthy or Degraded depending on actual disk space, but not crash
        result.Status.Should().NotBe(HealthStatus.Unhealthy);
        result.Data.Should().ContainKey("freeSpaceMb");
        result.Data.Should().ContainKey("freePercent");
    }

    [Fact]
    public void DiskSpaceHealthCheck_EmptyPath_ThrowsArgumentException()
    {
        var act = () => new DiskSpaceHealthCheck("");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task DiskSpaceHealthCheck_RootlessPath_ReturnsUnhealthy()
    {
        // CR-L261: a bare relative path has no root, so Path.GetPathRoot returns empty — report a clear
        // Unhealthy instead of letting new DriveInfo throw on a null.
        var check = new DiskSpaceHealthCheck("some-relative-folder");

        var result = await check.CheckAsync();

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("no root");
    }

    [Fact]
    public void DiskSpaceHealthCheck_CriticalNotBelowWarning_ThrowsArgumentException()
    {
        // CR-L262: for disk, critical must be < warning or the Degraded tier is unreachable.
        var act = () => new DiskSpaceHealthCheck("C:\\", warningThresholdMb: 256, criticalThresholdMb: 1024);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("criticalThresholdMb");
    }

    [Fact]
    public void MemoryHealthCheck_CriticalNotAboveWarning_ThrowsArgumentException()
    {
        // CR-L262: for memory, critical must be > warning or the Degraded tier is unreachable.
        var act = () => new MemoryHealthCheck(warningThresholdMb: 2048, criticalThresholdMb: 1024);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("criticalThresholdMb");
    }

    [Fact]
    public async Task MemoryHealthCheck_CurrentProcess_ReturnsHealthy()
    {
        var check = new MemoryHealthCheck(warningThresholdMb: 8192, criticalThresholdMb: 16384);

        var result = await check.CheckAsync();

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data.Should().ContainKey("workingSetMb");
        result.Data.Should().ContainKey("gcHeapMb");
        result.Data.Should().ContainKey("gen0Collections");
    }

    [Fact]
    public async Task MemoryHealthCheck_VeryLowThreshold_ReturnsDegradedOrUnhealthy()
    {
        // 1 MB threshold — any running process should exceed this
        var check = new MemoryHealthCheck(warningThresholdMb: 1, criticalThresholdMb: 2);

        var result = await check.CheckAsync();

        result.Status.Should().NotBe(HealthStatus.Healthy);
    }
}
