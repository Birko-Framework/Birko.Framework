using FluentAssertions;
using Xunit;
using Birko.Security.AzureKeyVault;

namespace Birko.Health.Azure.Tests.Azure;

public class AzureKeyVaultHealthCheckTests
{
    [Fact]
    public void Constructor_NullFactory_ThrowsArgumentNullException()
    {
        var act = () => new AzureKeyVaultHealthCheck((Func<AzureKeyVaultSecretProvider>)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullInstance_ThrowsArgumentNullException()
    {
        var act = () => new AzureKeyVaultHealthCheck((AzureKeyVaultSecretProvider)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_WithFactory_CreatesInstance()
    {
        var settings = new AzureKeyVaultSettings(
            "https://test.vault.azure.net", "tenant", "client", "secret");
        var provider = new AzureKeyVaultSecretProvider(settings);

        var check = new AzureKeyVaultHealthCheck(() => provider);

        check.Should().NotBeNull();
        provider.Dispose();
    }

    [Fact]
    public void Constructor_WithInstance_CreatesInstance()
    {
        var settings = new AzureKeyVaultSettings(
            "https://test.vault.azure.net", "tenant", "client", "secret");
        using var provider = new AzureKeyVaultSecretProvider(settings);

        var check = new AzureKeyVaultHealthCheck(provider);

        check.Should().NotBeNull();
    }

    [Fact]
    public async Task CheckAsync_WhenFactoryThrows_ReturnsUnhealthy()
    {
        var check = new AzureKeyVaultHealthCheck(() => throw new InvalidOperationException("vault unavailable"));

        var result = await check.CheckAsync();

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Exception.Should().BeOfType<InvalidOperationException>();
        result.Description.Should().Contain("vault unavailable");
    }

    [Fact]
    public async Task CheckAsync_WhenCancelled_PropagatesCancellation()
    {
        // CR-M191: cancellation must bubble so HealthCheckRunner applies its timeout-status logic.
        var settings = new AzureKeyVaultSettings(
            "https://test.vault.azure.net", "tenant", "client", "secret");
        using var provider = new AzureKeyVaultSecretProvider(settings);
        var check = new AzureKeyVaultHealthCheck(provider);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await check.Invoking(c => c.CheckAsync(cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }
}
