using FluentAssertions;
using Xunit;
using Birko.Storage.AzureBlob;

namespace Birko.Health.Azure.Tests.Azure;

public class AzureBlobHealthCheckTests
{
    [Fact]
    public void Constructor_NullFactory_ThrowsArgumentNullException()
    {
        var act = () => new AzureBlobHealthCheck((Func<AzureBlobStorage>)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullInstance_ThrowsArgumentNullException()
    {
        var act = () => new AzureBlobHealthCheck((AzureBlobStorage)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_WithFactory_CreatesInstance()
    {
        var settings = new AzureBlobSettings(
            "https://test.blob.core.windows.net", "container",
            "tenant", "client", "secret");
        var storage = new AzureBlobStorage(settings, new Birko.Time.SystemDateTimeProvider());

        var check = new AzureBlobHealthCheck(() => storage);

        check.Should().NotBeNull();
        storage.Dispose();
    }

    [Fact]
    public void Constructor_WithInstance_CreatesInstance()
    {
        var settings = new AzureBlobSettings(
            "https://test.blob.core.windows.net", "container",
            "tenant", "client", "secret");
        using var storage = new AzureBlobStorage(settings, new Birko.Time.SystemDateTimeProvider());

        var check = new AzureBlobHealthCheck(storage);

        check.Should().NotBeNull();
    }

    [Fact]
    public async Task CheckAsync_WhenFactoryThrows_ReturnsUnhealthy()
    {
        var check = new AzureBlobHealthCheck(() => throw new InvalidOperationException("connection failed"));

        var result = await check.CheckAsync();

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Exception.Should().BeOfType<InvalidOperationException>();
        result.Description.Should().Contain("connection failed");
    }

    [Fact]
    public async Task CheckAsync_WhenCancelled_ReturnsUnhealthy()
    {
        var settings = new AzureBlobSettings(
            "https://test.blob.core.windows.net", "container",
            "tenant", "client", "secret");
        using var storage = new AzureBlobStorage(settings, new Birko.Time.SystemDateTimeProvider());
        var check = new AzureBlobHealthCheck(storage);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await check.CheckAsync(cts.Token);

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }
}
