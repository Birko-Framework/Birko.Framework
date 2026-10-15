using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Birko.Security.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Birko.Security.Vault.Tests;

/// <summary>
/// TASK-165: SH-M251 (a Vault key reached the URL unescaped), SH-M258 (a KV v1 body without <c>data</c> threw) and
/// SH-M254 (the configuration walk read a flat provider's full names as relative ones).
/// </summary>
public class Task165VaultPathAndFlatProviderTests
{
    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly string _body;
        public RecordingHandler(string body) => _body = body;
        public List<(HttpMethod Method, string PathAndQuery)> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.Method, request.RequestUri!.PathAndQuery));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_body, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }

    private static VaultSecretProvider Provider(RecordingHandler handler, int kvVersion = 2)
        => new(new VaultSettings { Address = "http://vault.test:8200", Token = "t", KvVersion = kvVersion }, new HttpClient(handler));

    // ── SH-M251 ──────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("../../sys/policy/x")]
    [InlineData("app/../../sys/policy")]
    [InlineData("app/./db")]
    [InlineData("app//db")]
    [InlineData("/app")]
    [InlineData("")]
    public async Task A_key_that_would_address_another_path_is_refused_and_nothing_is_sent(string key)
    {
        var handler = new RecordingHandler("{}");
        using var provider = Provider(handler);

        await provider.Invoking(p => p.DeleteSecretAsync(key)).Should().ThrowAsync<ArgumentException>();
        await provider.Invoking(p => p.GetSecretAsync(key)).Should().ThrowAsync<ArgumentException>();
        await provider.Invoking(p => p.SetSecretAsync(key, "v")).Should().ThrowAsync<ArgumentException>();
        await provider.Invoking(p => p.ListSecretsAsync(key + "/x")).Should().ThrowAsync<ArgumentException>();

        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Query_and_fragment_characters_in_a_key_are_data()
    {
        var handler = new RecordingHandler("{\"data\":{\"data\":{\"value\":\"v\"}}}");
        using var provider = Provider(handler);

        await provider.GetSecretAsync("app/a?list=true#x");
        await provider.DeleteSecretAsync("app/a?b");

        handler.Requests.Select(r => r.PathAndQuery).Should().Equal(
            "/v1/secret/data/app/a%3Flist%3Dtrue%23x",
            "/v1/secret/metadata/app/a%3Fb");
    }

    [Fact]
    public async Task A_listed_folder_name_with_its_trailing_slash_is_listed()
    {
        var handler = new RecordingHandler("{\"data\":{\"keys\":[\"db\"]}}");
        using var provider = Provider(handler);

        (await provider.ListSecretsAsync("app/")).Should().Equal("db");
        (await provider.ListSecretsAsync()).Should().Equal("db");

        handler.Requests.Select(r => r.PathAndQuery).Should().Equal(
            "/v1/secret/metadata/app?list=true",
            "/v1/secret/metadata?list=true");
    }

    // ── SH-M258 ──────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"data\":null}")]
    [InlineData("{\"data\":\"text\"}")]
    public async Task A_kv1_body_without_data_reads_as_an_empty_value(string body)
    {
        using var provider = Provider(new RecordingHandler(body), kvVersion: 1);

        var result = await provider.GetSecretWithMetadataAsync("app/db");

        result.Should().NotBeNull();
        result!.Value.Should().BeEmpty();
    }

    // ── SH-M254 ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>A provider with flat names and a prefix listing, as Azure Key Vault has.</summary>
    private sealed class FlatProvider : ISecretProvider
    {
        private readonly Dictionary<string, string> _secrets;
        public FlatProvider(Dictionary<string, string> secrets) => _secrets = secrets;
        public int Listings { get; private set; }
        public bool IsHierarchical => false;

        public Task<string?> GetSecretAsync(string key, CancellationToken ct = default)
            => Task.FromResult(_secrets.TryGetValue(key, out var v) ? v : null);
        public Task<SecretResult?> GetSecretWithMetadataAsync(string key, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task SetSecretAsync(string key, string value, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteSecretAsync(string key, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<string>> ListSecretsAsync(string? path = null, CancellationToken ct = default)
        {
            Listings++;
            return Task.FromResult<IReadOnlyList<string>>(_secrets.Keys
                .Where(k => string.IsNullOrEmpty(path) || k.StartsWith(path, StringComparison.OrdinalIgnoreCase)).ToList());
        }
    }

    private static Dictionary<string, string?> Load(ISecretProvider provider, string path)
    {
        var configProvider = new SecretConfigurationProvider(provider, path, diagnostics: _ => { });
        var root = new ConfigurationRoot(new[] { configProvider }); // the root calls Load()
        return root.AsEnumerable().Where(kv => kv.Value != null).ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    [Fact]
    public void A_flat_provider_loads_the_names_under_the_path_once()
    {
        var provider = new FlatProvider(new Dictionary<string, string>
        {
            ["app"] = "root",
            ["app--db--password"] = "pw",
            ["app--name"] = "n",
            ["apple"] = "not under app",
            ["other--x"] = "not under app",
        });

        var config = Load(provider, "app");

        config.Should().BeEquivalentTo(new Dictionary<string, string?>
        {
            ["value"] = "root",
            ["db:password:value"] = "pw",
            ["name:value"] = "n",
        });
        provider.Listings.Should().Be(1, "a flat vault is listed once, not once per level");
    }

    [Fact]
    public void A_flat_provider_with_no_path_loads_every_name()
    {
        var provider = new FlatProvider(new Dictionary<string, string> { ["db--password"] = "pw", ["name"] = "n" });

        Load(provider, "").Should().BeEquivalentTo(new Dictionary<string, string?>
        {
            ["db:password:value"] = "pw",
            ["name:value"] = "n",
        });
    }
}
