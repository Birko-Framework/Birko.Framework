using Birko.Security.AzureKeyVault;
using FluentAssertions;
using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Birko.Security.AzureKeyVault.Tests;

public class AzureKeyVaultSecretProviderTests
{
    [Fact]
    public void Constructor_NullSettings_Throws()
    {
        var act = () => new AzureKeyVaultSecretProvider(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_EmptyVaultUri_Throws()
    {
        var act = () => new AzureKeyVaultSecretProvider(new AzureKeyVaultSettings());
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_ValidSettings_CreatesInstance()
    {
        var settings = new AzureKeyVaultSettings { VaultUri = "https://test.vault.azure.net/" };
        using var provider = new AzureKeyVaultSecretProvider(settings);
        provider.Should().NotBeNull();
    }

    [Fact]
    public void ImplementsISecretProvider()
    {
        var settings = new AzureKeyVaultSettings { VaultUri = "https://test.vault.azure.net/" };
        using var provider = new AzureKeyVaultSecretProvider(settings);
        provider.Should().BeAssignableTo<ISecretProvider>();
    }

    [Fact]
    public void ImplementsIDisposable()
    {
        var settings = new AzureKeyVaultSettings { VaultUri = "https://test.vault.azure.net/" };
        using var provider = new AzureKeyVaultSecretProvider(settings);
        provider.Should().BeAssignableTo<IDisposable>();
    }

    [Fact]
    public async Task GetSecretAsync_NullKey_Throws()
    {
        var settings = new AzureKeyVaultSettings { VaultUri = "https://test.vault.azure.net/" };
        using var provider = new AzureKeyVaultSecretProvider(settings);
        var act = () => provider.GetSecretAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task SetSecretAsync_NullKey_Throws()
    {
        var settings = new AzureKeyVaultSettings { VaultUri = "https://test.vault.azure.net/" };
        using var provider = new AzureKeyVaultSecretProvider(settings);
        var act = () => provider.SetSecretAsync(null!, "value");
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task DeleteSecretAsync_NullKey_Throws()
    {
        var settings = new AzureKeyVaultSettings { VaultUri = "https://test.vault.azure.net/" };
        using var provider = new AzureKeyVaultSecretProvider(settings);
        var act = () => provider.DeleteSecretAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task GetSecretAsync_WithoutCredentials_ThrowsOnAuth()
    {
        // Provider without TenantId/ClientId/ClientSecret will fail on token acquisition
        var settings = new AzureKeyVaultSettings { VaultUri = "https://test.vault.azure.net/" };
        var handler = new FakeHttpHandler(HttpStatusCode.OK, "{}");
        var httpClient = new HttpClient(handler);
        using var provider = new AzureKeyVaultSecretProvider(settings, httpClient);

        var act = () => provider.GetSecretAsync("my-secret");
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*TenantId*ClientId*ClientSecret*");
    }

    [Fact]
    public async Task GetSecretAsync_NotFound_ReturnsNull()
    {
        var handler = new SequentialHttpHandler(
            // First call: OAuth token
            (HttpStatusCode.OK, JsonSerializer.Serialize(new { access_token = "fake-token", expires_in = 3600 })),
            // Second call: secret not found
            (HttpStatusCode.NotFound, "{}")
        );
        var httpClient = new HttpClient(handler);
        var settings = new AzureKeyVaultSettings("https://test.vault.azure.net/", "tenant", "client", "secret");
        using var provider = new AzureKeyVaultSecretProvider(settings, httpClient);

        var result = await provider.GetSecretAsync("nonexistent");
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetSecretWithMetadataAsync_ParsesResponse()
    {
        var secretResponse = JsonSerializer.Serialize(new
        {
            value = "db-password-123",
            id = "https://test.vault.azure.net/secrets/db-pass/abc123",
            attributes = new { created = 1710500000L, updated = 1710500100L },
            tags = new { env = "staging" }
        });

        var handler = new SequentialHttpHandler(
            (HttpStatusCode.OK, JsonSerializer.Serialize(new { access_token = "token", expires_in = 3600 })),
            (HttpStatusCode.OK, secretResponse)
        );
        var httpClient = new HttpClient(handler);
        var settings = new AzureKeyVaultSettings("https://test.vault.azure.net/", "t", "c", "s");
        using var provider = new AzureKeyVaultSecretProvider(settings, httpClient);

        var result = await provider.GetSecretWithMetadataAsync("db-pass");

        result.Should().NotBeNull();
        result!.Key.Should().Be("db-pass");
        result.Value.Should().Be("db-password-123");
        result.Version.Should().Be("abc123");
        result.CreatedAt.Should().NotBeNull();
        result.Metadata.Should().ContainKey("env").WhoseValue.Should().Be("staging");
    }

    [Fact]
    public async Task ListSecretsAsync_ParsesResponse()
    {
        var listResponse = JsonSerializer.Serialize(new
        {
            value = new[]
            {
                new { id = "https://test.vault.azure.net/secrets/key1" },
                new { id = "https://test.vault.azure.net/secrets/key2" }
            }
        });

        var handler = new SequentialHttpHandler(
            (HttpStatusCode.OK, JsonSerializer.Serialize(new { access_token = "token", expires_in = 3600 })),
            (HttpStatusCode.OK, listResponse)
        );
        var httpClient = new HttpClient(handler);
        var settings = new AzureKeyVaultSettings("https://test.vault.azure.net/", "t", "c", "s");
        using var provider = new AzureKeyVaultSecretProvider(settings, httpClient);

        var result = await provider.ListSecretsAsync();
        result.Should().HaveCount(2);
        result.Should().Contain("key1");
        result.Should().Contain("key2");
    }

    #region Test Helpers

    private class FakeHttpHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _content;

        public FakeHttpHandler(HttpStatusCode statusCode, string content)
        {
            _statusCode = statusCode;
            _content = content;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_content, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }

    private class SequentialHttpHandler : HttpMessageHandler
    {
        private readonly (HttpStatusCode Status, string Content)[] _responses;
        private int _callIndex;

        public SequentialHttpHandler(params (HttpStatusCode, string)[] responses)
        {
            _responses = responses;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var index = Math.Min(_callIndex++, _responses.Length - 1);
            var (status, content) = _responses[index];
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }

    #endregion
}
