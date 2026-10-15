using Birko.Security.AzureKeyVault;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Birko.Security.AzureKeyVault.Tests;

/// <summary>
/// TASK-165: SH-M252 (a secret name reached the route unescaped) and SH-M260 (the listing never followed
/// <c>nextLink</c>).
/// </summary>
public class Task165NameAndPagingTests
{
    private const string Vault = "https://test.vault.azure.net/";
    private const string TokenOk = "{\"access_token\":\"tok\",\"expires_in\":3600}";

    private sealed class RoutingHandler : HttpMessageHandler
    {
        private readonly Func<Uri, string> _respond;
        public RoutingHandler(Func<Uri, string> respond) => _respond = respond;
        public List<Uri> Requests { get; } = new();
        public IEnumerable<Uri> VaultRequests => Requests.Where(u => !u.Host.Contains("login.microsoftonline.com"));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            var body = request.RequestUri!.Host.Contains("login.microsoftonline.com") ? TokenOk : _respond(request.RequestUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }

    private static AzureKeyVaultSecretProvider Provider(RoutingHandler h)
        => new(new AzureKeyVaultSettings(Vault, "t", "c", "s"), new HttpClient(h));

    // ── SH-M252 ──────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("db-pass/old-version")]
    [InlineData("../keys/x")]
    [InlineData("db?api-version=1.0")]
    [InlineData("db&x=1")]
    [InlineData("db#x")]
    [InlineData("db.pass")]
    [InlineData("")]
    public async Task A_name_outside_the_key_vault_alphabet_is_refused_and_nothing_is_sent(string key)
    {
        var h = new RoutingHandler(_ => "{}");
        using var provider = Provider(h);

        await provider.Invoking(p => p.DeleteSecretAsync(key)).Should().ThrowAsync<ArgumentException>();
        await provider.Invoking(p => p.GetSecretAsync(key)).Should().ThrowAsync<ArgumentException>();
        await provider.Invoking(p => p.SetSecretAsync(key, "v")).Should().ThrowAsync<ArgumentException>();

        h.VaultRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task A_name_of_127_characters_is_accepted_and_128_is_not()
    {
        var h = new RoutingHandler(_ => "{\"value\":\"v\"}");
        using var provider = Provider(h);

        (await provider.GetSecretAsync(new string('a', 127))).Should().Be("v");
        await provider.Invoking(p => p.GetSecretAsync(new string('a', 128))).Should().ThrowAsync<ArgumentException>();
    }

    // ── SH-M260 ──────────────────────────────────────────────────────────────────────────────────

    private static string Page(string? nextLink, params string[] names)
    {
        var items = string.Join(",", names.Select(n => $"{{\"id\":\"{Vault}secrets/{n}\"}}"));
        var next = nextLink == null ? "null" : $"\"{nextLink}\"";
        return $"{{\"value\":[{items}],\"nextLink\":{next}}}";
    }

    [Fact]
    public async Task Every_page_is_read()
    {
        var page2 = Vault + "secrets?api-version=7.4&$skiptoken=abc";
        var page3 = Vault + "secrets?api-version=7.4&$skiptoken=def";
        var h = new RoutingHandler(uri =>
            uri.Query.Contains("skiptoken=abc") ? Page(page3, "app--c", "zzz")
            : uri.Query.Contains("skiptoken=def") ? Page(null, "app--d")
            : Page(page2, "app--a", "app--b"));
        using var provider = Provider(h);

        (await provider.ListSecretsAsync("app")).Should().Equal("app--a", "app--b", "app--c", "app--d");
        h.VaultRequests.Should().HaveCount(3);
    }

    [Fact]
    public async Task A_next_link_to_another_host_is_not_followed()
    {
        var h = new RoutingHandler(_ => Page("https://attacker.example/secrets?x=1", "a"));
        using var provider = Provider(h);

        await provider.Invoking(p => p.ListSecretsAsync()).Should().ThrowAsync<InvalidOperationException>();
        h.Requests.Should().NotContain(u => u.Host == "attacker.example", "the request would carry the vault's access token");
    }

    [Fact]
    public async Task A_next_link_that_repeats_stops_the_listing()
    {
        var h = new RoutingHandler(_ => Page(Vault + "secrets?api-version=7.4", "a"));
        using var provider = Provider(h);

        await provider.Invoking(p => p.ListSecretsAsync()).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public void The_provider_declares_itself_flat()
    {
        using var provider = Provider(new RoutingHandler(_ => "{}"));
        ((ISecretProvider)provider).IsHierarchical.Should().BeFalse();
    }
}
