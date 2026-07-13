using Birko.Security.AzureKeyVault;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Birko.Security.AzureKeyVault.Tests;

/// <summary>
/// CR-M237: SetSecretAsync/DeleteSecretAsync success paths, the Delete-NotFound-is-OK branch, non-404
/// error propagation, and token caching/reuse were untested. A recording handler drives the sequence
/// and counts token-endpoint hits.
/// </summary>
public class AzureKeyVaultWriteAndTokenTests
{
    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly (HttpStatusCode Status, string Content)[] _responses;
        private int _i;
        public List<string> Requests { get; } = new();

        public RecordingHandler(params (HttpStatusCode, string)[] responses) => _responses = responses;

        public int TokenRequestCount => Requests.Count(u => u.Contains("login.microsoftonline.com"));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.ToString());
            var (status, content) = _responses[Math.Min(_i++, _responses.Length - 1)];
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }

    private const string TokenOk = "{\"access_token\":\"tok\",\"expires_in\":3600}";
    private static AzureKeyVaultSecretProvider Provider(RecordingHandler h)
        => new(new AzureKeyVaultSettings("https://test.vault.azure.net/", "t", "c", "s"), new HttpClient(h));

    [Fact]
    public async Task SetSecretAsync_Success_PutsSecret()
    {
        var h = new RecordingHandler((HttpStatusCode.OK, TokenOk), (HttpStatusCode.OK, "{\"value\":\"v\"}"));
        using var provider = Provider(h);

        await provider.Invoking(p => p.SetSecretAsync("db-pass", "v")).Should().NotThrowAsync();
        h.Requests.Last().Should().Contain("/secrets/db-pass");
    }

    [Fact]
    public async Task DeleteSecretAsync_Success_And_NotFound_AreTolerated()
    {
        var ok = new RecordingHandler((HttpStatusCode.OK, TokenOk), (HttpStatusCode.OK, "{}"));
        using (var provider = Provider(ok))
            await provider.Invoking(p => p.DeleteSecretAsync("k")).Should().NotThrowAsync();

        var notFound = new RecordingHandler((HttpStatusCode.OK, TokenOk), (HttpStatusCode.NotFound, "{}"));
        using (var provider = Provider(notFound))
            await provider.Invoking(p => p.DeleteSecretAsync("gone")).Should().NotThrowAsync("NotFound on delete is tolerated");
    }

    [Fact]
    public async Task SetSecretAsync_ServerError_Throws()
    {
        var h = new RecordingHandler((HttpStatusCode.OK, TokenOk), (HttpStatusCode.InternalServerError, "{}"));
        using var provider = Provider(h);

        await provider.Invoking(p => p.SetSecretAsync("k", "v")).Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task AccessToken_IsCachedAndReusedAcrossCalls()
    {
        var secret = "{\"value\":\"v\",\"id\":\"https://test.vault.azure.net/secrets/k/ver\"}";
        var h = new RecordingHandler(
            (HttpStatusCode.OK, TokenOk),   // token (once)
            (HttpStatusCode.OK, secret),    // first GetSecret
            (HttpStatusCode.OK, secret));   // second GetSecret (should reuse token)
        using var provider = Provider(h);

        await provider.GetSecretAsync("k");
        await provider.GetSecretAsync("k");

        h.TokenRequestCount.Should().Be(1, "the access token must be cached and reused (CR-M237)");
    }
}
