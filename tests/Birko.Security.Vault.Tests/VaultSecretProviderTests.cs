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
        result.CreatedAt.Should().NotBeNull();
        // CR-H140: KV2 version metadata exposes only created_time, so UpdatedAt must NOT be a
        // copy of CreatedAt — it is null when no genuine update timestamp is present.
        result.UpdatedAt.Should().BeNull();
    }

    [Fact]
    public async Task GetSecretWithMetadataAsync_Kv2_UpdatedTimePopulatesUpdatedAt()
    {
        var vaultResponse = JsonSerializer.Serialize(new
        {
            data = new
            {
                data = new { value = "v" },
                metadata = new
                {
                    version = 2,
                    created_time = "2026-03-15T10:00:00Z",
                    updated_time = "2026-04-20T08:30:00Z"
                }
            }
        });

        var handler = new FakeHttpHandler(HttpStatusCode.OK, vaultResponse);
        var httpClient = new HttpClient(handler);
        var settings = new VaultSettings { Address = "http://localhost:8200", Token = "test", KvVersion = 2 };
        using var provider = new VaultSecretProvider(settings, httpClient);

        var result = await provider.GetSecretWithMetadataAsync("myapp/db");

        result.Should().NotBeNull();
        result!.CreatedAt.Should().NotBeNull();
        result.UpdatedAt.Should().NotBeNull();
        result.UpdatedAt.Should().NotBe(result.CreatedAt, "UpdatedAt must reflect updated_time, not created_time");
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

    [Fact]
    public async Task GetSecretPairsAsync_MalformedBody_ReturnsNull_NoThrow()
    {
        // CR-M240: a 200 with an unexpected body (no "data" node) must return null gracefully,
        // not surface a raw KeyNotFoundException from GetProperty.
        var handler = new FakeHttpHandler(HttpStatusCode.OK, "{\"unexpected\":true}");
        var httpClient = new HttpClient(handler);
        var settings = new VaultSettings { Address = "http://localhost:8200", Token = "test", KvVersion = 2 };
        using var provider = new VaultSecretProvider(settings, httpClient);

        var result = await provider.Invoking(p => p.GetSecretPairsAsync("myapp"))
            .Should().NotThrowAsync();
        result.Subject.Should().BeNull();
    }

    [Fact]
    public async Task GetSecretPairsAsync_Kv2_MissingInnerData_ReturnsNull()
    {
        // KV2 outer "data" present but inner "data" absent → null, not KeyNotFoundException.
        var handler = new FakeHttpHandler(HttpStatusCode.OK, "{\"data\":{\"metadata\":{}}}");
        var httpClient = new HttpClient(handler);
        var settings = new VaultSettings { Address = "http://localhost:8200", Token = "test", KvVersion = 2 };
        using var provider = new VaultSecretProvider(settings, httpClient);

        (await provider.GetSecretPairsAsync("myapp")).Should().BeNull();
    }

    // ── Injected-HttpClient hygiene (CR-L354) ──

    [Fact]
    public void Constructor_InjectedHttpClient_NotMutated()
    {
        // CR-L354: the provider must not overwrite an injected/shared client's BaseAddress, Timeout, or
        // DefaultRequestHeaders — those belong to the caller (e.g. IHttpClientFactory).
        var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(7) };
        var settings = new VaultSettings { Address = "http://localhost:8200", Token = "test", Namespace = "ns", TimeoutSeconds = 30 };

        using var provider = new VaultSecretProvider(settings, httpClient);

        httpClient.BaseAddress.Should().BeNull("an injected client's BaseAddress must be left untouched");
        httpClient.Timeout.Should().Be(TimeSpan.FromSeconds(7), "an injected client's Timeout must be left untouched");
        httpClient.DefaultRequestHeaders.Contains("X-Vault-Token").Should().BeFalse();
        httpClient.DefaultRequestHeaders.Contains("X-Vault-Namespace").Should().BeFalse();
    }

    [Fact]
    public void Constructor_TwoProvidersOverOneClient_DoesNotThrow()
    {
        // Previously each provider did DefaultRequestHeaders.Add(...), so the second construction over a shared
        // client threw on the duplicate header. With per-request headers this is safe.
        var httpClient = new HttpClient();
        var settings = new VaultSettings { Address = "http://localhost:8200", Token = "test", Namespace = "ns" };

        var act = () =>
        {
            using var p1 = new VaultSecretProvider(settings, httpClient);
            using var p2 = new VaultSecretProvider(settings, httpClient);
        };

        act.Should().NotThrow();
    }

    [Fact]
    public async Task Requests_CarryTokenAndNamespaceHeaders_AtAbsoluteUri()
    {
        // CR-L354: token/namespace travel as per-request headers and the request targets an absolute URI
        // derived from the settings address (no reliance on a mutated client BaseAddress).
        var handler = new CapturingHandler(HttpStatusCode.OK, "{}");
        var httpClient = new HttpClient(handler);
        var settings = new VaultSettings { Address = "http://vault.local:8200", Token = "hvs.abc", Namespace = "team-a" };
        using var provider = new VaultSecretProvider(settings, httpClient);

        await provider.IsHealthyAsync();

        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.RequestUri!.AbsoluteUri.Should().Be("http://vault.local:8200/v1/sys/health");
        handler.LastRequest.Headers.GetValues("X-Vault-Token").Should().ContainSingle().Which.Should().Be("hvs.abc");
        handler.LastRequest.Headers.GetValues("X-Vault-Namespace").Should().ContainSingle().Which.Should().Be("team-a");
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

    private class CapturingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _content;
        public HttpRequestMessage? LastRequest { get; private set; }

        public CapturingHandler(HttpStatusCode statusCode, string content)
        {
            _statusCode = statusCode;
            _content = content;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_content, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }

    #endregion
}
