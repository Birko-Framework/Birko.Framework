using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Birko.Security.Vault;
using FluentAssertions;
using Xunit;

namespace Birko.Security.Vault.Tests;

/// <summary>
/// CR-M241: SetSecretAsync / DeleteSecretAsync / GetSecretPairsAsync success paths were untested. These
/// use a capturing handler to assert the outgoing method/URI/body across KV v1 and v2.
/// </summary>
public class VaultSecretProviderWriteTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _content;
        public HttpMethod? Method { get; private set; }
        public string? Uri { get; private set; }
        public string? Body { get; private set; }

        public CapturingHandler(HttpStatusCode status = HttpStatusCode.OK, string content = "{}")
        {
            _status = status;
            _content = content;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method;
            Uri = request.RequestUri?.ToString();
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(_status) { Content = new StringContent(_content, System.Text.Encoding.UTF8, "application/json") };
        }
    }

    private static VaultSecretProvider Provider(CapturingHandler handler, int kvVersion)
        => new(new VaultSettings { Address = "http://localhost:8200", Token = "test", KvVersion = kvVersion }, new HttpClient(handler));

    [Fact]
    public async Task SetSecretAsync_Kv2_PostsWrappedDataPayloadToDataPath()
    {
        var handler = new CapturingHandler();
        var provider = Provider(handler, 2);

        await provider.SetSecretAsync("myapp/db", "s3cret");

        handler.Method.Should().Be(HttpMethod.Post);
        handler.Uri.Should().Contain("/data/myapp/db");
        handler.Body.Should().Contain("\"data\"").And.Contain("s3cret");
    }

    [Fact]
    public async Task SetSecretAsync_Kv1_PostsFlatPayload()
    {
        var handler = new CapturingHandler();
        var provider = Provider(handler, 1);

        await provider.SetSecretAsync("myapp/db", "s3cret");

        handler.Method.Should().Be(HttpMethod.Post);
        handler.Uri.Should().NotContain("/data/");
        handler.Body.Should().Contain("s3cret");
    }

    [Fact]
    public async Task DeleteSecretAsync_Kv2_UsesMetadataPath()
    {
        var handler = new CapturingHandler();
        var provider = Provider(handler, 2);

        await provider.DeleteSecretAsync("myapp/db");

        handler.Method.Should().Be(HttpMethod.Delete);
        handler.Uri.Should().Contain("/metadata/myapp/db");
    }

    [Fact]
    public async Task DeleteSecretAsync_NotFound_IsTolerated()
    {
        var handler = new CapturingHandler(HttpStatusCode.NotFound);
        var provider = Provider(handler, 2);

        await provider.Invoking(p => p.DeleteSecretAsync("gone")).Should().NotThrowAsync();
    }

    [Fact]
    public async Task GetSecretPairsAsync_Kv2_ReturnsInnerData()
    {
        var body = JsonSerializer.Serialize(new { data = new { data = new { user = "admin", pass = "pw" } } });
        var handler = new CapturingHandler(HttpStatusCode.OK, body);
        var provider = Provider(handler, 2);

        var pairs = await provider.GetSecretPairsAsync("myapp");

        pairs.Should().NotBeNull();
        pairs!["user"].Should().Be("admin");
        pairs["pass"].Should().Be("pw");
    }
}
