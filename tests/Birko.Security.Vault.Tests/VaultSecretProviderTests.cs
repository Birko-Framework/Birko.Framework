using Birko.Security.Vault;
using FluentAssertions;
using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Birko.Security.Vault.Tests;

public class VaultSecretProviderTests
{
    [Fact]
    public void Constructor_NullSettings_Throws()
    {
        var act = () => new VaultSecretProvider(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_ValidSettings_CreatesInstance()
    {
        var settings = new VaultSettings { Address = "http://localhost:8200", Token = "test" };
        using var provider = new VaultSecretProvider(settings);
        provider.Should().NotBeNull();
    }

    [Fact]
    public void ImplementsISecretProvider()
    {
        var settings = new VaultSettings();
        using var provider = new VaultSecretProvider(settings);
        provider.Should().BeAssignableTo<ISecretProvider>();
    }

    [Fact]
    public void ImplementsIDisposable()
    {
        var settings = new VaultSettings();
        using var provider = new VaultSecretProvider(settings);
        provider.Should().BeAssignableTo<IDisposable>();
    }

    [Fact]
    public async Task GetSecretAsync_NullKey_Throws()
    {
        var settings = new VaultSettings();
        using var provider = new VaultSecretProvider(settings);
        var act = () => provider.GetSecretAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task SetSecretAsync_NullKey_Throws()
    {
        var settings = new VaultSettings();
        using var provider = new VaultSecretProvider(settings);
        var act = () => provider.SetSecretAsync(null!, "value");
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task SetSecretAsync_NullValue_Throws()
    {
        var settings = new VaultSettings();
        using var provider = new VaultSecretProvider(settings);
        var act = () => provider.SetSecretAsync("key", null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task DeleteSecretAsync_NullKey_Throws()
    {
        var settings = new VaultSettings();
        using var provider = new VaultSecretProvider(settings);
        var act = () => provider.DeleteSecretAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task GetSecretAsync_NotFound_ReturnsNull()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.NotFound, "{}");
        var httpClient = new HttpClient(handler);
        var settings = new VaultSettings { Address = "http://localhost:8200", Token = "test" };
        using var provider = new VaultSecretProvider(settings, httpClient);

        var result = await provider.GetSecretAsync("nonexistent");
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetSecretWithMetadataAsync_Kv2_ParsesResponse()
    {
        var vaultResponse = JsonSerializer.Serialize(new
        {
            data = new
            {
                data = new { value = "my-secret-value" },
                metadata = new
                {
                    version = 3,
                    created_time = "2026-03-15T10:00:00Z",
                    custom_metadata = new { env = "prod" }
                }
            }
        });

        var handler = new FakeHttpHandler(HttpStatusCode.OK, vaultResponse);
        var httpClient = new HttpClient(handler);
        var settings = new VaultSettings { Address = "http://localhost:8200", Token = "test", KvVersion = 2 };
        using var provider = new VaultSecretProvider(settings, httpClient);

        var result = await provider.GetSecretWithMetadataAsync("myapp/db");

        result.Should().NotBeNull();
        result!.Key.Should().Be("myapp/db");
        result.Value.Should().Be("my-secret-value");
        result.Version.Should().Be("3");
        result.Metadata.Should().ContainKey("env").WhoseValue.Should().Be("prod");
    }

    [Fact]
    public async Task GetSecretAsync_Kv1_ParsesResponse()
    {
        var vaultResponse = JsonSerializer.Serialize(new
        {
            data = new { value = "kv1-secret" }
        });

        var handler = new FakeHttpHandler(HttpStatusCode.OK, vaultResponse);
        var httpClient = new HttpClient(handler);
        var settings = new VaultSettings { Address = "http://localhost:8200", Token = "test", KvVersion = 1 };
        using var provider = new VaultSecretProvider(settings, httpClient);

        var result = await provider.GetSecretAsync("simple-key");
        result.Should().Be("kv1-secret");
    }

    [Fact]
    public async Task ListSecretsAsync_NotFound_ReturnsEmpty()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.NotFound, "{}");
        var httpClient = new HttpClient(handler);
        var settings = new VaultSettings { Address = "http://localhost:8200", Token = "test" };
        using var provider = new VaultSecretProvider(settings, httpClient);

        var result = await provider.ListSecretsAsync();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ListSecretsAsync_ParsesKeys()
    {
        var response = JsonSerializer.Serialize(new
        {
            data = new { keys = new[] { "key1", "key2", "key3/" } }
        });

        var handler = new FakeHttpHandler(HttpStatusCode.OK, response);
        var httpClient = new HttpClient(handler);
        var settings = new VaultSettings { Address = "http://localhost:8200", Token = "test" };
        using var provider = new VaultSecretProvider(settings, httpClient);

        var result = await provider.ListSecretsAsync();
        result.Should().HaveCount(3);
        result.Should().Contain("key1");
    }

    [Fact]
    public async Task IsHealthyAsync_ReturnsTrue_On200()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK, "{}");
        var httpClient = new HttpClient(handler);
        var settings = new VaultSettings { Address = "http://localhost:8200", Token = "test" };
        using var provider = new VaultSecretProvider(settings, httpClient);

        var healthy = await provider.IsHealthyAsync();
        healthy.Should().BeTrue();
    }

    [Fact]
    public async Task IsHealthyAsync_ReturnsFalse_OnError()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.ServiceUnavailable, "{}");
        var httpClient = new HttpClient(handler);
        var settings = new VaultSettings { Address = "http://localhost:8200", Token = "test" };
        using var provider = new VaultSecretProvider(settings, httpClient);

        var healthy = await provider.IsHealthyAsync();
        healthy.Should().BeFalse();
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

    #endregion
}
